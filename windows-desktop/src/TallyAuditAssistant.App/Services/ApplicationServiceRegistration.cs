using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using TallyAuditAssistant.App.ViewModels;
using TallyAuditAssistant.App.Views;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Core.Services;
using TallyAuditAssistant.Data;
using TallyAuditAssistant.Data.Repositories;
using TallyAuditAssistant.Engine;
using TallyAuditAssistant.Engine.Services;
using TallyAuditAssistant.TallyIntegration;
using TallyAuditAssistant.TallyIntegration.Mocks;

namespace TallyAuditAssistant.App.Services;

public static class ApplicationServiceRegistration
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration,
        string? dbPath = null)
    {
        dbPath ??= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "audit_assistant_data.db");
        Log.Information("Configuring SQLite database path at: {DbPath}", dbPath);

        // Data layer registrations
        var sqliteFactory = new SqliteConnectionFactory(dbPath);
        services.AddSingleton(sqliteFactory);
        services.AddSingleton<ISqliteConnectionFactory>(sqliteFactory);
        services.AddSingleton<IDatabaseInitializer>(sp => new DatabaseInitializer(
            sp.GetRequiredService<SqliteConnectionFactory>(),
            sp.GetRequiredService<ILogger<DatabaseInitializer>>(),
            dbPath));

        services.AddSingleton<IAuditRepository, AuditRepository>();
        services.AddSingleton<ISyncRepository, SyncRepository>();
        services.AddSingleton<ISettingsService, SettingsRepository>();
        services.AddSingleton<IAuditFinalizationRepository, AuditFinalizationRepository>();
        services.AddSingleton<IAuditFinalizationService, AuditFinalizationService>();
        services.AddSingleton<IAuditQualityControlService, AuditQualityControlService>();
        services.AddSingleton<IAuditTrailRepository, AuditTrailRepository>();
        services.AddSingleton<IAuditTrailService, AuditTrailService>();
        services.AddSingleton<IInvestigationRepository, InvestigationRepository>();
        services.AddSingleton<IInvestigationService, InvestigationService>();

        // Tally Integration registrations (strictly read-only policy)
        services.AddSingleton<ITallyReadOnlyPolicy, TallyReadOnlyPolicy>();
        services.AddSingleton<ITallyRequestBuilder, TallyRequestBuilder>();
        services.AddSingleton<ITallyResponseParser, TallyResponseParser>();
        services.AddHttpClient<TallyClient>();

        // Dynamic, runtime-switchable Tally services (no restart required)
        services.AddSingleton<MockTallyClient>();
        services.AddSingleton<MockTallyCompanyService>();
        services.AddSingleton<TallyCompanyService>();

        services.AddSingleton<ITallyClient, DynamicTallyClient>();
        services.AddSingleton<ITallyCompanyService, DynamicTallyCompanyService>();
        services.AddSingleton<IActiveCompanyContext, ActiveCompanyContext>();

        services.AddSingleton<ITallyMasterService, TallyMasterService>();
        services.AddSingleton<ITallyVoucherService, TallyVoucherService>();
        services.AddSingleton<ITallyQueryService, TallyQueryService>();
        services.AddSingleton<ITallyConnection, TallyConnection>();
        services.AddSingleton<TallyConnectionMonitor>();
        services.AddSingleton<ISyncManager, SyncManager>();

        // Core Audit Engine & All 19 Rules
        services.AddAuditEngine();

        // Central Navigation Service
        services.AddSingleton<INavigationService, NavigationService>();

        // ViewModels
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<TallyConnectionViewModel>();
        services.AddSingleton<SyncViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<CompaniesViewModel>();
        services.AddSingleton<GstAuditViewModel>();
        services.AddSingleton<TdsAuditViewModel>();
        services.AddSingleton<VouchersViewModel>();
        services.AddSingleton<LedgersViewModel>();
        services.AddSingleton<BankAuditViewModel>();
        services.AddSingleton<InvestigationViewModel>();
        services.AddSingleton<ExceptionsViewModel>();
        services.AddSingleton<ReportsViewModel>();

        // Views
        services.AddSingleton<MainWindow>();

        return services;
    }
}
