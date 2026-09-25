using TallyAuditAssistant.Core.Domain.Tally;

namespace TallyAuditAssistant.Core.Interfaces;

public interface ITallyQueryService
{
    string BuildExportEnvelope(string reportName, Dictionary<string, string>? staticVariables = null);
    string BuildCollectionEnvelope(string collectionName, List<string>? fetchFields = null, Dictionary<string, string>? filters = null);
    Task<string> ExecuteExportReportAsync(string endpointUrl, string reportName, Dictionary<string, string>? variables = null, CancellationToken cancellationToken = default);
}

public interface ITallyCompanyService
{
    Task<IReadOnlyList<string>> GetOpenCompaniesAsync(string? endpointUrl = null, CancellationToken cancellationToken = default);
    Task<string?> GetActiveCompanyAsync(string? endpointUrl = null, CancellationToken cancellationToken = default);
    Task<Dictionary<string, string>> GetCompanyProfileAsync(string endpointUrl, string companyName, CancellationToken cancellationToken = default);
    Task<TallyCompanyProfile?> GetCompanyProfileTypedAsync(string companyName, string? endpointUrl = null, CancellationToken cancellationToken = default);
}
