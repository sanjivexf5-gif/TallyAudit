using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;
using TallyAuditAssistant.Data.Repositories;
using TallyAuditAssistant.Engine.Services;
using TallyAuditAssistant.TallyIntegration;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class TallyReadOnlyPolicyTests : IAsyncLifetime
{
    private readonly string _testDbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly DatabaseInitializer _initializer;
    private readonly AuditRepository _auditRepository;
    private readonly InvestigationRepository _investigationRepository;
    private readonly AuditFinalizationRepository _finalizationRepository;
    private readonly Mock<IAuditTrailService> _mockAuditTrail;
    private readonly InvestigationService _investigationService;
    private readonly TallyReadOnlyPolicy _readOnlyPolicy;

    public TallyReadOnlyPolicyTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"readonly_test_{Guid.NewGuid():N}.db");
        _factory = new SqliteConnectionFactory(_testDbPath);
        _initializer = new DatabaseInitializer(_factory, NullLogger<DatabaseInitializer>.Instance, _testDbPath);
        _auditRepository = new AuditRepository(_factory);
        _investigationRepository = new InvestigationRepository(_factory, NullLogger<InvestigationRepository>.Instance);
        _finalizationRepository = new AuditFinalizationRepository(_factory);
        _mockAuditTrail = new Mock<IAuditTrailService>();
        _mockAuditTrail.Setup(a => a.GetAuditTrailAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(Array.Empty<AuditTrailEntry>());

        _investigationService = new InvestigationService(
            _investigationRepository,
            _auditRepository,
            _mockAuditTrail.Object,
            _finalizationRepository,
            NullLogger<InvestigationService>.Instance
        );

        _readOnlyPolicy = new TallyReadOnlyPolicy();
    }

    public async Task InitializeAsync()
    {
        await _initializer.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (File.Exists(_testDbPath)) File.Delete(_testDbPath);
        }
        catch { }
        await Task.CompletedTask;
    }

    [Fact]
    public void TallyReadOnlyPolicy_EnforcesStrictReadOnlyGuarantees()
    {
        Assert.True(_readOnlyPolicy.IsReadOnly);
        Assert.False(_readOnlyPolicy.AllowsWriting);
        Assert.False(_readOnlyPolicy.CanWriteAccountingData);
        Assert.True(_readOnlyPolicy.CanSynchronize);
        Assert.True(_readOnlyPolicy.CanQuery);
        Assert.True(_readOnlyPolicy.CanRead);
        Assert.Contains("read-only", _readOnlyPolicy.PolicyDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not modify TallyPrime", _readOnlyPolicy.PolicyDescription, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TallyReadOnlyPolicy_AssertCanWrite_AlwaysThrowsInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => _readOnlyPolicy.AssertCanWrite());
        Assert.Contains("read-only", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TallyRequestBuilder_OnlyBuildsReadAndExportRequests()
    {
        var builder = new TallyRequestBuilder();

        var pingXml = builder.BuildPingRequest();
        Assert.Contains("<TALLYREQUEST>Export</TALLYREQUEST>", pingXml);
        Assert.DoesNotContain("<TALLYREQUEST>Import</TALLYREQUEST>", pingXml);

        var compListXml = builder.BuildCompanyListRequest();
        Assert.Contains("<TALLYREQUEST>Export</TALLYREQUEST>", compListXml);
        Assert.DoesNotContain("<TALLYREQUEST>Import</TALLYREQUEST>", compListXml);

        var voucherXml = builder.BuildVoucherCollectionRequest("Test Co", new DateTime(2025, 4, 1), new DateTime(2026, 3, 31));
        Assert.Contains("<TALLYREQUEST>Export</TALLYREQUEST>", voucherXml);
        Assert.DoesNotContain("<TALLYREQUEST>Import</TALLYREQUEST>", voucherXml);
        Assert.DoesNotContain("ACTION=\"Create\"", voucherXml);
        Assert.DoesNotContain("ACTION=\"Alter\"", voucherXml);
        Assert.DoesNotContain("ACTION=\"Delete\"", voucherXml);

        var ledgerXml = builder.BuildLedgerCollectionRequest("Test Co");
        Assert.Contains("<TALLYREQUEST>Export</TALLYREQUEST>", ledgerXml);
        Assert.DoesNotContain("<TALLYREQUEST>Import</TALLYREQUEST>", ledgerXml);
    }

    [Fact]
    public async Task InvestigationResolution_OperatesStrictlyInLocalAuditDatabase_WithoutWritingToTally()
    {
        const string compId = "COMP-RO-01";
        const string excId = "EXC-RO-01";

        await _auditRepository.SaveCompanyAsync(new Company
        {
            Id = compId,
            TallyCompanyName = $"Read-Only Co {compId}",
            BooksFromDate = new DateTime(2025, 4, 1)
        });

        using (var conn = await _factory.CreateConnectionAsync())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                INSERT OR IGNORE INTO AuditRules (RuleId, Category, Name, Description, Severity, SuggestedReview, Version, IsEnabled)
                VALUES ('ACC-RO-01', 0, 'General Review', 'Review only', 2, 'Inspect', '1.0.0', 1);

                INSERT OR REPLACE INTO Exceptions (
                    Id, CompanyId, RuleId, RuleName, Category, Severity, VoucherNumber, FlaggedAmount, Status, EvidenceJson
                ) VALUES (
                    $ExceptionId, $CompanyId, 'ACC-RO-01', 'General Review', 0, 2, 'V-5001', 12000.0, 0, '{}'
                );";
            cmd.Parameters.AddWithValue("$ExceptionId", excId);
            cmd.Parameters.AddWithValue("$CompanyId", compId);
            await cmd.ExecuteNonQueryAsync();
        }

        var inv = await _investigationService.GetOrCreateInvestigationAsync(excId, compId, "AuditorUser");
        await _investigationService.TransitionStatusAsync(inv.Id, InvestigationStatus.Investigating, "AuditorUser");
        await _investigationService.TransitionStatusAsync(inv.Id, InvestigationStatus.Resolved, "AuditorUser", "Auditor confirmed accounting entry is justified.");

        var resolvedInv = await _investigationService.GetInvestigationByIdAsync(inv.Id);
        Assert.NotNull(resolvedInv);
        Assert.Equal(InvestigationStatus.Resolved, resolvedInv.Status);
        Assert.NotNull(resolvedInv.ClosedAt);

        var exceptions = await _auditRepository.GetExceptionsAsync(compId);
        var resolvedExc = exceptions.FirstOrDefault(e => e.Id == excId);
        Assert.NotNull(resolvedExc);
        Assert.Equal(ReviewStatus.Resolved, resolvedExc.Status);
    }
}
