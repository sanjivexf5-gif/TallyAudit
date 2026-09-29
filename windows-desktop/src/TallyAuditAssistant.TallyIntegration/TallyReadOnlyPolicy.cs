using System;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.TallyIntegration;

/// <summary>
/// Default implementation of ITallyReadOnlyPolicy permanently enforcing read-only operations.
/// </summary>
public class TallyReadOnlyPolicy : ITallyReadOnlyPolicy
{
    public bool IsReadOnly => true;
    public bool AllowsWriting => false;
    public bool CanWriteAccountingData => false;
    public bool CanSynchronize => true;
    public bool CanQuery => true;
    public bool CanRead => true;

    public string PolicyDescription =>
        "TallyAudit Assistant is a read-only audit and audit-intelligence layer. It reads and synchronizes data from TallyPrime but does not modify TallyPrime accounting data.";

    public void AssertCanWrite()
    {
        throw new InvalidOperationException(
            "TallyPrime data is read-only in TallyAudit Assistant. Writing, creating, altering, or deleting accounting data in TallyPrime is permanently prohibited by policy.");
    }
}
