using System.IO;
using System.Reflection;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Settings.Configuration;
using TallyAuditAssistant.App.ViewModels;
using TallyAuditAssistant.App.Views;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;
using TallyAuditAssistant.Data.Repositories;
using TallyAuditAssistant.Engine;
using TallyAuditAssistant.TallyIntegration;
using TallyAuditAssistant.TallyIntegration.Mocks;

namespace TallyAuditAssistant.App;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Setup global unhandled exception handling
        DispatcherUnhandledException += (s, args) =>
        {
            Log.Error(args.Exception, "Unhandled UI Thread Exception");
            MessageBox.Show($"An unexpected error occurred: {args.Exception.Message}\n\nCheck the application logs for details.", 
                            "Tally Audit Assistant Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                Log.Fatal(ex, "Fatal AppDomain Unhandled Exception");
            }
        };

        try
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((context, config) =>
                {
                    config.SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                          .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                })
                .UseSerilog((context, services, configuration) =>
                {
                    var options = new ConfigurationReaderOptions(typeof(FileLoggerConfigurationExtensions).Assembly);
                    configuration
                        .ReadFrom.Configuration(context.Configuration, options)
                        .Enrich.FromLogContext()
                        .WriteTo.File(
                            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "audit_assistant_.log"),
                            rollingInterval: RollingInterval.Day,
                            retainedFileCountLimit: 30);
                })
                .ConfigureServices((context, services) =>
                {
                    ConfigureServices(context.Configuration, services);
                })
                .Build();

            await _host.StartAsync();

            // Initialize SQLite Database schema & seeds
            var dbInitializer = _host.Services.GetRequiredService<IDatabaseInitializer>();
            await dbInitializer.InitializeAsync();

            // Start lightweight background Tally connection monitor
            var monitor = _host.Services.GetRequiredService<TallyConnectionMonitor>();
            monitor.StartMonitoring(normalIntervalSeconds: 15, backoffIntervalSeconds: 30);

            // Display main application shell
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application startup failed critically.");
            MessageBox.Show($"Application could not start:\n{ex.Message}", "Fatal Startup Error", MessageBoxButton.OK, MessageBoxImage.Stop);
            Shutdown(1);
        }
    }

    private static void ConfigureServices(IConfiguration configuration, IServiceCollection services)
    {
        var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "audit_assistant_data.db");
        
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

        // Tally Integration registrations
        services.AddSingleton<ITallyRequestBuilder, TallyRequestBuilder>();
        services.AddSingleton<ITallyResponseParser, TallyResponseParser>();
        services.AddHttpClient<TallyClient>();

        // Dynamic, runtime-switchable Tally services (no restart required)
        services.AddSingleton<MockTallyClient>();
        services.AddSingleton<MockTallyCompanyService>();
        services.AddSingleton<TallyCompanyService>();

        services.AddSingleton<ITallyClient, DynamicTallyClient>();
        services.AddSingleton<ITallyCompanyService, DynamicTallyCompanyService>();

        services.AddSingleton<ITallyMasterService, TallyMasterService>();
        services.AddSingleton<ITallyVoucherService, TallyVoucherService>();
        services.AddSingleton<ITallyQueryService, TallyQueryService>();
        services.AddSingleton<ITallyConnection, TallyConnection>();
        services.AddSingleton<TallyConnectionMonitor>();
        services.AddSingleton<ISyncManager, SyncManager>();

        // Core Audit Engine & All 19 Rules
        services.AddAuditEngine();

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
        services.AddSingleton<ExceptionsViewModel>();
        services.AddSingleton<ReportsViewModel>();

        // Views
        services.AddSingleton<MainWindow>();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
