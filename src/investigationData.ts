import { WorkspaceExceptionItem } from './workspaceData';

export type InvestigationStatus = 
  | 'Open'
  | 'Investigating'
  | 'AwaitingEvidence'
  | 'AwaitingManagementResponse'
  | 'Resolved'
  | 'NotResolved'
  | 'Accepted'
  | 'Escalated';

export type RootCauseClassification = 
  | 'DataEntry'
  | 'MasterDataIssue'
  | 'Configuration'
  | 'ProcessControlWeakness'
  | 'TimingCutoff'
  | 'TaxTreatment'
  | 'Duplicate'
  | 'ReconciliationDifference'
  | 'Other'
  | 'Unknown';

export interface InvestigationChecklistItem {
  id: string;
  code: string;
  description: string;
  isCompleted: boolean;
  completedAt: string | null;
  completedBy: string | null;
  notes: string;
}

export interface InvestigationHistoryEntry {
  id: string;
  timestamp: string;
  action: string;
  fromStatus?: InvestigationStatus;
  toStatus?: InvestigationStatus;
  user: string;
  reason?: string;
  notes?: string;
}

export interface ExceptionInvestigation {
  id: string;
  exceptionId: string;
  companyId: string;
  companyName: string;
  financialPeriodId: string;
  auditRunId: string;
  status: InvestigationStatus;
  rootCause: RootCauseClassification;
  auditorNotes: string;
  managementResponse: string;
  proposedCorrectiveAction: string;
  reviewerNotes: string;
  createdAt: string;
  updatedAt: string;
  closedAt: string | null;
  createdBy: string;
  updatedBy: string;
  checklist: InvestigationChecklistItem[];
  history: InvestigationHistoryEntry[];
}

export const ROOT_CAUSE_OPTIONS: Array<{ value: RootCauseClassification; label: string; description: string }> = [
  { value: 'DataEntry', label: 'Data Entry', description: 'Clerical, typo, or manual journal slip during transaction recording.' },
  { value: 'MasterDataIssue', label: 'Master Data Issue', description: 'Missing GSTIN, incorrect PAN, wrong ledger parent group, or invalid HSN in master.' },
  { value: 'Configuration', label: 'Configuration', description: 'ERP default parameters, automated rounding, or voucher numbering scheme setup anomaly.' },
  { value: 'ProcessControlWeakness', label: 'Process Control Weakness', description: 'Lack of supervisory review, absent segregation of duties, or missing invoice matching.' },
  { value: 'TimingCutoff', label: 'Timing / Cut-off', description: 'Goods received note prior to bill, delayed GRN, or end-of-period closing lag.' },
  { value: 'TaxTreatment', label: 'Tax Treatment', description: 'Section 17(5) blocked credit, inverted duty structure, or Sec 194C/J threshold miscalculation.' },
  { value: 'Duplicate', label: 'Duplicate Booking', description: 'Same invoice posted twice under distinct voucher numbers or supplier bill references.' },
  { value: 'ReconciliationDifference', label: 'Reconciliation Difference', description: 'Timing difference or variance between internal ledger and portal returns (GSTR-2B / 26AS).' },
  { value: 'Other', label: 'Other Auditor Finding', description: 'Specific scenario categorized by senior auditor documented in working papers.' },
  { value: 'Unknown', label: 'Unknown / Under Scrutiny', description: 'Initial preliminary state awaiting corroborative evidence and management representation.' }
];

