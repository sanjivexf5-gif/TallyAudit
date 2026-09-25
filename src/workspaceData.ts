export type ExceptionSeverity = 'Critical' | 'High' | 'Medium' | 'Low';

export type ExceptionModule = 
  | 'General Accounting' 
  | 'GST Statutory' 
  | 'TDS Withholding' 
  | 'Duplicate Detection' 
  | 'Sequencing' 
  | 'Anomaly & Outlier';

export type ExceptionReviewStatus = 
  | 'Requires Review - Pending' 
  | 'Reviewed' 
  | 'Requires Investigation' 
  | 'Dismissed with Reason';

export interface RelatedTransactionItem {
  voucherNumber: string;
  voucherDate: string;
  voucherType: string;
  partyName: string;
  amount: number;
  relationType: 'Linked Invoice' | 'Bank Clearance' | 'Prior Adjustment' | 'Duplicate Candidate' | 'Tax Component' | 'Reversal Entry';
  note: string;
}

export interface VoucherLineEntry {
  entryId: string;
  ledgerName: string;
  parentGroup: string;
  amount: number;
  isDebit: boolean;
  hsnOrSac?: string;
  taxOrTdsRate?: number;
}

export interface TallyGstDrillDown {
  partyGstin?: string;
  registrationType: 'Regular' | 'Composition' | 'Unregistered' | 'Consumer' | 'Overseas / SEZ';
  placeOfSupply: string;
  isReverseCharge: boolean;
  hsnOrSac?: string;
  taxRatePercent?: number;
  taxableAmount: number;
  cgstAmount?: number;
  sgstAmount?: number;
  igstAmount?: number;
  cessAmount?: number;
  totalTaxAmount: number;
  gstLedgers: Array<{ ledgerName: string; amount: number; rate: number }>;
}

export interface TallyTdsDrillDown {
  isApplicable: boolean;
  sectionCode?: string;
  sectionDescription?: string;
  deducteePan?: string;
  panStatus?: 'Valid & Verified' | 'Invalid PAN' | 'Higher Rate - Section 206AA' | 'Not Available';
  tdsRatePercent?: number;
  assessableValue?: number;
  tdsAmountDeducted?: number;
  challanBSRCode?: string;
  certificateNumber?: string;
  tdsLedgerName?: string;
}

export interface TallyNavigationGuide {
  masterId: string;
  alterId: string;
  guid: string;
  gatewayPath: string;
  quickGoTo: string;
  exactKeys: string[];
  xmlQueryPayload: string;
}

export interface SourceVoucherDetail {
  voucherId: string;
  voucherNumber: string;
  voucherType: 'Sales' | 'Purchase' | 'Payment' | 'Receipt' | 'Journal' | 'Credit Note' | 'Debit Note' | 'Contra';
  voucherDate: string;
  referenceNumber?: string;
  partyLedgerName: string;
  partyGstin?: string;
  partyPan?: string;
  totalAmount: number;
  narration?: string;
  entries: VoucherLineEntry[];
}

export interface RelatedLedgerInfo {
  ledgerName: string;
  parentGroup: string;
  primaryHead: 'Current Assets' | 'Current Liabilities' | 'Direct Incomes' | 'Indirect Expenses' | 'Duties & Taxes' | 'Suspense Account' | 'Bank Accounts' | 'Cash-in-Hand';
  openingBalance: number;
  currentBalance: number;
  closingBalanceType: 'Dr' | 'Cr';
}

export interface ReviewHistoryEntry {
  id: string;
  timestamp: string;
  auditorName: string;
  action: string;
  note: string;
}

export interface WorkspaceExceptionItem {
  id: string;
  companyName: string;
  severity: ExceptionSeverity;
  module: ExceptionModule;
  ruleId: string;
  ruleName: string;
  ruleVersion: string;
  ruleEffectiveDate: string;
  ruleJurisdiction: string;
  statutoryReference: string;
  ruleDescription: string;
  
  exceptionTitle: string;
  whyFlagged: string;
  
  voucherNumber: string;
  voucherDate: string;
  voucherType: 'Sales' | 'Purchase' | 'Payment' | 'Receipt' | 'Journal' | 'Credit Note' | 'Debit Note' | 'Contra';
  
  partyLedgerName: string;
  primaryLedger: string;
  partyPan?: string;
  partyGstin?: string;
  
  amount: number;
  narration: string;
  evidenceJson: string;
  
  gstDetails: TallyGstDrillDown;
  tdsDetails?: TallyTdsDrillDown;
  tallyNavigationGuide: TallyNavigationGuide;
  
  relatedLedger: RelatedLedgerInfo;
  sourceVoucher: SourceVoucherDetail;
  relatedTransactions: RelatedTransactionItem[];
  
  status: ExceptionReviewStatus;
  dismissalReason?: string;
  reviewerNotes: string;
  reviewHistory: ReviewHistoryEntry[];
}

