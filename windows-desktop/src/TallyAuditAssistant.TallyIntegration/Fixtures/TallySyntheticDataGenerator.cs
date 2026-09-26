using System;
using System.Text;

namespace TallyAuditAssistant.TallyIntegration.Fixtures;

public static class TallySyntheticDataGenerator
{
    public static string GenerateLedgersXml()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<ENVELOPE>");
        sb.AppendLine("  <HEADER><VERSION>1</VERSION><STATUS>1</STATUS></HEADER>");
        sb.AppendLine("  <BODY>");
        sb.AppendLine("    <DATA>");
        sb.AppendLine("      <COLLECTION>");

        // 1. Sales & Purchase Accounts
        AddLedgerNode(sb, "Product Sales 18%", "Sales Accounts", null, null, "Maharashtra", "0.00", "0.00", "GST", "8517", "18.00", "501");
        AddLedgerNode(sb, "Raw Material Purchases", "Purchase Accounts", null, null, "Maharashtra", "0.00", "0.00", "GST", "8517", "18.00", "502");

        // 2. Expense Accounts
        AddLedgerNode(sb, "Office Rent Expenses", "Indirect Expenses", null, null, "Maharashtra", "0.00", "0.00", null, null, "0.00", "503");
        AddLedgerNode(sb, "Consultancy Fees", "Indirect Expenses", null, null, "Maharashtra", "0.00", "0.00", null, null, "0.00", "504");
        AddLedgerNode(sb, "Direct Power & Fuel", "Direct Expenses", null, null, "Maharashtra", "0.00", "0.00", null, null, "0.00", "505");
        AddLedgerNode(sb, "Depreciation Expenses", "Indirect Expenses", null, null, "Maharashtra", "0.00", "0.00", null, null, "0.00", "506");

        // 3. Debtors (Parties)
        AddLedgerNode(sb, "Alpha Traders", "Sundry Debtors", "27ALPHD1234E1Z1", "ALPHD1234E", "Maharashtra", "150000.00", "225000.00", "GST", null, "0.00", "601");
        AddLedgerNode(sb, "Beta Distributors", "Sundry Debtors", "27BETAD5678F1Z2", "BETAD5678F", "Maharashtra", "45000.00", "84000.00", "GST", null, "0.00", "602");
        AddLedgerNode(sb, "Gamma Enterprises", "Sundry Debtors", "27GAMMD9012G1Z3", "GAMMD9012G", "Maharashtra", "0.00", "110000.00", "GST", null, "0.00", "603");
        AddLedgerNode(sb, "Local Consumer Retail", "Sundry Debtors", null, null, "Maharashtra", "0.00", "5000.00", null, null, "0.00", "604");
        AddLedgerNode(sb, "Out of State Buyer", "Sundry Debtors", "24OUTST7890H1ZA", "OUTST7890H", "Gujarat", "0.00", "350000.00", "GST", null, "0.00", "605");

        // 4. Creditors (Parties & Anomaly targets)
        AddLedgerNode(sb, "Mehta Fabrication Works", "Sundry Creditors", "27AABCM8888Q1Z2", "AABCM8888Q", "Maharashtra", "-150000.00", "-85000.00", "GST", null, "0.00", "701");
        AddLedgerNode(sb, "QuickLogistics Express", "Sundry Creditors", "24AZCQ7777K1Z4", "AAZCQ7777K", "Gujarat", "0.00", "-84500.00", "GST", null, "0.00", "702");
        AddLedgerNode(sb, "Composition Dealer Supplier", "Sundry Creditors", "27COMPD4321J1Z8", "COMPD4321J", "Maharashtra", "0.00", "-45000.00", "GST", null, "0.00", "703");
        AddLedgerNode(sb, "Invalid GSTIN Trader", "Sundry Creditors", "27INVALID1234FX", "INVALID12", "Maharashtra", "0.00", "-32000.00", "GST", null, "0.00", "704");
        AddLedgerNode(sb, "Unregistered Steel Supplier", "Sundry Creditors", null, null, "Maharashtra", "0.00", "-120000.00", null, null, "0.00", "705");
        AddLedgerNode(sb, "TDS Professional Vendor", "Sundry Creditors", "27VENDP1234K1Z5", "VENDP1234K", "Maharashtra", "0.00", "-150000.00", "TDS", null, "0.00", "706");
        AddLedgerNode(sb, "TDS No PAN Vendor", "Sundry Creditors", "27VENDN5678L1Z6", null, "Maharashtra", "0.00", "-50000.00", "TDS", null, "0.00", "707");