export const STATUS_METADATA: Record<InvestigationStatus, { label: string; badgeClass: string; description: string }> = {
  Open: {
    label: 'Open',
    badgeClass: 'bg-blue-950 text-blue-300 border-blue-800',
    description: 'Investigation initiated; initial working paper created.'
  },
  Investigating: {
    label: 'Investigating',
    badgeClass: 'bg-amber-950 text-amber-300 border-amber-800',
    description: 'Active auditor scrutiny underway; reviewing vouchers, ledgers, and master data.'
  },
  AwaitingEvidence: {
    label: 'Awaiting Evidence',
    badgeClass: 'bg-sky-950 text-sky-300 border-sky-800',
    description: 'External documentation, bank advice, or vendor statement requested.'
  },
  AwaitingManagementResponse: {
    label: 'Awaiting Management Response',
    badgeClass: 'bg-purple-950 text-purple-300 border-purple-800',
    description: 'Formal audit query submitted to client finance team for written explanation.'
  },
  Resolved: {
    label: 'Resolved',
    badgeClass: 'bg-emerald-950 text-emerald-300 border-emerald-800',
    description: 'Investigation completed; rectification journal posted or satisfactory evidence documented.'
  },
  NotResolved: {
    label: 'Not Resolved',
    badgeClass: 'bg-red-950 text-red-300 border-red-800',
    description: 'Unresolved discrepancy remaining; flagged for inclusion in statutory audit report.'
  },
  Accepted: {
    label: 'Accepted by Auditor',
    badgeClass: 'bg-teal-950 text-teal-300 border-teal-800',
    description: 'Auditor satisfied that transaction is legitimate, exempt, or immaterial.'
  },
  Escalated: {
    label: 'Escalated to Partner',
    badgeClass: 'bg-rose-950 text-rose-300 border-rose-800',
    description: 'Material control deficiency or non-compliance referred to Engagement Partner.'
  }
};

export const canTransitionInvestigation = (current: InvestigationStatus, target: InvestigationStatus): boolean => {
  if (current === target) return true;

  switch (current) {
    case 'Open':
      return target === 'Investigating';

    case 'Investigating':
      return ['AwaitingEvidence', 'AwaitingManagementResponse', 'Resolved', 'NotResolved', 'Accepted', 'Escalated'].includes(target);

    case 'AwaitingEvidence':
      return ['Investigating', 'Resolved', 'Escalated', 'AwaitingManagementResponse'].includes(target);

    case 'AwaitingManagementResponse':
      return ['Investigating', 'Resolved', 'Accepted', 'Escalated'].includes(target);

    case 'NotResolved':
      return ['Investigating', 'Escalated', 'Accepted'].includes(target);

    case 'Accepted':
      return ['Investigating', 'Resolved'].includes(target);

    case 'Resolved':
      return target === 'Investigating';

    case 'Escalated':
      return ['Investigating', 'Resolved'].includes(target);

    default:
      return false;
  }
};

export const getAllowedTransitions = (current: InvestigationStatus): InvestigationStatus[] => {
  const all: InvestigationStatus[] = [
    'Open',
    'Investigating',
    'AwaitingEvidence',
    'AwaitingManagementResponse',
    'Resolved',
    'NotResolved',
    'Accepted',
    'Escalated'
  ];
  return all.filter(s => s !== current && canTransitionInvestigation(current, s));
};

export const createInitialChecklist = (): InvestigationChecklistItem[] => [
  { id: 'chk-01', code: 'INV-CHK-01', description: 'Review source transaction', isCompleted: false, completedAt: null, completedBy: null, notes: '' },
  { id: 'chk-02', code: 'INV-CHK-02', description: 'Review related ledger', isCompleted: false, completedAt: null, completedBy: null, notes: '' },
  { id: 'chk-03', code: 'INV-CHK-03', description: 'Review related party/customer/vendor', isCompleted: false, completedAt: null, completedBy: null, notes: '' },
  { id: 'chk-04', code: 'INV-CHK-04', description: 'Review supporting evidence', isCompleted: false, completedAt: null, completedBy: null, notes: '' },
  { id: 'chk-05', code: 'INV-CHK-05', description: 'Check related vouchers', isCompleted: false, completedAt: null, completedBy: null, notes: '' },
  { id: 'chk-06', code: 'INV-CHK-06', description: 'Check related GST information', isCompleted: false, completedAt: null, completedBy: null, notes: '' },
  { id: 'chk-07', code: 'INV-CHK-07', description: 'Check related TDS information', isCompleted: false, completedAt: null, completedBy: null, notes: '' },
  { id: 'chk-08', code: 'INV-CHK-08', description: 'Check reconciliation results', isCompleted: false, completedAt: null, completedBy: null, notes: '' },
  { id: 'chk-09', code: 'INV-CHK-09', description: 'Check duplicate candidates', isCompleted: false, completedAt: null, completedBy: null, notes: '' },
  { id: 'chk-10', code: 'INV-CHK-10', description: 'Check period/cut-off', isCompleted: false, completedAt: null, completedBy: null, notes: '' },
  { id: 'chk-11', code: 'INV-CHK-11', description: 'Check master data', isCompleted: false, completedAt: null, completedBy: null, notes: '' },
  { id: 'chk-12', code: 'INV-CHK-12', description: 'Obtain additional evidence', isCompleted: false, completedAt: null, completedBy: null, notes: '' },
  { id: 'chk-13', code: 'INV-CHK-13', description: 'Obtain management response', isCompleted: false, completedAt: null, completedBy: null, notes: '' },
  { id: 'chk-14', code: 'INV-CHK-14', description: 'Perform re-check', isCompleted: false, completedAt: null, completedBy: null, notes: '' },
  { id: 'chk-15', code: 'INV-CHK-15', description: 'Record conclusion', isCompleted: false, completedAt: null, completedBy: null, notes: '' }
];

