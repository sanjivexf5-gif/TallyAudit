// Pilot Audit Workflow, Data Completeness, Audit Limitations & Rule Quality Engine

export type DataReadinessStatus = 'READY' | 'READY_WITH_LIMITATIONS' | 'INCOMPLETE';

export interface DatasetCompletenessItem {
  id: string;
  name: string;
  category: 'MASTERS' | 'TRANSACTIONS' | 'STATUTORY' | 'BANKING' | 'INVENTORY';
  isAvailable: boolean;
  recordCount: number;
  lastSyncTime: string;
  notes: string;
}

export interface DataCompletenessReport {
  companyId: string;
  companyName: string;
  financialYear: string;
  evaluatedAt: string;
  readinessStatus: DataReadinessStatus;
  totalLedgers: number;
  totalVouchers: number;
  totalGstTransactions: number;
  totalTdsTransactions: number;
  totalBankTransactions: number;
  datasetItems: DatasetCompletenessItem[];
  limitationsNoted: string[];
}

export interface AuditLimitationRecord {
  id: string;
  companyId: string;
  financialYear: string;
  unavailableDataset: string;
  reason: string;
  recordedDate: string;
  recordedBy: string;
  affectedAuditAreas: string[];
  mitigationStrategy: string;
}

export interface MaterialitySpecification {
  companyId: string;
  financialYear: string;
  benchmarkFinancialValue: number; // decimal
  benchmarkBasis: string;
  overallMaterialityPercentage: number;
  overallMaterialityAmount: number;
  performanceMaterialityPercentage: number;
  performanceMaterialityAmount: number;
  clearlyTrivialPercentage: number;
  clearlyTrivialThresholdAmount: number;
  auditorJustification: string;
  isAuditorOverridden: boolean;
  approvedBy: string;
  approvedAt: string;
}

export type SamplingMethodType = 'RANDOM' | 'SYSTEMATIC' | 'TARGETED' | 'MATERIAL_ITEM' | 'MONETARY_UNIT';

export interface SamplingRunRecord {
  id: string;
  sampleCode: string;
  auditArea: string;
  method: SamplingMethodType;
  populationCount: number;
  populationValue: number;
  sampleSize: number;
  sampleTotalValue: number;
  randomSeed: number;
  highValueThreshold: number;
  exceptionsFoundCount: number;
  auditorConclusion: string;
  executedAt: string;
  executedBy: string;
}

export interface AuditRunHistoryItem {
  runId: string;
  appVersion: string;
  ruleSetVersion: string;
  executedBy: string;
  companyId: string;
  financialYear: string;
  startTime: string;
  endTime: string;
  durationSeconds: number;
  status: 'COMPLETED' | 'RUNNING' | 'PARTIALLY_COMPLETED' | 'FAILED' | 'CANCELLED';
  transactionsAnalyzed: number;
  ledgersAnalyzed: number;
  rulesExecuted: number;
  findingsCount: number;
  highPriorityCount: number;
  reviewCount: number;
  infoCount: number;
  recurringFindingsCount: number;
  isRerun: boolean;
}

export interface AuditRuleCatalogItem {
  ruleCode: string;
  ruleName: string;
  version: string;
  category: 'GST' | 'TDS' | 'VOUCHERS' | 'LEDGERS' | 'RECONCILIATION' | 'DUPLICATES' | 'JOURNALS' | 'PERIOD_END';
  severity: 'HIGH' | 'REVIEW' | 'INFO';
  whatHappened: string;
  whyFlagged: string;
  requiredData: string;
  defaultThreshold: number;
  falsePositiveControls: string;
  auditorGuidance: string;
  isEnabled: boolean;
}

export interface WorkflowStageItem {
  stepNumber: number;
  id: string;
  title: string;
  category: 'SETUP' | 'CONNECT' | 'PLANNING' | 'EXECUTION' | 'REVIEW' | 'FINALIZATION';
  status: 'COMPLETED' | 'IN_PROGRESS' | 'PENDING' | 'BLOCKED';
  progressPercentage: number;
  description: string;
  navTarget: string;
}

