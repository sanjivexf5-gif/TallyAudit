using System.Text;
using TallyAuditAssistant.Core.Domain.Tally;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.TallyIntegration;

public class TallyRequestBuilder : ITallyRequestBuilder
{
    public string BuildPingRequest(TallyRequestFormat format = TallyRequestFormat.Xml)
    {
        var formatSysName = format == TallyRequestFormat.Json ? "$$SysName:JSON" : "$$SysName:XML";
        return $@"<ENVELOPE>
  <HEADER>
    <VERSION>1</VERSION>
    <TALLYREQUEST>Export</TALLYREQUEST>
    <TYPE>Data</TYPE>
    <ID>System Information</ID>
  </HEADER>
  <BODY>
    <DESC>
      <STATICVARIABLES>
        <SVEXPORTFORMAT>{formatSysName}</SVEXPORTFORMAT>
      </STATICVARIABLES>
    </DESC>
  </BODY>
</ENVELOPE>";
    }

    public string BuildCompanyListRequest(TallyRequestFormat format = TallyRequestFormat.Xml)
    {
        var formatSysName = format == TallyRequestFormat.Json ? "$$SysName:JSON" : "$$SysName:XML";
        return $@"<ENVELOPE>
  <HEADER>
    <VERSION>1</VERSION>
    <TALLYREQUEST>Export</TALLYREQUEST>
    <TYPE>Data</TYPE>
    <ID>List of Companies</ID>
  </HEADER>
  <BODY>
    <DESC>
      <STATICVARIABLES>
        <SVEXPORTFORMAT>{formatSysName}</SVEXPORTFORMAT>
      </STATICVARIABLES>
    </DESC>
  </BODY>
</ENVELOPE>";
    }

    public string BuildCompanyProfileRequest(string companyName, TallyRequestFormat format = TallyRequestFormat.Xml)
    {
        var formatSysName = format == TallyRequestFormat.Json ? "$$SysName:JSON" : "$$SysName:XML";
        return $@"<ENVELOPE>
  <HEADER>
    <VERSION>1</VERSION>
    <TALLYREQUEST>Export</TALLYREQUEST>
    <TYPE>Collection</TYPE>
    <ID>CompanyProfileCollection</ID>
  </HEADER>
  <BODY>
    <DESC>
      <STATICVARIABLES>
        <SVEXPORTFORMAT>{formatSysName}</SVEXPORTFORMAT>
        <SVCURRENTCOMPANY>{EscapeXml(companyName)}</SVCURRENTCOMPANY>
      </STATICVARIABLES>
      <TDL>
        <TDLMESSAGE>
          <COLLECTION NAME=""CompanyProfileCollection"">
            <TYPE>Company</TYPE>
            <FETCH>Name, FormalName, GSTIN, PAN, StateName, StateCode, BooksBeginningFrom, StartingFrom, BasicCurrencySymbol, AlterId</FETCH>
            <FILTER>SelectedCompanyOnly</FILTER>
          </COLLECTION>
          <SYSTEM TYPE=""Formulae"" NAME=""SelectedCompanyOnly"">$Name = ""{EscapeXml(companyName)}""</SYSTEM>
        </TDLMESSAGE>
      </TDL>
    </DESC>
  </BODY>
</ENVELOPE>";
    }

    public string BuildLedgerCollectionRequest(string companyName, long? fromAlterId = null, TallyRequestFormat format = TallyRequestFormat.Xml)
    {
        var formatSysName = format == TallyRequestFormat.Json ? "$$SysName:JSON" : "$$SysName:XML";
        var filterXml = fromAlterId.HasValue && fromAlterId.Value > 0
            ? $@"<FILTER>DeltaAlterIdFilter</FILTER>
                </COLLECTION>
                <SYSTEM TYPE=""Formulae"" NAME=""DeltaAlterIdFilter"">$AlterId > {fromAlterId.Value}</SYSTEM>"
            : "</COLLECTION>";

        return $@"<ENVELOPE>
  <HEADER>
    <VERSION>1</VERSION>
    <TALLYREQUEST>Export</TALLYREQUEST>
    <TYPE>Collection</TYPE>
    <ID>AuditLedgerCollection</ID>
  </HEADER>
  <BODY>
    <DESC>
      <STATICVARIABLES>
        <SVEXPORTFORMAT>{formatSysName}</SVEXPORTFORMAT>
        <SVCURRENTCOMPANY>{EscapeXml(companyName)}</SVCURRENTCOMPANY>
      </STATICVARIABLES>
      <TDL>
        <TDLMESSAGE>
          <COLLECTION NAME=""AuditLedgerCollection"">
            <TYPE>Ledger</TYPE>
            <FETCH>Name, Parent, GSTIN, IncomeTaxNumber, StateName, OpeningBalance, ClosingBalance, TaxType, HSNCode, GSTRate, AlterId</FETCH>
            {filterXml}
        </TDLMESSAGE>
      </TDL>
    </DESC>
  </BODY>
</ENVELOPE>";
    }