export const initialWorkspaceExceptions: WorkspaceExceptionItem[] = [
  // 1. Critical - GST Inter vs Intra mismatch
  {
    id: 'EXC-2026-001',
    companyName: 'Apex Industrial Solutions Pvt Ltd',
    severity: 'Critical',
    module: 'GST Statutory',
    ruleId: 'GST-CHK-05',
    ruleName: 'Interstate vs Intrastate Tax Allocation Consistency',
    ruleVersion: '1.2.0',
    ruleEffectiveDate: '01-Jul-2017',
    ruleJurisdiction: 'IN-ALL',
    statutoryReference: 'CGST Act 2017 Sec 8 / IGST Act 2017 Sec 7 & 8',
    ruleDescription: 'Verifies whether CGST+SGST is applied on intrastate supplies and IGST on interstate supplies based on state code prefixes.',
    exceptionTitle: 'Interstate Supply Charged With Local CGST + SGST',
    whyFlagged: 'The supplier ledger "Tata Steel Ltd (Gujarat Plant)" has GSTIN starting with state code 24 (Gujarat), but the voucher PUR-05 records local Maharashtra taxes (CGST 9% + SGST 9%) instead of Integrated Tax (IGST 18%).',
    voucherNumber: 'PUR-05',
    voucherDate: '05-Jun-2025',
    voucherType: 'Purchase',
    partyLedgerName: 'Tata Steel Ltd (Gujarat Plant)',
    primaryLedger: 'Raw Material Purchases - Steel',
    partyGstin: '24AAACT2727Q1ZW',
    partyPan: 'AAACT2727Q',
    amount: 147500,
    narration: 'Procurement of hot rolled steel coil consignment from Gujarat plant under PO-2025-8891',
    evidenceJson: JSON.stringify({
      SupplierStateCode: '24 (Gujarat)',
      CompanyStateCode: '27 (Maharashtra)',
      IsInterstate: true,
      TaxesPosted: ['CGST Input 9%', 'SGST Input 9%'],
      ExpectedTax: 'IGST Input 18%',
      VarianceAmount: 22500.00
    }, null, 2),
    gstDetails: {
      partyGstin: '24AAACT2727Q1ZW',
      registrationType: 'Regular',
      placeOfSupply: '24-Gujarat',
      isReverseCharge: false,
      hsnOrSac: '7208',
      taxRatePercent: 18.0,
      taxableAmount: 125000.00,
      cgstAmount: 11250.00,
      sgstAmount: 11250.00,
      igstAmount: 0.00,
      cessAmount: 0.00,
      totalTaxAmount: 22500.00,
      gstLedgers: [
        { ledgerName: 'CGST Input Tax (9%)', amount: 11250.00, rate: 9.0 },
        { ledgerName: 'SGST Input Tax (9%)', amount: 11250.00, rate: 9.0 }
      ]
    },
    tdsDetails: {
      isApplicable: true,
      sectionCode: '194Q',
      sectionDescription: 'TDS on Purchase of Goods exceeding ₹50 Lakhs',
      deducteePan: 'AAACT2727Q',
      panStatus: 'Valid & Verified',
      tdsRatePercent: 0.1,
      assessableValue: 125000.00,
      tdsAmountDeducted: 0.00,
      tdsLedgerName: 'TDS on Purchase 194Q Payable'
    },
    tallyNavigationGuide: {
      masterId: '10482',
      alterId: '42890',
      guid: '9a4c8e7b-12d4-4f89-a1b2-c3d4e5f60001',
      gatewayPath: 'Gateway of Tally > Display More Reports (D) > Account Books (A) > Purchase Register (P) > June 2025 > PUR-05',
      quickGoTo: 'Press Alt+G > Type "Purchase Register" > Enter > Locate Voucher # PUR-05 (05-Jun-2025)',
      exactKeys: ['Alt+G', 'Voucher Reports', 'Purchase Register', 'Enter on PUR-05'],
      xmlQueryPayload: `<ENVELOPE>
  <HEADER>
    <TALLYREQUEST>Export Data</TALLYREQUEST>
  </HEADER>
  <BODY>
    <EXPORTDATA>
      <REQUESTDESC>
        <REPORTNAME>Voucher</REPORTNAME>
        <STATICVARIABLES>
          <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
          <VOUCHERNUMBER>PUR-05</VOUCHERNUMBER>
          <VOUCHERTYPENAME>Purchase</VOUCHERTYPENAME>
          <MASTERID>10482</MASTERID>
        </STATICVARIABLES>
      </REQUESTDESC>
    </EXPORTDATA>
  </BODY>
</ENVELOPE>`
    },
    relatedLedger: {
      ledgerName: 'Tata Steel Ltd (Gujarat Plant)',
      parentGroup: 'Sundry Creditors',
      primaryHead: 'Current Liabilities',
      openingBalance: 0,
      currentBalance: 147500,
      closingBalanceType: 'Cr'
    },
    sourceVoucher: {
      voucherId: 'V5',
      voucherNumber: 'PUR-05',
      voucherType: 'Purchase',
      voucherDate: '05-Jun-2025',
      referenceNumber: 'TSL-GJ-8891',
      partyLedgerName: 'Tata Steel Ltd (Gujarat Plant)',
      partyGstin: '24AAACT2727Q1ZW',
      partyPan: 'AAACT2727Q',
      totalAmount: 147500,
      narration: 'Procurement of hot rolled steel coil consignment from Gujarat plant under PO-2025-8891',
      entries: [
        { entryId: 'e1', ledgerName: 'Raw Material Purchases - Steel', parentGroup: 'Direct Incomes', amount: 125000, isDebit: true, hsnOrSac: '7208', taxOrTdsRate: 18 },
        { entryId: 'e2', ledgerName: 'CGST Input Tax (9%)', parentGroup: 'Duties & Taxes', amount: 11250, isDebit: true, taxOrTdsRate: 9 },
        { entryId: 'e3', ledgerName: 'SGST Input Tax (9%)', parentGroup: 'Duties & Taxes', amount: 11250, isDebit: true, taxOrTdsRate: 9 },
        { entryId: 'e4', ledgerName: 'Tata Steel Ltd (Gujarat Plant)', parentGroup: 'Sundry Creditors', amount: 147500, isDebit: false }
      ]
    },
    relatedTransactions: [
      {
        voucherNumber: 'PUR-01',
        voucherDate: '01-Apr-2025',
        voucherType: 'Purchase',
        partyName: 'Tata Steel Ltd (Gujarat Plant)',
        amount: 325000,
        relationType: 'Linked Invoice',
        note: 'Prior purchase correctly booked with IGST 18%.'
      }
    ],
    status: 'Requires Review - Pending',
    reviewerNotes: '',
    reviewHistory: [
      {
        id: 'H1',
        timestamp: '25-Sep-2026 09:15:00 AM',
        auditorName: 'Automated Audit Engine',
        action: 'Flagged for Review',
        note: 'Triggered by Rule [GST-CHK-05].'
      }
    ]
  },

  // 2. Critical - TDS Section 206AA Missing PAN Higher Rate 20%
  {
    id: 'EXC-2026-002',
    companyName: 'Apex Industrial Solutions Pvt Ltd',
    severity: 'Critical',
    module: 'TDS Withholding',
    ruleId: 'TDS-206AA-01',
    ruleName: 'Mandatory 20% TDS Rate for Deductee Without PAN',
    ruleVersion: '2.0.1',
    ruleEffectiveDate: '01-Apr-2010',
    ruleJurisdiction: 'IN-ALL',
    statutoryReference: 'Income Tax Act 1961 Sec 206AA',
    ruleDescription: 'Requires deduction of tax at higher rate (20% or statutory rate) if the deductee fails to furnish valid PAN.',
    exceptionTitle: 'Contractor Payment Without PAN Deducted at Base 1% instead of 20%',
    whyFlagged: 'Contractor ledger "Shiv Shakti Logistics" has no valid PAN recorded in Tally, but TDS was deducted at standard 1% (₹750) under 194C rather than the mandatory statutory 20% rate (₹15,000) under Section 206AA.',
    voucherNumber: 'JRN-2025-104',
    voucherDate: '12-Jul-2025',
    voucherType: 'Journal',
    partyLedgerName: 'Shiv Shakti Logistics',
    primaryLedger: 'Freight & Transportation Charges',
    amount: 75000,
    narration: 'Freight charges for transport of raw materials from Bhiwandi warehouse to plant',
    evidenceJson: JSON.stringify({
      Party: 'Shiv Shakti Logistics',
      PANStatus: 'NOT_RECORDED',
      Section: '194C',
      GrossAmount: 75000.00,
      AppliedRate: '1.0%',
      DeductedAmount: 750.00,
      StatutoryRequiredRate: '20.0%',
      MandatedDeduction: 15000.00,
      ShortDeductionVariance: 14250.00
    }, null, 2),
    gstDetails: {
      registrationType: 'Unregistered',
      placeOfSupply: '27-Maharashtra',
      isReverseCharge: true,
      hsnOrSac: '9965',
      taxRatePercent: 5.0,
      taxableAmount: 75000.00,
      cgstAmount: 1875.00,
      sgstAmount: 1875.00,
      igstAmount: 0.00,
      cessAmount: 0.00,
      totalTaxAmount: 3750.00,
      gstLedgers: [
        { ledgerName: 'RCM CGST Payable (2.5%)', amount: 1875.00, rate: 2.5 },
        { ledgerName: 'RCM SGST Payable (2.5%)', amount: 1875.00, rate: 2.5 }
      ]
    },
    tdsDetails: {
      isApplicable: true,
      sectionCode: '194C / 206AA',
      sectionDescription: 'TDS on Contractors - Higher Rate for Non-PAN',
      deducteePan: 'PAN NOT FURNISHED',
      panStatus: 'Higher Rate - Section 206AA',
      tdsRatePercent: 1.0,
      assessableValue: 75000.00,
      tdsAmountDeducted: 750.00,
      challanBSRCode: '0210045',
      certificateNumber: 'PENDING_AUDIT',
      tdsLedgerName: 'TDS on Contractors 194C Payable'
    },
    tallyNavigationGuide: {
      masterId: '10512',
      alterId: '43015',
      guid: '9a4c8e7b-12d4-4f89-a1b2-c3d4e5f60002',
      gatewayPath: 'Gateway of Tally > Display More Reports (D) > Account Books (A) > Journal Register (J) > July 2025 > JRN-2025-104',
      quickGoTo: 'Press Alt+G > Type "Day Book" > Press F2 (12-Jul-2025) > Select JRN-2025-104',
      exactKeys: ['Alt+G', 'Day Book', 'F2: 12-Jul-2025', 'Enter on JRN-2025-104'],
      xmlQueryPayload: `<ENVELOPE>
  <HEADER>
    <TALLYREQUEST>Export Data</TALLYREQUEST>
  </HEADER>
  <BODY>
    <EXPORTDATA>
      <REQUESTDESC>
        <REPORTNAME>Voucher</REPORTNAME>
        <STATICVARIABLES>
          <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
          <VOUCHERNUMBER>JRN-2025-104</VOUCHERNUMBER>
          <VOUCHERTYPENAME>Journal</VOUCHERTYPENAME>
          <MASTERID>10512</MASTERID>
        </STATICVARIABLES>
      </REQUESTDESC>
    </EXPORTDATA>
  </BODY>
</ENVELOPE>`
    },
    relatedLedger: {
      ledgerName: 'Shiv Shakti Logistics',
      parentGroup: 'Sundry Creditors',
      primaryHead: 'Current Liabilities',
      openingBalance: 0,
      currentBalance: 74250,
      closingBalanceType: 'Cr'
    },
    sourceVoucher: {
      voucherId: 'V104',
      voucherNumber: 'JRN-2025-104',
      voucherType: 'Journal',
      voucherDate: '12-Jul-2025',
      referenceNumber: 'LR-9921',
      partyLedgerName: 'Shiv Shakti Logistics',
      totalAmount: 75000,
      narration: 'Freight charges for transport of raw materials from Bhiwandi warehouse to plant',
      entries: [
        { entryId: 'e201', ledgerName: 'Freight & Transportation Charges', parentGroup: 'Direct Incomes', amount: 75000, isDebit: true, hsnOrSac: '9965' },
        { entryId: 'e202', ledgerName: 'TDS on Contractors 194C Payable', parentGroup: 'Duties & Taxes', amount: 750, isDebit: false, taxOrTdsRate: 1 },
        { entryId: 'e203', ledgerName: 'Shiv Shakti Logistics', parentGroup: 'Sundry Creditors', amount: 74250, isDebit: false }
      ]
    },
    relatedTransactions: [],
    status: 'Requires Review - Pending',
    reviewerNotes: '',
    reviewHistory: [
      {
        id: 'H2',
        timestamp: '25-Sep-2026 09:15:00 AM',
        auditorName: 'Automated Audit Engine',
        action: 'Flagged for Review',
        note: 'Triggered by Rule [TDS-206AA-01].'
      }
    ]
  },

  // 3. High - Negative Cash Balance
  {
    id: 'EXC-2026-003',
    companyName: 'Apex Industrial Solutions Pvt Ltd',
    severity: 'High',
    module: 'General Accounting',
    ruleId: 'ACC-NEG-01',
    ruleName: 'Intraday Negative Cash Ledger Balance Detection',
    ruleVersion: '1.0.0',
    ruleEffectiveDate: '01-Apr-2024',
    ruleJurisdiction: 'Accounting Standard AS-3 / Ind AS 7',
    statutoryReference: 'Companies Act 2013 Sec 143(3)',
    ruleDescription: 'Ensures physical cash ledger balances do not drop below zero on any calendar date after transaction posting.',
    exceptionTitle: 'Petty Cash Ledger Deficit (-₹14,500.00 Cr Balance)',
    whyFlagged: 'After posting disbursement payment PMT-889 of ₹25,000, the "Petty Cash Ledger" closing balance dropped to ₹14,500 Credit deficit because replenishment contra was recorded on a later date.',
    voucherNumber: 'PMT-889',
    voucherDate: '14-Aug-2025',
    voucherType: 'Payment',
    partyLedgerName: 'Factory Welfare & Refreshment Exp',
    primaryLedger: 'Petty Cash Ledger',
    amount: 25000,
    narration: 'Disbursement for factory workers annual welfare tea snacks and cleaning supplies',
    evidenceJson: JSON.stringify({
      Ledger: 'Petty Cash Ledger',
      OpeningBalanceOnDate: 10500.00,
      DisbursementAmount: 25000.00,
      PostTransactionBalance: -14500.00,
      DeficitType: 'Credit (Negative Cash Balance)',
      SubsequentReplenishmentDate: '16-Aug-2025'
    }, null, 2),
    gstDetails: {
      registrationType: 'Unregistered',
      placeOfSupply: '27-Maharashtra',
      isReverseCharge: false,
      taxableAmount: 25000.00,
      totalTaxAmount: 0.00,
      gstLedgers: []
    },
    tdsDetails: {
      isApplicable: false
    },
    tallyNavigationGuide: {
      masterId: '10620',
      alterId: '43180',
      guid: '9a4c8e7b-12d4-4f89-a1b2-c3d4e5f60003',
      gatewayPath: 'Gateway of Tally > Display More Reports (D) > Account Books (A) > Cash/Bank Book(s) (C) > Petty Cash Ledger > August 2025 > PMT-889',
      quickGoTo: 'Press Alt+G > Type "Cash/Bank Books" > Select "Petty Cash Ledger" > Navigate to 14-Aug-2025',
      exactKeys: ['Alt+G', 'Cash/Bank Books', 'Petty Cash Ledger', 'F2: 14-Aug-2025'],
      xmlQueryPayload: `<ENVELOPE>
  <HEADER>
    <TALLYREQUEST>Export Data</TALLYREQUEST>
  </HEADER>
  <BODY>
    <EXPORTDATA>
      <REQUESTDESC>
        <REPORTNAME>Voucher</REPORTNAME>
        <STATICVARIABLES>
          <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
          <VOUCHERNUMBER>PMT-889</VOUCHERNUMBER>
          <VOUCHERTYPENAME>Payment</VOUCHERTYPENAME>
          <MASTERID>10620</MASTERID>
        </STATICVARIABLES>
      </REQUESTDESC>
    </EXPORTDATA>
  </BODY>
</ENVELOPE>`
    },
    relatedLedger: {
      ledgerName: 'Petty Cash Ledger',
      parentGroup: 'Cash-in-Hand',
      primaryHead: 'Cash-in-Hand',
      openingBalance: 10500,
      currentBalance: -14500,
      closingBalanceType: 'Cr'
    },
    sourceVoucher: {
      voucherId: 'V889',
      voucherNumber: 'PMT-889',
      voucherType: 'Payment',
      voucherDate: '14-Aug-2025',
      partyLedgerName: 'Factory Welfare & Refreshment Exp',
      totalAmount: 25000,
      narration: 'Disbursement for factory workers annual welfare tea snacks and cleaning supplies',
      entries: [
        { entryId: 'e301', ledgerName: 'Factory Welfare & Refreshment Exp', parentGroup: 'Indirect Expenses', amount: 25000, isDebit: true },
        { entryId: 'e302', ledgerName: 'Petty Cash Ledger', parentGroup: 'Cash-in-Hand', amount: 25000, isDebit: false }
      ]
    },
    relatedTransactions: [
      {
        voucherNumber: 'CNT-044',
        voucherDate: '16-Aug-2025',
        voucherType: 'Contra',
        partyName: 'HDFC Bank Current A/c',
        amount: 50000,
        relationType: 'Bank Clearance',
        note: 'Cash withdrawal contra entered 2 days late.'
      }
    ],
    status: 'Requires Review - Pending',
    reviewerNotes: '',
    reviewHistory: [
      {
        id: 'H3',
        timestamp: '25-Sep-2026 09:15:00 AM',
        auditorName: 'Automated Audit Engine',
        action: 'Flagged for Review',
        note: 'Triggered by Rule [ACC-NEG-01].'
      }
    ]
  },

  // 4. High - Exact Duplicate Sales Invoice
  {
    id: 'EXC-2026-004',
    companyName: 'Apex Industrial Solutions Pvt Ltd',
    severity: 'High',
    module: 'Duplicate Detection',
    ruleId: 'DUP-SAL-01',
    ruleName: 'Exact Duplicate Sales Invoice Match',
    ruleVersion: '2.1.0',
    ruleEffectiveDate: '01-Apr-2024',
    ruleJurisdiction: 'Internal Control & Revenue Recognition',
    statutoryReference: 'Ind AS 115 / ICAI Guidance Note on Auditing',
    ruleDescription: 'Detects multi-pass candidate collisions on Party + Exact Amount + Exact Invoice Number.',
    exceptionTitle: 'Duplicate Sales Invoice Booking (INV-2025-089)',
    whyFlagged: 'Voucher INV-2025-089-B booked on 14-Jul-2025 has identical Party "Bharat Petrochem Industries", exact amount ₹2,36,000, and identical invoice numbering as INV-2025-089.',
    voucherNumber: 'INV-2025-089-B',
    voucherDate: '14-Jul-2025',
    voucherType: 'Sales',
    partyLedgerName: 'Bharat Petrochem Industries',
    primaryLedger: 'Sales - Domestic Finished Goods',
    partyGstin: '27AAACB9901M1Z5',
    partyPan: 'AAACB9901M',
    amount: 236000,
    narration: 'Supply of 20 metric tons polymer plasticizer compounding resin',
    evidenceJson: JSON.stringify({
      CandidateA: 'INV-2025-089 (14-Jul-2025)',
      CandidateB: 'INV-2025-089-B (14-Jul-2025)',
      PartyMatch: 'Bharat Petrochem Industries (100%)',
      AmountMatch: '₹2,36,000.00 (Exact)',
      ConfidenceTier: 'Exact Duplicate (100%)'
    }, null, 2),
    gstDetails: {
      partyGstin: '27AAACB9901M1Z5',
      registrationType: 'Regular',
      placeOfSupply: '27-Maharashtra',
      isReverseCharge: false,
      hsnOrSac: '3901',
      taxRatePercent: 18.0,
      taxableAmount: 200000.00,
      cgstAmount: 18000.00,
      sgstAmount: 18000.00,
      igstAmount: 0.00,
      cessAmount: 0.00,
      totalTaxAmount: 36000.00,
      gstLedgers: [
        { ledgerName: 'CGST Output Tax (9%)', amount: 18000.00, rate: 9.0 },
        { ledgerName: 'SGST Output Tax (9%)', amount: 18000.00, rate: 9.0 }
      ]
    },
    tdsDetails: {
      isApplicable: false
    },
    tallyNavigationGuide: {
      masterId: '10705',
      alterId: '43310',
      guid: '9a4c8e7b-12d4-4f89-a1b2-c3d4e5f60004',
      gatewayPath: 'Gateway of Tally > Display More Reports (D) > Account Books (A) > Sales Register (S) > July 2025 > INV-2025-089-B',
      quickGoTo: 'Press Alt+G > Type "Sales Register" > Press Enter > Select July 2025 > Highlight INV-2025-089-B',
      exactKeys: ['Alt+G', 'Sales Register', 'July 2025', 'Enter on INV-2025-089-B'],
      xmlQueryPayload: `<ENVELOPE>
  <HEADER>
    <TALLYREQUEST>Export Data</TALLYREQUEST>
  </HEADER>
  <BODY>
    <EXPORTDATA>
      <REQUESTDESC>
        <REPORTNAME>Voucher</REPORTNAME>
        <STATICVARIABLES>
          <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
          <VOUCHERNUMBER>INV-2025-089-B</VOUCHERNUMBER>
          <VOUCHERTYPENAME>Sales</VOUCHERTYPENAME>
          <MASTERID>10705</MASTERID>
        </STATICVARIABLES>
      </REQUESTDESC>
    </EXPORTDATA>
  </BODY>
</ENVELOPE>`
    },
    relatedLedger: {
      ledgerName: 'Bharat Petrochem Industries',
      parentGroup: 'Sundry Debtors',
      primaryHead: 'Current Assets',
      openingBalance: 0,
      currentBalance: 472000,
      closingBalanceType: 'Dr'
    },
    sourceVoucher: {
      voucherId: 'V-DUP-02',
      voucherNumber: 'INV-2025-089-B',
      voucherType: 'Sales',
      voucherDate: '14-Jul-2025',
      referenceNumber: 'PO-BP-771',
      partyLedgerName: 'Bharat Petrochem Industries',
      partyGstin: '27AAACB9901M1Z5',
      partyPan: 'AAACB9901M',
      totalAmount: 236000,
      narration: 'Supply of 20 metric tons polymer plasticizer compounding resin',
      entries: [
        { entryId: 'd1', ledgerName: 'Bharat Petrochem Industries', parentGroup: 'Sundry Debtors', amount: 236000, isDebit: true },
        { entryId: 'd2', ledgerName: 'Sales - Domestic Finished Goods', parentGroup: 'Direct Incomes', amount: 200000, isDebit: false, hsnOrSac: '3901', taxOrTdsRate: 18 },
        { entryId: 'd3', ledgerName: 'CGST Output Tax (9%)', parentGroup: 'Duties & Taxes', amount: 18000, isDebit: false, taxOrTdsRate: 9 },
        { entryId: 'd4', ledgerName: 'SGST Output Tax (9%)', parentGroup: 'Duties & Taxes', amount: 18000, isDebit: false, taxOrTdsRate: 9 }
      ]
    },
    relatedTransactions: [
      {
        voucherNumber: 'INV-2025-089',
        voucherDate: '14-Jul-2025',
        voucherType: 'Sales',
        partyName: 'Bharat Petrochem Industries',
        amount: 236000,
        relationType: 'Duplicate Candidate',
        note: 'Original sales voucher entered at 10:14 AM by user operator.'
      }
    ],
    status: 'Requires Review - Pending',
    reviewerNotes: '',
    reviewHistory: [
      {
        id: 'H4',
        timestamp: '25-Sep-2026 09:15:00 AM',
        auditorName: 'Automated Audit Engine',
        action: 'Flagged for Review',
        note: 'Triggered by Rule [DUP-SAL-01].'
      }
    ]
  },

  // 5. Medium - Missing Voucher Number Sequence Gap
  {
    id: 'EXC-2026-005',
    companyName: 'Apex Industrial Solutions Pvt Ltd',
    severity: 'Medium',
    module: 'Sequencing',
    ruleId: 'SEQ-GAP-01',
    ruleName: 'Sequential Voucher Numbering Gap Detection',
    ruleVersion: '1.0.0',
    ruleEffectiveDate: '01-Apr-2024',
    ruleJurisdiction: 'Statutory Compliances & Internal Controls',
    statutoryReference: 'CGST Rules 2017 Rule 46(b)',
    ruleDescription: 'Flags breaks in continuous sequential voucher numbering sequences indicating possible missing or deleted entries.',
    exceptionTitle: 'Sequential Gap in Sales Invoices (Missing INV-2025-045)',
    whyFlagged: 'Voucher series jumps from INV-2025-044 (02-May-2025) directly to INV-2025-046 (03-May-2025), leaving invoice INV-2025-045 missing in the audit trail.',
    voucherNumber: 'INV-2025-046',
    voucherDate: '03-May-2025',
    voucherType: 'Sales',
    partyLedgerName: 'Godrej Process Equipment',
    primaryLedger: 'Sales - Domestic Finished Goods',
    partyGstin: '27AAACG1234F1Z8',
    partyPan: 'AAACG1234F',
    amount: 580000,
    narration: 'Fabrication components dispatched as per drawing specification GPE-2025',
    evidenceJson: JSON.stringify({
      SeriesPrefix: 'INV-2025-',
      PreviousVoucher: 'INV-2025-044 (02-May-2025)',
      CurrentVoucher: 'INV-2025-046 (03-May-2025)',
      MissingVouchers: ['INV-2025-045'],
      GapCount: 1
    }, null, 2),
    gstDetails: {
      partyGstin: '27AAACG1234F1Z8',
      registrationType: 'Regular',
      placeOfSupply: '27-Maharashtra',
      isReverseCharge: false,
      hsnOrSac: '8419',
      taxRatePercent: 18.0,
      taxableAmount: 491525.42,
      cgstAmount: 44237.29,
      sgstAmount: 44237.29,
      igstAmount: 0.00,
      cessAmount: 0.00,
      totalTaxAmount: 88474.58,
      gstLedgers: [
        { ledgerName: 'CGST Output Tax (9%)', amount: 44237.29, rate: 9.0 },
        { ledgerName: 'SGST Output Tax (9%)', amount: 44237.29, rate: 9.0 }
      ]
    },
    tdsDetails: {
      isApplicable: false
    },
    tallyNavigationGuide: {
      masterId: '10780',
      alterId: '43420',
      guid: '9a4c8e7b-12d4-4f89-a1b2-c3d4e5f60005',
      gatewayPath: 'Gateway of Tally > Display More Reports (D) > Account Books (A) > Sales Register (S) > May 2025 > INV-2025-046',
      quickGoTo: 'Press Alt+G > Type "Voucher Register" > Select Sales > May 2025',
      exactKeys: ['Alt+G', 'Sales Register', 'May 2025', 'Enter on INV-2025-046'],
      xmlQueryPayload: `<ENVELOPE>
  <HEADER>
    <TALLYREQUEST>Export Data</TALLYREQUEST>
  </HEADER>
  <BODY>
    <EXPORTDATA>
      <REQUESTDESC>
        <REPORTNAME>Voucher</REPORTNAME>
        <STATICVARIABLES>
          <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
          <VOUCHERNUMBER>INV-2025-046</VOUCHERNUMBER>
          <VOUCHERTYPENAME>Sales</VOUCHERTYPENAME>
          <MASTERID>10780</MASTERID>
        </STATICVARIABLES>
      </REQUESTDESC>
    </EXPORTDATA>
  </BODY>
</ENVELOPE>`
    },
    relatedLedger: {
      ledgerName: 'Godrej Process Equipment',
      parentGroup: 'Sundry Debtors',
      primaryHead: 'Current Assets',
      openingBalance: 0,
      currentBalance: 580000,
      closingBalanceType: 'Dr'
    },
    sourceVoucher: {
      voucherId: 'V-SEQ-01',
      voucherNumber: 'INV-2025-046',
      voucherType: 'Sales',
      voucherDate: '03-May-2025',
      referenceNumber: 'GPE-441',
      partyLedgerName: 'Godrej Process Equipment',
      partyGstin: '27AAACG1234F1Z8',
      partyPan: 'AAACG1234F',
      totalAmount: 580000,
      narration: 'Fabrication components dispatched as per drawing specification GPE-2025',
      entries: [
        { entryId: 'sq1', ledgerName: 'Godrej Process Equipment', parentGroup: 'Sundry Debtors', amount: 580000, isDebit: true },
        { entryId: 'sq2', ledgerName: 'Sales - Domestic Finished Goods', parentGroup: 'Direct Incomes', amount: 491525.42, isDebit: false, hsnOrSac: '8419', taxOrTdsRate: 18 },
        { entryId: 'sq3', ledgerName: 'CGST Output Tax (9%)', parentGroup: 'Duties & Taxes', amount: 44237.29, isDebit: false, taxOrTdsRate: 9 },
        { entryId: 'sq4', ledgerName: 'SGST Output Tax (9%)', parentGroup: 'Duties & Taxes', amount: 44237.29, isDebit: false, taxOrTdsRate: 9 }
      ]
    },
    relatedTransactions: [],
    status: 'Requires Review - Pending',
    reviewerNotes: '',
    reviewHistory: [
      {
        id: 'H5',
        timestamp: '25-Sep-2026 09:15:00 AM',
        auditorName: 'Automated Audit Engine',
        action: 'Flagged for Review',
        note: 'Triggered by Rule [SEQ-GAP-01].'
      }
    ]
  },

  // 6. Medium - Non-standard Tax Rate
  {
    id: 'EXC-2026-006',
    companyName: 'Apex Industrial Solutions Pvt Ltd',
    severity: 'Medium',
    module: 'GST Statutory',
    ruleId: 'GST-CHK-03',
    ruleName: 'Statutory GST Tax Rate Slab Consistency',
    ruleVersion: '1.2.0',
    ruleEffectiveDate: '01-Jul-2017',
    ruleJurisdiction: 'IN-ALL',
    statutoryReference: 'CGST Act 2017 Sec 9 / Notification 1/2017-CT(Rate)',
    ruleDescription: 'Validates that calculated effective tax rate belongs to official GST slabs (0%, 5%, 12%, 18%, 28%).',
    exceptionTitle: 'Irregular GST Rate (13.5%) Computed on Service Invoice',
    whyFlagged: 'Invoice SRV-2025-012 records taxable value ₹1,00,000 with tax ₹13,500 (effective rate 13.5%), which does not match standard 12% or 18% statutory slabs.',
    voucherNumber: 'SRV-2025-012',
    voucherDate: '22-Jun-2025',
    voucherType: 'Sales',
    partyLedgerName: 'Precision Engineering Works',
    primaryLedger: 'Job Work & Machine Calibration Charges',
    partyGstin: '27AAACP5521L1Z2',
    partyPan: 'AAACP5521L',
    amount: 113500,
    narration: 'Job work CNC precision milling and hardening calibration charges',
    evidenceJson: JSON.stringify({
      TaxableValue: 100000.00,
      TotalTaxBilled: 13500.00,
      EffectiveTaxRate: '13.50%',
      NearestStandardSlabs: ['12.0%', '18.0%'],
      AnomalyClassification: 'Possible manual tax ledger override'
    }, null, 2),
    gstDetails: {
      partyGstin: '27AAACP5521L1Z2',
      registrationType: 'Regular',
      placeOfSupply: '27-Maharashtra',
      isReverseCharge: false,
      hsnOrSac: '9987',
      taxRatePercent: 13.5,
      taxableAmount: 100000.00,
      cgstAmount: 6750.00,
      sgstAmount: 6750.00,
      igstAmount: 0.00,
      cessAmount: 0.00,
      totalTaxAmount: 13500.00,
      gstLedgers: [
        { ledgerName: 'CGST Output Tax', amount: 6750.00, rate: 6.75 },
        { ledgerName: 'SGST Output Tax', amount: 6750.00, rate: 6.75 }
      ]
    },
    tdsDetails: {
      isApplicable: false
    },
    tallyNavigationGuide: {
      masterId: '10815',
      alterId: '43501',
      guid: '9a4c8e7b-12d4-4f89-a1b2-c3d4e5f60006',
      gatewayPath: 'Gateway of Tally > Display More Reports (D) > Account Books (A) > Sales Register (S) > June 2025 > SRV-2025-012',
      quickGoTo: 'Press Alt+G > Type "Sales Register" > June 2025 > SRV-2025-012',
      exactKeys: ['Alt+G', 'Sales Register', 'June 2025', 'Enter on SRV-2025-012'],
      xmlQueryPayload: `<ENVELOPE>
  <HEADER>
    <TALLYREQUEST>Export Data</TALLYREQUEST>
  </HEADER>
  <BODY>
    <EXPORTDATA>
      <REQUESTDESC>
        <REPORTNAME>Voucher</REPORTNAME>
        <STATICVARIABLES>
          <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
          <VOUCHERNUMBER>SRV-2025-012</VOUCHERNUMBER>
          <VOUCHERTYPENAME>Sales</VOUCHERTYPENAME>
          <MASTERID>10815</MASTERID>
        </STATICVARIABLES>
      </REQUESTDESC>
    </EXPORTDATA>
  </BODY>
</ENVELOPE>`
    },
    relatedLedger: {
      ledgerName: 'Precision Engineering Works',
      parentGroup: 'Sundry Debtors',
      primaryHead: 'Current Assets',
      openingBalance: 0,
      currentBalance: 113500,
      closingBalanceType: 'Dr'
    },
    sourceVoucher: {
      voucherId: 'V-TX-01',
      voucherNumber: 'SRV-2025-012',
      voucherType: 'Sales',
      voucherDate: '22-Jun-2025',
      referenceNumber: 'PEW-991',
      partyLedgerName: 'Precision Engineering Works',
      partyGstin: '27AAACP5521L1Z2',
      partyPan: 'AAACP5521L',
      totalAmount: 113500,
      narration: 'Job work CNC precision milling and hardening calibration charges',
      entries: [
        { entryId: 't1', ledgerName: 'Precision Engineering Works', parentGroup: 'Sundry Debtors', amount: 113500, isDebit: true },
        { entryId: 't2', ledgerName: 'Job Work & Machine Calibration Charges', parentGroup: 'Direct Incomes', amount: 100000, isDebit: false, hsnOrSac: '9987' },
        { entryId: 't3', ledgerName: 'CGST Output Tax', parentGroup: 'Duties & Taxes', amount: 6750, isDebit: false, taxOrTdsRate: 6.75 },
        { entryId: 't4', ledgerName: 'SGST Output Tax', parentGroup: 'Duties & Taxes', amount: 6750, isDebit: false, taxOrTdsRate: 6.75 }
      ]
    },
    relatedTransactions: [],
    status: 'Requires Review - Pending',
    reviewerNotes: '',
    reviewHistory: [
      {
        id: 'H6',
        timestamp: '25-Sep-2026 09:15:00 AM',
        auditorName: 'Automated Audit Engine',
        action: 'Flagged for Review',
        note: 'Triggered by Rule [GST-CHK-03].'
      }
    ]
  },

  // 7. Low - Round Figure Disbursement
  {
    id: 'EXC-2026-007',
    companyName: 'Apex Industrial Solutions Pvt Ltd',
    severity: 'Low',
    module: 'Anomaly & Outlier',
    ruleId: 'ACC-ANO-02',
    ruleName: 'High-Value Exact Round Number Payment',
    ruleVersion: '1.0.0',
    ruleEffectiveDate: '01-Apr-2025',
    ruleJurisdiction: 'Forensic Audit',
    statutoryReference: 'ISA 240 Fraud Detection Guidelines',
    ruleDescription: 'Flags round-number disbursements (exact multiples of ₹10,000/₹50,000) for petty cash verification.',
    exceptionTitle: 'Round-Figure Payment of ₹1,00,000.00 to Site Petty Cash',
    whyFlagged: 'Payment voucher PMT/0101 has an exact round figure of ₹1,00,000.00 without fractional paise or attached expense bills.',
    voucherNumber: 'PMT/0101',
    voucherDate: '10-Jul-2025',
    voucherType: 'Payment',
    partyLedgerName: 'Site Petty Cash Advance',
    primaryLedger: 'HDFC Bank Current A/c',
    amount: 100000,
    narration: 'Site petty cash advance imprest refill for factory maintenance work order #12',
    evidenceJson: JSON.stringify({
      VoucherNumber: 'PMT/0101',
      Amount: 100000.00,
      RoundMultiple: 10000,
      Party: 'Site Petty Cash Advance'
    }, null, 2),
    gstDetails: {
      registrationType: 'Unregistered',
      placeOfSupply: '27-Maharashtra',
      isReverseCharge: false,
      taxableAmount: 100000.00,
      totalTaxAmount: 0.00,
      gstLedgers: []
    },
    tdsDetails: {
      isApplicable: false
    },
    tallyNavigationGuide: {
      masterId: '10920',
      alterId: '43650',
      guid: '9a4c8e7b-12d4-4f89-a1b2-c3d4e5f60007',
      gatewayPath: 'Gateway of Tally > Display More Reports (D) > Account Books (A) > Payment Register (P) > July 2025 > PMT/0101',
      quickGoTo: 'Press Alt+G > Type "Payment Register" > July 2025 > PMT/0101',
      exactKeys: ['Alt+G', 'Payment Register', 'July 2025', 'Enter on PMT/0101'],
      xmlQueryPayload: `<ENVELOPE>
  <HEADER>
    <TALLYREQUEST>Export Data</TALLYREQUEST>
  </HEADER>
  <BODY>
    <EXPORTDATA>
      <REQUESTDESC>
        <REPORTNAME>Voucher</REPORTNAME>
        <STATICVARIABLES>
          <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
          <VOUCHERNUMBER>PMT/0101</VOUCHERNUMBER>
          <VOUCHERTYPENAME>Payment</VOUCHERTYPENAME>
          <MASTERID>10920</MASTERID>
        </STATICVARIABLES>
      </REQUESTDESC>
    </EXPORTDATA>
  </BODY>
</ENVELOPE>`
    },
    relatedLedger: {
      ledgerName: 'Site Petty Cash Advance',
      parentGroup: 'Cash-in-Hand',
      primaryHead: 'Current Assets',
      openingBalance: 0,
      currentBalance: 100000,
      closingBalanceType: 'Dr'
    },
    sourceVoucher: {
      voucherId: 'V-ANO-01',
      voucherNumber: 'PMT/0101',
      voucherType: 'Payment',
      voucherDate: '10-Jul-2025',
      partyLedgerName: 'Site Petty Cash Advance',
      totalAmount: 100000,
      narration: 'Site petty cash advance imprest refill for factory maintenance work order #12',
      entries: [
        { entryId: 'an1', ledgerName: 'Site Petty Cash Advance', parentGroup: 'Cash-in-Hand', amount: 100000, isDebit: true },
        { entryId: 'an2', ledgerName: 'HDFC Bank Current A/c', parentGroup: 'Bank Accounts', amount: 100000, isDebit: false }
      ]
    },
    relatedTransactions: [],
    status: 'Dismissed with Reason',
    dismissalReason: 'Documented Policy Deviation (Approved Imprest Refill)',
    reviewerNotes: 'Approved petty cash imprest refill for factory maintenance work order #12 with management sanction.',
    reviewHistory: [
      {
        id: 'H7-1',
        timestamp: '25-Sep-2026 09:15:00 AM',
        auditorName: 'Automated Audit Engine',
        action: 'Flagged for Review',
        note: 'Triggered by Rule [ACC-ANO-02].'
      },
      {
        id: 'H7-2',
        timestamp: '25-Sep-2026 09:20:00 AM',
        auditorName: 'Senior Auditor (S. Sharma)',
        action: 'Dismissed with Reason',
        note: 'Dismissed with Reason: Documented Policy Deviation (Approved Imprest Refill).'
      }
    ]
  },

  // 8. High - Missing GSTIN on B2B High-Value Purchase
  {
    id: 'EXC-2026-008',
    companyName: 'Apex Industrial Solutions Pvt Ltd',
    severity: 'High',
    module: 'GST Statutory',
    ruleId: 'GST-CHK-02',
    ruleName: 'Missing GSTIN on Taxable Commercial Supply',
    ruleVersion: '1.1.0',
    ruleEffectiveDate: '01-Jul-2017',
    ruleJurisdiction: 'IN-ALL',
    statutoryReference: 'CGST Act 2017 Sec 31(1) / Rule 46(b)',
    ruleDescription: 'Flags commercial B2B supply transactions exceeding ₹50,000 threshold where counterparty has no registered GSTIN.',
    exceptionTitle: 'Missing GSTIN on High-Value Commercial Purchase (₹2,15,000.00)',
    whyFlagged: 'Purchase invoice PUR/25-26/112 of ₹2,15,000.00 is booked against "Shree Balaji Enterprises", exceeding the ₹50,000 B2B threshold, but the vendor master lacks a registered 15-character GSTIN.',
    voucherNumber: 'PUR/25-26/112',
    voucherDate: '18-Aug-2025',
    voucherType: 'Purchase',
    partyLedgerName: 'Shree Balaji Enterprises',
    primaryLedger: 'Packing Material Expenses',
    amount: 215000,
    narration: 'Procurement of packaging corrugated boxes in bulk for exports division',
    evidenceJson: JSON.stringify({
      VoucherNumber: 'PUR/25-26/112',
      Party: 'Shree Balaji Enterprises',
      Amount: 215000.00,
      ThresholdLimit: 50000.00,
      GSTIN: 'NOT_RECORDED'
    }, null, 2),
    gstDetails: {
      registrationType: 'Unregistered',
      placeOfSupply: '27-Maharashtra',
      isReverseCharge: false,
      hsnOrSac: '4819',
      taxRatePercent: 12.0,
      taxableAmount: 215000.00,
      cgstAmount: 0.00,
      sgstAmount: 0.00,
      igstAmount: 0.00,
      cessAmount: 0.00,
      totalTaxAmount: 0.00,
      gstLedgers: []
    },
    tdsDetails: {
      isApplicable: true,
      sectionCode: '194C',
      sectionDescription: 'TDS on Job Work & Packing Materials',
      deducteePan: 'PAN NOT FURNISHED',
      panStatus: 'Not Available',
      tdsRatePercent: 1.0,
      assessableValue: 215000.00,
      tdsAmountDeducted: 0.00,
      tdsLedgerName: 'TDS on Contractors 194C Payable'
    },
    tallyNavigationGuide: {
      masterId: '10995',
      alterId: '43720',
      guid: '9a4c8e7b-12d4-4f89-a1b2-c3d4e5f60008',
      gatewayPath: 'Gateway of Tally > Display More Reports (D) > Account Books (A) > Purchase Register (P) > August 2025 > PUR/25-26/112',
      quickGoTo: 'Press Alt+G > Type "Purchase Register" > August 2025 > PUR/25-26/112',
      exactKeys: ['Alt+G', 'Purchase Register', 'August 2025', 'Enter on PUR/25-26/112'],
      xmlQueryPayload: `<ENVELOPE>
  <HEADER>
    <TALLYREQUEST>Export Data</TALLYREQUEST>
  </HEADER>
  <BODY>
    <EXPORTDATA>
      <REQUESTDESC>
        <REPORTNAME>Voucher</REPORTNAME>
        <STATICVARIABLES>
          <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
          <VOUCHERNUMBER>PUR/25-26/112</VOUCHERNUMBER>
          <VOUCHERTYPENAME>Purchase</VOUCHERTYPENAME>
          <MASTERID>10995</MASTERID>
        </STATICVARIABLES>
      </REQUESTDESC>
    </EXPORTDATA>
  </BODY>
</ENVELOPE>`
    },
    relatedLedger: {
      ledgerName: 'Shree Balaji Enterprises',
      parentGroup: 'Sundry Creditors',
      primaryHead: 'Current Liabilities',
      openingBalance: 0,
      currentBalance: 215000,
      closingBalanceType: 'Cr'
    },
    sourceVoucher: {
      voucherId: 'V-GST-02',
      voucherNumber: 'PUR/25-26/112',
      voucherType: 'Purchase',
      voucherDate: '18-Aug-2025',
      referenceNumber: 'SBE-112',
      partyLedgerName: 'Shree Balaji Enterprises',
      totalAmount: 215000,
      narration: 'Procurement of packaging corrugated boxes in bulk for exports division',
      entries: [
        { entryId: 'g1', ledgerName: 'Packing Material Expenses', parentGroup: 'Direct Incomes', amount: 215000, isDebit: true, hsnOrSac: '4819' },
        { entryId: 'g2', ledgerName: 'Shree Balaji Enterprises', parentGroup: 'Sundry Creditors', amount: 215000, isDebit: false }
      ]
    },
    relatedTransactions: [],
    status: 'Requires Review - Pending',
    reviewerNotes: '',
    reviewHistory: [
      {
        id: 'H8',
        timestamp: '25-Sep-2026 09:15:00 AM',
        auditorName: 'Automated Audit Engine',
        action: 'Flagged for Review',
        note: 'Triggered by Rule [GST-CHK-02].'
      }
    ]
  }
];

