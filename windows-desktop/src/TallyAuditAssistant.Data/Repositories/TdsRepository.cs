using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Tds;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Data.Repositories;

public class TdsRepository : ITdsRepository, ITdsExceptionService
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<TdsRepository> _logger;

    public TdsRepository(SqliteConnectionFactory connectionFactory, ILogger<TdsRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TdsRuleDefinition>> GetRulesAsync(CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = @"
            SELECT Id as RuleId, Name, Category as Section, Description, Version, EffectiveFrom as EffectiveDate, EffectiveTo as ExpiryDate, Severity, Enabled, ParametersJson
            FROM Rules
            WHERE Category LIKE 'TDS%' OR Id LIKE 'TDS%'
            ORDER BY Id ASC;
        ";

        var rows = await connection.QueryAsync(new CommandDefinition(sql, cancellationToken: cancellationToken));
        var rules = new List<TdsRuleDefinition>();

        foreach (var r in rows)
        {
            var def = new TdsRuleDefinition
            {
                RuleId = (string)r.RuleId,
                Name = (string)r.Name,
                Section = (string)(r.Section ?? "General"),
                Description = (string)(r.Description ?? ""),
                Version = (string)(r.Version ?? "1.0.0"),
                EffectiveDate = r.EffectiveDate != null ? (DateTime)r.EffectiveDate : new DateTime(2020, 4, 1),
                ExpiryDate = (DateTime?)r.ExpiryDate,
                Severity = Enum.TryParse<SeverityLevel>((string)r.Severity, out var sev) ? sev : SeverityLevel.Medium,
                Enabled = (long)r.Enabled == 1
            };

            if (r.ParametersJson != null)
            {
                def.ParametersJson = (string)r.ParametersJson;
            }
            rules.Add(def);
        }

        return rules;
    }

    public async Task<TdsRuleDefinition?> GetRuleByIdAsync(string ruleId, CancellationToken cancellationToken = default)
    {
        var rules = await GetRulesAsync(cancellationToken);
        return rules.FirstOrDefault(r => r.RuleId == ruleId);
    }

    public async Task SaveRuleAsync(TdsRuleDefinition rule, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = @"
            INSERT INTO Rules (Id, Name, Category, Description, Version, EffectiveFrom, EffectiveTo, Severity, Enabled, ParametersJson)
            VALUES (@RuleId, @Name, @Section, @Description, @Version, @EffectiveDate, @ExpiryDate, @Severity, @Enabled, @ParametersJson)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Description = excluded.Description,
                Version = excluded.Version,
                Severity = excluded.Severity,
                Enabled = excluded.Enabled,
                ParametersJson = excluded.ParametersJson;
        ";

        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            rule.RuleId,
            rule.Name,
            rule.Section,
            rule.Description,
            rule.Version,
            rule.EffectiveDate,
            rule.ExpiryDate,
            Severity = rule.Severity.ToString(),
            Enabled = rule.Enabled ? 1 : 0,
            rule.ParametersJson
        }, cancellationToken: cancellationToken));
    }

    public async Task SetRuleEnabledAsync(string ruleId, bool enabled, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = "UPDATE Rules SET Enabled = @Enabled WHERE Id = @RuleId;";
        await connection.ExecuteAsync(new CommandDefinition(sql, new { RuleId = ruleId, Enabled = enabled ? 1 : 0 }, cancellationToken: cancellationToken));
    }

    public async Task UpdateRuleParametersAsync(string ruleId, string parametersJson, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = "UPDATE Rules SET ParametersJson = @ParametersJson WHERE Id = @RuleId;";
        await connection.ExecuteAsync(new CommandDefinition(sql, new { RuleId = ruleId, ParametersJson = parametersJson }, cancellationToken: cancellationToken));
    }

    private static DateTime? ParseNullableDateTime(object? val)
    {
        if (val == null) return null;
        if (val is DateTime dt) return dt;
        if (DateTime.TryParse(val.ToString(), out DateTime parsed)) return parsed;
        return null;
    }

    public async Task SaveResultsBatchAsync(IEnumerable<TdsCheckResult> results, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var tx = connection.BeginTransaction();

        const string ensureRuleSql = @"
            INSERT OR IGNORE INTO AuditRules (RuleId, Category, Name, Description, Severity, SuggestedReview, Version, IsEnabled)
            VALUES (@RuleId, 2, @RuleName, 'TDS Statutory Audit Rule', 3, 'Review TDS deduction', '1.0.0', 1);
        ";

        const string sql = @"
            INSERT INTO Exceptions (
                Id, CompanyId, RuleId, RuleName, Category, Severity, VoucherId, LedgerId,
                VoucherNumber, VoucherDate, FlaggedAmount, Explanation, EvidenceJson, Status
            )
            VALUES (
                @ResultId, @CompanyId, @RuleId, @RuleName, @Category, @Severity, @VoucherId, @PartyLedgerId,
                @VoucherNumber, @VoucherDate, @FlaggedAmount, @Explanation, @EvidenceJson, @Status
            )
            ON CONFLICT(Id) DO UPDATE SET
                Explanation = excluded.Explanation,
                EvidenceJson = excluded.EvidenceJson,
                Status = excluded.Status;
        ";

        foreach (var r in results)
        {
            await connection.ExecuteAsync(new CommandDefinition(ensureRuleSql, new { RuleId = r.RuleId, RuleName = r.RuleName }, tx, cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                r.ResultId,
                r.CompanyId,
                r.RuleId,
                r.RuleName,
                Category = "TDS - " + r.Section,
                Severity = r.Severity.ToString(),
                r.VoucherId,
                r.PartyLedgerId,
                r.VoucherNumber,
                VoucherDate = r.VoucherDate?.ToString("yyyy-MM-dd"),
                FlaggedAmount = r.TransactionAmount ?? r.DeductedTdsAmount ?? 0,
                r.Explanation,
                r.EvidenceJson,
                Status = r.ReviewStatus.ToString()
            }, tx, cancellationToken: cancellationToken));
        }

        tx.Commit();
    }

    public async Task<IReadOnlyList<TdsCheckResult>> GetResultsAsync(
        string companyId,
        string? ruleId = null,
        string? section = null,
        SeverityLevel? severity = null,
        TdsCheckStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        string sql = @"
            SELECT Id as ResultId, RuleId, RuleName, Category as Section, CompanyId, VoucherId,
                   VoucherNumber, VoucherDate, FlaggedAmount as TransactionAmount, Explanation,
                   EvidenceJson, Severity, Status as ReviewStatus
            FROM Exceptions
            WHERE CompanyId = @CompanyId AND (Category LIKE 'TDS%' OR RuleId LIKE 'TDS%')
        ";

        if (!string.IsNullOrEmpty(ruleId)) sql += " AND RuleId = @RuleId";
        if (severity.HasValue) sql += " AND Severity = @Severity";

        var rows = await connection.QueryAsync(new CommandDefinition(sql, new
        {
            CompanyId = companyId,
            RuleId = ruleId,
            Severity = severity?.ToString()
        }, cancellationToken: cancellationToken));

        var results = new List<TdsCheckResult>();
        foreach (var r in rows)
        {
            results.Add(new TdsCheckResult
            {
                ResultId = (string)r.ResultId,
                RuleId = (string)r.RuleId,
                RuleName = (string)r.RuleName,
                Section = (string)(r.Section ?? "TDS"),
                CompanyId = (string)r.CompanyId,
                VoucherId = (string?)r.VoucherId,
                VoucherNumber = (string?)r.VoucherNumber,
                VoucherDate = ParseNullableDateTime(r.VoucherDate),
                TransactionAmount = (decimal?)r.TransactionAmount,
                Explanation = (string)r.Explanation,
                EvidenceJson = (string)(r.EvidenceJson ?? "{}"),
                Severity = Enum.TryParse<SeverityLevel>((string)r.Severity, out var s) ? s : SeverityLevel.Medium,
                ReviewStatus = Enum.TryParse<ReviewStatus>((string)r.ReviewStatus, out var rs) ? rs : ReviewStatus.Pending
            });
        }

        return results;
    }

    public async Task<TdsAuditSummary> GetSummaryAsync(string companyId, CancellationToken cancellationToken = default)
    {
        var results = await GetResultsAsync(companyId, cancellationToken: cancellationToken);

        return new TdsAuditSummary
        {
            TotalTransactionsChecked = results.Count,
            PassedCount = results.Count(r => r.Status == TdsCheckStatus.Passed),
            ExceptionCount = results.Count(r => r.Status == TdsCheckStatus.Exception),
            HighSeverityCount = results.Count(r => r.Status == TdsCheckStatus.Exception && (r.Severity == SeverityLevel.High || r.Severity == SeverityLevel.Critical)),
            MediumSeverityCount = results.Count(r => r.Status == TdsCheckStatus.Exception && r.Severity == SeverityLevel.Medium),
            LowSeverityCount = results.Count(r => r.Status == TdsCheckStatus.Exception && r.Severity == SeverityLevel.Low),
            ReviewRequiredInsufficientDataCount = results.Count(r => r.Status == TdsCheckStatus.ReviewRequiredInsufficientData),
            EvaluatedAt = DateTime.UtcNow
        };
    }

    public async Task<TdsVoucherDetail?> GetVoucherDetailAsync(string voucherId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = @"
            SELECT v.Id as VoucherId, v.CompanyId, v.VoucherTypeName, v.VoucherNumber, v.ReferenceNumber,
                   v.VoucherDate, v.TotalAmount, v.Narration, v.PartyLedgerName, l.PAN as PartyPan
            FROM Vouchers v
            LEFT JOIN Ledgers l ON (l.CompanyId = v.CompanyId AND l.Name = v.PartyLedgerName)
            WHERE v.Id = @VoucherId;
        ";

        var v = await connection.QuerySingleOrDefaultAsync(new CommandDefinition(sql, new { VoucherId = voucherId }, cancellationToken: cancellationToken));
        if (v == null) return null;

        const string entriesSql = @"
            SELECT e.Id, e.LedgerName, l.ParentGroup, e.Amount, e.IsDebit, l.TdsRate, l.TaxType
            FROM VoucherEntries e
            LEFT JOIN Ledgers l ON (l.Name = e.LedgerName)
            WHERE e.VoucherId = @VoucherId;
        ";

        var entryRows = await connection.QueryAsync(new CommandDefinition(entriesSql, new { VoucherId = voucherId }, cancellationToken: cancellationToken));
        var entries = new List<TdsVoucherEntryDetail>();

        foreach (var er in entryRows)
        {
            entries.Add(new TdsVoucherEntryDetail
            {
                Id = (string)er.Id,
                LedgerName = (string)er.LedgerName,
                ParentGroup = (string?)er.ParentGroup,
                Amount = (decimal)er.Amount,
                IsDebit = (long)er.IsDebit == 1,
                TdsRate = (decimal?)er.TdsRate,
                TaxType = (string?)er.TaxType
            });
        }

        return new TdsVoucherDetail
        {
            VoucherId = (string)v.VoucherId,
            CompanyId = (string)v.CompanyId,
            VoucherTypeName = (string)v.VoucherTypeName,
            VoucherNumber = (string?)v.VoucherNumber,
            ReferenceNumber = (string?)v.ReferenceNumber,
            VoucherDate = ParseNullableDateTime(v.VoucherDate) ?? DateTime.MinValue,
            TotalAmount = (decimal)v.TotalAmount,
            Narration = (string?)v.Narration,
            PartyLedgerName = (string?)v.PartyLedgerName,
            PartyPan = (string?)v.PartyPan,
            Entries = entries
        };
    }

    public async Task UpdateStatusAsync(string resultId, ReviewStatus status, string reviewer, string? notes, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = @"
            UPDATE Exceptions
            SET Status = @Status, Reviewer = @Reviewer, ReviewerNote = @Notes, ReviewedAt = CURRENT_TIMESTAMP
            WHERE Id = @ResultId;
        ";

        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            ResultId = resultId,
            Status = status.ToString(),
            Reviewer = reviewer,
            Notes = notes
        }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<TdsCheckResult>> GetExceptionsByVoucherAsync(string voucherId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = "SELECT * FROM Exceptions WHERE VoucherId = @VoucherId;";
        var rows = await connection.QueryAsync(new CommandDefinition(sql, new { VoucherId = voucherId }, cancellationToken: cancellationToken));
        return rows.Select(r => new TdsCheckResult
        {
            ResultId = (string)r.Id,
            RuleId = (string)r.RuleId,
            RuleName = (string)r.RuleName,
            VoucherNumber = (string?)r.VoucherNumber,
            Explanation = (string)r.Explanation,
            EvidenceJson = (string)(r.EvidenceJson ?? "{}")
        }).ToList();
    }

    public async Task<IReadOnlyList<TdsCheckResult>> SearchExceptionsAsync(string companyId, string query, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = @"
            SELECT * FROM Exceptions
            WHERE CompanyId = @CompanyId AND (Category LIKE 'TDS%' OR RuleId LIKE 'TDS%')
              AND (Explanation LIKE @Q OR VoucherNumber LIKE @Q OR RuleName LIKE @Q);
        ";
        var rows = await connection.QueryAsync(new CommandDefinition(sql, new { CompanyId = companyId, Q = "%" + query + "%" }, cancellationToken: cancellationToken));
        return rows.Select(r => new TdsCheckResult
        {
            ResultId = (string)r.Id,
            RuleId = (string)r.RuleId,
            RuleName = (string)r.RuleName,
            VoucherNumber = (string?)r.VoucherNumber,
            Explanation = (string)r.Explanation,
            EvidenceJson = (string)(r.EvidenceJson ?? "{}")
        }).ToList();
    }
}
