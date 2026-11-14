namespace TallyAuditAssistant.Core.Domain.Practice;

/// <summary>
/// A CA-firm client record. Kept separate from Tally Companies because a client
/// may own or operate multiple legal entities and accounting books.
/// </summary>
public sealed class PracticeClient
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ClientCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ClientType { get; set; } = "Business";
    public string? PAN { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string Status { get; set; } = "Active";
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A legal/tax registration or accounting entity belonging to a practice client.</summary>
public sealed class PracticeClientEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ClientId { get; set; } = string.Empty;
    public string LegalName { get; set; } = string.Empty;
    public string? PAN { get; set; }
    public string? GSTIN { get; set; }
    public string? StateCode { get; set; }
    public string? TallyCompanyId { get; set; }
    public string Status { get; set; } = "Active";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A scoped professional assignment for a client/entity and financial year.
/// Status values are workflow labels, not proof of statutory filing.
/// </summary>
public sealed class ClientEngagement
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ClientId { get; set; } = string.Empty;
    public string? ClientEntityId { get; set; }
    public string ServiceCode { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string FinancialYear { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    public string? AssignedToUserId { get; set; }
    public string? ReviewerUserId { get; set; }
    public DateTime? DueDate { get; set; }
    public decimal? ProfessionalFee { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}


/// <summary>Client-scoped schedule for recurring compliance work; not evidence of filing.</summary>
public sealed class RecurringComplianceTaskTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ClientId { get; set; } = string.Empty;
    public string? ClientEntityId { get; set; }
    public string ServiceCode { get; set; } = string.Empty;
    public string TaskName { get; set; } = string.Empty;
    public string Frequency { get; set; } = "Monthly";
    public int IntervalMonths { get; set; } = 1;
    public DateTime NextDueDate { get; set; } = DateTime.Today;
    public int ReminderLeadDays { get; set; } = 7;
    public string? AssignedToUserId { get; set; }
    public string? ReviewerUserId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
