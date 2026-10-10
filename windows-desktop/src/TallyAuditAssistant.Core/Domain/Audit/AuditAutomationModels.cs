namespace TallyAuditAssistant.Core.Domain.Audit;

public enum AuditAutomationStage
{
    Idle = 0,
    Connecting = 1,
    SelectingCompany = 2,
    Synchronizing = 3,
    RunningAudit = 4,
    PreparingResults = 5,
    Completed = 6,
    Failed = 7,
    Cancelled = 8,
    Incomplete = 9
}

public record AuditAutomationProgress(
    AuditAutomationStage Stage,
    string StageTitle,
    string Message,
    double Percentage,
    int Findings = 0);

public record AuditAutomationResult(
    bool IsSuccess,
    string CompanyName,
    int RecordsSynchronized,
    int FindingsGenerated,
    TimeSpan Duration,
    string? ErrorMessage = null,
    bool IsIncomplete = false);
