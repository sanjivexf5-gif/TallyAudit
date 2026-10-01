using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Settings.Configuration;
using TallyAuditAssistant.App.Services;
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

namespace TallyAuditAssistant.App;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        const string mutexName = "Local\\TallyAuditAssistant.SingleInstance";
        _singleInstanceMutex = new Mutex(true, mutexName, out bool isOnlyInstance);
        if (!isOnlyInstance)
        {
            MessageBox.Show("Tally Audit Assistant is already running.", "Tally Audit Assistant", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

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
            var pathService = new ApplicationDataPathService();

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
                            Path.Combine(pathService.LogsDirectory, "audit_assistant_.log"),
                            rollingInterval: RollingInterval.Day,
                            retainedFileCountLimit: 30);
                })
                .ConfigureServices((context, services) =>
                {
                    ConfigureServices(context.Configuration, services);
                })
                .Build();

            await _host.StartAsync();

            Log.Information("DATABASE PATH: {DbPath}", pathService.DatabasePath);

            // Initialize SQLite Database schema & seeds
            var dbInitializer = _host.Services.GetRequiredService<IDatabaseInitializer>();
            await dbInitializer.InitializeAsync();

            // Initialize active company context
            var companyContext = _host.Services.GetRequiredService<IActiveCompanyContext>();
            await companyContext.EnsureAndInitializeActiveCompanyAsync();

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

    internal static void ConfigureServices(IConfiguration configuration, IServiceCollection services)
    {
        services.AddApplicationServices(configuration);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        Log.CloseAndFlush();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
