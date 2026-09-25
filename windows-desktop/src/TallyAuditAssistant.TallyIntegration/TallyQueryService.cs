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
