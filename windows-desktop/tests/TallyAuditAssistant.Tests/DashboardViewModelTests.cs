using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Domain.Tally;
using TallyAuditAssistant.Core.Domain.Vouchers;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Core.Services;
using TallyAuditAssistant.Data;
using TallyAuditAssistant.Data.Repositories;
using TallyAuditAssistant.Engine;
using TallyAuditAssistant.TallyIntegration.Mocks;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class DashboardViewModelTests : IAsyncLifetime
{
    private readonly string _testDbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly DatabaseInitializer _initializer;
    private readonly AuditRepository _auditRepo;
    private readonly MockTallyCompanyService _companyService;
    private readonly MockSettingsService _settingsService;
    private readonly ActiveCompanyContext _companyContext;
    private readonly MockTallyConnection _tallyConnection;
    private readonly DummyAuditEngine _auditEngine;

    public DashboardViewModelTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"dashboard_vm_test_{Guid.NewGuid():N}.db");
        _factory = new SqliteConnectionFactory(_testDbPath);
        _initializer = new DatabaseInitializer(_factory, NullLogger<DatabaseInitializer>.Instance, _testDbPath);
        _auditRepo = new AuditRepository(_factory);
        _companyService = new MockTallyCompanyService();
        _settingsService = new MockSettingsService();
        _companyContext = new ActiveCompanyContext(_auditRepo, _settingsService, _companyService);
        _tallyConnection = new MockTallyConnection();
        _auditEngine = new DummyAuditEngine();
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

    private class DummyAuditEngine : IAuditEngine
    {
        public bool ExecuteCalled { get; private set; }

        public IReadOnlyList<IAuditRule> RegisteredRules => new List<IAuditRule>();

        public event EventHandler<AuditEngineProgress>? ProgressChanged;

        public void RegisterRule(IAuditRule rule) { }

        public Task<IReadOnlyList<AuditResult>> ExecuteAuditAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
        {
            ExecuteCalled = true;
            IReadOnlyList<AuditResult> results = new List<AuditResult>
            {
                new AuditResult
                {
                    CompanyId = context.CompanyId,
                    RuleId = "TEST-01",
                    RuleName = "Test GST Rule",
                    Category = RuleCategory.GST,
                    Severity = SeverityLevel.High,
                    VoucherNumber = "VOUCH-101",
                    FlaggedAmount = 5000m,
                    Explanation = "Test finding explanation"
                }
            };
            return Task.FromResult(results);
        }

        public Task<IReadOnlyList<AuditResult>> ExecuteCategoryAsync(RuleCategory category, AuditExecutionContext context, CancellationToken cancellationToken = default)
        {
            return ExecuteAuditAsync(context, cancellationToken);
        }

        public Task<IReadOnlyList<AuditResult>> ExecuteRuleAsync(string ruleId, AuditExecutionContext context, CancellationToken cancellationToken = default)
        {
            return ExecuteAuditAsync(context, cancellationToken);
        }
    }

    private class MockSettingsService : ISettingsService
    {
        public Task<string> GetSettingAsync(string key, string defaultValue = "", CancellationToken cancellationToken = default) => Task.FromResult(defaultValue);
        public Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> GetTallyPortAsync() => Task.FromResult(9000);
        public Task SetTallyPortAsync(int port) => Task.CompletedTask;
        public Task<string> GetTallyHostAsync() => Task.FromResult("localhost");
        public Task SetTallyHostAsync(string host) => Task.CompletedTask;
        public Task<bool> IsMockModeEnabledAsync() => Task.FromResult(true);
        public Task SetMockModeEnabledAsync(bool enabled) => Task.CompletedTask;
    }

    private class MockTallyConnection : ITallyConnection
    {
        public ConnectionStatus CurrentStatus { get; set; } = ConnectionStatus.Connected;
        public TallyEndpointInfo? ActiveEndpoint { get; set; } = new TallyEndpointInfo("localhost", 9000, true, "Mock", "Demo Company", 5);
        public string? LastErrorMessage { get; set; }
        public event EventHandler<ConnectionStatus>? StatusChanged { add { } remove { } }
        public Task<bool> CheckIfProcessRunningAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<TallyEndpointInfo?> ProbePortRangeAsync(string host = "localhost", int startPort = 9000, int endPort = 9005, CancellationToken ct = default) => Task.FromResult<TallyEndpointInfo?>(ActiveEndpoint);
        public Task<bool> TestConnectionAsync(string host, int port, CancellationToken ct = default) => Task.FromResult(true);
    }

    private class MockDrillDownService : ITallyDrillDownService
    {
        public Task<TallyVoucherDrillDownInfo?> GetVoucherDrillDownAsync(string companyId, string voucherId, CancellationToken cancellationToken = default) => Task.FromResult<TallyVoucherDrillDownInfo?>(null);
        public Task<TallyOpenAttemptResult> AttemptOpenInTallyAsync(string companyId, string voucherId, CancellationToken cancellationToken = default) => Task.FromResult(new TallyOpenAttemptResult { IsDirectLaunchSuccess = false });
        public TallyNavigationBreadcrumb GenerateNavigationGuide(string companyName, string voucherNumber, string voucherTypeName, DateTime voucherDate, string? masterId = null) => new TallyNavigationBreadcrumb();
    }

    private class MockAiService : IAuditAssistantService
    {
        public bool IsAiEnabled => false;
        public string CurrentProviderName => "None";
        public Task<string> ExplainFindingAsync(AuditException exception, CancellationToken ct = default) => Task.FromResult("Mock Explanation");
        public Task<string> SuggestReviewQuestionsAsync(AuditException exception, CancellationToken ct = default) => Task.FromResult("Mock Questions");
        public Task<string> DraftWorkingPaperRemarkAsync(AuditException exception, CancellationToken ct = default) => Task.FromResult("Mock Remark");
        public Task<string> ExplainReconciliationAsync(AuditException exception, CancellationToken ct = default) => Task.FromResult("Mock Reconciliation");
        public Task<string> SummarizeAuditRunAsync(int t, int r, int f, int h, int rev, string json, CancellationToken ct = default) => Task.FromResult("Mock Summary");
    }

    [Fact]
    public async Task RunCompleteAuditCommand_WhenNoVouchersExist_DisplaysNoSynchronizedDataMessage()
    {
        var comp = await _companyContext.EnsureAndInitializeActiveCompanyAsync();

        var vm = new App.ViewModels.DashboardViewModel(
            _auditRepo,
            _tallyConnection,
            _auditEngine,
            _settingsService,
            new MockDrillDownService(),
            new MockAiService(),
            _companyContext,
            NullLogger<App.ViewModels.DashboardViewModel>.Instance);

        Assert.True(vm.RunCompleteAuditCommand.CanExecute(null));

        await vm.RunCompleteAuditCommand.ExecuteAsync(null);

        Assert.False(vm.IsAuditing);
        Assert.True(vm.CanRunAudit);
        Assert.Contains("No synchronized accounting data is available", vm.AuditStatusText);
        Assert.False(_auditEngine.ExecuteCalled);
    }

    [Fact]
    public async Task RunCompleteAuditCommand_WhenVouchersExist_ExecutesAuditEngine_AndSavesRun()
    {
        var comp = await _companyContext.EnsureAndInitializeActiveCompanyAsync();

        // Seed sync repository with sample vouchers
        var syncRepo = new SyncRepository(_factory, NullLogger<SyncRepository>.Instance);
        var vouchers = new List<Voucher>
        {
            new Voucher
            {
                Id = "VOUCH-001",
                CompanyId = comp!.Id,
                VoucherNumber = "V-1001",
                VoucherTypeName = "Sales",
                VoucherDate = new DateTime(2025, 5, 10),
                TotalAmount = 50000m
            }
        };
        await syncRepo.BatchUpsertVouchersAsync(vouchers, comp.Id);

        var vm = new App.ViewModels.DashboardViewModel(
            _auditRepo,
            _tallyConnection,
            _auditEngine,
            _settingsService,
            new MockDrillDownService(),
            new MockAiService(),
            _companyContext,
            NullLogger<App.ViewModels.DashboardViewModel>.Instance);

        await vm.RunCompleteAuditCommand.ExecuteAsync(null);

        Assert.False(vm.IsAuditing);
        Assert.True(vm.CanRunAudit);
        Assert.True(_auditEngine.ExecuteCalled);
        Assert.Contains("Audit run complete", vm.AuditStatusText);

        var runs = await _auditRepo.GetAuditRunHistoryAsync(comp.Id);
        Assert.NotEmpty(runs);
        Assert.Equal("Completed", runs[0].Status);
    }
}
