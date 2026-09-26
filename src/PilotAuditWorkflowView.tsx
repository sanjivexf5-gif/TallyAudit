import React, { useState } from 'react';
import {
  CheckCircle2,
  Clock,
  AlertTriangle,
  FileText,
  SlidersHorizontal,
  Layers,
  Search,
  Plus,
  ShieldCheck,
  Shield,
  ArrowRight,
  RefreshCw,
  Database,
  BarChart3,
  Calendar,
  Building2,
  CheckSquare,
  Lock,
  Download,
  Info,
  ExternalLink,
  Target
} from 'lucide-react';
import {
  DataCompletenessReport,
  AuditLimitationRecord,
  MaterialitySpecification,
  SamplingRunRecord,
  AuditRunHistoryItem,
  AuditRuleCatalogItem,
  WorkflowStageItem,
  initialDataCompleteness,
  initialAuditLimitations,
  initialMateriality,
  initialSamplingRuns,
  initialAuditRuns,
  auditRuleCatalog,
  standardAuditWorkflowStages
} from './pilotAuditData';
import { CompanyWorkspace } from './companyData';

interface PilotAuditWorkflowViewProps {
  currentCompany: CompanyWorkspace;
  activeFinancialYear: string;
  onNavigateToSection: (nav: string) => void;
  recordSecurityLog: (category: 'AUTHENTICATION' | 'DATA_ACCESS' | 'WRITE_BACK' | 'BACKUP_RESTORE' | 'SECURITY_POLICY' | 'SYSTEM', action: string, details: string) => void;
}

