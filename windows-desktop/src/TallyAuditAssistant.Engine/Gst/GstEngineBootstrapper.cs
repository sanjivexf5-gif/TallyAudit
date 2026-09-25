using Microsoft.Extensions.DependencyInjection;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data.Repositories;
using TallyAuditAssistant.Engine.Gst.Rules;

namespace TallyAuditAssistant.Engine.Gst;

public static class GstEngineBootstrapper
{
    public static IServiceCollection AddGstAuditEngine(this IServiceCollection services)
    {
        // Repository and Exception Service
        services.AddSingleton<GstRepository>();
        services.AddSingleton<IGstRepository>(sp => sp.GetRequiredService<GstRepository>());
        services.AddSingleton<IGstExceptionService>(sp => sp.GetRequiredService<GstRepository>());

        // Register All 18 GST Rules
        services.AddSingleton<IGstRule, GstinFormatCheckRule>();                    // 1. GSTIN format
        services.AddSingleton<IGstRule, MissingGstinOnB2BRule>();                   // 2. Missing GSTIN
        services.AddSingleton<IGstRule, GstRegistrationTypeInconsistencyRule>();    // 3. Reg type inconsistencies
        services.AddSingleton<IGstRule, CgstSgstIgstConsistencyRule>();             // 4. CGST/SGST/IGST consistency
        services.AddSingleton<IGstRule, InterstateIntrastateTaxConsistencyRule>();  // 5. Interstate/intrastate consistency
        services.AddSingleton<IGstRule, TaxableValueVsTaxAmountRule>();             // 6. Taxable value vs tax amount
        services.AddSingleton<IGstRule, GstRateConsistencyRule>();                  // 7. GST rate consistency
        services.AddSingleton<IGstRule, HsnSacPresenceRule>();                      // 8. HSN/SAC presence
        services.AddSingleton<IGstRule, GstCreditDebitNoteAnomalyRule>();           // 9. Credit/debit note anomalies
        services.AddSingleton<IGstRule, DuplicateGstInvoiceNumberRule>();           // 10. Duplicate invoice numbers
        services.AddSingleton<IGstRule, DuplicateGstTransactionRule>();             // 11. Duplicate GST transactions
        services.AddSingleton<IGstRule, UnusualTaxRatesRule>();                     // 12. Unusual tax rates
        services.AddSingleton<IGstRule, PossibleRcmExceptionsRule>();               // 13. Possible RCM exceptions
        services.AddSingleton<IGstRule, GstLedgerMappingIssuesRule>();              // 14. GST ledger mapping issues
        services.AddSingleton<IGstRule, TaxLedgerPostingAnomaliesRule>();           // 15. Tax ledger posting anomalies
        services.AddSingleton<IGstRule, NegativeTaxableAmountRule>();               // 16. Negative taxable amounts
        services.AddSingleton<IGstRule, RoundOffAnomaliesRule>();                   // 17. Round-off anomalies
        services.AddSingleton<IGstRule, PlaceOfSupplyInconsistencyRule>();          // 18. Place-of-supply inconsistencies

        // Engine coordinator
        services.AddSingleton<IGstAuditEngine, GstAuditEngine>();

        return services;
    }
}
