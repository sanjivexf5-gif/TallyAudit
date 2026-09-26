namespace TallyAuditAssistant.Core.Domain.Security;

public enum UserRole
{
    Administrator = 0,
    Auditor = 1,
    Reviewer = 2,
    ReadOnly = 3
}

public enum AuditPermission
{
    ApplicationSettings,
    TallyConnection,
    Synchronization,
    AuditExecution,
    AuditPlanning,
    RiskMateriality,
    FindingsReview,
    Reconciliation,
    Sampling,
    EvidenceManagement,
    WorkingPapers,
    ReportGeneration,
    UserManagement,
    BackupRestore,
    AuditFinalization,
    ReopenFinalizedAudit
}

public class AppUser
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Auditor;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}

public class UserSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString();
    public AppUser User { get; set; } = null!;
    public DateTime LoginTimestamp { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityTimestamp { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}
