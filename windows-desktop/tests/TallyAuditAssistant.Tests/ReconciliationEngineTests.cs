using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;
using TallyAuditAssistant.Engine.Reconciliation;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class ReconciliationEngineTests : IAsyncLifetime
{
    private readonly string _testDbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly DatabaseInitializer _initializer;
    private readonly string _companyId = "COMP-REC-001";

    public ReconciliationEngineTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"recon_test_{Guid.NewGuid():N}.db");
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

        // Seed synthetic test ledger datasets for perfect / small difference / material difference / missing entries / party mapping
        await conn.ExecuteAsync(@"
            INSERT INTO Ledgers (Id, CompanyId, Name, ParentGroup, GSTIN, PAN, OpeningBalance, ClosingBalance, TaxType, HsnCode, GstRate)
            VALUES 
            -- Perfect Reconciliation Ledger (Opening 1000 + Debit 500 - Credit 200 = Closing 1300)
            ('L1', @Comp, 'Perfect Ledger', 'Indirect Expenses', NULL, NULL, 1000.00, 1300.00, NULL, NULL, 0),
            
            -- Material Difference Ledger (Opening 1000 + Debit 500 - Credit 200 = Expected 1300, Closing 5000)
            ('L2', @Comp, 'Mismatched Ledger', 'Indirect Expenses', NULL, NULL, 1000.00, 5000.00, NULL, NULL, 0),
            
            -- Debtors / Creditors with missing registration credentials
            ('L3', @Comp, 'Suspicious Debtor Account', 'Sundry Debtors', '', '', 0.00, 0.00, NULL, NULL, 0),
            ('L4', @Comp, 'Valid Creditor Account', 'Sundry Creditors', '27AAAAA1111A1Z1', 'AAAAA1111A', 0.00, -50000.00, NULL, NULL, 0),

            -- GST and Tax Rate Product Ledgers
            ('L5', @Comp, 'Product Sales 18%', 'Sales Accounts', NULL, NULL, 0.00, 200000.00, 'GST', '9983', 18.0),
            ('L6', @Comp, 'Input CGST Ledger', 'Duties & Taxes', NULL, NULL, 0.00, 18000.00, 'GST', NULL, 0),
            ('L7', @Comp, 'Input SGST Ledger', 'Duties & Taxes', NULL, NULL, 0.00, 18000.00, 'GST', NULL, 0),

            -- Cash and Bank Ledgers
            ('L8', @Comp, 'HDFC Bank Accounts', 'Bank Accounts', NULL, NULL, 50000.00, 100000.00, NULL, NULL, 0);
        ", new { Comp = _companyId });

        // Seed vouchers representing distinct transaction flows
        await conn.ExecuteAsync(@"
            INSERT INTO Vouchers (Id, CompanyId, VoucherTypeId, VoucherTypeName, VoucherNumber, ReferenceNumber, VoucherDate, TotalAmount, PartyLedgerName, Narration, AlterId)
            VALUES 
            -- Perfect transactions (L1)
            ('V1', @Comp, 'Journal', 'Journal', 'JRN-REC-1', NULL, '2025-05-15', 500.00, NULL, 'Standard debit posting', 1),
            ('V2', @Comp, 'Journal', 'Journal', 'JRN-REC-2', NULL, '2025-05-16', 200.00, NULL, 'Standard credit posting', 2),

            -- GST Rate mismatch voucher
            ('V3', @Comp, 'Sales', 'Sales', 'INV-REC-100', 'REF-S1', '2025-06-10', 118000.00, 'Suspicious Debtor Account', 'Sales invoice', 3),

            -- TDS Rate discrepancy professional fee
            ('V4', @Comp, 'Journal', 'Journal', 'JRN-REC-TDS', NULL, '2025-07-20', 100000.00, 'Valid Creditor Account', 'Professional consulting services', 4),

            -- Duplicate Bank Payment candidate
            ('V5', @Comp, 'Payment', 'Payment', 'PMT-REC-1', NULL, '2025-08-01', 15000.00, 'Valid Creditor Account', 'Duplicate check 1', 5),
            ('V6', @Comp, 'Payment', 'Payment', 'PMT-REC-2', NULL, '2025-08-01', 15000.00, 'Valid Creditor Account', 'Duplicate check 2', 6),

            -- Cash Bank Contra Transfer Anomaly
            ('V7', @Comp, 'Contra', 'Contra', 'CON-REC-1', NULL, '2025-09-10', 5000.00, NULL, 'Inter bank contra transfer mismatch', 7);
        ", new { Comp = _companyId });

        // Seed voucher splits
        await conn.ExecuteAsync(@"
            INSERT INTO VoucherEntries (Id, VoucherId, LedgerName, Amount, IsDebit)
            VALUES 
            -- Perfect ledger splits (L1)
            ('E1', 'V1', 'Perfect Ledger', 500.00, 1),
            ('E2', 'V1', 'HDFC Bank Accounts', -500.00, 0),
            ('E3', 'V2', 'Perfect Ledger', -200.00, 0),
            ('E4', 'V2', 'HDFC Bank Accounts', 200.00, 1),

            -- GST Rate mismatch details (Taxable 100,000 * 18% expected is 18,000. Let's record only 5,000 to trigger rule)
            ('E5', 'V3', 'Product Sales 18%', 100000.00, 0),
            ('E6', 'V3', 'Input CGST Ledger', 2500.00, 1),
            ('E7', 'V3', 'Input SGST Ledger', 2500.00, 1),
            ('E8', 'V3', 'Suspicious Debtor Account', 105000.00, 1),

            -- TDS Deduction rate mismatch professional fees (Recorded only 500 TDS instead of 10,000 expected)
            ('E9', 'V4', 'Professional consulting services', 100000.00, 1),
            ('E10', 'V4', 'TDS Professional Ledger', 500.00, 0),
            ('E11', 'V4', 'Valid Creditor Account', 99500.00, 0),

            -- Inter-bank Contra transfer mismatch (Debits 5,000 vs Credits 100)
            ('E12', 'V7', 'HDFC Bank Accounts', 5000.00, 1),
            ('E13', 'V7', 'Perfect Ledger', -100.00, 0);
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
    public async Task TrialBalanceConsistencyRule_Flags_Material_Differences()
    {
        var rule = new TrialBalanceConsistencyRule(_factory);
        var results = await rule.ExecuteAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Explanation.Contains("Mismatched Ledger"));
        Assert.DoesNotContain(results, r => r.Explanation.Contains("Perfect Ledger"));
    }

    [Fact]
    public async Task GstRateReconciliationRule_Flags_Tax_Discrepancy()
    {
        var rule = new GstRateReconciliationRule(_factory);
        var results = await rule.ExecuteAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.VoucherNumber == "INV-REC-100");
    }

    [Fact]
    public async Task TdsExpenseVerificationRule_Flags_Incorrect_Deduction_Amount()
    {
        var rule = new TdsExpenseVerificationRule(_factory);
        var results = await rule.ExecuteAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.VoucherNumber == "JRN-REC-TDS");
    }

    [Fact]
    public async Task BankCashReconciliationRule_Flags_Duplicate_Candidate()
    {
        var rule = new BankCashReconciliationRule(_factory);
        var results = await rule.ExecuteAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.VoucherNumber == "PMT-REC-1");
    }

    [Fact]
    public async Task ContraVerificationRule_Flags_Transfer_Imbalance()
    {
        var rule = new ContraVerificationRule(_factory);
        var results = await rule.ExecuteAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.VoucherNumber == "CON-REC-1");
    }

    [Fact]
    public async Task PartyMasterReconciliationRule_Flags_Missing_Tax_Credentials()
    {
        var rule = new PartyMasterReconciliationRule(_factory);
        var results = await rule.ExecuteAsync(CreateContext());
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Explanation.Contains("Suspicious Debtor Account"));
        Assert.DoesNotContain(results, r => r.Explanation.Contains("Valid Creditor Account"));
    }
}
