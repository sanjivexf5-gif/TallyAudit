import {
  WorkspaceExceptionItem,
  AuditPlan,
  AuditRisk,
  AuditProcedure,
  AuditSample,
  AuditEvidence,
  EvidenceRequest,
  WorkingPaper,
  AuditActivity,
  AuditAmendment,
  RelatedLedgerInfo,
  SourceVoucherDetail,
  ReconciliationFinding,
  initialWorkspaceExceptions,
  initialAuditPlan,
  initialAuditRisks,
  initialAuditProcedures,
  initialAuditEvidence,
  initialEvidenceRequests,
  initialWorkingPapers,
  initialAuditActivities,
  initialReconciliationFindings,
  allSynchronizedLedgers,
  allSynchronizedVouchers
} from './workspaceData';

export interface FinancialPeriodInfo {
  id: string; // e.g., 'FY-2025-26'
  label: string; // e.g., 'FY 2025-26'
  startDate: string; // '01-Apr-2025'
  endDate: string; // '31-Mar-2026'
  assessmentYear: string; // 'AY 2026-27'
  isCurrent: boolean;
  isAuditFinalized: boolean;
  finalizedAt?: string;
  finalizedBy?: string;
  auditOpinion?: 'Unmodified' | 'Qualified' | 'Adverse' | 'Disclaimer';
  vouchersCount: number;
  ledgersCount: number;
}

export interface CompanyWorkspace {
  id: string; // e.g., 'COMP-001'
  name: string;
  legalName: string;
  tallyCompanyIdentifier: string; // Tally GUID or Internal ID
  tallyNumber: string; // e.g., '10001'
  pan: string;
  gstin: string;
  cin?: string;
  state: string;
  stateCode: string;
  industry: string;
  natureOfBusiness: string;
  registeredAddress: string;
  auditPartner: string;
  booksBeginningFrom: string;
  financialYears: FinancialPeriodInfo[];
  activeFinancialYearId: string;
  currency: string;
  lastSyncAt: string;
  status: 'Active' | 'Archived' | 'Suspended';
  auditStatus: 'Planning' | 'In Progress' | 'Review Completed' | 'Audit Finalized & Locked';
}

export interface YoYFinancialMetric {
  category: 'Revenue' | 'Purchases' | 'Gross Margin' | 'Operating Expenses' | 'Net Profit' | 'Receivables' | 'Payables' | 'Cash & Bank' | 'Duties & Taxes';
  metricName: string;
  accountHead: string;
  currentYearAmount: number;
  priorYearAmount: number;
  varianceAmount: number;
  variancePercent: number; // Safe against zero-division
  trend: 'Up' | 'Down' | 'Neutral';
  isAlert: boolean; // Flagged if variance > threshold (e.g., > 20% or > ₹5,00,000)
  auditorAnalyticalNotes?: string;
}

export interface YoYVolumeMetric {
  voucherType: string;
  currentYearCount: number;
  priorYearCount: number;
  varianceCount: number;
  variancePercent: number;
  currentTotalAmount: number;
  priorTotalAmount: number;
  avgAmountCurrent: number;
  avgAmountPrior: number;
  commentary: string;
}

export interface RecurringFindingItem {
  id: string;
  ruleId: string;
  ruleName: string;
  module: string;
  severity: 'Critical' | 'High' | 'Medium' | 'Low';
  partyOrLedger: string;
  description: string;
  occurrenceCount: number; // e.g., 2 consecutive years
  priorYearStatus: string;
  currentYearStatus: string;
  auditorActionRequired: string;
}

export interface YoYFindingComparison {
  module: string;
  currentYearFindings: number;
  priorYearFindings: number;
  variance: number;
  criticalCountCurrent: number;
  criticalCountPrior: number;
  resolvedFromPriorYear: number;
  newInCurrentYear: number;
  recurringCount: number;
}

export interface CompanyYearDataStore {
  companyId: string;
  financialYearId: string;
  companyName: string;
  financialPeriod: string;
  exceptions: WorkspaceExceptionItem[];
  auditPlan: AuditPlan;
  auditRisks: AuditRisk[];
  auditProcedures: AuditProcedure[];
  auditSamples: AuditSample[];
  auditEvidence: AuditEvidence[];
  evidenceRequests: EvidenceRequest[];
  workingPapers: WorkingPaper[];
  auditActivities: AuditActivity[];
  auditAmendments: AuditAmendment[];
  reconciliationFindings: ReconciliationFinding[];
  ledgers: RelatedLedgerInfo[];
  vouchers: SourceVoucherDetail[];
  financialMetrics: YoYFinancialMetric[];
  volumeMetrics: YoYVolumeMetric[];
  findingComparisons: YoYFindingComparison[];
  recurringFindings: RecurringFindingItem[];
  analyticalReviewNotes: string;
}

// -------------------------------------------------------------
// SEED DATA: 4 REALISTIC COMPANIES
// -------------------------------------------------------------

