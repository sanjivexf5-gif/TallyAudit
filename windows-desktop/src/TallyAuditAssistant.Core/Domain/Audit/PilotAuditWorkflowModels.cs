namespace TallyAuditAssistant.Core.Domain.Audit;

public enum DataReadinessStatus
{
    Ready = 0,
    ReadyWithLimitations = 1,
    Incomplete = 2
}

public class DatasetCompletenessItem
{
    public string DatasetName { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
    public int RecordCount { get; set; }
    public DateTime? LastSyncTimestamp { get; set; }
    public string StatusDescription { get; set; } = string.Empty;
}

public class DataCompletenessReport
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CompanyId { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string FinancialPeriodId { get; set; } = string.Empty;
    public string FinancialPeriodLabel { get; set; } = string.Empty;
    public DateTime EvaluatedAt { get; set; } = DateTime.UtcNow;
    public DataReadinessStatus ReadinessStatus { get; set; } = DataReadinessStatus.ReadyWithLimitations;
    public int TotalLedgers { get; set; }
    public int TotalVouchers { get; set; }
    public int TotalGstTransactions { get; set; }
    public int TotalTdsTransactions { get; set; }
    public int TotalBankTransactions { get; set; }
    public List<DatasetCompletenessItem> DatasetDetails { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class AuditLimitationRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CompanyId { get; set; } = string.Empty;
    public string FinancialPeriodId { get; set; } = string.Empty;
    public string UnavailableDataset { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime RecordedDate { get; set; } = DateTime.UtcNow;
    public string RecordedBy { get; set; } = string.Empty;
    public string AffectedAuditAreas { get; set; } = string.Empty;
    public string AuditorMitigationStrategy { get; set; } = string.Empty;
}

public class MaterialitySpecification
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CompanyId { get; set; } = string.Empty;
    public string FinancialPeriodId { get; set; } = string.Empty;
    public decimal BenchmarkFinancialValue { get; set; }
    public string BenchmarkBasis { get; set; } = "Turnover / Revenue from Operations";
    public decimal OverallMaterialityPercentage { get; set; } = 1.0m;
    public decimal OverallMaterialityAmount { get; set; }
    public decimal PerformanceMaterialityPercentage { get; set; } = 75.0m;
    public decimal PerformanceMaterialityAmount { get; set; }
    public decimal ClearlyTrivialPercentage { get; set; } = 5.0m;
    public decimal ClearlyTrivialThresholdAmount { get; set; }
    public string AuditorJustification { get; set; } = string.Empty;
    public bool IsAuditorOverridden { get; set; }
    public DateTime ApprovedAt { get; set; } = DateTime.UtcNow;
    public string ApprovedBy { get; set; } = string.Empty;
}

public enum SamplingMethod
{
    Random = 0,
    Systematic = 1,
    Targeted = 2,
    MaterialItem = 3,
    MonetaryUnit = 4
}

public class SamplingRunRecord
{
    public string SampleId { get; set; } = Guid.NewGuid().ToString();
    public string CompanyId { get; set; } = string.Empty;
    public string FinancialPeriodId { get; set; } = string.Empty;
    public string AuditArea { get; set; } = string.Empty;
    public SamplingMethod Method { get; set; } = SamplingMethod.Targeted;
    public int PopulationCount { get; set; }
    public decimal PopulationValue { get; set; }
    public int SampleSize { get; set; }
    public decimal SampleTotalValue { get; set; }
    public int RandomSeed { get; set; }
    public decimal HighValueThreshold { get; set; }
    public string SelectedVoucherIdsJson { get; set; } = "[]";
    public int ExceptionsFoundCount { get; set; }
    public string AuditorConclusion { get; set; } = string.Empty;
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
    public string ExecutedBy { get; set; } = string.Empty;
}
