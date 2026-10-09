using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
    private int _diagnosticDialogShown;
    private bool _scheduledAuditMode;

    protected override async void OnStartup(StartupEventArgs e)
    {
        var scheduledAuditMode = e.Args.Any(arg => string.Equals(arg, "--scheduled-audit", StringComparison.OrdinalIgnoreCase));
        _scheduledAuditMode = scheduledAuditMode;
        const string mutexName = "Local\\TallyAuditAssistant.SingleInstance";
        _singleInstanceMutex = new Mutex(true, mutexName, out bool isOnlyInstance);
        if (!isOnlyInstance)
        {
            if (!scheduledAuditMode)
            {
                MessageBox.Show("Tally Audit Assistant is already running.", "Tally Audit Assistant", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            Shutdown(scheduledAuditMode ? 2 : 0);
            return;
        }

        base.OnStartup(e);

        // Global diagnostic boundary. Capture the FIRST UI exception once so a
        // rapid chain of secondary exceptions cannot produce many popups.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

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

            var dbInitializer = _host.Services.GetRequiredService<IDatabaseInitializer>();
            await dbInitializer.InitializeAsync();

            var companyContext = _host.Services.GetRequiredService<IActiveCompanyContext>();
            await companyContext.EnsureAndInitializeActiveCompanyAsync();

            var monitor = _host.Services.GetRequiredService<TallyConnectionMonitor>();
            monitor.StartMonitoring(normalIntervalSeconds: 15, backoffIntervalSeconds: 30);

            if (scheduledAuditMode)
            {
                try
                {
                    var automation = _host.Services.GetRequiredService<IAuditAutomationService>();
                    TallyAuditAssistant.Core.Domain.Audit.AuditAutomationResult? result = null;

                    // Retry the safe workflow after temporary server/network failures.
                    for (var attempt = 1; attempt <= 3; attempt++)
                    {
                        result = await automation.RunAsync(runIncrementalSync: true, runFullAudit: true);
                        if (result.IsSuccess)
                        {
                            break;
                        }

                        var reason = result.ErrorMessage ?? string.Empty;
                        var retryable = reason.Contains("connection", StringComparison.OrdinalIgnoreCase)
                            || reason.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                            || reason.Contains("timed out", StringComparison.OrdinalIgnoreCase)
                            || reason.Contains("temporar", StringComparison.OrdinalIgnoreCase)
                            || reason.Contains("HTTP 5", StringComparison.OrdinalIgnoreCase);

                        if (!retryable || attempt == 3)
                        {
                            break;
                        }

                        Log.Warning("Scheduled audit attempt {Attempt}/3 failed transiently; retrying in 30 seconds: {Reason}", attempt, reason);
                        await Task.Delay(TimeSpan.FromSeconds(30));
                    }

                    if (result?.IsSuccess == true)
                    {
                        var reportPack = _host.Services.GetRequiredService<AuditReportPackViewModel>();
                        await reportPack.GeneratePackAsync();
                        if (reportPack.IsError)
                        {
                            Log.Error("Scheduled audit completed, but report pack generation failed: {Reason}", reportPack.StatusMessage);
                            RecordScheduledRun(result, "Failed", "Report pack generation failed: " + reportPack.StatusMessage);
                            Shutdown(1);
                            return;
                        }

                        RecordScheduledRun(result, "Success", null);
                        Log.Information(
                            "Scheduled audit completed for {Company}. Records synchronized: {Records}. Findings: {Findings}.",
                            result.CompanyName,
                            result.RecordsSynchronized,
                            result.FindingsGenerated);
                        Shutdown(0);
                    }
                    else
                    {
                        var reason = result?.ErrorMessage ?? "No result returned.";
                        RecordScheduledRun(result, "Failed", reason);
                        Log.Error("Scheduled audit failed after retries: {Reason}", reason);
                        Shutdown(1);
                    }
                }
                catch (Exception scheduledException)
                {
                    WriteDiagnostic("SCHEDULED AUDIT FAILURE", scheduledException);
                    RecordScheduledRun(null, "Failed", scheduledException.Message);
                    Log.Error(scheduledException, "Scheduled audit execution failed.");
                    Shutdown(1);
                }

                return;
            }

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            WriteDiagnostic("STARTUP FAILURE", ex);
            Log.Fatal(ex, "Application startup failed critically.");
            if (!scheduledAuditMode)
            {
                MessageBox.Show($"Application could not start:\n{ex.Message}\n\nDiagnostic file:\n{GetDiagnosticPath()}",
                    "Fatal Startup Error", MessageBoxButton.OK, MessageBoxImage.Stop);
            }
            Shutdown(1);
        }
    }

    private static void RecordScheduledRun(
        TallyAuditAssistant.Core.Domain.Audit.AuditAutomationResult? result,
        string status,
        string? details)
    {
        try
        {
            new AutomationRunHistoryService().RecordRun(new AutomationRunHistoryEntry(
                DateTime.Now,
                result?.CompanyName ?? "Unknown company",
                "Scheduled",
                status,
                result?.RecordsSynchronized ?? 0,
                result?.FindingsGenerated ?? 0,
                result?.Duration.TotalSeconds ?? 0,
                details));
        }
        catch (Exception ex)
        {
            // A local history write must not prevent scheduled task shutdown/reporting.
            Log.Warning(ex, "Could not persist scheduled automation history.");
        }
    }

    private void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs args)
    {
        WriteDiagnostic("UI THREAD UNHANDLED EXCEPTION", args.Exception);
        Log.Error(args.Exception, "Unhandled UI Thread Exception");

        // Suppress duplicate popup storms. The first exception is the useful one.
        if (!_scheduledAuditMode && Interlocked.Exchange(ref _diagnosticDialogShown, 1) == 0)
        {
            MessageBox.Show(
                "Tally Audit Assistant captured an unexpected error.\n\n" +
                "The application will remain open where possible.\n\n" +
                $"Diagnostic file:\n{GetDiagnosticPath()}\n\n" +
                "Please send that file for diagnosis.",
                "Tally Audit Assistant — Diagnostic Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        args.Handled = true;
    }

    private void OnAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception ex)
        {
            WriteDiagnostic("APPDOMAIN UNHANDLED EXCEPTION", ex);
            Log.Fatal(ex, "Fatal AppDomain Unhandled Exception");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        WriteDiagnostic("UNOBSERVED TASK EXCEPTION", args.Exception);
        Log.Error(args.Exception, "Unobserved Task Exception");
        args.SetObserved();
    }

    private static string GetDiagnosticPath()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "TallyAuditAssistant");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "CrashDiagnostics.txt");
    }

    private static void WriteDiagnostic(string category, Exception exception)
    {
        try
        {
            var path = GetDiagnosticPath();
            var text =
                "TALLY AUDIT ASSISTANT CRASH DIAGNOSTIC\r\n" +
                "================================\r\n" +
                $"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\r\n" +
                $"Category: {category}\r\n" +
                $"Application: {typeof(App).Assembly.GetName().Version}\r\n" +
                $"OS: {Environment.OSVersion}\r\n" +
                $"64-bit OS: {Environment.Is64BitOperatingSystem}\r\n" +
                $"64-bit Process: {Environment.Is64BitProcess}\r\n\r\n" +
                "Exception:\r\n" +
                exception + "\r\n\r\n";

            File.AppendAllText(path, text);
        }
        catch
        {
            // Diagnostic logging must never become another source of failure.
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