export const initialCompanies: CompanyWorkspace[] = [
  {
    id: 'COMP-001',
    name: 'Apex Industrial Solutions Pvt Ltd',
    legalName: 'Apex Industrial Solutions Private Limited',
    tallyCompanyIdentifier: 'TALLY-9a4c8e7b-10001',
    tallyNumber: '10001',
    pan: 'AAACT2727Q',
    gstin: '27AAACT2727Q1ZW',
    cin: 'U28112MH2015PTC268491',
    state: 'Maharashtra',
    stateCode: '27',
    industry: 'Industrial Heavy Manufacturing & Engineering',
    natureOfBusiness: 'Manufacturing, Assembly & Distribution of Heavy Industrial Machinery & Spares',
    registeredAddress: 'Plot 42, TTC Industrial Area, MIDC Mahape, Navi Mumbai, Maharashtra 400710',
    auditPartner: 'CA. Sanjiv (Senior Partner, M/s Sanjiv & Associates, Chartered Accountants)',
    booksBeginningFrom: '01-Apr-2023',
    activeFinancialYearId: 'FY-2025-26',
    currency: 'INR (₹)',
    lastSyncAt: '25-Sep-2026 09:14:00 AM',
    status: 'Active',
    auditStatus: 'In Progress',
    financialYears: [
      {
        id: 'FY-2024-25',
        label: 'FY 2024-25',
        startDate: '01-Apr-2024',
        endDate: '31-Mar-2025',
        assessmentYear: 'AY 2025-26',
        isCurrent: false,
        isAuditFinalized: true,
        finalizedAt: '2025-09-28T16:00:00Z',
        finalizedBy: 'CA. Sanjiv (Lead Partner)',
        auditOpinion: 'Unmodified',
        vouchersCount: 13060,
        ledgersCount: 312
      },
      {
        id: 'FY-2025-26',
        label: 'FY 2025-26',
        startDate: '01-Apr-2025',
        endDate: '31-Mar-2026',
        assessmentYear: 'AY 2026-27',
        isCurrent: true,
        isAuditFinalized: false,
        vouchersCount: 15400,
        ledgersCount: 342
      },
      {
        id: 'FY-2026-27',
        label: 'FY 2026-27',
        startDate: '01-Apr-2026',
        endDate: '31-Mar-2027',
        assessmentYear: 'AY 2027-28',
        isCurrent: false,
        isAuditFinalized: false,
        vouchersCount: 2150,
        ledgersCount: 342
      }
    ]
  },
  {
    id: 'COMP-002',
    name: 'Bharat Logistics & Infra Corp Ltd',
    legalName: 'Bharat Logistics & Infrastructure Corporation Limited',
    tallyCompanyIdentifier: 'TALLY-b72e1189-10002',
    tallyNumber: '10002',
    pan: 'AABCB4418P',
    gstin: '07AABCB4418P1ZF',
    cin: 'L63090DL2012PLC231940',
    state: 'Delhi',
    stateCode: '07',
    industry: 'Supply Chain, Freight Transportation & Warehousing',
    natureOfBusiness: 'Interstate Multi-Modal Goods Transport, 3PL Warehousing & Logistics Fleet Management',
    registeredAddress: 'Level 5, Express Trade Towers, Sector 132, Expressway, New Delhi 110001',
    auditPartner: 'CA. Meenakshi Sundaram (Partner, Infrastructure Audit Practice)',
    booksBeginningFrom: '01-Apr-2024',
    activeFinancialYearId: 'FY-2025-26',
    currency: 'INR (₹)',
    lastSyncAt: '24-Sep-2026 18:30:00 PM',
    status: 'Active',
    auditStatus: 'Planning',
    financialYears: [
      {
        id: 'FY-2024-25',
        label: 'FY 2024-25',
        startDate: '01-Apr-2024',
        endDate: '31-Mar-2025',
        assessmentYear: 'AY 2025-26',
        isCurrent: false,
        isAuditFinalized: true,
        finalizedAt: '2025-09-15T11:20:00Z',
        finalizedBy: 'CA. Meenakshi Sundaram',
        auditOpinion: 'Unmodified',
        vouchersCount: 18450,
        ledgersCount: 420
      },
      {
        id: 'FY-2025-26',
        label: 'FY 2025-26',
        startDate: '01-Apr-2025',
        endDate: '31-Mar-2026',
        assessmentYear: 'AY 2026-27',
        isCurrent: true,
        isAuditFinalized: false,
        vouchersCount: 22100,
        ledgersCount: 465
      }
    ]
  },
  {
    id: 'COMP-003',
    name: 'Zenith Precision Engineering Pvt Ltd',
    legalName: 'Zenith Precision Engineering Private Limited',
    tallyCompanyIdentifier: 'TALLY-c38d9904-10003',
    tallyNumber: '10003',
    pan: 'AACCZ9912K',
    gstin: '24AACCZ9912K1ZT',
    cin: 'U29253GJ2018PTC104523',
    state: 'Gujarat',
    stateCode: '24',
    industry: 'Automotive Components & Precision CNC Machining',
    natureOfBusiness: 'OEM Component Turning, Heat Treatment & Aerospace Sub-Assemblies',
    registeredAddress: 'GIDC Industrial Estate, Phase III, Vatva, Ahmedabad, Gujarat 382445',
    auditPartner: 'CA. Sanjiv (Senior Partner, M/s Sanjiv & Associates)',
    booksBeginningFrom: '01-Apr-2024',
    activeFinancialYearId: 'FY-2025-26',
    currency: 'INR (₹)',
    lastSyncAt: '22-Sep-2026 14:10:00 PM',
    status: 'Active',
    auditStatus: 'In Progress',
    financialYears: [
      {
        id: 'FY-2024-25',
        label: 'FY 2024-25',
        startDate: '01-Apr-2024',
        endDate: '31-Mar-2025',
        assessmentYear: 'AY 2025-26',
        isCurrent: false,
        isAuditFinalized: true,
        finalizedAt: '2025-08-30T17:45:00Z',
        finalizedBy: 'CA. Sanjiv',
        auditOpinion: 'Unmodified',
        vouchersCount: 9800,
        ledgersCount: 260
      },
      {
        id: 'FY-2025-26',
        label: 'FY 2025-26',
        startDate: '01-Apr-2025',
        endDate: '31-Mar-2026',
        assessmentYear: 'AY 2026-27',
        isCurrent: true,
        isAuditFinalized: false,
        vouchersCount: 11450,
        ledgersCount: 295
      }
    ]
  },
  {
    id: 'COMP-004',
    name: 'Heritage Textiles & Handlooms LLP',
    legalName: 'Heritage Textiles & Handlooms Limited Liability Partnership',
    tallyCompanyIdentifier: 'TALLY-f10a5542-10004',
    tallyNumber: '10004',
    pan: 'AAHFH6721M',
    gstin: '33AAHFH6721M1ZX',
    cin: 'AAB-1982',
    state: 'Tamil Nadu',
    stateCode: '33',
    industry: 'Textile Weaving, Processing & Garment Export',
    natureOfBusiness: 'Organic Cotton Fabric Spinning, Dyeing & High-End Garment Exports to EU/US',
    registeredAddress: '18/4, Tirupur Textile Hub, Avinashi Road, Tirupur, Tamil Nadu 641603',
    auditPartner: 'CA. K. Ramanathan (Senior Partner)',
    booksBeginningFrom: '01-Apr-2024',
    activeFinancialYearId: 'FY-2025-26',
    currency: 'INR (₹)',
    lastSyncAt: '20-Sep-2026 10:15:00 AM',
    status: 'Active',
    auditStatus: 'Review Completed',
    financialYears: [
      {
        id: 'FY-2024-25',
        label: 'FY 2024-25',
        startDate: '01-Apr-2024',
        endDate: '31-Mar-2025',
        assessmentYear: 'AY 2025-26',
        isCurrent: false,
        isAuditFinalized: true,
        finalizedAt: '2025-09-02T12:00:00Z',
        finalizedBy: 'CA. K. Ramanathan',
        auditOpinion: 'Unmodified',
        vouchersCount: 8120,
        ledgersCount: 210
      },
      {
        id: 'FY-2025-26',
        label: 'FY 2025-26',
        startDate: '01-Apr-2025',
        endDate: '31-Mar-2026',
        assessmentYear: 'AY 2026-27',
        isCurrent: true,
        isAuditFinalized: false,
        vouchersCount: 9400,
        ledgersCount: 235
      }
    ]
  }
];

// Helper to safely calculate variance and percentage
export function calculateVariance(current: number, prior: number): { diff: number; pct: number } {
  const diff = current - prior;
  let pct = 0;
  if (prior !== 0) {
    pct = Number(((diff / Math.abs(prior)) * 100).toFixed(2));
  } else if (current !== 0) {
    pct = 100;
  }
  return { diff, pct };
}

// -------------------------------------------------------------
// YEAR-OVER-YEAR FINANCIAL METRICS (Apex Industrial Solutions)
// -------------------------------------------------------------

