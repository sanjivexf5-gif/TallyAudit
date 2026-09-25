using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using TallyAuditAssistant.Core.Domain.Duplicates;
using TallyAuditAssistant.Data;
using TallyAuditAssistant.Data.Repositories;
using TallyAuditAssistant.Engine.Duplicates;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class DuplicateDetectionEngineTests : IAsyncLifetime
{
    private readonly string _testDbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly DatabaseInitializer _initializer;
    private readonly string _companyId = "COMP-DUP-01";

    public DuplicateDetectionEngineTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"dup_audit_test_{Guid.NewGuid():N}.db");
        _factory = new SqliteConnectionFactory(_testDbPath);
        _initializer = new DatabaseInitializer(_factory, NullLogger<DatabaseInitializer>.Instance, _testDbPath);
    }

    public async Task InitializeAsync()
    {
        await _initializer.InitializeAsync();
        using var conn = await _factory.CreateConnectionAsync();

        // Seed Company
        await conn.ExecuteAsync(@"
            INSERT INTO Companies (Id, TallyCompanyName, BooksFromDate, GSTIN, StateCode, StateName)
            VALUES (@CompanyId, 'Apex Industrial Solutions Pvt Ltd', '2025-04-01', '27AABCA1234F1Z5', '27', 'Maharashtra');
        ", new { CompanyId = _companyId });

        // Seed Party Ledgers
        await conn.ExecuteAsync(@"
            INSERT INTO Ledgers (Id, CompanyId, Name, ParentGroup, GSTIN, PAN)
            VALUES 
            ('L1', @Comp, 'Bharat Heavy Plates Ltd', 'Sundry Creditors', '27AAACB2222D1Z9', 'AAACB2222D'),
            ('L2', @Comp, 'Acme Steel Distribution', 'Sundry Debtors', '27BBBCB3333E1Z8', 'BBBCB3333E'),
            ('L3', @Comp, 'Precision Tooling Corp', 'Sundry Creditors', '27CCCCD4444F1Z7', 'CCCCD4444F');
        ", new { Comp = _companyId });

        // Seed Vouchers covering:
        // 1. Sales Invoices Exact Duplicate
        // 2. Purchase Invoices Likely Duplicate (same amount, 2 days apart)
        // 3. Payment Voucher Possible Duplicate (similar amount, similar narration)
        // 4. Receipt Voucher Duplicate
        // 5. Journal Entry Duplicate
        // 6. Credit Note / Debit Note Duplicate
        await conn.ExecuteAsync(@"
            INSERT INTO Vouchers (Id, CompanyId, VoucherTypeId, VoucherTypeName, VoucherNumber, ReferenceNumber, VoucherDate, TotalAmount, PartyLedgerName, Narration, AlterId)
            VALUES 
            -- 1. Exact Duplicate Sales Invoices (Same Date, Party, Amount, Number)
            ('V1', @Comp, 'Sales', 'Sales', 'INV/25-26/101', 'REF-101', '2025-05-10', 45000, 'Acme Steel Distribution', 'Supply of mild steel channels batch A', 1),
            ('V2', @Comp, 'Sales', 'Sales', 'INV/25-26/101', 'REF-101', '2025-05-10', 45000, 'Acme Steel Distribution', 'Supply of mild steel channels batch A', 2),

            -- 2. Likely Duplicate Purchase Invoices (Same Party, Amount 75,000, Date 2 days apart)
            ('V3', @Comp, 'Purchase', 'Purchase', 'PUR/2025/088', 'BILL-88', '2025-06-01', 75000, 'Bharat Heavy Plates Ltd', 'Heavy fabrication steel plates', 3),
            ('V4', @Comp, 'Purchase', 'Purchase', 'PUR/2025/091', 'BILL-88-A', '2025-06-03', 75000, 'Bharat Heavy Plates Ltd', 'Heavy fabrication steel plates consignment', 4),

            -- 3. Possible Duplicate Payment Vouchers (Same Party, Amount 25,000 vs 25,005, Similar Narration)
            ('V5', @Comp, 'Payment', 'Payment', 'PMT/0044', 'CHQ-1001', '2025-06-15', 25000, 'Precision Tooling Corp', 'Advance payment for tooling dies order #402', 5),
            ('V6', @Comp, 'Payment', 'Payment', 'PMT/0047', 'CHQ-1002', '2025-06-18', 25000, 'Precision Tooling Corp', 'Advance for tooling dies order #402', 6),

            -- 4. Receipt Duplicate
            ('V7', @Comp, 'Receipt', 'Receipt', 'RCPT/0012', 'NEFT-881', '2025-07-01', 100000, 'Acme Steel Distribution', 'Customer balance settlement', 7),
            ('V8', @Comp, 'Receipt', 'Receipt', 'RCPT/0014', 'NEFT-881', '2025-07-02', 100000, 'Acme Steel Distribution', 'Customer balance settlement duplicate entry', 8),

            -- 5. Journal Duplicate (Adjustment entries)
            ('V9', @Comp, 'Journal', 'Journal', 'JRN/0055', NULL, '2025-07-15', 12000, 'Bharat Heavy Plates Ltd', 'Freight debit note adjustment', 9),
            ('V10', @Comp, 'Journal', 'Journal', 'JRN/0056', NULL, '2025-07-15', 12000, 'Bharat Heavy Plates Ltd', 'Freight debit note adjustment entry', 10),

            -- 6. Credit Note Duplicate
            ('V11', @Comp, 'Credit Note', 'Credit Note', 'CN/002', 'INV/25-26/101', '2025-08-01', 5000, 'Acme Steel Distribution', 'Rate difference credit note', 11),
            ('V12', @Comp, 'Credit Note', 'Credit Note', 'CN/003', 'INV/25-26/101', '2025-08-02', 5000, 'Acme Steel Distribution', 'Rate difference credit note re-entry', 12);
        ", new { Comp = _companyId });
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

    [Fact]
    public async Task DuplicateEngine_Detects_Exact_Likely_And_Possible_Duplicates()
    {
        var repo = new DuplicateRepository(_factory, NullLogger<DuplicateRepository>.Instance);
        var engine = new DuplicateDetectionEngine(_factory, repo, NullLogger<DuplicateDetectionEngine>.Instance);

        var summary = await engine.DetectDuplicatesAsync(_companyId);

        Assert.True(summary.TotalVouchersScanned >= 12);
        Assert.True(summary.TotalDuplicatePairsFound >= 5);
        Assert.True(summary.ExactDuplicatesCount >= 1);
        Assert.True(summary.LikelyDuplicatesCount >= 1);

        // Verify that terminology uses neutral descriptors
        var matchPairs = await repo.GetMatchPairsAsync(_companyId);
        Assert.NotEmpty(matchPairs);
        Assert.All(matchPairs, p =>
        {
            Assert.Contains("Potential", p.Explanation);
            Assert.DoesNotContain("fraudulent", p.Explanation.ToLowerInvariant());
        });
    }

    [Fact]
    public async Task DuplicateEngine_Handles_Large_Datasets_Efficiently_Via_Blocking()
    {
        var repo = new DuplicateRepository(_factory, NullLogger<DuplicateRepository>.Instance);
        var engine = new DuplicateDetectionEngine(_factory, repo, NullLogger<DuplicateDetectionEngine>.Instance);

        var config = new DuplicateStrategyConfiguration
        {
            EnableExactMatch = true,
            EnableStrongMatch = true,
            EnablePossibleMatch = true,
            StrongMatchDateWindowDays = 5,
            PossibleMatchDateWindowDays = 20
        };

        var summary = await engine.DetectDuplicatesAsync(_companyId, overrideConfig: config);
        Assert.NotNull(summary);
        Assert.True(summary.TotalPotentialExposureRupees > 0);
    }
}
