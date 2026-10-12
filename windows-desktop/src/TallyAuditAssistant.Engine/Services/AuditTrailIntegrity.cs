using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TallyAuditAssistant.Core.Domain.Audit;

namespace TallyAuditAssistant.Engine.Services;

/// <summary>
/// Computes a stable SHA-256 digest across every persisted audit-trail field.
/// This detects partial or accidental record changes; it is not a digital signature
/// and does not prevent a privileged user from recalculating a changed record's digest.
/// </summary>
public static class AuditTrailIntegrity
{
    public const string HashPrefix = "SHA256V2-";

    public static string ComputeHash(AuditTrailEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var payload = JsonSerializer.Serialize(new string?[]
        {
            entry.Id,
            DateTime.SpecifyKind(entry.TimestampUtc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture),
            entry.UserName,
            entry.ActionType,
            entry.Module,
            entry.CompanyName,
            entry.FinancialYear,
            entry.EntityType,
            entry.EntityId,
            entry.PreviousState,
            entry.NewState,
            entry.Description,
            entry.Details,
            entry.ApplicationVersion,
            entry.MachineName
        });

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return HashPrefix + Convert.ToHexString(digest);
    }

    public static bool VerifyHash(AuditTrailEntry entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.IntegrityHash) ||
            !entry.IntegrityHash.StartsWith(HashPrefix, StringComparison.Ordinal))
            return false;

        var expected = Encoding.ASCII.GetBytes(ComputeHash(entry));
        var supplied = Encoding.ASCII.GetBytes(entry.IntegrityHash);
        return supplied.Length == expected.Length &&
               CryptographicOperations.FixedTimeEquals(supplied, expected);
    }
}
