using System;
using System.IO;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Data;
using TallyAuditAssistant.Data.Repositories;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class ReportsMonetaryTypeHandlerTests : IAsyncLifetime
{
    private readonly string _testDbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly DatabaseInitializer _initializer;
    private readonly AuditRepository _repository;

    public ReportsMonetaryTypeHandlerTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"reports_type_test_{Guid.NewGuid():N}.db");
        _factory = new SqliteConnectionFactory(_testDbPath);
        _initializer = new DatabaseInitializer(_factory, NullLogger<DatabaseInitializer>.Instance, _testDbPath);
        _repository = new AuditRepository(_factory);
    }

    public async Task InitializeAsync()
    {
        await _initializer.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (File.Exists(_testDbPath))
            {
                File.Delete(_testDbPath);
            }
        }
        catch { }
    }

    [Fact]
    public async Task GetExceptions_WithInt64FlaggedAmount_MaterializesToDecimal()
    {
        var companyId = "COMP-TYPE-TEST-1";
        await _repository.SaveCompanyAsync(new Company
        {
            Id = companyId,
            TallyCompanyName = "Type Test Industrial Pvt Ltd",
            BooksFromDate = new DateTime(2025, 4, 1)
        });

        using (var connection = await _factory.CreateConnectionAsync())
        {
            await connection.ExecuteAsync(@"
                INSERT INTO Exceptions (
                    Id, CompanyId, RuleId, RuleName, Category, Severity, 
                    EntityId, EntityType, VoucherNumber, VoucherDate, LedgerName, 
                    FlaggedAmount, Explanation, EvidenceJson, Status, FlaggedAt
                ) VALUES (
                    'EXC-INT-001', @CompanyId, 'RULE-001', 'Test Round Cash Rule', 0, 2,
                    'VOUCH-001', 'Voucher', 'PMT-101', '2025-05-10', 'Petty Cash',
                    120000, 'Flagged round integer cash payment', '{}', 0, CURRENT_TIMESTAMP
                )
            ", new { CompanyId = companyId });
        }

        var exceptions = await _repository.GetExceptionsAsync(companyId);

        Assert.NotEmpty(exceptions);
        var ex = Assert.Single(exceptions);
        Assert.NotNull(ex.FlaggedAmount);
        Assert.Equal(120000m, ex.FlaggedAmount.Value);
    }

    [Fact]
    public async Task GetExceptions_WithRealFlaggedAmount_MaterializesToDecimal()
    {
        var companyId = "COMP-TYPE-TEST-2";
        await _repository.SaveCompanyAsync(new Company
        {
            Id = companyId,
            TallyCompanyName = "Real Amount Industrial Pvt Ltd",
            BooksFromDate = new DateTime(2025, 4, 1)
        });

        using (var connection = await _factory.CreateConnectionAsync())
        {
            await connection.ExecuteAsync(@"
                INSERT INTO Exceptions (
                    Id, CompanyId, RuleId, RuleName, Category, Severity, 
                    EntityId, EntityType, VoucherNumber, VoucherDate, LedgerName, 
                    FlaggedAmount, Explanation, EvidenceJson, Status, FlaggedAt
                ) VALUES (
                    'EXC-REAL-001', @CompanyId, 'RULE-002', 'Test GST Rate Mismatch', 1, 3,
                    'VOUCH-002', 'Voucher', 'INV-202', '2025-06-15', 'Raw Material Purchases',
                    120000.50, 'GST tax calculation variance', '{}', 0, CURRENT_TIMESTAMP
                )
            ", new { CompanyId = companyId });
        }

        var exceptions = await _repository.GetExceptionsAsync(companyId);

        Assert.NotEmpty(exceptions);
        var ex = Assert.Single(exceptions);
        Assert.NotNull(ex.FlaggedAmount);
        Assert.Equal(120000.50m, ex.FlaggedAmount.Value);
    }

    [Fact]
    public async Task GetExceptions_WithNullFlaggedAmount_MaterializesToNullDecimal()
    {
        var companyId = "COMP-TYPE-TEST-3";
        await _repository.SaveCompanyAsync(new Company
        {
            Id = companyId,
            TallyCompanyName = "Null Amount Industrial Pvt Ltd",
            BooksFromDate = new DateTime(2025, 4, 1)
        });

        using (var connection = await _factory.CreateConnectionAsync())
        {
            await connection.ExecuteAsync(@"
                INSERT INTO Exceptions (
                    Id, CompanyId, RuleId, RuleName, Category, Severity, 
                    EntityId, EntityType, VoucherNumber, VoucherDate, LedgerName, 
                    FlaggedAmount, Explanation, EvidenceJson, Status, FlaggedAt
                ) VALUES (
                    'EXC-NULL-001', @CompanyId, 'RULE-003', 'Missing Voucher Sequence', 2, 1,
                    'VOUCH-003', 'Voucher', 'JRN-303', '2025-07-20', 'Suspense Account',
                    NULL, 'Sequence gap detected without monetary value', '{}', 0, CURRENT_TIMESTAMP
                )
            ", new { CompanyId = companyId });
        }

        var exceptions = await _repository.GetExceptionsAsync(companyId);

        Assert.NotEmpty(exceptions);
        var ex = Assert.Single(exceptions);
        Assert.Null(ex.FlaggedAmount);
    }
}
