using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Gst;
using TallyAuditAssistant.Data;
using TallyAuditAssistant.Data.Repositories;
using TallyAuditAssistant.Engine.Gst;
using TallyAuditAssistant.Engine.Gst.Rules;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class GstAuditEngineTests : IAsyncLifetime
{
    private readonly string _testDbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly DatabaseInitializer _initializer;
    private readonly string _companyId = "COMP-GST-01";

    public GstAuditEngineTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"gst_audit_test_{Guid.NewGuid():N}.db");
        _factory = new SqliteConnectionFactory(_testDbPath);
        _initializer = new DatabaseInitializer(_factory, NullLogger<DatabaseInitializer>.Instance, _testDbPath);
    }

    public async Task InitializeAsync()
    {
        await _initializer.InitializeAsync();
        using var conn = await _factory.CreateConnectionAsync();

        // Seed Company with Maharashtra GSTIN (27)
        await conn.ExecuteAsync(@"
            INSERT INTO Companies (Id, TallyCompanyName, BooksFromDate, GSTIN, StateCode, StateName)
            VALUES (@CompanyId, 'Apex Fabrication & Engineering Pvt Ltd', '2025-04-01', '27AABCA1234F1Z5', '27', 'Maharashtra');
        ", new { CompanyId = _companyId });

        // Seed Ledgers
        await conn.ExecuteAsync(@"
            INSERT INTO Ledgers (Id, CompanyId, Name, ParentGroup, GSTIN, PAN, StateName, TaxType, GstRate, HsnCode)
            VALUES 
            -- Regular Valid Vendor (27 - Maharashtra)
            ('L1', @Comp, 'Bharat Heavy Plates Ltd', 'Sundry Creditors', '27AAACB2222D1Z9', 'AAACB2222D', 'Maharashtra', 'GST', 18.0, '7208'),
            -- Interstate Vendor (24 - Gujarat)
            ('L2', @Comp, 'Gujarat Alloy Supplies', 'Sundry Creditors', '24BBBCD3333E1Z4', 'BBBCD3333E', 'Gujarat', 'GST', 18.0, '7204'),
            -- Invalid GSTIN vendor (only 13 chars)
            ('L3', @Comp, 'Defective Syntax Traders', 'Sundry Creditors', '27AAACB2222D1', 'AAACB2222D', 'Maharashtra', 'GST', 18.0, '7208'),
            -- Vendor with NO GSTIN recorded
            ('L4', @Comp, 'Unregistered Metal Works', 'Sundry Creditors', NULL, '', 'Maharashtra', 'GST', 18.0, '7208'),
            -- Composition Dealer
            ('L5', @Comp, 'Local Small Works (Composition)', 'Sundry Creditors', '27CCCCD4444F1Z8', 'CCCCD4444F', 'Maharashtra', 'GST', 1.0, '7208'),
            -- Taxable Sales Head with missing HSN
            ('L6', @Comp, 'Industrial Fabrication Sales', 'Sales Accounts', NULL, NULL, 'Maharashtra', 'GST', 18.0, NULL),
            -- Taxable Sales Head with unusual rate (7.5%)
            ('L7', @Comp, 'Special Tariff Sales', 'Sales Accounts', NULL, NULL, 'Maharashtra', 'GST', 7.5, '8481'),
            -- GST Duty Heads
            ('L8', @Comp, 'Input CGST @ 9%', 'Duties & Taxes', NULL, NULL, NULL, 'GST', 9.0, NULL),
            ('L9', @Comp, 'Input SGST @ 9%', 'Duties & Taxes', NULL, NULL, NULL, 'GST', 9.0, NULL),
            ('L10', @Comp, 'Input IGST @ 18%', 'Duties & Taxes', NULL, NULL, NULL, 'GST', 18.0, NULL),
            -- Misplaced GST head (under Indirect Expenses instead of Duties & Taxes)
            ('L11', @Comp, 'Output CGST Head (Misplaced)', 'Indirect Expenses', NULL, NULL, NULL, 'GST', 9.0, NULL),
            -- RCM Service Head
            ('L12', @Comp, 'National Goods Transport Agency (GTA)', 'Sundry Creditors', NULL, NULL, 'Maharashtra', 'GST', 5.0, '9965'),
            -- Rounding Head
            ('L13', @Comp, 'Invoice Round Off A/c', 'Indirect Expenses', NULL, NULL, NULL, NULL, NULL, NULL);
        ", new { Comp = _companyId });

        // Seed Vouchers for rule triggers
        await conn.ExecuteAsync(@"
            INSERT INTO Vouchers (Id, CompanyId, VoucherTypeId, VoucherTypeName, VoucherNumber, ReferenceNumber, VoucherDate, TotalAmount, PartyLedgerName, Narration, AlterId)
            VALUES 
            -- V1: Trigger for Rule 1 (Invalid GSTIN syntax)
            ('V1', @Comp, 'Purchase', 'Purchase', 'PUR-01', 'INV-001', '2025-05-10', 60000, 'Defective Syntax Traders', 'Metal plates purchase', 1),

            -- V2: Trigger for Rule 2 (Missing GSTIN on commercial purchase > 50,000)
            ('V2', @Comp, 'Purchase', 'Purchase', 'PUR-02', 'INV-002', '2025-05-15', 125000, 'Unregistered Metal Works', 'Unregistered raw materials', 2),

            -- V3: Trigger for Rule 3 (Composition dealer with tax charged)
            ('V3', @Comp, 'Purchase', 'Purchase', 'PUR-03', 'INV-003', '2025-05-20', 40000, 'Local Small Works (Composition)', 'Small contract', 3),

            -- V4: Trigger for Rule 4 (CGST and SGST mismatch)
            ('V4', @Comp, 'Purchase', 'Purchase', 'PUR-04', 'INV-004', '2025-06-01', 59000, 'Bharat Heavy Plates Ltd', 'Asymmetric tax', 4),

            -- V5: Trigger for Rule 5 (Interstate tax charged on Intrastate transaction: IGST on Maharashtra vendor)
            ('V5', @Comp, 'Purchase', 'Purchase', 'PUR-05', 'INV-005', '2025-06-05', 118000, 'Bharat Heavy Plates Ltd', 'Intrastate IGST error', 5),

            -- V6: Trigger for Rule 9 (Credit Note missing reference invoice)
            ('V6', @Comp, 'Credit Note', 'Credit Note', 'CN-01', NULL, '2025-06-15', 25000, 'Bharat Heavy Plates Ltd', 'Defective goods return without bill link', 6),

            -- V7 & V8: Trigger for Rule 10 (Duplicate supplier invoice reference INV-888)
            ('V7', @Comp, 'Purchase', 'Purchase', 'PUR-07', 'INV-888', '2025-07-01', 75000, 'Bharat Heavy Plates Ltd', 'Batch A', 7),
            ('V8', @Comp, 'Purchase', 'Purchase', 'PUR-08', 'INV-888', '2025-07-05', 75000, 'Bharat Heavy Plates Ltd', 'Batch A repeat', 8),

            -- V9: Trigger for Rule 13 (GTA RCM liability)
            ('V9', @Comp, 'Payment', 'Payment', 'PMT-01', 'FRT-10', '2025-07-10', 15000, 'National Goods Transport Agency (GTA)', 'Freight transport payments', 9),

            -- V10: Trigger for Rule 15 (Direct tax posting in Journal without base voucher)
            ('V10', @Comp, 'Journal', 'Journal', 'JRN-TAX-01', NULL, '2025-07-15', 5000, NULL, 'Tax balance adjustment', 10),

            -- V11: Trigger for Rule 16 (Negative total sales invoice instead of credit note)
            ('V11', @Comp, 'Sales', 'Sales', 'INV-NEG-01', 'REF-NEG', '2025-07-20', -35000, 'Bharat Heavy Plates Ltd', 'Negative adjustment', 11),

            -- V12: Trigger for Rule 17 (Excessive round-off > 10 rupees)
            ('V12', @Comp, 'Purchase', 'Purchase', 'PUR-12', 'INV-12', '2025-07-25', 50085, 'Bharat Heavy Plates Ltd', 'Purchase with large round off', 12);
        ", new { Comp = _companyId });

        // Seed Voucher line items
        await conn.ExecuteAsync(@"
            INSERT INTO VoucherEntries (Id, VoucherId, LedgerName, Amount, IsDebit)
            VALUES 
            -- V3 entries: Composition dealer billed with tax
            ('E1', 'V3', 'Industrial Fabrication Sales', 35000, 1),
            ('E2', 'V3', 'Input CGST @ 9%', 2500, 1),
            ('E3', 'V3', 'Input SGST @ 9%', 2500, 1),

            -- V4 entries: CGST 4500 vs SGST 5000 (mismatch > 1 rupee)
            ('E4', 'V4', 'Industrial Fabrication Sales', 50000, 1),
            ('E5', 'V4', 'Input CGST @ 9%', 4000, 1),
            ('E6', 'V4', 'Input SGST @ 9%', 5000, 1),

            -- V5 entries: IGST charged on MH vendor
            ('E7', 'V5', 'Industrial Fabrication Sales', 100000, 1),
            ('E8', 'V5', 'Input IGST @ 18%', 18000, 1),

            -- V9 entries: GTA payment
            ('E9', 'V9', 'National Goods Transport Agency (GTA)', 15000, 1),

            -- V10 entries: Pure tax debits/credits in journal
            ('E10', 'V10', 'Input CGST @ 9%', 2500, 1),
            ('E11', 'V10', 'Input SGST @ 9%', -2500, 0),

            -- V12 entries: Round off = 85 rupees (> 10 max limit)
            ('E12', 'V12', 'Industrial Fabrication Sales', 50000, 1),
            ('E13', 'V12', 'Invoice Round Off A/c', 85, 1);
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

    private GstAuditContext CreateContext() => new(_companyId, new DateTime(2025, 4, 1), new DateTime(2026, 3, 31), "27AABCA1234F1Z5", "27");

    [Fact]
    public async Task Check01_GstinFormat_Flags_Invalid_Structure()
    {
        var rule = new GstinFormatCheckRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.PartyGstin == "27AAACB2222D1");
        Assert.StartsWith("Flagged because", results[0].Explanation);
        Assert.Equal("IN-ALL", rule.Jurisdiction);
    }

    [Fact]
    public async Task Check02_MissingGstin_Flags_Unregistered_Commercial_Vendors()
    {
        var rule = new MissingGstinOnB2BRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.PartyLedgerName == "Unregistered Metal Works");
    }

    [Fact]
    public async Task Check03_Composition_Dealer_Collecting_Tax_Is_Flagged()
    {
        var rule = new GstRegistrationTypeInconsistencyRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.PartyLedgerName == "Local Small Works (Composition)");
    }

    [Fact]
    public async Task Check04_CgstSgst_Mismatch_Is_Flagged()
    {
        var rule = new CgstSgstIgstConsistencyRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.VoucherNumber == "PUR-04");
    }

    [Fact]
    public async Task Check05_Intrastate_Supply_Charged_With_IGST_Is_Flagged()
    {
        var rule = new InterstateIntrastateTaxConsistencyRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.VoucherNumber == "PUR-05");
        Assert.Contains("IGST applied on Intrastate Supply", results[0].EvidenceJson);
    }

    [Fact]
    public async Task Check07_GstRateConsistency_Flags_Unusual_Schedule_Rates()
    {
        var rule = new GstRateConsistencyRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.PartyLedgerName == "Special Tariff Sales");
    }

    [Fact]
    public async Task Check08_HsnSacPresence_Flags_Missing_Codes()
    {
        var rule = new HsnSacPresenceRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.PartyLedgerName == "Industrial Fabrication Sales");
    }

    [Fact]
    public async Task Check09_CreditNote_Missing_Reference_Is_Flagged()
    {
        var rule = new GstCreditDebitNoteAnomalyRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.VoucherNumber == "CN-01");
    }

    [Fact]
    public async Task Check10_Duplicate_Invoice_Numbers_Are_Flagged()
    {
        var rule = new DuplicateGstInvoiceNumberRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Explanation.Contains("INV-888"));
    }

    [Fact]
    public async Task Check13_Rcm_Notified_GTA_Expense_Is_Flagged()
    {
        var rule = new PossibleRcmExceptionsRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.VoucherNumber == "PMT-01");
    }

    [Fact]
    public async Task Check14_Misplaced_Gst_Ledger_Is_Flagged()
    {
        var rule = new GstLedgerMappingIssuesRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.PartyLedgerName == "Output CGST Head (Misplaced)");
    }

    [Fact]
    public async Task Check15_Standalone_Tax_Posting_In_Journal_Is_Flagged()
    {
        var rule = new TaxLedgerPostingAnomaliesRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.VoucherNumber == "JRN-TAX-01");
    }

    [Fact]
    public async Task Check16_Negative_Invoice_Total_Is_Flagged()
    {
        var rule = new NegativeTaxableAmountRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.VoucherNumber == "INV-NEG-01");
    }

    [Fact]
    public async Task Check17_Excessive_RoundOff_Is_Flagged()
    {
        var rule = new RoundOffAnomaliesRule(_factory);
        var results = await rule.EvaluateAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.VoucherNumber == "PUR-12");
    }

    [Fact]
    public async Task Full_GstAuditEngine_Executes_And_Generates_Summary()
    {
        var repo = new GstRepository(_factory, NullLogger<GstRepository>.Instance);
        var engine = new GstAuditEngine(repo, NullLogger<GstAuditEngine>.Instance);

        // Register all rules
        engine.RegisterRule(new GstinFormatCheckRule(_factory));
        engine.RegisterRule(new MissingGstinOnB2BRule(_factory));
        engine.RegisterRule(new GstRegistrationTypeInconsistencyRule(_factory));
        engine.RegisterRule(new CgstSgstIgstConsistencyRule(_factory));
        engine.RegisterRule(new InterstateIntrastateTaxConsistencyRule(_factory));
        engine.RegisterRule(new TaxableValueVsTaxAmountRule(_factory));
        engine.RegisterRule(new GstRateConsistencyRule(_factory));
        engine.RegisterRule(new HsnSacPresenceRule(_factory));
        engine.RegisterRule(new GstCreditDebitNoteAnomalyRule(_factory));
        engine.RegisterRule(new DuplicateGstInvoiceNumberRule(_factory));
        engine.RegisterRule(new DuplicateGstTransactionRule(_factory));
        engine.RegisterRule(new UnusualTaxRatesRule(_factory));
        engine.RegisterRule(new PossibleRcmExceptionsRule(_factory));
        engine.RegisterRule(new GstLedgerMappingIssuesRule(_factory));
        engine.RegisterRule(new TaxLedgerPostingAnomaliesRule(_factory));
        engine.RegisterRule(new NegativeTaxableAmountRule(_factory));
        engine.RegisterRule(new RoundOffAnomaliesRule(_factory));
        engine.RegisterRule(new PlaceOfSupplyInconsistencyRule(_factory));

        Assert.Equal(18, engine.RegisteredRules.Count);

        var summary = await engine.ExecuteAuditAsync(CreateContext());
        Assert.True(summary.TotalTransactionsChecked > 0);
        Assert.True(summary.ExceptionCount > 0);
        Assert.True(summary.HighSeverityCount > 0);

        // Verify drill down to underlying voucher detail
        var voucherDetail = await repo.GetVoucherDetailAsync("V4");
        Assert.NotNull(voucherDetail);
        Assert.Equal("PUR-04", voucherDetail.VoucherNumber);
        Assert.NotEmpty(voucherDetail.Entries);
    }
}
