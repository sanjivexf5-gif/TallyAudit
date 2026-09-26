using TallyAuditAssistant.Core.Domain.Security;

namespace TallyAuditAssistant.Core.Interfaces;

public interface IUserAuthenticationService
{
    UserSession? CurrentSession { get; }
    AppUser? CurrentUser => CurrentSession?.User;
    bool IsAuthenticated => CurrentSession != null && CurrentSession.IsActive;

    Task<UserSession> AuthenticateAsync(string username, string password, CancellationToken ct = default);
    Task<bool> ChangePasswordAsync(string userId, string currentPassword, string newPassword, CancellationToken ct = default);
    Task<AppUser> CreateUserAsync(AppUser user, string initialPassword, CancellationToken ct = default);
    Task<bool> UpdateUserAsync(AppUser user, CancellationToken ct = default);
    Task<AppUser?> GetUserByIdAsync(string userId, CancellationToken ct = default);
    Task<IReadOnlyList<AppUser>> GetUsersAsync(CancellationToken ct = default);
    void Logout();
}