// Initial Real-world Demo Datasets & Definitions (DEMO / SYNTHETIC DATA ONLY)
export const initialDataCompleteness: DataCompletenessReport = {
  companyId: 'COMP-001',
  companyName: 'Apex Industrial Solutions Pvt Ltd',
  financialYear: 'FY 2025-26',
  evaluatedAt: '26-Sep-2026 10:15 AM',
  readinessStatus: 'READY_WITH_LIMITATIONS',
  totalLedgers: 412,
  totalVouchers: 14280,
  totalGstTransactions: 3840,
  totalTdsTransactions: 920,
  totalBankTransactions: 0,
  datasetItems: [
    {
      id: 'DS-01',
      name: 'Chart of Accounts & Ledgers',
      category: 'MASTERS',
      isAvailable: true,
      recordCount: 412,
      lastSyncTime: '25-Sep-2026 09:30 AM',
      notes: 'All active and inactive ledgers retrieved with parent groups.'
    },
    {
      id: 'DS-02',
      name: 'All Accounting Vouchers (Sales, Purchase, Journal, Payment, Receipt)',
      category: 'TRANSACTIONS',
      isAvailable: true,
      recordCount: 14280,
      lastSyncTime: '25-Sep-2026 09:30 AM',
      notes: 'Complete financial period ledger postings and cost center breakdowns.'
    },
    {
      id: 'DS-03',
      name: 'GST Tax Rates & Ledger Classifications',
      category: 'STATUTORY',
      isAvailable: true,
      recordCount: 3840,
      lastSyncTime: '25-Sep-2026 09:30 AM',
      notes: 'Taxable values, CGST, SGST, IGST postings and invoice dates.'
    },
    {
      id: 'DS-04',
      name: 'TDS Section Rates & Deductions',
      category: 'STATUTORY',
      isAvailable: true,
      recordCount: 920,
      lastSyncTime: '25-Sep-2026 09:30 AM',
      notes: 'Sections 194C, 194J, 194I, 194Q records with PAN verification flags.'
    },
    {
      id: 'DS-05',
      name: 'Direct Electronic Bank Statement Feed',
      category: 'BANKING',
      isAvailable: false,
      recordCount: 0,
      lastSyncTime: 'Not Synchronized',
      notes: 'Bank MT940 / CSV statements not connected. Relying on Tally Bank Ledgers.'
    }
  ],
  limitationsNoted: [
    'Direct Bank statement feed unavailable; audit procedures rely on Tally Book ledger entries & BRS reconciliation memo.'
  ]
};

export const initialAuditLimitations: AuditLimitationRecord[] = [
  {
    id: 'LIM-001',
    companyId: 'COMP-001',
    financialYear: 'FY 2025-26',
    unavailableDataset: 'Direct Electronic Bank Feed (MT940 / CAMT)',
    reason: 'Client bank API credentials not integrated for pilot audit test run.',
    recordedDate: '25-Sep-2026',
    recordedBy: 'Senior Statutory Auditor',
    affectedAuditAreas: ['Bank Balance Verification', 'Unpresented Cheques BRS', 'Interest Direct Debit Checks'],
    mitigationStrategy: 'Substantive sample testing against certified PDF bank statements and month-end manual BRS working papers.'
  }
];

export const initialMateriality: MaterialitySpecification = {
  companyId: 'COMP-001',
  financialYear: 'FY 2025-26',
  benchmarkFinancialValue: 185000000.00, // 18.50 Crore Turnover
  benchmarkBasis: 'Turnover / Revenue from Operations (SA 320 Guidelines)',
  overallMaterialityPercentage: 1.0,
  overallMaterialityAmount: 1850000.00, // 18.5 Lakhs
  performanceMaterialityPercentage: 75.0,
  performanceMaterialityAmount: 1387500.00, // 13.875 Lakhs
  clearlyTrivialPercentage: 5.0,
  clearlyTrivialThresholdAmount: 92500.00, // 92,500
  auditorJustification: 'Standard 1.0% turnover benchmark adopted for trading/manufacturing entity with stable operating margins.',
  isAuditorOverridden: false,
  approvedBy: 'Engagement Partner (CA)',
  approvedAt: '25-Sep-2026'
};

