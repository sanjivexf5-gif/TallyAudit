using Dapper;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Gst;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Data.Repositories;

public class GstRepository : IGstRepository, IGstExceptionService
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<GstRepository> _logger;

    public GstRepository(SqliteConnectionFactory connectionFactory, ILogger<GstRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    private async Task EnsureTablesExistAsync(System.Data.Common.DbConnection connection)
    {
        const string sql = @"
            CREATE TABLE IF NOT EXISTS GstRules (
                RuleId TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT NOT NULL,
                EffectiveDate DATE NOT NULL,
                ExpiryDate DATE,
                Jurisdiction TEXT NOT NULL,
                Version TEXT NOT NULL,
                SourceReference TEXT NOT NULL,
                Severity INTEGER NOT NULL,
                Enabled INTEGER NOT NULL DEFAULT 1,
                ParametersJson TEXT
            );

            CREATE TABLE IF NOT EXISTS GstAuditResults (
                ResultId TEXT PRIMARY KEY,
                RuleId TEXT NOT NULL,
                RuleName TEXT NOT NULL,
                CompanyId TEXT NOT NULL,
                VoucherId TEXT,
                VoucherNumber TEXT,
                VoucherDate DATE,
                VoucherTypeName TEXT,
                PartyLedgerName TEXT,
                PartyGstin TEXT,
                TaxableAmount DECIMAL(18,2),
                TaxAmount DECIMAL(18,2),
                CgstAmount DECIMAL(18,2),
                SgstAmount DECIMAL(18,2),
                IgstAmount DECIMAL(18,2),
                PlaceOfSupply TEXT,
                Severity INTEGER NOT NULL,
                Status INTEGER NOT NULL,
                Explanation TEXT NOT NULL,
                EvidenceJson TEXT NOT NULL,
                Jurisdiction TEXT NOT NULL,
                SourceReference TEXT NOT NULL,
                DetectedAt DATETIME NOT NULL,
                ReviewStatus INTEGER NOT NULL DEFAULT 0,
                Reviewer TEXT,
                ReviewerNote TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_gst_results_comp ON GstAuditResults(CompanyId);
            CREATE INDEX IF NOT EXISTS idx_gst_results_rule ON GstAuditResults(RuleId);
            CREATE INDEX IF NOT EXISTS idx_gst_results_voucher ON GstAuditResults(VoucherId);
            CREATE INDEX IF NOT EXISTS idx_gst_results_status ON GstAuditResults(Status);
        ";
        await connection.ExecuteAsync(sql);
    }

    public async Task<IReadOnlyList<GstRuleDefinition>> GetRulesAsync(CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        await EnsureTablesExistAsync(connection);

        const string sql = "SELECT * FROM GstRules ORDER BY RuleId;";
        var rows = await connection.QueryAsync<GstRuleDefinition>(new CommandDefinition(sql, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task<GstRuleDefinition?> GetRuleByIdAsync(string ruleId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        await EnsureTablesExistAsync(connection);

        const string sql = "SELECT * FROM GstRules WHERE RuleId = @RuleId LIMIT 1;";
        return await connection.QuerySingleOrDefaultAsync<GstRuleDefinition>(new CommandDefinition(sql, new { RuleId = ruleId }, cancellationToken: cancellationToken));
    }

    public async Task SaveRuleAsync(GstRuleDefinition rule, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        await EnsureTablesExistAsync(connection);

        const string sql = @"
            INSERT INTO GstRules (RuleId, Name, Description, EffectiveDate, ExpiryDate, Jurisdiction, Version, SourceReference, Severity, Enabled, ParametersJson)
            VALUES (@RuleId, @Name, @Description, @EffectiveDate, @ExpiryDate, @Jurisdiction, @Version, @SourceReference, @Severity, @Enabled, @ParametersJson)
            ON CONFLICT(RuleId) DO UPDATE SET
                Name = excluded.Name,
                Description = excluded.Description,
                EffectiveDate = excluded.EffectiveDate,
                ExpiryDate = excluded.ExpiryDate,
                Jurisdiction = excluded.Jurisdiction,
                Version = excluded.Version,
                SourceReference = excluded.SourceReference,
                Severity = excluded.Severity,
                Enabled = excluded.Enabled,
                ParametersJson = excluded.ParametersJson;
        ";
        await connection.ExecuteAsync(new CommandDefinition(sql, rule, cancellationToken: cancellationToken));
    }

    public async Task SetRuleEnabledAsync(string ruleId, bool enabled, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        await EnsureTablesExistAsync(connection);

        const string sql = "UPDATE GstRules SET Enabled = @Enabled WHERE RuleId = @RuleId;";
        await connection.ExecuteAsync(new CommandDefinition(sql, new { RuleId = ruleId, Enabled = enabled ? 1 : 0 }, cancellationToken: cancellationToken));
    }

    public async Task UpdateRuleParametersAsync(string ruleId, string parametersJson, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        await EnsureTablesExistAsync(connection);

        const string sql = "UPDATE GstRules SET ParametersJson = @ParametersJson WHERE RuleId = @RuleId;";
        await connection.ExecuteAsync(new CommandDefinition(sql, new { RuleId = ruleId, ParametersJson = parametersJson }, cancellationToken: cancellationToken));
    }

    public async Task SaveResultsBatchAsync(IEnumerable<GstCheckResult> results, CancellationToken cancellationToken = default)
    {
        var list = results.ToList();
        if (list.Count == 0) return;

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        await EnsureTablesExistAsync(connection);
        using var tx = connection.BeginTransaction();

        const string sql = @"
            INSERT INTO GstAuditResults (
                ResultId, RuleId, RuleName, CompanyId, VoucherId, VoucherNumber, VoucherDate,
                VoucherTypeName, PartyLedgerName, PartyGstin, TaxableAmount, TaxAmount,
                CgstAmount, SgstAmount, IgstAmount, PlaceOfSupply, Severity, Status,
                Explanation, EvidenceJson, Jurisdiction, SourceReference, DetectedAt,
                ReviewStatus, Reviewer, ReviewerNote
            ) VALUES (
                @ResultId, @RuleId, @RuleName, @CompanyId, @VoucherId, @VoucherNumber, @VoucherDate,
                @VoucherTypeName, @PartyLedgerName, @PartyGstin, @TaxableAmount, @TaxAmount,
                @CgstAmount, @SgstAmount, @IgstAmount, @PlaceOfSupply, @Severity, @Status,
                @Explanation, @EvidenceJson, @Jurisdiction, @SourceReference, @DetectedAt,
                @ReviewStatus, @Reviewer, @ReviewerNote
            )
            ON CONFLICT(ResultId) DO UPDATE SET
                Status = excluded.Status,
                Explanation = excluded.Explanation,
                EvidenceJson = excluded.EvidenceJson,
                ReviewStatus = excluded.ReviewStatus,
                Reviewer = excluded.Reviewer,
                ReviewerNote = excluded.ReviewerNote;
        ";

        await connection.ExecuteAsync(sql, list, tx);
        tx.Commit();
        _logger.LogInformation("Saved {Count} GST check results to database", list.Count);
    }

    public async Task<IReadOnlyList<GstCheckResult>> GetResultsAsync(string companyId, string? ruleId = null, SeverityLevel? severity = null, GstCheckStatus? status = null, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        await EnsureTablesExistAsync(connection);

        var query = "SELECT * FROM GstAuditResults WHERE CompanyId = @CompanyId";
        var dp = new DynamicParameters();
        dp.Add("CompanyId", companyId);

        if (!string.IsNullOrEmpty(ruleId))
        {
            query += " AND RuleId = @RuleId";
            dp.Add("RuleId", ruleId);
        }

        if (severity.HasValue)
        {
            query += " AND Severity = @Severity";
            dp.Add("Severity", (int)severity.Value);
        }

        if (status.HasValue)
        {
            query += " AND Status = @Status";
            dp.Add("Status", (int)status.Value);
        }

        query += " ORDER BY DetectedAt DESC, Severity DESC;";
        var results = await connection.QueryAsync<GstCheckResult>(new CommandDefinition(query, dp, cancellationToken: cancellationToken));
        return results.ToList();
    }

    public async Task<GstAuditSummary> GetSummaryAsync(string companyId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        await EnsureTablesExistAsync(connection);

        const string sql = @"
            SELECT 
                COUNT(*) as Total,
                SUM(CASE WHEN Status = 0 THEN 1 ELSE 0 END) as Passed,
                SUM(CASE WHEN Status = 1 THEN 1 ELSE 0 END) as Exceptions,
                SUM(CASE WHEN Status = 1 AND Severity >= 3 THEN 1 ELSE 0 END) as HighSeverity,
                SUM(CASE WHEN Status = 1 AND Severity = 2 THEN 1 ELSE 0 END) as MediumSeverity,
                SUM(CASE WHEN Status = 1 AND Severity <= 1 THEN 1 ELSE 0 END) as LowSeverity,
                SUM(CASE WHEN Status = 2 THEN 1 ELSE 0 END) as UnableToDetermine
            FROM GstAuditResults
            WHERE CompanyId = @CompanyId;
        ";

        var row = await connection.QuerySingleOrDefaultAsync(new CommandDefinition(sql, new { CompanyId = companyId }, cancellationToken: cancellationToken));
        if (row == null || row.Total == null || row.Total is DBNull)
        {
            return new GstAuditSummary();
        }

        return new GstAuditSummary
        {
            TotalTransactionsChecked = row.Total != null && !(row.Total is DBNull) ? Convert.ToInt32(row.Total) : 0,
            PassedCount = row.Passed != null && !(row.Passed is DBNull) ? Convert.ToInt32(row.Passed) : 0,
            ExceptionCount = row.Exceptions != null && !(row.Exceptions is DBNull) ? Convert.ToInt32(row.Exceptions) : 0,
            HighSeverityCount = row.HighSeverity != null && !(row.HighSeverity is DBNull) ? Convert.ToInt32(row.HighSeverity) : 0,
            MediumSeverityCount = row.MediumSeverity != null && !(row.MediumSeverity is DBNull) ? Convert.ToInt32(row.MediumSeverity) : 0,
            LowSeverityCount = row.LowSeverity != null && !(row.LowSeverity is DBNull) ? Convert.ToInt32(row.LowSeverity) : 0,
            UnableToDetermineCount = row.UnableToDetermine != null && !(row.UnableToDetermine is DBNull) ? Convert.ToInt32(row.UnableToDetermine) : 0,
            EvaluatedAt = DateTime.UtcNow
        };
    }

    public async Task<GstVoucherDetail?> GetVoucherDetailAsync(string voucherId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        const string vSql = @"
            SELECT v.Id as VoucherId, v.CompanyId, v.VoucherTypeName, v.VoucherNumber, v.ReferenceNumber,
                   v.VoucherDate, v.TotalAmount, v.Narration, v.PartyLedgerName,
                   l.GSTIN as PartyGstin, l.StateName as PartyState
            FROM Vouchers v
            LEFT JOIN Ledgers l ON (l.Name = v.PartyLedgerName AND l.CompanyId = v.CompanyId)
            WHERE v.Id = @VoucherId LIMIT 1;
        ";

        var v = await connection.QuerySingleOrDefaultAsync<GstVoucherDetail>(new CommandDefinition(vSql, new { VoucherId = voucherId }, cancellationToken: cancellationToken));
        if (v == null) return null;

        const string eSql = @"
            SELECT e.Id as EntryId, e.LedgerName, e.Amount, e.IsDebit, l.ParentGroup, l.TaxType, l.GstRate, l.HsnCode
            FROM VoucherEntries e
            LEFT JOIN Ledgers l ON (l.Name = e.LedgerName AND l.CompanyId = @CompanyId)
            WHERE e.VoucherId = @VoucherId;
        ";

        var entries = await connection.QueryAsync<GstVoucherEntryDetail>(new CommandDefinition(eSql, new { VoucherId = voucherId, v.CompanyId }, cancellationToken: cancellationToken));
        v.Entries = entries.ToList();

        return v;
    }

    public async Task UpdateStatusAsync(string resultId, ReviewStatus status, string reviewer, string? notes, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        await EnsureTablesExistAsync(connection);

        const string sql = @"
            UPDATE GstAuditResults 
            SET ReviewStatus = @Status, Reviewer = @Reviewer, ReviewerNote = @Notes
            WHERE ResultId = @ResultId;
        ";
        await connection.ExecuteAsync(new CommandDefinition(sql, new { ResultId = resultId, Status = (int)status, Reviewer = reviewer, Notes = notes }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<GstCheckResult>> GetExceptionsByVoucherAsync(string voucherId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        await EnsureTablesExistAsync(connection);

        const string sql = "SELECT * FROM GstAuditResults WHERE VoucherId = @VoucherId AND Status = 1;";
        var results = await connection.QueryAsync<GstCheckResult>(new CommandDefinition(sql, new { VoucherId = voucherId }, cancellationToken: cancellationToken));
        return results.ToList();
    }

    public async Task<IReadOnlyList<GstCheckResult>> SearchExceptionsAsync(string companyId, string query, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        await EnsureTablesExistAsync(connection);

        const string sql = @"
            SELECT * FROM GstAuditResults
            WHERE CompanyId = @CompanyId AND Status = 1
              AND (VoucherNumber LIKE @Pattern OR PartyLedgerName LIKE @Pattern OR Explanation LIKE @Pattern OR RuleName LIKE @Pattern)
            ORDER BY DetectedAt DESC;
        ";
        var pattern = $"%{query}%";
        var results = await connection.QueryAsync<GstCheckResult>(new CommandDefinition(sql, new { CompanyId = companyId, Pattern = pattern }, cancellationToken: cancellationToken));
        return results.ToList();
    }
}
