namespace TallyAuditAssistant.Core.Interfaces;

/// <summary>
/// Architectural policy enforcing strictly read-only integration with TallyPrime.
/// TallyAudit Assistant reads, queries, and synchronizes data from TallyPrime,
/// but never writes, creates, edits, alters, or deletes accounting records in TallyPrime.
/// </summary>
public interface ITallyReadOnlyPolicy
{
    bool IsReadOnly { get; }
    bool AllowsWriting { get; }
    bool CanWriteAccountingData { get; }
    bool CanSynchronize { get; }
    bool CanQuery { get; }
    bool CanRead { get; }
    string PolicyDescription { get; }
    void AssertCanWrite();
}
