using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TallyAuditAssistant.App.Services;
using TallyAuditAssistant.App.ViewModels;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Core.Services;
using TallyAuditAssistant.Data;
using TallyAuditAssistant.Data.Repositories;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class CompanyActivationPersistenceRaceTests
{
    private string GetTempDbPath() => Path.Combine(Path.GetTempPath(), $"race_test_{Guid.NewGuid():N}.db");

    [Fact]
    public async Task CommitCompany_MustNotBeClearedByConcurrentStartupOrDiscovery()
    {
        var dbPath = GetTempDbPath();
        var config = new ConfigurationBuilder().Build();

        var services = new ServiceCollection();
        services.AddApplicationServices(config, dbPath);
        using var provider = services.BuildServiceProvider();

        var init = provider.GetRequiredService<IDatabaseInitializer>();
        await init.InitializeAsync();

        var context = provider.GetRequiredService<IActiveCompanyContext>();
        var connVM = provider.GetRequiredService<TallyConnectionViewModel>();
        var settings = provider.GetRequiredService<ISettingsService>();

        // 1. User commits company
        connVM.SelectedCompany = "Sanjiv Sinha Pvt Ltd";
        var commitTask = connVM.SelectAndSaveCompanyCommand.ExecuteAsync(null);

        // 2. Concurrent background tasks
        var startupTask = context.EnsureAndInitializeActiveCompanyAsync();
        var scanTask = connVM.ScanForTallyCommand.ExecuteAsync(null);

        await Task.WhenAll(commitTask, startupTask, scanTask);

        // 3. Verify company remains persisted across all 3 checkpoints
        Assert.Equal("Sanjiv Sinha Pvt Ltd", context.ActiveCompanyName);
        Assert.Equal("Sanjiv Sinha Pvt Ltd", connVM.ActiveCompany);

        var persistedName = await settings.GetSettingAsync("ActiveCompany");
        Assert.Equal("Sanjiv Sinha Pvt Ltd", persistedName);
    }

    [Fact]
    public async Task DisablingMockMode_MustNotClearRealActiveCompany()
    {
        var dbPath = GetTempDbPath();
        var config = new ConfigurationBuilder().Build();

        var services = new ServiceCollection();
        services.AddApplicationServices(config, dbPath);
        using var provider = services.BuildServiceProvider();

        var init = provider.GetRequiredService<IDatabaseInitializer>();
        await init.InitializeAsync();

        var context = provider.GetRequiredService<IActiveCompanyContext>();
        var connVM = provider.GetRequiredService<TallyConnectionViewModel>();
        var settingsVM = provider.GetRequiredService<SettingsViewModel>();
        var settingsRepo = provider.GetRequiredService<ISettingsService>();

        // 1. Commit real company
        connVM.SelectedCompany = "Sanjiv Sinha Pvt Ltd";
        await connVM.SelectAndSaveCompanyCommand.ExecuteAsync(null);

        // 2. User toggles settings and saves
        settingsVM.IsMockMode = false;
        await settingsVM.SaveSettingsCommand.ExecuteAsync(null);

        // 3. Real company must stay active
        Assert.Equal("Sanjiv Sinha Pvt Ltd", context.ActiveCompanyName);
        var persistedName = await settingsRepo.GetSettingAsync("ActiveCompany");
        Assert.Equal("Sanjiv Sinha Pvt Ltd", persistedName);
    }

    [Fact]
    public async Task TemporaryDiscoveryFailure_MustNotClearCommittedCompany()
    {
        var dbPath = GetTempDbPath();
        var config = new ConfigurationBuilder().Build();

        var services = new ServiceCollection();
        services.AddApplicationServices(config, dbPath);
        using var provider = services.BuildServiceProvider();

        var init = provider.GetRequiredService<IDatabaseInitializer>();
        await init.InitializeAsync();

        var context = provider.GetRequiredService<IActiveCompanyContext>();
        var connVM = provider.GetRequiredService<TallyConnectionViewModel>();
        var settingsRepo = provider.GetRequiredService<ISettingsService>();

        // 1. Commit company
        connVM.SelectedCompany = "Sanjiv Sinha Pvt Ltd";
        await connVM.SelectAndSaveCompanyCommand.ExecuteAsync(null);

        // 2. Tally discovery fails / Tally offline
        await connVM.ScanForTallyCommand.ExecuteAsync(null);

        // 3. Active company remains intact
        Assert.Equal("Sanjiv Sinha Pvt Ltd", context.ActiveCompanyName);
        Assert.Equal("Sanjiv Sinha Pvt Ltd", connVM.ActiveCompany);
        var persistedName = await settingsRepo.GetSettingAsync("ActiveCompany");
        Assert.Equal("Sanjiv Sinha Pvt Ltd", persistedName);
    }

    [Fact]
    public async Task Restart_MustRestoreCommittedCompany()
    {
        var dbPath = GetTempDbPath();
        var config = new ConfigurationBuilder().Build();

        // Session 1: Commit company
        {
            var services1 = new ServiceCollection();
            services1.AddApplicationServices(config, dbPath);
            using var provider1 = services1.BuildServiceProvider();

            var init1 = provider1.GetRequiredService<IDatabaseInitializer>();
            await init1.InitializeAsync();

            var connVM1 = provider1.GetRequiredService<TallyConnectionViewModel>();
            connVM1.SelectedCompany = "Sanjiv Sinha Pvt Ltd";
            await connVM1.SelectAndSaveCompanyCommand.ExecuteAsync(null);
        }

        // Session 2: Application restart with new DI container
        {
            var services2 = new ServiceCollection();
            services2.AddApplicationServices(config, dbPath);
            using var provider2 = services2.BuildServiceProvider();

            var init2 = provider2.GetRequiredService<IDatabaseInitializer>();
            await init2.InitializeAsync();

            var context2 = provider2.GetRequiredService<IActiveCompanyContext>();
            await context2.EnsureAndInitializeActiveCompanyAsync();

            var mainVM2 = provider2.GetRequiredService<MainWindowViewModel>();
            var syncVM2 = provider2.GetRequiredService<SyncViewModel>();

            Assert.Equal("Sanjiv Sinha Pvt Ltd", context2.ActiveCompanyName);
            Assert.Equal("Sanjiv Sinha Pvt Ltd", mainVM2.ActiveCompany);

            await syncVM2.OnNavigatedToAsync();
            Assert.Equal("Sanjiv Sinha Pvt Ltd", syncVM2.CompanyName);
        }
    }

    [Fact]
    public async Task MultiCompany_SelectionAndRestart_RestoresLatestCompany()
    {
        var dbPath = GetTempDbPath();
        var config = new ConfigurationBuilder().Build();

        var services1 = new ServiceCollection();
        services1.AddApplicationServices(config, dbPath);
        using var provider1 = services1.BuildServiceProvider();

        var init1 = provider1.GetRequiredService<IDatabaseInitializer>();
        await init1.InitializeAsync();

        var connVM1 = provider1.GetRequiredService<TallyConnectionViewModel>();

        // 1. Commit Company A
        connVM1.SelectedCompany = "Company A";
        await connVM1.SelectAndSaveCompanyCommand.ExecuteAsync(null);

        // 2. Commit Company B
        connVM1.SelectedCompany = "Company B";
        await connVM1.SelectAndSaveCompanyCommand.ExecuteAsync(null);

        // 3. Restart app
        var services2 = new ServiceCollection();
        services2.AddApplicationServices(config, dbPath);
        using var provider2 = services2.BuildServiceProvider();

        var init2 = provider2.GetRequiredService<IDatabaseInitializer>();
        await init2.InitializeAsync();

        var context2 = provider2.GetRequiredService<IActiveCompanyContext>();
        await context2.EnsureAndInitializeActiveCompanyAsync();

        Assert.Equal("Company B", context2.ActiveCompanyName);
    }
}