export const createDefaultInvestigation = (exception: WorkspaceExceptionItem, username: string = 'Senior Statutory Auditor'): ExceptionInvestigation => {
  const now = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) + ' ' + new Date().toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
  const invId = `INV-${exception.id.replace('EXC-', '')}-${Date.now().toString().slice(-4)}`;

  return {
    id: invId,
    exceptionId: exception.id,
    companyId: 'COMP-APEX-01',
    companyName: exception.companyName,
    financialPeriodId: 'FY-2025-26',
    auditRunId: 'RUN-2026-09',
    status: 'Open',
    rootCause: 'Unknown',
    auditorNotes: `Initial investigation opened for exception ${exception.id} flagged under Rule ${exception.ruleId} (${exception.ruleName}).\n\nPreliminary Observation:\n- Voucher ${exception.voucherNumber} dated ${exception.voucherDate} for amount ₹${exception.amount.toLocaleString()}.\n- Flagged issue: ${exception.whyFlagged}`,
    managementResponse: '',
    proposedCorrectiveAction: 'Review source transaction and post necessary rectification entry in Tally.',
    reviewerNotes: '',
    createdAt: now,
    updatedAt: now,
    closedAt: null,
    createdBy: username,
    updatedBy: username,
    checklist: createInitialChecklist(),
    history: [
      {
        id: `HIST-${Date.now()}-1`,
        timestamp: now,
        action: 'Investigation Created',
        toStatus: 'Open',
        user: username,
        notes: `Investigation workspace initialized for ${exception.id}.`
      }
    ]
  };
};

