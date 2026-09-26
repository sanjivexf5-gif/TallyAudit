export type ExceptionSeverity = 'Critical' | 'High' | 'Medium' | 'Low';

export type ExceptionModule = 
  | 'General Accounting' 
  | 'GST Statutory' 
  | 'TDS Withholding' 
  | 'Duplicate Detection' 
  | 'Sequencing' 
  | 'Anomaly & Outlier'
  | 'Planning & Risk';

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
  primaryLedger?: string;
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

export interface ReconciliationFinding {
  id: string;
  ruleId: string;
  ruleName: string;
  category: 'Trial Balance' | 'Ledger-Voucher' | 'GST' | 'TDS' | 'Bank';
  severity: ExceptionSeverity;
  expectedAmount: number;
  actualAmount: number;
  difference: number;
  ledgerName: string;
  period: string;
  evidenceJson: string;
  whyFlagged: string;
  status: ExceptionReviewStatus;
}

export const initialReconciliationFindings: ReconciliationFinding[] = [
  {
    id: 'REC-001',
    ruleId: 'REC-TB-01',
    ruleName: 'Trial Balance vs Ledger Summation Consistency',
    category: 'Trial Balance',
    severity: 'High',
    expectedAmount: 1420500,
    actualAmount: 1418500,
    difference: 2000,
    ledgerName: 'HDFC Bank A/c 502000',
    period: 'Apr-2025 to Mar-2026',
    evidenceJson: JSON.stringify({
      TrialBalanceBalance: 1420500,
      SumOfTransactions: 1418500,
      OpeningBalance: 850000,
      TotalDebits: 1200000,
      TotalCredits: 631500
    }, null, 2),
    whyFlagged: 'The closing balance in the Trial Balance for HDFC Bank does not match the sum of opening balance and all recorded transactions. A difference of ₹2,000 exists.',
    status: 'Requires Review - Pending'
  },
  {
    id: 'REC-002',
    ruleId: 'REC-GST-01',
    ruleName: 'GSTR-3B vs Ledger Tax Liability Reconciliation',
    category: 'GST',
    severity: 'Critical',
    expectedAmount: 148900,
    actualAmount: 142500,
    difference: 6400,
    ledgerName: 'Output CGST @ 9%',
    period: 'Jun-2025',
    evidenceJson: JSON.stringify({
      LedgerBalance: 148900,
      GSTR3B_Table3_1_a: 142500,
      UnreconciledVouchers: ['SAL/25-26/044', 'SAL/25-26/045']
    }, null, 2),
    whyFlagged: 'The total tax liability recorded in Output CGST ledger (₹1,48,900) exceeds the liability reported in GSTR-3B (₹1,42,500) for June 2025.',
    status: 'Requires Review - Pending'
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

export interface AuditArea {
  id: string;
  name: string;
  isEnabled: boolean;
  riskLevel: 'Low' | 'Medium' | 'High';
  findingsCount: number;
}

export interface AuditPlan {
  id: string;
  companyId: string;
  companyName: string;
  financialPeriod: string;
  createdAt: string;
  updatedAt: string;
  status: 'Draft' | 'In Progress' | 'Review' | 'Completed';
  materialityAmount: number;
  performanceMaterialityAmount: number;
  trivialThreshold: number;
  materialityBasis: 'Revenue' | 'Profit' | 'Assets' | 'Equity' | 'Other';
  notes: string;
  selectedAreas: AuditArea[];
}

export interface AuditRisk {
  id: string;
  planId: string;
  auditArea: string;
  description: string;
  indicator: string;
  evidenceSource: string;
  likelihood: number; // 1-5
  impact: number; // 1-5
  riskLevel: 'Low' | 'Medium' | 'High';
  status: 'Open' | 'Addressed' | 'Mitigated';
  remarks: string;
}

export interface AuditProcedure {
  id: string;
  planId: string;
  auditArea: string;
  name: string;
  objective: string;
  description: string;
  status: 'Not Started' | 'In Progress' | 'Completed' | 'Not Applicable';
  startedAt?: string;
  completedAt?: string;
  result?: string;
  remarks: string;
  linkedFindingIds: string[];
  requiredEvidenceCount?: number;
  linkedEvidenceIds?: string[];
}

export interface AuditSampleItem {
  id: string;
  sampleNumber?: number;
  voucherNumber: string;
  voucherDate: string;
  amount: number;
  selectionReason: string;
  testResult: 'Pass' | 'Exception' | 'Not Tested' | 'Inconclusive' | 'Not Applicable';
  remarks: string;
  evidenceReference: string;
  linkedEvidenceId?: string;
  auditorNotes?: string;
}

export interface AuditSample {
  id: string;
  planId: string;
  auditArea: string;
  procedureId: string;
  selectionMethod: 'Random' | 'Systematic' | 'Targeted' | 'Material-Item' | 'Risk-Based';
  populationSize: number;
  sampleSize: number;
  randomSeed?: string;
  interval?: number;
  startPosition?: number;
  items: AuditSampleItem[];
}

export type AuditEvidenceType = 
  | 'Tally Transaction'
  | 'Tally Ledger'
  | 'Tally Report'
  | 'Invoice'
  | 'Purchase Document'
  | 'Sales Document'
  | 'Bank Statement'
  | 'GST Document'
  | 'TDS Document'
  | 'Agreement'
  | 'Confirmation'
  | 'Calculation'
  | 'Working Paper'
  | 'Other';

export interface AuditEvidence {
  id: string;
  planId: string;
  auditArea: string;
  procedureId?: string;
  sampleId?: string;
  sampleItemId?: string;
  findingId?: string;
  evidenceType: AuditEvidenceType;
  description: string;
  referenceNumber: string;
  source: string;
  fileName?: string;
  filePath?: string;
  fileHash?: string;
  sizeBytes?: number;
  dateReceived?: string;
  uploadedAt: string;
  reviewedAt?: string;
  status: 'Requested' | 'Received' | 'Reviewed' | 'Accepted' | 'Needs Follow-up' | 'Not Applicable';
  auditorRemarks: string;
  // Tally Source Evidence fields (Requirement 4)
  voucherId?: string;
  voucherNumber?: string;
  voucherDate?: string;
  voucherType?: string;
  ledger?: string;
  party?: string;
  amount?: number;
  taxAmount?: number;
  sourceReference?: string;
  // Integrity & Storage metadata (Requirement 6 & 7)
  fileIntegrityStatus?: 'Verified' | 'Changed' | 'Corrupted';
  storagePath?: string;
}

export interface EvidenceRequest {
  id: string;
  planId: string;
  auditArea: string;
  procedureId?: string;
  description: string;
  requestedFrom: string;
  requestedDate: string;
  dueDate: string;
  status: 'Requested' | 'Received' | 'Reviewed' | 'Closed' | 'Cancelled';
  remarks: string;
}

export interface WorkingPaper {
  id: string;
  planId: string;
  auditArea: string;
  procedureId?: string;
  findingId?: string;
  sampleId?: string;
  title: string;
  objective: string;
  procedurePerformed: string;
  population: string;
  sample: string;
  evidenceReferences: string[];
  observation: string;
  difference: number;
  auditorRemarks: string;
  conclusion: 'No Exception Noted' | 'Exception Noted' | 'Further Review Required' | 'Unable to Complete' | 'Not Applicable';
  reviewerRemarks: string;
  status: 'Draft' | 'Submitted for Review' | 'Reviewed' | 'Review Notes' | 'Finalized';
  preparedBy: string;
  preparedDate: string;
  reviewedBy?: string;
  reviewedDate?: string;
}

export interface WorkingPaperTemplate {
  id: string;
  name: string;
  auditArea: string;
  defaultTitle: string;
  objective: string;
  suggestedProcedure: string;
  populationDescription: string;
  sampleCriteria: string;
  commonObservationGuide: string;
  standardConclusion: 'No Exception Noted' | 'Exception Noted' | 'Further Review Required' | 'Unable to Complete' | 'Not Applicable';
}

export const predefinedWorkingPaperTemplates: WorkingPaperTemplate[] = [
  {
    id: 'WPT-GST',
    name: 'Statutory GST - Tax Component & Rate Verification',
    auditArea: 'GST',
    defaultTitle: 'GST Interstate vs Intrastate & ITC Reconciliation Working Paper',
    objective: 'Verify whether input tax credit is availed on valid tax invoices and whether interstate vs intrastate GST components (IGST vs CGST/SGST) are correctly mapped per POS rules.',
    suggestedProcedure: '1. Scrutinize supplier GSTIN state prefix against place of supply on voucher.\n2. Verify GSTR-2B compliance.\n3. Validate tax rates against HSN definitions.',
    populationDescription: 'All B2B purchase vouchers with GST components for the fiscal period (Population: 3,840 records)',
    sampleCriteria: 'High value material transactions > ₹1,00,000 and all interstate purchases from unregistered or mixed-state vendors.',
    commonObservationGuide: 'Check for local taxes applied on interstate supplies or missing vendor GSTINs.',
    standardConclusion: 'Exception Noted'
  },
  {
    id: 'WPT-REV',
    name: 'Revenue & Trade Receivables - Substantive Testing',
    auditArea: 'Revenue / Sales',
    defaultTitle: 'Revenue Recognition & Trade Debtor Cut-off Working Paper',
    objective: 'Substantiate accuracy, occurrence, completeness, and cut-off of sales transactions recorded during the financial year.',
    suggestedProcedure: '1. Inspect sales orders, e-way bills, and customer acknowledgement.\n2. Trace debtor opening and closing ledger balances to external confirmations.\n3. Test year-end cut-off invoices (+/- 5 days of FY close).',
    populationDescription: 'All sales invoices generated in Tally during FY 2025-26 (Population: 5,420 invoices, Total Value: ₹18.42 Cr)',
    sampleCriteria: 'All invoices exceeding Performance Materiality (₹1,87,500) + systematic sampling of 20 routine invoices.',
    commonObservationGuide: 'Verify serial sequencing, date alignment, and GST e-invoice IRN validity.',
    standardConclusion: 'No Exception Noted'
  },
  {
    id: 'WPT-PUR',
    name: 'Purchases & Trade Payables - 3-Way Matching & ITC',
    auditArea: 'Purchases',
    defaultTitle: 'Procurement Completeness & Trade Creditors Scrutiny',
    objective: 'Ascertain that goods and services received have been recorded in the appropriate accounting period and reflect legitimate company obligations.',
    suggestedProcedure: '1. Perform 3-way match: Purchase Order vs Goods Receipt Note (GRN) vs Vendor Tax Invoice.\n2. Review purchase register for unrecorded liabilities.\n3. Validate TDS deduction under section 194Q.',
    populationDescription: 'All procurement vouchers recorded under Sundry Creditors (Population: 4,110 vouchers)',
    sampleCriteria: 'Stratified sampling across major raw material suppliers and top 10 vendor accounts.',
    commonObservationGuide: 'Watch for missing purchase order numbers or discrepancies between GRN and Invoice date.',
    standardConclusion: 'No Exception Noted'
  },
  {
    id: 'WPT-BANK',
    name: 'Cash & Bank Balances - BRS & Physical Count',
    auditArea: 'Cash',
    defaultTitle: 'Bank Reconciliation & Petty Cash Imprest Verification',
    objective: 'Confirm the physical existence, ownership, and accurate cutoff of liquid cash and bank balances appearing in the balance sheet.',
    suggestedProcedure: '1. Obtain direct independent bank confirmation letters.\n2. Verify monthly Bank Reconciliation Statements (BRS).\n3. Conduct surprise physical cash verification and compare against Tally cash book.',
    populationDescription: 'All transactions posted to Bank Accounts and Cash-in-Hand ledgers across FY 2025-26',
    sampleCriteria: '100% verification of month-end BRS for all active bank accounts; sample of unpresented cheques outstanding > 90 days.',
    commonObservationGuide: 'Look for negative intraday cash balances and stale cheques not written back.',
    standardConclusion: 'No Exception Noted'
  },
  {
    id: 'WPT-TDS',
    name: 'Statutory TDS - Withholding & Deposit Compliance',
    auditArea: 'TDS',
    defaultTitle: 'TDS Applicability, Section Rates & Challan Remittance Working Paper',
    objective: 'Evaluate compliance with Chapter XVII-B of the Income Tax Act 1961 regarding deduction rates, threshold limits, and timely remittance to the government.',
    suggestedProcedure: '1. Filter payments for contractors (194C), professionals (194J), rent (194I), and goods (194Q).\n2. Test deduction rates based on PAN status.\n3. Trace TDS deducted to OLTAS deposit challans and Form 26Q returns.',
    populationDescription: 'All expense vouchers attracting withholding tax obligations (Population: 1,120 transactions)',
    sampleCriteria: 'Threshold-based selection of vendor ledger totals exceeding annual statutory limits.',
    commonObservationGuide: 'Verify if 20% higher TDS rate was applied for non-furnishing or inoperative PANs under Sec 206AA.',
    standardConclusion: 'Further Review Required'
  },
  {
    id: 'WPT-CUSTOM',
    name: 'Blank / Custom Audit Procedure Working Paper',
    auditArea: 'General',
    defaultTitle: 'General Substantive Procedure Working Paper',
    objective: 'Document specific substantive or analytical audit procedures performed by the engagement team.',
    suggestedProcedure: 'Detail the exact audit testing steps, verification source documents, and sampling strategy.',
    populationDescription: 'Targeted ledger transactions under examination',
    sampleCriteria: 'Selected based on professional auditor judgment and risk assessment.',
    commonObservationGuide: 'Record factual observations and variance calculations.',
    standardConclusion: 'No Exception Noted'
  }
];

export interface AuditActivity {
  id: string;
  planId: string;
  timestamp: string;
  user: string;
  action: string;
  details: string;
}

export interface AuditAmendment {
  id: string;
  planId: string;
  entityType: 'AuditPlan' | 'WorkingPaper' | 'Finding' | 'Evidence';
  entityId: string;
  action: 'Created' | 'Modified' | 'Deleted' | 'Reopened';
  oldValue?: string;
  newValue?: string;
  user: string;
  timestamp: string;
  reason: string;
}

export const initialAuditPlan: AuditPlan = {
  id: 'PLAN-2026-001',
  companyId: 'COMP-001',
  companyName: 'Apex Industrial Solutions Pvt Ltd',
  financialPeriod: 'FY 2025-26',
  createdAt: '2026-09-20T10:00:00Z',
  updatedAt: '2026-09-25T09:14:00Z',
  status: 'In Progress',
  materialityAmount: 250000,
  performanceMaterialityAmount: 187500,
  trivialThreshold: 12500,
  materialityBasis: 'Revenue',
  notes: 'Audit focused on GST compliance and revenue recognition for the FY.',
  selectedAreas: [
    { id: 'AREA-001', name: 'Revenue / Sales', isEnabled: true, riskLevel: 'Medium', findingsCount: 3 },
    { id: 'AREA-002', name: 'Purchases', isEnabled: true, riskLevel: 'Low', findingsCount: 2 },
    { id: 'AREA-003', name: 'GST', isEnabled: true, riskLevel: 'High', findingsCount: 8 },
    { id: 'AREA-004', name: 'TDS', isEnabled: true, riskLevel: 'Medium', findingsCount: 4 },
    { id: 'AREA-005', name: 'Cash', isEnabled: true, riskLevel: 'Medium', findingsCount: 1 },
    { id: 'AREA-006', name: 'Journal Entries', isEnabled: true, riskLevel: 'High', findingsCount: 2 }
  ]
};

export const initialAuditRisks: AuditRisk[] = [
  {
    id: 'RISK-001',
    planId: 'PLAN-2026-001',
    auditArea: 'GST',
    description: 'Incorrect tax rate application or interstate vs intrastate mismatch.',
    indicator: 'GST reconciliation differences and rule violations detected.',
    evidenceSource: 'GSTR-3B vs Ledger Comparison',
    likelihood: 4,
    impact: 5,
    riskLevel: 'High',
    status: 'Open',
    remarks: 'Requires detailed verification of interstate purchase vouchers.'
  },
  {
    id: 'RISK-002',
    planId: 'PLAN-2026-001',
    auditArea: 'Cash',
    description: 'Potential physical cash deficit or unrecorded disbursements.',
    indicator: 'Intraday negative cash balance finding.',
    evidenceSource: 'Petty Cash Ledger',
    likelihood: 2,
    impact: 4,
    riskLevel: 'Medium',
    status: 'Open',
    remarks: 'Verify replenishment timing vs disbursement.'
  }
];

export const initialAuditProcedures: AuditProcedure[] = [
  {
    id: 'PROC-001',
    planId: 'PLAN-2026-001',
    auditArea: 'GST',
    name: 'Verify Interstate vs Intrastate Allocation',
    objective: 'Ensure correct GST component application based on Place of Supply.',
    description: 'Select samples of purchase vouchers with IGST/CGST/SGST and verify against supplier GSTIN state codes.',
    status: 'In Progress',
    remarks: 'Focusing on supplier Tata Steel Gujarat plant mismatch.',
    linkedFindingIds: ['EXC-2026-001'],
    requiredEvidenceCount: 2,
    linkedEvidenceIds: ['EVD-001', 'EVD-TALLY-001']
  },
  {
    id: 'PROC-002',
    planId: 'PLAN-2026-001',
    auditArea: 'Revenue / Sales',
    name: 'Test Year-End Cut-off on Sales Invoices',
    objective: 'Ascertain that sales are recorded in the proper financial year.',
    description: 'Examine sales invoices 5 days before and after fiscal year-end, tracing to dispatch notes and e-Way bills.',
    status: 'Completed',
    remarks: 'Cut-off testing complete; all invoices tested were recognized in correct period.',
    linkedFindingIds: [],
    requiredEvidenceCount: 1,
    linkedEvidenceIds: ['EVD-004']
  },
  {
    id: 'PROC-003',
    planId: 'PLAN-2026-001',
    auditArea: 'Purchases',
    name: '3-Way Match Verification of Major Capital Goods',
    objective: 'Confirm valid receipt of goods, vendor invoice amount, and PO authorization.',
    description: 'Match purchase invoice, GRN, and PO for transactions exceeding ₹1,00,000.',
    status: 'In Progress',
    remarks: 'Awaiting delivery challan for 2 purchase vouchers.',
    linkedFindingIds: ['EXC-2026-002'],
    requiredEvidenceCount: 2,
    linkedEvidenceIds: ['EVD-001', 'EVD-TALLY-001']
  },
  {
    id: 'PROC-004',
    planId: 'PLAN-2026-001',
    auditArea: 'Cash',
    name: 'Monthly Bank Reconciliation Statement Audit',
    objective: 'Ensure all reconciling items between Tally cash/bank book and bank statements are valid and timely cleared.',
    description: 'Examine bank confirmation and monthly BRS for all operational bank accounts.',
    status: 'Completed',
    remarks: 'HDFC & SBI statements verified against closing ledger balances.',
    linkedFindingIds: [],
    requiredEvidenceCount: 1,
    linkedEvidenceIds: ['EVD-003']
  }
];

export const initialAuditEvidence: AuditEvidence[] = [
  {
    id: 'EVD-001',
    planId: 'PLAN-2026-001',
    auditArea: 'GST',
    procedureId: 'PROC-001',
    findingId: 'EXC-2026-001',
    evidenceType: 'Invoice',
    description: 'Tata Steel Gujarat Plant Purchase Invoice (Scanned Copy)',
    referenceNumber: 'TSL-GJ-8891',
    source: 'External PDF Upload',
    fileName: 'INV_TSL_GJ_8891.pdf',
    filePath: 'AuditData/Apex Industrial Solutions Pvt Ltd/FY 2025-26/PLAN-2026-001/Evidence/INV_TSL_GJ_8891.pdf',
    fileHash: 'sha256:7f83b1657ff1fc53b92dc18148a1d65dfc2d4b1fa3d677284addd200126d9069',
    fileIntegrityStatus: 'Verified',
    storagePath: 'AuditData/Apex Industrial Solutions Pvt Ltd/FY 2025-26/PLAN-2026-001/Evidence/INV_TSL_GJ_8891.pdf',
    sizeBytes: 425600,
    dateReceived: '2026-09-25',
    uploadedAt: '2026-09-25T11:00:00Z',
    reviewedAt: '2026-09-25T14:30:00Z',
    status: 'Accepted',
    auditorRemarks: 'Verified against Tally voucher PUR-05. Confirms supplier GSTIN starts with state code 24 (Gujarat).'
  },
  {
    id: 'EVD-TALLY-001',
    planId: 'PLAN-2026-001',
    auditArea: 'GST',
    procedureId: 'PROC-001',
    findingId: 'EXC-2026-001',
    evidenceType: 'Tally Transaction',
    description: 'Tally Synchronized Voucher PUR-05 (Tata Steel Ltd)',
    referenceNumber: 'PUR-05',
    source: 'Tally ERP/Prime Sync (Source Evidence)',
    voucherId: 'V5',
    voucherNumber: 'PUR-05',
    voucherDate: '05-Jun-2025',
    voucherType: 'Purchase',
    ledger: 'Raw Material Purchases - Steel',
    party: 'Tata Steel Ltd (Gujarat Plant)',
    amount: 147500,
    taxAmount: 22500,
    sourceReference: 'Gateway of Tally > Account Books > Purchase Register > PUR-05',
    fileIntegrityStatus: 'Verified',
    uploadedAt: '2026-09-25T09:14:00Z',
    status: 'Accepted',
    auditorRemarks: 'Tagged directly as Source Evidence from local SQLite database during voucher review.'
  },
  {
    id: 'EVD-003',
    planId: 'PLAN-2026-001',
    auditArea: 'Cash',
    procedureId: 'PROC-004',
    evidenceType: 'Bank Statement',
    description: 'HDFC Bank Ltd Current Account E-Statement (Month ended March 2026)',
    referenceNumber: 'HDFC-CA-00928371',
    source: 'NetBanking Download',
    fileName: 'HDFC_Bank_Stmt_March2026.pdf',
    filePath: 'AuditData/Apex Industrial Solutions Pvt Ltd/FY 2025-26/PLAN-2026-001/Evidence/HDFC_Bank_Stmt_March2026.pdf',
    fileHash: 'sha256:d54128f72f073d8a149171b3e819b7875b22b07e78d91a9f384d5dfd6efdf81e',
    fileIntegrityStatus: 'Verified',
    storagePath: 'AuditData/Apex Industrial Solutions Pvt Ltd/FY 2025-26/PLAN-2026-001/Evidence/HDFC_Bank_Stmt_March2026.pdf',
    sizeBytes: 1248000,
    dateReceived: '2026-09-24',
    uploadedAt: '2026-09-24T16:20:00Z',
    reviewedAt: '2026-09-25T10:15:00Z',
    status: 'Accepted',
    auditorRemarks: 'Closing balance of ₹14,25,800 matches Tally BRS after adjusting unpresented cheque #402918.'
  },
  {
    id: 'EVD-004',
    planId: 'PLAN-2026-001',
    auditArea: 'Revenue / Sales',
    procedureId: 'PROC-002',
    evidenceType: 'GST Document',
    description: 'GSTR-2B Auto-Drafted ITC Statement from GST Portal (Excel Export)',
    referenceNumber: 'GSTR2B-NOV2025-27AAACT2727Q',
    source: 'GST Portal API Export',
    fileName: 'GSTR2B_Nov2025_Portal.xlsx',
    filePath: 'AuditData/Apex Industrial Solutions Pvt Ltd/FY 2025-26/PLAN-2026-001/Evidence/GSTR2B_Nov2025_Portal.xlsx',
    fileHash: 'sha256:a1b2c3d4e5f60718293a4b5c6d7e8f90123456789abcdef0123456789abcdef0',
    fileIntegrityStatus: 'Verified',
    storagePath: 'AuditData/Apex Industrial Solutions Pvt Ltd/FY 2025-26/PLAN-2026-001/Evidence/GSTR2B_Nov2025_Portal.xlsx',
    sizeBytes: 892000,
    dateReceived: '2026-09-25',
    uploadedAt: '2026-09-25T13:45:00Z',
    status: 'Reviewed',
    auditorRemarks: 'Used for reconciliation with purchase register ITC.'
  },
  {
    id: 'EVD-005',
    planId: 'PLAN-2026-001',
    auditArea: 'General',
    evidenceType: 'Agreement',
    description: 'Commercial Office & Warehouse Lease Agreement 2025-2028',
    referenceNumber: 'AGR-LEASE-2025-01',
    source: 'Legal Department',
    fileName: 'Industrial_Premises_Lease_Deed.pdf',
    filePath: 'AuditData/Apex Industrial Solutions Pvt Ltd/FY 2025-26/PLAN-2026-001/Evidence/Industrial_Premises_Lease_Deed.pdf',
    fileHash: 'sha256:e4d909c290d0fb1ca068ffaddf22cbd0add8abdf1a0e72650777ac85c7023f2f',
    fileIntegrityStatus: 'Verified',
    storagePath: 'AuditData/Apex Industrial Solutions Pvt Ltd/FY 2025-26/PLAN-2026-001/Evidence/Industrial_Premises_Lease_Deed.pdf',
    sizeBytes: 2840000,
    dateReceived: '2026-09-21',
    uploadedAt: '2026-09-21T10:00:00Z',
    status: 'Accepted',
    auditorRemarks: 'Monthly rent ₹85,000 + GST. Validates rent expense vouchers and TDS deduction under Sec 194I.'
  }
];

export const initialEvidenceRequests: EvidenceRequest[] = [
  {
    id: 'REQ-001',
    planId: 'PLAN-2026-001',
    auditArea: 'GST',
    procedureId: 'PROC-001',
    description: 'Missing RCM supporting transport consignment notes (LR copies) for July 2025',
    requestedFrom: 'Accounts Payable Team (Attn: Mr. Suresh K.)',
    requestedDate: '2026-09-22',
    dueDate: '2026-09-28',
    status: 'Received',
    remarks: 'Scanned LR copies received via secure internal transfer on 25-Sep-2026.'
  },
  {
    id: 'REQ-002',
    planId: 'PLAN-2026-001',
    auditArea: 'Cash',
    procedureId: 'PROC-004',
    description: 'Standard Bank Confirmation Certificate as on 31st March 2026 for SBI Credit Facility',
    requestedFrom: 'State Bank of India (Industrial Finance Branch)',
    requestedDate: '2026-09-23',
    dueDate: '2026-10-05',
    status: 'Requested',
    remarks: 'Formal confirmation letter dispatched under auditor cover letter.'
  },
  {
    id: 'REQ-003',
    planId: 'PLAN-2026-001',
    auditArea: 'TDS',
    description: 'Lower TDS Deduction Certificate under Section 197 for TechLogix Systems',
    requestedFrom: 'Vendor Finance Controller',
    requestedDate: '2026-09-20',
    dueDate: '2026-09-24',
    status: 'Requested',
    remarks: 'Due date passed; reminder sent to client accounts manager.'
  }
];

export const initialWorkingPapers: WorkingPaper[] = [
  {
    id: 'WP-001',
    planId: 'PLAN-2026-001',
    auditArea: 'GST',
    procedureId: 'PROC-001',
    findingId: 'EXC-2026-001',
    title: 'Verification of Interstate Purchases & Place of Supply (POS)',
    objective: 'Confirm correct IGST application for Gujarat vendors and identify misclassified local tax postings.',
    procedurePerformed: 'Selected 5 high-value purchase vouchers from Gujarat suppliers and verified against supplier GSTIN prefix 24.',
    population: 'All interstate purchase vouchers for FY 2025-26 (3,840 records)',
    sample: '5 Vouchers (₹12,45,000 total)',
    evidenceReferences: ['EVD-001', 'EVD-TALLY-001'],
    observation: 'Voucher PUR-05 (Tata Steel Gujarat) was erroneously recorded with local CGST 9% + SGST 9% instead of IGST 18%.',
    difference: 22500,
    auditorRemarks: 'Tax discrepancy of ₹22,500 identified. Supplier has filed invoice under B2B with POS 27. Rectification entry required in Tally.',
    conclusion: 'Exception Noted',
    reviewerRemarks: 'Agreed with finding. Management has agreed to pass reversal entry and adjust in subsequent GSTR-3B return.',
    status: 'Finalized',
    preparedBy: 'CA. Sanjiv (Senior Auditor)',
    preparedDate: '2026-09-25',
    reviewedBy: 'Partner (Audit Head)',
    reviewedDate: '2026-09-25'
  },
  {
    id: 'WP-002',
    planId: 'PLAN-2026-001',
    auditArea: 'Revenue / Sales',
    procedureId: 'PROC-002',
    title: 'Revenue Recognition & Year-End Sales Cut-off Substantive Testing',
    objective: 'Confirm that sales invoiced in March 2026 reflect goods physically dispatched prior to midnight 31st March 2026.',
    procedurePerformed: 'Selected last 10 invoices of FY 2025-26 and first 10 invoices of FY 2026-27. Traced each to e-Way bill generate time and transporter delivery receipt.',
    population: 'All sales vouchers recorded in Q4 FY 2025-26 (1,450 invoices)',
    sample: '20 Invoices spanning cut-off window (₹38,90,000 total)',
    evidenceReferences: ['EVD-004'],
    observation: 'All sampled sales invoices matched dispatch dates and delivery log. No premature revenue recognition detected.',
    difference: 0,
    auditorRemarks: 'Cut-off assertion verified without exception.',
    conclusion: 'No Exception Noted',
    reviewerRemarks: 'Sampling methodology is appropriate. Satisfied with audit evidence obtained.',
    status: 'Finalized',
    preparedBy: 'Associate Auditor',
    preparedDate: '2026-09-24',
    reviewedBy: 'CA. Sanjiv (Senior Auditor)',
    reviewedDate: '2026-09-25'
  },
  {
    id: 'WP-003',
    planId: 'PLAN-2026-001',
    auditArea: 'Cash',
    procedureId: 'PROC-004',
    title: 'Bank Reconciliation & Petty Cash Surprise Verification',
    objective: 'Ascertain accurate cutoff of liquid bank balances and absence of negative intraday cash balances.',
    procedurePerformed: '1. Scrutinized month-end BRS for HDFC and SBI accounts. 2. Performed surprise physical cash count at factory cash chest.',
    population: 'All 12 monthly BRS sheets and daily petty cash ledger',
    sample: '100% of bank accounts + physical cash count on 24-Sep-2026',
    evidenceReferences: ['EVD-003'],
    observation: 'Physical cash tallied with Tally cash book within ₹12 rounding difference. No unrecorded cash disbursements.',
    difference: 12,
    auditorRemarks: 'Reconciling items on HDFC account are regular clearing items cleared by 5th April.',
    conclusion: 'No Exception Noted',
    reviewerRemarks: 'Physical cash count memo signed by custodian inspected and approved.',
    status: 'Reviewed',
    preparedBy: 'Associate Auditor',
    preparedDate: '2026-09-24',
    reviewedBy: 'CA. Sanjiv (Senior Auditor)',
    reviewedDate: '2026-09-25'
  }
];

export const initialAuditActivities: AuditActivity[] = [
  {
    id: 'ACT-001',
    planId: 'PLAN-2026-001',
    timestamp: '2026-09-25T09:15:00Z',
    user: 'CA. Sanjiv (Senior Auditor)',
    action: 'Audit Run Completed',
    details: 'Automated audit rules executed across 14,280 vouchers. Identified 24 potential exceptions.'
  },
  {
    id: 'ACT-002',
    planId: 'PLAN-2026-001',
    timestamp: '2026-09-25T11:05:00Z',
    user: 'CA. Sanjiv (Senior Auditor)',
    action: 'Evidence Registered',
    details: 'Uploaded supplier invoice INV_TSL_GJ_8891.pdf and linked to finding EXC-2026-001.'
  },
  {
    id: 'ACT-003',
    planId: 'PLAN-2026-001',
    timestamp: '2026-09-25T14:30:00Z',
    user: 'CA. Sanjiv (Senior Auditor)',
    action: 'Working Paper Signed Off',
    details: 'Working paper WP-001 (Interstate Purchases) marked as Finalized.'
  }
];
