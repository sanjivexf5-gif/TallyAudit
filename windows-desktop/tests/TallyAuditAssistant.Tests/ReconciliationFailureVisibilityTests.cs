using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Engine;
using TallyAuditAssistant.Engine.Reconciliation;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class ReconciliationFailureVisibilityTests
{
    [Fact]
    public async Task AuditEngine_IncludesReconciliationRuleFailuresAndResetsThemForNextRun()
    {
        var failingRule = new FailingReconciliationRule();
        var resultRepository = new Mock<IAuditResultRepository>();
        resultRepository
            .Setup(x => x.SaveResultsBatchAsync(
                It.IsAny<IReadOnlyList<AuditResult>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var ruleRepository = new Mock<IAuditRuleRepository>();
        ruleRepository
            .Setup(x => x.GetAllRulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AuditRule>)Array.Empty<AuditRule>());

        var reconciliation = new ReconciliationEngine(
            resultRepository.Object,
            NullLogger<ReconciliationEngine>.Instance,
            new IReconciliationRule[] { failingRule });

        var auditEngine = new AuditEngine(
            ruleRepository.Object,
            resultRepository.Object,
            NullLogger<AuditEngine>.Instance,
            Array.Empty<IAuditRule>(),
            reconciliation);

        var context = new AuditExecutionContext(
            "company-1",
            new DateTime(2025, 4, 1),
            new DateTime(2026, 3, 31));

        await auditEngine.ExecuteAuditAsync(context);

        var failure = Assert.Single(auditEngine.LastExecutionFailures);
        Assert.Equal("REC-FAIL-01", failure.RuleId);
        Assert.Contains("Simulated reconciliation failure", failure.ErrorMessage);

        // A previous run's failure must not leak into a subsequent clean run.
        failingRule.IsEnabled = false;
        await auditEngine.ExecuteAuditAsync(context);

        Assert.Empty(auditEngine.LastExecutionFailures);
    }

    private sealed class FailingReconciliationRule : IReconciliationRule
    {
        public string RuleId => "REC-FAIL-01";
        public string RuleCode => RuleId;
        public string RuleName => "Simulated failing reconciliation";
        public RuleCategory Category => RuleCategory.GeneralAccounting;
        public string Description => "Test-only rule used to verify incomplete audit handling.";
        public bool IsEnabled { get; set; } = true;

        public Task<IReadOnlyList<AuditResult>> ExecuteAsync(
            AuditExecutionContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromException<IReadOnlyList<AuditResult>>(
                new InvalidOperationException("Simulated reconciliation failure."));
    }
}