export const initialInvestigationsSeed: Record<string, ExceptionInvestigation> = {
  'EXC-1001': {
    id: 'INV-1001-2026',
    exceptionId: 'EXC-1001',
    companyId: 'COMP-APEX-01',
    companyName: 'Apex Industrial Solutions Private Limited',
    financialPeriodId: 'FY-2025-26',
    auditRunId: 'RUN-2026-09',
    status: 'Investigating',
    rootCause: 'DataEntry',
    auditorNotes: 'Identified duplicate supplier invoice number PUR/25-26/044 recorded against vendor Bharat Steel Re-rolling Mills. The vendor had re-submitted an amended bill due to revised freight charges, but the original voucher was never cancelled or reversed.',
    managementResponse: 'Accounts executive confirmed that both vouchers were entered during month-end rush. Voucher PUR-089 is genuine; PUR-088 is a duplicate draft entry pending cancellation.',
    proposedCorrectiveAction: 'Cancel voucher PUR-088 in Tally and reverse input tax credit claimed on the duplicate bill.',
    reviewerNotes: 'Audit partner approved corrective journal procedure. Ensure supplier confirmation statement is reconciled before sign-off.',
    createdAt: '10:15 AM 28-Sep-2026',
    updatedAt: '03:40 PM 29-Sep-2026',
    closedAt: null,
    createdBy: 'Senior Statutory Auditor',
    updatedBy: 'Audit Engagement Senior',
    checklist: [
      { id: 'chk-01', code: 'INV-CHK-01', description: 'Review source transaction', isCompleted: true, completedAt: '10:30 AM 28-Sep-2026', completedBy: 'Senior Auditor', notes: 'Inspected PUR-088 and PUR-089' },
      { id: 'chk-02', code: 'INV-CHK-02', description: 'Review related ledger', isCompleted: true, completedAt: '11:00 AM 28-Sep-2026', completedBy: 'Senior Auditor', notes: 'Bharat Steel ledger reflects credit balance' },
      { id: 'chk-03', code: 'INV-CHK-03', description: 'Review related party/customer/vendor', isCompleted: true, completedAt: '11:45 AM 28-Sep-2026', completedBy: 'Senior Auditor', notes: 'Vendor GSTIN active in portal' },
      { id: 'chk-04', code: 'INV-CHK-04', description: 'Review supporting evidence', isCompleted: true, completedAt: '02:15 PM 28-Sep-2026', completedBy: 'Senior Auditor', notes: 'Physical copy of bill PUR/25-26/044 verified' },
      { id: 'chk-05', code: 'INV-CHK-05', description: 'Check related vouchers', isCompleted: true, completedAt: '03:30 PM 28-Sep-2026', completedBy: 'Senior Auditor', notes: 'Matched with bank payment entry' },
      { id: 'chk-06', code: 'INV-CHK-06', description: 'Check related GST information', isCompleted: true, completedAt: '04:00 PM 28-Sep-2026', completedBy: 'Senior Auditor', notes: 'Double ITC of ₹27,000 flagged in GSTR-3B draft' },
      { id: 'chk-07', code: 'INV-CHK-07', description: 'Check related TDS information', isCompleted: true, completedAt: '04:30 PM 28-Sep-2026', completedBy: 'Senior Auditor', notes: 'No TDS applicable on goods purchase' },
      { id: 'chk-08', code: 'INV-CHK-08', description: 'Check reconciliation results', isCompleted: true, completedAt: '10:00 AM 29-Sep-2026', completedBy: 'Audit Senior', notes: 'GSTR-2B only contains one invoice #044' },
      { id: 'chk-09', code: 'INV-CHK-09', description: 'Check duplicate candidates', isCompleted: true, completedAt: '11:15 AM 29-Sep-2026', completedBy: 'Audit Senior', notes: 'ACC-DUP-02 rule generated match pair' },
      { id: 'chk-10', code: 'INV-CHK-10', description: 'Check period/cut-off', isCompleted: false, completedAt: null, completedBy: null, notes: '' },
      { id: 'chk-11', code: 'INV-CHK-11', description: 'Check master data', isCompleted: false, completedAt: null, completedBy: null, notes: '' },
      { id: 'chk-12', code: 'INV-CHK-12', description: 'Obtain additional evidence', isCompleted: false, completedAt: null, completedBy: null, notes: '' },
      { id: 'chk-13', code: 'INV-CHK-13', description: 'Obtain management response', isCompleted: true, completedAt: '02:00 PM 29-Sep-2026', completedBy: 'Audit Senior', notes: 'Written response received via email' },
      { id: 'chk-14', code: 'INV-CHK-14', description: 'Perform re-check', isCompleted: false, completedAt: null, completedBy: null, notes: '' },
      { id: 'chk-15', code: 'INV-CHK-15', description: 'Record conclusion', isCompleted: false, completedAt: null, completedBy: null, notes: '' }
    ],
    history: [
      { id: 'h-1', timestamp: '10:15 AM 28-Sep-2026', action: 'Created', toStatus: 'Open', user: 'Senior Statutory Auditor', notes: 'Investigation initiated from Exceptions Workbench.' },
      { id: 'h-2', timestamp: '10:25 AM 28-Sep-2026', action: 'Status Changed', fromStatus: 'Open', toStatus: 'Investigating', user: 'Senior Statutory Auditor', reason: 'Beginning technical voucher audit.' }
    ]
  }
};