    public string BuildGroupCollectionRequest(string companyName, TallyRequestFormat format = TallyRequestFormat.Xml)
    {
        var formatSysName = format == TallyRequestFormat.Json ? "$$SysName:JSON" : "$$SysName:XML";
        return $@"<ENVELOPE>
  <HEADER>
    <VERSION>1</VERSION>
    <TALLYREQUEST>Export</TALLYREQUEST>
    <TYPE>Collection</TYPE>
    <ID>AuditGroupCollection</ID>
  </HEADER>
  <BODY>
    <DESC>
      <STATICVARIABLES>
        <SVEXPORTFORMAT>{formatSysName}</SVEXPORTFORMAT>
        <SVCURRENTCOMPANY>{EscapeXml(companyName)}</SVCURRENTCOMPANY>
      </STATICVARIABLES>
      <TDL>
        <TDLMESSAGE>
          <COLLECTION NAME=""AuditGroupCollection"">
            <TYPE>Group</TYPE>
            <FETCH>Name, Parent, BasicGroupIsPrimary, AlterId</FETCH>
          </COLLECTION>
        </TDLMESSAGE>
      </TDL>
    </DESC>
  </BODY>
</ENVELOPE>";
    }

    public string BuildVoucherCollectionRequest(string companyName, DateTime fromDate, DateTime toDate, long? fromAlterId = null, TallyRequestFormat format = TallyRequestFormat.Xml)
    {
        var formatSysName = format == TallyRequestFormat.Json ? "$$SysName:JSON" : "$$SysName:XML";
        var dateFromStr = fromDate.ToString("yyyyMMdd");
        var dateToStr = toDate.ToString("yyyyMMdd");

        var filterXml = fromAlterId.HasValue && fromAlterId.Value > 0
            ? $@"<FILTER>DeltaAlterIdFilter</FILTER>
                </COLLECTION>
                <SYSTEM TYPE=""Formulae"" NAME=""DeltaAlterIdFilter"">$AlterId > {fromAlterId.Value}</SYSTEM>"
            : "</COLLECTION>";

        return $@"<ENVELOPE>
  <HEADER>
    <VERSION>1</VERSION>
    <TALLYREQUEST>Export</TALLYREQUEST>
    <TYPE>Collection</TYPE>
    <ID>AuditVoucherCollection</ID>
  </HEADER>
  <BODY>
    <DESC>
      <STATICVARIABLES>
        <SVEXPORTFORMAT>{formatSysName}</SVEXPORTFORMAT>
        <SVCURRENTCOMPANY>{EscapeXml(companyName)}</SVCURRENTCOMPANY>
        <SVFROMDATE>{dateFromStr}</SVFROMDATE>
        <SVTODATE>{dateToStr}</SVTODATE>
      </STATICVARIABLES>
      <TDL>
        <TDLMESSAGE>
          <COLLECTION NAME=""AuditVoucherCollection"">
            <TYPE>Voucher</TYPE>
            <FETCH>GUID, VoucherNumber, Reference, VoucherTypeName, Date, EffectiveDate, Narration, Amount, PartyLedgerName, IsCancelled, IsOptional, AlterId, AllLedgerEntries.List.*</FETCH>
            {filterXml}
        </TDLMESSAGE>
      </TDL>
    </DESC>
  </BODY>
</ENVELOPE>";
    }

    private static string EscapeXml(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return input
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}
