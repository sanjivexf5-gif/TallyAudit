using System;
using TallyAuditAssistant.Core.Common;

namespace TallyAuditAssistant.Core.Domain.Audit;

/// <summary>
/// Immutable audit trail log entry capturing user and system activities for statutory compliance and forensic integrity.
/// </summary>
public class AuditTrailEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string UserName { get; set; } = Environment.UserName;
    public string ActionType { get; set; } = string.Empty;
    public string Module { get; set; } = "SYSTEM";
    public string? CompanyName { get; set; }
    public string? FinancialYear { get; set; }
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? PreviousState { get; set; }
    public string? NewState { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string ApplicationVersion { get; set; } = AppVersion.Version;
    public string MachineName { get; set; } = Environment.MachineName;

    // Backward compatibility aliases & helpers
    public DateTime Timestamp
    {
        get => TimestampUtc;
        set => TimestampUtc = value;
    }

    public string Action
    {
        get => ActionType;
        set => ActionType = value;
    }

    public string Category
    {
        get => Module;
        set => Module = value;
    }

    public string Username
    {
        get => UserName;
        set => UserName = value;
    }

    public string? CompanyId
    {
        get => CompanyName;
        set => CompanyName = value;
    }

    public string? FinancialPeriodId
    {
        get => FinancialYear;
        set => FinancialYear = value;
    }

    public string? MetadataJson
    {
        get => Details;
        set => Details = value;
    }

    public string IntegrityHash { get; set; } = string.Empty;
}