export const initialApexYoYMetrics: YoYFinancialMetric[] = [
  {
    category: 'Revenue',
    metricName: 'Revenue from Operations',
    accountHead: 'Sales Accounts / Direct Incomes',
    currentYearAmount: 184250000, // ₹18.42 Cr
    priorYearAmount: 151000000,   // ₹15.10 Cr
    varianceAmount: 33250000,
    variancePercent: 22.02,
    trend: 'Up',
    isAlert: true,
    auditorAnalyticalNotes: 'Revenue grew 22.02% driven by automotive OEM line expansion in Q2 & Q3. Substantiated by increase in dispatch volume and e-way bill generation.'
  },
  {
    category: 'Purchases',
    metricName: 'Cost of Raw Materials Consumed',
    accountHead: 'Raw Material Purchases - Steel & Alloys',
    currentYearAmount: 112400000, // ₹11.24 Cr
    priorYearAmount: 94500000,    // ₹9.45 Cr
    varianceAmount: 17900000,
    variancePercent: 18.94,
    trend: 'Up',
    isAlert: false,
    auditorAnalyticalNotes: 'Direct material procurement grew in tandem with production volume (18.94% increase vs 22.02% revenue growth, reflecting improved scrap realization).'
  },
  {
    category: 'Gross Margin',
    metricName: 'Gross Profit (Trading Margin)',
    accountHead: 'Trading Account Gross Margin',
    currentYearAmount: 71850000,  // ₹7.18 Cr (39.0%)
    priorYearAmount: 56500000,    // ₹5.65 Cr (37.4%)
    varianceAmount: 15350000,
    variancePercent: 27.17,
    trend: 'Up',
    isAlert: false,
    auditorAnalyticalNotes: 'Gross margin expanded from 37.42% to 38.99% (+157 bps) due to bulk procurement discounts negotiated with Tata Steel Ltd and SAIL.'
  },
  {
    category: 'Operating Expenses',
    metricName: 'Manufacturing & Administrative Overheads',
    accountHead: 'Indirect Expenses Head',
    currentYearAmount: 38520000,  // ₹3.85 Cr
    priorYearAmount: 31200000,    // ₹3.12 Cr
    varianceAmount: 7320000,
    variancePercent: 23.46,
    trend: 'Up',
    isAlert: true,
    auditorAnalyticalNotes: 'High variance flagged: Electricity & fuel costs increased 31% following night-shift operations. Verified against Maharashtra State Electricity Distribution bills.'
  },
  {
    category: 'Net Profit',
    metricName: 'Operating EBITDA / Net Profit Before Tax',
    accountHead: 'Profit & Loss Account',
    currentYearAmount: 33330000,  // ₹3.33 Cr
    priorYearAmount: 25300000,    // ₹2.53 Cr
    varianceAmount: 8030000,
    variancePercent: 31.74,
    trend: 'Up',
    isAlert: true,
    auditorAnalyticalNotes: 'PBT improved significantly (+31.74%). Operating leverage delivered higher bottom-line conversion.'
  },
  {
    category: 'Receivables',
    metricName: 'Trade Receivables (Sundry Debtors)',
    accountHead: 'Current Assets / Sundry Debtors',
    currentYearAmount: 34580000,  // ₹3.45 Cr
    priorYearAmount: 27800000,    // ₹2.78 Cr
    varianceAmount: 6780000,
    variancePercent: 24.39,
    trend: 'Up',
    isAlert: true,
    auditorAnalyticalNotes: 'Days Sales Outstanding (DSO) increased from 67 days to 69 days. Debtors aging analysis indicates ₹42 Lakhs outstanding > 180 days; provision adequacy evaluated.'
  },
  {
    category: 'Payables',
    metricName: 'Trade Payables (Sundry Creditors)',
    accountHead: 'Current Liabilities / Sundry Creditors',
    currentYearAmount: 21540000,  // ₹2.15 Cr
    priorYearAmount: 19200000,    // ₹1.92 Cr
    varianceAmount: 2340000,
    variancePercent: 12.19,
    trend: 'Up',
    isAlert: false,
    auditorAnalyticalNotes: 'Trade creditors increased 12.19% (DPO ~70 days). Compliant with MSMED Act 45-day payment statutory requirement.'
  },
  {
    category: 'Cash & Bank',
    metricName: 'Liquid Cash & Bank Balances',
    accountHead: 'Bank Accounts & Cash-in-Hand',
    currentYearAmount: 14205000,  // ₹1.42 Cr
    priorYearAmount: 8750000,     // ₹0.87 Cr
    varianceAmount: 5455000,
    variancePercent: 62.34,
    trend: 'Up',
    isAlert: true,
    auditorAnalyticalNotes: 'Bank balance expanded 62.34% due to customer advances for machinery dispatch in April 2026. Reconciled with HDFC & SBI bank confirmations.'
  },
  {
    category: 'Duties & Taxes',
    metricName: 'Net Statutory Duty Liability (GST + TDS)',
    accountHead: 'Duties & Taxes Account Head',
    currentYearAmount: 4890000,   // ₹48.9 Lakhs
    priorYearAmount: 3820000,     // ₹38.2 Lakhs
    varianceAmount: 1070000,
    variancePercent: 28.01,
    trend: 'Up',
    isAlert: false,
    auditorAnalyticalNotes: 'Output GST liability net of ITC matches GSTR-3B filings. TDS payable includes Section 194Q and Section 194J obligations.'
  }
];

export const initialApexVolumeMetrics: YoYVolumeMetric[] = [
  {
    voucherType: 'Sales',
    currentYearCount: 5420,
    priorYearCount: 4580,
    varianceCount: 840,
    variancePercent: 18.34,
    currentTotalAmount: 184250000,
    priorTotalAmount: 151000000,
    avgAmountCurrent: 33994,
    avgAmountPrior: 32969,
    commentary: 'Stable ticket size with volume growth across industrial casting clients.'
  },
  {
    voucherType: 'Purchase',
    currentYearCount: 4110,
    priorYearCount: 3620,
    varianceCount: 490,
    variancePercent: 13.54,
    currentTotalAmount: 112400000,
    priorTotalAmount: 94500000,
    avgAmountCurrent: 27347,
    avgAmountPrior: 26105,
    commentary: 'Procurement orders consolidated into larger batch deliveries.'
  },
  {
    voucherType: 'Payment',
    currentYearCount: 2650,
    priorYearCount: 2210,
    varianceCount: 440,
    variancePercent: 19.91,
    currentTotalAmount: 98400000,
    priorTotalAmount: 82100000,
    avgAmountCurrent: 37132,
    avgAmountPrior: 37149,
    commentary: 'Digital vendor payments executed via NEFT/RTGS with verified bank authorization.'
  },
  {
    voucherType: 'Receipt',
    currentYearCount: 1840,
    priorYearCount: 1510,
    varianceCount: 330,
    variancePercent: 21.85,
    currentTotalAmount: 172500000,
    priorTotalAmount: 141200000,
    avgAmountCurrent: 93750,
    avgAmountPrior: 93509,
    commentary: 'Customer receipt volume corresponds with higher debtor turnover.'
  },
  {
    voucherType: 'Journal',
    currentYearCount: 980,
    priorYearCount: 820,
    varianceCount: 160,
    variancePercent: 19.51,
    currentTotalAmount: 38400000,
    priorTotalAmount: 29500000,
    avgAmountCurrent: 39183,
    avgAmountPrior: 35975,
    commentary: 'Year-end provisions, depreciation, and tax adjustments.'
  },
  {
    voucherType: 'Debit / Credit Note',
    currentYearCount: 160,
    priorYearCount: 110,
    varianceCount: 50,
    variancePercent: 45.45,
    currentTotalAmount: 6450000,
    priorTotalAmount: 4100000,
    avgAmountCurrent: 40312,
    avgAmountPrior: 37272,
    commentary: 'Spike in Q2 credit notes due to transit damage claim with carrier.'
  },
  {
    voucherType: 'Contra',
    currentYearCount: 240,
    priorYearCount: 210,
    varianceCount: 30,
    variancePercent: 14.29,
    currentTotalAmount: 12500000,
    priorTotalAmount: 10800000,
    avgAmountCurrent: 52083,
    avgAmountPrior: 51428,
    commentary: 'Inter-bank fund transfers between HDFC working capital and SBI credit lines.'
  }
];

