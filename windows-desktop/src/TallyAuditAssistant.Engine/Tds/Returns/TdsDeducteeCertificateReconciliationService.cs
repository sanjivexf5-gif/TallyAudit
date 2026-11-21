using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TallyAuditAssistant.Engine.Tds.Returns;

public sealed record TdsDeducteeCertificate(
    string CertificateNumber,
    string DeducteePan,
    string FormNumber,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly IssuedOn,
    decimal AmountPaidOrCredited,
    decimal TaxDeducted);

public sealed record TdsDeducteeCertificateFinding(
    string Code,
    string Severity,
    string DeducteePan,
    string? CertificateNumber,
    decimal StatementAmount,
    decimal StatementTax,
    decimal CertificateAmount,
    decimal CertificateTax,
    string Message,
    string AuditorAction);

/// <summary>
/// Reconciles standard quarterly non-salary TDS certificates to an accepted statement workpaper.
/// Supports old-Act Form 16A and new-Act Form 131; salary certificates and
/// transaction-specific challan-cum-statements require separate period rules.
/// This is an audit workpaper and never generates, signs, uploads or issues certificates.
/// </summary>
public sealed class TdsDeducteeCertificateReconciliationService
{
    private const decimal AmountTolerance = 0.01m;
    private static readonly Regex PanPattern = new(
        "^[A-Z]{5}[0-9]{4}[A-Z]$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly TdsCertificateDeadlineService _deadlines;

    public TdsDeducteeCertificateReconciliationService(TdsCertificateDeadlineService? deadlines = null)
        => _deadlines = deadlines ?? new TdsCertificateDeadlineService();

    public IReadOnlyList<TdsDeducteeCertificateFinding> Reconcile(
        TdsStatementWorkpaper statement,
        IEnumerable<TdsDeducteeCertificate> certificates)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ArgumentNullException.ThrowIfNull(statement.Lines);
        ArgumentNullException.ThrowIfNull(certificates);

        if (statement.StatementKind is not (TdsStatementKind.NonSalaryResident or TdsStatementKind.NonSalaryNonResident))
            throw new ArgumentException(
                "This reconciliation supports quarterly non-salary TDS statements only; salary and TCS certificates use separate rules.",
                nameof(statement));

        var periodStart = statement.Period.PeriodStart;
        var periodEnd = statement.Period.PeriodEnd;
        var deadline = _deadlines.ResolveNonSalaryQuarter(periodStart, periodEnd);
        var expectedForm = deadline.FormNumber == "16A" ? "16A" : "131";
        var certificateRows = certificates.ToArray();
        if (certificateRows.Any(x => x is null))
            throw new ArgumentException("Certificate collection cannot contain null rows.", nameof(certificates));

        var findings = new List<TdsDeducteeCertificateFinding>();
        var statementByPan = statement.Lines
            .Where(x => x.TaxDeducted > 0m)
            .GroupBy(x => Normalize(x.DeducteePan), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (
                Amount: g.Sum(x => x.AmountPaidOrCredited),
                Tax: g.Sum(x => x.TaxDeducted)), StringComparer.Ordinal);
        var certificatesByPan = certificateRows
            .GroupBy(x => Normalize(x.DeducteePan), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);

        foreach (var duplicate in certificateRows
            .Where(x => !string.IsNullOrWhiteSpace(x.CertificateNumber))
            .GroupBy(x => Normalize(x.CertificateNumber), StringComparer.Ordinal)
            .Where(g => g.Count() > 1))
        {
            foreach (var certificate in duplicate)
            {
                var pan = Normalize(certificate.DeducteePan);
                var amounts = statementByPan.GetValueOrDefault(pan);
                var matching = certificatesByPan.GetValueOrDefault(pan) ?? Array.Empty<TdsDeducteeCertificate>();
                Add(findings, "DUPLICATE_CERTIFICATE_NUMBER", "Error", pan, certificate.CertificateNumber,
                    amounts.Amount, amounts.Tax, matching.Sum(x => x.AmountPaidOrCredited),
                    matching.Sum(x => x.TaxDeducted),
                    "The same certificate number appears more than once in the supplied certificate records.",
                    "Verify source exports and identify the authoritative certificate; retain evidence of any replacement or correction.");
            }
        }

        foreach (var pan in statementByPan.Keys.Union(certificatesByPan.Keys, StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal))
        {
            var amounts = statementByPan.GetValueOrDefault(pan);
            var matching = certificatesByPan.GetValueOrDefault(pan) ?? Array.Empty<TdsDeducteeCertificate>();
            var certificateAmount = matching.Sum(x => x.AmountPaidOrCredited);
            var certificateTax = matching.Sum(x => x.TaxDeducted);
            var certificate = matching.Length == 1 ? matching[0] : null;

            if (!PanPattern.IsMatch(pan))
            {
                Add(findings, "INVALID_DEDUCTEE_PAN", "Error", pan, certificate?.CertificateNumber,
                    amounts.Amount, amounts.Tax, certificateAmount, certificateTax,
                    "Deductee PAN is missing or does not match the standard PAN structure.",
                    "Verify deductee PAN against authoritative records; do not infer or auto-correct identity data.");
                continue;
            }

            if (matching.Length == 0)
            {
                Add(findings, "MISSING_CERTIFICATE", "Error", pan, null, amounts.Amount, amounts.Tax, 0m, 0m,
                    $"The quarterly statement reports TDS for this deductee but no Form {expectedForm} certificate was supplied.",
                    $"Verify issuance and delivery of Form {expectedForm}; retain the issued certificate and delivery evidence.");
                continue;
            }

            if (matching.Length > 1)
            {
                Add(findings, "MULTIPLE_DEDUCTEE_CERTIFICATES", "Error", pan, null,
                    amounts.Amount, amounts.Tax, certificateAmount, certificateTax,
                    "More than one certificate record exists for the same deductee and quarter.",
                    "Check for duplicate exports, correction or replacement certificates; reconcile to the authoritative certificate.");
                continue;
            }

            var row = certificate!;
            if (string.IsNullOrWhiteSpace(row.CertificateNumber))
                Add(findings, "MISSING_CERTIFICATE_NUMBER", "Error", pan, null, amounts.Amount, amounts.Tax,
                    certificateAmount, certificateTax, "Certificate number or traceable reference is missing.",
                    "Obtain the issued certificate reference and preserve its source evidence.");

            if (!string.Equals(Normalize(row.FormNumber), expectedForm, StringComparison.Ordinal))
                Add(findings, "CERTIFICATE_FORM_MISMATCH", "Error", pan, row.CertificateNumber,
                    amounts.Amount, amounts.Tax, certificateAmount, certificateTax,
                    $"The reporting period requires Form {expectedForm}, but the supplied certificate is Form {Normalize(row.FormNumber)}.",
                    $"Verify the governing Act and use the prescribed Form {expectedForm} for this period.");

            if (row.PeriodStart != periodStart || row.PeriodEnd != periodEnd)
                Add(findings, "CERTIFICATE_PERIOD_MISMATCH", "Error", pan, row.CertificateNumber,
                    amounts.Amount, amounts.Tax, certificateAmount, certificateTax,
                    "Certificate reporting period does not match the quarterly statement workpaper.",
                    "Compare the certificate with the filed quarterly statement and correct the period mapping.");

            if (row.IssuedOn == default || row.IssuedOn < periodEnd)
                Add(findings, "INVALID_CERTIFICATE_ISSUE_DATE", "Error", pan, row.CertificateNumber,
                    amounts.Amount, amounts.Tax, certificateAmount, certificateTax,
                    "Certificate issue date is missing or predates the end of the reporting quarter.",
                    "Verify the actual issue date from the authoritative issued certificate.");
            else if (row.IssuedOn > deadline.DueDate)
                Add(findings, "LATE_CERTIFICATE", "Warning", pan, row.CertificateNumber,
                    amounts.Amount, amounts.Tax, certificateAmount, certificateTax,
                    $"Certificate issue date {row.IssuedOn:dd MMM yyyy} is after the standard due date {deadline.DueDate:dd MMM yyyy}.",
                    "Check for a CBDT extension or other applicable relief and retain supporting evidence.");

            if (row.AmountPaidOrCredited < 0m || row.TaxDeducted < 0m)
            {
                Add(findings, "NEGATIVE_CERTIFICATE_AMOUNT", "Error", pan, row.CertificateNumber,
                    amounts.Amount, amounts.Tax, certificateAmount, certificateTax,
                    "Certificate amount paid or credited and tax deducted cannot be negative.",
                    "Verify the certificate export and correct the source data.");
                continue;
            }

            if (Math.Abs(amounts.Amount - row.AmountPaidOrCredited) > AmountTolerance ||
                Math.Abs(amounts.Tax - row.TaxDeducted) > AmountTolerance)
                Add(findings, "CERTIFICATE_AMOUNT_MISMATCH", "Error", pan, row.CertificateNumber,
                    amounts.Amount, amounts.Tax, certificateAmount, certificateTax,
                    "Deductee-wise amount paid or credited or TDS differs between the quarterly statement and certificate.",
                    "Reconcile gross payments, credits, TDS, reversals and corrections against the filed statement and issued certificate.");
            else if (!findings.Any(x => x.DeducteePan == pan))
                Add(findings, "MATCHED", "Info", pan, row.CertificateNumber,
                    amounts.Amount, amounts.Tax, certificateAmount, certificateTax,
                    "Deductee-wise payment and TDS amounts agree with the certificate.",
                    "Retain the issued certificate and delivery evidence with the quarterly audit workpaper.");
        }

        return findings;
    }

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().Trim('\uFEFF').ToUpperInvariant();

    private static void Add(ICollection<TdsDeducteeCertificateFinding> findings,
        string code, string severity, string pan, string? certificateNumber,
        decimal statementAmount, decimal statementTax, decimal certificateAmount,
        decimal certificateTax, string message, string action)
        => findings.Add(new TdsDeducteeCertificateFinding(code, severity, pan, certificateNumber,
            statementAmount, statementTax, certificateAmount, certificateTax, message, action));
}
