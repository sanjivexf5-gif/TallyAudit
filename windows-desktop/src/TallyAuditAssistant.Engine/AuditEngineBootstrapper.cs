using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;
using TallyAuditAssistant.Data.Repositories;
using TallyAuditAssistant.Engine.Rules;
using TallyAuditAssistant.Engine.Reconciliation;

namespace TallyAuditAssistant.Engine;

public static class AuditEngineBootstrapper
{
    public static IServiceCollection AddAuditEngine(this IServiceCollection services)
    {
        // Repositories
        services.AddSingleton<IAuditRuleRepository, AuditRuleRepository>();
        services.AddSingleton<IAuditResultRepository, AuditResultRepository>();
        services.AddSingleton<IExceptionRepository, ExceptionRepository>();

        // Services
        services.AddHttpClient();
        services.AddSingleton<ITallyDrillDownService, Services.TallyDrillDownService>();
        services.AddSingleton<IAuditAiProvider, GeminiAuditProvider>();
        services.AddSingleton<IAuditAssistantService, AuditAssistantService>();

        // Reconciliation Engine & Rules
        services.AddSingleton<IReconciliationRule, TrialBalanceConsistencyRule>();
        services.AddSingleton<IReconciliationRule, LedgerVoucherReconciliationRule>();
        services.AddSingleton<IReconciliationRule, GstRateReconciliationRule>();
        services.AddSingleton<IReconciliationRule, GstInputOutputNetReconciliationRule>();
        services.AddSingleton<IReconciliationRule, TdsExpenseVerificationRule>();
        services.AddSingleton<IReconciliationRule, PartyMasterReconciliationRule>();
        services.AddSingleton<IReconciliationRule, SalesGstReconciliationRule>();
        services.AddSingleton<IReconciliationRule, PurchaseGstReconciliationRule>();
        services.AddSingleton<IReconciliationRule, ExpenseTdsReconciliationRule>();
        services.AddSingleton<IReconciliationRule, BankCashReconciliationRule>();
        services.AddSingleton<IReconciliationRule, ContraVerificationRule>();
        services.AddSingleton<IReconciliationRule, PeriodReconciliationRule>();

        services.AddSingleton<IReconciliationEngine, ReconciliationEngine>();

        // Register All 19 Rules
        services.AddSingleton<IAuditRule, DuplicateVoucherRule>();
        services.AddSingleton<IAuditRule, DuplicateInvoiceNumberRule>();
        services.AddSingleton<IAuditRule, MissingNarrationRule>();
        services.AddSingleton<IAuditRule, MissingPartyInfoRule>();
        services.AddSingleton<IAuditRule, NegativeLedgerBalanceRule>();
        services.AddSingleton<IAuditRule, SuspenseLedgerActivityRule>();
        services.AddSingleton<IAuditRule, UnusualJournalEntryRule>();
        services.AddSingleton<IAuditRule, LargeManualJournalRule>();
        services.AddSingleton<IAuditRule, BackdatedTransactionRule>();
        services.AddSingleton<IAuditRule, YearEndAdjustmentRule>();
        services.AddSingleton<IAuditRule, VoucherNumberingGapRule>();
        services.AddSingleton<IAuditRule, UnusualTransactionAmountRule>();
        services.AddSingleton<IAuditRule, RoundNumberPatternRule>();
        services.AddSingleton<IAuditRule, UnusualLedgerCombinationRule>();
        services.AddSingleton<IAuditRule, ReversalAnomalyRule>();
        services.AddSingleton<IAuditRule, CreditDebitNoteAnomalyRule>();
        services.AddSingleton<IAuditRule, MissingGstInformationRule>();
        services.AddSingleton<IAuditRule, MissingPanWhereApplicableRule>();
        services.AddSingleton<IAuditRule, MissingHsnSacRule>();

        // Advanced Audit Rules
        services.AddSingleton<IAuditRule, GstTaxCalculationConsistencyRule>();
        services.AddSingleton<IAuditRule, InputTaxCreditReviewRule>();
        services.AddSingleton<IAuditRule, OutputGstReviewRule>();
        services.AddSingleton<IAuditRule, TdsApplicabilityThresholdRule>();
        services.AddSingleton<IAuditRule, LargeTransactionRule>();
        services.AddSingleton<IAuditRule, PeriodEndTransactionReviewRule>();
        services.AddSingleton<IAuditRule, MasterDataQualityCheckRule>();
        services.AddSingleton<IAuditRule, CrossDatasetConsistencyCheckRule>();

        // Audit Engine Coordinator
        services.AddSingleton<IAuditEngine, AuditEngine>();

        return services;
    }
}
