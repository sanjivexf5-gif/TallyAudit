using System.Text.Json;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Engine;

public class AuditEngine : IAuditEngine
{
    private readonly List<IAuditRule> _rules = new();
    private readonly IAuditRuleRepository _ruleRepository;
    private readonly IAuditResultRepository _resultRepository;
    private readonly ILogger<AuditEngine> _logger;
    private readonly IReconciliationEngine? _reconciliationEngine;

    public IReadOnlyList<IAuditRule> RegisteredRules => _rules.AsReadOnly();

    public event EventHandler<AuditEngineProgress>? ProgressChanged;

    public AuditEngine(
        IAuditRuleRepository ruleRepository,
        IAuditResultRepository resultRepository,
        ILogger<AuditEngine> logger,
        IEnumerable<IAuditRule>? initialRules = null,
        IReconciliationEngine? reconciliationEngine = null)
    {
        _ruleRepository = ruleRepository;
        _resultRepository = resultRepository;
        _logger = logger;
        _reconciliationEngine = reconciliationEngine;

        if (initialRules != null)
        {
            foreach (var rule in initialRules)
            {
                RegisterRule(rule);
            }
        }
    }

    public void RegisterRule(IAuditRule rule)
    {
        if (!_rules.Any(r => r.RuleId == rule.RuleId))
        {
            _rules.Add(rule);
            _logger.LogDebug("Registered audit rule {RuleId} ({Name})", rule.RuleId, rule.Name);
        }
    }

    public async Task<IReadOnlyList<AuditResult>> ExecuteAuditAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting full audit execution for company {Company} across {Count} registered rules...", context.CompanyId, _rules.Count);

        // Synchronize in-memory rules with database configuration (Enabled / Parameters)
        await SynchronizeRuleConfigurationsAsync(cancellationToken);

        var allResults = new List<AuditResult>();
        var enabledRules = _rules.Where(r => r.Enabled).ToList();
        var totalRules = enabledRules.Count;
        var executedCount = 0;

        foreach (var rule in enabledRules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                _logger.LogDebug("Evaluating rule {RuleId}...", rule.RuleId);
                var ruleResults = await rule.EvaluateAsync(context, cancellationToken);
                allResults.AddRange(ruleResults);

                executedCount++;
                var percentage = Math.Round(((double)executedCount / totalRules) * 100, 1);

                ProgressChanged?.Invoke(this, new AuditEngineProgress(
                    CurrentRuleId: rule.RuleId,
                    CurrentRuleName: rule.Name,
                    RulesExecuted: executedCount,
                    TotalRules: totalRules,
                    ExceptionsFound: allResults.Count,
                    Percentage: percentage));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed evaluating audit rule {RuleId}", rule.RuleId);
            }
        }

        // Persist discovered audit results to SQLite database
        _logger.LogInformation("Saving {Count} discovered exceptions to local repository...", allResults.Count);
        await _resultRepository.SaveResultsBatchAsync(allResults, cancellationToken);

        if (_reconciliationEngine != null)
        {
            _logger.LogInformation("Executing cross-dataset reconciliations...");
            var reconciliationResults = await _reconciliationEngine.ExecuteReconciliationsAsync(context, cancellationToken);
            allResults.AddRange(reconciliationResults);
        }

        return allResults;
    }

    public async Task<IReadOnlyList<AuditResult>> ExecuteCategoryAsync(RuleCategory category, AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        await SynchronizeRuleConfigurationsAsync(cancellationToken);
        var targetRules = _rules.Where(r => r.Enabled && r.Category == category).ToList();
        var results = new List<AuditResult>();

        foreach (var rule in targetRules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var r = await rule.EvaluateAsync(context, cancellationToken);
            results.AddRange(r);
        }

        await _resultRepository.SaveResultsBatchAsync(results, cancellationToken);
        return results;
    }

    public async Task<IReadOnlyList<AuditResult>> ExecuteRuleAsync(string ruleId, AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        await SynchronizeRuleConfigurationsAsync(cancellationToken);
        var rule = _rules.FirstOrDefault(r => r.RuleId == ruleId);
        if (rule == null)
        {
            throw new ArgumentException($"Audit rule with ID '{ruleId}' is not registered.");
        }

        var results = await rule.EvaluateAsync(context, cancellationToken);
        await _resultRepository.SaveResultsBatchAsync(results, cancellationToken);
        return results;
    }

    private async Task SynchronizeRuleConfigurationsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var dbRules = await _ruleRepository.GetAllRulesAsync(cancellationToken);
            foreach (var dbRule in dbRules)
            {
                var memoryRule = _rules.FirstOrDefault(r => r.RuleId == dbRule.RuleId);
                if (memoryRule != null)
                {
                    memoryRule.Enabled = dbRule.IsEnabled;
                    memoryRule.Severity = dbRule.Severity;

                    if (!string.IsNullOrEmpty(dbRule.ParametersJson))
                    {
                        try
                        {
                            var parsedParams = JsonSerializer.Deserialize<Dictionary<string, object>>(dbRule.ParametersJson);
                            if (parsedParams != null)
                            {
                                memoryRule.Parameters = parsedParams;
                            }
                        }
                        catch
                        {
                            // Keep default parameters
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load stored rule configurations from database. Using memory defaults.");
        }
    }
}
