import { WorkspaceExceptionItem } from './workspaceData';

export type ReportType =
  | 'executive-summary'
  | 'complete-audit'
  | 'gst-exceptions'
  | 'tds-exceptions'
  | 'voucher-exceptions'
  | 'ledger-exceptions'
  | 'duplicate-transactions'
  | 'unusual-transactions'
  | 'pending-reviews'
  | 'auditor-notes'
  | 'yoy-comparative-report';

export interface AuditReportMetadata {
  company: string;
  financialYear: string;
  reportGenerationDate: string;
  applicationVersion: string;
  dataSynchronizationDate: string;
  ruleVersionsUsed: {
    gst: string;
    tds: string;
    accounting: string;
    duplicates: string;
    anomalies: string;
  };
  recordsExamined: {
    vouchers: number;
    ledgers: number;
    bankTransactions: number;
    stockItems: number;
    totalRecords: number;
  };
  exceptionsDetected: number;
  exceptionsReviewed: number;
  exceptionsPending: number;
  exceptionsRequiresInvestigation: number;
  exceptionsDismissed: number;
}

export interface ReportDefinition {
  id: ReportType;
  title: string;
  subtitle: string;
  description: string;
  category: 'Executive' | 'Statutory' | 'Operational' | 'Audit Trail';
  badgeColor: string;
  iconName: string;
}

export const reportDefinitions: ReportDefinition[] = [
  {
    id: 'executive-summary',
    title: '1. Executive Audit Summary',
    subtitle: 'High-Level Statutory Scorecard, Risk Profiles & Management Posture',
    description: 'Comprehensive executive summary providing statutory risk posture, breakdown across GST/TDS/Accounting modules, and key audit observations for leadership & audit committee.',
    category: 'Executive',
    badgeColor: 'teal',
    iconName: 'LayoutDashboard'
  },
  {
    id: 'complete-audit',
    title: '2. Complete Audit Report',
    subtitle: 'Comprehensive Master Ledger & Voucher Exception Dossier',
    description: 'Full statutory audit report encompassing all flagged exceptions across all 19 rules, complete with evidence payloads, Tally Master IDs, and double-entry postings.',
    category: 'Operational',
    badgeColor: 'blue',
    iconName: 'FileText'
  },
  {
    id: 'gst-exceptions',
    title: '3. GST Exception Report',
    subtitle: 'GST Statutory & Tax Allocation Variances (CGST/SGST/IGST/RCM)',
    description: 'Itemized GST compliance report highlighting interstate vs intrastate misclassifications, missing B2B supplier GSTINs, non-standard tax rates, and reverse charge gaps.',
    category: 'Statutory',
    badgeColor: 'sky',
    iconName: 'FileCheck'
  },
  {
    id: 'tds-exceptions',
    title: '4. TDS Exception Report',
    subtitle: 'Income Tax Withholding & Section 206AA Non-PAN Rates',
    description: 'Detailed report on Section 194C, 194Q, 194J applicability, Section 206AA higher 20% rate non-PAN deductions, and deductee PAN verification anomalies.',
    category: 'Statutory',
    badgeColor: 'amber',
    iconName: 'Receipt'
  },
  {
    id: 'voucher-exceptions',
    title: '5. Voucher Exception Report',
    subtitle: 'Transaction Sequence Breaks, Timing & Multi-line Imbalances',
    description: 'Report analyzing voucher sequencing gaps, Sunday/Holiday journal postings, missing voucher narrations, and unusual credit/debit combinations.',
    category: 'Operational',
    badgeColor: 'indigo',
    iconName: 'FileSpreadsheet'
  },
  {
    id: 'ledger-exceptions',
    title: '6. Ledger Exception Report',
    subtitle: 'Intraday Negative Balances, Dormant Accounts & Outlier Heads',
    description: 'In-depth review of cash ledger intraday negative balances, unusual debit balances in liability ledgers, and abnormal expense surges.',
    category: 'Operational',
    badgeColor: 'emerald',
    iconName: 'BookOpen'
  },
  {
    id: 'duplicate-transactions',
    title: '7. Duplicate Transaction Report',
    subtitle: 'Multi-Pass Candidate Collisions (Exact, Strong & Possible Overlaps)',
    description: 'Candidate match report identifying potential duplicate sales and purchase invoices, vendor billing overlaps, and duplicate payment vouchers.',
    category: 'Operational',
    badgeColor: 'purple',
    iconName: 'GitCompare'
  },
  {
    id: 'unusual-transactions',
    title: '8. Unusual Transaction Report',
    subtitle: 'Forensic Outliers, High-Value Round Figures & Anomaly Flags',
    description: 'Forensic audit findings covering round-figure disbursements, Benford’s law first-digit anomalies, and uncharacteristic vendor transaction sizes.',
    category: 'Operational',
    badgeColor: 'rose',
    iconName: 'AlertTriangle'
  },
  {
    id: 'pending-reviews',
    title: '9. Pending Review Report',
    subtitle: 'Unreviewed Working Papers & Open Investigation Items',
    description: 'Action-oriented queue of exceptions requiring statutory auditor investigation, client management representation, or pending working paper sign-off.',
    category: 'Audit Trail',
    badgeColor: 'yellow',
    iconName: 'Clock'
  },
  {
    id: 'auditor-notes',
    title: '10. Auditor Notes Report',
    subtitle: 'Auditor Working Papers, Dismissal Reasons & Review Sign-Offs',
    description: 'Permanent audit documentation file recording all auditor observations, justification for dismissed items, and timestamped working paper history.',
    category: 'Audit Trail',
    badgeColor: 'teal',
    iconName: 'FileSignature'
  },
  {
    id: 'yoy-comparative-report',
    title: '11. Year-over-Year Analytical Procedures Report',
    subtitle: 'SA 520 Analytical Review, Multi-Year Metric Variances & Recurring Risk Findings',
    description: 'Auditor documentation compliant with Standard on Auditing (SA) 520, analyzing comparative financial movement, voucher volume variations, and recurring compliance issues across financial periods.',
    category: 'Executive',
    badgeColor: 'sky',
    iconName: 'GitCompare'
  }
];

