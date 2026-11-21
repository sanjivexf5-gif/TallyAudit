using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TallyAuditAssistant.Engine.Tds.Returns;

public sealed record TcsBuyerCertificate(string CertificateNumber, string BuyerPan, string Form,
    DateOnly IssuedOn, decimal CollectionAmount, decimal TcsCollected);

public sealed record TcsBuyerCertificateFinding(string Code, string Severity, string BuyerPan,
    string? CertificateNumber, decimal StatementCollectionAmount, decimal StatementTcsAmount,
    decimal CertificateCollectionAmount, decimal CertificateTcsAmount, string Message, string AuditorAction);

/// <summary>Buyer-wise certificate reconciliation workpaper. Does not generate, sign, upload or issue certificates.</summary>
public sealed class TcsBuyerCertificateReconciliationService
{
    private const decimal AmountTolerance = 0.01m;
    private static readonly DateOnly NewActStart = new(2026, 4, 1);
    private static readonly Regex PanPattern = new("^[A-Z]{5}[0-9]{4}[A-Z]$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly TcsComplianceDeadlineService _deadlines;

    public TcsBuyerCertificateReconciliationService(TcsComplianceDeadlineService? deadlines = null)
        => _deadlines = deadlines ?? new TcsComplianceDeadlineService();

    public IReadOnlyList<TcsBuyerCertificateFinding> Reconcile(TcsQuarterlyReturn statement,
        IEnumerable<TcsBuyerCertificate> certificates)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ArgumentNullException.ThrowIfNull(statement.Lines);
        ArgumentNullException.ThrowIfNull(certificates);
        if (statement.FinancialYearStartYear is < 1900 or > 9998 || statement.Quarter is < 1 or > 4)
            throw new ArgumentException("A valid financial year and quarter are required.", nameof(statement));

        var expectedStart = QuarterStart(statement.FinancialYearStartYear, statement.Quarter);
        var expectedEnd = expectedStart.AddMonths(3).AddDays(-1);
        if (statement.PeriodStart != expectedStart || statement.PeriodEnd != expectedEnd)
            throw new ArgumentException("Statement period does not match the selected financial-year quarter.", nameof(statement));

        var expectedForm = expectedEnd < NewActStart ? "FORM 27D" : "FORM 133";
        var certificateDue = _deadlines.GetQuarterlyDeadlines(statement.FinancialYearStartYear)
            .Single(x => x.Code == "TCS-CERTIFICATE" && x.PeriodStart == expectedStart).DueDate;
        var lines = statement.Lines.ToArray();
        var certificateList = certificates.ToArray();
        if (certificateList.Any(x => x is null))
            throw new ArgumentException("Certificate collection cannot contain null rows.", nameof(certificates));

        var findings = new List<TcsBuyerCertificateFinding>();
        var statementByBuyer = lines.GroupBy(x => Normalize(x.BuyerPan), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (Collection: g.Sum(x => x.SaleAmount), Tcs: g.Sum(x => x.TcsAmount)), StringComparer.Ordinal);
        var certificateGroups = certificateList.GroupBy(x => Normalize(x.BuyerPan), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var duplicateNumbers = certificateList.Where(x => !string.IsNullOrWhiteSpace(x.CertificateNumber))
            .GroupBy(x => Normalize(x.CertificateNumber), StringComparer.Ordinal).Where(g => g.Count() > 1)
            .Select(g => g.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var buyer in statementByBuyer.Keys.Union(certificateGroups.Keys, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
        {
            var statementAmounts = statementByBuyer.GetValueOrDefault(buyer, (Collection: 0m, Tcs: 0m));
            var matching = certificateGroups.GetValueOrDefault(buyer) ?? Array.Empty<TcsBuyerCertificate>();
            var certificate = matching.Length == 1 ? matching[0] : null;
            var certCollection = matching.Sum(x => x.CollectionAmount);
            var certTcs = matching.Sum(x => x.TcsCollected);
            var certNumber = certificate?.CertificateNumber;

            if (!PanPattern.IsMatch(buyer))
            {
                Add(findings, "INVALID_BUYER_PAN", "Error", buyer, certNumber, statementAmounts.Collection, statementAmounts.Tcs,
                    certCollection, certTcs, "Buyer PAN is missing or structurally invalid in the quarterly statement or certificate data.",
                    "Verify buyer identity and PAN against authoritative records; do not infer or auto-correct PAN.");
                continue;
            }
            if (matching.Length == 0)
            {
                Add(findings, "MISSING_CERTIFICATE", "Error", buyer, null, statementAmounts.Collection, statementAmounts.Tcs, 0m, 0m,
                    "The quarterly statement contains TCS collections for this buyer but no certificate record was supplied.",
                    $"Prepare and issue the applicable {expectedForm} after validating the filed statement and certificate particulars.");
                continue;
            }
            if (matching.Length > 1)
            {
                Add(findings, "MULTIPLE_BUYER_CERTIFICATES", "Error", buyer, null, statementAmounts.Collection, statementAmounts.Tcs,
                    certCollection, certTcs, "More than one certificate record exists for the same buyer and quarter.",
                    "Check whether these are duplicate exports or a valid replacement/correction; retain the authoritative certificate only.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(certificate!.CertificateNumber))
                Add(findings, "MISSING_CERTIFICATE_NUMBER", "Error", buyer, null, statementAmounts.Collection, statementAmounts.Tcs,
                    certCollection, certTcs, "Certificate number or traceable certificate reference is missing.",
                    "Obtain the issued certificate reference and retain its evidence.");
            else if (duplicateNumbers.Contains(Normalize(certificate.CertificateNumber)))
                Add(findings, "DUPLICATE_CERTIFICATE_NUMBER", "Error", buyer, certificate.CertificateNumber,
                    statementAmounts.Collection, statementAmounts.Tcs, certCollection, certTcs,
                    "The certificate number is repeated in the supplied certificate records.",
                    "Verify the source export and investigate duplicate certificate records.");

            if (!string.Equals(Normalize(certificate.Form), expectedForm, StringComparison.Ordinal))
                Add(findings, "CERTIFICATE_FORM_MISMATCH", "Error", buyer, certificate.CertificateNumber,
                    statementAmounts.Collection, statementAmounts.Tcs, certCollection, certTcs,
                    $"This quarter requires {expectedForm}; the supplied certificate form is {Normalize(certificate.Form)}.",
                    $"Use {expectedForm} for the governing Act and quarter.");
            if (certificate.IssuedOn > certificateDue)
                Add(findings, "LATE_CERTIFICATE", "Warning", buyer, certificate.CertificateNumber,
                    statementAmounts.Collection, statementAmounts.Tcs, certCollection, certTcs,
                    $"Certificate issue date {certificate.IssuedOn:dd MMM yyyy} is after the standard due date {certificateDue:dd MMM yyyy}.",
                    "Check for any CBDT extension or applicable relief and retain supporting evidence.");
            if (certificate.CollectionAmount < 0m || certificate.TcsCollected < 0m)
            {
                Add(findings, "NEGATIVE_CERTIFICATE_AMOUNT", "Error", buyer, certificate.CertificateNumber,
                    statementAmounts.Collection, statementAmounts.Tcs, certCollection, certTcs,
                    "Certificate collection and TCS amounts cannot be negative.", "Verify the certificate export and correct the source data.");
                continue;
            }
            if (Math.Abs(statementAmounts.Collection - certificate.CollectionAmount) > AmountTolerance ||
                Math.Abs(statementAmounts.Tcs - certificate.TcsCollected) > AmountTolerance)
                Add(findings, "CERTIFICATE_AMOUNT_MISMATCH", "Error", buyer, certificate.CertificateNumber,
                    statementAmounts.Collection, statementAmounts.Tcs, certCollection, certTcs,
                    "Buyer-wise collection amount or TCS amount differs between the quarterly statement and certificate.",
                    "Reconcile buyer-wise gross collections and TCS against the filed statement and certificate; investigate amendments and reversals.");
            else if (!findings.Any(x => x.BuyerPan == buyer))
                Add(findings, "MATCHED", "Info", buyer, certificate.CertificateNumber,
                    statementAmounts.Collection, statementAmounts.Tcs, certCollection, certTcs,
                    "Buyer-wise collection and TCS amounts agree with the certificate.",
                    "Retain the issued certificate and delivery evidence with the quarterly workpaper.");
        }
        return findings;
    }

    private static DateOnly QuarterStart(int year, int quarter) => quarter switch
    {
        1 => new DateOnly(year, 4, 1), 2 => new DateOnly(year, 7, 1),
        3 => new DateOnly(year, 10, 1), 4 => new DateOnly(year + 1, 1, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(quarter))
    };
    private static string Normalize(string? value) => (value ?? string.Empty).Trim().Trim('\uFEFF').ToUpperInvariant();
    private static void Add(ICollection<TcsBuyerCertificateFinding> findings, string code, string severity,
        string pan, string? certificateNumber, decimal statementCollection, decimal statementTcs,
        decimal certificateCollection, decimal certificateTcs, string message, string action)
        => findings.Add(new TcsBuyerCertificateFinding(code, severity, pan, certificateNumber,
            statementCollection, statementTcs, certificateCollection, certificateTcs, message, action));
}
