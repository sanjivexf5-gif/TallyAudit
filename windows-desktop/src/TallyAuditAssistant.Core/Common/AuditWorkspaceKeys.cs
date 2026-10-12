using System;

namespace TallyAuditAssistant.Core.Common;

/// <summary>
/// Builds stable local IDs for audit checklist workspaces.
/// Checklist completion and open items must be isolated by company and financial period.
/// </summary>
public static class AuditWorkspaceKeys
{
    public static string LegacyChecklistForCompany(string companyId)
    {
        if (string.IsNullOrWhiteSpace(companyId))
            throw new ArgumentException("A company ID is required.", nameof(companyId));

        return $"CHECKLIST-{companyId.Trim()}";
    }

}
