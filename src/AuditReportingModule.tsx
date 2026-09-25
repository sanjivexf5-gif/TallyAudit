import React, { useState } from 'react';
import {
  FileText,
  Download,
  Printer,
  FileSpreadsheet,
  Building2,
  Calendar,
  Clock,
  ShieldCheck,
  CheckCircle,
  AlertTriangle,
  FileCheck,
  Receipt,
  GitCompare,
  BookOpen,
  FileSignature,
  LayoutDashboard,
  Search,
  Filter,
  Eye,
  Info,
  Layers,
  ChevronRight,
  ChevronDown,
  Hash,
  Database,
  ExternalLink,
  RotateCcw
} from 'lucide-react';
import { WorkspaceExceptionItem } from './workspaceData';
import {
  ReportType,
  reportDefinitions,
  getReportMetadata,
  filterExceptionsForReport,
  generateCsvExport,
  downloadCsvFile,
  ReportDefinition
} from './reportData';

interface AuditReportingModuleProps {
  exceptions: WorkspaceExceptionItem[];
  onOpenDrillDown?: (exception: WorkspaceExceptionItem) => void;
  onNavigateToWorkspace?: (exceptionId?: string) => void;
}

export const AuditReportingModule: React.FC<AuditReportingModuleProps> = ({
  exceptions,
  onOpenDrillDown,
  onNavigateToWorkspace
}) => {
  const [selectedReportType, setSelectedReportType] = useState<ReportType>('executive-summary');
  const [searchQuery, setSearchQuery] = useState<string>('');
  const [severityFilter, setSeverityFilter] = useState<string>('ALL');
  const [expandedExceptionId, setExpandedExceptionId] = useState<string | null>(null);
  const [isPrintPreviewOpen, setIsPrintPreviewOpen] = useState<boolean>(false);
  const [isExporting, setIsExporting] = useState<boolean>(false);
  const [exportSuccessMessage, setExportSuccessMessage] = useState<string | null>(null);

  const activeReportDef = reportDefinitions.find(r => r.id === selectedReportType) || reportDefinitions[0];
  const metadata = getReportMetadata(exceptions);
  const filteredForReport = filterExceptionsForReport(selectedReportType, exceptions);

  const displayItems = filteredForReport.filter(item => {
    if (severityFilter !== 'ALL' && item.severity !== severityFilter) return false;
    if (searchQuery) {
      const q = searchQuery.toLowerCase();
      return (
        (item.id ?? '').toLowerCase().includes(q) ||
        (item.voucherNumber ?? '').toLowerCase().includes(q) ||
        (item.partyLedgerName ?? '').toLowerCase().includes(q) ||
        (item.primaryLedger ?? '').toLowerCase().includes(q) ||
        (item.ruleName ?? '').toLowerCase().includes(q) ||
        (item.exceptionTitle ?? '').toLowerCase().includes(q)
      );
    }
    return true;
  });

  const handleExportExcel = () => {
    setIsExporting(true);
    setTimeout(() => {
      const csvData = generateCsvExport(activeReportDef, metadata, filteredForReport);
      const safeTitle = activeReportDef.title.toLowerCase().replace(/[^a-z0-9]/g, '_');
      downloadCsvFile(`TallyAudit_${safeTitle}_${Date.now()}.csv`, csvData);
      setIsExporting(false);
      setExportSuccessMessage(`Excel/CSV file exported successfully for ${activeReportDef.title}`);
      setTimeout(() => setExportSuccessMessage(null), 3500);
    }, 400);
  };

  const handlePrintPdf = () => {
    window.print();
  };

  const getReportIcon = (name: string) => {
    switch (name) {
      case 'LayoutDashboard': return <LayoutDashboard className="w-4 h-4 text-teal-400" />;
      case 'FileText': return <FileText className="w-4 h-4 text-blue-400" />;
      case 'FileCheck': return <FileCheck className="w-4 h-4 text-sky-400" />;
      case 'Receipt': return <Receipt className="w-4 h-4 text-amber-400" />;
      case 'FileSpreadsheet': return <FileSpreadsheet className="w-4 h-4 text-indigo-400" />;
      case 'BookOpen': return <BookOpen className="w-4 h-4 text-emerald-400" />;
      case 'GitCompare': return <GitCompare className="w-4 h-4 text-purple-400" />;
      case 'AlertTriangle': return <AlertTriangle className="w-4 h-4 text-rose-400" />;
      case 'Clock': return <Clock className="w-4 h-4 text-yellow-400" />;
      case 'FileSignature': return <FileSignature className="w-4 h-4 text-teal-400" />;
      default: return <FileText className="w-4 h-4 text-teal-400" />;
    }
  };

  return (
    <div className="space-y-4 max-w-7xl mx-auto h-full flex flex-col">
      {/* Module Title & Export Actions Bar */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 pb-3 border-b border-slate-800">
        <div>
          <div className="flex items-center gap-2">
            <h2 className="text-xl font-black text-white tracking-tight flex items-center gap-2">
              <FileText className="w-5 h-5 text-teal-400" />
              <span>Statutory Audit Reporting Module</span>
            </h2>
            <span className="text-[10px] bg-teal-950 text-teal-300 font-mono px-2 py-0.5 rounded border border-teal-800">
              10 Reports Available
            </span>
            <span className="text-[10px] bg-emerald-950 text-emerald-300 font-mono px-2 py-0.5 rounded border border-emerald-800 flex items-center gap-1">
              <ShieldCheck className="w-3 h-3" /> Read-Only (Zero Tally Modification)
            </span>
          </div>
          <p className="text-xs text-slate-400 mt-0.5">
            Generate, preview, and export comprehensive statutory exception dossiers, management summaries, and working papers.
          </p>
        </div>

        {/* Global Export Buttons */}
        <div className="flex items-center gap-2">
          <button
            onClick={() => setIsPrintPreviewOpen(true)}
            className="px-3 py-1.5 bg-slate-800 hover:bg-slate-700 text-slate-200 border border-slate-700 rounded text-xs font-semibold flex items-center gap-1.5 cursor-pointer shadow-sm transition-all"
          >
            <Printer className="w-3.5 h-3.5 text-teal-400" />
            <span>Printable / PDF Preview</span>
          </button>

          <button
            onClick={handleExportExcel}
            disabled={isExporting}
            className="px-3.5 py-1.5 bg-gradient-to-r from-teal-600 to-emerald-600 hover:from-teal-500 hover:to-emerald-500 text-white rounded text-xs font-bold flex items-center gap-1.5 cursor-pointer shadow-md transition-all"
          >
            <Download className="w-3.5 h-3.5" />
            <span>{isExporting ? 'Generating Excel...' : 'Export Excel (.csv)'}</span>
          </button>
        </div>
      </div>

      {/* Success Notification */}
      {exportSuccessMessage && (
        <div className="p-3 bg-emerald-950/80 border border-emerald-700 rounded-lg text-emerald-200 text-xs flex items-center justify-between shadow-lg">
          <div className="flex items-center gap-2">
            <CheckCircle className="w-4 h-4 text-emerald-400 shrink-0" />
            <span className="font-semibold">{exportSuccessMessage}</span>
          </div>
          <button onClick={() => setExportSuccessMessage(null)} className="text-emerald-400 hover:text-white text-xs">
            Dismiss
          </button>
        </div>
      )}

      {/* Main 2-Column Layout: Left (10 Reports Navigator) + Right (Active Report Preview) */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-4 flex-1 overflow-hidden min-h-[580px]">
        {/* Left Sidebar: 10 Reports Selector */}
        <div className="lg:col-span-4 bg-[#121c30] border border-slate-800 rounded-lg p-3 flex flex-col overflow-hidden">
          <div className="pb-2 border-b border-slate-800 flex items-center justify-between mb-2">
            <span className="text-[10px] font-bold text-slate-400 uppercase tracking-wider">Select Audit Report (1 to 10)</span>
            <span className="text-[10px] font-mono text-teal-400">{filteredForReport.length} records</span>
          </div>

          <div className="space-y-1 overflow-y-auto flex-1 pr-1">
            {reportDefinitions.map((report, idx) => {
              const count = filterExceptionsForReport(report.id, exceptions).length;
              const isSelected = selectedReportType === report.id;

              return (
                <button
                  key={report.id}
                  onClick={() => {
                    setSelectedReportType(report.id);
                    setExpandedExceptionId(null);
                  }}
                  className={`w-full text-left p-2.5 rounded-lg border transition-all cursor-pointer flex flex-col gap-1 ${
                    isSelected
                      ? 'bg-[#182642] border-teal-600 shadow-md'
                      : 'bg-[#0a0f1d]/80 border-slate-800/80 hover:bg-[#121c32] text-slate-300'
                  }`}
                >
                  <div className="flex items-center justify-between">
                    <div className="flex items-center gap-2">
                      <div className={`p-1 rounded ${isSelected ? 'bg-teal-950 text-teal-300 border border-teal-700' : 'bg-slate-800 text-slate-400'}`}>
                        {getReportIcon(report.iconName)}
                      </div>
                      <span className={`font-bold text-xs ${isSelected ? 'text-white' : 'text-slate-200'}`}>
                        {report.title}
                      </span>
                    </div>
                    <span className={`text-[10px] font-mono px-1.5 py-0.2 rounded font-bold ${
                      isSelected
                        ? 'bg-teal-600 text-white'
                        : count > 0 ? 'bg-slate-800 text-slate-300' : 'bg-slate-900 text-slate-500'
                    }`}>
                      {count}
                    </span>
                  </div>

                  <p className="text-[11px] text-slate-400 line-clamp-1 pl-7">
                    {report.subtitle}
                  </p>
                </button>
              );
            })}
          </div>

          {/* Read-Only Safety Assurance Card */}
          <div className="mt-3 pt-3 border-t border-slate-800 bg-[#090e1a] p-2.5 rounded border border-slate-800/80 text-[10px] text-slate-400 space-y-1">
            <div className="flex items-center gap-1.5 text-teal-400 font-bold">
              <ShieldCheck className="w-3.5 h-3.5" />
              <span>Read-Only Audit Safety Policy</span>
            </div>
            <p className="leading-relaxed text-[10px]">
              All 10 reports extract findings from the localized SQLite synchronization cache. Zero write commands are ever issued to TallyPrime.
            </p>
          </div>
        </div>

        {/* Right Pane: Report Header Banner & Live Content */}
        <div className="lg:col-span-8 bg-[#0d1424] border border-slate-800 rounded-lg flex flex-col overflow-hidden">
          {/* Active Report Header Bar */}
          <div className="bg-[#070b14] px-4 py-3 border-b border-slate-800 flex flex-col sm:flex-row sm:items-center justify-between gap-2">
            <div>
              <div className="flex items-center gap-2">
                <span className="text-xs font-bold text-teal-300">{activeReportDef.title}</span>
                <span className="text-[10px] bg-slate-800 text-slate-300 px-2 py-0.5 rounded border border-slate-700">
                  {activeReportDef.category}
                </span>
              </div>
              <p className="text-[11px] text-slate-400">{activeReportDef.subtitle}</p>
            </div>

            <div className="flex items-center gap-2">
              <button
                onClick={handleExportExcel}
                className="px-2.5 py-1 bg-slate-800 hover:bg-slate-700 text-teal-300 rounded text-[11px] font-semibold flex items-center gap-1 border border-slate-700 cursor-pointer"
                title="Download CSV / Excel spreadsheet"
              >
                <FileSpreadsheet className="w-3.5 h-3.5" /> Export Excel
              </button>
              <button
                onClick={() => setIsPrintPreviewOpen(true)}
                className="px-2.5 py-1 bg-slate-800 hover:bg-slate-700 text-slate-200 rounded text-[11px] font-semibold flex items-center gap-1 border border-slate-700 cursor-pointer"
                title="Print or Save as PDF"
              >
                <Printer className="w-3.5 h-3.5" /> PDF / Print
              </button>
            </div>
          </div>

          {/* Scrollable Report Content */}
          <div className="flex-1 overflow-y-auto p-4 space-y-4 text-xs">
            {/* 1. MANDATORY REPORT METADATA BANNER (Required on ALL 10 Reports) */}
            <div className="bg-[#121c32] border border-teal-900/80 rounded-lg p-4 space-y-3 shadow-md">
              <div className="flex items-center justify-between pb-2 border-b border-slate-800">
                <div className="flex items-center gap-2">
                  <Building2 className="w-4 h-4 text-teal-400" />
                  <div>
                    <span className="text-[10px] text-slate-400 uppercase tracking-wider block font-bold">Auditee Entity</span>
                    <span className="text-white font-bold text-sm">{metadata.company}</span>
                  </div>
                </div>
                <div className="text-right">
                  <span className="text-[10px] text-slate-400 uppercase tracking-wider block font-bold">Financial Year</span>
                  <span className="text-amber-300 font-mono font-bold text-xs">{metadata.financialYear}</span>
                </div>
              </div>

              {/* 8 Metadata Pillars */}
              <div className="grid grid-cols-2 sm:grid-cols-4 gap-2 text-[11px]">
                <div className="bg-[#070b14] p-2 rounded border border-slate-800">
                  <span className="text-[9px] text-slate-400 block font-semibold">Report Generation:</span>
                  <span className="text-slate-200 font-mono text-[10px]">{metadata.reportGenerationDate}</span>
                </div>
                <div className="bg-[#070b14] p-2 rounded border border-slate-800">
                  <span className="text-[9px] text-slate-400 block font-semibold">Application Version:</span>
                  <span className="text-teal-300 font-mono text-[10px]">{metadata.applicationVersion}</span>
                </div>
                <div className="bg-[#070b14] p-2 rounded border border-slate-800">
                  <span className="text-[9px] text-slate-400 block font-semibold">Data Sync Date:</span>
                  <span className="text-slate-200 font-mono text-[10px]">{metadata.dataSynchronizationDate}</span>
                </div>
                <div className="bg-[#070b14] p-2 rounded border border-slate-800">
                  <span className="text-[9px] text-slate-400 block font-semibold">Records Examined:</span>
                  <span className="text-emerald-400 font-mono font-bold text-[10px]">
                    {metadata.recordsExamined.totalRecords.toLocaleString()} ({metadata.recordsExamined.vouchers.toLocaleString()} Vouchers)
                  </span>
                </div>
              </div>

              {/* Statutory Rule Versions Manifest & Exception KPI Strip */}
              <div className="grid grid-cols-1 md:grid-cols-2 gap-2 pt-1 text-[10px]">
                <div className="bg-[#090f1e] p-2.5 rounded border border-slate-800 space-y-1">
                  <span className="text-slate-400 font-bold uppercase tracking-wider block text-[9px]">
                    Rule Versions Applied in Audit:
                  </span>
                  <div className="flex flex-wrap gap-1 font-mono text-[9px]">
                    <span className="bg-slate-800 text-sky-300 px-1.5 py-0.5 rounded">{metadata.ruleVersionsUsed.gst}</span>
                    <span className="bg-slate-800 text-amber-300 px-1.5 py-0.5 rounded">{metadata.ruleVersionsUsed.tds}</span>
                    <span className="bg-slate-800 text-emerald-300 px-1.5 py-0.5 rounded">{metadata.ruleVersionsUsed.accounting}</span>
                    <span className="bg-slate-800 text-purple-300 px-1.5 py-0.5 rounded">{metadata.ruleVersionsUsed.duplicates}</span>
                  </div>
                </div>

                <div className="bg-[#090f1e] p-2.5 rounded border border-slate-800 flex items-center justify-between">
                  <div>
                    <span className="text-slate-400 font-bold uppercase tracking-wider block text-[9px]">
                      Exception Status Balance:
                    </span>
                    <div className="flex items-center gap-2 mt-1">
                      <span className="font-mono text-xs font-bold text-rose-400">{metadata.exceptionsDetected} Detected</span>
                      <span className="text-slate-600">|</span>
                      <span className="font-mono text-xs font-bold text-emerald-400">{metadata.exceptionsReviewed} Reviewed</span>
                      <span className="text-slate-600">|</span>
                      <span className="font-mono text-xs font-bold text-amber-300">{metadata.exceptionsPending} Pending</span>
                    </div>
                  </div>
                  <div className="text-right text-[10px] text-slate-400 font-mono">
                    <div>Investigation: <strong className="text-purple-300">{metadata.exceptionsRequiresInvestigation}</strong></div>
                    <div>Dismissed: <strong className="text-slate-300">{metadata.exceptionsDismissed}</strong></div>
                  </div>
                </div>
              </div>
            </div>

            {/* Filter & Search Toolbar */}
            <div className="flex flex-col sm:flex-row items-center justify-between gap-2">
              <div className="relative w-full sm:w-72">
                <Search className="w-3.5 h-3.5 text-slate-400 absolute left-2.5 top-2.5" />
                <input
                  type="text"
                  placeholder="Filter by voucher, party, ledger, rule..."
                  value={searchQuery}
                  onChange={(e) => setSearchQuery(e.target.value)}
                  className="w-full bg-[#0a0f1d] border border-slate-700 rounded-md pl-8 pr-3 py-1.5 text-xs text-white placeholder-slate-500 focus:outline-none focus:border-teal-500"
                />
              </div>

              <div className="flex items-center gap-2 w-full sm:w-auto justify-end">
                <span className="text-[11px] text-slate-400">Severity:</span>
                <select
                  value={severityFilter}
                  onChange={(e) => setSeverityFilter(e.target.value)}
                  className="bg-[#0a0f1d] border border-slate-700 rounded px-2.5 py-1 text-xs text-slate-200"
                >
                  <option value="ALL">All Severities</option>
                  <option value="Critical">Critical</option>
                  <option value="High">High</option>
                  <option value="Medium">Medium</option>
                  <option value="Low">Low</option>
                </select>
                <span className="text-[11px] text-slate-400 font-mono">({displayItems.length} matching)</span>
              </div>
            </div>

            {/* Itemized Exceptions Table & Expandable Details */}
            {displayItems.length > 0 ? (
              <div className="bg-[#121c30] border border-slate-800 rounded-lg overflow-hidden space-y-1">
                <div className="bg-[#050811] px-3 py-2 border-b border-slate-800 flex items-center justify-between text-[10px] font-bold uppercase tracking-wider text-slate-400">
                  <span>Itemized Exception Records with Local Source Evidence</span>
                  <span>Total Value: ₹{displayItems.reduce((acc, c) => acc + c.amount, 0).toLocaleString()}</span>
                </div>

                <div className="divide-y divide-slate-800/80">
                  {displayItems.map((item) => {
                    const isExpanded = expandedExceptionId === item.id;
                    const sevBadgeClass = 
                      item.severity === 'Critical' ? 'bg-rose-950 text-rose-300 border-rose-800' :
                      item.severity === 'High' ? 'bg-orange-950 text-orange-300 border-orange-800' :
                      item.severity === 'Medium' ? 'bg-amber-950 text-amber-300 border-amber-800' :
                      'bg-slate-800 text-slate-300 border-slate-700';

                    const statusBadgeClass =
                      item.status === 'Reviewed' ? 'bg-emerald-950 text-emerald-300 border-emerald-800' :
                      item.status === 'Requires Investigation' ? 'bg-purple-950 text-purple-300 border-purple-800' :
                      item.status === 'Dismissed with Reason' ? 'bg-slate-800 text-slate-300 border-slate-700' :
                      'bg-amber-950 text-amber-300 border-amber-800';

                    return (
                      <div key={item.id} className="p-3 hover:bg-[#15223a] transition-colors">
                        <div
                          onClick={() => setExpandedExceptionId(isExpanded ? null : item.id)}
                          className="flex items-start justify-between gap-3 cursor-pointer select-none"
                        >
                          <div className="flex items-start gap-2.5">
                            <div className="pt-0.5 text-slate-400">
                              {isExpanded ? <ChevronDown className="w-4 h-4 text-teal-400" /> : <ChevronRight className="w-4 h-4" />}
                            </div>

                            <div className="space-y-1">
                              <div className="flex items-center gap-2">
                                <span className="font-mono font-bold text-xs text-teal-300">{item.id}</span>
                                <span className={`text-[9px] font-bold px-1.5 py-0.2 rounded border font-mono ${sevBadgeClass}`}>
                                  {item.severity}
                                </span>
                                <span className="text-[10px] text-slate-400 font-mono">[{item.ruleId}]</span>
                                <span className={`text-[9px] px-1.5 py-0.2 rounded border font-mono ${statusBadgeClass}`}>
                                  {item.status}
                                </span>
                              </div>

                              <div className="font-semibold text-white text-xs">
                                {item.exceptionTitle}
                              </div>

                              <div className="text-[11px] text-slate-400 flex items-center gap-2 flex-wrap">
                                <span>Voucher: <strong className="text-slate-200 font-mono">{item.voucherNumber} ({item.voucherType})</strong></span>
                                <span>•</span>
                                <span>Date: <strong className="text-slate-200 font-mono">{item.voucherDate}</strong></span>
                                <span>•</span>
                                <span>Party: <strong className="text-slate-200">{item.partyLedgerName}</strong></span>
                              </div>
                            </div>
                          </div>

                          <div className="text-right shrink-0">
                            <div className="font-mono font-bold text-emerald-400 text-sm">
                              ₹{item.amount.toLocaleString()}
                            </div>
                            <span className="text-[10px] text-slate-400 font-mono block">Master ID: {item.tallyNavigationGuide.masterId}</span>
                          </div>
                        </div>

                        {/* Expanded Local Evidence & Full Working Paper Traceability */}
                        {isExpanded && (
                          <div className="mt-3 pt-3 border-t border-slate-800/80 space-y-3 pl-6 text-[11px]">
                            {/* Why Flagged / Statutory Violation */}
                            <div className="bg-[#070b14] p-2.5 rounded border border-slate-800 space-y-1">
                              <span className="text-slate-400 text-[10px] font-bold uppercase tracking-wider block">
                                Statutory Audit Finding &amp; Violation:
                              </span>
                              <p className="text-slate-300 leading-relaxed">{item.whyFlagged}</p>
                              <div className="text-[10px] text-teal-400 pt-0.5">
                                Statutory Reference: <strong>{item.statutoryReference}</strong>
                              </div>
                            </div>

                            {/* Evidence JSON Payload */}
                            <div className="space-y-1">
                              <span className="text-slate-400 text-[10px] font-bold uppercase tracking-wider block">
                                Traceable Local Evidence Payload:
                              </span>
                              <pre className="bg-[#050811] p-2.5 rounded border border-slate-800 font-mono text-[10px] text-emerald-300 overflow-x-auto">
                                {item.evidenceJson}
                              </pre>
                            </div>

                            {/* Multi-Line Double Entry Postings */}
                            <div className="space-y-1">
                              <span className="text-slate-400 text-[10px] font-bold uppercase tracking-wider block">
                                Tally Double-Entry Multi-Line Ledger Postings:
                              </span>
                              <div className="bg-[#050811] border border-slate-800 rounded overflow-hidden">
                                <table className="w-full text-left text-[10px]">
                                  <thead className="bg-[#090f1d] text-slate-400 border-b border-slate-800">
                                    <tr>
                                      <th className="p-1.5">Ledger Name</th>
                                      <th className="p-1.5">Parent Group</th>
                                      <th className="p-1.5 text-right">Debit (₹)</th>
                                      <th className="p-1.5 text-right">Credit (₹)</th>
                                    </tr>
                                  </thead>
                                  <tbody className="divide-y divide-slate-800/60 font-mono">
                                    {item.sourceVoucher.entries.map((entry, idx) => (
                                      <tr key={idx}>
                                        <td className="p-1.5 font-sans text-slate-200">{entry.ledgerName}</td>
                                        <td className="p-1.5 font-sans text-slate-400">{entry.parentGroup}</td>
                                        <td className="p-1.5 text-right text-emerald-400">
                                          {entry.isDebit ? entry.amount.toLocaleString() : '—'}
                                        </td>
                                        <td className="p-1.5 text-right text-rose-400">
                                          {!entry.isDebit ? entry.amount.toLocaleString() : '—'}
                                        </td>
                                      </tr>
                                    ))}
                                  </tbody>
                                </table>
                              </div>
                            </div>

                            {/* Auditor Notes & Disposition */}
                            {item.reviewerNotes && (
                              <div className="bg-[#091522] p-2.5 rounded border border-teal-900/60 text-[11px] space-y-1">
                                <span className="text-teal-400 text-[10px] font-bold uppercase tracking-wider block">
                                  Recorded Auditor Working Paper Notes:
                                </span>
                                <div className="text-slate-200 font-sans whitespace-pre-wrap">{item.reviewerNotes}</div>
                              </div>
                            )}

                            {/* Actions on exception */}
                            <div className="flex items-center gap-2 pt-1">
                              {onOpenDrillDown && (
                                <button
                                  onClick={() => onOpenDrillDown(item)}
                                  className="px-2.5 py-1 bg-teal-800 hover:bg-teal-700 text-white rounded text-[11px] font-bold flex items-center gap-1 cursor-pointer"
                                >
                                  <ExternalLink className="w-3 h-3" /> Tally Drill-Down &amp; Navigation
                                </button>
                              )}
                              {onNavigateToWorkspace && (
                                <button
                                  onClick={() => onNavigateToWorkspace(item.id)}
                                  className="px-2.5 py-1 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded text-[11px] font-semibold flex items-center gap-1 cursor-pointer"
                                >
                                  <FileSignature className="w-3 h-3 text-teal-400" /> Open in Workspace
                                </button>
                              )}
                            </div>
                          </div>
                        )}
                      </div>
                    );
                  })}
                </div>
              </div>
            ) : (
              <div className="text-center py-16 bg-[#121c30] border border-slate-800 rounded-lg space-y-2">
                <CheckCircle className="w-8 h-8 text-emerald-400 mx-auto opacity-70" />
                <h4 className="font-bold text-white text-sm">No Exceptions Under This Filter</h4>
                <p className="text-slate-400 text-xs max-w-sm mx-auto">
                  All transactions examined under <strong>{activeReportDef.title}</strong> conform to statutory thresholds and internal controls.
                </p>
              </div>
            )}
          </div>
        </div>
      </div>

      {/* PRINTABLE / FORMAL PDF PREVIEW MODAL */}
      {isPrintPreviewOpen && (
        <div className="fixed inset-0 bg-black/85 z-50 flex items-center justify-center p-4 backdrop-blur-xs overflow-y-auto">
          <div className="bg-white text-slate-900 rounded-xl max-w-4xl w-full p-8 space-y-6 shadow-2xl max-h-[92vh] flex flex-col font-sans">
            {/* Modal Top Control Bar */}
            <div className="flex items-center justify-between pb-3 border-b border-slate-200 shrink-0">
              <div className="flex items-center gap-2">
                <Printer className="w-5 h-5 text-teal-700" />
                <span className="font-bold text-base text-slate-800">Printable Statutory Audit Report &amp; PDF Preview</span>
              </div>
              <div className="flex items-center gap-2">
                <button
                  onClick={handlePrintPdf}
                  className="px-3.5 py-1.5 bg-teal-700 hover:bg-teal-800 text-white rounded text-xs font-bold flex items-center gap-1.5 cursor-pointer shadow"
                >
                  <Printer className="w-3.5 h-3.5" /> Print / Save as PDF
                </button>
                <button
                  onClick={() => setIsPrintPreviewOpen(false)}
                  className="px-3 py-1.5 bg-slate-200 hover:bg-slate-300 text-slate-700 rounded text-xs font-semibold cursor-pointer"
                >
                  Close
                </button>
              </div>
            </div>

            {/* Printable Document Sheet */}
            <div className="flex-1 overflow-y-auto pr-2 space-y-5 text-xs text-slate-800 leading-normal">
              {/* CA Formal Letterhead Header */}
              <div className="text-center border-b-2 border-slate-800 pb-3 space-y-1">
                <div className="text-[11px] tracking-widest uppercase font-bold text-slate-500">
                  STATUTORY AUDIT &amp; ASSURANCE WORKING PAPER
                </div>
                <h1 className="text-xl font-black text-slate-900 tracking-tight">
                  {metadata.company.toUpperCase()}
                </h1>
                <div className="text-xs font-semibold text-slate-700">
                  FINANCIAL YEAR: {metadata.financialYear}
                </div>
                <div className="text-sm font-bold text-teal-900 uppercase pt-1">
                  {activeReportDef.title}
                </div>
              </div>

              {/* Mandatory Metadata Grid in Document */}
              <div className="bg-slate-100 p-3 rounded-lg border border-slate-300 grid grid-cols-2 sm:grid-cols-4 gap-2 text-[11px]">
                <div>
                  <span className="text-slate-500 block text-[9px] uppercase font-bold">Report Date:</span>
                  <span className="font-semibold text-slate-800">{metadata.reportGenerationDate}</span>
                </div>
                <div>
                  <span className="text-slate-500 block text-[9px] uppercase font-bold">Software Build:</span>
                  <span className="font-semibold text-slate-800">{metadata.applicationVersion}</span>
                </div>
                <div>
                  <span className="text-slate-500 block text-[9px] uppercase font-bold">Data Sync Date:</span>
                  <span className="font-semibold text-slate-800">{metadata.dataSynchronizationDate}</span>
                </div>
                <div>
                  <span className="text-slate-500 block text-[9px] uppercase font-bold">Records Examined:</span>
                  <span className="font-bold text-teal-800">{metadata.recordsExamined.totalRecords.toLocaleString()}</span>
                </div>
              </div>

              {/* Statutory Rules & Summary Pillar */}
              <div className="grid grid-cols-2 gap-3 text-[11px]">
                <div className="p-2.5 border border-slate-300 rounded bg-slate-50">
                  <span className="font-bold text-slate-700 block mb-1">Applied Rule Versions:</span>
                  <ul className="list-disc pl-4 space-y-0.5 text-[10px] text-slate-600">
                    <li>{metadata.ruleVersionsUsed.gst}</li>
                    <li>{metadata.ruleVersionsUsed.tds}</li>
                    <li>{metadata.ruleVersionsUsed.accounting}</li>
                    <li>{metadata.ruleVersionsUsed.duplicates}</li>
                  </ul>
                </div>
                <div className="p-2.5 border border-slate-300 rounded bg-slate-50">
                  <span className="font-bold text-slate-700 block mb-1">Exception Review Balance:</span>
                  <div className="space-y-0.5 text-[10px] text-slate-600">
                    <div>Total Exceptions Flagged: <strong>{metadata.exceptionsDetected}</strong></div>
                    <div>Statutory Reviews Completed: <strong>{metadata.exceptionsReviewed}</strong></div>
                    <div>Pending Working Paper Resolution: <strong>{metadata.exceptionsPending}</strong></div>
                    <div>Marked for Investigation: <strong>{metadata.exceptionsRequiresInvestigation}</strong></div>
                  </div>
                </div>
              </div>

              {/* Table of Flagged Exceptions */}
              <div className="space-y-2">
                <span className="font-bold text-xs uppercase tracking-wider text-slate-900 block">
                  Detailed Findings &amp; Exception Register
                </span>
                <table className="w-full text-left border-collapse border border-slate-300 text-[10px]">
                  <thead className="bg-slate-200 text-slate-800 font-bold border-b border-slate-300">
                    <tr>
                      <th className="p-2 border border-slate-300">Voucher / Ref</th>
                      <th className="p-2 border border-slate-300">Date</th>
                      <th className="p-2 border border-slate-300">Party / Primary Ledger</th>
                      <th className="p-2 border border-slate-300">Rule ID &amp; Finding</th>
                      <th className="p-2 border border-slate-300 text-right">Amount (₹)</th>
                      <th className="p-2 border border-slate-300 text-center">Status</th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredForReport.map((ex, idx) => (
                      <tr key={idx} className={idx % 2 === 0 ? 'bg-white' : 'bg-slate-50'}>
                        <td className="p-2 border border-slate-300 font-mono font-bold">
                          {ex.voucherNumber} ({ex.voucherType})
                        </td>
                        <td className="p-2 border border-slate-300 font-mono">{ex.voucherDate}</td>
                        <td className="p-2 border border-slate-300">
                          <div className="font-semibold text-slate-900">{ex.partyLedgerName}</div>
                          <div className="text-[9px] text-slate-500">{ex.primaryLedger}</div>
                        </td>
                        <td className="p-2 border border-slate-300">
                          <div className="font-bold text-slate-800">[{ex.ruleId}] {ex.exceptionTitle}</div>
                          <div className="text-[9px] text-slate-600 mt-0.5">{ex.whyFlagged}</div>
                        </td>
                        <td className="p-2 border border-slate-300 font-mono font-bold text-right text-slate-900">
                          ₹{ex.amount.toLocaleString()}
                        </td>
                        <td className="p-2 border border-slate-300 text-center font-bold text-[9px]">
                          {ex.status}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              {/* Auditor Sign-Off Block & Statutory Disclaimer */}
              <div className="pt-6 border-t-2 border-slate-300 flex items-end justify-between text-[11px] text-slate-600">
                <div className="space-y-1 max-w-sm">
                  <div className="font-bold text-slate-800">Statutory Non-Modification Disclaimer:</div>
                  <p className="text-[9px] leading-tight">
                    This document was compiled strictly in read-only mode from the local SQLite repository cache. The source TallyPrime data file remains unaltered.
                  </p>
                </div>
                <div className="text-center space-y-6">
                  <div className="w-48 border-b border-slate-800 pb-1">
                    <span className="text-[10px] italic text-slate-400">Authorized Signature &amp; Stamp</span>
                  </div>
                  <div className="text-[10px] font-bold text-slate-800 uppercase">
                    Statutory Auditor / Engagement Partner
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
