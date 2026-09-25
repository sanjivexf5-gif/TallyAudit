using TallyAuditAssistant.Core.Domain.Tally;

namespace TallyAuditAssistant.Core.Interfaces;

public interface ITallyRequestBuilder
{
    string BuildPingRequest(TallyRequestFormat format = TallyRequestFormat.Xml);
    string BuildCompanyListRequest(TallyRequestFormat format = TallyRequestFormat.Xml);
    string BuildCompanyProfileRequest(string companyName, TallyRequestFormat format = TallyRequestFormat.Xml);
    string BuildLedgerCollectionRequest(string companyName, long? fromAlterId = null, TallyRequestFormat format = TallyRequestFormat.Xml);
    string BuildGroupCollectionRequest(string companyName, TallyRequestFormat format = TallyRequestFormat.Xml);
    string BuildVoucherCollectionRequest(string companyName, DateTime fromDate, DateTime toDate, long? fromAlterId = null, TallyRequestFormat format = TallyRequestFormat.Xml);
}