        // 5. Assets & Liabilities & Loans
        AddLedgerNode(sb, "HDFC Bank Account", "Bank Accounts", null, null, "Maharashtra", "500000.00", "650000.00", null, null, "0.00", "801");
        AddLedgerNode(sb, "Main Cash Account", "Cash-in-Hand", null, null, "Maharashtra", "80000.00", "45000.00", null, null, "0.00", "802");
        AddLedgerNode(sb, "Share Capital Account", "Capital Account", null, null, "Maharashtra", "-1000000.00", "-1000000.00", null, null, "0.00", "803");
        AddLedgerNode(sb, "Office Machinery & Equipments", "Fixed Assets", null, null, "Maharashtra", "350000.00", "350000.00", null, null, "0.00", "804");
        AddLedgerNode(sb, "Secured Loan from HDFC", "Secured Loans", null, null, "Maharashtra", "-250000.00", "-250000.00", null, null, "0.00", "805");

        // 6. Duties & Taxes (GST, TDS)
        AddLedgerNode(sb, "Input CGST 9%", "Duties & Taxes", null, null, "Maharashtra", "12500.00", "68400.00", "GST", null, "9.00", "901");
        AddLedgerNode(sb, "Input SGST 9%", "Duties & Taxes", null, null, "Maharashtra", "12500.00", "68400.00", "GST", null, "9.00", "902");
        AddLedgerNode(sb, "Input IGST 18%", "Duties & Taxes", null, null, "Maharashtra", "25000.00", "124000.00", "GST", null, "18.00", "903");
        AddLedgerNode(sb, "Output CGST 9%", "Duties & Taxes", null, null, "Maharashtra", "-14000.00", "-72000.00", "GST", null, "9.00", "904");
        AddLedgerNode(sb, "Output SGST 9%", "Duties & Taxes", null, null, "Maharashtra", "-14000.00", "-72000.00", "GST", null, "9.00", "905");
        AddLedgerNode(sb, "Output IGST 18%", "Duties & Taxes", null, null, "Maharashtra", "-30000.00", "-144000.00", "GST", null, "18.00", "906");
        AddLedgerNode(sb, "TDS Payable Sec 194C", "Duties & Taxes", null, null, "Maharashtra", "0.00", "-5400.00", "TDS", null, "2.00", "907");
        AddLedgerNode(sb, "TDS Payable Sec 194J", "Duties & Taxes", null, null, "Maharashtra", "0.00", "-32000.00", "TDS", null, "10.00", "908");

        // 7. Special Accounts (Suspense, Round-off)
        AddLedgerNode(sb, "Suspense Account", "Suspense Accounts", null, null, "Maharashtra", "0.00", "25000.00", null, null, "0.00", "990");
        AddLedgerNode(sb, "Round-off Account", "Indirect Expenses", null, null, "Maharashtra", "0.00", "25.00", null, null, "0.00", "991");