export const initialApexRecurringFindings: RecurringFindingItem[] = [
  {
    id: 'REC-FIND-01',
    ruleId: 'GST-CHK-05',
    ruleName: 'Interstate vs Intrastate Allocation Consistency',
    module: 'GST Statutory',
    severity: 'Critical',
    partyOrLedger: 'Tata Steel Ltd (Gujarat Plant)',
    description: 'Supplier with Gujarat GSTIN (prefix 24) charged with Maharashtra local CGST+SGST (state 27) instead of IGST 18%. Same issue occurred in FY 2024-25 Q4 voucher PUR-88.',
    occurrenceCount: 2,
    priorYearStatus: 'Flagged in FY 2024-25 (Rectified via subsequent return credit adjustment)',
    currentYearStatus: 'Repeated in FY 2025-26 PUR-05 (₹22,500 mismatch)',
    auditorActionRequired: 'Vendor Master Place of Supply rule must be locked in Tally to prevent recurring data-entry operator error.'
  },
  {
    id: 'REC-FIND-02',
    ruleId: 'ACC-CASH-01',
    ruleName: 'Negative Cash-in-Hand Balance (Intraday Deficit)',
    module: 'Accounting Hygiene',
    severity: 'Medium',
    partyOrLedger: 'Cash-in-Hand / Factory Petty Cash',
    description: 'Petty cash ledger exhibited intraday negative balance (-₹8,500) prior to withdrawal posting. Identical timing lag occurred in FY 2024-25 on 14-Aug-2024.',
    occurrenceCount: 2,
    priorYearStatus: 'Flagged in FY 2024-25 (Management representation given on timing delay)',
    currentYearStatus: 'Repeated on 18-Jun-2025 (Cash withdrawal entered 2 days after physical disbursement)',
    auditorActionRequired: 'Management must enforce imprest petty cash system and daily register balancing.'
  },
  {
    id: 'REC-FIND-03',
    ruleId: 'TDS-194Q-01',
    ruleName: 'TDS on Purchase of Goods Exceeding ₹50 Lakhs (Sec 194Q)',
    module: 'TDS Withholding',
    severity: 'High',
    partyOrLedger: 'Steel Authority of India Ltd (SAIL)',
    description: 'Cumulative purchases exceeded ₹50 Lakhs statutory limit, but TDS @ 0.1% was omitted on voucher PUR-142.',
    occurrenceCount: 2,
    priorYearStatus: 'Flagged in FY 2024-25 (Paid with interest u/s 201(1A))',
    currentYearStatus: 'Re-occurred on 2nd tranche of procurement in FY 2025-26',
    auditorActionRequired: 'Configure Tally TDS nature of payment thresholds to auto-deduct 194Q tax on invoice booking.'
  }
];

export const initialApexFindingComparisons: YoYFindingComparison[] = [
  {
    module: 'GST Statutory',
    currentYearFindings: 8,
    priorYearFindings: 6,
    variance: 2,
    criticalCountCurrent: 2,
    criticalCountPrior: 1,
    resolvedFromPriorYear: 5,
    newInCurrentYear: 7,
    recurringCount: 1
  },
  {
    module: 'TDS Withholding',
    currentYearFindings: 4,
    priorYearFindings: 3,
    variance: 1,
    criticalCountCurrent: 1,
    criticalCountPrior: 0,
    resolvedFromPriorYear: 2,
    newInCurrentYear: 3,
    recurringCount: 1
  },
  {
    module: 'Duplicate Detection',
    currentYearFindings: 3,
    priorYearFindings: 3,
    variance: 0,
    criticalCountCurrent: 0,
    criticalCountPrior: 0,
    resolvedFromPriorYear: 3,
    newInCurrentYear: 3,
    recurringCount: 0
  },
  {
    module: 'General Accounting',
    currentYearFindings: 5,
    priorYearFindings: 4,
    variance: 1,
    criticalCountCurrent: 0,
    criticalCountPrior: 1,
    resolvedFromPriorYear: 3,
    newInCurrentYear: 4,
    recurringCount: 1
  },
  {
    module: 'Sequencing & Numbers',
    currentYearFindings: 2,
    priorYearFindings: 1,
    variance: 1,
    criticalCountCurrent: 0,
    criticalCountPrior: 0,
    resolvedFromPriorYear: 1,
    newInCurrentYear: 2,
    recurringCount: 0
  },
  {
    module: 'Forensic Anomalies',
    currentYearFindings: 2,
    priorYearFindings: 2,
    variance: 0,
    criticalCountCurrent: 0,
    criticalCountPrior: 0,
    resolvedFromPriorYear: 2,
    newInCurrentYear: 2,
    recurringCount: 0
  }
];

// -------------------------------------------------------------
// DATA REPOSITORY: COMPANY & FINANCIAL YEAR STORE
// -------------------------------------------------------------

