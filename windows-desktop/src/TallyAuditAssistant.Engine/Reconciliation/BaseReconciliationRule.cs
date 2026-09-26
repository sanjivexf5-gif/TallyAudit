using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;

namespace TallyAuditAssistant.Engine.Reconciliation;

public abstract class BaseReconciliationRule : IReconciliationRule
{
    protected readonly SqliteConnectionFactory ConnectionFactory;

    public abstract string RuleId { get; }
    public abstract string RuleCode { get; }
    public abstract string RuleName { get; }
    public abstract RuleCategory Category { get; }
    public abstract string Description { get; }
    public bool IsEnabled { get; set; } = true;

    protected BaseReconciliationRule(SqliteConnectionFactory connectionFactory)
    {
        ConnectionFactory = connectionFactory;
    }

    public abstract Task<IReadOnlyList<AuditResult>> ExecuteAsync(AuditExecutionContext context, CancellationToken cancellationToken = default);

    public AuditResult CreateReconciliationResult(
        string companyId,
        string explanation,
        SeverityLevel severity,
        string? voucherId = null,
        string? ledgerId = null,
        string? voucherNumber = null,
        DateTime? voucherDate = null,
        decimal? flaggedAmount = null,
        object? evidenceObj = null)
    {
        return new AuditResult
        {
            RuleId = RuleId,
            RuleName = RuleName,
            Category = Category,
            CompanyId = companyId,
            VoucherId = voucherId,
            LedgerId = ledgerId,
            VoucherNumber = voucherNumber,
            VoucherDate = voucherDate,
            FlaggedAmount = flaggedAmount,
            Severity = severity,
            Explanation = explanation,
            Evidence = evidenceObj != null ? JsonSerializer.Serialize(evidenceObj) : "{}",
            Status = ReviewStatus.Pending,
            DetectedAt = DateTime.UtcNow
        };
    }
}