export const allSynchronizedVouchers: SourceVoucherDetail[] = [
  {
    voucherId: 'V-1001',
    voucherNumber: 'SAL/25-26/089',
    voucherType: 'Sales',
    voucherDate: '14-May-2025',
    referenceNumber: 'PO-99120',
    partyLedgerName: 'Acme Technologies Pvt Ltd',
    partyGstin: '27AAACA9876E1Z2',
    partyPan: 'AAACA9876E',
    totalAmount: 485000,
    narration: 'Supply of industrial Automation Controllers and Sensor Units',
    entries: [
      { entryId: 'e1', ledgerName: 'Acme Technologies Pvt Ltd', parentGroup: 'Sundry Debtors', amount: 485000, isDebit: true },
      { entryId: 'e2', ledgerName: 'Sales - Industrial Equipment', parentGroup: 'Sales Accounts', amount: 411016.95, isDebit: false, hsnOrSac: '8537', taxOrTdsRate: 18 },
      { entryId: 'e3', ledgerName: 'Output CGST @ 9%', parentGroup: 'Duties & Taxes', amount: 36991.52, isDebit: false, taxOrTdsRate: 9 },
      { entryId: 'e4', ledgerName: 'Output SGST @ 9%', parentGroup: 'Duties & Taxes', amount: 36991.52, isDebit: false, taxOrTdsRate: 9 }
    ]
  },
  {
    voucherId: 'V-1002',
    voucherNumber: 'PUR/25-26/112',
    voucherType: 'Purchase',
    voucherDate: '18-Aug-2025',
    referenceNumber: 'SBE-112',
    partyLedgerName: 'Shree Balaji Enterprises',
    partyGstin: '',
    partyPan: 'AAAFB1234K',
    totalAmount: 215000,
    narration: 'Procurement of packaging corrugated boxes in bulk for exports division',
    entries: [
      { entryId: 'e5', ledgerName: 'Packing Material Expenses', parentGroup: 'Direct Expenses', amount: 215000, isDebit: true, hsnOrSac: '4819' },
      { entryId: 'e6', ledgerName: 'Shree Balaji Enterprises', parentGroup: 'Sundry Creditors', amount: 215000, isDebit: false }
    ]
  },
  {
    voucherId: 'V-1003',
    voucherNumber: 'PAY/25-26/045',
    voucherType: 'Payment',
    voucherDate: '12-Jun-2025',
    referenceNumber: 'CHQ-88201',
    partyLedgerName: 'TechServe Solutions LLP',
    partyPan: 'AABFT9981K',
    totalAmount: 180000,
    narration: 'Payment for IT infrastructure maintenance and software advisory',
    entries: [
      { entryId: 'e7', ledgerName: 'TechServe Solutions LLP', parentGroup: 'Sundry Creditors', amount: 200000, isDebit: true },
      { entryId: 'e8', ledgerName: 'HDFC Bank A/c 502000', parentGroup: 'Bank Accounts', amount: 180000, isDebit: false },
      { entryId: 'e9', ledgerName: 'TDS on Professional Fees 194J Payable', parentGroup: 'Duties & Taxes', amount: 20000, isDebit: false, taxOrTdsRate: 10 }
    ]
  },
  {
    voucherId: 'V-1004',
    voucherNumber: 'JRN/25-26/012',
    voucherType: 'Journal',
    voucherDate: '30-Sep-2025',
    referenceNumber: 'ADJ-102',
    partyLedgerName: 'Suspense Account',
    totalAmount: 500000,
    narration: 'Year-end provision adjustment for unbilled consulting fees',
    entries: [
      { entryId: 'e10', ledgerName: 'Consulting Expenses', parentGroup: 'Indirect Expenses', amount: 500000, isDebit: true },
      { entryId: 'e11', ledgerName: 'Suspense Account', parentGroup: 'Suspense Account', amount: 500000, isDebit: false }
    ]
  },
  {
    voucherId: 'V-1005',
    voucherNumber: 'RCP/25-26/088',
    voucherType: 'Receipt',
    voucherDate: '15-Jul-2025',
    referenceNumber: 'FT-99102',
    partyLedgerName: 'Acme Technologies Pvt Ltd',
    totalAmount: 300000,
    narration: 'NEFT customer collection received against Invoice SAL/25-26/089',
    entries: [
      { entryId: 'e12', ledgerName: 'HDFC Bank A/c 502000', parentGroup: 'Bank Accounts', amount: 300000, isDebit: true },
      { entryId: 'e13', ledgerName: 'Acme Technologies Pvt Ltd', parentGroup: 'Sundry Debtors', amount: 300000, isDebit: false }
    ]
  },
  {
    voucherId: 'V-1006',
    voucherNumber: 'PAY/25-26/109',
    voucherType: 'Payment',
    voucherDate: '02-Nov-2025',
    referenceNumber: 'CASH-01',
    partyLedgerName: 'Office Petty Cash',
    totalAmount: 14500,
    narration: 'Disbursement for staff refreshments, stationery and local courier charges',
    entries: [
      { entryId: 'e14', ledgerName: 'General Office Expenses', parentGroup: 'Indirect Expenses', amount: 14500, isDebit: true },
      { entryId: 'e15', ledgerName: 'Cash-in-Hand', parentGroup: 'Cash-in-Hand', amount: 14500, isDebit: false }
    ]
  }
];

