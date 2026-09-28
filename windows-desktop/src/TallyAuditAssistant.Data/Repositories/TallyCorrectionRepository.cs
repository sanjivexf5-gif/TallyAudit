using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using TallyAuditAssistant.Core.Domain.Corrections;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Data.Repositories;

public class TallyCorrectionRepository : ITallyCorrectionRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public TallyCorrectionRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task SaveCorrectionAsync(TallyCorrection correction, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = @"
            INSERT INTO TallyCorrections (
                Id, CompanyId, FinancialPeriodId, AuditFindingId, VoucherId, VoucherNumber, LedgerId, LedgerName,
                CorrectionType, FieldName, OriginalValue, ProposedValue, OriginalAmount, ProposedAmount, Reason,
                EvidenceId, Status, CreatedBy, CreatedAt, ApprovedBy, ApprovedAt, AppliedBy, AppliedAt,
                TallyResponse, TallyTransactionReference, VerificationStatus, VerifiedAt, FailureReason,
                BeforeSnapshot, AfterSnapshot, CorrelationId, IsAiAssisted
            ) VALUES (
                @Id, @CompanyId, @FinancialPeriodId, @AuditFindingId, @VoucherId, @VoucherNumber, @LedgerId, @LedgerName,
                @CorrectionType, @FieldName, @OriginalValue, @ProposedValue, @OriginalAmount, @ProposedAmount, @Reason,
                @EvidenceId, @Status, @CreatedBy, @CreatedAt, @ApprovedBy, @ApprovedAt, @AppliedBy, @AppliedAt,
                @TallyResponse, @TallyTransactionReference, @VerificationStatus, @VerifiedAt, @FailureReason,
                @BeforeSnapshot, @AfterSnapshot, @CorrelationId, @IsAiAssisted
            )
            ON CONFLICT(Id) DO UPDATE SET
                Status = excluded.Status,
                ApprovedBy = excluded.ApprovedBy,
                ApprovedAt = excluded.ApprovedAt,
                AppliedBy = excluded.AppliedBy,
                AppliedAt = excluded.AppliedAt,
                TallyResponse = excluded.TallyResponse,
                TallyTransactionReference = excluded.TallyTransactionReference,
                VerificationStatus = excluded.VerificationStatus,
                VerifiedAt = excluded.VerifiedAt,
                FailureReason = excluded.FailureReason,
                AfterSnapshot = excluded.AfterSnapshot;
        ";
        await connection.ExecuteAsync(new CommandDefinition(sql, correction, cancellationToken: cancellationToken));
    }

    public async Task<TallyCorrection?> GetCorrectionByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = "SELECT * FROM TallyCorrections WHERE Id = @Id LIMIT 1;";
        return await connection.QuerySingleOrDefaultAsync<TallyCorrection>(new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<TallyCorrection>> GetCorrectionsAsync(string companyId, CorrectionStatus? statusFilter = null, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        string sql = "SELECT * FROM TallyCorrections WHERE CompanyId = @CompanyId";
        if (statusFilter.HasValue)
        {
            sql += " AND Status = @StatusFilter";
        }
        sql += " ORDER BY CreatedAt DESC;";

        var results = await connection.QueryAsync<TallyCorrection>(new CommandDefinition(sql, new { CompanyId = companyId, StatusFilter = statusFilter }, cancellationToken: cancellationToken));
        return results.ToList();
    }

    public async Task UpdateCorrectionStatusAsync(string id, CorrectionStatus status, string? updatedBy, string? note, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = @"
            UPDATE TallyCorrections 
            SET Status = @Status,
                FailureReason = COALESCE(@Note, FailureReason)
            WHERE Id = @Id;
        ";
        await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = id, Status = status, Note = note }, cancellationToken: cancellationToken));
    }

    public async Task RecordCorrectionAuditTrailAsync(string correctionId, string companyId, string action, string actor, string details, string correlationId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = @"
            INSERT INTO TallyCorrectionAuditLogs (
                Id, CorrectionId, CompanyId, Action, Actor, Details, CorrelationId, Timestamp
            ) VALUES (
                @Id, @CorrectionId, @CompanyId, @Action, @Actor, @Details, @CorrelationId, CURRENT_TIMESTAMP
            );
        ";
        var entry = new TallyCorrectionAuditEntry
        {
            CorrectionId = correctionId,
            CompanyId = companyId,
            Action = action,
            Actor = actor,
            Details = details,
            CorrelationId = correlationId
        };
        await connection.ExecuteAsync(new CommandDefinition(sql, entry, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<TallyCorrectionAuditEntry>> GetCorrectionAuditTrailAsync(string correctionId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = "SELECT * FROM TallyCorrectionAuditLogs WHERE CorrectionId = @CorrectionId ORDER BY Timestamp ASC;";
        var results = await connection.QueryAsync<TallyCorrectionAuditEntry>(new CommandDefinition(sql, new { CorrectionId = correctionId }, cancellationToken: cancellationToken));
        return results.ToList();
    }
}
