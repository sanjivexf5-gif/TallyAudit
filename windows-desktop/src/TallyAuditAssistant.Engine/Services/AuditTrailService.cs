using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data.Repositories;

namespace TallyAuditAssistant.Engine.Services;

public class AuditTrailService : IAuditTrailService
{
    private readonly IAuditTrailRepository _repository;
    private readonly ILogger<AuditTrailService> _logger;

    public AuditTrailService(IAuditTrailRepository repository, ILogger<AuditTrailService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task RecordActivityAsync(
        string action,
        string category,
        string description,
        string? companyId = null,
        string? financialPeriodId = null,
        string? entityType = null,
        string? entityId = null,
        string? metadataJson = null,
        CancellationToken ct = default)
    {
        var timestamp = DateTime.UtcNow;
        var username = "System"; // Will be overridden or populated by session if available

        string rawHashString = $"{timestamp:O}|{username}|{action}|{category}|{companyId}|{financialPeriodId}|{entityId}|{description}";
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawHashString));
        string integrityHash = "SHA256-" + Convert.ToHexString(hashBytes)[..16];

        var entry = new AuditTrailEntry
        {
            Id = $"LOG-{timestamp:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..6]}",
            Timestamp = timestamp,
            Username = username,
            Action = action,
            Category = category,
            CompanyId = companyId,
            FinancialPeriodId = financialPeriodId,
            EntityType = entityType,
            EntityId = entityId,
            Description = description,
            MetadataJson = metadataJson,
            IntegrityHash = integrityHash
        };

        try
        {
            await _repository.InsertAsync(entry, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist audit trail record for action {Action}.", action);
        }
    }

    public Task<IReadOnlyList<AuditTrailEntry>> GetAuditTrailAsync(
        string? companyId = null,
        string? financialPeriodId = null,
        int limit = 100,
        CancellationToken ct = default)
    {
        return _repository.GetEntriesAsync(companyId, financialPeriodId, limit, ct);
    }
}
