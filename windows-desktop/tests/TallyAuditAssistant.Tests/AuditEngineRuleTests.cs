using Dapper;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;
using TallyAuditAssistant.Engine.Rules;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class AuditEngineRuleTests : IAsyncLifetime
{
    private readonly string _testDbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly DatabaseInitializer _initializer;
    private readonly string _companyId = "COMP-001";

    public AuditEngineRuleTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"audit_test_{Guid.NewGuid():N}.db");
        _factory = new SqliteConnectionFactory(_testDbPath);
        _initializer = new DatabaseInitializer(_factory, Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseInitializer>.Instance, _testDbPath);
    }

    public async Task InitializeAsync()
    {
        await _initializer.InitializeAsync();
        using var conn = await _factory.CreateConnectionAsync();

        // Seed basic company
        await conn.ExecuteAsync(@"
            INSERT INTO Companies (Id, TallyCompanyName, BooksFromDate, LastAlterId)
            VALUES (@CompanyId, 'Apex Industrial Solutions Pvt Ltd', '2025-04-01', 100);
        ", new { CompanyId = _companyId });

        // Seed basic chart of accounts
        await conn.ExecuteAsync(@"
            INSERT INTO Ledgers (Id, CompanyId, Name, ParentGroup, GSTIN, PAN, ClosingBalance, TaxType, HsnCode, GstRate)
            VALUES 
            ('L1', @Comp, 'Main Cash Account', 'Cash-in-Hand', NULL, NULL, -15000.00, NULL, NULL, 0),
            ('L2', @Comp, 'HDFC Bank Account', 'Bank Accounts', NULL, 'AAAAB1111C', 250000.00, NULL, NULL, 0),
            ('L3', @Comp, 'Suspense Clearance Account', 'Suspense A/c', NULL, NULL, 48000.00, NULL, NULL, 0),
            ('L4', @Comp, 'Acme Steel Corp', 'Sundry Creditors', '', 'AABCA2222P', -180000.00, 'TDS', NULL, 0),
            ('L5', @Comp, 'Unregistered Contractor', 'Sundry Creditors', NULL, '', -85000.00, 'TDS', NULL, 0),
            ('L6', @Comp, 'Fabrication Sales Head', 'Sales Accounts', NULL, NULL, 500000.00, 'GST', '', 18.0),
            ('L7', @Comp, 'Capital Equity Fund', 'Capital Account', NULL, NULL, 1000000.00, NULL, NULL, 0),
            ('L8', @Comp, 'Office General Expense', 'Indirect Expenses', NULL, NULL, 50000.00, NULL, NULL, 0);
        ", new { Comp = _companyId });

        // Seed test vouchers for all rule triggers
        await conn.ExecuteAsync(@"
            INSERT INTO Vouchers (Id, CompanyId, VoucherTypeId, VoucherTypeName, VoucherNumber, ReferenceNumber, VoucherDate, TotalAmount, PartyLedgerName, Narration, AlterId)
            VALUES 
            -- 1. Duplicate Voucher Number
            ('V1', @Comp, 'Sales', 'Sales', 'INV-101', 'REF-1', '2025-05-10', 45000, 'Acme Steel Corp', 'Sale of goods', 10),
            ('V2', @Comp, 'Sales', 'Sales', 'INV-101', 'REF-2', '2025-05-12', 45000, 'Acme Steel Corp', 'Sale of goods', 11),

            -- 2. Duplicate Invoice Reference
            ('V3', @Comp, 'Purchase', 'Purchase', 'PUR-01', 'BILL-999', '2025-06-01', 80000, 'Acme Steel Corp', 'Purchase of metal', 12),
            ('V4', @Comp, 'Purchase', 'Purchase', 'PUR-02', 'BILL-999', '2025-06-05', 80000, 'Acme Steel Corp', 'Purchase of metal', 13),

            -- 3. Missing Narration on large payment
            ('V5', @Comp, 'Payment', 'Payment', 'PMT-100', 'REF-P1', '2025-06-10', 75000, 'Acme Steel Corp', '', 14),

            -- 4. Missing Party Info on commercial voucher
            ('V6', @Comp, 'Sales', 'Sales', 'INV-102', 'REF-3', '2025-06-15', 60000, '', 'Cash sales over counter', 15),

            -- 6. Suspense Entry
            ('V7', @Comp, 'Journal', 'Journal', 'JRN-01', NULL, '2025-06-20', 25000, NULL, 'Suspense adjustment', 16),

            -- 7. Unusual Journal (Bank in Journal)
            ('V8', @Comp, 'Journal', 'Journal', 'JRN-02', NULL, '2025-06-22', 15000, NULL, 'Bank movement in journal', 17),

            -- 8. Large Manual Journal
            ('V9', @Comp, 'Journal', 'Journal', 'JRN-03', NULL, '2025-06-25', 1200000, NULL, 'High value asset transfer', 18),

            -- 9. Backdated Transaction (before BooksFromDate 2025-04-01)
            ('V10', @Comp, 'Purchase', 'Purchase', 'PUR-00', 'OLD-1', '2025-02-15', 30000, 'Acme Steel Corp', 'Late voucher entry', 19),

            -- 10. Year-End Adjustment (31-March)
            ('V11', @Comp, 'Journal', 'Journal', 'JRN-99', NULL, '2026-03-31', 450000, NULL, 'Closing provision adjustment', 20),

            -- 11. Numbering Gap (1, 2, then skips to 5)
            ('V12', @Comp, 'Contra', 'Contra', '1', NULL, '2025-07-01', 5000, NULL, 'Cash to bank', 21),
            ('V13', @Comp, 'Contra', 'Contra', '2', NULL, '2025-07-02', 5000, NULL, 'Cash to bank', 22),
            ('V14', @Comp, 'Contra', 'Contra', '5', NULL, '2025-07-05', 5000, NULL, 'Cash to bank', 23),

            -- 12. Statistical Outlier Amount (average is ~5000)
            ('V15', @Comp, 'Contra', 'Contra', '6', NULL, '2025-07-06', 450000, NULL, 'Unusually huge contra transfer', 24),

            -- 13. Round Number Payment Pattern
            ('V16', @Comp, 'Payment', 'Payment', 'PMT-101', 'REF-P2', '2025-07-10', 100000.00, 'Acme Steel Corp', 'Round lump sum payment', 25),

            -- 14. Unusual Ledger Combination (Capital vs Expense)
            ('V17', @Comp, 'Journal', 'Journal', 'JRN-04', NULL, '2025-07-15', 50000, NULL, 'Direct capital adjustment', 26),

            -- 15. Delayed Reversal Anomaly (reverses after 120 days)
            ('V18', @Comp, 'Sales', 'Sales', 'INV-50', 'REF-50', '2025-04-05', 90000, 'Acme Steel Corp', 'Original sale', 27),
            ('V19', @Comp, 'Credit Note', 'Credit Note', 'CN-01', 'REF-50', '2025-08-25', 90000, 'Acme Steel Corp', 'Late credit note reversal', 28),

            -- 16. Credit/Debit Note Missing Reference
            ('V20', @Comp, 'Debit Note', 'Debit Note', 'DN-01', '', '2025-09-01', 25000, 'Acme Steel Corp', 'Debit note without invoice link', 29),

            -- 17. Missing GSTIN on B2B transaction > 50,000
            ('V21', @Comp, 'Purchase', 'Purchase', 'PUR-03', 'REF-P3', '2025-09-05', 180000, 'Acme Steel Corp', 'High value B2B purchase without GSTIN', 30);
        ", new { Comp = _companyId });

        // Seed voucher line entries
        await conn.ExecuteAsync(@"
            INSERT INTO VoucherEntries (Id, VoucherId, LedgerName, Amount, IsDebit)
            VALUES 
            ('E1', 'V7', 'Suspense Clearance Account', 25000, 1),
            ('E2', 'V7', 'HDFC Bank Account', -25000, 0),
            ('E3', 'V8', 'HDFC Bank Account', 15000, 1),
            ('E4', 'V8', 'Acme Steel Corp', -15000, 0),
            ('E5', 'V17', 'Capital Equity Fund', -50000, 0),
            ('E6', 'V17', 'Office General Expense', 50000, 1),
            ('E7', 'V21', 'Unregistered Contractor', -85000, 0),
            ('E8', 'V21', 'Office General Expense', 85000, 1);
        ");
    }

    public Task DisposeAsync()
    {
        try
        {
            if (File.Exists(_testDbPath)) File.Delete(_testDbPath);
        }
        catch { }
        return Task.CompletedTask;
    }

    private AuditExecutionContext CreateContext() => new(_companyId, new DateTime(2025, 4, 1), new DateTime(2026, 3, 31));

    [Fact]
    public async Task Rule01_DuplicateVoucher_Flags_Matching_Vouchers()
    {
        var rule = new DuplicateVoucherRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.VoucherNumber == "INV-101");
        Assert.StartsWith("Flagged because", results[0].Explanation);
    }

    [Fact]
    public async Task Rule02_DuplicateInvoiceNumber_Flags_Matching_References()
    {
        var rule = new DuplicateInvoiceNumberRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.VoucherNumber == "BILL-999");
        Assert.StartsWith("Flagged because", results[0].Explanation);
    }

    [Fact]
    public async Task Rule03_MissingNarration_Flags_Empty_Narration_Above_Threshold()
    {
        var rule = new MissingNarrationRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.VoucherNumber == "PMT-100");
    }

    [Fact]
    public async Task Rule04_MissingPartyInfo_Flags_Anonymous_Commercial_Vouchers()
    {
        var rule = new MissingPartyInfoRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.VoucherNumber == "INV-102");
    }

    [Fact]
    public async Task Rule05_NegativeLedgerBalance_Flags_Negative_Cash()
    {
        var rule = new NegativeLedgerBalanceRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Explanation.Contains("Main Cash Account"));
    }

    [Fact]
    public async Task Rule06_SuspenseLedgerActivity_Flags_Direct_Suspense_Postings()
    {
        var rule = new SuspenseLedgerActivityRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.VoucherNumber == "JRN-01");
    }

    [Fact]
    public async Task Rule07_UnusualJournalEntry_Flags_Bank_In_Journal()
    {
        var rule = new UnusualJournalEntryRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.VoucherNumber == "JRN-02");
    }

    [Fact]
    public async Task Rule08_LargeManualJournal_Flags_High_Value_Adjustments()
    {
        var rule = new LargeManualJournalRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.VoucherNumber == "JRN-03");
    }

    [Fact]
    public async Task Rule09_BackdatedTransaction_Flags_Pre_Commencement_Date()
    {
        var rule = new BackdatedTransactionRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.VoucherNumber == "PUR-00");
    }

    [Fact]
    public async Task Rule10_YearEndAdjustment_Flags_March31_Large_Postings()
    {
        var rule = new YearEndAdjustmentRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.VoucherNumber == "JRN-99");
    }

    [Fact]
    public async Task Rule11_VoucherNumberingGap_Flags_Missing_Sequence_Numbers()
    {
        var rule = new VoucherNumberingGapRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Explanation.Contains("between voucher number 2 and 5"));
    }

    [Fact]
    public async Task Rule12_UnusualTransactionAmount_Flags_Outlier_Amounts()
    {
        var rule = new UnusualTransactionAmountRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.VoucherNumber == "6");
    }

    [Fact]
    public async Task Rule13_RoundNumberPattern_Flags_Exact_Lump_Sum_Payments()
    {
        var rule = new RoundNumberPatternRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.VoucherNumber == "PMT-101");
    }

    [Fact]
    public async Task Rule14_UnusualLedgerCombination_Flags_Capital_And_Expense_Combo()
    {
        var rule = new UnusualLedgerCombinationRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.VoucherNumber == "JRN-04");
    }

    [Fact]
    public async Task Rule15_ReversalAnomaly_Flags_Delayed_Credit_Note_Reversals()
    {
        var rule = new ReversalAnomalyRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.VoucherNumber == "CN-01");
    }

    [Fact]
    public async Task Rule16_CreditDebitNoteAnomaly_Flags_Missing_Reference()
    {
        var rule = new CreditDebitNoteAnomalyRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.VoucherNumber == "DN-01");
    }

    [Fact]
    public async Task Rule17_MissingGstInformation_Flags_Unregistered_B2B_Vendors()
    {
        var rule = new MissingGstInformationRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.VoucherNumber == "PUR-03");
    }

    [Fact]
    public async Task Rule18_MissingPanWhereApplicable_Flags_Deductee_Without_Pan()
    {
        var rule = new MissingPanWhereApplicableRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Explanation.Contains("Unregistered Contractor"));
    }

    [Fact]
    public async Task Rule19_MissingHsnSac_Flags_Taxable_Heads_Without_Code()
    {
        var rule = new MissingHsnSacRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Explanation.Contains("Fabrication Sales Head"));
    }
}
