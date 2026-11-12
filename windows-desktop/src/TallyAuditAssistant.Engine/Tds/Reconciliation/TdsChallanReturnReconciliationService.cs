using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TallyAuditAssistant.Engine.Tds.Reconciliation;

public sealed record TdsChallanRecord(
    string Tan, string FinancialYear, int Quarter, string Section,
    string BsrCode, DateOnly ChallanDate, string SerialNumber, decimal TaxAmount);

public sealed record TdsReturnLine(
    string DeducteePan, string FinancialYear, int Quarter, string Section,
    string BsrCode, DateOnly ChallanDate, string ChallanSerialNumber,
    decimal DeductedAmount, string? DeducteeName = null);

public sealed record TdsReconciliationFinding(
    string Reference, string Section, string FinancialYear, int Quarter,
    decimal ChallanTaxAmount, decimal StatementAllocatedAmount,
    string Status, string Explanation, string AuditorAction);

/// <summary>
/// Reconciles normalized challan (CIN) data against normalized TDS return allocation lines.
/// This is a review aid only; it does not validate a filed return or communicate with TRACES.
/// </summary>
public sealed class TdsChallanReturnReconciliationService
{
    private static readonly Regex PanPattern = new("^[A-Z]{5}[0-9]{4}[A-Z]$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public IReadOnlyList<TdsReconciliationFinding> Reconcile(
        IEnumerable<TdsChallanRecord> challans,
        IEnumerable<TdsReturnLine> returnLines)
    {
        ArgumentNullException.ThrowIfNull(challans);
        ArgumentNullException.ThrowIfNull(returnLines);

        var challanList = challans.ToArray();
        var lineList = returnLines.ToArray();
        Validate(challanList, lineList);

        var challanGroups = challanList.GroupBy(Key).ToDictionary(g => g.Key, g => g.ToArray());
        var allocationGroups = lineList.GroupBy(Key).ToDictionary(g => g.Key, g => g.ToArray());
        var keys = challanGroups.Keys.Union(allocationGroups.Keys).OrderBy(k => k.FinancialYear)
            .ThenBy(k => k.Quarter).ThenBy(k => k.Section).ThenBy(k => k.BsrCode)
            .ThenBy(k => k.ChallanDate).ThenBy(k => k.SerialNumber);

        var findings = new List<TdsReconciliationFinding>();
        foreach (var key in keys)
        {
            var matchingChallans = challanGroups.GetValueOrDefault(key) ?? Array.Empty<TdsChallanRecord>();
            var matchingLines = allocationGroups.GetValueOrDefault(key) ?? Array.Empty<TdsReturnLine>();
            var challanAmount = matchingChallans.Sum(x => x.TaxAmount);
            var allocatedAmount = matchingLines.Sum(x => x.DeductedAmount);
            var reference = $"{key.BsrCode}/{key.ChallanDate:ddMMyyyy}/{key.SerialNumber}";
            string status;
            string explanation;
            string action;

            if (matchingChallans.Length == 0)
            {
                status = "Missing challan";
                explanation = $"Return statement allocates {allocatedAmount:N2}, but no matching challan was imported.";
                action = "Obtain the paid challan receipt and verify CIN, TAN, section and period.";
            }
            else if (matchingLines.Length == 0)
            {
                status = "Unallocated challan";
                explanation = $"Challan tax amount {challanAmount:N2} has no matching return allocation.";
                action = "Check whether the challan is pending allocation, belongs to another statement, or was imported incorrectly.";
            }
            else if (matchingChallans.Length > 1)
            {
                status = "Duplicate challan";
                explanation = $"Found {matchingChallans.Length} challan records with the same reconciliation key.";
                action = "Verify the source extract and remove duplicate rows only after checking original challan receipts.";
            }
            else if (matchingLines.Any(x => !PanPattern.IsMatch(Normalize(x.DeducteePan))))
            {
                status = "Invalid deductee PAN";
                explanation = "One or more statement rows linked to this challan contain a missing or structurally invalid PAN.";
                action = "Verify deductee PAN against authoritative records and correct the statement source.";
            }
            else if (Math.Abs(challanAmount - allocatedAmount) > 0.01m)
            {
                status = "Amount mismatch";
                explanation = $"Challan tax amount {challanAmount:N2} differs from statement allocation {allocatedAmount:N2}.";
                action = "Reconcile challan tax amount with the sum allocated in the return; separately verify interest and fee components.";
            }
            else
            {
                status = "Matched";
                explanation = $"Challan tax amount and return allocations agree at {challanAmount:N2}.";
                action = "Retain challan receipt and filed-statement acknowledgement as audit evidence.";
            }

            findings.Add(new TdsReconciliationFinding(reference, key.Section, key.FinancialYear,
                key.Quarter, challanAmount, allocatedAmount, status, explanation, action));
        }

        return findings;
    }

    private static (string FinancialYear, int Quarter, string Section, string BsrCode, DateOnly ChallanDate, string SerialNumber) Key(TdsChallanRecord x)
        => (Normalize(x.FinancialYear), x.Quarter, Normalize(x.Section), Normalize(x.BsrCode), x.ChallanDate, Normalize(x.SerialNumber));

    private static (string FinancialYear, int Quarter, string Section, string BsrCode, DateOnly ChallanDate, string SerialNumber) Key(TdsReturnLine x)
        => (Normalize(x.FinancialYear), x.Quarter, Normalize(x.Section), Normalize(x.BsrCode), x.ChallanDate, Normalize(x.ChallanSerialNumber));

    private static void Validate(IReadOnlyList<TdsChallanRecord> challans, IReadOnlyList<TdsReturnLine> lines)
    {
        foreach (var c in challans)
        {
            if (string.IsNullOrWhiteSpace(c.Tan) || string.IsNullOrWhiteSpace(c.FinancialYear) ||
                string.IsNullOrWhiteSpace(c.Section) || string.IsNullOrWhiteSpace(c.BsrCode) ||
                string.IsNullOrWhiteSpace(c.SerialNumber) || c.Quarter is < 1 or > 4 || c.TaxAmount < 0)
                throw new ArgumentException("Challan records require TAN, financial year, quarter 1-4, section, BSR code, date, serial number and a non-negative tax amount.");
        }

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line.FinancialYear) || string.IsNullOrWhiteSpace(line.Section) ||
                string.IsNullOrWhiteSpace(line.BsrCode) || string.IsNullOrWhiteSpace(line.ChallanSerialNumber) ||
                line.Quarter is < 1 or > 4 || line.DeductedAmount < 0)
                throw new ArgumentException("Return lines require financial year, quarter 1-4, section, challan CIN and a non-negative deducted amount.");
        }
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();
}
