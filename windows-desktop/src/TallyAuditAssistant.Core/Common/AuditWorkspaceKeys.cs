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

    public static string ChecklistForPeriod(string companyId, string financialPeriodId)
    {
        if (string.IsNullOrWhiteSpace(financialPeriodId))
            throw new ArgumentException("A financial period ID is required.", nameof(financialPeriodId));

        return $"{LegacyChecklistForCompany(companyId)}-PERIOD-{financialPeriodId.Trim()}";
    }
}
