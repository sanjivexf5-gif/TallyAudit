export interface TdsRuleItem {
  ruleId: string;
  name: string;
  section: string;
  description: string;
  effectiveDate: string;
  expiryDate?: string;
  jurisdiction: string;
  version: string;
  sourceReference: string;
  severity: 'Critical' | 'High' | 'Medium' | 'Low';
  enabled: boolean;
  parameters: Record<string, any>;
}

export interface TdsVoucherLineEntry {
  entryId: string;
  ledgerName: string;
  parentGroup?: string;
  amount: number;
  isDebit: boolean;
  tdsSection?: string;
  tdsRate?: number;
  taxType?: string;
}

export interface TdsVoucherDetailInfo {
  voucherId: string;
  voucherNumber: string;
  voucherDate: string;
  voucherTypeName: string;
  referenceNumber?: string;
  totalAmount: number;
  narration?: string;
  partyLedgerName: string;
  partyPan?: string;
  entries: TdsVoucherLineEntry[];
}

export interface TdsCheckResultItem {
  resultId: string;
  ruleId: string;
  ruleName: string;
  section: string;
  voucherId?: string;
  voucherNumber?: string;
  voucherDate?: string;
  voucherTypeName?: string;
  partyLedgerName?: string;
  partyPan?: string;
  expenseLedgerName?: string;
  transactionAmount?: number;
  cumulativeVendorAmount?: number;
  sectionThreshold?: number;
  deductedTdsAmount?: number;
  expectedTdsAmount?: number;
  appliedRate?: number;
  expectedRate?: number;
  severity: 'Critical' | 'High' | 'Medium' | 'Low';
  status: 'Passed' | 'Exception' | 'Review Required - Insufficient Data';
  explanation: string;
  evidenceJson: string;
  jurisdiction: string;
  sourceReference: string;
  reviewStatus: 'Pending' | 'Reviewed' | 'False Positive' | 'Resolved';
  reviewer?: string;
  reviewerNote?: string;
  voucherDetail?: TdsVoucherDetailInfo;
}

