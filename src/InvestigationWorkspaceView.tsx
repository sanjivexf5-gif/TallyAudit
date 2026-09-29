import React, { useState } from 'react';
import {
  Compass,
  CheckCircle2,
  AlertTriangle,
  Clock,
  User,
  Calendar,
  FileText,
  FileCheck2,
  FileSignature,
  ArrowRight,
  ExternalLink,
  BookOpen,
  Check,
  X,
  History,
  ShieldCheck,
  Tag,
  ListChecks,
  Scale,
  GitCompare,
  UploadCloud,
  Hash,
  Sparkles,
  Info,
  Download,
  Printer,
  ChevronRight
} from 'lucide-react';
import { WorkspaceExceptionItem } from './workspaceData';
import {
  ExceptionInvestigation,
  InvestigationStatus,
  RootCauseClassification,
  ROOT_CAUSE_OPTIONS,
  STATUS_METADATA,
  getAllowedTransitions,
  canTransitionInvestigation
} from './investigationData';

interface Props {
  exception: WorkspaceExceptionItem;
  investigation: ExceptionInvestigation;
  onSave: (updated: ExceptionInvestigation) => void;
  onClose: () => void;
  onOpenInTally: () => void;
  onOpenSourceData: () => void;
}

export const InvestigationWorkspaceView: React.FC<Props> = ({
  exception,
  investigation,
  onSave,
  onClose,
  onOpenInTally,
  onOpenSourceData
}) => {
  const [currentInv, setCurrentInv] = useState<ExceptionInvestigation>({ ...investigation });
  const [activeTab, setActiveTab] = useState<'notes' | 'checklist' | 'vouchers' | 'statutory' | 'reconciliation' | 'auditTrail'>('notes');
  const [transitionModalTarget, setTransitionModalTarget] = useState<InvestigationStatus | null>(null);
  const [transitionReason, setTransitionReason] = useState<string>('');
  const [saveToast, setSaveToast] = useState<string | null>(null);

  // Quick stats
  const completedChecklistCount = currentInv.checklist.filter(c => c.isCompleted).length;
  const checklistPercentage = Math.round((completedChecklistCount / currentInv.checklist.length) * 100);

  const allowedTransitions = getAllowedTransitions(currentInv.status);

  // Status transition handler
  const handleTransition = (target: InvestigationStatus) => {
    if (!canTransitionInvestigation(currentInv.status, target)) {
      alert(`Invalid transition from ${currentInv.status} to ${target}.`);
      return;
    }

    const now = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) + ' ' + new Date().toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
    const isClosing = target === 'Resolved' || target === 'NotResolved' || target === 'Accepted';
    const isReopening = target === 'Investigating' || target === 'Open';

    const newClosedAt = isClosing ? now : isReopening ? null : currentInv.closedAt;

    const newHistoryEntry = {
      id: `H-${Date.now()}`,
      timestamp: now,
      action: `Status Changed to ${target}`,
      fromStatus: currentInv.status,
      toStatus: target,
      user: 'Senior Statutory Auditor',
      reason: transitionReason.trim() || 'Status changed in Investigation Workspace',
      notes: transitionReason.trim()
    };

    const updated: ExceptionInvestigation = {
      ...currentInv,
      status: target,
      updatedAt: now,
      closedAt: newClosedAt,
      updatedBy: 'Senior Statutory Auditor',
      history: [newHistoryEntry, ...currentInv.history]
    };

    setCurrentInv(updated);
    onSave(updated);
    setTransitionModalTarget(null);
    setTransitionReason('');

    setSaveToast(`Investigation status updated to "${target}"`);
    setTimeout(() => setSaveToast(null), 3000);
  };

  // Checklist toggle handler
  const handleToggleChecklist = (id: string) => {
    const now = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) + ' ' + new Date().toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
    
    setCurrentInv(prev => {
      const updatedChecklist = prev.checklist.map(item => {
        if (item.id === id) {
          const willComplete = !item.isCompleted;
          return {
            ...item,
            isCompleted: willComplete,
            completedAt: willComplete ? now : null,
            completedBy: willComplete ? 'Senior Auditor' : null
          };
        }
        return item;
      });

      const updated = {
        ...prev,
        checklist: updatedChecklist,
        updatedAt: now
      };
      onSave(updated);
      return updated;
    });
  };

  const handleUpdateChecklistNote = (id: string, note: string) => {
    setCurrentInv(prev => {
      const updatedChecklist = prev.checklist.map(item => {
        if (item.id === id) {
          return { ...item, notes: note };
        }
        return item;
      });
      return { ...prev, checklist: updatedChecklist };
    });
  };

  // Save full investigation
  const handleSaveAll = () => {
    const now = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) + ' ' + new Date().toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
    const updated: ExceptionInvestigation = {
      ...currentInv,
      updatedAt: now,
      updatedBy: 'Senior Statutory Auditor'
    };
    setCurrentInv(updated);
    onSave(updated);
    setSaveToast('Investigation workspace saved successfully.');
    setTimeout(() => setSaveToast(null), 2500);
  };

  // Export summary report
  const handleExportSummary = () => {
    const text = `======================================================================
TALLY AUDIT ASSISTANT - EXCEPTION INVESTIGATION WORKSPACE
======================================================================
Investigation ID: ${currentInv.id}
Exception ID:     ${exception.id}
Company:          ${exception.companyName}
Financial Period: ${currentInv.financialPeriodId}
Audit Run:        ${currentInv.auditRunId}
Status:           ${currentInv.status}
Root Cause:       ${currentInv.rootCause}
Created By:       ${currentInv.createdBy} (${currentInv.createdAt})
Updated At:       ${currentInv.updatedAt}
Closed At:        ${currentInv.closedAt || 'Active / Open'}

EXCEPTION DETAILS:
- Rule:           ${exception.ruleId} - ${exception.ruleName}
- Severity:       ${exception.severity}
- Voucher:        ${exception.voucherNumber} (${exception.voucherType})
- Date:           ${exception.voucherDate}
- Party / Ledger: ${exception.partyLedgerName}
- Amount:         ₹${exception.amount.toLocaleString()}
- Summary:        ${exception.whyFlagged}

AUDITOR WORKING NOTES:
${currentInv.auditorNotes || 'No notes entered.'}

MANAGEMENT RESPONSE:
${currentInv.managementResponse || 'No response recorded.'}

PROPOSED CORRECTIVE ACTION:
${currentInv.proposedCorrectiveAction || 'None specified.'}

REVIEWER NOTES:
${currentInv.reviewerNotes || 'Pending supervisory review.'}

INVESTIGATION CHECKLIST (${completedChecklistCount}/${currentInv.checklist.length} Completed):
${currentInv.checklist.map(c => `[${c.isCompleted ? 'X' : ' '}] ${c.code}: ${c.description} ${c.completedBy ? `(by ${c.completedBy} at ${c.completedAt})` : ''} ${c.notes ? `- Note: ${c.notes}` : ''}`).join('\n')}

INVESTIGATION AUDIT TRAIL:
${currentInv.history.map(h => `- [${h.timestamp}] ${h.action} by ${h.user}: ${h.notes || h.reason || ''}`).join('\n')}
======================================================================`;

    const blob = new Blob([text], { type: 'text/plain' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `Investigation_${currentInv.id}_${exception.id}.txt`;
    a.click();
    URL.revokeObjectURL(url);
  };

  return (
    <div className="fixed inset-0 bg-black/85 flex items-center justify-center z-50 p-2 sm:p-4 backdrop-blur-xs">
      <div className="bg-[#0b1222] border border-teal-800/80 rounded-xl max-w-6xl w-full h-[94vh] flex flex-col shadow-2xl text-xs overflow-hidden">
        
        {/* 1. Modal Top Bar */}
        <div className="p-3 bg-[#070c18] border-b border-slate-800 flex items-center justify-between shrink-0">
          <div className="flex items-center gap-2.5">
            <div className="w-8 h-8 rounded-lg bg-teal-950 border border-teal-700 flex items-center justify-center text-teal-300">
              <Compass className="w-4 h-4" />
            </div>
            <div>
              <div className="flex items-center gap-2">
                <h3 className="font-bold text-white text-sm">Audit Exception Root-Cause &amp; Investigation Workspace</h3>
                <span className="font-mono text-[10px] bg-teal-950 text-teal-300 px-2 py-0.5 rounded border border-teal-800 font-bold">
                  {currentInv.id}
                </span>
                <span className={`px-2 py-0.5 rounded text-[10px] font-semibold border ${STATUS_METADATA[currentInv.status].badgeClass}`}>
                  {STATUS_METADATA[currentInv.status].label}
                </span>
              </div>
              <div className="text-[10px] text-slate-400 flex items-center gap-2 mt-0.5">
                <span>Company: <strong className="text-slate-200">{exception.companyName}</strong></span>
                <span>•</span>
                <span>Exception: <strong className="text-slate-200">{exception.id}</strong></span>
                <span>•</span>
                <span>Voucher: <strong className="text-teal-300 font-mono">{exception.voucherNumber}</strong></span>
              </div>
            </div>
          </div>

          <div className="flex items-center gap-2">
            {saveToast && (
              <span className="text-[11px] bg-emerald-950 text-emerald-300 border border-emerald-800 px-2.5 py-1 rounded font-medium flex items-center gap-1">
                <CheckCircle2 className="w-3.5 h-3.5" /> {saveToast}
              </span>
            )}
            <button
              onClick={handleExportSummary}
              className="px-2.5 py-1.5 bg-slate-800 hover:bg-slate-700 text-teal-300 rounded border border-slate-700 flex items-center gap-1 font-semibold cursor-pointer"
              title="Download text summary"
            >
              <Download className="w-3.5 h-3.5" /> Export Report
            </button>
            <button
              onClick={handleSaveAll}
              className="px-3 py-1.5 bg-teal-600 hover:bg-teal-500 text-white rounded font-bold flex items-center gap-1 shadow-sm cursor-pointer"
            >
              <Check className="w-3.5 h-3.5" /> Save Workspace
            </button>
            <button
              onClick={onClose}
              className="p-1.5 text-slate-400 hover:text-white rounded hover:bg-slate-800 cursor-pointer"
              title="Close Workspace"
            >
              <X className="w-5 h-5" />
            </button>
          </div>
        </div>

        {/* 2. Top Summary & Lifecycle State Pipeline */}
        <div className="bg-[#0f172a] border-b border-slate-800 p-3 shrink-0 space-y-2.5">
          {/* Exception Details Strip */}
          <div className="grid grid-cols-2 sm:grid-cols-4 lg:grid-cols-6 gap-2 bg-[#080d1a] p-2.5 rounded-lg border border-slate-800/80">
            <div>
              <span className="text-[9px] uppercase tracking-wider text-slate-400 block font-semibold">Statutory Rule</span>
              <span className="font-mono text-teal-300 font-bold text-[11px]">{exception.ruleId}</span>
              <span className="block text-[10px] text-slate-300 truncate">{exception.ruleName}</span>
            </div>
            <div>
              <span className="text-[9px] uppercase tracking-wider text-slate-400 block font-semibold">Severity &amp; Module</span>
              <span className={`inline-block px-1.5 py-0.2 rounded text-[10px] font-bold ${
                exception.severity === 'Critical' ? 'bg-rose-950 text-rose-300' :
                exception.severity === 'High' ? 'bg-red-950 text-red-300' :
                'bg-amber-950 text-amber-300'
              }`}>
                {exception.severity}
              </span>
              <span className="block text-[10px] text-slate-400 truncate">{exception.module}</span>
            </div>
            <div>
              <span className="text-[9px] uppercase tracking-wider text-slate-400 block font-semibold">Voucher &amp; Date</span>
              <span className="font-mono text-slate-200 font-bold text-[11px]">{exception.voucherNumber}</span>
              <span className="block text-[10px] text-slate-400">{exception.voucherDate} ({exception.voucherType})</span>
            </div>
            <div>
              <span className="text-[9px] uppercase tracking-wider text-slate-400 block font-semibold">Flagged Amount</span>
              <span className="font-mono text-emerald-400 font-bold text-sm">₹{exception.amount.toLocaleString()}</span>
              <span className="block text-[10px] text-slate-400 truncate">{exception.partyLedgerName}</span>
            </div>
            <div>
              <span className="text-[9px] uppercase tracking-wider text-slate-400 block font-semibold">Investigation Lead</span>
              <span className="text-slate-200 font-semibold text-[11px]">{currentInv.createdBy}</span>
              <span className="block text-[10px] text-slate-400">{currentInv.createdAt}</span>
            </div>
            <div>
              <span className="text-[9px] uppercase tracking-wider text-slate-400 block font-semibold">Checklist Progress</span>
              <div className="flex items-center gap-1.5 mt-0.5">
                <div className="flex-1 bg-slate-800 rounded-full h-2 overflow-hidden border border-slate-700">
                  <div className="bg-teal-500 h-full transition-all" style={{ width: `${checklistPercentage}%` }} />
                </div>
                <span className="font-mono text-[10px] font-bold text-teal-300">{checklistPercentage}%</span>
              </div>
              <span className="text-[9px] text-slate-400">{completedChecklistCount} of {currentInv.checklist.length} Completed</span>
            </div>
          </div>

          {/* Lifecycle State Transitions & Action Bar */}
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 pt-1 border-t border-slate-800/60">
            <div className="flex items-center gap-2">
              <span className="text-[10px] uppercase tracking-wider text-slate-400 font-bold flex items-center gap-1">
                <Clock className="w-3 h-3 text-teal-400" /> Lifecycle State:
              </span>
              <span className={`px-2 py-0.5 rounded text-[10px] font-bold border ${STATUS_METADATA[currentInv.status].badgeClass}`}>
                {STATUS_METADATA[currentInv.status].label}
              </span>
              <span className="text-[11px] text-slate-400 italic">({STATUS_METADATA[currentInv.status].description})</span>
            </div>

            {/* Allowed Transition Actions */}
            <div className="flex items-center gap-1.5 flex-wrap">
              <span className="text-[10px] text-slate-400 font-semibold mr-1">Permitted Transitions:</span>
              {allowedTransitions.length === 0 ? (
                <span className="text-[10px] text-slate-500 italic">Terminal State</span>
              ) : (
                allowedTransitions.map(target => (
                  <button
                    key={target}
                    onClick={() => {
                      setTransitionModalTarget(target);
                      setTransitionReason('');
                    }}
                    className="px-2 py-1 bg-slate-800 hover:bg-slate-700 hover:text-teal-300 text-slate-200 rounded border border-slate-700 text-[10px] font-semibold flex items-center gap-1 cursor-pointer transition-colors"
                  >
                    <span>Move to</span>
                    <strong className="text-teal-300">{STATUS_METADATA[target].label}</strong>
                    <ArrowRight className="w-3 h-3 text-slate-400" />
                  </button>
                ))
              )}
            </div>
          </div>
        </div>

        {/* 3. Root-Cause Classification Selector & Professional Auditor Disclaimer */}
        <div className="bg-[#0a0f1d] border-b border-slate-800 p-3 shrink-0 flex flex-col md:flex-row md:items-center justify-between gap-3">
          <div className="flex-1 space-y-1">
            <div className="flex items-center gap-2">
              <Tag className="w-4 h-4 text-purple-400" />
              <span className="font-bold text-slate-200 text-xs uppercase tracking-wider">
                Auditor Root-Cause Classification
              </span>
              <span className="text-[10px] bg-purple-950 text-purple-300 px-2 py-0.2 rounded border border-purple-800">
                Auditor Judgment Only
              </span>
            </div>
            <div className="flex items-center gap-2">
              <select
                value={currentInv.rootCause}
                onChange={e => {
                  const val = e.target.value as RootCauseClassification;
                  setCurrentInv(prev => ({ ...prev, rootCause: val }));
                }}
                className="bg-[#121c30] border border-slate-700 text-slate-200 text-xs rounded px-3 py-1.5 font-semibold focus:outline-none focus:border-teal-500 max-w-xs"
              >
                {ROOT_CAUSE_OPTIONS.map(opt => (
                  <option key={opt.value} value={opt.value}>
                    {opt.label} — {opt.description}
                  </option>
                ))}
              </select>
              <span className="text-[11px] text-slate-400 italic">
                {ROOT_CAUSE_OPTIONS.find(o => o.value === currentInv.rootCause)?.description}
              </span>
            </div>
          </div>

          {/* Mandatory Disciplinary Notice */}
          <div className="bg-[#141b2d] border border-amber-800/60 rounded-lg p-2 max-w-md text-[10px] text-amber-200/90 leading-tight">
            <div className="font-bold flex items-center gap-1 text-amber-300 mb-0.5">
              <ShieldCheck className="w-3 h-3" /> Professional Auditor Standard Notice
            </div>
            Root cause is an auditor classification. The application must not automatically conclude that a transaction is fraudulent, illegal, intentional, negligent, or caused by management.
          </div>
        </div>

        {/* 4. Tab Navigation Strip */}
        <div className="bg-[#070b16] border-b border-slate-800 px-3 flex items-center gap-2 shrink-0 overflow-x-auto">
          {[
            { id: 'notes', label: 'Auditor Notes & Analysis', icon: FileSignature },
            { id: 'checklist', label: `15-Point Investigation Checklist (${completedChecklistCount}/15)`, icon: ListChecks },
            { id: 'vouchers', label: 'Source Voucher & Entries', icon: BookOpen },
            { id: 'statutory', label: 'Statutory GST & TDS', icon: Scale },
            { id: 'reconciliation', label: 'Reconciliation & Duplicates', icon: GitCompare },
            { id: 'auditTrail', label: `Investigation Audit Trail (${currentInv.history.length})`, icon: History }
          ].map(tab => {
            const Icon = tab.icon;
            const isActive = activeTab === tab.id;
            return (
              <button
                key={tab.id}
                onClick={() => setActiveTab(tab.id as any)}
                className={`py-2 px-3 border-b-2 font-medium flex items-center gap-1.5 cursor-pointer whitespace-nowrap transition-colors ${
                  isActive
                    ? 'border-teal-500 text-teal-300 font-bold'
                    : 'border-transparent text-slate-400 hover:text-slate-200 hover:border-slate-700'
                }`}
              >
                <Icon className="w-3.5 h-3.5" />
                <span>{tab.label}</span>
              </button>
            );
          })}
        </div>

        {/* 5. Tab Content Body */}
        <div className="flex-1 overflow-y-auto p-4 space-y-4">
          
          {/* TAB 1: AUDITOR NOTES & INQUIRY */}
          {activeTab === 'notes' && (
            <div className="space-y-4 max-w-5xl">
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                {/* 1. Auditor Working Notes & Hypothesis */}
                <div className="bg-[#101728] border border-slate-800 rounded-lg p-3.5 space-y-2">
                  <div className="flex items-center justify-between">
                    <span className="text-xs font-bold text-white uppercase tracking-wider flex items-center gap-1.5">
                      <FileSignature className="w-3.5 h-3.5 text-teal-400" />
                      1. Auditor Technical Notes &amp; Working Hypothesis
                    </span>
                    <span className="text-[10px] text-slate-400">SA 230 Working Paper</span>
                  </div>
                  <textarea
                    rows={6}
                    value={currentInv.auditorNotes}
                    onChange={e => setCurrentInv(prev => ({ ...prev, auditorNotes: e.target.value }))}
                    placeholder="Enter detailed audit inquiry, ledger cross-checks, physical verification findings, and why this exception occurred..."
                    className="w-full bg-[#080d18] border border-slate-700 rounded p-2.5 text-xs text-white focus:outline-none focus:border-teal-500 font-sans leading-relaxed"
                  />
                  <p className="text-[10px] text-slate-400">
                    Record working hypothesis, specific voucher anomalies, and whether this represents a single occurrence or systemic trend.
                  </p>
                </div>

                {/* 2. Management Response */}
                <div className="bg-[#101728] border border-slate-800 rounded-lg p-3.5 space-y-2">
                  <div className="flex items-center justify-between">
                    <span className="text-xs font-bold text-white uppercase tracking-wider flex items-center gap-1.5">
                      <User className="w-3.5 h-3.5 text-purple-400" />
                      2. Client Management Representation / Response
                    </span>
                    <span className="text-[10px] text-purple-300 font-mono">SA 580 Written Representation</span>
                  </div>
                  <textarea
                    rows={6}
                    value={currentInv.managementResponse}
                    onChange={e => setCurrentInv(prev => ({ ...prev, managementResponse: e.target.value }))}
                    placeholder="Document management's formal explanation, date received, officer contact, and rationale for voucher discrepancies..."
                    className="w-full bg-[#080d18] border border-slate-700 rounded p-2.5 text-xs text-white focus:outline-none focus:border-purple-500 font-sans leading-relaxed"
                  />
                  <p className="text-[10px] text-slate-400">
                    Capture the auditee's response, date received, and whether management agrees with the finding.
                  </p>
                </div>

                {/* 3. Proposed Corrective Action */}
                <div className="bg-[#101728] border border-slate-800 rounded-lg p-3.5 space-y-2">
                  <div className="flex items-center justify-between">
                    <span className="text-xs font-bold text-white uppercase tracking-wider flex items-center gap-1.5">
                      <CheckCircle2 className="w-3.5 h-3.5 text-emerald-400" />
                      3. Proposed Corrective Action &amp; Remediation
                    </span>
                    <span className="text-[10px] text-emerald-300 font-semibold">Tally Adjustment Plan</span>
                  </div>
                  <textarea
                    rows={5}
                    value={currentInv.proposedCorrectiveAction}
                    onChange={e => setCurrentInv(prev => ({ ...prev, proposedCorrectiveAction: e.target.value }))}
                    placeholder="Specify rectification journal, vendor bill cancellation, party master update, or GSTR return adjustment..."
                    className="w-full bg-[#080d18] border border-slate-700 rounded p-2.5 text-xs text-white focus:outline-none focus:border-emerald-500 font-sans leading-relaxed"
                  />
                  <p className="text-[10px] text-slate-400">
                    State the exact journal voucher or master change required to clear this exception.
                  </p>
                </div>

                {/* 4. Reviewer Notes & Supervisory Sign-off */}
                <div className="bg-[#101728] border border-slate-800 rounded-lg p-3.5 space-y-2">
                  <div className="flex items-center justify-between">
                    <span className="text-xs font-bold text-white uppercase tracking-wider flex items-center gap-1.5">
                      <ShieldCheck className="w-3.5 h-3.5 text-sky-400" />
                      4. Engagement Partner / Quality Reviewer Sign-Off
                    </span>
                    <span className="text-[10px] text-sky-300 font-semibold">SQC 1 Quality Review</span>
                  </div>
                  <textarea
                    rows={5}
                    value={currentInv.reviewerNotes}
                    onChange={e => setCurrentInv(prev => ({ ...prev, reviewerNotes: e.target.value }))}
                    placeholder="Supervisory reviewer comments, approval of root cause classification, and clearance for audit opinion..."
                    className="w-full bg-[#080d18] border border-slate-700 rounded p-2.5 text-xs text-white focus:outline-none focus:border-sky-500 font-sans leading-relaxed"
                  />
                  <p className="text-[10px] text-slate-400">
                    Partner or audit senior sign-off affirming conclusion and adequate working paper documentation.
                  </p>
                </div>
              </div>

              {/* Quick Actions Footer inside Notes */}
              <div className="flex items-center justify-between bg-[#121c30] p-3 rounded-lg border border-slate-800">
                <div className="text-[11px] text-slate-400">
                  Status: <strong className="text-teal-300">{currentInv.status}</strong> • Last updated {currentInv.updatedAt}
                </div>
                <div className="flex items-center gap-2">
                  <button
                    onClick={handleSaveAll}
                    className="px-3 py-1.5 bg-teal-600 hover:bg-teal-500 text-white rounded font-bold cursor-pointer"
                  >
                    Save Notes
                  </button>
                </div>
              </div>
            </div>
          )}

          {/* TAB 2: 15-POINT INVESTIGATION CHECKLIST */}
          {activeTab === 'checklist' && (
            <div className="space-y-3 max-w-5xl">
              <div className="flex items-center justify-between pb-1 border-b border-slate-800">
                <div>
                  <h4 className="font-bold text-white text-sm flex items-center gap-2">
                    <ListChecks className="w-4 h-4 text-teal-400" />
                    Structured 15-Point Investigation Checklist
                  </h4>
                  <p className="text-slate-400 text-[11px]">
                    Standardized investigation methodology ensuring all statutory, evidentiary, and reconciliation aspects are rigorously scrutinized.
                  </p>
                </div>
                <div className="text-right">
                  <span className="font-mono text-sm font-bold text-teal-300">{completedChecklistCount} / {currentInv.checklist.length}</span>
                  <span className="text-slate-400 text-[10px] block">Completed ({checklistPercentage}%)</span>
                </div>
              </div>

              <div className="space-y-2">
                {currentInv.checklist.map((item, idx) => (
                  <div
                    key={item.id}
                    className={`p-3 rounded-lg border transition-all ${
                      item.isCompleted
                        ? 'bg-[#0f1b2e] border-teal-800/80 text-slate-200'
                        : 'bg-[#101726] border-slate-800 text-slate-300 hover:border-slate-700'
                    }`}
                  >
                    <div className="flex items-start justify-between gap-3">
                      <div className="flex items-start gap-3 flex-1">
                        <input
                          type="checkbox"
                          checked={item.isCompleted}
                          onChange={() => handleToggleChecklist(item.id)}
                          className="mt-0.5 w-4 h-4 rounded text-teal-600 focus:ring-teal-500 bg-slate-900 border-slate-700 cursor-pointer"
                        />
                        <div className="flex-1 space-y-1">
                          <div className="flex items-center gap-2">
                            <span className="font-mono text-[10px] font-bold text-teal-400 bg-slate-900 px-1.5 py-0.2 rounded border border-slate-800">
                              {item.code}
                            </span>
                            <span className={`font-semibold text-xs ${item.isCompleted ? 'text-teal-200 line-through opacity-80' : 'text-white'}`}>
                              {item.description}
                            </span>
                            {item.isCompleted && (
                              <span className="text-[9px] bg-emerald-950 text-emerald-300 px-1.5 py-0.2 rounded border border-emerald-800">
                                Verified by {item.completedBy} at {item.completedAt}
                              </span>
                            )}
                          </div>

                          {/* Notes field per checklist item */}
                          <div className="pt-1">
                            <input
                              type="text"
                              value={item.notes}
                              onChange={e => handleUpdateChecklistNote(item.id, e.target.value)}
                              placeholder="Auditor observation / working paper cross-reference for this step..."
                              className="w-full bg-[#080d18] border border-slate-800 rounded px-2.5 py-1 text-[11px] text-slate-200 focus:outline-none focus:border-teal-500"
                            />
                          </div>
                        </div>
                      </div>

                      <span className="font-mono text-[10px] text-slate-500">#{idx + 1}</span>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          )}

          {/* TAB 3: SOURCE VOUCHER & ENTRIES */}
          {activeTab === 'vouchers' && (
            <div className="space-y-4 max-w-5xl">
              <div className="flex items-center justify-between">
                <div>
                  <h4 className="font-bold text-white text-sm">Primary Flagged Voucher Details</h4>
                  <p className="text-slate-400 text-[11px]">Direct accounting entries pulled from synchronized TallyPrime database.</p>
                </div>
                <div className="flex items-center gap-2">
                  <button
                    onClick={onOpenInTally}
                    className="px-2.5 py-1.5 bg-teal-600 hover:bg-teal-500 text-white rounded font-bold flex items-center gap-1 shadow-sm cursor-pointer"
                  >
                    <ExternalLink className="w-3.5 h-3.5" /> Drill-Down in TallyPrime
                  </button>
                  <button
                    onClick={onOpenSourceData}
                    className="px-2.5 py-1.5 bg-slate-800 hover:bg-slate-700 text-teal-300 rounded border border-slate-700 flex items-center gap-1 font-semibold cursor-pointer"
                  >
                    <BookOpen className="w-3.5 h-3.5" /> View Raw Ledger
                  </button>
                </div>
              </div>

              {/* Voucher Header Card */}
              <div className="bg-[#121c30] border border-slate-800 rounded-lg p-3.5 grid grid-cols-2 sm:grid-cols-4 gap-3">
                <div>
                  <span className="text-[10px] text-slate-400 uppercase font-semibold block">Voucher Number</span>
                  <span className="font-mono font-bold text-white text-sm">{exception.sourceVoucher.voucherNumber}</span>
                  <span className="text-[10px] text-teal-300 block">{exception.sourceVoucher.voucherType}</span>
                </div>
                <div>
                  <span className="text-[10px] text-slate-400 uppercase font-semibold block">Voucher Date</span>
                  <span className="font-mono text-slate-200 text-xs">{exception.sourceVoucher.voucherDate}</span>
                  {exception.sourceVoucher.referenceNumber && (
                    <span className="text-[10px] text-slate-400 block">Ref: {exception.sourceVoucher.referenceNumber}</span>
                  )}
                </div>
                <div>
                  <span className="text-[10px] text-slate-400 uppercase font-semibold block">Party Ledger</span>
                  <span className="text-slate-200 font-bold text-xs">{exception.sourceVoucher.partyLedgerName}</span>
                  <span className="font-mono text-[10px] text-slate-400 block">{exception.sourceVoucher.partyGstin || 'No GSTIN'}</span>
                </div>
                <div>
                  <span className="text-[10px] text-slate-400 uppercase font-semibold block">Total Amount</span>
                  <span className="font-mono text-emerald-400 font-bold text-base">₹{exception.sourceVoucher.totalAmount.toLocaleString()}</span>
                </div>
              </div>

              {/* Narration */}
              <div className="bg-[#090e1a] border border-slate-800 rounded-lg p-3">
                <span className="text-[10px] uppercase tracking-wider text-slate-400 font-semibold block mb-1">
                  Voucher Narration Text
                </span>
                <p className="font-mono text-xs text-slate-200 italic">
                  "{exception.sourceVoucher.narration || 'No narration provided.'}"
                </p>
              </div>

              {/* Entry Line Items */}
              <div className="bg-[#101726] border border-slate-800 rounded-lg overflow-hidden">
                <div className="p-2.5 bg-[#090e1a] border-b border-slate-800 font-bold text-slate-200 text-xs">
                  Voucher Debit / Credit Postings
                </div>
                <table className="w-full text-left text-xs">
                  <thead className="bg-[#070b14] text-[10px] text-slate-400 uppercase border-b border-slate-800">
                    <tr>
                      <th className="p-2.5">Ledger Name</th>
                      <th className="p-2.5">Parent Group</th>
                      <th className="p-2.5 text-right">Debit (₹)</th>
                      <th className="p-2.5 text-right">Credit (₹)</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-800">
                    {exception.sourceVoucher.entries.map((entry, idx) => (
                      <tr key={idx} className="hover:bg-slate-800/40">
                        <td className="p-2.5 font-medium text-slate-100">{entry.ledgerName}</td>
                        <td className="p-2.5 text-slate-400">{entry.parentGroup}</td>
                        <td className="p-2.5 text-right font-mono font-bold text-teal-300">
                          {entry.isDebit ? `₹${entry.amount.toLocaleString()}` : '—'}
                        </td>
                        <td className="p-2.5 text-right font-mono font-bold text-amber-300">
                          {!entry.isDebit ? `₹${entry.amount.toLocaleString()}` : '—'}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}

          {/* TAB 4: STATUTORY GST & TDS */}
          {activeTab === 'statutory' && (
            <div className="space-y-4 max-w-5xl">
              <h4 className="font-bold text-white text-sm">Statutory Profile &amp; Compliance Details</h4>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                {/* GST Profiling Card */}
                <div className="bg-[#101728] border border-slate-800 rounded-lg p-3.5 space-y-2.5">
                  <div className="flex items-center justify-between pb-1 border-b border-slate-800">
                    <span className="font-bold text-sky-300 uppercase tracking-wider flex items-center gap-1.5">
                      <Scale className="w-3.5 h-3.5" /> GST Statutory Breakdown
                    </span>
                    <span className="font-mono text-[10px] bg-sky-950 text-sky-300 px-2 py-0.2 rounded border border-sky-800">
                      {exception.gstDetails.registrationType}
                    </span>
                  </div>

                  <div className="space-y-1.5 text-slate-300 text-xs">
                    <div className="flex justify-between">
                      <span className="text-slate-400">Party GSTIN:</span>
                      <span className="font-mono text-white font-bold">{exception.gstDetails.partyGstin || 'Not Registered'}</span>
                    </div>
                    <div className="flex justify-between">
                      <span className="text-slate-400">Place of Supply:</span>
                      <span>{exception.gstDetails.placeOfSupply}</span>
                    </div>
                    <div className="flex justify-between">
                      <span className="text-slate-400">Taxable Value:</span>
                      <span className="font-mono text-white">₹{exception.gstDetails.taxableAmount.toLocaleString()}</span>
                    </div>
                    <div className="flex justify-between">
                      <span className="text-slate-400">Total Tax Amount:</span>
                      <span className="font-mono text-emerald-400 font-bold">₹{exception.gstDetails.totalTaxAmount.toLocaleString()}</span>
                    </div>
                    <div className="flex justify-between">
                      <span className="text-slate-400">Reverse Charge (RCM):</span>
                      <span className={exception.gstDetails.isReverseCharge ? 'text-amber-400 font-bold' : 'text-slate-400'}>
                        {exception.gstDetails.isReverseCharge ? 'Applicable (Yes)' : 'No'}
                      </span>
                    </div>
                  </div>

                  {exception.gstDetails.gstLedgers.length > 0 && (
                    <div className="pt-2 border-t border-slate-800 space-y-1">
                      <span className="text-[10px] font-bold text-slate-400 uppercase block">GST Postings</span>
                      {exception.gstDetails.gstLedgers.map((g, idx) => (
                        <div key={idx} className="flex justify-between text-[11px] bg-[#070b14] p-1.5 rounded">
                          <span className="text-slate-300">{g.ledgerName} ({g.rate}%)</span>
                          <span className="font-mono text-teal-300">₹{g.amount.toLocaleString()}</span>
                        </div>
                      ))}
                    </div>
                  )}
                </div>

                {/* TDS Withholding Profiling Card */}
                <div className="bg-[#101728] border border-slate-800 rounded-lg p-3.5 space-y-2.5">
                  <div className="flex items-center justify-between pb-1 border-b border-slate-800">
                    <span className="font-bold text-purple-300 uppercase tracking-wider flex items-center gap-1.5">
                      <Tag className="w-3.5 h-3.5" /> TDS Withholding Compliance
                    </span>
                    <span className={`font-mono text-[10px] px-2 py-0.2 rounded border ${
                      exception.tdsDetails?.isApplicable ? 'bg-purple-950 text-purple-300 border-purple-800' : 'bg-slate-800 text-slate-400 border-slate-700'
                    }`}>
                      {exception.tdsDetails?.isApplicable ? 'TDS Applicable' : 'Non-TDS Head'}
                    </span>
                  </div>

                  <div className="space-y-1.5 text-slate-300 text-xs">
                    <div className="flex justify-between">
                      <span className="text-slate-400">Section:</span>
                      <span className="font-bold text-white">{exception.tdsDetails?.sectionCode || '—'} {exception.tdsDetails?.sectionDescription ? `(${exception.tdsDetails.sectionDescription})` : ''}</span>
                    </div>
                    <div className="flex justify-between">
                      <span className="text-slate-400">Deductee PAN:</span>
                      <span className="font-mono text-white font-bold">{exception.tdsDetails?.deducteePan || 'None'}</span>
                    </div>
                    <div className="flex justify-between">
                      <span className="text-slate-400">PAN Status (Sec 206AA):</span>
                      <span className="font-semibold text-amber-300">{exception.tdsDetails?.panStatus || 'Not Evaluated'}</span>
                    </div>
                    <div className="flex justify-between">
                      <span className="text-slate-400">Statutory TDS Rate:</span>
                      <span className="font-mono text-teal-300 font-bold">{exception.tdsDetails?.tdsRatePercent ? `${exception.tdsDetails.tdsRatePercent}%` : '—'}</span>
                    </div>
                    <div className="flex justify-between">
                      <span className="text-slate-400">TDS Deducted:</span>
                      <span className="font-mono text-emerald-400 font-bold">
                        {exception.tdsDetails?.tdsAmountDeducted ? `₹${exception.tdsDetails.tdsAmountDeducted.toLocaleString()}` : '₹0.00'}
                      </span>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          )}

          {/* TAB 5: RECONCILIATION & DUPLICATES */}
          {activeTab === 'reconciliation' && (
            <div className="space-y-4 max-w-5xl">
              <h4 className="font-bold text-white text-sm">Correlated Records &amp; Duplicate Analysis</h4>

              {/* Related Transactions */}
              {exception.relatedTransactions.length > 0 ? (
                <div className="space-y-2">
                  <span className="text-xs font-bold text-slate-300 block">
                    Correlated Vouchers in Database ({exception.relatedTransactions.length})
                  </span>
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                    {exception.relatedTransactions.map((tx, idx) => (
                      <div key={idx} className="bg-[#121c30] border border-slate-800 rounded-lg p-3 space-y-1.5">
                        <div className="flex items-center justify-between">
                          <span className="font-mono font-bold text-teal-300 text-xs">{tx.voucherNumber}</span>
                          <span className="text-[10px] bg-slate-800 text-slate-300 px-1.5 py-0.2 rounded font-semibold">
                            {tx.relationType}
                          </span>
                        </div>
                        <div className="text-[11px] text-slate-300">Party: {tx.partyName}</div>
                        <div className="flex items-center justify-between text-[11px] text-slate-400">
                          <span>Date: {tx.voucherDate}</span>
                          <span className="font-mono text-emerald-400 font-bold">₹{tx.amount.toLocaleString()}</span>
                        </div>
                        <p className="text-[10px] text-slate-400 pt-1 border-t border-slate-800 italic">
                          "{tx.note}"
                        </p>
                      </div>
                    ))}
                  </div>
                </div>
              ) : (
                <div className="bg-[#0e1628] border border-slate-800 p-8 text-center rounded-lg text-slate-400">
                  <GitCompare className="w-8 h-8 mx-auto mb-2 text-slate-500 opacity-40" />
                  No immediate duplicate vouchers or correlated bank entries found for this specific identifier.
                </div>
              )}
            </div>
          )}

          {/* TAB 6: INVESTIGATION AUDIT TRAIL */}
          {activeTab === 'auditTrail' && (
            <div className="space-y-3 max-w-4xl">
              <div className="flex items-center justify-between pb-1 border-b border-slate-800">
                <h4 className="font-bold text-white text-sm flex items-center gap-2">
                  <History className="w-4 h-4 text-teal-400" />
                  Investigation Lifecycle History &amp; Immutable Audit Trail
                </h4>
                <span className="text-[10px] text-slate-400">{currentInv.history.length} Recorded Activity Logs</span>
              </div>

              <div className="space-y-3 border-l-2 border-slate-800 pl-4 py-2">
                {currentInv.history.map(entry => (
                  <div key={entry.id} className="bg-[#101726] border border-slate-800 p-3 rounded-lg space-y-1">
                    <div className="flex items-center justify-between text-xs">
                      <span className="font-bold text-teal-300 flex items-center gap-1.5">
                        <User className="w-3.5 h-3.5 text-slate-400" />
                        {entry.user}
                      </span>
                      <span className="font-mono text-[10px] text-slate-400">{entry.timestamp}</span>
                    </div>
                    <div className="text-[11px] text-white font-semibold">{entry.action}</div>
                    {entry.fromStatus && entry.toStatus && (
                      <div className="text-[10px] text-slate-400 flex items-center gap-1.5">
                        <span>Transition:</span>
                        <span className="text-amber-300">{entry.fromStatus}</span>
                        <ArrowRight className="w-3 h-3 text-slate-500" />
                        <span className="text-emerald-300 font-bold">{entry.toStatus}</span>
                      </div>
                    )}
                    {entry.reason && (
                      <p className="text-[11px] text-slate-300 italic pt-1 border-t border-slate-800/80">
                        Reason: "{entry.reason}"
                      </p>
                    )}
                  </div>
                ))}
              </div>
            </div>
          )}

        </div>

        {/* 6. Modal Footer */}
        <div className="p-3 bg-[#070c18] border-t border-slate-800 flex items-center justify-between shrink-0">
          <div className="text-[11px] text-slate-400 flex items-center gap-2">
            <span>Root Cause: <strong className="text-purple-300">{ROOT_CAUSE_OPTIONS.find(o => o.value === currentInv.rootCause)?.label}</strong></span>
            <span>•</span>
            <span>Status: <strong className="text-teal-300">{currentInv.status}</strong></span>
            <span>•</span>
            <span>Checklist: <strong className="text-emerald-300">{completedChecklistCount}/15 Done</strong></span>
          </div>

          <div className="flex items-center gap-2">
            <button
              onClick={onClose}
              className="px-3 py-1.5 bg-slate-800 hover:bg-slate-700 text-slate-200 rounded font-semibold cursor-pointer"
            >
              Return to Exceptions Review
            </button>
            <button
              onClick={handleSaveAll}
              className="px-4 py-1.5 bg-teal-600 hover:bg-teal-500 text-white rounded font-bold shadow-sm cursor-pointer"
            >
              Save Investigation Workspace
            </button>
          </div>
        </div>

      </div>

      {/* CONFIRM STATUS TRANSITION MODAL */}
      {transitionModalTarget && (
        <div className="fixed inset-0 bg-black/80 flex items-center justify-center z-60 p-4">
          <div className="bg-[#0f172a] border border-slate-700 rounded-xl max-w-md w-full p-5 space-y-4 shadow-2xl text-xs">
            <div className="flex items-center justify-between pb-2 border-b border-slate-800">
              <h4 className="font-bold text-white text-sm flex items-center gap-2">
                <Clock className="w-4 h-4 text-teal-400" />
                Confirm Investigation Transition
              </h4>
              <button
                onClick={() => setTransitionModalTarget(null)}
                className="text-slate-400 hover:text-white"
              >
                <X className="w-4 h-4" />
              </button>
            </div>

            <div className="space-y-2 text-slate-300">
              <p>
                You are transitioning the investigation state from:
              </p>
              <div className="flex items-center justify-center gap-3 p-3 bg-[#080d18] rounded border border-slate-800 font-bold">
                <span className="text-amber-300">{currentInv.status}</span>
                <ArrowRight className="w-4 h-4 text-slate-500" />
                <span className="text-emerald-300">{STATUS_METADATA[transitionModalTarget].label}</span>
              </div>
              <p className="text-[11px] text-slate-400">
                {STATUS_METADATA[transitionModalTarget].description}
              </p>

              <div>
                <label className="block text-[10px] text-slate-400 uppercase font-bold mb-1">
                  Reason for Transition (Logged to Audit Trail)
                </label>
                <textarea
                  rows={3}
                  value={transitionReason}
                  onChange={e => setTransitionReason(e.target.value)}
                  placeholder="State the auditor rationale for advancing or updating this status..."
                  className="w-full bg-[#080d18] border border-slate-700 rounded p-2 text-xs text-white focus:outline-none focus:border-teal-500"
                />
              </div>
            </div>

            <div className="flex items-center justify-end gap-2 pt-2 border-t border-slate-800">
              <button
                onClick={() => setTransitionModalTarget(null)}
                className="px-3 py-1.5 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded font-semibold cursor-pointer"
              >
                Cancel
              </button>
              <button
                onClick={() => handleTransition(transitionModalTarget)}
                className="px-4 py-1.5 bg-teal-600 hover:bg-teal-500 text-white rounded font-bold cursor-pointer"
              >
                Confirm &amp; Record Transition
              </button>
            </div>
          </div>
        </div>
      )}

    </div>
  );
};
