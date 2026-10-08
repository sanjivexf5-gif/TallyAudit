namespace TallyAuditAssistant.Core.Domain.Audit;

public class AuditMaterialityPlan
{
    public string Id { get; set; } = string.Empty;
    public string CompanyId { get; set; } = string.Empty;
    public string FinancialPeriodId { get; set; } = string.Empty;
    public decimal BenchmarkAmount { get; set; }
    public decimal BenchmarkPercentage { get; set; }
    public decimal OverallMateriality { get; set; }
    public decimal PerformanceMateriality { get; set; }
    public decimal TrivialThreshold { get; set; }
    public int PopulationCount { get; set; }
    public decimal PopulationAmount { get; set; }
    public int SuggestedSampleSize { get; set; }
    public string SamplingMethod { get; set; } = "Risk-based";
    public string Rationale { get; set; } = string.Empty;
    public string PreparedBy { get; set; } = string.Empty;
    public string ReviewerName { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft";
    public System.DateTime UpdatedAt { get; set; } = System.DateTime.UtcNow;
}