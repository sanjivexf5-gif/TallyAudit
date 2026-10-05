using Dapper;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Data.Repositories;

public class AuditRepository : IAuditRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public AuditRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<Company>> GetAllCompaniesAsync(CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = "SELECT * FROM Companies ORDER BY LastSyncDate DESC, TallyCompanyName ASC";
        var result = await connection.QueryAsync<Company>(new CommandDefinition(sql, cancellationToken: cancellationToken));
        return result.ToList();
    }

    public async Task<Company?> GetCompanyByIdAsync(string companyId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = "SELECT * FROM Companies WHERE Id = @Id OR TallyCompanyName = @Id LIMIT 1";
        return await connection.QuerySingleOrDefaultAsync<Company>(new CommandDefinition(sql, new { Id = companyId }, cancellationToken: cancellationToken));
    }

    public async Task<Company?> GetCompanyByNameAsync(string companyName, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = "SELECT * FROM Companies WHERE TallyCompanyName = @TallyCompanyName OR FormalName = @TallyCompanyName LIMIT 1";
        return await connection.QuerySingleOrDefaultAsync<Company>(new CommandDefinition(sql, new { TallyCompanyName = companyName }, cancellationToken: cancellationToken));
    }

    public async Task SaveCompanyAsync(Company company, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = @"
            INSERT INTO Companies (Id, TallyCompanyName, FormalName, GSTIN, PAN, StateName, StateCode, BooksFromDate, LastSyncDate, LastAlterId, IsActive, IsMock, CreatedAt)
            VALUES (@Id, @TallyCompanyName, @FormalName, @GSTIN, @PAN, @StateName, @StateCode, @BooksFromDate, @LastSyncDate, @LastAlterId, @IsActive, @IsMock, @CreatedAt)
            ON CONFLICT(Id) DO UPDATE SET
                TallyCompanyName = excluded.TallyCompanyName,
                FormalName = excluded.FormalName,
                GSTIN = excluded.GSTIN,
                PAN = excluded.PAN,
                StateName = excluded.StateName,
                StateCode = excluded.StateCode,
                BooksFromDate = excluded.BooksFromDate,
                LastSyncDate = excluded.LastSyncDate,
                LastAlterId = excluded.LastAlterId,
                IsActive = excluded.IsActive,
                IsMock = excluded.IsMock;
        ";
        await connection.ExecuteAsync(new CommandDefinition(sql, company, cancellationToken: cancellationToken));
    }

    public async Task<Company> EnsureCompanyAsync(Company company, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        var existing = await connection.QuerySingleOrDefaultAsync<Company>(
            new CommandDefinition("SELECT * FROM Companies WHERE Id = @Id OR TallyCompanyName = @TallyCompanyName LIMIT 1",
            new { company.Id, company.TallyCompanyName },
            cancellationToken: cancellationToken));

        if (existing != null)
        {
            company.Id = existing.Id;
            company.CreatedAt = existing.CreatedAt;
            if (string.IsNullOrEmpty(company.FormalName)) company.FormalName = existing.FormalName;
            if (string.IsNullOrEmpty(company.GSTIN)) company.GSTIN = existing.GSTIN;
            if (string.IsNullOrEmpty(company.PAN)) company.PAN = existing.PAN;
            if (string.IsNullOrEmpty(company.StateName)) company.StateName = existing.StateName;
            if (string.IsNullOrEmpty(company.StateCode)) company.StateCode = existing.StateCode;
            if (company.LastSyncDate == null) company.LastSyncDate = existing.LastSyncDate;
            if (company.LastAlterId == 0) company.LastAlterId = existing.LastAlterId;

            const string updateSql = @"
                UPDATE Companies SET
                    TallyCompanyName = @TallyCompanyName,
                    FormalName = @FormalName,
                    GSTIN = @GSTIN,
                    PAN = @PAN,
                    StateName = @StateName,
                    StateCode = @StateCode,
                    BooksFromDate = @BooksFromDate,
                    LastSyncDate = @LastSyncDate,
                    LastAlterId = @LastAlterId,
                    IsActive = @IsActive,
                    IsMock = @IsMock
                WHERE Id = @Id;
            ";
            await connection.ExecuteAsync(new CommandDefinition(updateSql, company, cancellationToken: cancellationToken));
            return (await GetCompanyByIdAsync(company.Id, cancellationToken)) ?? company;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(company.Id))
            {
                company.Id = company.TallyCompanyName;
            }
            await SaveCompanyAsync(company, cancellationToken);
            return (await GetCompanyByIdAsync(company.Id, cancellationToken)) ?? company;
        }
    }

    public async Task<int> GetVoucherCountAsync(string companyId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = "SELECT COUNT(*) FROM Vouchers WHERE CompanyId = @CompanyId";
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { CompanyId = companyId }, cancellationToken: cancellationToken));
    }

    public async Task<int> GetLedgerCountAsync(string companyId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = "SELECT COUNT(*) FROM Ledgers WHERE CompanyId = @CompanyId";
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { CompanyId = companyId }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<AuditException>> GetExceptionsAsync(
        string companyId, 
        RuleCategory? category = null, 
        SeverityLevel? minSeverity = null, 
        ReviewStatus? status = null, 
        int skip = 0, 
        int take = 50, 
        CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        var sql = "SELECT * FROM Exceptions WHERE CompanyId = @CompanyId";

        var parameters = new DynamicParameters();
        parameters.Add("CompanyId", companyId);

        if (category.HasValue)
        {
            sql += " AND Category = @Category";
            parameters.Add("Category", (int)category.Value);
        }

        if (minSeverity.HasValue)
        {
            sql += " AND Severity >= @Severity";
            parameters.Add("Severity", (int)minSeverity.Value);
        }

        if (status.HasValue)
        {
            sql += " AND Status = @Status";
            parameters.Add("Status", (int)status.Value);
        }

        sql += " ORDER BY Severity DESC, VoucherDate DESC LIMIT @Take OFFSET @Skip";
        parameters.Add("Take", take);
        parameters.Add("Skip", skip);

        var result = await connection.QueryAsync<AuditException>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        return result.ToList();
    }

    public async Task<int> GetExceptionCountAsync(string companyId, RuleCategory? category = null, SeverityLevel? minSeverity = null, ReviewStatus? status = null, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        var sql = "SELECT COUNT(*) FROM Exceptions WHERE CompanyId = @CompanyId";
        var parameters = new DynamicParameters();
        parameters.Add("CompanyId", companyId);

        if (category.HasValue)
        {
            sql += " AND Category = @Category";
            parameters.Add("Category", (int)category.Value);
        }
        if (minSeverity.HasValue)
        {
            sql += " AND Severity >= @Severity";
            parameters.Add("Severity", (int)minSeverity.Value);
        }
        if (status.HasValue)
        {
            sql += " AND Status = @Status";
            parameters.Add("Status", (int)status.Value);
        }

        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
    }

    public async Task UpdateExceptionStatusAsync(string exceptionId, ReviewStatus newStatus, string? auditorNote, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        
        const string sql = @"
            UPDATE Exceptions
            SET Status = @Status,
                AuditorNote = COALESCE(@AuditorNote, AuditorNote),
                ReviewedAt = CURRENT_TIMESTAMP
            WHERE Id = @Id
        ";
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = exceptionId, Status = (int)newStatus, AuditorNote = auditorNote },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<AuditRule>> GetActiveRulesAsync(CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = "SELECT * FROM AuditRules WHERE IsEnabled = 1 ORDER BY Category, Severity DESC";
        var result = await connection.QueryAsync<AuditRule>(new CommandDefinition(sql, cancellationToken: cancellationToken));
        return result.ToList();
    }

    public async Task<IReadOnlyList<AuditException>> GetExceptionsFilteredAsync(
        string companyId,
        string? category = null,
        string? severity = null,
        string? status = null,
        string? searchQuery = null,
        string? sortBy = null,
        bool isDescending = true,
        CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        var sql = "SELECT * FROM Exceptions WHERE CompanyId = @CompanyId";
        var parameters = new DynamicParameters();
        parameters.Add("CompanyId", companyId);

        if (!string.IsNullOrEmpty(category) && category != "All")
        {
            sql += " AND Category = @Category";
            if (Enum.TryParse<RuleCategory>(category, out var catEnum))
            {
                parameters.Add("Category", (int)catEnum);
            }
        }

        if (!string.IsNullOrEmpty(severity) && severity != "All")
        {
            sql += " AND Severity = @Severity";
            if (Enum.TryParse<SeverityLevel>(severity, out var sevEnum))
            {
                parameters.Add("Severity", (int)sevEnum);
            }
        }

        if (!string.IsNullOrEmpty(status) && status != "All")
        {
            sql += " AND Status = @Status";
            // Map Terminology terms
            var statusTerm = status;
            if (status == "Unreviewed") statusTerm = "Pending";
            else if (status == "Accepted") statusTerm = "Resolved";
            else if (status == "Needs Follow-up") statusTerm = "RequiresClientClarification";

            if (Enum.TryParse<ReviewStatus>(statusTerm, out var statEnum))
            {
                parameters.Add("Status", (int)statEnum);
            }
        }

        if (!string.IsNullOrEmpty(searchQuery))
        {
            sql += " AND (VoucherNumber LIKE @Query OR LedgerName LIKE @Query OR RuleName LIKE @Query OR SuggestedCorrection LIKE @Query)";
            parameters.Add("Query", $"%{searchQuery}%");
        }

        // Sorting
        var sortCol = "VoucherDate";
        if (sortBy == "Amount") sortCol = "FlaggedAmount";
        else if (sortBy == "Priority" || sortBy == "Severity") sortCol = "Severity";
        else if (sortBy == "Category") sortCol = "Category";
        else if (sortBy == "Review Status" || sortBy == "Status") sortCol = "Status";

        var dir = isDescending ? "DESC" : "ASC";
        sql += $" ORDER BY {sortCol} {dir}, FlaggedAt DESC";

        var results = await connection.QueryAsync<AuditException>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        return results.ToList();
    }

    public async Task SaveAuditRunAsync(AuditRun run, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = @"
            INSERT INTO AuditRuns (Id, CompanyId, Period, StartTime, EndTime, TransactionsAnalysed, FindingsGenerated, Status)
            VALUES (@Id, @CompanyId, @Period, @StartTime, @EndTime, @TransactionsAnalysed, @FindingsGenerated, @Status)
            ON CONFLICT(Id) DO UPDATE SET
                EndTime = excluded.EndTime,
                TransactionsAnalysed = excluded.TransactionsAnalysed,
                FindingsGenerated = excluded.FindingsGenerated,
                Status = excluded.Status;
        ";
        await connection.ExecuteAsync(new CommandDefinition(sql, run, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<AuditRun>> GetAuditRunsAsync(string companyId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = "SELECT * FROM AuditRuns WHERE CompanyId = @CompanyId ORDER BY StartTime DESC";
        var result = await connection.QueryAsync<AuditRun>(new CommandDefinition(sql, new { CompanyId = companyId }, cancellationToken: cancellationToken));
        return result.ToList();
    }

    public async Task ClearMockDatasetAsync(CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        const string deleteExceptionsSql = @"
            DELETE FROM Exceptions 
            WHERE CompanyId IN (SELECT Id FROM Companies WHERE IsMock = 1)
               OR CompanyId IN ('Demo Industrial Solutions Pvt Ltd (FY 2025-26)', 'Apex Industrial Solutions Pvt Ltd (FY 2025-26)', 'Delta Retail Ventures LLP (FY 2025-26)');
        ";

        const string deleteEntriesSql = @"
            DELETE FROM VoucherEntries 
            WHERE VoucherId IN (
                SELECT Id FROM Vouchers 
                WHERE CompanyId IN (SELECT Id FROM Companies WHERE IsMock = 1)
                   OR CompanyId IN ('Demo Industrial Solutions Pvt Ltd (FY 2025-26)', 'Apex Industrial Solutions Pvt Ltd (FY 2025-26)', 'Delta Retail Ventures LLP (FY 2025-26)')
            );
        ";

        const string deleteVouchersSql = @"
            DELETE FROM Vouchers 
            WHERE CompanyId IN (SELECT Id FROM Companies WHERE IsMock = 1)
               OR CompanyId IN ('Demo Industrial Solutions Pvt Ltd (FY 2025-26)', 'Apex Industrial Solutions Pvt Ltd (FY 2025-26)', 'Delta Retail Ventures LLP (FY 2025-26)');
        ";

        const string deleteLedgersSql = @"
            DELETE FROM Ledgers 
            WHERE CompanyId IN (SELECT Id FROM Companies WHERE IsMock = 1)
               OR CompanyId IN ('Demo Industrial Solutions Pvt Ltd (FY 2025-26)', 'Apex Industrial Solutions Pvt Ltd (FY 2025-26)', 'Delta Retail Ventures LLP (FY 2025-26)');
        ";

        const string deleteVoucherTypesSql = @"
            DELETE FROM VoucherTypes 
            WHERE CompanyId IN (SELECT Id FROM Companies WHERE IsMock = 1)
               OR CompanyId IN ('Demo Industrial Solutions Pvt Ltd (FY 2025-26)', 'Apex Industrial Solutions Pvt Ltd (FY 2025-26)', 'Delta Retail Ventures LLP (FY 2025-26)');
        ";

        const string deleteGroupsSql = @"
            DELETE FROM Groups 
            WHERE CompanyId IN (SELECT Id FROM Companies WHERE IsMock = 1)
               OR CompanyId IN ('Demo Industrial Solutions Pvt Ltd (FY 2025-26)', 'Apex Industrial Solutions Pvt Ltd (FY 2025-26)', 'Delta Retail Ventures LLP (FY 2025-26)');
        ";

        const string deleteFinancialYearsSql = @"
            DELETE FROM FinancialYears 
            WHERE CompanyId IN (SELECT Id FROM Companies WHERE IsMock = 1)
               OR CompanyId IN ('Demo Industrial Solutions Pvt Ltd (FY 2025-26)', 'Apex Industrial Solutions Pvt Ltd (FY 2025-26)', 'Delta Retail Ventures LLP (FY 2025-26)');
        ";

        const string deleteAuditRunsSql = @"
            DELETE FROM AuditRuns 
            WHERE CompanyId IN (SELECT Id FROM Companies WHERE IsMock = 1)
               OR CompanyId IN ('Demo Industrial Solutions Pvt Ltd (FY 2025-26)', 'Apex Industrial Solutions Pvt Ltd (FY 2025-26)', 'Delta Retail Ventures LLP (FY 2025-26)');
        ";

        const string deleteSyncHistorySql = @"
            DELETE FROM SyncHistory 
            WHERE CompanyId IN (SELECT Id FROM Companies WHERE IsMock = 1)
               OR CompanyId IN ('Demo Industrial Solutions Pvt Ltd (FY 2025-26)', 'Apex Industrial Solutions Pvt Ltd (FY 2025-26)', 'Delta Retail Ventures LLP (FY 2025-26)');
        ";

        const string deleteCompaniesSql = @"
            DELETE FROM Companies 
            WHERE IsMock = 1 
               OR Id IN ('Demo Industrial Solutions Pvt Ltd (FY 2025-26)', 'Apex Industrial Solutions Pvt Ltd (FY 2025-26)', 'Delta Retail Ventures LLP (FY 2025-26)');
        ";

        await connection.ExecuteAsync(new CommandDefinition(deleteExceptionsSql, transaction: transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(deleteEntriesSql, transaction: transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(deleteVouchersSql, transaction: transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(deleteLedgersSql, transaction: transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(deleteVoucherTypesSql, transaction: transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(deleteGroupsSql, transaction: transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(deleteFinancialYearsSql, transaction: transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(deleteAuditRunsSql, transaction: transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(deleteSyncHistorySql, transaction: transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(deleteCompaniesSql, transaction: transaction, cancellationToken: cancellationToken));

        transaction.Commit();
    }
}