export const initialSamplingRuns: SamplingRunRecord[] = [
  {
    id: 'SMP-101',
    sampleCode: 'SMP-PUR-2026-01',
    auditArea: 'Vendor Purchases > ₹5,00,000 (Substantive Vouching)',
    method: 'MATERIAL_ITEM',
    populationCount: 4210,
    populationValue: 118400000.00,
    sampleSize: 45,
    sampleTotalValue: 74200000.00,
    randomSeed: 202609,
    highValueThreshold: 500000.00,
    exceptionsFoundCount: 3,
    auditorConclusion: 'All 45 material invoices vouched against GRN and purchase orders. 3 minor GST classification exceptions flagged.',
    executedAt: '25-Sep-2026',
    executedBy: 'Audit Senior'
  },
  {
    id: 'SMP-102',
    sampleCode: 'SMP-EXP-2026-02',
    auditArea: 'Administrative & Operating Expenses (Systematic Sample)',
    method: 'SYSTEMATIC',
    populationCount: 1840,
    populationValue: 14200000.00,
    sampleSize: 30,
    sampleTotalValue: 3850000.00,
    randomSeed: 44102,
    highValueThreshold: 100000.00,
    exceptionsFoundCount: 1,
    auditorConclusion: 'Systematic interval sampling every 60th voucher. 1 cash payment over Section 40A(3) threshold identified.',
    executedAt: '25-Sep-2026',
    executedBy: 'Audit Senior'
  }
];

export const initialAuditRuns: AuditRunHistoryItem[] = [
  {
    runId: 'RUN-2026-0925-01',
    appVersion: '1.0.0 (Release)',
    ruleSetVersion: '2026.4-STATUTORY',
    executedBy: 'Senior Statutory Auditor',
    companyId: 'COMP-001',
    financialYear: 'FY 2025-26',
    startTime: '25-Sep-2026 09:45:00 AM',
    endTime: '25-Sep-2026 09:45:04 AM',
    durationSeconds: 3.8,
    status: 'COMPLETED',
    transactionsAnalyzed: 14280,
    ledgersAnalyzed: 412,
    rulesExecuted: 19,
    findingsCount: 8,
    highPriorityCount: 4,
    reviewCount: 3,
    infoCount: 1,
    recurringFindingsCount: 2,
    isRerun: false
  }
];

