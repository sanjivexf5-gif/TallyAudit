using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Tds;
using TallyAuditAssistant.Data;
using TallyAuditAssistant.Data.Repositories;
using TallyAuditAssistant.Engine.Tds;
using TallyAuditAssistant.Engine.Tds.Rules;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class TdsAuditEngineTests : IAsyncLifetime
{
    private readonly string _testDbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly DatabaseInitializer _initializer;
    private readonly string _companyId = "COMP-TDS-01";

    public TdsAuditEngineTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"tds_audit_test_{Guid.NewGuid():N}.db");
        _factory = new SqliteConnectionFactory(_testDbPath);
        _initializer = new DatabaseInitializer(_factory, NullLogger<DatabaseInitializer>.Instance, _testDbPath);
    }

    public async Task InitializeAsync()
    {
        await _initializer.InitializeAsync();
        using var conn = await _factory.CreateConnectionAsync();

        // Seed Company with PAN
        await conn.ExecuteAsync(@"
            INSERT INTO Companies (Id, TallyCompanyName, BooksFromDate, GSTIN, PAN, StateCode, StateName)
            VALUES (@CompanyId, 'Apex Industrial Solutions Pvt Ltd', '2025-04-01', '27AABCA1234F1Z5', 'AABCA1234F', '27', 'Maharashtra');
        ", new { CompanyId = _companyId });

        // Seed Ledgers
        await conn.ExecuteAsync(@"
            INSERT INTO Ledgers (Id, CompanyId, Name, ParentGroup, PAN, TaxType, TdsRate)
            VALUES 
            -- Contractor with PAN
            ('L1', @Comp, 'Apex Transport Contractors', 'Sundry Creditors', 'AABCA9999K', 'TDS', 2.0),
            -- Professional Firm with PAN
            ('L2', @Comp, 'Legal & Tax Associates LLP', 'Sundry Creditors', 'AACCL8888M', 'TDS', 10.0),
            -- Vendor LACKING PAN (Triggers 206AA & Check 3)
            ('L3', @Comp, 'Unregistered Technical Experts', 'Sundry Creditors', NULL, 'TDS', 20.0),
            -- Expense Heads
            ('L4', @Comp, 'Freight & Transport Charges', 'Direct Expenses', NULL, NULL, NULL),
            ('L5', @Comp, 'Legal & Professional Fees', 'Indirect Expenses', NULL, NULL, NULL),
            ('L6', @Comp, 'Office Rent Expenses', 'Indirect Expenses', NULL, NULL, NULL),
            -- TDS Liability Heads
            ('L7', @Comp, 'TDS on Contract @ 2% (194C)', 'Duties & Taxes', NULL, 'TDS', 2.0),
            ('L8', @Comp, 'TDS on Professional Fees @ 10% (194J)', 'Duties & Taxes', NULL, 'TDS', 10.0),
            -- Misplaced TDS Head (under Indirect Expenses)
            ('L9', @Comp, 'TDS Payable (Misplaced Group)', 'Indirect Expenses', NULL, 'TDS', 10.0);
        ", new { Comp = _companyId });

        // Seed Vouchers for rule testing
        await conn.ExecuteAsync(@"
            INSERT INTO Vouchers (Id, CompanyId, VoucherTypeId, VoucherTypeName, VoucherNumber, ReferenceNumber, VoucherDate, TotalAmount, PartyLedgerName, Narration, AlterId)
            VALUES 
            -- V1: Trigger for Rule 2 (Single bill > 30,000 threshold under 194C with no TDS)
            ('V1', @Comp, 'Purchase', 'Purchase', 'PUR-TDS-01', 'INV-101', '2025-05-10', 45000, 'Apex Transport Contractors', 'Freight charges', 1),

            -- V2: Trigger for Rule 3 (No PAN, amount 85,000 requiring 206AA check)
            ('V2', @Comp, 'Purchase', 'Purchase', 'PUR-TDS-02', 'INV-102', '2025-05-15', 85000, 'Unregistered Technical Experts', 'Technical consultancy without PAN', 2),

            -- V3: Trigger for Rule 5 (Math mismatch: 50,000 @ 10% = 5,000 expected, but 2,000 deducted)
            ('V3', @Comp, 'Purchase', 'Purchase', 'PUR-TDS-03', 'INV-103', '2025-05-20', 50000, 'Legal & Tax Associates LLP', 'Legal advisory math error', 3),

            -- V4: Trigger for Rule 6 (Professional Fees deducted under 194C Contractor TDS head)
            ('V4', @Comp, 'Purchase', 'Purchase', 'PUR-TDS-04', 'INV-104', '2025-06-01', 60000, 'Legal & Tax Associates LLP', 'Legal retainership under contractor', 4),

            -- V5: Trigger for Rule 10 (High value > 1,00,000 with 0 TDS)
            ('V5', @Comp, 'Purchase', 'Purchase', 'PUR-TDS-05', 'INV-105', '2025-06-15', 150000, 'Apex Transport Contractors', 'Large transport contract', 5),

            -- V6: Trigger for Rule 11 (Unusual TDS rate: 7.5% on 40,000 = 3,000)
            ('V6', @Comp, 'Purchase', 'Purchase', 'PUR-TDS-06', 'INV-106', '2025-06-20', 40000, 'Apex Transport Contractors', 'Arbitrary rate deduction', 6),

            -- V7: Trigger for Rule 12 (Direct debit reduction in Journal)
            ('V7', @Comp, 'Journal', 'Journal', 'JRN-TDS-01', NULL, '2025-07-01', 5000, NULL, 'Unilateral TDS liability write-off', 7),

            -- V8 & V9: Trigger for Rule 13 (Threshold border: 28,500 and 29,000)
            ('V8', @Comp, 'Purchase', 'Purchase', 'PUR-TDS-08', 'SPLIT-01', '2025-07-10', 28500, 'Apex Transport Contractors', 'Split bill 1', 8),
            ('V9', @Comp, 'Purchase', 'Purchase', 'PUR-TDS-09', 'SPLIT-02', '2025-07-12', 29000, 'Apex Transport Contractors', 'Split bill 2', 9);
        ", new { Comp = _companyId });

        // Seed Entries
        await conn.ExecuteAsync(@"
            INSERT INTO VoucherEntries (Id, VoucherId, LedgerName, Amount, IsDebit)
            VALUES 
            -- V3 entries: Base 50,000, TDS 2,000
            ('E1', 'V3', 'Legal & Professional Fees', 50000, 1),
            ('E2', 'V3', 'TDS on Professional Fees @ 10% (194J)', -2000, 0),

            -- V4 entries: Professional expense with 194C TDS head
            ('E3', 'V4', 'Legal & Professional Fees', 60000, 1),
            ('E4', 'V4', 'TDS on Contract @ 2% (194C)', -1200, 0),

            -- V6 entries: 40,000 with 3,000 TDS (7.5% non-standard)
            ('E5', 'V6', 'Freight & Transport Charges', 40000, 1),
            ('E6', 'V6', 'TDS on Contract @ 2% (194C)', -3000, 0),

            -- V7 entries: Journal debit to TDS head
            ('E7', 'V7', 'TDS on Contract @ 2% (194C)', 5000, 1),
            ('E8', 'V7', 'Freight & Transport Charges', -5000, 0);
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

    private TdsAuditContext CreateContext() => new(_companyId, new DateTime(2025, 4, 1), new DateTime(2026, 3, 31), "AABCA1234F", "MUMB12345A");

    [Fact]
    public async Task Check02_ThresholdMonitoring_Flags_Unwithheld_Transactions()
    {
        var rule = new ThresholdMonitoringRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.VoucherNumber == "PUR-TDS-01");
    }

    [Fact]
    public async Task Check03_PanAvailability_Flags_Payees_Without_PAN()
    {
        var rule = new PanAvailabilityAndHigherDeductionRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.PartyLedgerName == "Unregistered Technical Experts");
        Assert.Equal(20.0m, results[0].ExpectedRate);
    }

    [Fact]
    public async Task Check04_LedgerMapping_Flags_Misclassified_Liability_Heads()
    {
        var rule = new TdsLedgerMappingRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.PartyLedgerName == "TDS Payable (Misplaced Group)");
    }

    [Fact]
    public async Task Check05_DeductionAmountConsistency_Flags_Calculation_Discrepancies()
    {
        var rule = new DeductionAmountConsistencyRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.VoucherNumber == "PUR-TDS-03");
    }

    [Fact]
    public async Task Check06_SectionClassification_Flags_Mismatched_Heads()
    {
        var rule = new TdsSectionClassificationRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.VoucherNumber == "PUR-TDS-04");
    }

    [Fact]
    public async Task Check11_UnusualTdsRates_Flags_NonStandard_Tariff_Deductions()
    {
        var rule = new UnusualTdsRatesRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.VoucherNumber == "PUR-TDS-06");
    }

    [Fact]
    public async Task Check12_TdsReversalAnomalies_Flags_Journal_Debit_Reductions()
    {
        var rule = new TdsReversalAnomaliesRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.VoucherNumber == "JRN-TDS-01");
    }

    [Fact]
    public async Task Check13_ThresholdBorderTransactions_Flags_Clustered_Invoices()
    {
        var rule = new ThresholdBorderTransactionsRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.PartyLedgerName == "Apex Transport Contractors");
    }

    [Fact]
    public async Task Full_TdsAuditEngine_Executes_All_13_Rules_And_Persists()
    {
        var repo = new TdsRepository(_factory, NullLogger<TdsRepository>.Instance);
        var engine = new TdsAuditEngine(repo, NullLogger<TdsAuditEngine>.Instance);

        engine.RegisterRule(new PotentialTdsApplicabilityRule(_factory));
        engine.RegisterRule(new ThresholdMonitoringRule(_factory));
        engine.RegisterRule(new PanAvailabilityAndHigherDeductionRule(_factory));
        engine.RegisterRule(new TdsLedgerMappingRule(_factory));
        engine.RegisterRule(new DeductionAmountConsistencyRule(_factory));
        engine.RegisterRule(new TdsSectionClassificationRule(_factory));
        engine.RegisterRule(new VendorCumulativeAnalysisRule(_factory));
        engine.RegisterRule(new ExpenseCategoryAnalysisRule(_factory));
        engine.RegisterRule(new TdsPayableVsDeductionRule(_factory));
        engine.RegisterRule(new MissingTdsEntriesRule(_factory));
        engine.RegisterRule(new UnusualTdsRatesRule(_factory));
        engine.RegisterRule(new TdsReversalAnomaliesRule(_factory));
        engine.RegisterRule(new ThresholdBorderTransactionsRule(_factory));

        Assert.Equal(13, engine.RegisteredRules.Count);

        var summary = await engine.ExecuteAuditAsync(CreateContext());
        Assert.True(summary.TotalTransactionsChecked > 0);
        Assert.True(summary.ExceptionCount > 0);

        // Verify voucher drill-down
        var detail = await repo.GetVoucherDetailAsync("V3");
        Assert.NotNull(detail);
        Assert.Equal("PUR-TDS-03", detail.VoucherNumber);
        Assert.Equal(2, detail.Entries.Count);
    }
}
