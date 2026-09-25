using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Gst;
using TallyAuditAssistant.Core.Domain.Tds;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Engine.Tds;

public class TdsAuditEngine : ITdsAuditEngine
{
    private readonly List<ITdsRule> _rules = new();
    private readonly ITdsRepository _repository;
    private readonly ILogger<TdsAuditEngine> _logger;

    public IReadOnlyList<ITdsRule> RegisteredRules => _rules.AsReadOnly();
    public event EventHandler<TdsAuditProgress>? ProgressChanged;

    public TdsAuditEngine(ITdsRepository repository, ILogger<TdsAuditEngine> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public void RegisterRule(ITdsRule rule)
    {
        if (!_rules.Any(r => r.RuleId == rule.RuleId))
        {
            _rules.Add(rule);
        }
    }

    public async Task<GstAuditSummary?> GetGstSummaryPlaceholder() => null;

    public async Task<TdsAuditSummary> ExecuteAuditAsync(TdsAuditContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting TDS Statutory Audit execution for company {CompanyId}", context.CompanyId);

        var activeRules = _rules.Where(r => r.Enabled).ToList();
        var allResults = new List<TdsCheckResult>();
        int executedCount = 0;

        foreach (var rule in activeRules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogDebug("Executing TDS rule [{RuleId}] {RuleName}", rule.RuleId, rule.Name);

            try
            {
                var ruleResults = await rule.EvaluateAsync(context, cancellationToken);
                allResults.AddRange(ruleResults);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error evaluating TDS rule [{RuleId}] {RuleName}", rule.RuleId, rule.Name);
            }

            executedCount++;
            double percent = activeRules.Count > 0 ? (double)executedCount / activeRules.Count * 100.0 : 100.0;

            ProgressChanged?.Invoke(this, new TdsAuditProgress(
                rule.RuleId,
                rule.Name,
                executedCount,
                activeRules.Count,
                allResults.Count,
                allResults.Count(r => r.Status == TdsCheckStatus.Exception),
                allResults.Count(r => r.Status == TdsCheckStatus.ReviewRequiredInsufficientData),
                percent
            ));
        }

        // Persist findings into SQLite
        await _repository.SaveResultsBatchAsync(allResults, cancellationToken);

        var summary = new TdsAuditSummary
        {
            TotalTransactionsChecked = allResults.Count,
            PassedCount = allResults.Count(r => r.Status == TdsCheckStatus.Passed),
            ExceptionCount = allResults.Count(r => r.Status == TdsCheckStatus.Exception),
            HighSeverityCount = allResults.Count(r => r.Status == TdsCheckStatus.Exception && (r.Severity == SeverityLevel.High || r.Severity == SeverityLevel.Critical)),
            MediumSeverityCount = allResults.Count(r => r.Status == TdsCheckStatus.Exception && r.Severity == SeverityLevel.Medium),
            LowSeverityCount = allResults.Count(r => r.Status == TdsCheckStatus.Exception && r.Severity == SeverityLevel.Low),
            ReviewRequiredInsufficientDataCount = allResults.Count(r => r.Status == TdsCheckStatus.ReviewRequiredInsufficientData),
            EvaluatedAt = DateTime.UtcNow
        };

        _logger.LogInformation("Completed TDS Audit: {Exceptions} exceptions found, {Insufficient} requiring review", summary.ExceptionCount, summary.ReviewRequiredInsufficientDataCount);

        return summary;
    }

    public async Task<IReadOnlyList<TdsCheckResult>> EvaluateRuleAsync(string ruleId, TdsAuditContext context, CancellationToken cancellationToken = default)
    {
        var rule = _rules.FirstOrDefault(r => r.RuleId == ruleId)
            ?? throw new InvalidOperationException($"TDS rule '{ruleId}' is not registered.");

        return await rule.EvaluateAsync(context, cancellationToken);
    }
}
