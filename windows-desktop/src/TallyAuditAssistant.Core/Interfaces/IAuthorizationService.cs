using TallyAuditAssistant.Core.Domain.Security;

namespace TallyAuditAssistant.Core.Interfaces;

public interface IAuthorizationService
{
    bool HasPermission(UserRole role, AuditPermission permission);
    bool HasPermission(AuditPermission permission);
    void DemandPermission(AuditPermission permission);
}