export const auditRuleCatalog: AuditRuleCatalogItem[] = [
  {
    ruleCode: 'GST-01',
    ruleName: 'Tax Calculation & Rate Consistency',
    version: '1.0.0',
    category: 'GST',
    severity: 'HIGH',
    whatHappened: 'Tax ledger amount does not match (Taxable Value × Specified GST Rate).',
    whyFlagged: 'Potential mathematical error or manual ledger override in Tally invoice.',
    requiredData: 'Voucher Taxable Amount, Tax Rate, Ledger Postings (CGST/SGST/IGST)',
    defaultThreshold: 5.0, // ₹5 tolerance
    falsePositiveControls: 'Tolerates rounding variance up to ₹5.00 pursuant to Section 170 CGST Act.',
    auditorGuidance: 'Review supplier tax invoice and verify whether special freight/insurance items caused tax base discrepancy.',
    isEnabled: true
  },
  {
    ruleCode: 'GST-02',
    ruleName: 'Inter-State IGST vs Intra-State (CGST+SGST) Consistency',
    version: '1.0.0',
    category: 'GST',
    severity: 'HIGH',
    whatHappened: 'CGST/SGST booked on inter-state supplier or IGST booked on intra-state supplier.',
    whyFlagged: 'Section 7/8 IGST Act violation. Incorrect tax type charged cannot be adjusted directly.',
    requiredData: 'Company State Code, Supplier GSTIN prefix, Tax Ledger Groups',
    defaultThreshold: 0.0,
    falsePositiveControls: 'Excludes SEZ units where IGST is valid regardless of state code.',
    auditorGuidance: 'Check Place of Supply (POS) recorded on invoice. Obtain credit note from vendor if tax type was wrongly billed.',
    isEnabled: true
  },
  {
    ruleCode: 'TDS-01',
    ruleName: 'Section 194C / 194J Threshold & Deduction Verification',
    version: '1.0.0',
    category: 'TDS',
    severity: 'HIGH',
    whatHappened: 'Vendor cumulative payments exceed statutory threshold without TDS deduction.',
    whyFlagged: 'Potential Section 40(a)(ia) disallowance of 30% expenditure for non-deduction of tax.',
    requiredData: 'Party Ledger Cumulative Turn, TDS Group, Section Code, PAN',
    defaultThreshold: 30000.0,
    falsePositiveControls: 'Checks for lower deduction certificates (Section 197) and Form 15G/15H submissions.',
    auditorGuidance: 'Verify whether vendor submitted 197 Nil TDS certificate or if expense falls under exempt category.',
    isEnabled: true
  },
  {
    ruleCode: 'DUP-01',
    ruleName: 'Multi-Attribute Potential Duplicate Invoice Detection',
    version: '1.0.0',
    category: 'DUPLICATES',
    severity: 'HIGH',
    whatHappened: 'Multiple purchase vouchers recorded with matching invoice number, party, and exact amount.',
    whyFlagged: 'Potential double payment to vendor or duplicate input tax credit claim.',
    requiredData: 'Invoice Reference No, Party Ledger, Voucher Date, Gross Amount',
    defaultThreshold: 0.0,
    falsePositiveControls: 'Groups items and excludes recurring monthly utility bills with distinct billing period notes.',
    auditorGuidance: 'Inspect original physical/digital vendor bills. Verify if second voucher is a duplicate entry or genuine multi-batch delivery.',
    isEnabled: true
  },
  {
    ruleCode: 'CASH-01',
    ruleName: 'Section 40A(3) Single-Day Cash Payment Ceiling (> ₹10,000)',
    version: '1.0.0',
    category: 'VOUCHERS',
    severity: 'HIGH',
    whatHappened: 'Cash payment voucher exceeding ₹10,000 to a single party in a single day.',
    whyFlagged: 'Income Tax Act Section 40A(3) disallowance of 100% expenditure paid in cash exceeding ₹10,000.',
    requiredData: 'Cash Ledger Vouchers, Party Name, Voucher Date, Amount',
    defaultThreshold: 10000.0,
    falsePositiveControls: 'Excludes Rule 6DD statutory exceptions (agricultural produce, bank holidays, government payments).',
    auditorGuidance: 'Obtain auditor justification or document Rule 6DD applicable exception clause in working papers.',
    isEnabled: true
  },
  {
    ruleCode: 'REC-01',
    ruleName: 'GST 2B vs Purchase Book 7-Bucket Reconciliation',
    version: '1.0.0',
    category: 'RECONCILIATION',
    severity: 'REVIEW',
    whatHappened: 'ITC claimed in Books not reflecting in GSTR-2B portal extract.',
    whyFlagged: 'Section 16(2)(aa) condition for ITC entitlement requires appearance in GSTR-2B.',
    requiredData: 'GSTR-2B JSON/Excel records, Purchase Register Vouchers',
    defaultThreshold: 100.0,
    falsePositiveControls: 'Permits timing differences within 3-month filing grace window.',
    auditorGuidance: 'Issue vendor follow-up memo to ensure supplier uploads missing invoice in their next GSTR-1 return.',
    isEnabled: true
  },
  {
    ruleCode: 'JRNL-01',
    ruleName: 'High-Value Year-End / Period-End Journal Review',
    version: '1.0.0',
    category: 'JOURNALS',
    severity: 'REVIEW',
    whatHappened: 'Manual journal entries exceeding performance materiality recorded in last 7 days of financial year.',
    whyFlagged: 'Review indicator for year-end window dressing, unverified provisions, or cutoff errors.',
    requiredData: 'Journal Vouchers, Voucher Date, Materiality Threshold',
    defaultThreshold: 500000.0,
    falsePositiveControls: 'Distinguishes standard depreciation & tax provision journals from manual adjustments.',
    auditorGuidance: 'Review underlying working calculation sheet, management approval, and subsequent period realization.',
    isEnabled: true
  }
];

