using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace TallyAuditAssistant.App.Services;

/// <summary>
/// Exports a local risk dashboard snapshot and the dashboard's displayed top findings.
/// The user chooses the destination; exports are not uploaded automatically.
/// </summary>
public static class RiskDashboardCsvExporter
{
    private const string Header =
        "Record Type,Company,Exported At,Metric,Value,Finding,Category,Severity,Status,Voucher Number,Ledger";

    public static string ToCsv(
        RiskDashboardCsvSummary summary,
        IEnumerable<RiskDashboardCsvFinding> topFindings)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(topFindings);

        var exportedAt = summary.ExportedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var builder = new StringBuilder();
        builder.Append(Header).Append("\r\n");

        AppendMetric(builder, summary, exportedAt, "Risk Score", summary.RiskScore);
        AppendMetric(builder, summary, exportedAt, "Risk Level", summary.RiskLabel);
        AppendMetric(builder, summary, exportedAt, "Total Findings", summary.TotalFindings);
        AppendMetric(builder, summary, exportedAt, "Findings Analyzed", summary.AnalyzedFindings);
        AppendMetric(builder, summary, exportedAt, "Critical Findings", summary.CriticalCount);
        AppendMetric(builder, summary, exportedAt, "High Findings", summary.HighCount);
        AppendMetric(builder, summary, exportedAt, "Medium Findings", summary.MediumCount);
        AppendMetric(builder, summary, exportedAt, "Low Findings", summary.LowCount);
        AppendMetric(builder, summary, exportedAt, "Pending Review", summary.PendingCount);
        AppendMetric(builder, summary, exportedAt, "Reviewed or Resolved", summary.ReviewedCount);
        AppendMetric(builder, summary, exportedAt, "Duplicate Detection", summary.DuplicateCount);
        AppendMetric(builder, summary, exportedAt, "GST Findings", summary.GstCount);
        AppendMetric(builder, summary, exportedAt, "TDS Findings", summary.TdsCount);
        AppendMetric(builder, summary, exportedAt, "Accounting Findings", summary.AccountingCount);
        AppendMetric(builder, summary, exportedAt, "Banking Findings", summary.BankingCount);
        AppendMetric(builder, summary, exportedAt, "Checklist Completed", summary.ChecklistCompleted);
        AppendMetric(builder, summary, exportedAt, "Checklist Applicable", summary.ChecklistApplicable);
        AppendMetric(builder, summary, exportedAt, "Checklist Percent", summary.ChecklistPercent);

        var rank = 0;
        foreach (var finding in topFindings)
        {
            ArgumentNullException.ThrowIfNull(finding);
            rank++;
            AppendRow(builder, new[]
            {
                "Top Finding",
                summary.CompanyName,
                exportedAt,
                "Rank",
                rank.ToString(CultureInfo.InvariantCulture),
                finding.RuleName,
                finding.Category,
                finding.Severity,
                finding.Status,
                finding.VoucherNumber,
                finding.LedgerName
            });
        }

        return builder.ToString();
    }

    public static void WriteToFile(
        string filePath,
        RiskDashboardCsvSummary summary,
        IEnumerable<RiskDashboardCsvFinding> topFindings)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("A destination file path is required.", nameof(filePath));
        }

        var csv = ToCsv(summary, topFindings);
        File.WriteAllText(filePath, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static void AppendMetric(
        StringBuilder builder,
        RiskDashboardCsvSummary summary,
        string exportedAt,
        string metric,
        int value) =>
        AppendMetric(builder, summary, exportedAt, metric, value.ToString(CultureInfo.InvariantCulture));

    private static void AppendMetric(
        StringBuilder builder,
        RiskDashboardCsvSummary summary,
        string exportedAt,
        string metric,
        string value) =>
        AppendRow(builder, new[]
        {
            "Summary",
            summary.CompanyName,
            exportedAt,
            metric,
            value,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty
        });

    private static void AppendRow(StringBuilder builder, IEnumerable<string?> fields) =>
        builder.Append(string.Join(",", fields.Select(EscapeField))).Append("\r\n");

    private static string EscapeField(string? value)
    {
        var normalized = value ?? string.Empty;
        var trimmed = normalized.TrimStart();
        var formulaLike = trimmed.Length > 0 && trimmed[0] is '=' or '+' or '-' or '@';
        var startsWithControl = normalized.Length > 0 && normalized[0] is '\t' or '\r' or '\n';

        // CSV quoting does not stop spreadsheet formula execution; prefix risky text cells.
        if (formulaLike || startsWithControl)
        {
            normalized = "'" + normalized;
        }

        if (normalized.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
        {
            return "\"" + normalized.Replace("\"", "\"\"") + "\"";
        }

        return normalized;
    }
}

public sealed record RiskDashboardCsvSummary(
    string CompanyName,
    DateTime ExportedAt,
    int RiskScore,
    string RiskLabel,
    int TotalFindings,
    int AnalyzedFindings,
    int CriticalCount,
    int HighCount,
    int MediumCount,
    int LowCount,
    int PendingCount,
    int ReviewedCount,
    int DuplicateCount,
    int GstCount,
    int TdsCount,
    int AccountingCount,
    int BankingCount,
    int ChecklistCompleted,
    int ChecklistApplicable,
    int ChecklistPercent);

public sealed record RiskDashboardCsvFinding(
    string RuleName,
    string Category,
    string Severity,
    string Status,
    string VoucherNumber,
    string LedgerName);
