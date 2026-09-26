using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;

namespace TallyAuditAssistant.Engine.Reconciliation;

public class ReconciliationEngine : IReconciliationEngine
{
    private readonly List<IReconciliationRule> _rules = new();
    private readonly IAuditResultRepository _resultRepository;
    private readonly ILogger<ReconciliationEngine> _logger;

    public IReadOnlyList<IReconciliationRule> RegisteredRules => _rules.AsReadOnly();

    public ReconciliationEngine(
        IAuditResultRepository resultRepository,
        ILogger<ReconciliationEngine> logger,
        IEnumerable<IReconciliationRule>? rules = null)
    {
        _resultRepository = resultRepository;
        _logger = logger;

        if (rules != null)
        {
            foreach (var rule in rules)
            {
                RegisterRule(rule);
            }
        }
    }

    public void RegisterRule(IReconciliationRule rule)
    {
        if (!_rules.Any(r => r.RuleId == rule.RuleId))
        {
            _rules.Add(rule);
            _logger.LogInformation("Registered reconciliation rule: {RuleId} ({RuleName})", rule.RuleId, rule.RuleName);
        }
    }

    public async Task<IReadOnlyList<AuditResult>> ExecuteReconciliationsAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting cross-dataset reconciliation execution for company {Company} across {Count} registered rules...", context.CompanyId, _rules.Count);

        var allResults = new List<AuditResult>();
        var enabledRules = _rules.Where(r => r.IsEnabled).ToList();
        var totalRules = enabledRules.Count;
        var executedCount = 0;

        foreach (var rule in enabledRules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                _logger.LogDebug("Executing reconciliation check {RuleId}...", rule.RuleId);
                var ruleResults = await rule.ExecuteAsync(context, cancellationToken);
                allResults.AddRange(ruleResults);

                executedCount++;
                _logger.LogInformation("Reconciliation rule {RuleId} executed. Found {Count} exceptions.", rule.RuleId, ruleResults.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to run reconciliation rule {RuleId}", rule.RuleId);
            }
        }

        // Persist findings batch
        if (allResults.Count > 0)
        {
            _logger.LogInformation("Persisting {Count} discovered reconciliation differences to SQLite...", allResults.Count);
            await _resultRepository.SaveResultsBatchAsync(allResults, cancellationToken);
        }

        return allResults;
    }
}