export const initialTdsRules: TdsRuleItem[] = [
  {
    ruleId: 'TDS-CHK-01',
    name: 'Potential Statutory TDS Applicability on Inward Expense Heads',
    section: 'General (194C/J/I/H)',
    description: 'Scans all inward commercial and operating expense ledger debits to detect potential withholding tax applicability under Chapter XVII-B.',
    effectiveDate: '01-Apr-2020',
    jurisdiction: 'IN-IT-ACT',
    version: '1.2.0',
    sourceReference: 'Income Tax Act 1961 Chapter XVII-B Deduction at Source',
    severity: 'Medium',
    enabled: true,
    parameters: { MonitoredHeads: 'Professional,Legal,Consultancy,Contract,Sub-contract,Rent,Commission,Brokerage,Transport,Freight,Advertising,Technical' }
  },
  {
    ruleId: 'TDS-CHK-02',
    name: 'Single Transaction Statutory Threshold Monitoring',
    section: '194C / 194J / 194I / 194H',
    description: 'Monitors individual transaction amounts exceeding single-bill statutory thresholds (e.g. ₹30,000 for 194C/194J, ₹2,40,000 for 194I) without TDS deduction.',
    effectiveDate: '01-Apr-2020',
    jurisdiction: 'IN-IT-ACT',
    version: '1.1.0',
    sourceReference: 'Income Tax Act 1961 Sec 194C(5), 194J(1), 194I, 194H',
    severity: 'High',
    enabled: true,
    parameters: { Threshold194C_Single: 30000.0, Threshold194J: 30000.0, Threshold194I: 240000.0, Threshold194H: 15000.0 }
  },
  {
    ruleId: 'TDS-CHK-03',
    name: 'Deductee PAN Availability & Section 206AA Higher Rate Evaluation',
    section: '206AA',
    description: 'Verifies whether payees subject to withholding tax have a valid 10-character PAN on record; flags cases where deduction is not made at 20% higher statutory rate.',
    effectiveDate: '01-Apr-2010',
    jurisdiction: 'IN-IT-ACT',
    version: '1.3.0',
    sourceReference: 'Income Tax Act 1961 Sec 206AA Requirement to furnish PAN',
    severity: 'High',
    enabled: true,
    parameters: { HigherRatePercentage: 20.0, MinimumTurnover: 30000.0 }
  },
  {
    ruleId: 'TDS-CHK-04',
    name: 'TDS Duties & Taxes Ledger Chart Mapping Check',
    section: 'Accounting Classification',
    description: 'Ensures that all withholding tax ledger heads are correctly parented under Duties & Taxes (or statutory liability sub-groups) and not misclassified as direct/indirect expenses.',
    effectiveDate: '01-Apr-2020',
    jurisdiction: 'IN-IT-ACT',
    version: '1.0.0',
    sourceReference: 'Guidance Note on Tax Audit under Section 44AB / ICAI Accounting Standards',
    severity: 'Medium',
    enabled: true,
    parameters: { ExpectedParent: 'Duties & Taxes' }
  },
  {
    ruleId: 'TDS-CHK-05',
    name: 'TDS Deduction Math & Rate-Base Consistency Check',
    section: 'General Math Validation',
    description: 'Recalculates the exact mathematical TDS deduction (Applicable Rate × Taxable Base Value) and flags vouchers where the posted deduction differs beyond rounding tolerances.',
    effectiveDate: '01-Apr-2020',
    jurisdiction: 'IN-IT-ACT',
    version: '1.1.0',
    sourceReference: 'Income Tax Act 1961 Chapter XVII-B Calculation Standards',
    severity: 'High',
    enabled: true,
    parameters: { ToleranceRupees: 5.0 }
  },
  {
    ruleId: 'TDS-CHK-06',
    name: 'Expense Type vs TDS Section Classification Consistency',
    section: '194C vs 194J vs 194I',
    description: 'Cross-checks expense classifications against applied TDS heads (e.g., Professional Fees deducted under 194C Contractor at 1%/2% instead of 194J Technical/Professional at 10%).',
    effectiveDate: '01-Apr-2020',
    jurisdiction: 'IN-IT-ACT',
    version: '1.2.0',
    sourceReference: 'Income Tax Act 1961 Sec 194C vs Sec 194J Scope of Works',
    severity: 'High',
    enabled: true,
    parameters: { FlagProfessionalUnderContractor: true }
  },
  {
    ruleId: 'TDS-CHK-07',
    name: 'Payee Cumulative Financial Year Aggregate Threshold Analysis',
    section: '194C(5) / 194Q',
    description: 'Aggregates multi-voucher transaction totals per vendor across the financial year to detect when cumulative amounts breach statutory thresholds (e.g. ₹1,00,000 for 194C, ₹50,00,000 for 194Q).',
    effectiveDate: '01-Apr-2020',
    jurisdiction: 'IN-IT-ACT',
    version: '1.1.0',
    sourceReference: 'Income Tax Act 1961 Sec 194C(5) Aggregate Threshold & Sec 194Q',
    severity: 'High',
    enabled: true,
    parameters: { ContractAggregateThreshold: 100000.0, PurchaseAggregateThreshold: 5000000.0 }
  },
  {
    ruleId: 'TDS-CHK-08',
    name: 'Expense Head Category-Wise Annual TDS Audit',
    section: 'Expense Portfolio',
    description: 'Analyzes annual debit turnovers on high-spend expense categories requiring withholding and flags ledger groups with 0% total tax deducted.',
    effectiveDate: '01-Apr-2020',
    jurisdiction: 'IN-IT-ACT',
    version: '1.0.0',
    sourceReference: 'Income Tax Act 1961 Section 40(a)(ia) Disallowance of Expenses',
    severity: 'Medium',
    enabled: true,
    parameters: { MinCategoryExpense: 150000.0 }
  },
  {
    ruleId: 'TDS-CHK-09',
    name: 'TDS Payable Liability vs Government Remittance Verification',
    section: 'Chapter XVII-B / Sec 200(1)',
    description: 'Tracks monthly cumulative credit accumulations in TDS payable ledgers versus challan payments (Challan 281) to identify potential late deposit or unremitted tax liabilities.',
    effectiveDate: '01-Apr-2020',
    jurisdiction: 'IN-IT-ACT',
    version: '1.0.0',
    sourceReference: 'Income Tax Act 1961 Sec 200(1) & Rule 30 Time of Payment',
    severity: 'High',
    enabled: true,
    parameters: { DueDateGraceDays: 7 }
  },
  {
    ruleId: 'TDS-CHK-10',
    name: 'Missing Withholding Tax Entries on High-Value Contractor / Service Bills',
    section: '194C / 194J High-Value',
    description: 'Flags high-value service, subcontracting, and professional invoices (e.g. > ₹1,00,000) entered into accounts with 0% TDS line items.',
    effectiveDate: '01-Apr-2020',
    jurisdiction: 'IN-IT-ACT',
    version: '1.1.0',
    sourceReference: 'Income Tax Act 1961 Section 194C & Section 194J Mandatory Deduction',
    severity: 'High',
    enabled: true,
    parameters: { HighValueThreshold: 100000.0 }
  },
  {
    ruleId: 'TDS-CHK-11',
    name: 'Unusual or Non-Statutory TDS Deduction Rate Pattern',
    section: 'Statutory Tariff Schedule',
    description: 'Detects arbitrary or non-statutory fractional TDS rates applied to invoices (standard statutory rates: 0.1%, 1%, 2%, 5%, 10%, 20%, 30%).',
    effectiveDate: '01-Apr-2020',
    jurisdiction: 'IN-IT-ACT',
    version: '1.0.0',
    sourceReference: 'Income Tax Act 1961 Chapter XVII-B Statutory Rate Schedule',
    severity: 'Medium',
    enabled: true,
    parameters: { ValidRates: '0.1,1,2,3.75,5,7.5,10,20,30' }
  },
  {
    ruleId: 'TDS-CHK-12',
    name: 'TDS Debit Reversal & Manual Tax Adjustment Anomaly',
    section: 'Journal Reversals',
    description: 'Detects debit entries or manual credit reversals posted into TDS liability ledgers outside of official government challan payment vouchers.',
    effectiveDate: '01-Apr-2020',
    jurisdiction: 'IN-IT-ACT',
    version: '1.0.0',
    sourceReference: 'Income Tax Rules 1962 / Tax Audit Guidance Note',
    severity: 'High',
    enabled: true,
    parameters: { RequireChallanForDebits: true }
  },
  {
    ruleId: 'TDS-CHK-13',
    name: 'Threshold Border & Invoice Splitting Pattern Analysis',
    section: '194C / 194J',
    description: 'Detects recurring clusters of invoices issued just below statutory TDS thresholds (e.g., between ₹27,000 and ₹29,999) to highlight potential artificial threshold circumvention.',
    effectiveDate: '01-Apr-2020',
    jurisdiction: 'IN-IT-ACT',
    version: '1.0.0',
    sourceReference: 'Audit Standards on Fraud & Anti-Circumvention (SA 240 / Section 194C)',
    severity: 'Medium',
    enabled: true,
    parameters: { LowerBound: 27000.0, UpperBound: 29999.0, ClusterCountThreshold: 2 }
  }
];

