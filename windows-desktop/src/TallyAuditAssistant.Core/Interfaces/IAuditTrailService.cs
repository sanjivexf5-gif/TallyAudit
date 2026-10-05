using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TallyAuditAssistant.Core.Domain.Audit;

namespace TallyAuditAssistant.Core.Interfaces;

/// <summary>
/// Service responsible for recording and querying immutable audit trail entries across the application lifecycle.
/// </summary>
public interface IAuditTrailService
{
    Task RecordActivityAsync(
        string actionType,
        string module,
        string description,
        string? entityType = null,
        string? entityId = null,
        string? previousState = null,
        string? newState = null,
        string? details = null,
        string? companyName = null,
        string? financialYear = null,
        CancellationToken ct = default);

    Task RecordActivityAsync(
        string action,
        string category,
        string description,
        string? companyId = null,
        string? financialPeriodId = null,
        string? entityType = null,
        string? entityId = null,
        string? metadataJson = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<AuditTrailEntry>> GetEntriesAsync(
        string? searchTerm = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        string? companyName = null,
        string? module = null,
        string? actionType = null,
        string? userName = null,
        int limit = 1000,
        CancellationToken ct = default);

    Task<IReadOnlyList<AuditTrailEntry>> GetAuditTrailAsync(
        string? companyId = null,
        string? financialPeriodId = null,
        int limit = 100,
        CancellationToken ct = default);

    Task<byte[]> ExportToExcelAsync(IReadOnlyList<AuditTrailEntry> entries, CancellationToken ct = default);
    Task<byte[]> ExportToPdfAsync(IReadOnlyList<AuditTrailEntry> entries, CancellationToken ct = default);
}

