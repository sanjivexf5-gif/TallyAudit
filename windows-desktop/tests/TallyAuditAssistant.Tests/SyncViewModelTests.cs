using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Domain.Sync;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.TallyIntegration.Mocks;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class SyncViewModelTests
{
    private class DummySyncManager : ISyncManager
    {
        public SyncStatus CurrentStatus { get; set; } = SyncStatus.Idle;
        public SyncMetrics CurrentMetrics { get; set; } = new SyncMetrics();

        public event EventHandler<SyncMetrics>? ProgressChanged;
        public event EventHandler<string>? SyncLogEmitted;

        public Task<SyncResult> StartSyncAsync(string companyName, SyncMode mode, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SyncResult(true, mode, 150, 100, 50, 0, 0, TimeSpan.FromSeconds(2)));
        }

        public Task PauseAsync() => Task.CompletedTask;
        public Task ResumeAsync() => Task.CompletedTask;
        public Task CancelAsync() => Task.CompletedTask;

        public Task<SyncResult> RetryAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SyncResult(true, SyncMode.Full, 150, 100, 50, 0, 0, TimeSpan.FromSeconds(2)));
        }

        public Task<IReadOnlyList<SyncHistoryRecord>> GetSyncHistoryAsync(string companyId, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<SyncHistoryRecord> list = new[]
            {
                new SyncHistoryRecord { CompanyId = companyId, CompanyName = companyId, VouchersFetched = 100 }
            };
            return Task.FromResult(list);
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

    private class MockContext : IActiveCompanyContext
    {
        public string? ActiveCompanyName => "Demo Industrial Solutions Pvt Ltd (FY 2025-26)";
        public string? ActiveCompanyId => "COMP-01";
        public Company? CurrentCompany => new Company
        {
            Id = "COMP-01",
            TallyCompanyName = "Demo Industrial Solutions Pvt Ltd (FY 2025-26)",
            BooksFromDate = new DateTime(2025, 4, 1)
        };

        public event EventHandler<Company?>? ActiveCompanyChanged;

        public Task<Company?> GetActiveCompanyAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Company?>(CurrentCompany);
        }

        public Task SetActiveCompanyAsync(Company company, CancellationToken cancellationToken = default)
        {
            ActiveCompanyChanged?.Invoke(this, company);
            return Task.CompletedTask;
        }

        public Task SetActiveCompanyNameAsync(string companyName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<Company?> EnsureAndInitializeActiveCompanyAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Company?>(CurrentCompany);
        }
    }

    [Fact]
    public void Commands_State_Validation()
    {
        var dummySync = new DummySyncManager();
        var companyService = new MockTallyCompanyService();
        var settingsService = new MockSettingsService();
        var context = new MockContext();

        var vm = new App.ViewModels.SyncViewModel(dummySync, companyService, settingsService, context);

        Assert.False(vm.IsSyncing);
        Assert.False(vm.IsPaused);

        // Verify initial state
        Assert.True(vm.StartFullSyncCommand.CanExecute(null));
        Assert.True(vm.StartIncrementalSyncCommand.CanExecute(null));
        Assert.True(vm.RetrySyncCommand.CanExecute(null));
    }

    [Fact]
    public async Task StartFullSync_ExecutesSyncManager_AndNotifiesContext()
    {
        var dummySync = new DummySyncManager();
        var companyService = new MockTallyCompanyService();
        var settingsService = new MockSettingsService();
        var context = new MockContext();

        var vm = new App.ViewModels.SyncViewModel(dummySync, companyService, settingsService, context);
        vm.CompanyName = "Demo Industrial Solutions Pvt Ltd (FY 2025-26)";

        await vm.StartFullSyncCommand.ExecuteAsync(null);

        Assert.False(vm.IsSyncing);
        Assert.Contains("completed successfully", vm.CurrentTaskDescription);
    }
}
