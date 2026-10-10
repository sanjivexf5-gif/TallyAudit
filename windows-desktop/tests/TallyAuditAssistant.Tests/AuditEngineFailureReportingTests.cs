using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;
using TallyAuditAssistant.Data.Repositories;
using TallyAuditAssistant.Engine;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class AuditEngineFailureReportingTests : IAsyncLifetime
{
    private readonly string _databasePath = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), $"audit-failure-tests-{Guid.NewGuid():N}.db");
    private SqliteConnectionFactory _factory = null!;
    private DatabaseInitializer _initializer = null!;

    public AuditEngineFailureReportingTests()
    {
        _factory = new SqliteConnectionFactory(_databasePath);
        _initializer = new DatabaseInitializer(
            _factory,
            NullLogger<DatabaseInitializer>.Instance,
            _databasePath);
    }

    public Task InitializeAsync() => _initializer.InitializeAsync();

    public Task DisposeAsync()
    {
        foreach (var path in new[] { _databasePath, _databasePath + "-wal", _databasePath + "-shm" })
        {
            try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); } catch { }
        }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ExecuteAudit_ExposesRuleFailureInsteadOfLookingComplete()
    {
        var passingRule = new StubRule("RULE-PASS", shouldFail: false);
        var failingRule = new StubRule("RULE-FAIL", shouldFail: true);
        var engine = new AuditEngine(
            new AuditRuleRepository(_factory, NullLogger<AuditRuleRepository>.Instance),
            new AuditResultRepository(_factory, NullLogger<AuditResultRepository>.Instance),
            NullLogger<AuditEngine>.Instance,
            new IAuditRule[] { passingRule, failingRule });

        var results = await engine.ExecuteAuditAsync(new AuditExecutionContext(
            "COMP-ENGINE-TEST",
            new DateTime(2025, 4, 1),
            new DateTime(2026, 3, 31)));

        Assert.Empty(results);
        Assert.True(passingRule.WasEvaluated);
        Assert.True(failingRule.WasEvaluated);
        var failure = Assert.Single(engine.LastExecutionFailures);
        Assert.Equal("RULE-FAIL", failure.RuleId);
        Assert.Equal("Failing test rule", failure.RuleName);
        Assert.Contains("simulated rule failure", failure.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubRule : IAuditRule
    {
        private readonly bool _shouldFail;
        public string RuleId { get; }
        public string Name => _shouldFail ? "Failing test rule" : "Passing test rule";
        public RuleCategory Category => RuleCategory.GeneralAccounting;
        public string Description => "Test-only rule.";
        public SeverityLevel Severity { get; set; } = SeverityLevel.Medium;
        public string Version => "1.0.0";
        public DateTime? EffectiveFrom => null;
        public DateTime? EffectiveTo => null;
        public bool Enabled { get; set; } = true;
        public Dictionary<string, object> Parameters { get; set; } = new();
        public bool WasEvaluated { get; private set; }

        public StubRule(string ruleId, bool shouldFail)
        {
            RuleId = ruleId;
            _shouldFail = shouldFail;
        }

        public Task<IReadOnlyList<AuditResult>> EvaluateAsync(
            AuditExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            WasEvaluated = true;
            if (_shouldFail)
                throw new InvalidOperationException("simulated rule failure");

            return Task.FromResult<IReadOnlyList<AuditResult>>(Array.Empty<AuditResult>());
        }
    }
}
