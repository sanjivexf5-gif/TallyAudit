using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TallyAuditAssistant.Engine.Tds.Returns;

/// <summary>
/// Reconciles annual salary TDS certificates (old Act Form 16 / new Act Form 130)
/// to all four quarterly salary statement workpapers for one financial/tax year.
/// The workpaper does not generate, sign, upload or issue certificates.
/// </summary>
public sealed class TdsSalaryCertificateReconciliationService
{
    private const decimal AmountTolerance = 0.01m;
    private static readonly Regex PanPattern = new(
        "^[A-Z]{5}[0-9]{4}[A-Z]$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly TdsCertificateDeadlineService _deadlines;

    public TdsSalaryCertificateReconciliationService(TdsCertificateDeadlineService? deadlines = null)
        => _deadlines = deadlines ?? new TdsCertificateDeadlineService();

    public IReadOnlyList<TdsDeducteeCertificateFinding> Reconcile(
        IEnumerable<TdsStatementWorkpaper> quarterlyStatements,
        IEnumerable<TdsDeducteeCertificate> certificates)
    {
        ArgumentNullException.ThrowIfNull(quarterlyStatements);
        ArgumentNullException.ThrowIfNull(certificates);

        var statements = quarterlyStatements.ToArray();
        var rows = certificates.ToArray();
        if (statements.Any(x => x is null))
            throw new ArgumentException("Quarterly statement collection cannot contain null workpapers.", nameof(quarterlyStatements));
        if (rows.Any(x => x is null))
            throw new ArgumentException("Certificate collection cannot contain null rows.", nameof(certificates));
        if (statements.Length != 4)
            throw new ArgumentException("Exactly four quarterly salary statement workpapers are required.", nameof(quarterlyStatements));
        if (statements.Any(x => x.StatementKind != TdsStatementKind.Salary))
            throw new ArgumentException("Only salary (Form 24Q / Form 138) statement workpapers are supported.", nameof(quarterlyStatements));

        var ordered = statements.OrderBy(x => x.Period.PeriodStart).ToArray();
        var yearStart = ordered[0].Period.PeriodStart;
        if (yearStart.Month != 4 || yearStart.Day != 1)
            throw new ArgumentException("Annual salary reconciliation must start on 1 April.", nameof(quarterlyStatements));
        var deadline = _deadlines.ResolveSalary(yearStart);
        var yearEnd = deadline.ReportingPeriodEnd;

        for (var i = 0; i < 4; i++)
        {
            var expectedStart = yearStart.AddMonths(i * 3);
            var expectedEnd = expectedStart.AddMonths(3).AddDays(-1);
            var statement = ordered[i];
            if (statement.Period.PeriodStart != expectedStart ||
                statement.Period.PeriodEnd != expectedEnd ||
                statement.Period.Quarter != i + 1)
                throw new ArgumentException("The four salary statements must cover Q1-Q4 consecutively without gaps, overlaps or duplicate quarters.", nameof(quarterlyStatements));

            var expectedStatementForm = deadline.Regime == TdsCertificateRegime.IncomeTaxAct1961 ? "24Q" : "138";
            if (!string.Equals(statement.FormNumber, expectedStatementForm, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"The salary statement for this year must use Form {expectedStatementForm}.", nameof(quarterlyStatements));
        }

        var expectedCertificateForm = deadline.FormNumber;
        var statementByPan = ordered.SelectMany(x => x.Lines ?? Array.Empty<TdsStatementLine>())
            .Where(x => x.TaxDeducted > 0m)
            .GroupBy(x => Normalize(x.DeducteePan), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (
                Amount: g.Sum(x => x.AmountPaidOrCredited),
                Tax: g.Sum(x => x.TaxDeducted)), StringComparer.Ordinal);
        var certificatesByPan = rows.GroupBy(x => Normalize(x.DeducteePan), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var findings = new List<TdsDeducteeCertificateFinding>();

        foreach (var duplicate in rows.Where(x => !string.IsNullOrWhiteSpace(x.CertificateNumber))
            .GroupBy(x => Normalize(x.CertificateNumber), StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            foreach (var row in duplicate)
            {
                var pan = Normalize(row.DeducteePan);
                var amounts = statementByPan.GetValueOrDefault(pan);
                var matching = certificatesByPan.GetValueOrDefault(pan) ?? Array.Empty<TdsDeducteeCertificate>();
                Add(findings, "DUPLICATE_CERTIFICATE_NUMBER", "Error", pan, row.CertificateNumber,
                    amounts.Amount, amounts.Tax, matching.Sum(x => x.AmountPaidOrCredited),
                    matching.Sum(x => x.TaxDeducted),
                    "The same annual salary certificate number appears more than once.",
                    "Verify the authoritative certificate and retain evidence of any correction or replacement.");
            }
        }

        foreach (var pan in statementByPan.Keys.Union(certificatesByPan.Keys, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
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
                    "Verify PAN against authoritative records; do not infer or auto-correct identity data.");
                continue;
            }
            if (!statementByPan.ContainsKey(pan))
            {
                Add(findings, "CERTIFICATE_WITHOUT_STATEMENT", "Error", pan, certificate?.CertificateNumber,
                    0m, 0m, certificateAmount, certificateTax,
                    "An annual salary certificate was supplied for a deductee with no TDS in the four quarterly statement workpapers.",
                    "Verify deductor, tax year, omitted statement lines and any correction statements.");
                continue;
            }
            if (matching.Length == 0)
            {
                Add(findings, "MISSING_CERTIFICATE", "Error", pan, null, amounts.Amount, amounts.Tax, 0m, 0m,
                    $"The annual salary statements report TDS but no Form {expectedCertificateForm} certificate was supplied.",
                    $"Verify issuance and delivery of Form {expectedCertificateForm}; retain the certificate and delivery evidence.");
                continue;
            }
            if (matching.Length > 1)
            {
                Add(findings, "MULTIPLE_DEDUCTEE_CERTIFICATES", "Error", pan, null,
                    amounts.Amount, amounts.Tax, certificateAmount, certificateTax,
                    "More than one annual salary certificate record exists for the same deductee.",
                    "Check duplicate exports, corrections or replacement certificates and identify the authoritative certificate.");
                continue;
            }

            var row = certificate!;
            if (string.IsNullOrWhiteSpace(row.CertificateNumber))
                Add(findings, "MISSING_CERTIFICATE_NUMBER", "Error", pan, null, amounts.Amount, amounts.Tax,
                    certificateAmount, certificateTax, "Annual salary certificate number or traceable reference is missing.",
                    "Obtain the issued certificate reference and preserve its source evidence.");
            if (!string.Equals(Normalize(row.FormNumber), expectedCertificateForm, StringComparison.Ordinal))
                Add(findings, "CERTIFICATE_FORM_MISMATCH", "Error", pan, row.CertificateNumber,
                    amounts.Amount, amounts.Tax, certificateAmount, certificateTax,
                    $"The tax year requires Form {expectedCertificateForm}, but the supplied certificate is Form {Normalize(row.FormNumber)}.",
                    $"Verify the governing Act and use prescribed Form {expectedCertificateForm}.");
            if (row.PeriodStart != yearStart || row.PeriodEnd != yearEnd)
                Add(findings, "CERTIFICATE_PERIOD_MISMATCH", "Error", pan, row.CertificateNumber,
                    amounts.Amount, amounts.Tax, certificateAmount, certificateTax,
                    "Certificate period does not match the complete financial/tax year.",
                    "Compare the certificate with all four quarterly statements and confirm the year mapping.");
            if (row.IssuedOn == default || row.IssuedOn < yearEnd)
                Add(findings, "INVALID_CERTIFICATE_ISSUE_DATE", "Error", pan, row.CertificateNumber,
                    amounts.Amount, amounts.Tax, certificateAmount, certificateTax,
                    "Certificate issue date is missing or predates the end of the financial/tax year.",
                    "Verify the issue date from the authoritative issued certificate.");
            else if (row.IssuedOn > deadline.DueDate)
                Add(findings, "LATE_CERTIFICATE", "Warning", pan, row.CertificateNumber,
                    amounts.Amount, amounts.Tax, certificateAmount, certificateTax,
                    $"Certificate issue date {row.IssuedOn:dd MMM yyyy} is after the standard due date {deadline.DueDate:dd MMM yyyy}.",
                    "Check for a CBDT extension or other applicable relief and retain supporting evidence.");
            if (row.AmountPaidOrCredited < 0m || row.TaxDeducted < 0m)
            {
                Add(findings, "NEGATIVE_CERTIFICATE_AMOUNT", "Error", pan, row.CertificateNumber,
                    amounts.Amount, amounts.Tax, certificateAmount, certificateTax,
                    "Annual salary amount and tax deducted cannot be negative.",
                    "Verify the certificate export and correct the source data.");
                continue;
            }
            if (Math.Abs(amounts.Amount - row.AmountPaidOrCredited) > AmountTolerance ||
                Math.Abs(amounts.Tax - row.TaxDeducted) > AmountTolerance)
                Add(findings, "CERTIFICATE_AMOUNT_MISMATCH", "Error", pan, row.CertificateNumber,
                    amounts.Amount, amounts.Tax, certificateAmount, certificateTax,
                    "Annual salary amount or TDS differs between the four quarterly statements and certificate.",
                    "Reconcile salary, perquisites, exemptions, other income, relief, tax and quarterly TDS against payroll and the filed statements.");
            else if (!findings.Any(x => x.DeducteePan == pan))
                Add(findings, "MATCHED", "Info", pan, row.CertificateNumber,
                    amounts.Amount, amounts.Tax, certificateAmount, certificateTax,
                    "Annual salary and TDS totals agree with the certificate.",
                    "Retain the certificate, payroll reconciliation and delivery evidence with the annual audit workpaper.");
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