export const allSynchronizedLedgers: RelatedLedgerInfo[] = [
  {
    ledgerName: 'Acme Technologies Pvt Ltd',
    parentGroup: 'Sundry Debtors',
    primaryHead: 'Current Assets',
    openingBalance: 120000,
    currentBalance: 305000,
    closingBalanceType: 'Dr'
  },
  {
    ledgerName: 'Shree Balaji Enterprises',
    parentGroup: 'Sundry Creditors',
    primaryHead: 'Current Liabilities',
    openingBalance: 0,
    currentBalance: 215000,
    closingBalanceType: 'Cr'
  },
  {
    ledgerName: 'TechServe Solutions LLP',
    parentGroup: 'Sundry Creditors',
    primaryHead: 'Current Liabilities',
    openingBalance: 50000,
    currentBalance: 70000,
    closingBalanceType: 'Cr'
  },
  {
    ledgerName: 'Output CGST @ 9%',
    parentGroup: 'Duties & Taxes',
    primaryHead: 'Current Liabilities',
    openingBalance: 12500,
    currentBalance: 148900,
    closingBalanceType: 'Cr'
  },
  {
    ledgerName: 'Output SGST @ 9%',
    parentGroup: 'Duties & Taxes',
    primaryHead: 'Current Liabilities',
    openingBalance: 12500,
    currentBalance: 148900,
    closingBalanceType: 'Cr'
  },
  {
    ledgerName: 'Input IGST @ 18%',
    parentGroup: 'Duties & Taxes',
    primaryHead: 'Current Assets',
    openingBalance: 45000,
    currentBalance: 212000,
    closingBalanceType: 'Dr'
  },
  {
    ledgerName: 'HDFC Bank A/c 502000',
    parentGroup: 'Bank Accounts',
    primaryHead: 'Bank Accounts',
    openingBalance: 850000,
    currentBalance: 1420500,
    closingBalanceType: 'Dr'
  },
  {
    ledgerName: 'Cash-in-Hand',
    parentGroup: 'Cash-in-Hand',
    primaryHead: 'Cash-in-Hand',
    openingBalance: 25000,
    currentBalance: -8500,
    closingBalanceType: 'Dr'
  },
  {
    ledgerName: 'Suspense Account',
    parentGroup: 'Suspense Account',
    primaryHead: 'Suspense Account',
    openingBalance: 0,
    currentBalance: 500000,
    closingBalanceType: 'Cr'
  },
  {
    ledgerName: 'TDS on Contractors 194C Payable',
    parentGroup: 'Duties & Taxes',
    primaryHead: 'Current Liabilities',
    openingBalance: 4200,
    currentBalance: 38500,
    closingBalanceType: 'Cr'
  },
  {
    ledgerName: 'TDS on Professional Fees 194J Payable',
    parentGroup: 'Duties & Taxes',
    primaryHead: 'Current Liabilities',
    openingBalance: 18000,
    currentBalance: 92000,
    closingBalanceType: 'Cr'
  }
];