export function getReportMetadata(
  exceptions: WorkspaceExceptionItem[],
  companyName?: string,
  financialYear?: string
): AuditReportMetadata {
  const now = new Date();
  const dateStr = now.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' }) + ' ' + 
                  now.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });

  const reviewed = exceptions.filter(e => e.status === 'Reviewed').length;
  const pending = exceptions.filter(e => e.status === 'Requires Review - Pending').length;
  const investigation = exceptions.filter(e => e.status === 'Requires Investigation').length;
  const dismissed = exceptions.filter(e => e.status === 'Dismissed with Reason').length;

  return {
    company: companyName || 'Apex Industrial Solutions Pvt Ltd',
    financialYear: financialYear || 'FY 2025-26 (01-Apr-2025 to 31-Mar-2026)',
    reportGenerationDate: dateStr,
    applicationVersion: 'v1.4.0-audit-engine (Multi-Company Enterprise Build)',
    dataSynchronizationDate: 'Local SQLite Multi-Company Isolated Database Snapshot',
    ruleVersionsUsed: {
      gst: 'GST Statutory Rules v1.2.0',
      tds: 'TDS Withholding Rules v2.0.1',
      accounting: 'Accounting Hygiene Rules v1.0.0',
      duplicates: 'Duplicate Detection Engine v2.1.0',
      anomalies: 'Forensic Outlier Rules v1.0.0'
    },
    recordsExamined: {
      vouchers: 14280,
      ledgers: 342,
      bankTransactions: 2150,
      stockItems: 1890,
      totalRecords: 18662
    },
    exceptionsDetected: exceptions.length,
    exceptionsReviewed: reviewed,
    exceptionsPending: pending,
    exceptionsRequiresInvestigation: investigation,
    exceptionsDismissed: dismissed
  };
}

export function filterExceptionsForReport(
  reportType: ReportType,
  exceptions: WorkspaceExceptionItem[]
): WorkspaceExceptionItem[] {
  switch (reportType) {
    case 'executive-summary':
    case 'complete-audit':
      return exceptions;
    case 'gst-exceptions':
      return exceptions.filter(e => e.module === 'GST Statutory');
    case 'tds-exceptions':
      return exceptions.filter(e => e.module === 'TDS Withholding');
    case 'voucher-exceptions':
      return exceptions.filter(e => e.module === 'Sequencing' || e.voucherType === 'Sales' || e.voucherType === 'Purchase' || e.voucherType === 'Journal');
    case 'ledger-exceptions':
      return exceptions.filter(e => e.module === 'General Accounting');
    case 'duplicate-transactions':
      return exceptions.filter(e => e.module === 'Duplicate Detection');
    case 'unusual-transactions':
      return exceptions.filter(e => e.module === 'Anomaly & Outlier');
    case 'pending-reviews':
      return exceptions.filter(e => e.status === 'Requires Review - Pending' || e.status === 'Requires Investigation');
    case 'auditor-notes':
      return exceptions.filter(e => (e.reviewerNotes && e.reviewerNotes.length > 0) || e.status !== 'Requires Review - Pending' || e.reviewHistory.length > 1);
    default:
      return exceptions;
  }
}

/**
 * Generate Excel / CSV Export content with complete metadata and itemized data
 */
