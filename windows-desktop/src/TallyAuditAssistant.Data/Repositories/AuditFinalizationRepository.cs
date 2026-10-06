using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Data.Repositories;

public class AuditFinalizationRepository : IAuditFinalizationRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public AuditFinalizationRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<AuditFinalizationState?> GetStateAsync(string companyId, string financialPeriodId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = "SELECT * FROM AuditFinalizationStates WHERE CompanyId = @CompanyId AND FinancialPeriodId = @FinancialPeriodId LIMIT 1;";
        return await connection.QuerySingleOrDefaultAsync<AuditFinalizationState>(new CommandDefinition(sql, new { CompanyId = companyId, FinancialPeriodId = financialPeriodId }, cancellationToken: cancellationToken));
    }

    public async Task<AuditFinalizationState?> GetStateByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = "SELECT * FROM AuditFinalizationStates WHERE Id = @Id LIMIT 1;";
        return await connection.QuerySingleOrDefaultAsync<AuditFinalizationState>(new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task SaveStateAsync(AuditFinalizationState state, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = @"
            INSERT INTO AuditFinalizationStates (
                Id, CompanyId, FinancialPeriodId, Status, CompletionPercentage,
                AuditorConclusionStatus, AuditorConclusionText, AuditorConclusionBasis, AuditorConclusionDate, AuditorConclusionPreparedBy,
                ReviewerName, ReviewerComments, ReviewedAt, FinalizedBy, FinalizedAt
            ) VALUES (
                @Id, @CompanyId, @FinancialPeriodId, @Status, @CompletionPercentage,
                @AuditorConclusionStatus, @AuditorConclusionText, @AuditorConclusionBasis, @AuditorConclusionDate, @AuditorConclusionPreparedBy,
                @ReviewerName, @ReviewerComments, @ReviewedAt, @FinalizedBy, @FinalizedAt
            )
            ON CONFLICT(Id) DO UPDATE SET
                Status = excluded.Status,
                CompletionPercentage = excluded.CompletionPercentage,
                AuditorConclusionStatus = excluded.AuditorConclusionStatus,
                AuditorConclusionText = excluded.AuditorConclusionText,
                AuditorConclusionBasis = excluded.AuditorConclusionBasis,
                AuditorConclusionDate = excluded.AuditorConclusionDate,
                AuditorConclusionPreparedBy = excluded.AuditorConclusionPreparedBy,
                ReviewerName = excluded.ReviewerName,
                ReviewerComments = excluded.ReviewerComments,
                ReviewedAt = excluded.ReviewedAt,
                FinalizedBy = excluded.FinalizedBy,
                FinalizedAt = excluded.FinalizedAt;
        ";
        await connection.ExecuteAsync(new CommandDefinition(sql, state, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<ChecklistItem>> GetChecklistAsync(string auditId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = "SELECT * FROM AuditChecklistItems WHERE AuditId = @AuditId;";
        var results = await connection.QueryAsync<ChecklistItem>(new CommandDefinition(sql, new { AuditId = auditId }, cancellationToken: cancellationToken));
        return results.ToList();
    }

    public async Task<ChecklistItem?> GetChecklistItemByIdAsync(string itemId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = "SELECT * FROM AuditChecklistItems WHERE Id = @Id LIMIT 1;";
        return await connection.QuerySingleOrDefaultAsync<ChecklistItem>(new CommandDefinition(sql, new { Id = itemId }, cancellationToken: cancellationToken));
    }

    public async Task SaveChecklistItemAsync(ChecklistItem item, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = @"
            INSERT INTO AuditChecklistItems (
                Id, AuditId, Section, Code, Description, IsCompleted, CompletedAt, CompletedBy, Notes, SourceReference
            ) VALUES (
                @Id, @AuditId, @Section, @Code, @Description, @IsCompleted, @CompletedAt, @CompletedBy, @Notes, @SourceReference
            )
            ON CONFLICT(Id) DO UPDATE SET
                IsCompleted = excluded.IsCompleted,
                CompletedAt = excluded.CompletedAt,
                CompletedBy = excluded.CompletedBy,
                Notes = excluded.Notes,
                SourceReference = excluded.SourceReference;
        ";
        await connection.ExecuteAsync(new CommandDefinition(sql, item, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<OpenItem>> GetOpenItemsAsync(string auditId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = "SELECT * FROM AuditOpenItems WHERE AuditId = @AuditId;";
        var results = await connection.QueryAsync<OpenItem>(new CommandDefinition(sql, new { AuditId = auditId }, cancellationToken: cancellationToken));
        return results.ToList();
    }

    public async Task SaveOpenItemAsync(OpenItem item, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = @"
            INSERT INTO AuditOpenItems (
                Id, AuditId, Description, Category, Priority, Owner, DueDate, Status, RelatedFindingId, RelatedProcedureId, RelatedEvidenceId, Remarks
            ) VALUES (
                @Id, @AuditId, @Description, @Category, @Priority, @Owner, @DueDate, @Status, @RelatedFindingId, @RelatedProcedureId, @RelatedEvidenceId, @Remarks
            )
            ON CONFLICT(Id) DO UPDATE SET
                Description = excluded.Description,
                Category = excluded.Category,
                Priority = excluded.Priority,
                Owner = excluded.Owner,
                DueDate = excluded.DueDate,
                Status = excluded.Status,
                RelatedFindingId = excluded.RelatedFindingId,
                RelatedProcedureId = excluded.RelatedProcedureId,
                RelatedEvidenceId = excluded.RelatedEvidenceId,
                Remarks = excluded.Remarks;
        ";
        await connection.ExecuteAsync(new CommandDefinition(sql, item, cancellationToken: cancellationToken));
    }

    public async Task DeleteOpenItemAsync(string id, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = "DELETE FROM AuditOpenItems WHERE Id = @Id;";
        await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<ReviewNote>> GetReviewNotesAsync(string auditId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = "SELECT * FROM AuditReviewNotes WHERE AuditId = @AuditId ORDER BY CreatedAt ASC;";
        var results = await connection.QueryAsync<ReviewNote>(new CommandDefinition(sql, new { AuditId = auditId }, cancellationToken: cancellationToken));
        return results.ToList();
    }

    public async Task SaveReviewNoteAsync(ReviewNote note, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = @"
            INSERT INTO AuditReviewNotes (
                Id, AuditId, Area, Reference, Reviewer, CreatedAt, Comment, Status, ResolvedBy, ResolvedAt, Resolution
            ) VALUES (
                @Id, @AuditId, @Area, @Reference, @Reviewer, @CreatedAt, @Comment, @Status, @ResolvedBy, @ResolvedAt, @Resolution
            )
            ON CONFLICT(Id) DO UPDATE SET
                Status = excluded.Status,
                ResolvedBy = excluded.ResolvedBy,
                ResolvedAt = excluded.ResolvedAt,
                Resolution = excluded.Resolution;
        ";
        await connection.ExecuteAsync(new CommandDefinition(sql, note, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<AuditAmendment>> GetAmendmentsAsync(string auditId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = "SELECT * FROM AuditAmendments WHERE AuditId = @AuditId ORDER BY RequestedAt DESC;";
        var results = await connection.QueryAsync<AuditAmendment>(new CommandDefinition(sql, new { AuditId = auditId }, cancellationToken: cancellationToken));
        return results.ToList();
    }

    public async Task<string> GetOrCreateWorkingPaperPlanIdAsync(string companyId, string companyName, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string findSql = "SELECT Id FROM AuditPlans WHERE CompanyId = @CompanyId AND FinancialPeriodId = 'WORKING-PAPER' LIMIT 1;";
        var existing = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(findSql, new { CompanyId = companyId }, cancellationToken: cancellationToken));
        if (!string.IsNullOrWhiteSpace(existing)) return existing;

        var id = $"WP-PLAN-{companyId}";
        const string sql = @"INSERT OR IGNORE INTO AuditPlans (Id, CompanyId, FinancialPeriodId, Status, MaterialityAmount, PerformanceMateriality, TrivialThreshold, IsFinalized, CreatedAt, UpdatedAt)
                             VALUES (@Id, @CompanyId, 'WORKING-PAPER', 'In Progress', 250000, 187500, 12500, 0, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);";
        await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = id, CompanyId = companyId }, cancellationToken: cancellationToken));
        return id;
    }

    public async Task<IReadOnlyList<WorkingPaper>> GetWorkingPapersAsync(string planId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = @"SELECT wp.*, ap.CompanyId AS CompanyId, c.TallyCompanyName AS CompanyName
                             FROM WorkingPapers wp
                             INNER JOIN AuditPlans ap ON ap.Id = wp.PlanId
                             INNER JOIN Companies c ON c.Id = ap.CompanyId
                             WHERE wp.PlanId = @PlanId ORDER BY wp.PreparedDate DESC, wp.Title ASC;";
        var rows = await connection.QueryAsync<WorkingPaper>(new CommandDefinition(sql, new { PlanId = planId }, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task SaveWorkingPaperAsync(WorkingPaper paper, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = @"INSERT INTO WorkingPapers (Id, PlanId, AuditArea, Title, Objective, ProcedurePerformed, Conclusion, Status, PreparedBy, PreparedDate, ReviewedBy, ReviewedDate, RelatedFindingId)
                             VALUES (@Id, @PlanId, @AuditArea, @Title, @Objective, @ProcedurePerformed, @Conclusion, @Status, @PreparedBy, @PreparedDate, @ReviewedBy, @ReviewedDate, @RelatedFindingId)
                             ON CONFLICT(Id) DO UPDATE SET AuditArea=excluded.AuditArea, Title=excluded.Title, Objective=excluded.Objective,
                             ProcedurePerformed=excluded.ProcedurePerformed, Conclusion=excluded.Conclusion, Status=excluded.Status,
                             PreparedBy=excluded.PreparedBy, PreparedDate=excluded.PreparedDate, ReviewedBy=excluded.ReviewedBy,
                             ReviewedDate=excluded.ReviewedDate, RelatedFindingId=excluded.RelatedFindingId;";
        await connection.ExecuteAsync(new CommandDefinition(sql, paper, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<WorkingPaperAttachment>> GetWorkingPaperAttachmentsAsync(string workingPaperId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = "SELECT * FROM WorkingPaperAttachments WHERE WorkingPaperId = @WorkingPaperId ORDER BY AddedAt DESC;";
        var rows = await connection.QueryAsync<WorkingPaperAttachment>(new CommandDefinition(sql, new { WorkingPaperId = workingPaperId }, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task SaveWorkingPaperAttachmentAsync(WorkingPaperAttachment attachment, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = @"INSERT INTO WorkingPaperAttachments (Id, WorkingPaperId, FileName, FilePath, FileHash, FileSizeBytes, AddedAt, AddedBy)
                             VALUES (@Id, @WorkingPaperId, @FileName, @FilePath, @FileHash, @FileSizeBytes, @AddedAt, @AddedBy);";
        await connection.ExecuteAsync(new CommandDefinition(sql, attachment, cancellationToken: cancellationToken));
    }

    public async Task SaveAmendmentAsync(AuditAmendment amendment, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = @"
            INSERT INTO AuditAmendments (
                Id, AuditId, Reason, RequestedBy, RequestedAt, ApprovedBy, ApprovedAt, Status, Description
            ) VALUES (
                @Id, @AuditId, @Reason, @RequestedBy, @RequestedAt, @ApprovedBy, @ApprovedAt, @Status, @Description
            )
            ON CONFLICT(Id) DO UPDATE SET
                ApprovedBy = excluded.ApprovedBy,
                ApprovedAt = excluded.ApprovedAt,
                Status = excluded.Status,
                Description = excluded.Description;
        ";
        await connection.ExecuteAsync(new CommandDefinition(sql, amendment, cancellationToken: cancellationToken));
    }
}
