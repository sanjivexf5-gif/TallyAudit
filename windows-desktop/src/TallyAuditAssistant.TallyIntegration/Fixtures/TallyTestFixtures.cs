namespace TallyAuditAssistant.TallyIntegration.Fixtures;

public static class TallyTestFixtures
{
    public const string CompanyListXml = @"<ENVELOPE>
  <HEADER>
    <VERSION>1</VERSION>
    <STATUS>1</STATUS>
  </HEADER>
  <BODY>
    <DATA>
      <COLLECTION>
        <COMPANY>
          <NAME>Apex Industrial Solutions Pvt Ltd (FY 2025-26)</NAME>
        </COMPANY>
        <COMPANY>
          <NAME>Delta Retail Ventures LLP (FY 2025-26)</NAME>
        </COMPANY>
      </COLLECTION>
    </DATA>
  </BODY>
</ENVELOPE>";

    public const string CompanyListJson = @"{
  ""HEADER"": {
    ""VERSION"": ""1"",
    ""STATUS"": ""1""
  },
  ""BODY"": {
    ""DATA"": {
      ""COLLECTION"": [
        { ""NAME"": ""Apex Industrial Solutions Pvt Ltd (FY 2025-26)"" },
        { ""NAME"": ""Delta Retail Ventures LLP (FY 2025-26)"" }
      ]
    }
  }
}";

    public const string CompanyProfileXml = @"<ENVELOPE>
  <HEADER><VERSION>1</VERSION><STATUS>1</STATUS></HEADER>
  <BODY>
    <DATA>
      <COLLECTION>
        <COMPANY>
          <NAME>Apex Industrial Solutions Pvt Ltd</NAME>
          <FORMALNAME>Apex Industrial Solutions Private Limited</FORMALNAME>
          <GSTIN>27AAACA9999P1Z1</GSTIN>
          <PAN>AAACA9999P</PAN>
          <STATENAME>Maharashtra</STATENAME>
          <STATECODE>27</STATECODE>
          <BOOKSBEGINNINGFROM>20250401</BOOKSBEGINNINGFROM>
          <STARTINGFROM>20250401</STARTINGFROM>
          <BASICCURRENCYSYMBOL>₹</BASICCURRENCYSYMBOL>
          <ALTERID>10042</ALTERID>
        </COMPANY>
      </COLLECTION>
    </DATA>
  </BODY>
</ENVELOPE>";

    public const string LedgerCollectionXml = @"<ENVELOPE>
  <HEADER><VERSION>1</VERSION><STATUS>1</STATUS></HEADER>
  <BODY>
    <DATA>
      <COLLECTION>
        <LEDGER>
          <NAME>Mehta Fabrication Works</NAME>
          <PARENT>Sundry Creditors</PARENT>
          <GSTIN>27AABCM8888Q1Z2</GSTIN>
          <INCOMETAXNUMBER>AABCM8888Q</INCOMETAXNUMBER>
          <STATENAME>Maharashtra</STATENAME>
          <OPENINGBALANCE>-150000.00</OPENINGBALANCE>
          <CLOSINGBALANCE>-85000.00</CLOSINGBALANCE>
          <TAXTYPE>GST</TAXTYPE>
          <ALTERID>201</ALTERID>
        </LEDGER>
        <LEDGER>
          <NAME>QuickLogistics Express</NAME>
          <PARENT>Sundry Creditors</PARENT>
          <GSTIN></GSTIN>
          <INCOMETAXNUMBER>AAZCQ7777K</INCOMETAXNUMBER>
          <STATENAME>Gujarat</STATENAME>
          <OPENINGBALANCE>0.00</OPENINGBALANCE>
          <CLOSINGBALANCE>-84500.00</CLOSINGBALANCE>
          <TAXTYPE>TDS</TAXTYPE>
          <ALTERID>202</ALTERID>
        </LEDGER>
        <LEDGER>
          <NAME>Input CGST 9%</NAME>
          <PARENT>Duties &amp; Taxes</PARENT>
          <TAXTYPE>GST</TAXTYPE>
          <GSTRATE>9.00</GSTRATE>
          <OPENINGBALANCE>12500.00</OPENINGBALANCE>
          <CLOSINGBALANCE>68400.00</CLOSINGBALANCE>
          <ALTERID>203</ALTERID>
        </LEDGER>
        <LEDGER>
          <NAME>Main Cash Account</NAME>
          <PARENT>Cash-in-Hand</PARENT>
          <OPENINGBALANCE>45000.00</OPENINGBALANCE>
          <CLOSINGBALANCE>-32450.00</CLOSINGBALANCE>
          <ALTERID>204</ALTERID>
        </LEDGER>
      </COLLECTION>
    </DATA>
  </BODY>
</ENVELOPE>";

    public const string VoucherCollectionXml = @"<ENVELOPE>
  <HEADER><VERSION>1</VERSION><STATUS>1</STATUS></HEADER>
  <BODY>
    <DATA>
      <COLLECTION>
        <VOUCHER>
          <GUID>9B8E3D21-1A4C-4D2A-8B1E-F32948172901</GUID>
          <VOUCHERNUMBER>PUR/25-26/089</VOUCHERNUMBER>
          <REFERENCE>INV-8910</REFERENCE>
          <VOUCHERTYPENAME>Purchase</VOUCHERTYPENAME>
          <DATE>20250812</DATE>
          <EFFECTIVEDATE>20250812</EFFECTIVEDATE>
          <NARRATION>Purchase of fabrication components</NARRATION>
          <AMOUNT>142800.00</AMOUNT>
          <PARTYLEDGERNAME>Mehta Fabrication Works</PARTYLEDGERNAME>
          <ISCANCELLED>No</ISCANCELLED>
          <ISOPTIONAL>No</ISOPTIONAL>
          <ALTERID>4510</ALTERID>
          <ALLLEDGERENTRIES.LIST>
            <LEDGERNAME>Mehta Fabrication Works</LEDGERNAME>
            <AMOUNT>-142800.00</AMOUNT>
            <BILLTYPE>New Ref</BILLTYPE>
            <BILLNAME>INV-8910</BILLNAME>
          </ALLLEDGERENTRIES.LIST>
          <ALLLEDGERENTRIES.LIST>
            <LEDGERNAME>Raw Material Purchases</LEDGERNAME>
            <AMOUNT>121016.95</AMOUNT>
          </ALLLEDGERENTRIES.LIST>
          <ALLLEDGERENTRIES.LIST>
            <LEDGERNAME>Input IGST 18%</LEDGERNAME>
            <AMOUNT>21783.05</AMOUNT>
          </ALLLEDGERENTRIES.LIST>
        </VOUCHER>
      </COLLECTION>
    </DATA>
  </BODY>
</ENVELOPE>";

    public const string TallyLineErrorXml = @"<ENVELOPE>
  <HEADER><VERSION>1</VERSION><STATUS>0</STATUS></HEADER>
  <BODY>
    <DESC>
      <LINEERROR>Line 14: Action 'Export' failed on report 'NonExistentReport'. Unknown symbol.</LINEERROR>
    </DESC>
  </BODY>
</ENVELOPE>";

    public const string TallyParseErrorXml = @"<ENVELOPE>
  <HEADER><VERSION>1</VERSION><STATUS>0</STATUS></HEADER>
  <BODY>
    <DESC>
      <PARSERROR>Invalid XML format: unclosed tag &lt;STATICVARIABLES&gt;</PARSERROR>
    </DESC>
  </BODY>
</ENVELOPE>";

    public const string MalformedXml = @"<ENVELOPE><HEADER><VERSION>1</VERSION><BODY><DATA><TRUNCATED";
}
