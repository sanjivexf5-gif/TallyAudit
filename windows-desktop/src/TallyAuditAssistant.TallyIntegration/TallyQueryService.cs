using System.Text;
using System.Xml;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.TallyIntegration;

public class TallyQueryService : ITallyQueryService
{
    private readonly ITallyClient _client;
    private readonly ILogger<TallyQueryService> _logger;

    public TallyQueryService(ITallyClient client, ILogger<TallyQueryService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public string BuildExportEnvelope(string reportName, Dictionary<string, string>? staticVariables = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<ENVELOPE>");
        sb.AppendLine("  <HEADER>");
        sb.AppendLine("    <VERSION>1</VERSION>");
        sb.AppendLine("    <TALLYREQUEST>Export</TALLYREQUEST>");
        sb.AppendLine("    <TYPE>Data</TYPE>");
        sb.AppendFormat("    <ID>{0}</ID>\n", reportName);
        sb.AppendLine("  </HEADER>");
        sb.AppendLine("  <BODY>");
        sb.AppendLine("    <DESC>");
        sb.AppendLine("      <STATICVARIABLES>");
        sb.AppendLine("        <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>");
        if (staticVariables != null)
        {
            foreach (var kvp in staticVariables)
            {
                sb.AppendFormat("        <{0}>{1}</{0}>\n", kvp.Key, kvp.Value);
            }
        }
        sb.AppendLine("      </STATICVARIABLES>");
        sb.AppendLine("    </DESC>");
        sb.AppendLine("  </BODY>");
        sb.AppendLine("</ENVELOPE>");

        return sb.ToString();
    }

    public string BuildCollectionEnvelope(string collectionName, List<string>? fetchFields = null, Dictionary<string, string>? filters = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<ENVELOPE>");
        sb.AppendLine("  <HEADER>");
        sb.AppendLine("    <VERSION>1</VERSION>");
        sb.AppendLine("    <TALLYREQUEST>Export</TALLYREQUEST>");
        sb.AppendLine("    <TYPE>Collection</TYPE>");
        sb.AppendFormat("    <ID>{0}</ID>\n", collectionName);
        sb.AppendLine("  </HEADER>");
        sb.AppendLine("  <BODY>");
        sb.AppendLine("    <DESC>");
        sb.AppendLine("      <STATICVARIABLES>");
        sb.AppendLine("        <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>");
        sb.AppendLine("      </STATICVARIABLES>");
        if (fetchFields != null && fetchFields.Count > 0)
        {
            sb.AppendLine("      <TDL>");
            sb.AppendLine("        <TDLMESSAGE>");
            sb.AppendFormat("          <COLLECTION NAME=\"{0}\">\n", collectionName);
            sb.AppendFormat("            <FETCH>{0}</FETCH>\n", string.Join(", ", fetchFields));
            sb.AppendLine("          </COLLECTION>");
            sb.AppendLine("        </TDLMESSAGE>");
            sb.AppendLine("      </TDL>");
        }
        sb.AppendLine("    </DESC>");
        sb.AppendLine("  </BODY>");
        sb.AppendLine("</ENVELOPE>");

        return sb.ToString();
    }

    public async Task<string> ExecuteExportReportAsync(string endpointUrl, string reportName, Dictionary<string, string>? variables = null, CancellationToken cancellationToken = default)
    {
        var xml = BuildExportEnvelope(reportName, variables);
        return await _client.PostXmlAsync(endpointUrl, xml, cancellationToken);
    }
}

public class TallyCompanyService : ITallyCompanyService
{
    private readonly ITallyQueryService _queryService;
    private readonly ILogger<TallyCompanyService> _logger;

    public TallyCompanyService(ITallyQueryService queryService, ILogger<TallyCompanyService> logger)
    {
        _queryService = queryService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> GetOpenCompaniesAsync(string endpointUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            var rawXml = await _queryService.ExecuteExportReportAsync(endpointUrl, "List of Companies", null, cancellationToken);
            return ParseCompaniesFromXml(rawXml);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve company list from TallyPrime at {Url}", endpointUrl);
            return Array.Empty<string>();
        }
    }

    public async Task<string?> GetActiveCompanyAsync(string endpointUrl, CancellationToken cancellationToken = default)
    {
        var companies = await GetOpenCompaniesAsync(endpointUrl, cancellationToken);
        return companies.Count > 0 ? companies[0] : null;
    }

    public Task<Dictionary<string, string>> GetCompanyProfileAsync(string endpointUrl, string companyName, CancellationToken cancellationToken = default)
    {
        // Real implementation queries company master
        var profile = new Dictionary<string, string>
        {
            { "Name", companyName },
            { "Endpoint", endpointUrl }
        };
        return Task.FromResult(profile);
    }

    private static List<string> ParseCompaniesFromXml(string xmlContent)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(xmlContent)) return results;

        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xmlContent);

            var nodes = doc.SelectNodes("//COMPANY | //NAME | //COMPANYNAME");
            if (nodes != null)
            {
                foreach (XmlNode node in nodes)
                {
                    var text = node.InnerText?.Trim();
                    if (!string.IsNullOrEmpty(text) && !results.Contains(text))
                    {
                        results.Add(text);
                    }
                }
            }
        }
        catch
        {
            // Fallback lightweight regex if XML has unescaped ampersands from Tally
            var matches = System.Text.RegularExpressions.Regex.Matches(xmlContent, @"<NAME[^>]*>([^<]+)</NAME>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var val = match.Groups[1].Value.Trim();
                if (!results.Contains(val)) results.Add(val);
            }
        }

        return results;
    }
}
