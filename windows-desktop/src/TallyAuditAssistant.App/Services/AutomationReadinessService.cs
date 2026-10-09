using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.App.Services;

/// <summary>
/// Runs lightweight prerequisite checks before starting the automated audit pipeline.
/// TallyPrime is only probed; the service does not post or modify accounting data.
/// </summary>
public sealed class AutomationReadinessService
{
    private readonly ITallyConnection _tallyConnection;
    private readonly IActiveCompanyContext _companyContext;
    private readonly IApplicationDataPathService _dataPaths;

    public AutomationReadinessService(
        ITallyConnection tallyConnection,
        IActiveCompanyContext companyContext,
        IApplicationDataPathService dataPaths)
    {
        _tallyConnection = tallyConnection;
        _companyContext = companyContext;
        _dataPaths = dataPaths;
    }

    public async Task<AutomationReadinessResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<(bool Passed, string Message)>();

        TallyEndpointInfo? endpoint = null;
        string? connectionError = null;
        var activeEndpoint = _tallyConnection.ActiveEndpoint;

        if (activeEndpoint != null)
        {
            try
            {
                var probe = await _tallyConnection.TestConnectionDetailedAsync(
                    activeEndpoint.Host,
                    activeEndpoint.Port,
                    cancellationToken);
                if (probe.IsResponsive)
                {
                    endpoint = probe;
                }
                else
                {
                    connectionError = probe.ErrorMessage;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                connectionError = ex.Message;
            }
        }

        if (endpoint?.IsResponsive != true)
        {
            try
            {
                endpoint = await _tallyConnection.DiscoverTallyAsync(
                    preferredHost: activeEndpoint?.Host ?? "localhost",
                    preferredPort: activeEndpoint?.Port ?? 9000,
                    scanRangeMax: 9100,
                    cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                connectionError = ex.Message;
            }
        }

        var isConnected = endpoint?.IsResponsive == true;
        var connectionMessage = isConnected
            ? $"TallyPrime is responding at {endpoint!.Host}:{endpoint.Port}."
            : "TallyPrime is not responding. Open TallyPrime and enable its HTTP server."
                + (string.IsNullOrWhiteSpace(endpoint?.ErrorMessage ?? connectionError ?? _tallyConnection.LastErrorMessage)
                    ? string.Empty
                    : $" Details: {endpoint?.ErrorMessage ?? connectionError ?? _tallyConnection.LastErrorMessage}");
        checks.Add((isConnected, connectionMessage));

        Company? company = null;
        string? companyError = null;
        try
        {
            company = await _companyContext.GetActiveCompanyAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            companyError = ex.Message;
        }

        var hasCompany = company != null && !string.IsNullOrWhiteSpace(company.TallyCompanyName);
        checks.Add((hasCompany, hasCompany
            ? $"Active company is selected: {company!.TallyCompanyName}."
            : "No active TallyPrime company is selected."
                + (string.IsNullOrWhiteSpace(companyError) ? string.Empty : $" Details: {companyError}")));

        if (company == null)
        {
            checks.Add((false, "Audit period cannot be validated until a company is selected."));
        }
        else
        {
            try
            {
                var period = await _companyContext.GetActivePeriodAsync(cancellationToken);
                var fromDate = period?.StartDate ?? company.BooksFromDate;
                var toDate = period?.EndDate ?? fromDate.AddYears(1).AddDays(-1);
                var validPeriod = fromDate != default
                    && toDate != default
                    && fromDate <= toDate;

                var periodMessage = validPeriod
                    ? period == null
                        ? $"Audit date range is usable ({fromDate:dd MMM yyyy} to {toDate:dd MMM yyyy}); using the company's books-from date because no explicit period is selected."
                        : $"Audit period is valid ({fromDate:dd MMM yyyy} to {toDate:dd MMM yyyy})."
                    : $"Audit period is invalid ({fromDate:dd MMM yyyy} to {toDate:dd MMM yyyy}). Select a valid start and end date.";
                checks.Add((validPeriod, periodMessage));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                checks.Add((false, $"Audit period could not be read. Details: {ex.Message}"));
            }
        }

        string? temporaryFile = null;
        try
        {
            var dataDirectory = _dataPaths.ApplicationDataDirectory;
            if (string.IsNullOrWhiteSpace(dataDirectory))
            {
                throw new InvalidOperationException("The application data directory is not configured.");
            }

            Directory.CreateDirectory(dataDirectory);
            temporaryFile = Path.Combine(dataDirectory, $".automation-readiness-{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(temporaryFile, "readiness-check", cancellationToken);
            File.Delete(temporaryFile);
            temporaryFile = null;
            checks.Add((true, "Local application storage is writable."));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            checks.Add((false, $"Local application storage is not writable. Details: {ex.Message}"));
        }
        finally
        {
            if (temporaryFile != null)
            {
                try { File.Delete(temporaryFile); } catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        var ready = checks.All(check => check.Passed);
        var summary = ready
            ? "READY — all automation prerequisites passed."
            : "NOT READY — review the failed checks before starting automation.";
        var detail = string.Join(
            Environment.NewLine,
            checks.Select(check => $"{(check.Passed ? "[PASS]" : "[FAIL]")} {check.Message}"));

        return new AutomationReadinessResult(ready, summary + Environment.NewLine + detail);
    }
}

public sealed record AutomationReadinessResult(bool IsReady, string Report);