// Helper to construct isolated data for other companies/years
function createBharatLogisticsData(): CompanyYearDataStore {
  const companyName = 'Bharat Logistics & Infra Corp Ltd';
  const fy = 'FY 2025-26';
  return {
    companyId: 'COMP-002',
    financialYearId: 'FY-2025-26',
    companyName,
    financialPeriod: fy,
    exceptions: [
      {
        id: 'EXC-BL-001',
        companyName,
        severity: 'Critical',
        module: 'GST Statutory',
        ruleId: 'GST-RCM-01',
        ruleName: 'Goods Transport Agency (GTA) Reverse Charge Mechanism (RCM)',
        ruleVersion: '1.2.0',
        ruleEffectiveDate: '01-Jul-2017',
        ruleJurisdiction: 'IN-ALL',
        statutoryReference: 'CGST Act 2017 Sec 9(3) / Notif 13/2017-CT(R)',
        ruleDescription: 'Verifies whether tax on GTA freight services is paid under Reverse Charge at 5% when GTA has not opted for forward charge.',
        exceptionTitle: 'RCM Liability Not Booked for GTA Fleet Freight Invoices',
        whyFlagged: 'Freight payment voucher PMT-BL-102 of ₹3,40,000 to Western Roadlines lacks RCM liability posting in GSTR-3B Table 3.1(d).',
        voucherNumber: 'PMT-BL-102',
        voucherDate: '12-Jul-2025',
        voucherType: 'Payment',
        partyLedgerName: 'Western Roadlines Freight Carriers',
        primaryLedger: 'Freight & Transportation Charges',
        partyGstin: '07AAACW8812L1ZZ',
        partyPan: 'AAACW8812L',
        amount: 340000,
        narration: 'Interstate bulk carriage charges for consignments Ex-Noida to Mumbai warehouse',
        evidenceJson: JSON.stringify({ GTA_Status: 'Unregistered GTA', TaxRate: '5% RCM', UnpaidRcmTax: 17000 }),
        gstDetails: {
          partyGstin: '07AAACW8812L1ZZ',
          registrationType: 'Regular',
          placeOfSupply: '07-Delhi',
          isReverseCharge: true,
          hsnOrSac: '9965',
          taxRatePercent: 5.0,
          taxableAmount: 340000,
          cgstAmount: 8500,
          sgstAmount: 8500,
          totalTaxAmount: 17000,
          gstLedgers: []
        },
        tallyNavigationGuide: {
          masterId: '20194',
          alterId: '55102',
          guid: '8a1b2c3d-4e5f-6789-0123-456789abcdef',
          gatewayPath: 'Gateway of Tally > Display More Reports > Account Books > Payment Register',
          quickGoTo: 'Alt+G > Payment Register > PMT-BL-102',
          exactKeys: ['Alt+G', 'Payment Register', 'PMT-BL-102'],
          xmlQueryPayload: '<ENVELOPE></ENVELOPE>'
        },
        relatedLedger: {
          ledgerName: 'Western Roadlines Freight Carriers',
          parentGroup: 'Sundry Creditors',
          primaryHead: 'Current Liabilities',
          openingBalance: 0,
          currentBalance: 340000,
          closingBalanceType: 'Cr'
        },
        sourceVoucher: {
          voucherId: 'VBL-102',
          voucherNumber: 'PMT-BL-102',
          voucherType: 'Payment',
          voucherDate: '12-Jul-2025',
          partyLedgerName: 'Western Roadlines Freight Carriers',
          totalAmount: 340000,
          entries: [
            { entryId: 'E1', ledgerName: 'Freight & Transportation Charges', parentGroup: 'Direct Expenses', amount: 340000, isDebit: true },
            { entryId: 'E2', ledgerName: 'HDFC Bank Freight A/c', parentGroup: 'Bank Accounts', amount: 340000, isDebit: false }
          ]
        },
        relatedTransactions: [],
        status: 'Requires Review - Pending',
        reviewerNotes: 'Auditor must obtain declaration from Western Roadlines confirming non-availment of forward charge 12% GST option.',
        reviewHistory: []
      },
      {
        id: 'EXC-BL-002',
        companyName,
        severity: 'High',
        module: 'TDS Withholding',
        ruleId: 'TDS-194C-01',
        ruleName: 'TDS on Payments to Contractors & Sub-contractors (Sec 194C)',
        ruleVersion: '2.0.1',
        ruleEffectiveDate: '01-Apr-2020',
        ruleJurisdiction: 'IN-ALL',
        statutoryReference: 'Income Tax Act 1961 Sec 194C',
        ruleDescription: 'Verifies whether TDS @ 1% / 2% is deducted on freight contracts exceeding ₹30,000 single or ₹1,00,000 annual aggregate without Form 26A / 10-vehicle declaration.',
        exceptionTitle: 'Section 194C TDS Omitted on Fleet Contractor Billing',
        whyFlagged: 'Invoice PUR-BL-84 of ₹1,85,000 booked without TDS deduction under Sec 194C; contractor vehicle ownership certificate not on record.',
        voucherNumber: 'PUR-BL-84',
        voucherDate: '24-Aug-2025',
        voucherType: 'Purchase',
        partyLedgerName: 'Jai Hind Logistics Co-operative',
        primaryLedger: 'Vehicle Hire & Fleet Subcontract Charges',
        partyPan: 'AABFJ9921E',
        amount: 185000,
        narration: 'Container movement charges for August 2025',
        evidenceJson: JSON.stringify({ Amount: 185000, ExpectedTDS: 3700, Rate: '2%' }),
        gstDetails: {
          registrationType: 'Regular',
          placeOfSupply: '07-Delhi',
          isReverseCharge: false,
          taxableAmount: 185000,
          totalTaxAmount: 0,
          gstLedgers: []
        },
        tallyNavigationGuide: {
          masterId: '20210',
          alterId: '55140',
          guid: '9b2c3d4e-5f60-7890-1234-567890abcdef',
          gatewayPath: 'Gateway of Tally > Purchase Register',
          quickGoTo: 'Alt+G > Purchase Register',
          exactKeys: ['Alt+G'],
          xmlQueryPayload: '<ENVELOPE></ENVELOPE>'
        },
        relatedLedger: {
          ledgerName: 'Jai Hind Logistics Co-operative',
          parentGroup: 'Sundry Creditors',
          primaryHead: 'Current Liabilities',
          openingBalance: 0,
          currentBalance: 185000,
          closingBalanceType: 'Cr'
        },
        sourceVoucher: {
          voucherId: 'VBL-84',
          voucherNumber: 'PUR-BL-84',
          voucherType: 'Purchase',
          voucherDate: '24-Aug-2025',
          partyLedgerName: 'Jai Hind Logistics Co-operative',
          totalAmount: 185000,
          entries: [
            { entryId: 'E1', ledgerName: 'Vehicle Hire & Fleet Subcontract Charges', parentGroup: 'Direct Expenses', amount: 185000, isDebit: true },
            { entryId: 'E2', ledgerName: 'Jai Hind Logistics Co-operative', parentGroup: 'Sundry Creditors', amount: 185000, isDebit: false }
          ]
        },
        relatedTransactions: [],
        status: 'Requires Review - Pending',
        reviewerNotes: 'Client notified to obtain Form 26A / valid 10-vehicle declaration under Section 194C(6).',
        reviewHistory: []
      }
    ],
    auditPlan: {
      id: 'PLAN-BL-2026',
      companyId: 'COMP-002',
      companyName,
      financialPeriod: fy,
      createdAt: '2026-09-18T10:00:00Z',
      updatedAt: '2026-09-24T18:30:00Z',
      status: 'In Progress',
      materialityAmount: 450000,
      performanceMaterialityAmount: 337500,
      trivialThreshold: 22500,
      materialityBasis: 'Revenue',
      notes: 'Logistics audit focusing on RCM liabilities on freight carriers, FASTag fuel reconciliation, and Section 194C fleet deductions.',
      selectedAreas: [
        { id: 'BL-AREA-1', name: 'Freight & Fleet Operations', isEnabled: true, riskLevel: 'High', findingsCount: 4 },
        { id: 'BL-AREA-2', name: 'GST Statutory & RCM', isEnabled: true, riskLevel: 'High', findingsCount: 6 },
        { id: 'BL-AREA-3', name: 'TDS Withholding', isEnabled: true, riskLevel: 'Medium', findingsCount: 3 },
        { id: 'BL-AREA-4', name: 'Bank & FASTag Accounts', isEnabled: true, riskLevel: 'Low', findingsCount: 1 }
      ]
    },
    auditRisks: [
      {
        id: 'RISK-BL-01',
        planId: 'PLAN-BL-2026',
        auditArea: 'GST Statutory & RCM',
        description: 'Unrecorded GTA RCM tax liability under CGST Act Section 9(3).',
        indicator: 'High freight expenditure without commensurate RCM tax cash payment.',
        evidenceSource: 'Freight Ledger vs GSTR-3B Table 3.1(d)',
        likelihood: 4,
        impact: 5,
        riskLevel: 'High',
        status: 'Open',
        remarks: 'Verification of transport consignment notes (bilty) in progress.'
      }
    ],
    auditProcedures: [
      {
        id: 'PROC-BL-01',
        planId: 'PLAN-BL-2026',
        auditArea: 'GST Statutory & RCM',
        name: 'Verify GTA RCM Inward Supply Tax Payments',
        objective: 'Ensure all freight payments where transporter did not charge 12% forward GST are discharged under 5% RCM in cash.',
        description: 'Sample top 20 transport vendors and inspect freight bills against GSTR-3B cash ledger debits.',
        status: 'In Progress',
        remarks: 'Western Roadlines voucher flagged for missing RCM tax.',
        linkedFindingIds: ['EXC-BL-001'],
        requiredEvidenceCount: 2,
        linkedEvidenceIds: []
      }
    ],
    auditSamples: [],
    auditEvidence: [
      {
        id: 'EVD-BL-01',
        planId: 'PLAN-BL-2026',
        auditArea: 'GST Statutory & RCM',
        procedureId: 'PROC-BL-01',
        findingId: 'EXC-BL-001',
        evidenceType: 'Purchase Document',
        description: 'Western Roadlines Freight Invoice #WR-9921',
        referenceNumber: 'WR-9921',
        source: 'PDF Upload',
        fileName: 'Freight_Bill_WR_9921.pdf',
        filePath: 'AuditData/Bharat Logistics & Infra Corp Ltd/FY 2025-26/PLAN-BL-2026/Evidence/Freight_Bill_WR_9921.pdf',
        fileHash: 'sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855',
        fileIntegrityStatus: 'Verified',
        storagePath: 'AuditData/Bharat Logistics & Infra Corp Ltd/FY 2025-26/PLAN-BL-2026/Evidence/Freight_Bill_WR_9921.pdf',
        sizeBytes: 312000,
        dateReceived: '2026-09-24',
        uploadedAt: '2026-09-24T18:00:00Z',
        status: 'Reviewed',
        auditorRemarks: 'Transporter has explicitly endorsed "No ITC availed, 5% RCM applicable". Confirming RCM liability.'
      }
    ],
    evidenceRequests: [],
    workingPapers: [],
    auditActivities: [
      {
        id: 'ACT-BL-01',
        planId: 'PLAN-BL-2026',
        timestamp: '2026-09-24T18:30:00Z',
        user: 'CA. Meenakshi Sundaram',
        action: 'Company Synchronized',
        details: 'Synchronized 22,100 vouchers and 465 ledgers from Tally port 9000.'
      }
    ],
    auditAmendments: [],
    reconciliationFindings: [
      {
        id: 'REC-BL-01',
        ruleId: 'REC-GST-02',
        ruleName: 'GSTR-3B Table 3.1(d) vs RCM Ledger Verification',
        category: 'GST',
        severity: 'Critical',
        expectedAmount: 185000,
        actualAmount: 168000,
        difference: 17000,
        ledgerName: 'RCM Output CGST + SGST Payable',
        period: 'Jul-2025',
        evidenceJson: JSON.stringify({ Variance: 17000 }),
        whyFlagged: 'Short payment of RCM cash liability on freight by ₹17,000 in July 2025.',
        status: 'Requires Review - Pending'
      }
    ],
    ledgers: [
      {
        ledgerName: 'Western Roadlines Freight Carriers',
        parentGroup: 'Sundry Creditors',
        primaryHead: 'Current Liabilities',
        openingBalance: 120000,
        currentBalance: 340000,
        closingBalanceType: 'Cr'
      },
      {
        ledgerName: 'Freight & Transportation Charges',
        parentGroup: 'Direct Expenses',
        primaryHead: 'Indirect Expenses',
        openingBalance: 0,
        currentBalance: 4210000,
        closingBalanceType: 'Dr'
      }
    ],
    vouchers: [],
    financialMetrics: [
      {
        category: 'Revenue',
        metricName: 'Logistics & Freight Revenue',
        accountHead: 'Freight Incomes',
        currentYearAmount: 342000000, // ₹34.2 Cr
        priorYearAmount: 285000000,   // ₹28.5 Cr
        varianceAmount: 57000000,
        variancePercent: 20.00,
        trend: 'Up',
        isAlert: true,
        auditorAnalyticalNotes: 'Fleet capacity expanded with 40 new leased multi-axle trailers. Revenue increased 20.0%.'
      },
      {
        category: 'Purchases',
        metricName: 'Diesel, Fuel & FASTag Toll Costs',
        accountHead: 'Direct Fleet Operating Expenses',
        currentYearAmount: 198000000, // ₹19.8 Cr
        priorYearAmount: 162000000,   // ₹16.2 Cr
        varianceAmount: 36000000,
        variancePercent: 22.22,
        trend: 'Up',
        isAlert: true,
        auditorAnalyticalNotes: 'Fuel costs rose faster than revenue (+22.22%) due to diesel price index movement.'
      },
      {
        category: 'Cash & Bank',
        metricName: 'Bank Balances & Overdraft Utilization',
        accountHead: 'Bank OD Accounts',
        currentYearAmount: 24500000,  // ₹2.45 Cr
        priorYearAmount: 19000000,    // ₹1.90 Cr
        varianceAmount: 5500000,
        variancePercent: 28.95,
        trend: 'Up',
        isAlert: false,
        auditorAnalyticalNotes: 'Comfortable liquidity buffer maintained against fuel vendor payment cycles.'
      }
    ],
    volumeMetrics: [
      {
        voucherType: 'Sales / Consignment Notes',
        currentYearCount: 11200,
        priorYearCount: 9400,
        varianceCount: 1800,
        variancePercent: 19.15,
        currentTotalAmount: 342000000,
        priorTotalAmount: 285000000,
        avgAmountCurrent: 30535,
        avgAmountPrior: 30319,
        commentary: 'Consignment volume expanded 19.15% across north-south corridors.'
      },
      {
        voucherType: 'Fleet Expenses & Payments',
        currentYearCount: 8400,
        priorYearCount: 7100,
        varianceCount: 1300,
        variancePercent: 18.31,
        currentTotalAmount: 215000000,
        priorTotalAmount: 178000000,
        avgAmountCurrent: 25595,
        avgAmountPrior: 25070,
        commentary: 'En-route FASTag electronic toll transactions reconciled with NHAI server.'
      }
    ],
    findingComparisons: [
      {
        module: 'GST Statutory & RCM',
        currentYearFindings: 6,
        priorYearFindings: 4,
        variance: 2,
        criticalCountCurrent: 2,
        criticalCountPrior: 1,
        resolvedFromPriorYear: 3,
        newInCurrentYear: 5,
        recurringCount: 1
      },
      {
        module: 'TDS Withholding',
        currentYearFindings: 3,
        priorYearFindings: 2,
        variance: 1,
        criticalCountCurrent: 1,
        criticalCountPrior: 0,
        resolvedFromPriorYear: 2,
        newInCurrentYear: 3,
        recurringCount: 0
      }
    ],
    recurringFindings: [
      {
        id: 'REC-BL-01',
        ruleId: 'GST-RCM-01',
        ruleName: 'GTA RCM Tax Payment Omission',
        module: 'GST Statutory',
        severity: 'Critical',
        partyOrLedger: 'Western Roadlines Freight Carriers',
        description: 'Freight vouchers booked under direct expenses without corresponding RCM tax debit to cash ledger.',
        occurrenceCount: 2,
        priorYearStatus: 'Identified in FY 2024-25 audit (Challan paid under Section 73)',
        currentYearStatus: 'Repeated in July 2025 freight batch',
        auditorActionRequired: 'Management must configure Tally purchase voucher type with mandatory RCM tax calculation.'
      }
    ],
    analyticalReviewNotes: 'Bharat Logistics & Infra Corp has experienced high volume expansion in interstate container movement. Special focus required on GTA reverse-charge mechanism (RCM) liabilities and Section 194C sub-contractor declarations.'
  };
}

