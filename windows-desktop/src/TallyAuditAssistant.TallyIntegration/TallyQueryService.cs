using System.Security;
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
        ArgumentException.ThrowIfNullOrWhiteSpace(reportName);

        var sb = new StringBuilder();
        sb.AppendLine("<ENVELOPE>");
        sb.AppendLine("  <HEADER>");
        sb.AppendLine("    <VERSION>1</VERSION>");
        sb.AppendLine("    <TALLYREQUEST>Export</TALLYREQUEST>");
        sb.AppendLine("    <TYPE>Data</TYPE>");
        sb.Append("    <ID>").Append(EscapeXmlText(reportName)).AppendLine("</ID>");
        sb.AppendLine("  </HEADER>");
        sb.AppendLine("  <BODY>");
        sb.AppendLine("    <DESC>");
        sb.AppendLine("      <STATICVARIABLES>");
        sb.AppendLine("        <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>");
        if (staticVariables != null)
        {
            foreach (var kvp in staticVariables)
            {
                ValidateElementName(kvp.Key);
                sb.Append("        <").Append(kvp.Key).Append('>')
                    .Append(EscapeXmlText(kvp.Value)).Append("</").Append(kvp.Key).AppendLine(">");
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
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);

        var sb = new StringBuilder();
        sb.AppendLine("<ENVELOPE>");
        sb.AppendLine("  <HEADER>");
        sb.AppendLine("    <VERSION>1</VERSION>");
        sb.AppendLine("    <TALLYREQUEST>Export</TALLYREQUEST>");
        sb.AppendLine("    <TYPE>Collection</TYPE>");
        sb.Append("    <ID>").Append(EscapeXmlText(collectionName)).AppendLine("</ID>");
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
            sb.Append("          <COLLECTION NAME=\"").Append(EscapeXmlAttribute(collectionName)).AppendLine("\">");
            sb.Append("            <FETCH>")
                .Append(string.Join(", ", fetchFields.Select(EscapeXmlText)))
                .AppendLine("</FETCH>");
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

    private static void ValidateElementName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        try
        {
            XmlConvert.VerifyNCName(name);
        }
        catch (XmlException ex)
        {
            throw new ArgumentException($"'{name}' is not a valid XML element name.", nameof(name), ex);
        }
    }

    private static string EscapeXmlText(string? value)
    {
        if (value == null) return string.Empty;
        XmlConvert.VerifyXmlChars(value);
        return SecurityElement.Escape(value) ?? string.Empty;
    }

    private static string EscapeXmlAttribute(string? value) => EscapeXmlText(value);
}
