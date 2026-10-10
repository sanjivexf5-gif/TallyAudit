using System;
using System.Globalization;
using System.IO;
using TallyAuditAssistant.App.Services;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class RiskDashboardCsvExporterTests
{
    private static RiskDashboardCsvSummary Summary(string companyName = "Example Company") =>
        new(
            companyName,
            new DateTime(2026, 10, 10, 13, 44, 5),
            65,
            "HIGH",
            123,
            120,
            1,
            2,
            3,
            4,
            70,
            50,
            12,
            22,
            18,
            45,
            8,
            3,
            5,
            60);

    private static RiskDashboardCsvFinding Finding(
        string ruleName = "Unusual journal entry",
        string category = "GeneralAccounting",
        string severity = "High",
        string status = "Pending",
        string voucher = "JV-104",
        string ledger = "Suspense Ledger") =>
        new(ruleName, category, severity, status, voucher, ledger);

    [Fact]
    public void ToCsv_WritesSummaryMetricsAndTopFindings()
    {
        var csv = RiskDashboardCsvExporter.ToCsv(Summary(), new[] { Finding() });

        Assert.StartsWith(
            "Record Type,Company,Exported At,Metric,Value,Finding,Category,Severity,Status,Voucher Number,Ledger\r\n",
            csv);
        Assert.Contains("Summary,Example Company,2026-10-10 13:44:05,Risk Score,65", csv);
        Assert.Contains("Summary,Example Company,2026-10-10 13:44:05,Findings Analyzed,120", csv);
        Assert.Contains("Summary,Example Company,2026-10-10 13:44:05,Total Findings,123", csv);
        Assert.Contains("Top Finding,Example Company,2026-10-10 13:44:05,Rank,1,Unusual journal entry,GeneralAccounting,High,Pending,JV-104,Suspense Ledger", csv);
    }

    [Fact]
    public void ToCsv_EscapesCommasQuotesAndNewLines()
    {
        var csv = RiskDashboardCsvExporter.ToCsv(
            Summary("Example, \"West\""),
            new[] { Finding(ruleName: "First line\r\nSecond, \"quoted\" line", voucher: "V,1") });

        Assert.Contains("\"Example, \"\"West\"\"\"", csv);
        Assert.Contains("\"First line\r\nSecond, \"\"quoted\"\" line\"", csv);
        Assert.Contains(",\"V,1\",", csv);
    }

    [Fact]
    public void ToCsv_ProtectsFormulaLikeCompanyAndFindingValues()
    {
        var csv = RiskDashboardCsvExporter.ToCsv(
            Summary("=HYPERLINK(\"https://example.invalid\",\"click\")"),
            new[] { Finding(ruleName: "+cmd|' /C calc'!A0") });

        Assert.Contains("\"'=HYPERLINK(\"\"https://example.invalid\"\"", csv);
        Assert.Contains("'+cmd|' /C calc'!A0", csv);
        Assert.DoesNotContain(",=HYPERLINK(", csv);
    }

    [Fact]
    public void ToCsv_UsesInvariantDateAndNumberFormatting()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var csv = RiskDashboardCsvExporter.ToCsv(Summary(), Array.Empty<RiskDashboardCsvFinding>());
            Assert.Contains("2026-10-10 13:44:05,Risk Score,65", csv);
            Assert.Contains("2026-10-10 13:44:05,Checklist Percent,60", csv);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void ToCsv_WithNoTopFindingsStillExportsSummary()
    {
        var csv = RiskDashboardCsvExporter.ToCsv(Summary(), Array.Empty<RiskDashboardCsvFinding>());

        Assert.Contains("Total Findings,123", csv);
        Assert.DoesNotContain("Top Finding", csv);
        Assert.EndsWith("\r\n", csv);
    }

    [Fact]
    public void WriteToFile_CreatesUtf8CsvWithBom()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"RiskDashboardCsv-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "risk.csv");

        try
        {
            RiskDashboardCsvExporter.WriteToFile(path, Summary(), new[] { Finding() });
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
