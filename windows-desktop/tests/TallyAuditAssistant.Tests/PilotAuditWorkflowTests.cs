using TallyAuditAssistant.Core.Domain.Audit;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class PilotAuditWorkflowTests
{
    [Fact]
    public void MaterialitySpecification_CalculatesDecimalThresholdsCorrectly()
    {
        var spec = new MaterialitySpecification
        {
            CompanyId = "COMP-001",
            FinancialPeriodId = "FY-2025-26",
            BenchmarkFinancialValue = 150000000.00m, // 15 Crore
            BenchmarkBasis = "Turnover / Revenue from Operations",
            OverallMaterialityPercentage = 1.0m,
            PerformanceMaterialityPercentage = 75.0m,
            ClearlyTrivialPercentage = 5.0m
        };

        // Overall materiality = 1% of 15 Crore = 15 Lakhs
        spec.OverallMaterialityAmount = spec.BenchmarkFinancialValue * (spec.OverallMaterialityPercentage / 100.0m);
        // Performance materiality = 75% of Overall = 11.25 Lakhs
        spec.PerformanceMaterialityAmount = spec.OverallMaterialityAmount * (spec.PerformanceMaterialityPercentage / 100.0m);
        // Clearly trivial = 5% of Overall = 75,000
        spec.ClearlyTrivialThresholdAmount = spec.OverallMaterialityAmount * (spec.ClearlyTrivialPercentage / 100.0m);

        Assert.Equal(1500000.00m, spec.OverallMaterialityAmount);
        Assert.Equal(1125000.00m, spec.PerformanceMaterialityAmount);
        Assert.Equal(75000.00m, spec.ClearlyTrivialThresholdAmount);
    }

    [Fact]
    public void DataCompletenessReport_EvaluatesLimitationsCorrectly()
    {
        var report = new DataCompletenessReport
        {
            CompanyId = "COMP-001",
            CompanyName = "Apex Industrial Solutions Pvt Ltd",
            FinancialPeriodId = "FY-2025-26",
            TotalLedgers = 412,
            TotalVouchers = 14280,
            TotalGstTransactions = 3840,
            TotalTdsTransactions = 920,
            TotalBankTransactions = 0 // Bank not synced
        };

        report.DatasetDetails.Add(new DatasetCompletenessItem
        {
            DatasetName = "Chart of Accounts / Ledgers",
            IsAvailable = true,
            RecordCount = report.TotalLedgers,
            StatusDescription = "Complete master list synchronized"
        });

        report.DatasetDetails.Add(new DatasetCompletenessItem
        {
            DatasetName = "General Ledger Vouchers",
            IsAvailable = true,
            RecordCount = report.TotalVouchers,
            StatusDescription = "14,280 transactions available for audit"
        });

        report.DatasetDetails.Add(new DatasetCompletenessItem
        {
            DatasetName = "Bank Statement Direct Feed",
            IsAvailable = false,
            RecordCount = 0,
            StatusDescription = "Bank statement XML feed not connected"
        });

        if (report.TotalBankTransactions == 0)
        {
            report.ReadinessStatus = DataReadinessStatus.ReadyWithLimitations;
            report.Warnings.Add("Bank statement feed unavailable. Relying on Tally Book Bank Ledger.");
        }

        Assert.Equal(DataReadinessStatus.ReadyWithLimitations, report.ReadinessStatus);
        Assert.Single(report.Warnings);
    }
}