export const standardAuditWorkflowStages: WorkflowStageItem[] = [
  {
    stepNumber: 1,
    id: 'step-login',
    title: '1. Application Login & Authentication',
    category: 'SETUP',
    status: 'COMPLETED',
    progressPercentage: 100,
    description: 'Local RBAC login authenticated as Statutory Auditor.',
    navTarget: 'security'
  },
  {
    stepNumber: 2,
    id: 'step-company',
    title: '2. Company & Financial Year Selection',
    category: 'SETUP',
    status: 'COMPLETED',
    progressPercentage: 100,
    description: 'Active context: Apex Industrial Solutions Pvt Ltd (FY 2025-26).',
    navTarget: 'companies'
  },
  {
    stepNumber: 3,
    id: 'step-tally-connect',
    title: '3. TallyPrime Loopback Connection',
    category: 'CONNECT',
    status: 'COMPLETED',
    progressPercentage: 100,
    description: 'Connected to Tally XML Server at http://127.0.0.1:9000.',
    navTarget: 'connection'
  },
  {
    stepNumber: 4,
    id: 'step-sync',
    title: '4. Local Synchronization & Data Ingestion',
    category: 'CONNECT',
    status: 'COMPLETED',
    progressPercentage: 100,
    description: 'Synchronized 14,280 vouchers, 412 ledgers into local SQLite database.',
    navTarget: 'sync'
  },
  {
    stepNumber: 5,
    id: 'step-completeness',
    title: '5. Data Completeness & Limitations Verification',
    category: 'CONNECT',
    status: 'COMPLETED',
    progressPercentage: 100,
    description: 'Ready with limitations (Direct bank feed unavailable; relying on BRS).',
    navTarget: 'pilot-workflow'
  },
  {
    stepNumber: 6,
    id: 'step-planning',
    title: '6. Audit Strategy & Planning Memo',
    category: 'PLANNING',
    status: 'COMPLETED',
    progressPercentage: 100,
    description: 'Audit plan finalized covering Sales, Purchases, GST, TDS, Cash, and Fixed Assets.',
    navTarget: 'planning'
  },
  {
    stepNumber: 7,
    id: 'step-risk-materiality',
    title: '7. Risk Assessment & Materiality Benchmarks',
    category: 'PLANNING',
    status: 'COMPLETED',
    progressPercentage: 100,
    description: 'Overall Materiality: ₹18.50L (1.0% Turnover), Performance: ₹13.88L, Trivial: ₹92.5K.',
    navTarget: 'pilot-workflow'
  },
  {
    stepNumber: 8,
    id: 'step-rule-execution',
    title: '8. Automated Statutory & Anomaly Rule Execution',
    category: 'EXECUTION',
    status: 'COMPLETED',
    progressPercentage: 100,
    description: '19 audit rules evaluated across 14,280 vouchers in 3.8 seconds.',
    navTarget: 'audit'
  },
  {
    stepNumber: 9,
    id: 'step-gst-audit',
    title: '9. GST Audit & Rate Consistency Review',
    category: 'EXECUTION',
    status: 'COMPLETED',
    progressPercentage: 100,
    description: 'GST tax rate calculations and inter-state classification analyzed.',
    navTarget: 'gst'
  },
  {
    stepNumber: 10,
    id: 'step-tds-audit',
    title: '10. TDS Section Rates & Non-Deduction Audit',
    category: 'EXECUTION',
    status: 'COMPLETED',
    progressPercentage: 100,
    description: 'Sections 194C, 194J, 194I threshold validations evaluated.',
    navTarget: 'tds'
  },
  {
    stepNumber: 11,
    id: 'step-duplicate-engine',
    title: '11. Multi-Attribute Duplicate Detection',
    category: 'EXECUTION',
    status: 'COMPLETED',
    progressPercentage: 100,
    description: 'Potential duplicate vendor invoices and ledger entries grouped.',
    navTarget: 'duplicates'
  },
  {
    stepNumber: 12,
    id: 'step-reconciliation',
    title: '12. Sub-Ledger & 2B Reconciliations',
    category: 'EXECUTION',
    status: 'COMPLETED',
    progressPercentage: 100,
    description: 'GSTR-2B vs Books and Ledger vs Voucher reconciliations performed.',
    navTarget: 'reconciliation'
  },
  {
    stepNumber: 13,
    id: 'step-sampling',
    title: '13. Statistical & Material Sampling Runs',
    category: 'EXECUTION',
    status: 'COMPLETED',
    progressPercentage: 100,
    description: 'Material purchase sample (45 items) and systematic expense sample (30 items) executed.',
    navTarget: 'pilot-workflow'
  },
  {
    stepNumber: 14,
    id: 'step-findings',
    title: '14. Findings Investigation & Classification',
    category: 'REVIEW',
    status: 'IN_PROGRESS',
    progressPercentage: 75,
    description: '8 total findings: 4 High, 3 Review, 1 Info. 6 reviewed, 2 pending follow-up.',
    navTarget: 'exceptions'
  },
  {
    stepNumber: 15,
    id: 'step-evidence',
    title: '15. Audit Evidence & Document Register',
    category: 'REVIEW',
    status: 'COMPLETED',
    progressPercentage: 90,
    description: '18 verified evidence items attached with SHA-256 integrity hashes.',
    navTarget: 'evidence'
  },
  {
    stepNumber: 16,
    id: 'step-working-papers',
    title: '16. Audit Working Papers & Observations',
    category: 'REVIEW',
    status: 'IN_PROGRESS',
    progressPercentage: 80,
    description: '24 working papers prepared across statutory audit areas.',
    navTarget: 'planning'
  },
  {
    stepNumber: 17,
    id: 'step-auditor-review',
    title: '17. Auditor Sign-Off & Review Workflow',
    category: 'REVIEW',
    status: 'IN_PROGRESS',
    progressPercentage: 70,
    description: 'Engagement Senior review in progress; partner sign-off pending finalization.',
    navTarget: 'pilot-workflow'
  },
  {
    stepNumber: 18,
    id: 'step-comparative-yoy',
    title: '18. Comparative Prior-Year Analytical Review',
    category: 'REVIEW',
    status: 'COMPLETED',
    progressPercentage: 100,
    description: 'SA 520 analytical review completed: +14.6% turnover, -20.6% tax variance audited.',
    navTarget: 'companies'
  },
  {
    stepNumber: 19,
    id: 'step-reports',
    title: '19. Audit Summary & Statutory Reports',
    category: 'FINALIZATION',
    status: 'COMPLETED',
    progressPercentage: 100,
    description: 'Executive Summary and 10 specialized audit reports ready for export.',
    navTarget: 'reports'
  },
  {
    stepNumber: 20,
    id: 'step-final-audit-file',
    title: '20. Final Audit File Compilation',
    category: 'FINALIZATION',
    status: 'IN_PROGRESS',
    progressPercentage: 85,
    description: 'Pre-finalization completeness checklist active.',
    navTarget: 'audit-file'
  },
  {
    stepNumber: 21,
    id: 'step-finalization',
    title: '21. Audit Finalization & Immutable Lock',
    category: 'FINALIZATION',
    status: 'PENDING',
    progressPercentage: 0,
    description: 'Awaiting completion of open finding follow-ups and partner sign-off.',
    navTarget: 'audit-file'
  },
  {
    stepNumber: 22,
    id: 'step-backup',
    title: '22. Post-Audit Archive & SQLite Backup',
    category: 'FINALIZATION',
    status: 'COMPLETED',
    progressPercentage: 100,
    description: 'Pre-finalization database snapshot created safely in AppData.',
    navTarget: 'security'
  }
];