        sb.AppendLine("      </COLLECTION>");
        sb.AppendLine("    </DATA>");
        sb.AppendLine("  </BODY>");
        sb.AppendLine("</ENVELOPE>");
        return sb.ToString();
    }

    private static void AddLedgerNode(StringBuilder sb, string name, string parent, string? gstin, string? pan, string state, string opBal, string clBal, string? taxType, string? hsnCode, string gstRate, string alterId)
    {
        sb.AppendLine("        <LEDGER>");
        sb.AppendLine($"          <NAME>{name}</NAME>");
        sb.AppendLine($"          <PARENT>{parent}</PARENT>");
        sb.AppendLine($"          <GSTIN>{gstin ?? ""}</GSTIN>");
        sb.AppendLine($"          <INCOMETAXNUMBER>{pan ?? ""}</INCOMETAXNUMBER>");
        sb.AppendLine($"          <STATENAME>{state}</STATENAME>");
        sb.AppendLine($"          <OPENINGBALANCE>{opBal}</OPENINGBALANCE>");
        sb.AppendLine($"          <CLOSINGBALANCE>{clBal}</CLOSINGBALANCE>");
        if (taxType != null) sb.AppendLine($"          <TAXTYPE>{taxType}</TAXTYPE>");
        if (hsnCode != null) sb.AppendLine($"          <HSNCODE>{hsnCode}</HSNCODE>");
        sb.AppendLine($"          <GSTRATE>{gstRate}</GSTRATE>");
        sb.AppendLine($"          <ALTERID>{alterId}</ALTERID>");
        sb.AppendLine("        </LEDGER>");
    }

    public static string GenerateVouchersXml()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<ENVELOPE>");
        sb.AppendLine("  <HEADER><VERSION>1</VERSION><STATUS>1</STATUS></HEADER>");
        sb.AppendLine("  <BODY>");
        sb.AppendLine("    <DATA>");
        sb.AppendLine("      <COLLECTION>");

        var alterId = 5000;

        // Generate 120 normal Sales Invoices
        for (int i = 1; i <= 120; i++)
        {
            var date = new DateTime(2025, 4, 1).AddDays((i * 3) % 360);
            var dateStr = date.ToString("yyyyMMdd");
            var vNo = $"SAL-{i:D3}";
            var party = GetPartyForSales(i);
            var amt = 25000.00m + (i * 1250.50m);
            var taxRate = 0.18m;
            var taxAmt = Math.Round(amt * taxRate / 2.0m, 2);
            var totalAmt = amt + (taxAmt * 2);

            // Special Exception triggers:
            string? narration = $"Sales invoice billing to {party}";
            bool isCancelled = false;
            long currentAlterId = alterId++;

            // 1. Missing Narration Case (SAL-010)
            if (i == 10)
            {
                narration = "";
            }

            // 2. Excessive Round-off Case (SAL-030)
            decimal roundOffAmt = 0.00m;
            if (i == 30)
            {
                roundOffAmt = 85.00m; // Clearly excessive
                totalAmt += roundOffAmt;
            }

            // 3. GST calculation inconsistency (SAL-050)
            decimal outputCgst = taxAmt;
            decimal outputSgst = taxAmt;
            if (i == 50)
            {
                outputCgst = 1200.00m; // Incorrect calculation mismatch
                outputSgst = 1200.00m;
                totalAmt = amt + outputCgst + outputSgst;
                narration = "Sales invoice with manual rate override";
            }

            // 4. Backdated transaction (SAL-060)
            string effectiveDateStr = dateStr;
            if (i == 60)
            {
                effectiveDateStr = date.AddDays(25).ToString("yyyyMMdd"); // Backdated warning
            }

            // 5. Duplicate Sales Voucher Number (SAL-020 & SAL-021)
            // (We will add SAL-020-DUP explicitly outside the loop)

            sb.AppendLine("        <VOUCHER>");
            sb.AppendLine($"          <GUID>{Guid.NewGuid()}</GUID>");
            sb.AppendLine($"          <VOUCHERNUMBER>{vNo}</VOUCHERNUMBER>");
            sb.AppendLine($"          <REFERENCE>REF-{i:D4}</REFERENCE>");
            sb.AppendLine("          <VOUCHERTYPENAME>Sales</VOUCHERTYPENAME>");
            sb.AppendLine($"          <DATE>{dateStr}</DATE>");
            sb.AppendLine($"          <EFFECTIVEDATE>{effectiveDateStr}</EFFECTIVEDATE>");
            sb.AppendLine($"          <NARRATION>{narration}</NARRATION>");
            sb.AppendLine($"          <AMOUNT>{totalAmt}</AMOUNT>");
            sb.AppendLine($"          <PARTYLEDGERNAME>{party}</PARTYLEDGERNAME>");
            sb.AppendLine($"          <ISCANCELLED>{(isCancelled ? "Yes" : "No")}</ISCANCELLED>");
            sb.AppendLine("          <ISOPTIONAL>No</ISOPTIONAL>");
            sb.AppendLine($"          <ALTERID>{currentAlterId}</ALTERID>");

            // Double Entry postings
            // Debited to Party (Receivables)
            sb.AppendLine("          <ALLLEDGERENTRIES.LIST>");
            sb.AppendLine($"            <LEDGERNAME>{party}</LEDGERNAME>");
            sb.AppendLine($"            <AMOUNT>{totalAmt}</AMOUNT>");
            sb.AppendLine("          </ALLLEDGERENTRIES.LIST>");

            // Credited to Sales Account (Positive represents debit in Tally, negative is credit)
            sb.AppendLine("          <ALLLEDGERENTRIES.LIST>");
            sb.AppendLine("            <LEDGERNAME>Product Sales 18%</LEDGERNAME>");
            sb.AppendLine($"            <AMOUNT>-{amt}</AMOUNT>");
            sb.AppendLine("          </ALLLEDGERENTRIES.LIST>");

            // Credited to Taxes
            if (party == "Out of State Buyer")
            {
                sb.AppendLine("          <ALLLEDGERENTRIES.LIST>");
                sb.AppendLine("            <LEDGERNAME>Output IGST 18%</LEDGERNAME>");
                sb.AppendLine($"            <AMOUNT>-{outputCgst + outputSgst}</AMOUNT>");
                sb.AppendLine("          </ALLLEDGERENTRIES.LIST>");
            }
            else
            {
                sb.AppendLine("          <ALLLEDGERENTRIES.LIST>");
                sb.AppendLine("            <LEDGERNAME>Output CGST 9%</LEDGERNAME>");
                sb.AppendLine($"            <AMOUNT>-{outputCgst}</AMOUNT>");
                sb.AppendLine("          </ALLLEDGERENTRIES.LIST>");
                sb.AppendLine("          <ALLLEDGERENTRIES.LIST>");
                sb.AppendLine("            <LEDGERNAME>Output SGST 9%</LEDGERNAME>");
                sb.AppendLine($"            <AMOUNT>-{outputSgst}</AMOUNT>");
                sb.AppendLine("          </ALLLEDGERENTRIES.LIST>");
            }

            if (roundOffAmt != 0.00m)
            {
                sb.AppendLine("          <ALLLEDGERENTRIES.LIST>");
                sb.AppendLine("            <LEDGERNAME>Round-off Account</LEDGERNAME>");
                sb.AppendLine($"            <AMOUNT>-{roundOffAmt}</AMOUNT>");
                sb.AppendLine("          </ALLLEDGERENTRIES.LIST>");
            }

            sb.AppendLine("        </VOUCHER>");
        }

        // Add exact duplicate sales voucher (triggers DuplicateVoucherRule and DuplicateInvoiceNumberRule)
        AddSalesDuplicateVouchers(sb, ref alterId);

        // Generate 80 Purchase Invoices
        for (int i = 1; i <= 80; i++)
        {
            var date = new DateTime(2025, 4, 15).AddDays((i * 4) % 350);
            var dateStr = date.ToString("yyyyMMdd");
            var vNo = $"PUR-{i:D3}";
            var party = GetPartyForPurchases(i);
            var amt = 35000.00m + (i * 2200.00m);
            var taxRate = 0.18m;
            var taxAmt = Math.Round(amt * taxRate / 2.0m, 2);
            var totalAmt = amt + (taxAmt * 2);
            string? narration = $"Purchased raw fabrication stock from {party}";
            long currentAlterId = alterId++;

            // Special Exception triggers:
            // 1. Missing GSTIN on B2B supply (PUR-015)
            if (i == 15)
            {
                party = "Unregistered Steel Supplier"; // Has no GSTIN in master, amt is > 50,000
            }

            // 2. Invalid GSTIN syntax (PUR-025)
            if (i == 25)
            {
                party = "Invalid GSTIN Trader"; // Has "27INVALID1234FX" (14 chars or bad check digit)
            }

            // 3. Composition supplier charging tax (PUR-035)
            if (i == 35)
            {
                party = "Composition Dealer Supplier"; // Composition supplier cannot charge tax
            }

            // 4. Blocked ITC anomaly (PUR-045)
            if (i == 45)
            {
                narration = "Motor vehicle purchased for personal usage of director"; // Under Section 17(5) blocked ITC
            }

            sb.AppendLine("        <VOUCHER>");
            sb.AppendLine($"          <GUID>{Guid.NewGuid()}</GUID>");
            sb.AppendLine($"          <VOUCHERNUMBER>{vNo}</VOUCHERNUMBER>");
            sb.AppendLine($"          <REFERENCE>PINV-{i:D4}</REFERENCE>");
            sb.AppendLine("          <VOUCHERTYPENAME>Purchase</VOUCHERTYPENAME>");
            sb.AppendLine($"          <DATE>{dateStr}</DATE>");
            sb.AppendLine($"          <EFFECTIVEDATE>{dateStr}</EFFECTIVEDATE>");
            sb.AppendLine($"          <NARRATION>{narration}</NARRATION>");
            sb.AppendLine($"          <AMOUNT>{totalAmt}</AMOUNT>");
            sb.AppendLine($"          <PARTYLEDGERNAME>{party}</PARTYLEDGERNAME>");
            sb.AppendLine("          <ISCANCELLED>No</ISCANCELLED>");
            sb.AppendLine("          <ISOPTIONAL>No</ISOPTIONAL>");
            sb.AppendLine($"          <ALTERID>{currentAlterId}</ALTERID>");

            // Double Entry postings
            // Credited to Party (Payables) - negative in Tally represents credit
            sb.AppendLine("          <ALLLEDGERENTRIES.LIST>");
            sb.AppendLine($"            <LEDGERNAME>{party}</LEDGERNAME>");
            sb.AppendLine($"            <AMOUNT>-{totalAmt}</AMOUNT>");
            sb.AppendLine("          </ALLLEDGERENTRIES.LIST>");

            // Debited to Purchase Account (positive in Tally represents debit)
            sb.AppendLine("          <ALLLEDGERENTRIES.LIST>");
            sb.AppendLine("            <LEDGERNAME>Raw Material Purchases</LEDGERNAME>");
            sb.AppendLine($"            <AMOUNT>{amt}</AMOUNT>");
            sb.AppendLine("          </ALLLEDGERENTRIES.LIST>");

            // Debited to Taxes
            if (party == "QuickLogistics Express")
            {
                sb.AppendLine("          <ALLLEDGERENTRIES.LIST>");
                sb.AppendLine("            <LEDGERNAME>Input IGST 18%</LEDGERNAME>");
                sb.AppendLine($"            <AMOUNT>{taxAmt * 2}</AMOUNT>");
                sb.AppendLine("          </ALLLEDGERENTRIES.LIST>");
            }
            else
            {
                sb.AppendLine("          <ALLLEDGERENTRIES.LIST>");
                sb.AppendLine("            <LEDGERNAME>Input CGST 9%</LEDGERNAME>");
                sb.AppendLine($"            <AMOUNT>{taxAmt}</AMOUNT>");
                sb.AppendLine("          </ALLLEDGERENTRIES.LIST>");
                sb.AppendLine("          <ALLLEDGERENTRIES.LIST>");
                sb.AppendLine("            <LEDGERNAME>Input SGST 9%</LEDGERNAME>");
                sb.AppendLine($"            <AMOUNT>{taxAmt}</AMOUNT>");
                sb.AppendLine("          </ALLLEDGERENTRIES.LIST>");
            }

            sb.AppendLine("        </VOUCHER>");
        }

        // Generate 60 Expense / Professional Fee payments (triggers TDS rules)
        for (int i = 1; i <= 60; i++)
        {
            var date = new DateTime(2025, 5, 1).AddDays((i * 5) % 300);
            var dateStr = date.ToString("yyyyMMdd");
            var vNo = $"EXP-{i:D3}";
            var party = "TDS Professional Vendor";
            decimal amt = 15000.00m + (i * 3000.00m);
            decimal tdsRate = 0.10m; // 10% under Sec 194J
            decimal tdsAmt = Math.Round(amt * tdsRate, 2);
            decimal payableAmt = amt - tdsAmt;
            string? narration = $"Consultancy charges paid to {party}";
            long currentAlterId = alterId++;

            // Special TDS Exception triggers:
            // 1. Missing TDS Deduction (EXP-010) - Single payment exceeding threshold with zero TDS
            if (i == 10)
            {
                tdsAmt = 0.00m;
                payableAmt = amt;
                narration = "Management consulting fee paid without TDS";
            }

            // 2. Incorrect TDS rate / calculation anomaly (EXP-020)
            if (i == 20)
            {
                tdsRate = 0.02m; // 2% instead of 10%
                tdsAmt = Math.Round(amt * tdsRate, 2);
                payableAmt = amt - tdsAmt;
                narration = "Consultancy fee processed with incorrect TDS rate";
            }

            // 3. No PAN TDS rate trigger (EXP-030) - Supplier has no PAN, should deduct 20% but deducted only 10%
            if (i == 30)
            {
                party = "TDS No PAN Vendor";
                tdsRate = 0.10m; // Underdeducted! Should be 20%
                tdsAmt = Math.Round(amt * tdsRate, 2);
                payableAmt = amt - tdsAmt;
                narration = "Payment processed to non-PAN holder at base rate";
            }

            // 4. Suspense Ledger Activity (EXP-040)
            if (i == 40)
            {
                party = "Suspense Account";
                amt = 25000.00m;
                tdsAmt = 0.00m;
                payableAmt = 25000.00m;
                narration = "Cash advance for site works recorded in suspense";
            }

            sb.AppendLine("        <VOUCHER>");
            sb.AppendLine($"          <GUID>{Guid.NewGuid()}</GUID>");
            sb.AppendLine($"          <VOUCHERNUMBER>{vNo}</VOUCHERNUMBER>");
            sb.AppendLine($"          <REFERENCE>EXPREF-{i:D4}</REFERENCE>");
            sb.AppendLine("          <VOUCHERTYPENAME>Journal</VOUCHERTYPENAME>");
            sb.AppendLine($"          <DATE>{dateStr}</DATE>");
            sb.AppendLine($"          <EFFECTIVEDATE>{dateStr}</EFFECTIVEDATE>");
            sb.AppendLine($"          <NARRATION>{narration}</NARRATION>");
            sb.AppendLine($"          <AMOUNT>{amt}</AMOUNT>");
            sb.AppendLine($"          <PARTYLEDGERNAME>{party}</PARTYLEDGERNAME>");
            sb.AppendLine("          <ISCANCELLED>No</ISCANCELLED>");
            sb.AppendLine("          <ISOPTIONAL>No</ISOPTIONAL>");
            sb.AppendLine($"          <ALTERID>{currentAlterId}</ALTERID>");

            // Debited to Expense
            sb.AppendLine("          <ALLLEDGERENTRIES.LIST>");
            sb.AppendLine("            <LEDGERNAME>Consultancy Fees</LEDGERNAME>");
            sb.AppendLine($"            <AMOUNT>{amt}</AMOUNT>");
            sb.AppendLine("          </ALLLEDGERENTRIES.LIST>");

            // Credited to Party
            sb.AppendLine("          <ALLLEDGERENTRIES.LIST>");
            sb.AppendLine($"            <LEDGERNAME>{party}</LEDGERNAME>");
            sb.AppendLine($"            <AMOUNT>-{payableAmt}</AMOUNT>");
            sb.AppendLine("          </ALLLEDGERENTRIES.LIST>");

            // Credited to TDS Payable
            if (tdsAmt > 0.00m)
            {
                sb.AppendLine("          <ALLLEDGERENTRIES.LIST>");
                sb.AppendLine("            <LEDGERNAME>TDS Payable Sec 194J</LEDGERNAME>");
                sb.AppendLine($"            <AMOUNT>-{tdsAmt}</AMOUNT>");
                sb.AppendLine("          </ALLLEDGERENTRIES.LIST>");
            }

            sb.AppendLine("        </VOUCHER>");
        }

        // Generate 50 Payments & Receipts (including duplicate cash payment checks)
        for (int i = 1; i <= 50; i++)
        {
            var date = new DateTime(2025, 5, 10).AddDays((i * 6) % 310);
            var dateStr = date.ToString("yyyyMMdd");
            var vNo = $"PMT-{i:D3}";
            var party = "Mehta Fabrication Works";
            decimal amt = 10000.00m + (i * 1000.00m);
            string? narration = $"Paid outstanding bill to {party}";
            string paymentType = "Payment";
            string led1 = "Mehta Fabrication Works";
            string led2 = "HDFC Bank Account";
            decimal l1Amt = amt;
            decimal l2Amt = -amt;
            long currentAlterId = alterId++;

            // Special duplicate Cash payment trigger (PMT-015 & PMT-016)
            if (i == 15)
            {
                dateStr = "20251015";
                vNo = "PMT-CASH-015";
                amt = 15000.00m;
                l1Amt = amt;
                l2Amt = -amt;
                led1 = "Mehta Fabrication Works";
                led2 = "Main Cash Account";
                narration = "Paid fabrication labor cash advances";
            }
            if (i == 16)
            {
                dateStr = "20251015";
                vNo = "PMT-CASH-016";
                amt = 15000.00m;
                l1Amt = amt;
                l2Amt = -amt;
                led1 = "Mehta Fabrication Works";
                led2 = "Main Cash Account";
                narration = "Paid fabrication labor cash advances"; // Same day, same amt, same narration
            }

            // Cash Dip Trigger (PMT-040)
            if (i == 40)
            {
                vNo = "PMT-CASH-DIP";
                amt = 120000.00m; // Exceeds the cash limit and forces Cash balance negative
                l1Amt = amt;
                l2Amt = -amt;
                led1 = "QuickLogistics Express";
                led2 = "Main Cash Account";
                narration = "Paid bulk transport charges in cash";
            }

            sb.AppendLine("        <VOUCHER>");
            sb.AppendLine($"          <GUID>{Guid.NewGuid()}</GUID>");
            sb.AppendLine($"          <VOUCHERNUMBER>{vNo}</VOUCHERNUMBER>");
            sb.AppendLine("          <REFERENCE></REFERENCE>");
            sb.AppendLine($"          <VOUCHERTYPENAME>{paymentType}</VOUCHERTYPENAME>");
            sb.AppendLine($"          <DATE>{dateStr}</DATE>");
            sb.AppendLine($"          <EFFECTIVEDATE>{dateStr}</EFFECTIVEDATE>");
            sb.AppendLine($"          <NARRATION>{narration}</NARRATION>");
            sb.AppendLine($"          <AMOUNT>{amt}</AMOUNT>");
            sb.AppendLine($"          <PARTYLEDGERNAME>{led1}</PARTYLEDGERNAME>");
            sb.AppendLine("          <ISCANCELLED>No</ISCANCELLED>");
            sb.AppendLine("          <ISOPTIONAL>No</ISOPTIONAL>");
            sb.AppendLine($"          <ALTERID>{currentAlterId}</ALTERID>");

            // Double Entry postings
            sb.AppendLine("          <ALLLEDGERENTRIES.LIST>");
            sb.AppendLine($"            <LEDGERNAME>{led1}</LEDGERNAME>");
            sb.AppendLine($"            <AMOUNT>{l1Amt}</AMOUNT>");
            sb.AppendLine("          </ALLLEDGERENTRIES.LIST>");

            sb.AppendLine("          <ALLLEDGERENTRIES.LIST>");
            sb.AppendLine($"            <LEDGERNAME>{led2}</LEDGERNAME>");
            sb.AppendLine($"            <AMOUNT>{l2Amt}</AMOUNT>");
            sb.AppendLine("          </ALLLEDGERENTRIES.LIST>");

            sb.AppendLine("        </VOUCHER>");
        }

        // Generate 10 Contra vouchers
        for (int i = 1; i <= 10; i++)
        {
            var date = new DateTime(2025, 4, 5).AddDays(i * 30);
            var dateStr = date.ToString("yyyyMMdd");
            var vNo = $"CON-{i:D3}";
            decimal amt = 10000.00m + (i * 5000.00m);
            decimal l1Amt = amt;
            decimal l2Amt = -amt;
            long currentAlterId = alterId++;

            // Special Contra Transfer Imbalance warning (CON-005)
            if (i == 5)
            {
                l1Amt = 25000.00m;
                l2Amt = -24000.00m; // Discrepancy gap of 1000
            }

            sb.AppendLine("        <VOUCHER>");
            sb.AppendLine($"          <GUID>{Guid.NewGuid()}</GUID>");
            sb.AppendLine($"          <VOUCHERNUMBER>{vNo}</VOUCHERNUMBER>");
            sb.AppendLine("          <VOUCHERTYPENAME>Contra</VOUCHERTYPENAME>");
            sb.AppendLine($"          <DATE>{dateStr}</DATE>");
            sb.AppendLine($"          <EFFECTIVEDATE>{dateStr}</EFFECTIVEDATE>");
            sb.AppendLine("          <NARRATION>Cash deposit to Bank</NARRATION>");
            sb.AppendLine($"          <AMOUNT>{amt}</AMOUNT>");
            sb.AppendLine("          <PARTYLEDGERNAME>HDFC Bank Account</PARTYLEDGERNAME>");
            sb.AppendLine("          <ISCANCELLED>No</ISCANCELLED>");
            sb.AppendLine("          <ISOPTIONAL>No</ISOPTIONAL>");
            sb.AppendLine($"          <ALTERID>{currentAlterId}</ALTERID>");

            sb.AppendLine("          <ALLLEDGERENTRIES.LIST>");
            sb.AppendLine("            <LEDGERNAME>HDFC Bank Account</LEDGERNAME>");
            sb.AppendLine($"            <AMOUNT>{l1Amt}</AMOUNT>");
            sb.AppendLine("          </ALLLEDGERENTRIES.LIST>");

            sb.AppendLine("          <ALLLEDGERENTRIES.LIST>");
            sb.AppendLine("            <LEDGERNAME>Main Cash Account</LEDGERNAME>");
            sb.AppendLine($"            <AMOUNT>{l2Amt}</AMOUNT>");
            sb.AppendLine("          </ALLLEDGERENTRIES.LIST>");

            sb.AppendLine("        </VOUCHER>");
        }

        sb.AppendLine("      </COLLECTION>");
        sb.AppendLine("    </DATA>");
        sb.AppendLine("  </BODY>");
        sb.AppendLine("</ENVELOPE>");
        return sb.ToString();
    }

    private static string GetPartyForSales(int index)
    {
        var parties = new[] { "Alpha Traders", "Beta Distributors", "Gamma Enterprises", "Local Consumer Retail", "Out of State Buyer" };
        return parties[index % parties.Length];
    }

    private static string GetPartyForPurchases(int index)
    {
        var parties = new[] { "Mehta Fabrication Works", "QuickLogistics Express" };
        return parties[index % parties.Length];
    }

    private static void AddSalesDuplicateVouchers(StringBuilder sb, ref int alterId)
    {
        // 1. Exact Duplicate (SAL-020-A & SAL-020-B)
        var dateStr = "20250520";
        var vNo = "SAL-DUP-020";
        var totalAmt = 45000.00m;
        var amt = 38135.59m;
        var taxAmt = 3432.20m;

        for (int d = 0; d < 2; d++)
        {
            long currentAlterId = alterId++;
            sb.AppendLine("        <VOUCHER>");
            sb.AppendLine($"          <GUID>{Guid.NewGuid()}</GUID>");
            sb.AppendLine($"          <VOUCHERNUMBER>{vNo}</VOUCHERNUMBER>");
            sb.AppendLine("          <REFERENCE>DUP-INV-020</REFERENCE>");
            sb.AppendLine("          <VOUCHERTYPENAME>Sales</VOUCHERTYPENAME>");
            sb.AppendLine($"          <DATE>{dateStr}</DATE>");
            sb.AppendLine($"          <EFFECTIVEDATE>{dateStr}</EFFECTIVEDATE>");
            sb.AppendLine("          <NARRATION>Duplicate entry for sales of fabrication components</NARRATION>");
            sb.AppendLine($"          <AMOUNT>{totalAmt}</AMOUNT>");
            sb.AppendLine("          <PARTYLEDGERNAME>Beta Distributors</PARTYLEDGERNAME>");
            sb.AppendLine("          <ISCANCELLED>No</ISCANCELLED>");
            sb.AppendLine("          <ISOPTIONAL>No</ISOPTIONAL>");
            sb.AppendLine($"          <ALTERID>{currentAlterId}</ALTERID>");

            sb.AppendLine("          <ALLLEDGERENTRIES.LIST>");
            sb.AppendLine("            <LEDGERNAME>Beta Distributors</LEDGERNAME>");
            sb.AppendLine($"            <AMOUNT>{totalAmt}</AMOUNT>");
            sb.AppendLine("          </ALLLEDGERENTRIES.LIST>");

            sb.AppendLine("          <ALLLEDGERENTRIES.LIST>");
            sb.AppendLine("            <LEDGERNAME>Product Sales 18%</LEDGERNAME>");
            sb.AppendLine($"            <AMOUNT>-{amt}</AMOUNT>");
            sb.AppendLine("          </ALLLEDGERENTRIES.LIST>");

            sb.AppendLine("          <ALLLEDGERENTRIES.LIST>");
            sb.AppendLine("            <LEDGERNAME>Output CGST 9%</LEDGERNAME>");
            sb.AppendLine($"            <AMOUNT>-{taxAmt}</AMOUNT>");
            sb.AppendLine("          </ALLLEDGERENTRIES.LIST>");

            sb.AppendLine("          <ALLLEDGERENTRIES.LIST>");
            sb.AppendLine("            <LEDGERNAME>Output SGST 9%</LEDGERNAME>");
            sb.AppendLine($"            <AMOUNT>-{taxAmt}</AMOUNT>");
            sb.AppendLine("          </ALLLEDGERENTRIES.LIST>");
            sb.AppendLine("        </VOUCHER>");
        }
    }
}