function createZenithEngineeringData(): CompanyYearDataStore {
  const companyName = 'Zenith Precision Engineering Pvt Ltd';
  const fy = 'FY 2025-26';
  return {
    companyId: 'COMP-003',
    financialYearId: 'FY-2025-26',
    companyName,
    financialPeriod: fy,
    exceptions: [
      {
        id: 'EXC-ZP-001',
        companyName,
        severity: 'High',
        module: 'GST Statutory',
        ruleId: 'GST-ITC-02',
        ruleName: 'Capital Goods ITC Availment & Asset Capitalization Matching',
        ruleVersion: '1.2.0',
        ruleEffectiveDate: '01-Jul-2017',
        ruleJurisdiction: 'IN-ALL',
        statutoryReference: 'CGST Act 2017 Sec 16(3) & Sec 17(5)(c)',
        ruleDescription: 'Ensures no depreciation under Income Tax Section 32 is claimed on the GST component of capital goods where ITC has been availed.',
        exceptionTitle: 'Double Benefit Check: Depreciation on GST Component of CNC Machine',
        whyFlagged: 'Invoice PUR-ZP-18 for ₹48,00,000 + 18% IGST (₹8,64,000) for 5-Axis CNC Milling Center. Capitalized gross amount in Fixed Asset register must be verified against ITC claim.',
        voucherNumber: 'PUR-ZP-18',
        voucherDate: '15-May-2025',
        voucherType: 'Purchase',
        partyLedgerName: 'DMG Mori Precision Machine Tools India',
        primaryLedger: 'Plant & Machinery - CNC Division',
        partyGstin: '29AABCD1122K1ZL',
        partyPan: 'AABCD1122K',
        amount: 5664000,
        narration: 'Procurement of 5-Axis CNC Vertical Machining Center for aerospace bracket line',
        evidenceJson: JSON.stringify({ MachineValue: 4800000, IGST_Claimed: 864000, CapitalizedCost: 4800000 }),
        gstDetails: {
          partyGstin: '29AABCD1122K1ZL',
          registrationType: 'Regular',
          placeOfSupply: '24-Gujarat',
          isReverseCharge: false,
          hsnOrSac: '8457',
          taxRatePercent: 18.0,
          taxableAmount: 4800000,
          igstAmount: 864000,
          totalTaxAmount: 864000,
          gstLedgers: [{ ledgerName: 'Input IGST on Capital Goods', amount: 864000, rate: 18.0 }]
        },
        tallyNavigationGuide: {
          masterId: '31045',
          alterId: '62190',
          guid: '1c2d3e4f-5a6b-7c8d-9e0f-1a2b3c4d5e6f',
          gatewayPath: 'Gateway of Tally > Purchase Register',
          quickGoTo: 'Alt+G > Purchase Register > PUR-ZP-18',
          exactKeys: ['Alt+G'],
          xmlQueryPayload: '<ENVELOPE></ENVELOPE>'
        },
        relatedLedger: {
          ledgerName: 'Plant & Machinery - CNC Division',
          parentGroup: 'Fixed Assets',
          primaryHead: 'Current Assets',
          openingBalance: 14500000,
          currentBalance: 19300000,
          closingBalanceType: 'Dr'
        },
        sourceVoucher: {
          voucherId: 'VZP-18',
          voucherNumber: 'PUR-ZP-18',
          voucherType: 'Purchase',
          voucherDate: '15-May-2025',
          partyLedgerName: 'DMG Mori Precision Machine Tools India',
          totalAmount: 5664000,
          entries: [
            { entryId: 'E1', ledgerName: 'Plant & Machinery - CNC Division', parentGroup: 'Fixed Assets', amount: 4800000, isDebit: true },
            { entryId: 'E2', ledgerName: 'Input IGST on Capital Goods', parentGroup: 'Duties & Taxes', amount: 864000, isDebit: true },
            { entryId: 'E3', ledgerName: 'DMG Mori Precision Machine Tools India', parentGroup: 'Sundry Creditors', amount: 5664000, isDebit: false }
          ]
        },
        relatedTransactions: [],
        status: 'Reviewed',
        reviewerNotes: 'Asset register verified: Capitalized at ₹48,00,000 (net of GST). ITC of ₹8,64,000 availed in GSTR-3B Table 4(A)(2). No double benefit claimed under Section 16(3).',
        reviewHistory: []
      }
    ],
    auditPlan: {
      id: 'PLAN-ZP-2026',
      companyId: 'COMP-003',
      companyName,
      financialPeriod: fy,
      createdAt: '2026-09-15T09:00:00Z',
      updatedAt: '2026-09-22T14:10:00Z',
      status: 'In Progress',
      materialityAmount: 220000,
      performanceMaterialityAmount: 165000,
      trivialThreshold: 11000,
      materialityBasis: 'Assets',
      notes: 'Precision engineering audit with emphasis on capital asset additions, job work scrap reconciliations, and vendor MSME compliance.',
      selectedAreas: [
        { id: 'ZP-AREA-1', name: 'Plant & Fixed Assets', isEnabled: true, riskLevel: 'High', findingsCount: 2 },
        { id: 'ZP-AREA-2', name: 'Job Work & Subcontracting', isEnabled: true, riskLevel: 'Medium', findingsCount: 3 },
        { id: 'ZP-AREA-3', name: 'GST on Capital Goods', isEnabled: true, riskLevel: 'High', findingsCount: 4 }
      ]
    },
    auditRisks: [],
    auditProcedures: [],
    auditSamples: [],
    auditEvidence: [],
    evidenceRequests: [],
    workingPapers: [],
    auditActivities: [],
    auditAmendments: [],
    reconciliationFindings: [],
    ledgers: [],
    vouchers: [],
    financialMetrics: [
      {
        category: 'Revenue',
        metricName: 'Machining & OEM Job Work Revenue',
        accountHead: 'Sales & Job Work Charges',
        currentYearAmount: 124500000, // ₹12.45 Cr
        priorYearAmount: 108000000,   // ₹10.80 Cr
        varianceAmount: 16500000,
        variancePercent: 15.28,
        trend: 'Up',
        isAlert: false,
        auditorAnalyticalNotes: 'Steady 15.28% revenue growth in aerospace machining contract line.'
      },
      {
        category: 'Operating Expenses',
        metricName: 'Consumables, Tooling & Electricity',
        accountHead: 'Factory Power & Cutting Tool Consumables',
        currentYearAmount: 28400000,  // ₹2.84 Cr
        priorYearAmount: 24100000,    // ₹2.41 Cr
        varianceAmount: 4300000,
        variancePercent: 17.84,
        trend: 'Up',
        isAlert: false,
        auditorAnalyticalNotes: 'Tooling costs commensurate with titanium alloy machining volume.'
      }
    ],
    volumeMetrics: [
      {
        voucherType: 'Sales Invoices',
        currentYearCount: 2840,
        priorYearCount: 2450,
        varianceCount: 390,
        variancePercent: 15.92,
        currentTotalAmount: 124500000,
        priorTotalAmount: 108000000,
        avgAmountCurrent: 43838,
        avgAmountPrior: 44081,
        commentary: 'High average invoice value reflecting specialized precision turning jobs.'
      }
    ],
    findingComparisons: [],
    recurringFindings: [],
    analyticalReviewNotes: 'Zenith Precision Engineering shows healthy margins and solid capital reinvestment. Asset register verified with physically tagged barcodes on CNC machines.'
  };
}