export const PilotAuditWorkflowView: React.FC<PilotAuditWorkflowViewProps> = ({
  currentCompany,
  activeFinancialYear,
  onNavigateToSection,
  recordSecurityLog
}) => {
  const [activeTab, setActiveTab] = useState<'WORKFLOW' | 'COMPLETENESS' | 'RULES_QUALITY' | 'MATERIALITY_SAMPLING' | 'RUN_HISTORY'>('WORKFLOW');

  const [workflowStages, setWorkflowStages] = useState<WorkflowStageItem[]>(standardAuditWorkflowStages);
  const [dataCompleteness, setDataCompleteness] = useState<DataCompletenessReport>(initialDataCompleteness);
  const [limitations, setLimitations] = useState<AuditLimitationRecord[]>(initialAuditLimitations);
  const [materiality, setMateriality] = useState<MaterialitySpecification>(initialMateriality);
  const [samplingRuns, setSamplingRuns] = useState<SamplingRunRecord[]>(initialSamplingRuns);
  const [auditRuns, setAuditRuns] = useState<AuditRunHistoryItem[]>(initialAuditRuns);
  const [ruleCatalog, setRuleCatalog] = useState<AuditRuleCatalogItem[]>(auditRuleCatalog);

  // Filter/Search states
  const [ruleSearchQuery, setRuleSearchQuery] = useState<string>('');
  const [ruleCategoryFilter, setRuleCategoryFilter] = useState<string>('ALL');

  // Interactive modal states
  const [isAddLimitationOpen, setIsAddLimitationOpen] = useState<boolean>(false);
  const [newLimDataset, setNewLimDataset] = useState<string>('Direct Bank API Feed');
  const [newLimReason, setNewLimReason] = useState<string>('Bank API connection credentials pending client IT approval.');
  const [newLimAreas, setNewLimAreas] = useState<string>('Cash & Bank Balances, BRS Verification');
  const [newLimStrategy, setNewLimStrategy] = useState<string>('Manual audit of physical stamped bank statements & manager certificate.');

  // Sampling modal states
  const [isAddSamplingOpen, setIsAddSamplingOpen] = useState<boolean>(false);
  const [sampleArea, setSampleArea] = useState<string>('Capital Goods Purchases > ₹2,00,000');
  const [sampleMethod, setSampleMethod] = useState<'MATERIAL_ITEM' | 'SYSTEMATIC' | 'RANDOM'>('MATERIAL_ITEM');
  const [samplePopCount, setSamplePopCount] = useState<number>(420);
  const [samplePopVal, setSamplePopVal] = useState<number>(38500000);
  const [sampleSize, setSampleSize] = useState<number>(25);

  const completedStepsCount = workflowStages.filter(s => s.status === 'COMPLETED').length;
  const overallWorkflowProgress = Math.round((completedStepsCount / workflowStages.length) * 100);

  const handleAddLimitation = () => {
    if (!newLimDataset.trim() || !newLimReason.trim()) return;

    const newLim: AuditLimitationRecord = {
      id: `LIM-${String(limitations.length + 1).padStart(3, '0')}`,
      companyId: currentCompany.id,
      financialYear: activeFinancialYear,
      unavailableDataset: newLimDataset,
      reason: newLimReason,
      recordedDate: new Date().toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' }),
      recordedBy: 'Senior Statutory Auditor',
      affectedAuditAreas: newLimAreas.split(',').map(s => s.trim()),
      mitigationStrategy: newLimStrategy
    };

    setLimitations(prev => [newLim, ...prev]);
    setIsAddLimitationOpen(false);
    recordSecurityLog('SECURITY_POLICY', 'Audit Limitation Logged', `Recorded limitation on ${newLimDataset} for ${currentCompany.name}.`);
  };

  const handleCreateSampleRun = () => {
    const newRun: SamplingRunRecord = {
      id: `SMP-${Date.now().toString().slice(-4)}`,
      sampleCode: `SMP-PILOT-${Date.now().toString().slice(-4)}`,
      auditArea: sampleArea,
      method: sampleMethod,
      populationCount: samplePopCount,
      populationValue: samplePopVal,
      sampleSize: sampleSize,
      sampleTotalValue: Math.round(samplePopVal * (sampleSize / samplePopCount) * 1.2),
      randomSeed: Math.floor(Math.random() * 90000) + 10000,
      highValueThreshold: 200000,
      exceptionsFoundCount: 1,
      auditorConclusion: `Representative sample of ${sampleSize} transactions tested against supporting bills and approvals.`,
      executedAt: new Date().toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' }),
      executedBy: 'Audit Senior'
    };

    setSamplingRuns(prev => [newRun, ...prev]);
    setIsAddSamplingOpen(false);
    recordSecurityLog('DATA_ACCESS', 'Sampling Run Executed', `Executed ${sampleMethod} sample on ${sampleArea}.`);
  };

  const filteredRules = ruleCatalog.filter(r => {
    const matchCat = ruleCategoryFilter === 'ALL' || r.category === ruleCategoryFilter;
    const matchQuery =
      r.ruleName.toLowerCase().includes(ruleSearchQuery.toLowerCase()) ||
      r.ruleCode.toLowerCase().includes(ruleSearchQuery.toLowerCase()) ||
      r.whyFlagged.toLowerCase().includes(ruleSearchQuery.toLowerCase());
    return matchCat && matchQuery;
  });

  return (
    <div className="space-y-5 max-w-7xl mx-auto pb-10 flex flex-col h-full text-xs">
      {/* Header Bar */}
      <div className="pb-3 border-b border-slate-800 flex flex-col md:flex-row md:items-center justify-between gap-3">
        <div>
          <div className="flex items-center gap-2">
            <h2 className="text-xl font-bold text-white tracking-tight flex items-center gap-2">
              <CheckCircle2 className="w-5 h-5 text-amber-400" />
              <span>Pilot Audit Deployment &amp; Workflow Validation Center</span>
            </h2>
            <span className="text-[10px] bg-amber-950 text-amber-300 font-mono px-2 py-0.5 rounded border border-amber-800 font-bold">
              SA 200 / 300 / 320 / 500 Compliant
            </span>
          </div>
          <p className="text-xs text-slate-400 mt-0.5">
            Real-world auditor workflow tracking, data completeness verification, false-positive controls, materiality benchmarks, and audit run versioning.
          </p>
        </div>

        {/* Top KPI Metrics */}
        <div className="flex items-center gap-2 font-mono text-xs">
          <div className="bg-[#121c30] px-3 py-1.5 rounded border border-slate-800 flex items-center gap-2">
            <span className="text-slate-400 text-[10px]">WORKFLOW:</span>
            <strong className="text-emerald-400 font-bold">{completedStepsCount} / 22 STEPS ({overallWorkflowProgress}%)</strong>
          </div>
          <div className="bg-[#121c30] px-3 py-1.5 rounded border border-slate-800 flex items-center gap-2">
            <span className="text-slate-400 text-[10px]">READINESS:</span>
            <strong className="text-amber-300 font-bold">READY (W/ LIMITATIONS)</strong>
          </div>
        </div>
      </div>

      {/* Primary Sub-Navigation Tabs */}
      <div className="flex items-center gap-2 border-b border-slate-800 pb-2">
        <button
          onClick={() => setActiveTab('WORKFLOW')}
          className={`px-3 py-2 rounded font-bold text-xs transition-all cursor-pointer flex items-center gap-2 ${
            activeTab === 'WORKFLOW'
              ? 'bg-teal-600 text-white shadow'
              : 'bg-[#121c30] text-slate-400 hover:text-white border border-slate-800'
          }`}
        >
          <Layers className="w-4 h-4" />
          <span>22-Step Real Auditor Workflow ({overallWorkflowProgress}%)</span>
        </button>

        <button
          onClick={() => setActiveTab('COMPLETENESS')}
          className={`px-3 py-2 rounded font-bold text-xs transition-all cursor-pointer flex items-center gap-2 ${
            activeTab === 'COMPLETENESS'
              ? 'bg-teal-600 text-white shadow'
              : 'bg-[#121c30] text-slate-400 hover:text-white border border-slate-800'
          }`}
        >
          <Database className="w-4 h-4 text-cyan-400" />
          <span>Data Completeness &amp; Limitations</span>
          <span className="bg-amber-950 text-amber-300 text-[10px] px-1.5 py-0.2 rounded border border-amber-800 font-mono">
            {limitations.length} Noted
          </span>
        </button>

        <button
          onClick={() => setActiveTab('RULES_QUALITY')}
          className={`px-3 py-2 rounded font-bold text-xs transition-all cursor-pointer flex items-center gap-2 ${
            activeTab === 'RULES_QUALITY'
              ? 'bg-teal-600 text-white shadow'
              : 'bg-[#121c30] text-slate-400 hover:text-white border border-slate-800'
          }`}
        >
          <ShieldCheck className="w-4 h-4 text-emerald-400" />
          <span>Rule Quality &amp; False-Positive Control</span>
          <span className="bg-slate-800 text-slate-300 text-[10px] px-1.5 py-0.2 rounded font-mono">
            {ruleCatalog.length} Rules
          </span>
        </button>

        <button
          onClick={() => setActiveTab('MATERIALITY_SAMPLING')}
          className={`px-3 py-2 rounded font-bold text-xs transition-all cursor-pointer flex items-center gap-2 ${
            activeTab === 'MATERIALITY_SAMPLING'
              ? 'bg-teal-600 text-white shadow'
              : 'bg-[#121c30] text-slate-400 hover:text-white border border-slate-800'
          }`}
        >
          <Target className="w-4 h-4 text-purple-400" />
          <span>Materiality &amp; Sampling (SA 320/530)</span>
        </button>

        <button
          onClick={() => setActiveTab('RUN_HISTORY')}
          className={`px-3 py-2 rounded font-bold text-xs transition-all cursor-pointer flex items-center gap-2 ${
            activeTab === 'RUN_HISTORY'
              ? 'bg-teal-600 text-white shadow'
              : 'bg-[#121c30] text-slate-400 hover:text-white border border-slate-800'
          }`}
        >
          <Clock className="w-4 h-4 text-sky-400" />
          <span>Audit Run Versioning &amp; Re-run Safety</span>
        </button>
      </div>

      {/* TAB 1: 22-STEP REAL AUDITOR WORKFLOW */}
      {activeTab === 'WORKFLOW' && (
        <div className="space-y-4">
          <div className="bg-[#121c30] border border-slate-800 rounded-lg p-4 flex items-center justify-between">
            <div className="flex items-center gap-3">
              <div className="w-10 h-10 rounded-lg bg-teal-950 border border-teal-800 flex items-center justify-center text-teal-400">
                <Layers className="w-5 h-5" />
              </div>
              <div>
                <h3 className="text-sm font-bold text-white">Sequential Statutory Audit Lifecycle</h3>
                <p className="text-[11px] text-slate-400">
                  Step-by-step audit execution trail. Every stage preserves timestamps, auditor remarks, and deep links to active modules.
                </p>
              </div>
            </div>
            <div className="text-right font-mono text-xs">
              <span className="text-slate-400 block text-[10px]">CURRENT ENGAGEMENT:</span>
              <strong className="text-white">{currentCompany.name} • {activeFinancialYear}</strong>
            </div>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
            {workflowStages.map((stage) => (
              <div
                key={stage.id}
                className="bg-[#121c30] border border-slate-800 hover:border-teal-700/60 rounded-lg p-4 flex flex-col justify-between space-y-3 transition-all group"
              >
                <div>
                  <div className="flex items-center justify-between pb-2 border-b border-slate-800">
                    <span className="text-[10px] bg-slate-900 text-slate-400 px-2 py-0.5 rounded font-mono font-bold">
                      {stage.category}
                    </span>
                    <span className={`text-[10px] px-2 py-0.5 rounded font-bold border ${
                      stage.status === 'COMPLETED'
                        ? 'bg-emerald-950 text-emerald-300 border-emerald-800'
                        : stage.status === 'IN_PROGRESS'
                        ? 'bg-sky-950 text-sky-300 border-sky-800'
                        : 'bg-slate-900 text-slate-400 border-slate-800'
                    }`}>
                      {stage.status.replace('_', ' ')}
                    </span>
                  </div>

                  <h4 className="font-bold text-white text-xs mt-2 group-hover:text-teal-300 transition-colors">
                    {stage.title}
                  </h4>
                  <p className="text-[11px] text-slate-400 mt-1 leading-relaxed">
                    {stage.description}
                  </p>
                </div>

                <div className="pt-2 border-t border-slate-800 flex items-center justify-between">
                  <span className="text-[10px] font-mono text-slate-400">
                    Progress: <strong className="text-slate-200">{stage.progressPercentage}%</strong>
                  </span>
                  <button
                    onClick={() => onNavigateToSection(stage.navTarget)}
                    className="px-2.5 py-1 bg-slate-800 hover:bg-teal-600 text-teal-300 hover:text-white rounded text-[11px] font-semibold transition-all cursor-pointer flex items-center gap-1"
                  >
                    <span>Open Module</span>
                    <ArrowRight className="w-3 h-3" />
                  </button>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* TAB 2: DATA COMPLETENESS & LIMITATIONS */}
      {activeTab === 'COMPLETENESS' && (
        <div className="space-y-4">
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
            {/* Readiness Card */}
            <div className="md:col-span-2 bg-[#121c30] border border-slate-800 rounded-lg p-5 space-y-4">
              <div className="flex items-center justify-between pb-2 border-b border-slate-800">
                <div>
                  <h3 className="font-bold text-white text-sm flex items-center gap-2">
                    <Database className="w-4 h-4 text-cyan-400" />
                    <span>Pre-Audit Data Completeness Evaluation</span>
                  </h3>
                  <p className="text-[11px] text-slate-400">Verified against synchronized TallyPrime local SQLite snapshot.</p>
                </div>
                <span className="bg-amber-950 text-amber-300 text-xs px-2.5 py-1 rounded font-bold border border-amber-800 font-mono">
                  READY WITH LIMITATIONS
                </span>
              </div>

              {/* Dataset Items Table */}
              <div className="divide-y divide-slate-800/80">
                {dataCompleteness.datasetItems.map((item) => (
                  <div key={item.id} className="py-2.5 flex items-center justify-between gap-3">
                    <div className="space-y-0.5">
                      <div className="flex items-center gap-2">
                        <span className={`w-2 h-2 rounded-full ${item.isAvailable ? 'bg-emerald-400' : 'bg-rose-500'}`}></span>
                        <strong className="text-white text-xs">{item.name}</strong>
                        <span className="text-[10px] bg-slate-900 text-slate-400 font-mono px-1.5 py-0.2 rounded border border-slate-800">
                          {item.category}
                        </span>
                      </div>
                      <p className="text-[11px] text-slate-400">{item.notes}</p>
                    </div>
                    <div className="text-right font-mono shrink-0">
                      <span className="text-xs text-white font-bold block">{item.recordCount.toLocaleString()} records</span>
                      <span className="text-[10px] text-slate-400">{item.lastSyncTime}</span>
                    </div>
                  </div>
                ))}
              </div>
            </div>

            {/* Actions & Summary Card */}
            <div className="bg-[#121c30] border border-slate-800 rounded-lg p-5 space-y-4">
              <h3 className="font-bold text-white text-xs uppercase tracking-wider">Statutory Audit Scope Boundary</h3>
              <p className="text-[11px] text-slate-300 leading-relaxed">
                SA 210 / 580 requires auditors to document any limitations in data availability. Limitations documented here are automatically embedded in generated Executive Reports and the Final Audit File.
              </p>

              <button
                onClick={() => setIsAddLimitationOpen(true)}
                className="w-full py-2 bg-gradient-to-r from-amber-600 to-amber-500 hover:from-amber-500 hover:to-amber-400 text-slate-950 font-bold rounded text-xs transition-all shadow cursor-pointer flex items-center justify-center gap-1.5"
              >
                <Plus className="w-3.5 h-3.5" />
                <span>Record Audit Limitation Memo</span>
              </button>

              <div className="p-3 bg-[#070b14] rounded border border-slate-800 text-[11px] text-slate-400 space-y-1">
                <span className="text-emerald-400 font-bold block">Integrity Guarantee:</span>
                <span>The application will not fabricate missing transactions or tax values if a dataset is absent.</span>
              </div>
            </div>
          </div>

          {/* Audit Limitations Register */}
          <div className="bg-[#121c30] border border-slate-800 rounded-lg p-5 space-y-3">
            <h3 className="font-bold text-white text-sm flex items-center gap-2">
              <AlertTriangle className="w-4 h-4 text-amber-400" />
              <span>Registered Audit Limitations &amp; Mitigation Strategies</span>
            </h3>

            <div className="space-y-3">
              {limitations.map((lim) => (
                <div key={lim.id} className="p-4 bg-[#070b14] rounded-lg border border-slate-800 space-y-2">
                  <div className="flex items-center justify-between pb-1 border-b border-slate-800/80">
                    <div className="flex items-center gap-2">
                      <span className="font-mono text-amber-400 font-bold">{lim.id}</span>
                      <strong className="text-white text-xs">{lim.unavailableDataset}</strong>
                    </div>
                    <span className="text-[11px] text-slate-400">Recorded on {lim.recordedDate} by {lim.recordedBy}</span>
                  </div>

                  <div className="grid grid-cols-1 md:grid-cols-2 gap-3 text-[11px]">
                    <div>
                      <span className="text-slate-400 block">Reason for Non-Availability:</span>
                      <p className="text-slate-200 mt-0.5">{lim.reason}</p>
                    </div>
                    <div>
                      <span className="text-slate-400 block">Auditor Mitigation &amp; Alternate Substantive Procedures:</span>
                      <p className="text-emerald-300 mt-0.5">{lim.mitigationStrategy}</p>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </div>
      )}

      {/* TAB 3: RULE QUALITY & FALSE-POSITIVE CONTROL */}
      {activeTab === 'RULES_QUALITY' && (
        <div className="space-y-4">
          <div className="bg-[#121c30] border border-slate-800 rounded-lg p-4 flex flex-col md:flex-row md:items-center justify-between gap-3">
            <div className="flex items-center gap-3">
              <Search className="w-4 h-4 text-teal-400" />
              <input
                type="text"
                placeholder="Search audit rules by code, name, statutory section..."
                value={ruleSearchQuery}
                onChange={(e) => setRuleSearchQuery(e.target.value)}
                className="bg-[#070b14] border border-slate-700 rounded px-3 py-1.5 text-xs text-white placeholder-slate-500 w-64 md:w-80"
              />
            </div>

            <div className="flex items-center gap-1.5 text-[11px]">
              {(['ALL', 'GST', 'TDS', 'VOUCHERS', 'DUPLICATES', 'RECONCILIATION', 'JOURNALS'] as const).map((cat) => (
                <button
                  key={cat}
                  onClick={() => setRuleCategoryFilter(cat)}
                  className={`px-2.5 py-1 rounded font-semibold transition-all cursor-pointer ${
                    ruleCategoryFilter === cat
                      ? 'bg-teal-600 text-white shadow'
                      : 'bg-[#070b14] text-slate-400 hover:text-white border border-slate-800'
                  }`}
                >
                  {cat}
                </button>
              ))}
            </div>
          </div>

          <div className="space-y-3">
            {filteredRules.map((rule) => (
              <div key={rule.ruleCode} className="bg-[#121c30] border border-slate-800 rounded-lg p-4 space-y-3">
                <div className="flex items-center justify-between pb-2 border-b border-slate-800">
                  <div className="flex items-center gap-2">
                    <span className="font-mono text-xs text-teal-400 font-bold">[{rule.ruleCode}]</span>
                    <strong className="text-white text-xs">{rule.ruleName}</strong>
                    <span className="text-[10px] bg-slate-900 text-slate-400 font-mono px-1.5 py-0.2 rounded border border-slate-800">
                      v{rule.version}
                    </span>
                  </div>
                  <span className={`text-[10px] font-bold px-2 py-0.5 rounded border ${
                    rule.severity === 'HIGH'
                      ? 'bg-rose-950 text-rose-300 border-rose-800'
                      : rule.severity === 'REVIEW'
                      ? 'bg-amber-950 text-amber-300 border-amber-800'
                      : 'bg-sky-950 text-sky-300 border-sky-800'
                  }`}>
                    {rule.severity} (Potential Exception Requiring Review)
                  </span>
                </div>

                <div className="grid grid-cols-1 md:grid-cols-3 gap-3 text-[11px]">
                  <div className="p-2.5 bg-[#070b14] rounded border border-slate-800">
                    <span className="text-slate-400 font-bold block mb-1">WHAT HAPPENED &amp; TRIGGER:</span>
                    <p className="text-slate-200">{rule.whatHappened}</p>
                    <p className="text-amber-300/90 mt-1"><strong>Why Flagged:</strong> {rule.whyFlagged}</p>
                  </div>

                  <div className="p-2.5 bg-[#070b14] rounded border border-slate-800">
                    <span className="text-slate-400 font-bold block mb-1">FALSE-POSITIVE CONTROLS &amp; TOLERANCE:</span>
                    <p className="text-teal-300">{rule.falsePositiveControls}</p>
                    <div className="mt-1 font-mono text-[10px] text-slate-400">
                      Default Threshold: ₹{rule.defaultThreshold.toLocaleString()}
                    </div>
                  </div>

                  <div className="p-2.5 bg-[#070b14] rounded border border-slate-800">
                    <span className="text-slate-400 font-bold block mb-1">AUDITOR REVIEW GUIDANCE (SA 500):</span>
                    <p className="text-slate-300">{rule.auditorGuidance}</p>
                  </div>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* TAB 4: MATERIALITY & SAMPLING (SA 320 / SA 530) */}
      {activeTab === 'MATERIALITY_SAMPLING' && (
        <div className="space-y-4">
          {/* Materiality Card */}
          <div className="bg-[#121c30] border border-slate-800 rounded-lg p-5 space-y-4">
            <div className="flex items-center justify-between pb-2 border-b border-slate-800">
              <div>
                <h3 className="font-bold text-white text-sm flex items-center gap-2">
                  <Target className="w-4 h-4 text-purple-400" />
                  <span>Audit Materiality Benchmarks (Standard on Auditing 320)</span>
                </h3>
                <p className="text-[11px] text-slate-400">Decimal arithmetic precision based on synchronized financial statements.</p>
              </div>
              <span className="text-[11px] font-mono text-slate-400">
                Approved by {materiality.approvedBy} on {materiality.approvedAt}
              </span>
            </div>

            <div className="grid grid-cols-1 md:grid-cols-4 gap-3 text-xs">
              <div className="p-3 bg-[#070b14] rounded border border-slate-800">
                <span className="text-slate-400 text-[11px] block">Benchmark Revenue</span>
                <strong className="text-white text-base font-mono">₹{materiality.benchmarkFinancialValue.toLocaleString()}</strong>
                <span className="text-[10px] text-slate-400 block mt-0.5">Turnover / Sales</span>
              </div>

              <div className="p-3 bg-[#070b14] rounded border border-purple-900/60">
                <span className="text-purple-300 text-[11px] block font-bold">Overall Materiality (1.0%)</span>
                <strong className="text-purple-300 text-base font-mono">₹{materiality.overallMaterialityAmount.toLocaleString()}</strong>
                <span className="text-[10px] text-slate-400 block mt-0.5">SA 320 Overall Planning Level</span>
              </div>

              <div className="p-3 bg-[#070b14] rounded border border-slate-800">
                <span className="text-slate-400 text-[11px] block">Performance Materiality (75%)</span>
                <strong className="text-amber-300 text-base font-mono">₹{materiality.performanceMaterialityAmount.toLocaleString()}</strong>
                <span className="text-[10px] text-slate-400 block mt-0.5">Execution &amp; Testing Level</span>
              </div>

              <div className="p-3 bg-[#070b14] rounded border border-slate-800">
                <span className="text-slate-400 text-[11px] block">Clearly Trivial Threshold (5%)</span>
                <strong className="text-emerald-400 text-base font-mono">₹{materiality.clearlyTrivialThresholdAmount.toLocaleString()}</strong>
                <span className="text-[10px] text-slate-400 block mt-0.5">Below Which Findings Omitted</span>
              </div>
            </div>

            <p className="text-[11px] text-slate-300 bg-[#070b14] p-3 rounded border border-slate-800">
              <strong>Auditor Justification:</strong> {materiality.auditorJustification}
            </p>
          </div>

          {/* Sampling Header & Button */}
          <div className="flex items-center justify-between pt-2">
            <h3 className="font-bold text-white text-sm flex items-center gap-2">
              <BarChart3 className="w-4 h-4 text-teal-400" />
              <span>Statistical &amp; Material Sampling Runs (SA 530)</span>
            </h3>
            <button
              onClick={() => setIsAddSamplingOpen(true)}
              className="px-3 py-1.5 bg-teal-600 hover:bg-teal-500 text-white font-bold rounded shadow cursor-pointer flex items-center gap-1.5"
            >
              <Plus className="w-3.5 h-3.5" />
              <span>Execute New Sampling Run</span>
            </button>
          </div>

          {/* Sampling History Table */}
          <div className="bg-[#121c30] border border-slate-800 rounded-lg p-4 space-y-3">
            <div className="space-y-3">
              {samplingRuns.map((smp) => (
                <div key={smp.id} className="p-4 bg-[#070b14] rounded-lg border border-slate-800 space-y-2">
                  <div className="flex items-center justify-between pb-1 border-b border-slate-800/80">
                    <div className="flex items-center gap-2">
                      <span className="font-mono text-purple-400 font-bold">{smp.sampleCode}</span>
                      <strong className="text-white text-xs">{smp.auditArea}</strong>
                    </div>
                    <span className="text-[10px] bg-slate-800 text-slate-300 font-mono px-2 py-0.5 rounded border border-slate-700">
                      Method: {smp.method}
                    </span>
                  </div>

                  <div className="grid grid-cols-2 sm:grid-cols-4 gap-2 text-[11px] font-mono">
                    <div>
                      <span className="text-slate-400 block text-[10px]">Population Size</span>
                      <strong className="text-white">{smp.populationCount.toLocaleString()} items (₹{(smp.populationValue / 100000).toFixed(2)}L)</strong>
                    </div>
                    <div>
                      <span className="text-slate-400 block text-[10px]">Sample Size Selected</span>
                      <strong className="text-teal-300">{smp.sampleSize} vouchers (₹{(smp.sampleTotalValue / 100000).toFixed(2)}L)</strong>
                    </div>
                    <div>
                      <span className="text-slate-400 block text-[10px]">Random Seed / Rule</span>
                      <span className="text-slate-300">{smp.randomSeed} (Reproducible)</span>
                    </div>
                    <div>
                      <span className="text-slate-400 block text-[10px]">Exceptions Identified</span>
                      <strong className="text-amber-400">{smp.exceptionsFoundCount} Exceptions</strong>
                    </div>
                  </div>

                  <div className="pt-2 border-t border-slate-800/80 text-[11px] text-slate-300">
                    <span className="text-slate-400 font-bold">Auditor Conclusion: </span>
                    <span>{smp.auditorConclusion}</span>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </div>
      )}

      {/* TAB 5: AUDIT RUN VERSIONING & RE-RUN SAFETY */}
      {activeTab === 'RUN_HISTORY' && (
        <div className="space-y-4">
          <div className="bg-[#121c30] border border-slate-800 rounded-lg p-5 space-y-3">
            <div className="flex items-center justify-between pb-2 border-b border-slate-800">
              <h3 className="font-bold text-white text-sm flex items-center gap-2">
                <Clock className="w-4 h-4 text-sky-400" />
                <span>Audit Execution Run History &amp; Re-Run Idempotency</span>
              </h3>
              <span className="text-[10px] bg-emerald-950 text-emerald-300 font-mono px-2 py-0.5 rounded border border-emerald-800 font-bold">
                NO SILENT OVERWRITES
              </span>
            </div>

            <p className="text-[11px] text-slate-300 leading-relaxed">
              When an audit is executed multiple times on the same accounting snapshot, the engine preserves previous review histories, marks recurring findings, and tracks rule version evolution.
            </p>

            <div className="space-y-3 pt-2">
              {auditRuns.map((run) => (
                <div key={run.runId} className="p-4 bg-[#070b14] rounded-lg border border-slate-800 space-y-3">
                  <div className="flex items-center justify-between pb-2 border-b border-slate-800/80">
                    <div className="flex items-center gap-2">
                      <span className="font-mono text-teal-400 font-bold text-xs">{run.runId}</span>
                      <span className="text-[10px] bg-slate-800 text-slate-300 font-mono px-1.5 py-0.2 rounded border border-slate-700">
                        Ruleset: {run.ruleSetVersion}
                      </span>
                    </div>
                    <span className="bg-emerald-950 text-emerald-300 text-[10px] font-bold px-2 py-0.5 rounded border border-emerald-800 font-mono">
                      {run.status} ({run.durationSeconds}s)
                    </span>
                  </div>

                  <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 text-[11px] font-mono">
                    <div>
                      <span className="text-slate-400 block text-[10px]">Transactions Analyzed</span>
                      <strong className="text-white text-xs">{run.transactionsAnalyzed.toLocaleString()}</strong>
                    </div>
                    <div>
                      <span className="text-slate-400 block text-[10px]">Rules Executed</span>
                      <strong className="text-white text-xs">{run.rulesExecuted} Rules</strong>
                    </div>
                    <div>
                      <span className="text-slate-400 block text-[10px]">Findings Identified</span>
                      <strong className="text-amber-400 text-xs">{run.findingsCount} (High: {run.highPriorityCount}, Review: {run.reviewCount})</strong>
                    </div>
                    <div>
                      <span className="text-slate-400 block text-[10px]">Recurring Findings</span>
                      <strong className="text-sky-300 text-xs">{run.recurringFindingsCount} Tracked</strong>
                    </div>
                  </div>

                  <div className="pt-2 border-t border-slate-800/80 flex items-center justify-between text-[11px] text-slate-400">
                    <span>Executed by: <strong className="text-slate-200">{run.executedBy}</strong></span>
                    <span>Start: {run.startTime} • End: {run.endTime}</span>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </div>
      )}

      {/* MODAL: RECORD AUDIT LIMITATION */}
      {isAddLimitationOpen && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm flex items-center justify-center z-[95] p-4">
          <div className="bg-[#0f172a] border border-slate-700 rounded-2xl max-w-lg w-full p-6 space-y-4 shadow-2xl text-xs">
            <div className="flex items-center justify-between pb-3 border-b border-slate-800">
              <h3 className="font-bold text-white text-sm flex items-center gap-2">
                <AlertTriangle className="w-4 h-4 text-amber-400" />
                <span>Record Audit Limitation Memo</span>
              </h3>
              <button onClick={() => setIsAddLimitationOpen(false)} className="text-slate-400 hover:text-white cursor-pointer font-bold">✕</button>
            </div>

            <div className="space-y-3">
              <div>
                <label className="text-[10px] text-slate-400 uppercase block mb-1">Unavailable Dataset / Source</label>
                <input
                  type="text"
                  value={newLimDataset}
                  onChange={(e) => setNewLimDataset(e.target.value)}
                  className="w-full bg-[#070b14] border border-slate-700 rounded px-3 py-1.5 text-xs text-white"
                />
              </div>

              <div>
                <label className="text-[10px] text-slate-400 uppercase block mb-1">Reason for Non-Availability</label>
                <textarea
                  rows={2}
                  value={newLimReason}
                  onChange={(e) => setNewLimReason(e.target.value)}
                  className="w-full bg-[#070b14] border border-slate-700 rounded px-3 py-1.5 text-xs text-white"
                />
              </div>

              <div>
                <label className="text-[10px] text-slate-400 uppercase block mb-1">Affected Audit Areas</label>
                <input
                  type="text"
                  value={newLimAreas}
                  onChange={(e) => setNewLimAreas(e.target.value)}
                  className="w-full bg-[#070b14] border border-slate-700 rounded px-3 py-1.5 text-xs text-white"
                />
              </div>

              <div>
                <label className="text-[10px] text-slate-400 uppercase block mb-1">Auditor Alternate Mitigation Strategy</label>
                <textarea
                  rows={2}
                  value={newLimStrategy}
                  onChange={(e) => setNewLimStrategy(e.target.value)}
                  className="w-full bg-[#070b14] border border-slate-700 rounded px-3 py-1.5 text-xs text-white"
                />
              </div>
            </div>

            <div className="flex justify-end gap-2 pt-3 border-t border-slate-800">
              <button
                onClick={() => setIsAddLimitationOpen(false)}
                className="px-4 py-1.5 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded font-semibold cursor-pointer"
              >
                Cancel
              </button>
              <button
                onClick={handleAddLimitation}
                className="px-4 py-1.5 bg-amber-600 hover:bg-amber-500 text-slate-950 font-bold rounded shadow cursor-pointer"
              >
                Save Limitation Memo
              </button>
            </div>
          </div>
        </div>
      )}

      {/* MODAL: EXECUTE SAMPLING RUN */}
      {isAddSamplingOpen && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm flex items-center justify-center z-[95] p-4">
          <div className="bg-[#0f172a] border border-slate-700 rounded-2xl max-w-lg w-full p-6 space-y-4 shadow-2xl text-xs">
            <div className="flex items-center justify-between pb-3 border-b border-slate-800">
              <h3 className="font-bold text-white text-sm flex items-center gap-2">
                <BarChart3 className="w-4 h-4 text-teal-400" />
                <span>Configure &amp; Execute Sampling Run</span>
              </h3>
              <button onClick={() => setIsAddSamplingOpen(false)} className="text-slate-400 hover:text-white cursor-pointer font-bold">✕</button>
            </div>

            <div className="space-y-3">
              <div>
                <label className="text-[10px] text-slate-400 uppercase block mb-1">Audit Population Area</label>
                <input
                  type="text"
                  value={sampleArea}
                  onChange={(e) => setSampleArea(e.target.value)}
                  className="w-full bg-[#070b14] border border-slate-700 rounded px-3 py-1.5 text-xs text-white"
                />
              </div>

              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="text-[10px] text-slate-400 uppercase block mb-1">Sampling Method</label>
                  <select
                    value={sampleMethod}
                    onChange={(e) => setSampleMethod(e.target.value as any)}
                    className="w-full bg-[#070b14] border border-slate-700 rounded px-3 py-1.5 text-xs text-white"
                  >
                    <option value="MATERIAL_ITEM">Material Item Sampling (Exceeding Threshold)</option>
                    <option value="SYSTEMATIC">Systematic Interval Sampling</option>
                    <option value="RANDOM">Random Pseudo-Seed Sampling</option>
                  </select>
                </div>
                <div>
                  <label className="text-[10px] text-slate-400 uppercase block mb-1">Sample Target Size</label>
                  <input
                    type="number"
                    value={sampleSize}
                    onChange={(e) => setSampleSize(Number(e.target.value))}
                    className="w-full bg-[#070b14] border border-slate-700 rounded px-3 py-1.5 text-xs text-white font-mono"
                  />
                </div>
              </div>

              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="text-[10px] text-slate-400 uppercase block mb-1">Population Count</label>
                  <input
                    type="number"
                    value={samplePopCount}
                    onChange={(e) => setSamplePopCount(Number(e.target.value))}
                    className="w-full bg-[#070b14] border border-slate-700 rounded px-3 py-1.5 text-xs text-white font-mono"
                  />
                </div>
                <div>
                  <label className="text-[10px] text-slate-400 uppercase block mb-1">Population Total Value (₹)</label>
                  <input
                    type="number"
                    value={samplePopVal}
                    onChange={(e) => setSamplePopVal(Number(e.target.value))}
                    className="w-full bg-[#070b14] border border-slate-700 rounded px-3 py-1.5 text-xs text-white font-mono"
                  />
                </div>
              </div>
            </div>

            <div className="flex justify-end gap-2 pt-3 border-t border-slate-800">
              <button
                onClick={() => setIsAddSamplingOpen(false)}
                className="px-4 py-1.5 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded font-semibold cursor-pointer"
              >
                Cancel
              </button>
              <button
                onClick={handleCreateSampleRun}
                className="px-4 py-1.5 bg-teal-600 hover:bg-teal-500 text-white font-bold rounded shadow cursor-pointer"
              >
                Execute Sample Selection
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
