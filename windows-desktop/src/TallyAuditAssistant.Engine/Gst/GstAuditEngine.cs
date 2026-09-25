using System.Text.Json;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Gst;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Engine.Gst;

public class GstAuditEngine : IGstAuditEngine
{
    private readonly List<IGstRule> _rules = new();
    private readonly IGstRepository _repository;
    private readonly ILogger<GstAuditEngine> _logger;

    public IReadOnlyList<IGstRule> RegisteredRules => _rules.AsReadOnly();
    public event EventHandler<GstAuditProgress>? ProgressChanged;

    public GstAuditEngine(
        IGstRepository repository,
        ILogger<GstAuditEngine> logger,
        IEnumerable<IGstRule>? initialRules = null)
    {
        _repository = repository;
        _logger = logger;

        if (initialRules != null)
        {
            foreach (var rule in initialRules)
            {
                RegisterRule(rule);
            }
        }
    }

    public void RegisterRule(IGstRule rule)
    {
        if (!_rules.Any(r => r.RuleId == rule.RuleId))
        {
            _rules.Add(rule);
            _logger.LogDebug("Registered GST rule {RuleId} ({Name})", rule.RuleId, rule.Name);
        }
    }

    public async Task<GstAuditSummary> ExecuteAuditAsync(GstAuditContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting full GST statutory audit execution for company {Company} across {Count} registered rules...", context.CompanyId, _rules.Count);

        // Sync rules with repository configuration
        await SynchronizeRuleConfigurationsAsync(cancellationToken);

        var allResults = new List<GstCheckResult>();
        var enabledRules = _rules.Where(r => r.Enabled).ToList();
        var totalRules = enabledRules.Count;
        var executedCount = 0;

        foreach (var rule in enabledRules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                _logger.LogDebug("Evaluating GST rule {RuleId}...", rule.RuleId);
                var ruleResults = await rule.EvaluateAsync(context, cancellationToken);
                allResults.AddRange(ruleResults);

                executedCount++;
                var percentage = Math.Round(((double)executedCount / totalRules) * 100, 1);

                int exceptions = allResults.Count(r => r.Status == GstCheckStatus.Exception);
                int unable = allResults.Count(r => r.Status == GstCheckStatus.UnableToDetermine);

                ProgressChanged?.Invoke(this, new GstAuditProgress(
                    CurrentRuleId: rule.RuleId,
                    CurrentRuleName: rule.Name,
                    RulesExecuted: executedCount,
                    TotalRules: totalRules,
                    TransactionsProcessed: allResults.Count,
                    ExceptionsFound: exceptions,
                    UnableToDetermineCount: unable,
                    Percentage: percentage));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed evaluating GST audit rule {RuleId}", rule.RuleId);
            }
        }

        // Persist discovered audit results to SQLite database
        _logger.LogInformation("Saving {Count} GST check results to database...", allResults.Count);
        await _repository.SaveResultsBatchAsync(allResults, cancellationToken);

        // Compute summary
        var summary = new GstAuditSummary
        {
            TotalTransactionsChecked = allResults.Count,
            PassedCount = allResults.Count(r => r.Status == GstCheckStatus.Passed),
            ExceptionCount = allResults.Count(r => r.Status == GstCheckStatus.Exception),
            HighSeverityCount = allResults.Count(r => r.Status == GstCheckStatus.Exception && (r.Severity == SeverityLevel.High || r.Severity == SeverityLevel.Critical)),
            MediumSeverityCount = allResults.Count(r => r.Status == GstCheckStatus.Exception && r.Severity == SeverityLevel.Medium),
            LowSeverityCount = allResults.Count(r => r.Status == GstCheckStatus.Exception && r.Severity == SeverityLevel.Low),
            UnableToDetermineCount = allResults.Count(r => r.Status == GstCheckStatus.UnableToDetermine),
            EvaluatedAt = DateTime.UtcNow
        };

        return summary;
    }

    public async Task<IReadOnlyList<GstCheckResult>> EvaluateRuleAsync(string ruleId, GstAuditContext context, CancellationToken cancellationToken = default)
    {
        await SynchronizeRuleConfigurationsAsync(cancellationToken);
        var rule = _rules.FirstOrDefault(r => r.RuleId == ruleId);
        if (rule == null)
        {
            _logger.LogWarning("GST rule {RuleId} not found", ruleId);
            return Array.Empty<GstCheckResult>();
        }

        var results = await rule.EvaluateAsync(context, cancellationToken);
        await _repository.SaveResultsBatchAsync(results, cancellationToken);
        return results;
    }

    private async Task SynchronizeRuleConfigurationsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var savedRules = await _repository.GetRulesAsync(cancellationToken);
            var savedMap = savedRules.ToDictionary(r => r.RuleId);

            foreach (var rule in _rules)
            {
                if (savedMap.TryGetValue(rule.RuleId, out var saved))
                {
                    rule.Enabled = saved.Enabled;
                    rule.Severity = saved.Severity;
                    if (saved.Parameters != null && saved.Parameters.Count > 0)
                    {
                        foreach (var kvp in saved.Parameters)
                        {
                            rule.Parameters[kvp.Key] = kvp.Value;
                        }
                    }
                }
                else
                {
                    // Seed rule definition into repository
                    await _repository.SaveRuleAsync(new GstRuleDefinition
                    {
                        RuleId = rule.RuleId,
                        Name = rule.Name,
                        Description = rule.Description,
                        EffectiveDate = rule.EffectiveDate,
                        ExpiryDate = rule.ExpiryDate,
                        Jurisdiction = rule.Jurisdiction,
                        Version = rule.Version,
                        SourceReference = rule.SourceReference,
                        Severity = rule.Severity,
                        Enabled = rule.Enabled,
                        Parameters = rule.Parameters
                    }, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not synchronize GST rule configurations from database.");
        }
    }
}