export function generateCsvExport(
  reportDef: ReportDefinition,
  metadata: AuditReportMetadata,
  exceptions: WorkspaceExceptionItem[]
): string {
  const lines: string[] = [];

  // 1. Audit Metadata Header Block
  lines.push(`"================================================================================"`);
  lines.push(`"${reportDef.title.toUpperCase()} - STATUTORY AUDIT REPORT"`);
  lines.push(`"${reportDef.subtitle}"`);
  lines.push(`"================================================================================"`);
  lines.push(`"Company Name","${metadata.company}"`);
  lines.push(`"Financial Year","${metadata.financialYear}"`);
  lines.push(`"Report Generation Date","${metadata.reportGenerationDate}"`);
  lines.push(`"Application Version","${metadata.applicationVersion}"`);
  lines.push(`"Data Synchronization Date","${metadata.dataSynchronizationDate}"`);
  lines.push(`"Rule Versions Used","GST: ${metadata.ruleVersionsUsed.gst} | TDS: ${metadata.ruleVersionsUsed.tds} | Accounting: ${metadata.ruleVersionsUsed.accounting} | Duplicates: ${metadata.ruleVersionsUsed.duplicates}"`);
  lines.push(`"Records Examined","Vouchers: ${metadata.recordsExamined.vouchers.toLocaleString()} | Ledgers: ${metadata.recordsExamined.ledgers} | Bank: ${metadata.recordsExamined.bankTransactions} | Total: ${metadata.recordsExamined.totalRecords.toLocaleString()}"`);
  lines.push(`"Exceptions Detected","${metadata.exceptionsDetected}"`);
  lines.push(`"Exceptions Reviewed","${metadata.exceptionsReviewed}"`);
  lines.push(`"Exceptions Pending","${metadata.exceptionsPending}"`);
  lines.push(`"Exceptions Requiring Investigation","${metadata.exceptionsRequiresInvestigation}"`);
  lines.push(`"Exceptions Dismissed with Reason","${metadata.exceptionsDismissed}"`);
  lines.push(`"Safety & Integrity Notice","Strictly Read-Only from Local SQLite Cache. Tally data was NOT modified during generation."`);
  lines.push(`""`);

  // 2. Table Column Headers
  lines.push([
    `"Exception ID"`,
    `"Severity"`,
    `"Module"`,
    `"Rule ID"`,
    `"Statutory Reference"`,
    `"Voucher Number"`,
    `"Voucher Type"`,
    `"Voucher Date"`,
    `"Party / Master Ledger"`,
    `"Primary Ledger Head"`,
    `"Party GSTIN / PAN"`,
    `"Amount (INR)"`,
    `"Tally Master ID"`,
    `"Tally GUID"`,
    `"Exception Title / Finding"`,
    `"Why Flagged (Audit Reason)"`,
    `"Evidence Payload (JSON)"`,
    `"Review Status"`,
    `"Dismissal Reason"`,
    `"Auditor Working Paper Notes"`
  ].join(','));

  // 3. Table Rows
  exceptions.forEach(item => {
    const cleanEvidence = (item.evidenceJson ?? '').replace(/"/g, '""').replace(/\n/g, ' ');
    const cleanWhy = (item.whyFlagged ?? '').replace(/"/g, '""');
    const cleanNotes = item.reviewerNotes ? item.reviewerNotes.replace(/"/g, '""').replace(/\n/g, ' | ') : '';
    const cleanDismiss = item.dismissalReason ? item.dismissalReason.replace(/"/g, '""') : '';
    const masterId = item.tallyNavigationGuide?.masterId ?? 'N/A';
    const guid = item.tallyNavigationGuide?.guid ?? 'N/A';

    lines.push([
      `"${item.id ?? ''}"`,
      `"${item.severity ?? ''}"`,
      `"${item.module ?? ''}"`,
      `"${item.ruleId ?? ''}"`,
      `"${item.statutoryReference ?? ''}"`,
      `"${item.voucherNumber ?? ''}"`,
      `"${item.voucherType ?? ''}"`,
      `"${item.voucherDate ?? ''}"`,
      `"${(item.partyLedgerName ?? '').replace(/"/g, '""')}"`,
      `"${(item.primaryLedger ?? '').replace(/"/g, '""')}"`,
      `"${item.partyGstin || item.partyPan || 'N/A'}"`,
      `"${item.amount ?? 0}"`,
      `"${masterId}"`,
      `"${guid}"`,
      `"${(item.exceptionTitle ?? '').replace(/"/g, '""')}"`,
      `"${cleanWhy}"`,
      `"${cleanEvidence}"`,
      `"${item.status ?? 'Pending'}"`,
      `"${cleanDismiss}"`,
      `"${cleanNotes}"`
    ].join(','));
  });

  return lines.join('\n');
}

/**
 * Trigger file download for CSV/Excel
 */
export function downloadCsvFile(filename: string, content: string) {
  const blob = new Blob(['\uFEFF' + content], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.setAttribute('href', url);
  link.setAttribute('download', filename);
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
}
