using TallyAuditAssistant.Core.Domain.Tally;

namespace TallyAuditAssistant.Core.Interfaces;

public interface ITallyResponseParser
{
    IReadOnlyList<string> ParseCompanyList(string responseContent, TallyRequestFormat format = TallyRequestFormat.Xml);
    TallyCompanyProfile? ParseCompanyProfile(string responseContent, TallyRequestFormat format = TallyRequestFormat.Xml);
    IReadOnlyList<TallyLedgerDto> ParseLedgers(string responseContent, TallyRequestFormat format = TallyRequestFormat.Xml);
    IReadOnlyList<string> ParseGroups(string responseContent, TallyRequestFormat format = TallyRequestFormat.Xml);
    IReadOnlyList<TallyVoucherDto> ParseVouchers(string responseContent, TallyRequestFormat format = TallyRequestFormat.Xml);
    
    // Checks for standard Tally error structures (e.g., <LINEERROR>, <PARSERROR>, <STATUS>0</STATUS>)
    (bool HasError, string? ErrorMessage) CheckForTallyErrors(string responseContent, TallyRequestFormat format = TallyRequestFormat.Xml);
}
