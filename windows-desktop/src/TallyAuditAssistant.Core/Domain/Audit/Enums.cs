namespace TallyAuditAssistant.Core.Domain.Audit;

public enum SeverityLevel
{
    Informational = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum RuleCategory
{
    GeneralAccounting = 0,
    GST = 1,
    TDS = 2,
    Banking = 3,
    VoucherSequencing = 4,
    AnomalyDetection = 5,
    DuplicateDetection = 6
}

public enum ReviewStatus
{
    Pending = 0,
    Reviewed = 1,
    FlaggedAsFalsePositive = 2,
    Resolved = 3,
    RequiresClientClarification = 4
}

public enum ConnectionStatus
{
    Disconnected = 0,
    Scanning = 1,
    Connected = 2,
    PortOpenNoResponse = 3,
    ProcessRunningPortClosed = 4,
    Error = 5
}
