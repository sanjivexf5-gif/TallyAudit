using TallyAuditAssistant.Core.Domain.Security;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Engine.Services;

public class AuthorizationService : IAuthorizationService
{
    private readonly IUserAuthenticationService _authenticationService;

    public AuthorizationService(IUserAuthenticationService authenticationService)
    {
        _authenticationService = authenticationService;
    }

    public bool HasPermission(UserRole role, AuditPermission permission)
    {
        return role switch
        {
            UserRole.Administrator => true, // Full access to all features

            UserRole.Auditor => permission switch
            {
                AuditPermission.UserManagement => false,
                AuditPermission.ReopenFinalizedAudit => false,
                AuditPermission.BackupRestore => false,
                _ => true // Full audit execution, findings, planning, evidence, reporting
            },

            UserRole.Reviewer => permission switch
            {
                AuditPermission.FindingsReview => true,
                AuditPermission.Reconciliation => true,
                AuditPermission.WorkingPapers => true,
                AuditPermission.ReportGeneration => true,
                AuditPermission.EvidenceManagement => true,
                AuditPermission.RiskMateriality => true,
                AuditPermission.AuditPlanning => true,
                _ => false // Cannot execute raw sync, change system settings, or manage users
            },

            UserRole.ReadOnly => permission switch
            {
                AuditPermission.ReportGeneration => true,
                _ => false // Strictly read-only viewing of reports and finalized files
            },

            _ => false
        };
    }

    public bool HasPermission(AuditPermission permission)
    {
        var session = _authenticationService.CurrentSession;
        if (session == null || !session.IsActive)
            return false;

        return HasPermission(session.User.Role, permission);
    }

    public void DemandPermission(AuditPermission permission)
    {
        if (!HasPermission(permission))
        {
            var role = _authenticationService.CurrentUser?.Role.ToString() ?? "Unauthenticated";
            throw new UnauthorizedAccessException($"Access Denied: Current role '{role}' does not possess required permission '{permission}'.");
        }
    }
}
