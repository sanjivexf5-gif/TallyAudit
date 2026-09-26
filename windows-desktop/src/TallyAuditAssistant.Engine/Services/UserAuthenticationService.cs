using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Domain.Security;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data.Repositories;
using TallyAuditAssistant.Data.Security;

namespace TallyAuditAssistant.Engine.Services;

public class UserAuthenticationService : IUserAuthenticationService
{
    private readonly IUserRepository _userRepository;
    private readonly IAuditTrailService _auditTrailService;
    private readonly ILogger<UserAuthenticationService> _logger;
    private UserSession? _currentSession;

    public UserAuthenticationService(
        IUserRepository userRepository,
        IAuditTrailService auditTrailService,
        ILogger<UserAuthenticationService> logger)
    {
        _userRepository = userRepository;
        _auditTrailService = auditTrailService;
        _logger = logger;
    }

    public UserSession? CurrentSession => _currentSession;

    public async Task<UserSession> AuthenticateAsync(string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username is required.", nameof(username));

        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password is required.", nameof(password));

        _logger.LogInformation("Authentication attempt for user {Username}", username);

        var user = await _userRepository.GetByUsernameAsync(username, ct);
        if (user == null || !user.IsActive)
        {
            _logger.LogWarning("Authentication failed: User {Username} not found or inactive.", username);
            await _auditTrailService.RecordActivityAsync(
                action: "Login Failed",
                category: "AUTHENTICATION",
                description: $"Failed login attempt for username: {username} (Invalid credentials or inactive account)",
                ct: ct);

            throw new UnauthorizedAccessException("Invalid username or password.");
        }

        bool isPasswordValid = PasswordHasher.VerifyPassword(password, user.PasswordHash, user.PasswordSalt);
        if (!isPasswordValid)
        {
            _logger.LogWarning("Authentication failed: Invalid password for user {Username}.", username);
            await _auditTrailService.RecordActivityAsync(
                action: "Login Failed",
                category: "AUTHENTICATION",
                description: $"Failed login attempt for username: {username} (Invalid password)",
                ct: ct);

            throw new UnauthorizedAccessException("Invalid username or password.");
        }

        await _userRepository.UpdateLastLoginAsync(user.Id, DateTime.UtcNow, ct);

        _currentSession = new UserSession
        {
            SessionId = Guid.NewGuid().ToString(),
            User = user,
            LoginTimestamp = DateTime.UtcNow,
            LastActivityTimestamp = DateTime.UtcNow,
            IsActive = true
        };

        _logger.LogInformation("User {Username} ({Role}) authenticated successfully.", user.Username, user.Role);

        await _auditTrailService.RecordActivityAsync(
            action: "Login Succeeded",
            category: "AUTHENTICATION",
            description: $"User {user.Username} logged in with role {user.Role}.",
            ct: ct);

        return _currentSession;
    }

    public async Task<bool> ChangePasswordAsync(string userId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            throw new ArgumentException("New password must be at least 6 characters.", nameof(newPassword));

        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user == null)
            throw new InvalidOperationException("User not found.");

        if (!PasswordHasher.VerifyPassword(currentPassword, user.PasswordHash, user.PasswordSalt))
        {
            _logger.LogWarning("Password change rejected for user {Username}: current password mismatch.", user.Username);
            return false;
        }

        var (newHash, newSalt) = PasswordHasher.HashPassword(newPassword);
        bool updated = await _userRepository.UpdatePasswordAsync(userId, newHash, newSalt, ct);

        if (updated)
        {
            _logger.LogInformation("Password changed successfully for user {Username}.", user.Username);
            await _auditTrailService.RecordActivityAsync(
                action: "Password Changed",
                category: "AUTHENTICATION",
                description: $"Password changed for user {user.Username}.",
                ct: ct);
        }

        return updated;
    }

    public async Task<AppUser> CreateUserAsync(AppUser user, string initialPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(user.Username))
            throw new ArgumentException("Username is required.", nameof(user));

        if (string.IsNullOrWhiteSpace(initialPassword) || initialPassword.Length < 6)
            throw new ArgumentException("Initial password must be at least 6 characters.", nameof(initialPassword));

        var existing = await _userRepository.GetByUsernameAsync(user.Username, ct);
        if (existing != null)
            throw new InvalidOperationException($"User with username '{user.Username}' already exists.");

        var (hash, salt) = PasswordHasher.HashPassword(initialPassword);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        user.CreatedAt = DateTime.UtcNow;

        await _userRepository.CreateAsync(user, ct);
        _logger.LogInformation("Created new user {Username} with role {Role}.", user.Username, user.Role);

        await _auditTrailService.RecordActivityAsync(
            action: "User Created",
            category: "USER_MANAGEMENT",
            description: $"Created user {user.Username} with role {user.Role}.",
            ct: ct);

        return user;
    }

    public async Task<bool> UpdateUserAsync(AppUser user, CancellationToken ct = default)
    {
        bool success = await _userRepository.UpdateAsync(user, ct);
        if (success)
        {
            _logger.LogInformation("Updated user profile for {Username}.", user.Username);
            await _auditTrailService.RecordActivityAsync(
                action: "User Updated",
                category: "USER_MANAGEMENT",
                description: $"Updated profile for user {user.Username} (Role: {user.Role}, Active: {user.IsActive}).",
                ct: ct);
        }
        return success;
    }

    public Task<AppUser?> GetUserByIdAsync(string userId, CancellationToken ct = default)
    {
        return _userRepository.GetByIdAsync(userId, ct);
    }

    public Task<IReadOnlyList<AppUser>> GetUsersAsync(CancellationToken ct = default)
    {
        return _userRepository.GetAllAsync(ct);
    }

    public void Logout()
    {
        if (_currentSession != null)
        {
            _logger.LogInformation("User {Username} logged out.", _currentSession.User.Username);
            _auditTrailService.RecordActivityAsync(
                action: "Logout",
                category: "AUTHENTICATION",
                description: $"User {_currentSession.User.Username} logged out.").ConfigureAwait(false);

            _currentSession.IsActive = false;
            _currentSession = null;
        }
    }
}
