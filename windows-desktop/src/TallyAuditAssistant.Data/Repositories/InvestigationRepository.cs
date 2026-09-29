using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Data.Repositories;

public class InvestigationRepository : IInvestigationRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<InvestigationRepository> _logger;

    public InvestigationRepository(SqliteConnectionFactory connectionFactory, ILogger<InvestigationRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<ExceptionInvestigation?> GetByIdAsync(string investigationId, CancellationToken ct = default)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = "SELECT * FROM ExceptionInvestigations WHERE Id = @Id LIMIT 1;";
        var item = await conn.QuerySingleOrDefaultAsync<ExceptionInvestigation>(new CommandDefinition(sql, new { Id = investigationId }, cancellationToken: ct));
        if (item != null)
        {
            var checklist = await GetChecklistItemsAsync(item.Id, ct);
            item.ChecklistItems = checklist.ToList();
        }
        return item;
    }

    public async Task<ExceptionInvestigation?> GetByExceptionIdAsync(string exceptionId, CancellationToken ct = default)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = "SELECT * FROM ExceptionInvestigations WHERE ExceptionId = @ExceptionId LIMIT 1;";
        var item = await conn.QuerySingleOrDefaultAsync<ExceptionInvestigation>(new CommandDefinition(sql, new { ExceptionId = exceptionId }, cancellationToken: ct));
        if (item != null)
        {
            var checklist = await GetChecklistItemsAsync(item.Id, ct);
            item.ChecklistItems = checklist.ToList();
        }
        return item;
    }

    public async Task<IReadOnlyList<ExceptionInvestigation>> GetByCompanyIdAsync(string companyId, InvestigationStatus? status = null, CancellationToken ct = default)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        string sql = "SELECT * FROM ExceptionInvestigations WHERE CompanyId = @CompanyId";
        if (status.HasValue)
        {
            sql += " AND Status = @Status";
        }
        sql += " ORDER BY CreatedAt DESC;";

        var items = (await conn.QueryAsync<ExceptionInvestigation>(
            new CommandDefinition(sql, new { CompanyId = companyId, Status = (int?)status }, cancellationToken: ct))).ToList();

        foreach (var item in items)
        {
            var checklist = await GetChecklistItemsAsync(item.Id, ct);
            item.ChecklistItems = checklist.ToList();
        }

        return items;
    }

    public async Task SaveInvestigationAsync(ExceptionInvestigation investigation, CancellationToken ct = default)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = @"
            INSERT INTO ExceptionInvestigations (
                Id, ExceptionId, CompanyId, FinancialPeriodId, AuditRunId,
                Status, RootCause, AuditorNotes, ManagementResponse,
                ProposedCorrectiveAction, ReviewerNotes, CreatedAt, UpdatedAt,
                CreatedBy, UpdatedBy, ClosedAt
            ) VALUES (
                @Id, @ExceptionId, @CompanyId, @FinancialPeriodId, @AuditRunId,
                @Status, @RootCause, @AuditorNotes, @ManagementResponse,
                @ProposedCorrectiveAction, @ReviewerNotes, @CreatedAt, @UpdatedAt,
                @CreatedBy, @UpdatedBy, @ClosedAt
            );";

        await conn.ExecuteAsync(new CommandDefinition(sql, investigation, cancellationToken: ct));

        if (investigation.ChecklistItems != null && investigation.ChecklistItems.Count > 0)
        {
            await SaveChecklistItemsAsync(investigation.ChecklistItems, ct);
        }
    }

    public async Task UpdateInvestigationAsync(ExceptionInvestigation investigation, CancellationToken ct = default)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = @"
            UPDATE ExceptionInvestigations SET
                Status = @Status,
                RootCause = @RootCause,
                AuditorNotes = @AuditorNotes,
                ManagementResponse = @ManagementResponse,
                ProposedCorrectiveAction = @ProposedCorrectiveAction,
                ReviewerNotes = @ReviewerNotes,
                UpdatedAt = @UpdatedAt,
                UpdatedBy = @UpdatedBy,
                ClosedAt = @ClosedAt
            WHERE Id = @Id;";

        investigation.UpdatedAt = DateTime.UtcNow;
        await conn.ExecuteAsync(new CommandDefinition(sql, investigation, cancellationToken: ct));
    }

    public async Task UpdateStatusAsync(string investigationId, InvestigationStatus newStatus, string updatedBy, DateTime? closedAt, CancellationToken ct = default)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = @"
            UPDATE ExceptionInvestigations SET
                Status = @Status,
                UpdatedBy = @UpdatedBy,
                UpdatedAt = CURRENT_TIMESTAMP,
                ClosedAt = @ClosedAt
            WHERE Id = @Id;";

        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = investigationId,
            Status = (int)newStatus,
            UpdatedBy = updatedBy,
            ClosedAt = closedAt
        }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<InvestigationChecklistItem>> GetChecklistItemsAsync(string investigationId, CancellationToken ct = default)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = "SELECT * FROM InvestigationChecklistItems WHERE InvestigationId = @InvestigationId ORDER BY Code ASC;";
        var items = await conn.QueryAsync<InvestigationChecklistItem>(new CommandDefinition(sql, new { InvestigationId = investigationId }, cancellationToken: ct));
        return items.ToList();
    }

    public async Task SaveChecklistItemsAsync(IEnumerable<InvestigationChecklistItem> items, CancellationToken ct = default)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = @"
            INSERT OR REPLACE INTO InvestigationChecklistItems (
                Id, InvestigationId, Code, Description, IsCompleted, CompletedAt, CompletedBy, Notes
            ) VALUES (
                @Id, @InvestigationId, @Code, @Description, @IsCompleted, @CompletedAt, @CompletedBy, @Notes
            );";

        await conn.ExecuteAsync(new CommandDefinition(sql, items, cancellationToken: ct));
    }

    public async Task UpdateChecklistItemAsync(InvestigationChecklistItem item, CancellationToken ct = default)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = @"
            UPDATE InvestigationChecklistItems SET
                IsCompleted = @IsCompleted,
                CompletedAt = @CompletedAt,
                CompletedBy = @CompletedBy,
                Notes = @Notes
            WHERE Id = @Id;";

        await conn.ExecuteAsync(new CommandDefinition(sql, item, cancellationToken: ct));
    }
}
