using TallyAuditAssistant.Core.Domain.Security;

namespace TallyAuditAssistant.Core.Interfaces;

public interface IUserRepository
{
    Task<AppUser?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<AppUser?> GetByUsernameAsync(string username, CancellationToken ct = default);
    Task<IReadOnlyList<AppUser>> GetAllAsync(CancellationToken ct = default);
    Task<int> GetCountAsync(CancellationToken ct = default);
    Task<bool> CreateAsync(AppUser user, CancellationToken ct = default);
    Task<bool> UpdateAsync(AppUser user, CancellationToken ct = default);
    Task<bool> UpdateLastLoginAsync(string id, DateTime loginTime, CancellationToken ct = default);
    Task<bool> UpdatePasswordAsync(string id, string passwordHash, string passwordSalt, CancellationToken ct = default);
}
