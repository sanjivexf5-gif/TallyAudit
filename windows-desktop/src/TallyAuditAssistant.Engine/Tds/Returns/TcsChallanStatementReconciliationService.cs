using System;
using System.Collections.Generic;
using System.Linq;
using TallyAuditAssistant.Engine.Tds.Reconciliation;

namespace TallyAuditAssistant.Engine.Tds.Returns;

public sealed record TcsChallanAllocationLine(
    string Tan,
    string BuyerPan,
    string FinancialYear,
    int Quarter,
    string Section,
    string BsrCode,
    DateOnly ChallanDate,
    string ChallanSerialNumber,
    decimal TcsAmount);

public sealed record TcsChallanReconciliationFinding(
    string ChallanReference,
    string Section,
    string FinancialYear,
    int Quarter,
    decimal ChallanTcsAmount,
    decimal StatementAllocatedTcsAmount,
    string Status,
    string Explanation,
    string AuditorAction);

/// <summary>
/// Reconciles TCS challan tax amounts with normalized quarterly-statement allocations.
/// This is an audit workpaper only; it does not validate portal filing or communicate with TRACES.
/// </summary>
public sealed class TcsChallanStatementReconciliationService
{
    private readonly TdsChallanReturnReconciliationService _reconciliation;

    public TcsChallanStatementReconciliationService(TdsChallanReturnReconciliationService? reconciliation = null)
        => _reconciliation = reconciliation ?? new TdsChallanReturnReconciliationService();

    public IReadOnlyList<TcsChallanReconciliationFinding> Reconcile(
        IEnumerable<TdsChallanRecord> challans,
        IEnumerable<TcsChallanAllocationLine> statementLines)
    {
        ArgumentNullException.ThrowIfNull(challans);
        ArgumentNullException.ThrowIfNull(statementLines);

        var mappedLines = statementLines.Select(line => line is null
            ? throw new ArgumentException("TCS statement allocations cannot contain null rows.", nameof(statementLines))
            : new TdsReturnLine(
                line.Tan,
                line.BuyerPan,
                line.FinancialYear,
                line.Quarter,
                line.Section,
                line.BsrCode,
                line.ChallanDate,
                line.ChallanSerialNumber,
                line.TcsAmount));

        return _reconciliation.Reconcile(challans, mappedLines)
            .Select(x => new TcsChallanReconciliationFinding(
                x.Reference,
                x.Section,
                x.FinancialYear,
                x.Quarter,
                x.ChallanTaxAmount,
                x.StatementAllocatedAmount,
                x.Status,
                x.Explanation,
                x.AuditorAction))
            .ToArray();
    }
}