function createHeritageTextilesData(): CompanyYearDataStore {
  const companyName = 'Heritage Textiles & Handlooms LLP';
  const fy = 'FY 2025-26';
  return {
    companyId: 'COMP-004',
    financialYearId: 'FY-2025-26',
    companyName,
    financialPeriod: fy,
    exceptions: [],
    auditPlan: {
      id: 'PLAN-HT-2026',
      companyId: 'COMP-004',
      companyName,
      financialPeriod: fy,
      createdAt: '2026-09-12T11:00:00Z',
      updatedAt: '2026-09-20T10:15:00Z',
      status: 'Review',
      materialityAmount: 180000,
      performanceMaterialityAmount: 135000,
      trivialThreshold: 9000,
      materialityBasis: 'Revenue',
      notes: 'Textile export audit covering GST LUT zero-rated export refund claims, duty drawback, and foreign exchange realized within FEMA time limits.',
      selectedAreas: [
        { id: 'HT-AREA-1', name: 'Zero-Rated Exports (LUT)', isEnabled: true, riskLevel: 'High', findingsCount: 2 },
        { id: 'HT-AREA-2', name: 'Raw Cotton Purchases & RCM', isEnabled: true, riskLevel: 'Medium', findingsCount: 1 }
      ]
    },
    auditRisks: [],
    auditProcedures: [],
    auditSamples: [],
    auditEvidence: [],
    evidenceRequests: [],
    workingPapers: [],
    auditActivities: [],
    auditAmendments: [],
    reconciliationFindings: [],
    ledgers: [],
    vouchers: [],
    financialMetrics: [
      {
        category: 'Revenue',
        metricName: 'Direct Garment Export Realization',
        accountHead: 'Export Sales Accounts (FOB)',
        currentYearAmount: 98500000, // ₹9.85 Cr
        priorYearAmount: 84000000,   // ₹8.40 Cr
        varianceAmount: 14500000,
        variancePercent: 17.26,
        trend: 'Up',
        isAlert: false,
        auditorAnalyticalNotes: 'Zero-rated exports under Letter of Undertaking (LUT) verified against shipping bills and BRC/e-FIRC.'
      }
    ],
    volumeMetrics: [],
    findingComparisons: [],
    recurringFindings: [],
    analyticalReviewNotes: 'Heritage Textiles has completed review stage. Foreign inward remittances reconciled with EDPMS portal.'
  };
}

