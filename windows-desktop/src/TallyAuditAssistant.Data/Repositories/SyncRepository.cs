using Dapper;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Domain.Ledgers;
using TallyAuditAssistant.Core.Domain.Sync;
using TallyAuditAssistant.Core.Domain.Vouchers;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Data.Repositories;

public class SyncRepository : ISyncRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<SyncRepository> _logger;

    public SyncRepository(SqliteConnectionFactory connectionFactory, ILogger<SyncRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task UpsertCompanyAsync(Company company, FinancialYear fy, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        const string compSql = @"
            INSERT INTO Companies (Id, TallyCompanyName, FormalName, GSTIN, PAN, StateName, StateCode, BooksFromDate, LastSyncDate, LastAlterId, IsActive, CreatedAt)
            VALUES (@Id, @TallyCompanyName, @FormalName, @GSTIN, @PAN, @StateName, @StateCode, @BooksFromDate, @LastSyncDate, @LastAlterId, @IsActive, @CreatedAt)
            ON CONFLICT(Id) DO UPDATE SET
                TallyCompanyName = excluded.TallyCompanyName,
                FormalName = excluded.FormalName,
                GSTIN = excluded.GSTIN,
                PAN = excluded.PAN,
                StateName = excluded.StateName,
                StateCode = excluded.StateCode,
                LastSyncDate = excluded.LastSyncDate,
                LastAlterId = excluded.LastAlterId,
                IsActive = excluded.IsActive;
        ";

        const string fySql = @"
            INSERT INTO FinancialYears (Id, CompanyId, StartDate, EndDate, IsAudited)
            VALUES (@Id, @CompanyId, @StartDate, @EndDate, @IsAudited)
            ON CONFLICT(Id) DO UPDATE SET
                StartDate = excluded.StartDate,
                EndDate = excluded.EndDate;
        ";

        await connection.ExecuteAsync(new CommandDefinition(compSql, company, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(fySql, fy, transaction, cancellationToken: cancellationToken));

        transaction.Commit();
    }

    public async Task<int> BatchUpsertGroupsAsync(IReadOnlyList<Group> groups, string companyId, CancellationToken cancellationToken = default)
    {
        if (groups.Count == 0) return 0;

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        const string sql = @"
            INSERT INTO Groups (Id, CompanyId, Name, ParentName, PrimaryGroup, AlterId)
            VALUES (@Id, @CompanyId, @Name, @ParentName, @PrimaryGroup, @AlterId)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                ParentName = excluded.ParentName,
                PrimaryGroup = excluded.PrimaryGroup,
                AlterId = excluded.AlterId;
        ";

        var count = await connection.ExecuteAsync(new CommandDefinition(sql, groups, transaction, cancellationToken: cancellationToken));
        transaction.Commit();
        return count;
    }

    public async Task<(int inserted, int updated)> BatchUpsertLedgersAsync(IReadOnlyList<Ledger> ledgers, string companyId, CancellationToken cancellationToken = default)
    {
        if (ledgers.Count == 0) return (0, 0);

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        var inserted = 0;
        var updated = 0;

        foreach (var l in ledgers)
        {
            const string checkSql = "SELECT COUNT(*) FROM Ledgers WHERE Id = @Id";
            var exists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(checkSql, new { l.Id }, transaction, cancellationToken: cancellationToken)) > 0;

            const string upsertSql = @"
                INSERT INTO Ledgers (Id, CompanyId, Name, ParentGroup, GSTIN, PAN, StateName, OpeningBalance, ClosingBalance, IsBillWise, TaxType, HsnCode, GstRate, AlterId)
                VALUES (@Id, @CompanyId, @Name, @ParentGroup, @GSTIN, @PAN, @StateName, @OpeningBalance, @ClosingBalance, @IsBillWise, @TaxType, @HsnCode, @GstRate, @AlterId)
                ON CONFLICT(Id) DO UPDATE SET
                    Name = excluded.Name,
                    ParentGroup = excluded.ParentGroup,
                    GSTIN = excluded.GSTIN,
                    PAN = excluded.PAN,
                    StateName = excluded.StateName,
                    OpeningBalance = excluded.OpeningBalance,
                    ClosingBalance = excluded.ClosingBalance,
                    TaxType = excluded.TaxType,
                    HsnCode = excluded.HsnCode,
                    GstRate = excluded.GstRate,
                    AlterId = excluded.AlterId;
            ";

            await connection.ExecuteAsync(new CommandDefinition(upsertSql, l, transaction, cancellationToken: cancellationToken));
            if (exists) updated++;
            else inserted++;
        }

        transaction.Commit();
        return (inserted, updated);
    }

    public async Task<int> BatchUpsertVoucherTypesAsync(IReadOnlyList<VoucherType> types, string companyId, CancellationToken cancellationToken = default)
    {
        if (types.Count == 0) return 0;

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        const string sql = @"
            INSERT INTO VoucherTypes (Id, CompanyId, Name, ParentType, NumberingMethod)
            VALUES (@Id, @CompanyId, @Name, @ParentType, @NumberingMethod)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                ParentType = excluded.ParentType,
                NumberingMethod = excluded.NumberingMethod;
        ";

        var count = await connection.ExecuteAsync(new CommandDefinition(sql, types, transaction, cancellationToken: cancellationToken));
        transaction.Commit();
        return count;
    }

    public async Task<(int inserted, int updated)> BatchUpsertVouchersAsync(IReadOnlyList<Voucher> vouchers, string companyId, CancellationToken cancellationToken = default)
    {
        if (vouchers.Count == 0) return (0, 0);

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        var inserted = 0;
        var updated = 0;

        const string checkSql = "SELECT COUNT(*) FROM Vouchers WHERE Id = @Id";
        const string voucherSql = @"
            INSERT INTO Vouchers (Id, CompanyId, VoucherTypeId, VoucherTypeName, VoucherNumber, ReferenceNumber, VoucherDate, EffectiveDate, Narration, TotalAmount, IsCancelled, IsOptional, PartyLedgerName, AlterId, CreatedAt)
            VALUES (@Id, @CompanyId, @VoucherTypeId, @VoucherTypeName, @VoucherNumber, @ReferenceNumber, @VoucherDate, @EffectiveDate, @Narration, @TotalAmount, @IsCancelled, @IsOptional, @PartyLedgerName, @AlterId, @CreatedAt)
            ON CONFLICT(Id) DO UPDATE SET
                VoucherTypeId = excluded.VoucherTypeId,
                VoucherTypeName = excluded.VoucherTypeName,
                VoucherNumber = excluded.VoucherNumber,
                ReferenceNumber = excluded.ReferenceNumber,
                VoucherDate = excluded.VoucherDate,
                EffectiveDate = excluded.EffectiveDate,
                Narration = excluded.Narration,
                TotalAmount = excluded.TotalAmount,
                IsCancelled = excluded.IsCancelled,
                IsOptional = excluded.IsOptional,
                PartyLedgerName = excluded.PartyLedgerName,
                AlterId = excluded.AlterId;
        ";

        const string deleteEntriesSql = "DELETE FROM VoucherEntries WHERE VoucherId = @VoucherId";
        const string entrySql = @"
            INSERT INTO VoucherEntries (Id, VoucherId, LedgerName, Amount, IsDebit, BillRefType, BillName)
            VALUES (@Id, @VoucherId, @LedgerName, @Amount, @IsDebit, @BillRefType, @BillName);
        ";

        foreach (var v in vouchers)
        {
            var exists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(checkSql, new { v.Id }, transaction, cancellationToken: cancellationToken)) > 0;

            await connection.ExecuteAsync(new CommandDefinition(voucherSql, v, transaction, cancellationToken: cancellationToken));

            // Replace line entries atomically
            await connection.ExecuteAsync(new CommandDefinition(deleteEntriesSql, new { VoucherId = v.Id }, transaction, cancellationToken: cancellationToken));

            if (v.Entries.Count > 0)
            {
                await connection.ExecuteAsync(new CommandDefinition(entrySql, v.Entries, transaction, cancellationToken: cancellationToken));
            }

            if (exists) updated++;
            else inserted++;
        }

        transaction.Commit();
        return (inserted, updated);
    }

    public async Task RecordSyncHistoryAsync(SyncHistoryRecord record, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = @"
            INSERT INTO SyncHistory (Id, CompanyId, SyncType, VouchersFetched, MastersFetched, DurationMs, Status, ErrorMessage, Timestamp)
            VALUES (@Id, @CompanyId, @SyncType, @VouchersFetched, @MastersFetched, @DurationMs, @Status, @ErrorMessage, @Timestamp);
        ";
        await connection.ExecuteAsync(new CommandDefinition(sql, record, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<SyncHistoryRecord>> GetSyncHistoryAsync(string companyId, int limit = 20, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = @"
            SELECT * FROM SyncHistory 
            WHERE CompanyId = @CompanyId 
            ORDER BY Timestamp DESC 
            LIMIT @Limit;
        ";
        var result = await connection.QueryAsync<SyncHistoryRecord>(new CommandDefinition(sql, new { CompanyId = companyId, Limit = limit }, cancellationToken: cancellationToken));
        return result.ToList();
    }

    public async Task OptimizeIndexesAsync(CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("PRAGMA optimize;", cancellationToken: cancellationToken));
    }
}