export const initialTdsResults: TdsCheckResultItem[] = [
  {
    resultId: 'TDS-RES-001',
    ruleId: 'TDS-CHK-02',
    ruleName: 'Single Transaction Statutory Threshold Monitoring',
    section: '194C',
    voucherId: 'V-TDS-01',
    voucherNumber: 'PUR-TDS-01',
    voucherDate: '10-May-2025',
    voucherTypeName: 'Purchase',
    partyLedgerName: 'Apex Transport Contractors',
    partyPan: 'AABCA9999K',
    expenseLedgerName: 'Freight & Transport Charges',
    transactionAmount: 45000,
    sectionThreshold: 30000,
    deductedTdsAmount: 0,
    expectedTdsAmount: 900,
    appliedRate: 0,
    expectedRate: 2.0,
    severity: 'High',
    status: 'Exception',
    explanation: 'Transaction amount ₹45,000.00 on expense head "Freight & Transport Charges" exceeds the Section 194C single-bill statutory threshold of ₹30,000.00 without recorded TDS withholding in voucher PUR-TDS-01.',
    evidenceJson: '{"Section": "194C", "Threshold": 30000.00, "TransactionAmount": 45000.00, "VoucherNumber": "PUR-TDS-01", "ExpectedRate": "2%", "ExpectedTDS": 900.00}',
    jurisdiction: 'IN-IT-ACT',
    sourceReference: 'Income Tax Act 1961 Sec 194C(5)',
    reviewStatus: 'Pending',
    voucherDetail: {
      voucherId: 'V-TDS-01',
      voucherNumber: 'PUR-TDS-01',
      voucherDate: '10-May-2025',
      voucherTypeName: 'Purchase',
      referenceNumber: 'INV-101',
      totalAmount: 45000,
      narration: 'Freight and cartage charges for raw material transport from Pune factory',
      partyLedgerName: 'Apex Transport Contractors',
      partyPan: 'AABCA9999K',
      entries: [
        { entryId: 'E1', ledgerName: 'Freight & Transport Charges', parentGroup: 'Direct Expenses', amount: 45000, isDebit: true, tdsSection: '194C', tdsRate: 2.0 },
        { entryId: 'E2', ledgerName: 'Apex Transport Contractors', parentGroup: 'Sundry Creditors', amount: -45000, isDebit: false }
      ]
    }
  },
  {
    resultId: 'TDS-RES-002',
    ruleId: 'TDS-CHK-03',
    ruleName: 'Deductee PAN Availability & Section 206AA Higher Rate Evaluation',
    section: '206AA',
    voucherId: 'V-TDS-02',
    voucherNumber: 'PUR-TDS-02',
    voucherDate: '15-May-2025',
    voucherTypeName: 'Purchase',
    partyLedgerName: 'Unregistered Technical Experts',
    partyPan: 'MISSING',
    expenseLedgerName: 'Technical Consultancy Services',
    transactionAmount: 85000,
    deductedTdsAmount: 0,
    expectedTdsAmount: 17000,
    appliedRate: 0,
    expectedRate: 20.0,
    severity: 'High',
    status: 'Exception',
    explanation: 'Payee ledger "Unregistered Technical Experts" with transaction movement of ₹85,000.00 lacks a valid 10-character PAN on record. Section 206AA requires tax withholding at the higher rate of 20.00%.',
    evidenceJson: '{"Payee": "Unregistered Technical Experts", "PAN": null, "Amount": 85000.00, "HigherRate": "20%", "RequiredTDS": 17000.00}',
    jurisdiction: 'IN-IT-ACT',
    sourceReference: 'Income Tax Act 1961 Sec 206AA',
    reviewStatus: 'Pending',
    voucherDetail: {
      voucherId: 'V-TDS-02',
      voucherNumber: 'PUR-TDS-02',
      voucherDate: '15-May-2025',
      voucherTypeName: 'Purchase',
      referenceNumber: 'INV-102',
      totalAmount: 85000,
      narration: 'Technical consulting and design evaluation work without deductee PAN',
      partyLedgerName: 'Unregistered Technical Experts',
      partyPan: undefined,
      entries: [
        { entryId: 'E1', ledgerName: 'Technical Consultancy Services', parentGroup: 'Indirect Expenses', amount: 85000, isDebit: true, tdsSection: '194J', tdsRate: 20.0 },
        { entryId: 'E2', ledgerName: 'Unregistered Technical Experts', parentGroup: 'Sundry Creditors', amount: -85000, isDebit: false }
      ]
    }
  },
  {
    resultId: 'TDS-RES-003',
    ruleId: 'TDS-CHK-05',
    ruleName: 'TDS Deduction Math & Rate-Base Consistency Check',
    section: '194J',
    voucherId: 'V-TDS-03',
    voucherNumber: 'PUR-TDS-03',
    voucherDate: '20-May-2025',
    voucherTypeName: 'Purchase',
    partyLedgerName: 'Legal & Tax Associates LLP',
    partyPan: 'AACCL8888M',
    expenseLedgerName: 'Legal & Professional Fees',
    transactionAmount: 50000,
    deductedTdsAmount: 2000,
    expectedTdsAmount: 5000,
    appliedRate: 4.0,
    expectedRate: 10.0,
    severity: 'High',
    status: 'Exception',
    explanation: 'Mathematical discrepancy detected in voucher PUR-TDS-03: Base professional fee of ₹50,000.00 at standard rate 10.00% yields expected TDS of ₹5,000.00, but actual deduction posted was ₹2,000.00 (Short deduction: ₹3,000.00).',
    evidenceJson: '{"BaseAmount": 50000.00, "ConfiguredRate": "10%", "ExpectedTDS": 5000.00, "ActualDeduction": 2000.00, "ShortDeduction": 3000.00}',
    jurisdiction: 'IN-IT-ACT',
    sourceReference: 'Income Tax Act 1961 Sec 194J Calculation Standards',
    reviewStatus: 'Pending',
    voucherDetail: {
      voucherId: 'V-TDS-03',
      voucherNumber: 'PUR-TDS-03',
      voucherDate: '20-May-2025',
      voucherTypeName: 'Purchase',
      referenceNumber: 'INV-103',
      totalAmount: 48000,
      narration: 'Retainership legal advisory fee for April-May 2025',
      partyLedgerName: 'Legal & Tax Associates LLP',
      partyPan: 'AACCL8888M',
      entries: [
        { entryId: 'E1', ledgerName: 'Legal & Professional Fees', parentGroup: 'Indirect Expenses', amount: 50000, isDebit: true, tdsSection: '194J', tdsRate: 10.0 },
        { entryId: 'E2', ledgerName: 'TDS on Professional Fees @ 10% (194J)', parentGroup: 'Duties & Taxes', amount: -2000, isDebit: false, tdsSection: '194J' },
        { entryId: 'E3', ledgerName: 'Legal & Tax Associates LLP', parentGroup: 'Sundry Creditors', amount: -48000, isDebit: false }
      ]
    }
  },
  {
    resultId: 'TDS-RES-004',
    ruleId: 'TDS-CHK-06',
    ruleName: 'Expense Type vs TDS Section Classification Consistency',
    section: '194C vs 194J',
    voucherId: 'V-TDS-04',
    voucherNumber: 'PUR-TDS-04',
    voucherDate: '01-Jun-2025',
    voucherTypeName: 'Purchase',
    partyLedgerName: 'Legal & Tax Associates LLP',
    partyPan: 'AACCL8888M',
    expenseLedgerName: 'Legal & Professional Fees',
    transactionAmount: 60000,
    deductedTdsAmount: 1200,
    expectedTdsAmount: 6000,
    appliedRate: 2.0,
    expectedRate: 10.0,
    severity: 'High',
    status: 'Exception',
    explanation: 'Possible Section misclassification: Expense head "Legal & Professional Fees" (Professional nature) has withholding tax deducted under "TDS on Contract @ 2% (194C)" in voucher PUR-TDS-04. Section 194J governs professional fees at 10%.',
    evidenceJson: '{"ExpenseNature": "Professional Fees", "AppliedHead": "TDS on Contract @ 2% (194C)", "RequiredSection": "194J (10%)", "Difference": 4800.00}',
    jurisdiction: 'IN-IT-ACT',
    sourceReference: 'Income Tax Act 1961 Sec 194C vs Sec 194J',
    reviewStatus: 'Pending',
    voucherDetail: {
      voucherId: 'V-TDS-04',
      voucherNumber: 'PUR-TDS-04',
      voucherDate: '01-Jun-2025',
      voucherTypeName: 'Purchase',
      referenceNumber: 'INV-104',
      totalAmount: 58800,
      narration: 'Contract legal support for arbitration proceedings',
      partyLedgerName: 'Legal & Tax Associates LLP',
      partyPan: 'AACCL8888M',
      entries: [
        { entryId: 'E1', ledgerName: 'Legal & Professional Fees', parentGroup: 'Indirect Expenses', amount: 60000, isDebit: true, tdsSection: '194J' },
        { entryId: 'E2', ledgerName: 'TDS on Contract @ 2% (194C)', parentGroup: 'Duties & Taxes', amount: -1200, isDebit: false, tdsSection: '194C' },
        { entryId: 'E3', ledgerName: 'Legal & Tax Associates LLP', parentGroup: 'Sundry Creditors', amount: -58800, isDebit: false }
      ]
    }
  },
  {
    resultId: 'TDS-RES-005',
    ruleId: 'TDS-CHK-01',
    ruleName: 'Potential Statutory TDS Applicability on Inward Expense Heads',
    section: '194C',
    voucherId: 'V-TDS-05',
    voucherNumber: 'PUR-TDS-15',
    voucherDate: '12-Jun-2025',
    voucherTypeName: 'Purchase',
    partyLedgerName: 'Counter Local Repair Servicing',
    expenseLedgerName: 'Plant Machinery Repair & Maintenance',
    transactionAmount: 28000,
    severity: 'Low',
    status: 'Review Required - Insufficient Data',
    explanation: 'Review Required - Insufficient Data: Nature of service on ledger "Plant Machinery Repair & Maintenance" (₹28,000.00) indicates potential TDS applicability, but deductee PAN and specific contract terms are unavailable in Tally records.',
    evidenceJson: '{"MissingFields": ["PAN", "ContractClassification"], "ExpenseHead": "Plant Machinery Repair & Maintenance", "Amount": 28000.00, "Status": "Review Required - Insufficient Data"}',
    jurisdiction: 'IN-IT-ACT',
    sourceReference: 'Income Tax Act 1961 Chapter XVII-B',
    reviewStatus: 'Pending',
    voucherDetail: {
      voucherId: 'V-TDS-05',
      voucherNumber: 'PUR-TDS-15',
      voucherDate: '12-Jun-2025',
      voucherTypeName: 'Purchase',
      totalAmount: 28000,
      narration: 'Annual machinery overhaul and motor rewinding work',
      partyLedgerName: 'Counter Local Repair Servicing',
      entries: [
        { entryId: 'E1', ledgerName: 'Plant Machinery Repair & Maintenance', parentGroup: 'Direct Expenses', amount: 28000, isDebit: true },
        { entryId: 'E2', ledgerName: 'Counter Local Repair Servicing', parentGroup: 'Sundry Creditors', amount: -28000, isDebit: false }
      ]
    }
  },
  {
    resultId: 'TDS-RES-006',
    ruleId: 'TDS-CHK-07',
    ruleName: 'Payee Cumulative Financial Year Aggregate Threshold Analysis',
    section: '194C(5)',
    voucherNumber: 'PUR-TDS-22',
    voucherDate: '18-Jul-2025',
    partyLedgerName: 'Apex Transport Contractors',
    partyPan: 'AABCA9999K',
    cumulativeVendorAmount: 145000,
    sectionThreshold: 100000,
    severity: 'High',
    status: 'Exception',
    explanation: 'Cumulative financial year transactions with payee "Apex Transport Contractors" total ₹1,45,000.00 across 4 vouchers, breaching aggregate statutory threshold (₹1,00,000.00) without recorded TDS withholding under Section 194C(5).',
    evidenceJson: '{"Payee": "Apex Transport Contractors", "CumulativeTotal": 145000.00, "VoucherCount": 4, "Threshold": 100000.00, "Section": "194C(5)"}',
    jurisdiction: 'IN-IT-ACT',
    sourceReference: 'Income Tax Act 1961 Sec 194C(5)',
    reviewStatus: 'Pending'
  },
  {
    resultId: 'TDS-RES-007',
    ruleId: 'TDS-CHK-13',
    ruleName: 'Threshold Border & Invoice Splitting Pattern Analysis',
    section: '194C',
    voucherNumber: 'PUR-TDS-08 / PUR-TDS-09',
    partyLedgerName: 'Apex Transport Contractors',
    transactionAmount: 57500,
    sectionThreshold: 30000,
    severity: 'Medium',
    status: 'Exception',
    explanation: 'Payee "Apex Transport Contractors" has 2 invoices entered in the border threshold band (₹27,000.00 - ₹29,999.00) totaling ₹57,500.00 (Vouchers: PUR-TDS-08 ₹28,500, PUR-TDS-09 ₹29,000). Review required to evaluate potential invoice splitting.',
    evidenceJson: '{"Payee": "Apex Transport Contractors", "InvoicesInBand": 2, "TotalAmount": 57500.00, "Vouchers": ["PUR-TDS-08", "PUR-TDS-09"]}',
    jurisdiction: 'IN-IT-ACT',
    sourceReference: 'Audit Standards on Anti-Circumvention (SA 240 / Section 194C)',
    reviewStatus: 'Pending'
  },
  {
    resultId: 'TDS-RES-008',
    ruleId: 'TDS-CHK-11',
    ruleName: 'Unusual or Non-Statutory TDS Deduction Rate Pattern',
    section: 'Statutory Tariff',
    voucherId: 'V-TDS-06',
    voucherNumber: 'PUR-TDS-06',
    voucherDate: '20-Jun-2025',
    voucherTypeName: 'Purchase',
    partyLedgerName: 'Apex Transport Contractors',
    partyPan: 'AABCA9999K',
    transactionAmount: 40000,
    deductedTdsAmount: 3000,
    appliedRate: 7.5,
    severity: 'Medium',
    status: 'Exception',
    explanation: 'Unusual withholding tax rate detected in voucher PUR-TDS-06: Effective deduction rate is 7.50% (TDS: ₹3,000.00 on Base: ₹40,000.00), which does not correspond to standard statutory rates (0.1%, 1%, 2%, 5%, 10%, 20%).',
    evidenceJson: '{"BaseAmount": 40000.00, "TdsAmount": 3000.00, "CalculatedRate": 7.50, "Voucher": "PUR-TDS-06"}',
    jurisdiction: 'IN-IT-ACT',
    sourceReference: 'Income Tax Act 1961 Chapter XVII-B',
    reviewStatus: 'Pending'
  },
  {
    resultId: 'TDS-RES-009',
    ruleId: 'TDS-CHK-12',
    ruleName: 'TDS Debit Reversal & Manual Tax Adjustment Anomaly',
    section: 'Journal Reversals',
    voucherId: 'V-TDS-07',
    voucherNumber: 'JRN-TDS-01',
    voucherDate: '01-Jul-2025',
    voucherTypeName: 'Journal',
    partyLedgerName: 'TDS on Contract @ 2% (194C)',
    transactionAmount: 5000,
    severity: 'High',
    status: 'Exception',
    explanation: 'Irregular TDS liability reduction posted via non-payment voucher JRN-TDS-01 (Journal) of amount ₹5,000.00 on ledger "TDS on Contract @ 2% (194C)". TDS liabilities should only be discharged through statutory government challans.',
    evidenceJson: '{"VoucherType": "Journal", "VoucherNumber": "JRN-TDS-01", "ReversalAmount": 5000.00, "Narration": "Manual liability write-off"}',
    jurisdiction: 'IN-IT-ACT',
    sourceReference: 'Income Tax Rules 1962 / Tax Audit Guidance Note',
    reviewStatus: 'Pending'
  },
  {
    resultId: 'TDS-RES-010',
    ruleId: 'TDS-CHK-04',
    ruleName: 'TDS Duties & Taxes Ledger Chart Mapping Check',
    section: 'Accounting Classification',
    partyLedgerName: 'TDS Payable (Misplaced Group)',
    transactionAmount: 18500,
    severity: 'Medium',
    status: 'Exception',
    explanation: 'TDS withholding ledger "TDS Payable (Misplaced Group)" is mapped under group "Indirect Expenses" instead of the standard statutory "Duties & Taxes" or "Current Liabilities" parent hierarchy.',
    evidenceJson: '{"Ledger": "TDS Payable (Misplaced Group)", "CurrentParent": "Indirect Expenses", "ExpectedParent": "Duties & Taxes"}',
    jurisdiction: 'IN-IT-ACT',
    sourceReference: 'Guidance Note on Tax Audit under Section 44AB',
    reviewStatus: 'Pending'
  },
  {
    resultId: 'TDS-RES-011',
    ruleId: 'TDS-CHK-01',
    ruleName: 'Potential Statutory TDS Applicability on Inward Expense Heads',
    section: 'General',
    voucherId: 'V-PASS-TDS',
    voucherNumber: 'PUR-TDS-10',
    voucherDate: '25-Jun-2025',
    voucherTypeName: 'Purchase',
    partyLedgerName: 'Legal & Tax Associates LLP',
    partyPan: 'AACCL8888M',
    transactionAmount: 75000,
    deductedTdsAmount: 7500,
    appliedRate: 10.0,
    expectedRate: 10.0,
    severity: 'Low',
    status: 'Passed',
    explanation: 'Withholding tax check passed successfully. Standard 10% TDS (₹7,500.00) correctly deducted on professional fee voucher PUR-TDS-10 against valid PAN AACCL8888M.',
    evidenceJson: '{"Status": "Compliant", "BaseAmount": 75000.00, "TdsDeducted": 7500.00, "Rate": 10.0, "PAN": "AACCL8888M"}',
    jurisdiction: 'IN-IT-ACT',
    sourceReference: 'Income Tax Act 1961 Sec 194J',
    reviewStatus: 'Reviewed'
  }
];
