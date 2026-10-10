using System;
using System.Globalization;
using System.IO;
using TallyAuditAssistant.App.Services;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class AutomationRunHistoryCsvExporterTests
{
    private static AutomationRunHistoryEntry Entry(
        string company = "Example Company",
        string trigger = "Manual",
        string status = "Failed",
        int records = 125,
        int findings = 7,
        double duration = 12.5,
        string? details = "Temporary connection failure.") =>
        new(
            new DateTime(2026, 10, 10, 9, 8, 7),
            company,
            trigger,
            status,
            records,
            findings,
            duration,
            details);

    [Fact]
    public void ToCsv_WritesHeaderAndRunValues()
    {
        var csv = AutomationRunHistoryCsvExporter.ToCsv(new[] { Entry() });

        Assert.StartsWith(
            "Started At,Company,Trigger,Status,Records Synchronized,Findings Generated,Duration Seconds,Details\r\n",
            csv);
        Assert.Contains("2026-10-10 09:08:07,Example Company,Manual,Failed,125,7,12.5,Temporary connection failure.", csv);
    }

    [Fact]
    public void ToCsv_EscapesCommasQuotesAndNewLines()
    {
        var csv = AutomationRunHistoryCsvExporter.ToCsv(new[]
        {
            Entry(company: "Ledger, \"West\"", details: "First line\r\nSecond \"quoted\" line")
        });

        Assert.Contains("\"Ledger, \"\"West\"\"\"", csv);
        Assert.Contains("\"First line\r\nSecond \"\"quoted\"\" line\"", csv);
    }

    [Fact]
    public void ToCsv_PrefixesFormulaLikeTextToAvoidSpreadsheetFormulaExecution()
    {
        var csv = AutomationRunHistoryCsvExporter.ToCsv(new[]
        {
            Entry(company: "=HYPERLINK(\"https://example.invalid\",\"click\")")
        });

        Assert.Contains("\"'=HYPERLINK(\"\"https://example.invalid\"", csv);
        Assert.DoesNotContain(",=HYPERLINK(", csv);
    }

    [Fact]
    public void ToCsv_UsesInvariantNumberFormatting()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var csv = AutomationRunHistoryCsvExporter.ToCsv(new[] { Entry(duration: 12.5) });
            Assert.Contains(",125,7,12.5,Temporary connection failure.", csv);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void ToCsv_HandlesEmptyHistory()
    {
        var csv = AutomationRunHistoryCsvExporter.ToCsv(Array.Empty<AutomationRunHistoryEntry>());
        Assert.EndsWith("\r\n", csv);
        Assert.Equal(1, csv.Split("\r\n", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void WriteToFile_CreatesUtf8Csv()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"AutomationHistoryCsv-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "runs.csv");

        try
        {
            AutomationRunHistoryCsvExporter.WriteToFile(path, new[] { Entry() });
            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length > 3);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
            Assert.Contains("Example Company", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