// -------------------------------------------------------------
// MAIN COMPANY-YEAR REPOSITORY MAP
// -------------------------------------------------------------

export const companyYearDataMap: Record<string, Record<string, CompanyYearDataStore>> = {
  'COMP-001': {
    'FY-2025-26': {
      companyId: 'COMP-001',
      financialYearId: 'FY-2025-26',
      companyName: 'Apex Industrial Solutions Pvt Ltd',
      financialPeriod: 'FY 2025-26',
      exceptions: initialWorkspaceExceptions,
      auditPlan: initialAuditPlan,
      auditRisks: initialAuditRisks,
      auditProcedures: initialAuditProcedures,
      auditSamples: [],
      auditEvidence: initialAuditEvidence,
      evidenceRequests: initialEvidenceRequests,
      workingPapers: initialWorkingPapers,
      auditActivities: initialAuditActivities,
      auditAmendments: [],
      reconciliationFindings: initialReconciliationFindings,
      ledgers: allSynchronizedLedgers,
      vouchers: allSynchronizedVouchers,
      financialMetrics: initialApexYoYMetrics,
      volumeMetrics: initialApexVolumeMetrics,
      findingComparisons: initialApexFindingComparisons,
      recurringFindings: initialApexRecurringFindings,
      analyticalReviewNotes: 'Apex Industrial Solutions Pvt Ltd demonstrated strong topline growth (+22.02%) in FY 2025-26. Analytical review highlighted GST place-of-supply classifications, higher electricity overheads (+23.46%), and debtors aging (>65 days) as primary focus areas for detailed substantive testing per SA 520.'
    },
    'FY-2024-25': {
      companyId: 'COMP-001',
      financialYearId: 'FY-2024-25',
      companyName: 'Apex Industrial Solutions Pvt Ltd',
      financialPeriod: 'FY 2024-25',
      exceptions: [
        {
          ...initialWorkspaceExceptions[0],
          id: 'EXC-2025-088',
          voucherNumber: 'PUR-88',
          voucherDate: '14-Feb-2025',
          exceptionTitle: 'Prior Year Interstate Supply Mismatch on Gujarat Vendor',
          status: 'Reviewed',
          reviewerNotes: 'Finalized and settled in FY 2024-25 tax return.'
        }
      ],
      auditPlan: {
        ...initialAuditPlan,
        id: 'PLAN-2025-001',
        financialPeriod: 'FY 2024-25',
        status: 'Completed',
        notes: 'Finalized statutory audit for FY 2024-25. Unmodified opinion issued.'
      },
      auditRisks: [],
      auditProcedures: [],
      auditSamples: [],
      auditEvidence: [],
      evidenceRequests: [],
      workingPapers: [],
      auditActivities: [
        {
          id: 'ACT-2025-01',
          planId: 'PLAN-2025-001',
          timestamp: '2025-09-28T16:00:00Z',
          user: 'CA. Sanjiv (Lead Partner)',
          action: 'Audit File Finalized & Locked',
          details: 'Statutory audit for FY 2024-25 signed off with Unmodified Opinion.'
        }
      ],
      auditAmendments: [],
      reconciliationFindings: [],
      ledgers: allSynchronizedLedgers,
      vouchers: allSynchronizedVouchers,
      financialMetrics: initialApexYoYMetrics,
      volumeMetrics: initialApexVolumeMetrics,
      findingComparisons: initialApexFindingComparisons,
      recurringFindings: initialApexRecurringFindings,
      analyticalReviewNotes: 'Archived statutory audit file for FY 2024-25. All working papers signed off.'
    }
  },
  'COMP-002': {
    'FY-2025-26': createBharatLogisticsData()
  },
  'COMP-003': {
    'FY-2025-26': createZenithEngineeringData()
  },
  'COMP-004': {
    'FY-2025-26': createHeritageTextilesData()
  }
};

// -------------------------------------------------------------
// HELPER REPOSITORY FUNCTIONS
// -------------------------------------------------------------

export function getCompanyWorkspace(companyId: string): CompanyWorkspace {
  const found = initialCompanies.find(c => c.id === companyId);
  return found || initialCompanies[0];
}

export function getCompanyYearData(companyId: string, financialYearId: string): CompanyYearDataStore {
  const companyStore = companyYearDataMap[companyId];
  if (companyStore && companyStore[financialYearId]) {
    return companyStore[financialYearId];
  }
  // Fallback to default
  const defaultStore = companyYearDataMap['COMP-001']['FY-2025-26'];
  return {
    ...defaultStore,
    companyId,
    financialYearId
  };
}

export function rollForwardAuditPlan(
  sourceStore: CompanyYearDataStore,
  targetFinancialYear: string,
  targetFinancialYearId: string
): Partial<CompanyYearDataStore> {
  const newPlanId = `PLAN-${targetFinancialYearId.replace('FY-', '')}-001`;
  
  // 1. Roll forward Audit Plan with fresh status
  const rolledPlan: AuditPlan = {
    ...sourceStore.auditPlan,
    id: newPlanId,
    financialPeriod: targetFinancialYear,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    status: 'Draft',
    notes: `Rolled forward from ${sourceStore.financialPeriod}. Review materiality thresholds and risk ratings for current fiscal period.`
  };

  // 2. Roll forward recurring risks
  const rolledRisks: AuditRisk[] = sourceStore.auditRisks.map((risk, idx) => ({
    ...risk,
    id: `RISK-${targetFinancialYearId.replace('FY-', '')}-${(idx + 1).toString().padStart(3, '0')}`,
    planId: newPlanId,
    status: 'Open',
    remarks: `Carried forward from ${sourceStore.financialPeriod} as focus audit area.`
  }));

  // 3. Roll forward standard procedures
  const rolledProcedures: AuditProcedure[] = sourceStore.auditProcedures.map((proc, idx) => ({
    ...proc,
    id: `PROC-${targetFinancialYearId.replace('FY-', '')}-${(idx + 1).toString().padStart(3, '0')}`,
    planId: newPlanId,
    status: 'Not Started',
    linkedFindingIds: [],
    linkedEvidenceIds: [],
    remarks: `Standard procedure rolled forward from ${sourceStore.financialPeriod}.`
  }));

  // 4. Carry forward recurring findings as open risks
  const carryForwardActivities: AuditActivity[] = [
    {
      id: `ACT-ROLL-${Date.now().toString().slice(-4)}`,
      planId: newPlanId,
      timestamp: new Date().toISOString(),
      user: 'Lead Auditor',
      action: 'Audit Roll-Forward Executed',
      details: `Rolled forward audit plan, materiality policies, and ${rolledRisks.length} recurring risk areas from ${sourceStore.financialPeriod} to ${targetFinancialYear}.`
    }
  ];

  return {
    auditPlan: rolledPlan,
    auditRisks: rolledRisks,
    auditProcedures: rolledProcedures,
    auditActivities: carryForwardActivities,
    auditEvidence: [],
    evidenceRequests: [],
    workingPapers: [],
    auditAmendments: []
  };
}
