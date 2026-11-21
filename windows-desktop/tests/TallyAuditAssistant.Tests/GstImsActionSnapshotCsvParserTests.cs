using TallyAuditAssistant.Engine.Gst;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class GstImsActionSnapshotCsvParserTests
{
    [Fact]
    public void Parse_MapsCanonicalHeadersAndQuotedValues()
    {
        const string csv = "Supplier GSTIN,Invoice Number,Invoice Date,Invoice Value,IMS Action,Document Type,Reference ID\r\n" +
            "27AAACB2222D1Z9,INV-1,10-05-2026,118000,Accepted,Invoice,REF-1\r\n" +
            "27AAACB2222D1Z9,\"INV, 2\",11-05-2026,59000,No Action,Credit Note,REF-2";
        var rows = GstImsActionSnapshotCsvParser.Parse(csv);
        Assert.Equal(2, rows.Count);
        Assert.Equal("INV, 2", rows[1].InvoiceNumber);
        Assert.Equal(GstImsSupplierAction.NoAction, rows[1].Action);
        Assert.Equal("REF-1", rows[0].ReferenceId);
        Assert.Equal(new DateTime(2026, 5, 10), rows[0].InvoiceDate);
    }

    [Theory]
    [InlineData("Accepted", GstImsSupplierAction.Accepted)]
    [InlineData("Rejected", GstImsSupplierAction.Rejected)]
    [InlineData("Pending", GstImsSupplierAction.Pending)]
    [InlineData("No Action", GstImsSupplierAction.NoAction)]
    [InlineData("Deemed Accepted", GstImsSupplierAction.DeemedAccepted)]
    [InlineData("Unexpected Portal Label", GstImsSupplierAction.Unknown)]
    public void Parse_MapsKnownActionsAndPreservesUnknown(string label, GstImsSupplierAction expected)
    {
        var csv = $"GSTIN,Invoice Number,Invoice Value,Action\n27AAACB2222D1Z9,INV-1,100,{label}";
        Assert.Equal(expected, Assert.Single(GstImsActionSnapshotCsvParser.Parse(csv)).Action);
    }

    [Fact]
    public void Parse_RejectsMissingRequiredColumns()
    {
        var error = Assert.Throws<InvalidDataException>(() =>
            GstImsActionSnapshotCsvParser.Parse("GSTIN,Invoice Number,Invoice Value\n27AAACB2222D1Z9,INV-1,100"));
        Assert.Contains("Required IMS CSV columns", error.Message);
    }

    [Theory]
    [InlineData("GSTIN,Invoice Number,Invoice Value,Action\n27AAACB2222D1Z9,INV-1,abc,Accepted")]
    [InlineData("GSTIN,Invoice Number,Invoice Value,Action\n27AAACB2222D1Z9,INV-1,100,Accepted\"bad")]
    [InlineData("GSTIN,Invoice Number,Invoice Value,Action\n27AAACB2222D1Z9,INV-1,100,\"Accepted")]
    public void Parse_RejectsMalformedRows(string csv)
    {
        Assert.Throws<InvalidDataException>(() => GstImsActionSnapshotCsvParser.Parse(csv));
    }
}
