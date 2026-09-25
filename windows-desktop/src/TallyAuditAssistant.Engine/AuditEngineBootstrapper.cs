using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;
using TallyAuditAssistant.Data.Repositories;
using TallyAuditAssistant.Engine.Rules;

namespace TallyAuditAssistant.Engine;

public static class AuditEngineBootstrapper
{
    public static IServiceCollection AddAuditEngine(this IServiceCollection services)
    {
        // Repositories
        services.AddSingleton<IAuditRuleRepository, AuditRuleRepository>();
        services.AddSingleton<IAuditResultRepository, AuditResultRepository>();
        services.AddSingleton<IExceptionRepository, ExceptionRepository>();

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

        // Audit Engine Coordinator
        services.AddSingleton<IAuditEngine, AuditEngine>();

        return services;
    }
}
