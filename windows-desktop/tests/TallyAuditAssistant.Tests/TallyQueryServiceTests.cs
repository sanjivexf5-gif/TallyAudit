using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.TallyIntegration;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class TallyQueryServiceTests
{
    private readonly TallyQueryService _service = new(new Mock<ITallyClient>().Object,
        NullLogger<TallyQueryService>.Instance);

    [Fact]
    public void BuildExportEnvelope_EscapesReportNameAndStaticVariableValues()
    {
        var xml = _service.BuildExportEnvelope("Report & <Details>", new Dictionary<string, string>
        {
            ["SVCurrentCompany"] = "A & B <Private> Ltd"
        });

        var document = XDocument.Parse(xml);
        Assert.Equal("Report & <Details>", document.Root!.Element("HEADER")!.Element("ID")!.Value);
        Assert.Equal("A & B <Private> Ltd",
            document.Root.Element("BODY")!.Element("DESC")!.Element("STATICVARIABLES")!
                .Element("SVCurrentCompany")!.Value);
    }

    [Fact]
    public void BuildExportEnvelope_RejectsInvalidStaticVariableElementNames()
    {
        Assert.Throws<ArgumentException>(() =>
            _service.BuildExportEnvelope("DayBook", new Dictionary<string, string>
            {
                ["SVBad><INJECT"] = "value"
            }));
    }

    [Fact]
    public void BuildCollectionEnvelope_EscapesCollectionNameAndFetchFields()
    {
        var xml = _service.BuildCollectionEnvelope("Ledger & <Custom>", new List<string>
        {
            "NAME",
            "PARENT & <Injected>"
        });

        var document = XDocument.Parse(xml);
        Assert.Equal("Ledger & <Custom>", document.Root!.Element("HEADER")!.Element("ID")!.Value);
        var collection = document.Descendants("COLLECTION").Single();
        Assert.Equal("Ledger & <Custom>", collection.Attribute("NAME")!.Value);
        Assert.Equal("NAME, PARENT & <Injected>", collection.Element("FETCH")!.Value);
    }

    [Fact]
    public void BuildExportEnvelope_RejectsInvalidXmlCharacters()
    {
        Assert.Throws<XmlException>(() =>
            _service.BuildExportEnvelope("DayBook", new Dictionary<string, string>
            {
                ["SVCurrentCompany"] = "Invalid\u0001Value"
            }));
    }
}
