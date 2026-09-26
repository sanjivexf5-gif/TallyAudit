import React, { useState } from 'react';
import {
  Building2,
  Calendar,
  Layers,
  CheckCircle,
  Clock,
  ArrowRight,
  TrendingUp,
  TrendingDown,
  AlertTriangle,
  RotateCcw,
  Plus,
  RefreshCw,
  Search,
  Filter,
  Shield,
  FileText,
  FileCheck,
  CheckSquare,
  Lock,
  Unlock,
  ExternalLink,
  ChevronRight,
  DollarSign,
  Briefcase,
  SlidersHorizontal,
  Info,
  GitCompare,
  BarChart3,
  Scale,
  Sparkles,
  Zap,
  FolderTree,
  AlertCircle
} from 'lucide-react';
import {
  CompanyWorkspace,
  FinancialPeriodInfo,
  YoYFinancialMetric,
  YoYVolumeMetric,
  RecurringFindingItem,
  YoYFindingComparison,
  CompanyYearDataStore,
  calculateVariance
} from './companyData';

interface CompanyWorkspaceViewProps {
  companies: CompanyWorkspace[];
  activeCompany: CompanyWorkspace;
  activeFinancialYearId: string;
  yearDataStore: CompanyYearDataStore;
  onSwitchCompany: (company: CompanyWorkspace) => void;
  onSwitchFinancialYear: (yearId: string) => void;
  onRollForwardYear: (targetYear: string, targetYearId: string) => void;
  onAddCompany: (newCompany: CompanyWorkspace) => void;
  onScanTallyCompanies: () => void;
  isScanningTally: boolean;
  tallyConnected: boolean;
  onNavigateToAudit: () => void;
  onNavigateToPlanning: () => void;
  onNavigateToReports: () => void;
}

export const CompanyWorkspaceView: React.FC<CompanyWorkspaceViewProps> = ({
  companies,
  activeCompany,
  activeFinancialYearId,
  yearDataStore,
  onSwitchCompany,
  onSwitchFinancialYear,
  onRollForwardYear,
  onAddCompany,
  onScanTallyCompanies,
  isScanningTally,
  tallyConnected,
  onNavigateToAudit,
  onNavigateToPlanning,
  onNavigateToReports
}) => {
  const [activeTab, setActiveTab] = useState<'workspaces' | 'yoy-analysis' | 'recurring-issues' | 'analytical-memo'>('workspaces');
  const [searchQuery, setSearchQuery] = useState<string>('');
  const [statusFilter, setStatusFilter] = useState<string>('ALL');

  // Modals state
  const [isSwitchCompanyModalOpen, setIsSwitchCompanyModalOpen] = useState<boolean>(false);
  const [pendingCompanyToSwitch, setPendingCompanyToSwitch] = useState<CompanyWorkspace | null>(null);
  const [isOnboardModalOpen, setIsOnboardModalOpen] = useState<boolean>(false);
  const [isRollForwardModalOpen, setIsRollForwardModalOpen] = useState<boolean>(false);
  const [selectedMetricCategory, setSelectedMetricCategory] = useState<string>('ALL');

  // Form states for new company onboarding
  const [newCompanyName, setNewCompanyName] = useState<string>('');
  const [newLegalName, setNewLegalName] = useState<string>('');
  const [newTallyNumber, setNewTallyNumber] = useState<string>('10005');
  const [newPan, setNewPan] = useState<string>('');
  const [newGstin, setNewGstin] = useState<string>('');
  const [newState, setNewState] = useState<string>('Maharashtra');
  const [newIndustry, setNewIndustry] = useState<string>('Manufacturing');
  const [newAuditPartner, setNewAuditPartner] = useState<string>('CA. Sanjiv (Senior Partner)');
  const [newStartFy, setNewStartFy] = useState<string>('FY 2025-26');

  // Form state for roll-forward
  const [rollForwardTargetFy, setRollForwardTargetFy] = useState<string>('FY 2026-27');

  const activeFyObj = activeCompany.financialYears.find(f => f.id === activeFinancialYearId) || activeCompany.financialYears[0];
  const priorFyObj = activeCompany.financialYears.find(f => f.id !== activeFinancialYearId && f.isAuditFinalized) || activeCompany.financialYears[0];

  const filteredCompanies = companies.filter(c => {
    if (statusFilter !== 'ALL' && c.status !== statusFilter) return false;
    if (searchQuery) {
      const q = searchQuery.toLowerCase();
      return (
        c.name.toLowerCase().includes(q) ||
        c.gstin.toLowerCase().includes(q) ||
        c.pan.toLowerCase().includes(q) ||
        c.industry.toLowerCase().includes(q) ||
        c.state.toLowerCase().includes(q)
      );
    }
    return true;
  });

  const handleInitiateSwitch = (company: CompanyWorkspace) => {
    if (company.id === activeCompany.id) return;
    setPendingCompanyToSwitch(company);
    setIsSwitchCompanyModalOpen(true);
  };

  const handleConfirmSwitch = () => {
    if (pendingCompanyToSwitch) {
      onSwitchCompany(pendingCompanyToSwitch);
      setIsSwitchCompanyModalOpen(false);
      setPendingCompanyToSwitch(null);
    }
  };

  const handleCreateCompanySubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!newCompanyName.trim()) return;

    const newCompany: CompanyWorkspace = {
      id: `COMP-00${companies.length + 1}`,
      name: newCompanyName.trim(),
      legalName: newLegalName.trim() || newCompanyName.trim(),
      tallyCompanyIdentifier: `TALLY-guid-${Date.now().toString().slice(-6)}`,
      tallyNumber: newTallyNumber.trim(),
      pan: newPan.trim() || 'AABCA1234F',
      gstin: newGstin.trim() || '27AABCA1234F1Z5',
      state: newState,
      stateCode: newState.includes('Maharashtra') ? '27' : '07',
      industry: newIndustry,
      natureOfBusiness: `${newIndustry} Services & Trading Operations`,
      registeredAddress: 'Corporate Office, Registered Business Park, India',
      auditPartner: newAuditPartner,
      booksBeginningFrom: '01-Apr-2024',
      activeFinancialYearId: `FY-${newStartFy.replace('FY ', '')}`,
      currency: 'INR (₹)',
      lastSyncAt: 'Never (Pending First Sync)',
      status: 'Active',
      auditStatus: 'Planning',
      financialYears: [
        {
          id: `FY-${newStartFy.replace('FY ', '')}`,
          label: newStartFy,
          startDate: `01-Apr-${newStartFy.slice(3, 7)}`,
          endDate: `31-Mar-20${newStartFy.slice(8, 10)}`,
          assessmentYear: `AY 20${Number(newStartFy.slice(8, 10)) + 1}-${Number(newStartFy.slice(8, 10)) + 2}`,
          isCurrent: true,
          isAuditFinalized: false,
          vouchersCount: 0,
          ledgersCount: 0
        }
      ]
    };

    onAddCompany(newCompany);
    setIsOnboardModalOpen(false);
    // Reset form
    setNewCompanyName('');
    setNewLegalName('');
    setNewPan('');
    setNewGstin('');
  };

  const handleRollForwardSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    const targetId = `FY-${rollForwardTargetFy.replace('FY ', '')}`;
    onRollForwardYear(rollForwardTargetFy, targetId);
    setIsRollForwardModalOpen(false);
  };

  const filteredMetrics = yearDataStore.financialMetrics.filter(m => {
    if (selectedMetricCategory === 'ALL') return true;
    return m.category === selectedMetricCategory;
  });

  return (
    <div className="space-y-5 pb-10">
      {/* TOP HEADER: ACTIVE COMPANY & PERIOD BAR */}
      <div className="bg-[#0f172a] rounded-lg border border-slate-800 p-4 shadow-md">
        <div className="flex flex-col lg:flex-row lg:items-center justify-between gap-4">
          <div className="flex items-center gap-3">
            <div className="w-12 h-12 rounded-lg bg-teal-500/20 border border-teal-500/40 flex items-center justify-center text-teal-300">
              <Building2 className="w-6 h-6" />
            </div>
            <div>
              <div className="flex items-center gap-2">
                <span className="text-[11px] font-bold uppercase tracking-wider text-teal-400">Company Workspace Center</span>
                <span className="bg-emerald-950 text-emerald-300 border border-emerald-800 text-[10px] px-2 py-0.5 rounded font-mono font-semibold">
                  Tally ID: #{activeCompany.tallyNumber}
                </span>
                <span className={`text-[10px] px-2 py-0.5 rounded font-semibold border ${
                  activeCompany.auditStatus === 'In Progress' ? 'bg-amber-950 text-amber-300 border-amber-800' :
                  activeCompany.auditStatus === 'Planning' ? 'bg-blue-950 text-blue-300 border-blue-800' :
                  'bg-emerald-950 text-emerald-300 border-emerald-800'
                }`}>
                  {activeCompany.auditStatus}
                </span>
              </div>
              <h2 className="text-xl font-bold text-white tracking-tight flex items-center gap-2 mt-0.5">
                {activeCompany.name}
              </h2>
              <div className="text-xs text-slate-400 flex flex-wrap items-center gap-x-4 gap-y-1 mt-1">
                <span>GSTIN: <strong className="text-slate-200 font-mono">{activeCompany.gstin}</strong></span>
                <span>PAN: <strong className="text-slate-200 font-mono">{activeCompany.pan}</strong></span>
                <span>State: <strong className="text-slate-200">{activeCompany.state} ({activeCompany.stateCode})</strong></span>
                <span>Industry: <strong className="text-slate-300">{activeCompany.industry}</strong></span>
              </div>
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            {/* Financial Year Selector Quick Dropdown */}
            <div className="bg-[#121c32] px-3 py-1.5 rounded border border-slate-700 text-xs flex items-center gap-2">
              <Calendar className="w-3.5 h-3.5 text-teal-400" />
              <span className="text-slate-400">Current Period:</span>
              <select
                value={activeFinancialYearId}
                onChange={(e) => onSwitchFinancialYear(e.target.value)}
                className="bg-[#0b101e] text-teal-300 font-bold border border-slate-700 rounded px-2 py-1 focus:outline-none focus:border-teal-500"
              >
                {activeCompany.financialYears.map(fy => (
                  <option key={fy.id} value={fy.id}>
                    {fy.label} ({fy.startDate} to {fy.endDate}) {fy.isAuditFinalized ? '🔒 Finalized' : '📝 Active'}
                  </option>
                ))}
              </select>
            </div>

            <button
              onClick={() => setIsRollForwardModalOpen(true)}
              className="flex items-center gap-1.5 px-3 py-2 bg-indigo-900/60 hover:bg-indigo-800/80 text-indigo-200 rounded border border-indigo-700/80 text-xs font-semibold transition-all cursor-pointer"
            >
              <RotateCcw className="w-3.5 h-3.5 text-indigo-300" />
              <span>Roll-Forward FY</span>
            </button>

            <button
              onClick={() => setIsOnboardModalOpen(true)}
              className="flex items-center gap-1.5 px-3 py-2 bg-teal-600 hover:bg-teal-500 text-white rounded text-xs font-semibold shadow transition-all cursor-pointer"
            >
              <Plus className="w-3.5 h-3.5" />
              <span>Add Company</span>
            </button>
          </div>
        </div>

        {/* ISOLATION & INTEGRITY GUARANTEE BANNER */}
        <div className="mt-4 pt-3 border-t border-slate-800/80 flex flex-col sm:flex-row sm:items-center justify-between gap-2 text-xs text-slate-400">
          <div className="flex items-center gap-2">
            <Shield className="w-4 h-4 text-emerald-400 shrink-0" />
            <span>
              <strong>Company Isolation Active:</strong> All vouchers, findings, working papers, and reports are partitioned under <span className="text-teal-300 font-mono font-semibold">{activeCompany.id} / {activeFyObj?.label}</span> in SQLite.
            </span>
          </div>
          <div className="flex items-center gap-4 text-slate-400">
            <span>Audit Partner: <strong className="text-slate-300">{activeCompany.auditPartner.split('(')[0]}</strong></span>
            <span>Last Sync: <strong className="text-slate-300">{activeCompany.lastSyncAt}</strong></span>
          </div>
        </div>
      </div>

      {/* NAVIGATION TABS */}
      <div className="flex flex-wrap items-center gap-2 border-b border-slate-800 pb-2">
        <button
          onClick={() => setActiveTab('workspaces')}
          className={`flex items-center gap-2 px-4 py-2 rounded-md font-semibold text-xs transition-all ${
            activeTab === 'workspaces'
              ? 'bg-teal-600 text-white shadow-sm'
              : 'text-slate-400 hover:bg-slate-800 hover:text-white'
          }`}
        >
          <Building2 className="w-4 h-4" />
          <span>All Companies ({companies.length})</span>
        </button>

        <button
          onClick={() => setActiveTab('yoy-analysis')}
          className={`flex items-center gap-2 px-4 py-2 rounded-md font-semibold text-xs transition-all ${
            activeTab === 'yoy-analysis'
              ? 'bg-teal-600 text-white shadow-sm'
              : 'text-slate-400 hover:bg-slate-800 hover:text-white'
          }`}
        >
          <GitCompare className="w-4 h-4 text-sky-400" />
          <span>Year-over-Year (YoY) Analysis</span>
          <span className="bg-sky-950 text-sky-300 text-[10px] px-1.5 py-0.5 rounded border border-sky-800 font-mono">
            {activeFyObj?.label} vs {priorFyObj?.label}
          </span>
        </button>

        <button
          onClick={() => setActiveTab('recurring-issues')}
          className={`flex items-center gap-2 px-4 py-2 rounded-md font-semibold text-xs transition-all ${
            activeTab === 'recurring-issues'
              ? 'bg-teal-600 text-white shadow-sm'
              : 'text-slate-400 hover:bg-slate-800 hover:text-white'
          }`}
        >
          <AlertTriangle className="w-4 h-4 text-amber-400" />
          <span>Recurring Exceptions</span>
          <span className="bg-amber-950 text-amber-300 text-[10px] px-1.5 py-0.5 rounded border border-amber-800 font-mono">
            {yearDataStore.recurringFindings.length}
          </span>
        </button>

        <button
          onClick={() => setActiveTab('analytical-memo')}
          className={`flex items-center gap-2 px-4 py-2 rounded-md font-semibold text-xs transition-all ${
            activeTab === 'analytical-memo'
              ? 'bg-teal-600 text-white shadow-sm'
              : 'text-slate-400 hover:bg-slate-800 hover:text-white'
          }`}
        >
          <FileText className="w-4 h-4 text-emerald-400" />
          <span>SA 520 Analytical Memo</span>
        </button>

        <div className="ml-auto flex items-center gap-2">
          <button
            onClick={onScanTallyCompanies}
            disabled={isScanningTally}
            className="flex items-center gap-1.5 px-3 py-1.5 bg-slate-800 hover:bg-slate-700 text-teal-300 rounded border border-slate-700 text-xs font-medium transition-all"
          >
            <RefreshCw className={`w-3.5 h-3.5 ${isScanningTally ? 'animate-spin' : ''}`} />
            <span>{isScanningTally ? 'Detecting Tally Companies...' : 'Scan Tally Port 9000'}</span>
          </button>
        </div>
      </div>

      {/* TAB 1: WORKSPACES LIST & MANAGEMENT */}
      {activeTab === 'workspaces' && (
        <div className="space-y-4">
          {/* Filter Bar */}
          <div className="flex flex-col sm:flex-row items-center justify-between gap-3 bg-[#0d1527] p-3 rounded-lg border border-slate-800">
            <div className="relative w-full sm:w-80">
              <Search className="w-4 h-4 absolute left-3 top-2.5 text-slate-500" />
              <input
                type="text"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                placeholder="Search by company name, GSTIN, PAN, state..."
                className="w-full bg-[#070b14] border border-slate-800 rounded pl-9 pr-3 py-1.5 text-xs text-slate-200 placeholder-slate-500 focus:outline-none focus:border-teal-500"
              />
            </div>

            <div className="flex items-center gap-2 w-full sm:w-auto">
              <span className="text-xs text-slate-400">Status:</span>
              <select
                value={statusFilter}
                onChange={(e) => setStatusFilter(e.target.value)}
                className="bg-[#070b14] border border-slate-800 rounded px-2.5 py-1.5 text-xs text-slate-300 focus:outline-none focus:border-teal-500"
              >
                <option value="ALL">All Statuses</option>
                <option value="Active">Active</option>
                <option value="Archived">Archived</option>
              </select>
            </div>
          </div>

          {/* Company Cards Grid */}
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            {filteredCompanies.map(comp => {
              const isActive = comp.id === activeCompany.id;
              return (
                <div
                  key={comp.id}
                  className={`bg-[#0f172a] rounded-lg border p-4 transition-all flex flex-col justify-between ${
                    isActive
                      ? 'border-teal-500/80 shadow-md ring-1 ring-teal-500/30'
                      : 'border-slate-800 hover:border-slate-700'
                  }`}
                >
                  <div>
                    {/* Header */}
                    <div className="flex items-start justify-between gap-2">
                      <div className="flex items-start gap-3">
                        <div className={`w-10 h-10 rounded-md flex items-center justify-center font-bold text-sm shrink-0 ${
                          isActive
                            ? 'bg-teal-500/20 border border-teal-500/40 text-teal-300'
                            : 'bg-slate-800 text-slate-400'
                        }`}>
                          <Building2 className="w-5 h-5" />
                        </div>
                        <div>
                          <div className="flex items-center gap-2">
                            <span className="text-xs font-mono font-bold text-teal-400">{comp.id}</span>
                            <span className="text-[10px] text-slate-400">Tally #{comp.tallyNumber}</span>
                            {isActive && (
                              <span className="bg-teal-950 text-teal-300 border border-teal-800 text-[10px] px-2 py-0.2 rounded font-bold uppercase tracking-wider">
                                Currently Selected
                              </span>
                            )}
                          </div>
                          <h3 className="text-base font-bold text-white mt-0.5 leading-snug">{comp.name}</h3>
                          <p className="text-xs text-slate-400">{comp.legalName}</p>
                        </div>
                      </div>

                      <span className={`text-[10px] font-semibold px-2 py-0.5 rounded border shrink-0 ${
                        comp.auditStatus === 'In Progress' ? 'bg-amber-950 text-amber-300 border-amber-800' :
                        comp.auditStatus === 'Planning' ? 'bg-blue-950 text-blue-300 border-blue-800' :
                        'bg-emerald-950 text-emerald-300 border-emerald-800'
                      }`}>
                        {comp.auditStatus}
                      </span>
                    </div>

                    {/* Metadata Details */}
                    <div className="grid grid-cols-2 gap-2 mt-4 bg-[#0a0f1d] p-3 rounded border border-slate-800/80 text-xs">
                      <div>
                        <span className="text-[11px] text-slate-400 block">GSTIN</span>
                        <span className="font-mono text-slate-200 font-semibold">{comp.gstin}</span>
                      </div>
                      <div>
                        <span className="text-[11px] text-slate-400 block">PAN</span>
                        <span className="font-mono text-slate-200 font-semibold">{comp.pan}</span>
                      </div>
                      <div>
                        <span className="text-[11px] text-slate-400 block">State Jurisdiction</span>
                        <span className="text-slate-300">{comp.state} ({comp.stateCode})</span>
                      </div>
                      <div>
                        <span className="text-[11px] text-slate-400 block">Industry</span>
                        <span className="text-slate-300 truncate block">{comp.industry}</span>
                      </div>
                    </div>

                    {/* Financial Years Supported */}
                    <div className="mt-3">
                      <span className="text-[11px] text-slate-400 font-medium block mb-1.5">Financial Periods & Status:</span>
                      <div className="flex flex-wrap gap-1.5">
                        {comp.financialYears.map(fy => (
                          <span
                            key={fy.id}
                            className={`text-[11px] px-2 py-0.5 rounded font-mono flex items-center gap-1 border ${
                              fy.id === activeFinancialYearId && isActive
                                ? 'bg-teal-900/60 text-teal-200 border-teal-600 font-bold'
                                : 'bg-[#070b14] text-slate-400 border-slate-800'
                            }`}
                          >
                            {fy.isAuditFinalized ? <Lock className="w-3 h-3 text-emerald-400" /> : <Unlock className="w-3 h-3 text-amber-400" />}
                            <span>{fy.label}</span>
                            <span className="text-[9px] text-slate-400">({(fy.vouchersCount).toLocaleString()} vchr)</span>
                          </span>
                        ))}
                      </div>
                    </div>
                  </div>

                  {/* Actions Bar */}
                  <div className="mt-4 pt-3 border-t border-slate-800/80 flex items-center justify-between">
                    <span className="text-[11px] text-slate-400">
                      Sync: <strong className="text-slate-300">{comp.lastSyncAt}</strong>
                    </span>

                    <div className="flex items-center gap-2">
                      {isActive ? (
                        <div className="flex items-center gap-2">
                          <button
                            onClick={onNavigateToAudit}
                            className="flex items-center gap-1 px-3 py-1.5 bg-slate-800 hover:bg-slate-700 text-teal-300 rounded text-xs font-semibold transition-all"
                          >
                            <Zap className="w-3.5 h-3.5" />
                            <span>Audit Rules</span>
                          </button>
                          <button
                            onClick={onNavigateToPlanning}
                            className="flex items-center gap-1 px-3 py-1.5 bg-slate-800 hover:bg-slate-700 text-amber-300 rounded text-xs font-semibold transition-all"
                          >
                            <CheckSquare className="w-3.5 h-3.5" />
                            <span>Audit Plan</span>
                          </button>
                        </div>
                      ) : (
                        <button
                          onClick={() => handleInitiateSwitch(comp)}
                          className="flex items-center gap-1.5 px-3 py-1.5 bg-teal-600 hover:bg-teal-500 text-white rounded text-xs font-bold transition-all shadow cursor-pointer"
                        >
                          <Building2 className="w-3.5 h-3.5" />
                          <span>Switch to Company</span>
                        </button>
                      )}
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      )}

      {/* TAB 2: YEAR-OVER-YEAR (YoY) COMPARATIVE ANALYSIS */}
      {activeTab === 'yoy-analysis' && (
        <div className="space-y-5">
          {/* Overview KPI Cards */}
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
            <div className="bg-[#0f172a] p-4 rounded-lg border border-slate-800">
              <span className="text-[11px] text-slate-400 font-medium uppercase tracking-wider">Topline Revenue Growth</span>
              <div className="flex items-baseline gap-2 mt-1">
                <span className="text-2xl font-bold text-white">₹18.42 Cr</span>
                <span className="text-xs font-bold text-emerald-400 flex items-center">
                  <TrendingUp className="w-3 h-3 mr-0.5" /> +22.02%
                </span>
              </div>
              <span className="text-[11px] text-slate-400 mt-1 block">Prior FY: ₹15.10 Cr (+₹3.32 Cr)</span>
            </div>

            <div className="bg-[#0f172a] p-4 rounded-lg border border-slate-800">
              <span className="text-[11px] text-slate-400 font-medium uppercase tracking-wider">Gross Trading Margin</span>
              <div className="flex items-baseline gap-2 mt-1">
                <span className="text-2xl font-bold text-white">38.99%</span>
                <span className="text-xs font-bold text-emerald-400 flex items-center">
                  <TrendingUp className="w-3 h-3 mr-0.5" /> +157 bps
                </span>
              </div>
              <span className="text-[11px] text-slate-400 mt-1 block">Prior FY: 37.42% (₹7.18 Cr vs ₹5.65 Cr)</span>
            </div>

            <div className="bg-[#0f172a] p-4 rounded-lg border border-slate-800">
              <span className="text-[11px] text-slate-400 font-medium uppercase tracking-wider">Total Voucher Throughput</span>
              <div className="flex items-baseline gap-2 mt-1">
                <span className="text-2xl font-bold text-white">15,400</span>
                <span className="text-xs font-bold text-sky-400 flex items-center">
                  <TrendingUp className="w-3 h-3 mr-0.5" /> +17.92%
                </span>
              </div>
              <span className="text-[11px] text-slate-400 mt-1 block">Prior FY: 13,060 (+2,340 vouchers)</span>
            </div>

            <div className="bg-[#0f172a] p-4 rounded-lg border border-slate-800">
              <span className="text-[11px] text-slate-400 font-medium uppercase tracking-wider">Audit Exceptions Identified</span>
              <div className="flex items-baseline gap-2 mt-1">
                <span className="text-2xl font-bold text-amber-400">24</span>
                <span className="text-xs font-bold text-rose-400 flex items-center">
                  <TrendingUp className="w-3 h-3 mr-0.5" /> +26.3%
                </span>
              </div>
              <span className="text-[11px] text-slate-400 mt-1 block">Prior FY: 19 exceptions (3 Recurring)</span>
            </div>
          </div>

          {/* Section 1: Financial Statement Caption Variances */}
          <div className="bg-[#0f172a] rounded-lg border border-slate-800 p-4">
            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 mb-4">
              <div>
                <h3 className="text-sm font-bold text-white flex items-center gap-2">
                  <BarChart3 className="w-4 h-4 text-teal-400" />
                  <span>1. Financial Statement Metric Variances (SA 520 Analytical Review)</span>
                </h3>
                <p className="text-xs text-slate-400">
                  Comparing {activeCompany.name} figures for {activeFyObj?.label} against prior period {priorFyObj?.label}.
                </p>
              </div>

              {/* Category Filter */}
              <div className="flex items-center gap-2">
                <span className="text-xs text-slate-400">Category:</span>
                <select
                  value={selectedMetricCategory}
                  onChange={(e) => setSelectedMetricCategory(e.target.value)}
                  className="bg-[#070b14] border border-slate-800 rounded px-2.5 py-1 text-xs text-slate-200 focus:outline-none focus:border-teal-500"
                >
                  <option value="ALL">All Financial Captions</option>
                  <option value="Revenue">Revenue & Incomes</option>
                  <option value="Purchases">Purchases & COGS</option>
                  <option value="Operating Expenses">Operating Overheads</option>
                  <option value="Receivables">Receivables (Sundry Debtors)</option>
                  <option value="Payables">Payables (Sundry Creditors)</option>
                  <option value="Cash & Bank">Liquid Cash & Bank</option>
                  <option value="Duties & Taxes">Statutory Taxes (GST/TDS)</option>
                </select>
              </div>
            </div>

            <div className="overflow-x-auto">
              <table className="w-full text-xs text-left">
                <thead className="bg-[#0a0f1d] text-slate-400 uppercase text-[10px] tracking-wider border-b border-slate-800">
                  <tr>
                    <th className="py-2.5 px-3">Caption & Ledger Head</th>
                    <th className="py-2.5 px-3 text-right">Prior Year ({priorFyObj?.label})</th>
                    <th className="py-2.5 px-3 text-right">Current Year ({activeFyObj?.label})</th>
                    <th className="py-2.5 px-3 text-right">Variance (₹)</th>
                    <th className="py-2.5 px-3 text-right">% Change</th>
                    <th className="py-2.5 px-3 text-center">Alert</th>
                    <th className="py-2.5 px-3">Auditor Analytical Review Notes</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-800/60 font-sans">
                  {filteredMetrics.map((metric, idx) => (
                    <tr key={idx} className="hover:bg-slate-800/30 transition-colors">
                      <td className="py-2.5 px-3">
                        <span className="font-semibold text-slate-200 block">{metric.metricName}</span>
                        <span className="text-[10px] text-slate-400">{metric.accountHead}</span>
                      </td>
                      <td className="py-2.5 px-3 text-right font-mono text-slate-300">
                        ₹{(metric.priorYearAmount).toLocaleString('en-IN')}
                      </td>
                      <td className="py-2.5 px-3 text-right font-mono font-bold text-white">
                        ₹{(metric.currentYearAmount).toLocaleString('en-IN')}
                      </td>
                      <td className="py-2.5 px-3 text-right font-mono font-semibold">
                        <span className={metric.varianceAmount >= 0 ? 'text-emerald-400' : 'text-rose-400'}>
                          {metric.varianceAmount >= 0 ? '+' : ''}₹{metric.varianceAmount.toLocaleString('en-IN')}
                        </span>
                      </td>
                      <td className="py-2.5 px-3 text-right font-mono font-bold">
                        <span className={`inline-flex items-center gap-0.5 px-1.5 py-0.5 rounded text-[11px] ${
                          metric.variancePercent >= 0 ? 'text-emerald-300 bg-emerald-950/60' : 'text-rose-300 bg-rose-950/60'
                        }`}>
                          {metric.variancePercent >= 0 ? <TrendingUp className="w-3 h-3" /> : <TrendingDown className="w-3 h-3" />}
                          {metric.variancePercent >= 0 ? '+' : ''}{metric.variancePercent}%
                        </span>
                      </td>
                      <td className="py-2.5 px-3 text-center">
                        {metric.isAlert ? (
                          <span className="bg-amber-950 text-amber-300 border border-amber-800 text-[10px] px-1.5 py-0.5 rounded font-bold">
                            High Variance
                          </span>
                        ) : (
                          <span className="text-slate-400 text-[10px]">Normal</span>
                        )}
                      </td>
                      <td className="py-2.5 px-3 text-slate-300 text-[11px] max-w-xs">
                        {metric.auditorAnalyticalNotes || '—'}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>

          {/* Section 2: Transaction Volume Trends */}
          <div className="bg-[#0f172a] rounded-lg border border-slate-800 p-4">
            <h3 className="text-sm font-bold text-white flex items-center gap-2 mb-1">
              <Layers className="w-4 h-4 text-sky-400" />
              <span>2. Voucher Volume & Audit Population Movements</span>
            </h3>
            <p className="text-xs text-slate-400 mb-4">
              Breakdown of voucher transaction volumes across accounting types in Tally.
            </p>

            <div className="overflow-x-auto">
              <table className="w-full text-xs text-left">
                <thead className="bg-[#0a0f1d] text-slate-400 uppercase text-[10px] tracking-wider border-b border-slate-800">
                  <tr>
                    <th className="py-2.5 px-3">Voucher Type</th>
                    <th className="py-2.5 px-3 text-right">Prior FY Count</th>
                    <th className="py-2.5 px-3 text-right">Current FY Count</th>
                    <th className="py-2.5 px-3 text-right">Count Variance</th>
                    <th className="py-2.5 px-3 text-right">% Change</th>
                    <th className="py-2.5 px-3 text-right">Current Monetary Total</th>
                    <th className="py-2.5 px-3">Audit Population Commentary</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-800/60 font-sans">
                  {yearDataStore.volumeMetrics.map((vol, idx) => (
                    <tr key={idx} className="hover:bg-slate-800/30">
                      <td className="py-2.5 px-3 font-semibold text-slate-200">
                        {vol.voucherType}
                      </td>
                      <td className="py-2.5 px-3 text-right font-mono text-slate-300">
                        {vol.priorYearCount.toLocaleString()}
                      </td>
                      <td className="py-2.5 px-3 text-right font-mono font-bold text-white">
                        {vol.currentYearCount.toLocaleString()}
                      </td>
                      <td className="py-2.5 px-3 text-right font-mono font-semibold text-teal-400">
                        +{vol.varianceCount.toLocaleString()}
                      </td>
                      <td className="py-2.5 px-3 text-right font-mono font-bold text-emerald-400">
                        +{vol.variancePercent}%
                      </td>
                      <td className="py-2.5 px-3 text-right font-mono font-semibold text-slate-200">
                        ₹{(vol.currentTotalAmount).toLocaleString('en-IN')}
                      </td>
                      <td className="py-2.5 px-3 text-slate-400 text-[11px]">
                        {vol.commentary}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      )}

      {/* TAB 3: RECURRING EXCEPTIONS ACROSS FINANCIAL YEARS */}
      {activeTab === 'recurring-issues' && (
        <div className="space-y-4">
          <div className="bg-amber-950/30 border border-amber-800/60 rounded-lg p-4 text-xs text-amber-200 flex items-start gap-3">
            <AlertTriangle className="w-5 h-5 text-amber-400 shrink-0 mt-0.5" />
            <div>
              <h4 className="font-bold text-amber-100 text-sm">Recurring Multi-Year Audit Findings Detected</h4>
              <p className="mt-1 text-slate-300">
                The audit engine has identified <strong className="text-amber-300">{yearDataStore.recurringFindings.length} statutory rule violations</strong> that persisted across both <strong className="text-white">{priorFyObj?.label}</strong> and <strong className="text-white">{activeFyObj?.label}</strong>. These indicate systemic internal control weaknesses requiring formal communication in the Auditor's Management Letter per SA 265.
              </p>
            </div>
          </div>

          <div className="grid grid-cols-1 gap-4">
            {yearDataStore.recurringFindings.map((rec) => (
              <div key={rec.id} className="bg-[#0f172a] rounded-lg border border-slate-800 p-4 space-y-3">
                <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2">
                  <div className="flex items-center gap-2">
                    <span className="font-mono text-xs font-bold text-amber-400">{rec.ruleId}</span>
                    <h4 className="text-sm font-bold text-white">{rec.ruleName}</h4>
                    <span className="bg-rose-950 text-rose-300 border border-rose-800 text-[10px] px-2 py-0.5 rounded font-bold uppercase">
                      {rec.severity}
                    </span>
                    <span className="bg-amber-950 text-amber-300 border border-amber-800 text-[10px] px-2 py-0.5 rounded font-semibold">
                      {rec.occurrenceCount} Consecutive Years
                    </span>
                  </div>
                  <span className="text-xs text-slate-400">
                    Party / Ledger: <strong className="text-slate-200">{rec.partyOrLedger}</strong>
                  </span>
                </div>

                <p className="text-xs text-slate-300 bg-[#070b14] p-3 rounded border border-slate-800">
                  {rec.description}
                </p>

                <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 text-xs">
                  <div className="bg-[#0b101e] p-2.5 rounded border border-slate-800">
                    <span className="text-[11px] text-slate-400 block font-semibold mb-1">
                      Prior Year Status ({priorFyObj?.label}):
                    </span>
                    <span className="text-slate-300">{rec.priorYearStatus}</span>
                  </div>

                  <div className="bg-[#0b101e] p-2.5 rounded border border-slate-800">
                    <span className="text-[11px] text-slate-400 block font-semibold mb-1">
                      Current Year Re-occurrence ({activeFyObj?.label}):
                    </span>
                    <span className="text-amber-300">{rec.currentYearStatus}</span>
                  </div>
                </div>

                <div className="bg-teal-950/30 p-2.5 rounded border border-teal-800/60 text-xs text-teal-200 flex items-start gap-2">
                  <CheckCircle className="w-4 h-4 text-teal-400 shrink-0 mt-0.5" />
                  <div>
                    <strong className="text-teal-300">Auditor Action Required:</strong> {rec.auditorActionRequired}
                  </div>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* TAB 4: SA 520 ANALYTICAL REVIEW MEMO */}
      {activeTab === 'analytical-memo' && (
        <div className="bg-[#0f172a] rounded-lg border border-slate-800 p-6 space-y-5">
          <div className="border-b border-slate-800 pb-4">
            <span className="text-xs font-mono font-bold text-teal-400 uppercase tracking-wider">
              Statutory Working Paper Ref: WP-ANALYTICAL-520
            </span>
            <h3 className="text-lg font-bold text-white mt-1">
              Substantive Analytical Review Memorandum (Standard on Auditing SA 520)
            </h3>
            <p className="text-xs text-slate-400 mt-1">
              Engagement: {activeCompany.name} | Financial Period: {activeFyObj?.label} vs {priorFyObj?.label} | Lead Partner: {activeCompany.auditPartner}
            </p>
          </div>

          <div className="space-y-4 text-xs text-slate-300 leading-relaxed font-sans">
            <div>
              <h4 className="font-bold text-teal-300 text-sm mb-1">1. Objective & Scope of Analytical Procedures</h4>
              <p>
                In accordance with <em>SA 520 (Analytical Procedures)</em> and <em>SA 315 (Identifying and Assessing the Risks of Material Misstatement)</em>, analytical procedures were performed at the overall financial statement level and class-of-transactions level. The objective was to corroborate substantive audit testing, identify unusual or unexpected relationships, and evaluate whether financial statement trends are consistent with our understanding of the entity's operational expansion.
              </p>
            </div>

            <div>
              <h4 className="font-bold text-teal-300 text-sm mb-1">2. Key Financial Variances & Audit Explanations</h4>
              <ul className="list-disc pl-5 space-y-1.5 text-slate-300">
                <li>
                  <strong className="text-white">Revenue Growth (+22.02%):</strong> Total revenue increased from ₹15.10 Crores to ₹18.42 Crores. Corroborated with e-Way bill data and 18.34% growth in sales invoices. No premature cut-off recognition identified in substantive test sampling.
                </li>
                <li>
                  <strong className="text-white">Gross Margin (+157 bps):</strong> Expanded to 38.99% from 37.42% due to supplier volume rebate contracts executed with primary raw material steel mills.
                </li>
                <li>
                  <strong className="text-white">Power & Electricity Overheads (+31%):</strong> Investigated high variance in manufacturing utilities. Substantiated against MSEDCL monthly industrial high-tension utility invoices corresponding with second-shift production schedule.
                </li>
                <li>
                  <strong className="text-white">Trade Receivables DSO Expansion:</strong> Debtors increased 24.39% with DSO extending from 67 to 69 days. Provision for expected credit loss (ECL) reviewed for overdue debts exceeding 180 days.
                </li>
              </ul>
            </div>

            <div>
              <h4 className="font-bold text-teal-300 text-sm mb-1">3. Evaluation of Recurring Non-Compliance</h4>
              <p>
                The audit team flagged recurring findings relating to Interstate vs Intrastate GST classification (Vendor: Tata Steel Gujarat) and Section 194Q TDS omission. Management has been advised to implement automated Place-of-Supply validation rules within TallyPrime master settings.
              </p>
            </div>

            <div>
              <h4 className="font-bold text-teal-300 text-sm mb-1">4. Lead Auditor Conclusion</h4>
              <div className="bg-[#070b14] p-4 rounded border border-slate-800 text-slate-200">
                <p>
                  "Based on the substantive analytical procedures performed, the fluctuations and relationships identified between FY 2024-25 and FY 2025-26 are consistent with relevant operational data and management representations. The variances have been satisfactorily substantiated with third-party corroborative evidence."
                </p>
                <div className="mt-3 pt-3 border-t border-slate-800 flex items-center justify-between text-xs text-slate-400">
                  <span>Signed: <strong>{activeCompany.auditPartner}</strong></span>
                  <span>Date: <strong>26-Sep-2026</strong></span>
                  <span className="text-emerald-400 font-bold">Status: Analytical Review Complete</span>
                </div>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* MODAL 1: SAFE SWITCH COMPANY CONFIRMATION */}
      {isSwitchCompanyModalOpen && pendingCompanyToSwitch && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-[#0f172a] rounded-lg border border-teal-500/80 w-full max-w-md shadow-2xl p-5 space-y-4">
            <div className="flex items-center gap-3">
              <div className="w-10 h-10 rounded-full bg-teal-500/20 border border-teal-500/40 flex items-center justify-center text-teal-300 shrink-0">
                <Building2 className="w-5 h-5" />
              </div>
              <div>
                <h3 className="text-base font-bold text-white">Safe Company Workspace Switch</h3>
                <p className="text-xs text-slate-400">Confirm isolated tenant environment transition</p>
              </div>
            </div>

            <div className="bg-[#070b14] p-3.5 rounded border border-slate-800 text-xs space-y-2">
              <div className="flex justify-between">
                <span className="text-slate-400">Current Workspace:</span>
                <span className="font-semibold text-rose-300">{activeCompany.name}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-slate-400">Target Workspace:</span>
                <span className="font-semibold text-emerald-300">{pendingCompanyToSwitch.name}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-slate-400">Target Period:</span>
                <span className="font-mono text-slate-200">
                  {pendingCompanyToSwitch.financialYears[0]?.label}
                </span>
              </div>
            </div>

            <div className="bg-amber-950/40 border border-amber-800/80 p-3 rounded text-[11px] text-amber-200 space-y-1">
              <strong className="text-amber-100 block flex items-center gap-1.5">
                <Shield className="w-3.5 h-3.5 text-amber-400" /> Company Isolation Safety Protocols:
              </strong>
              <p>• Any active in-memory audit scanning or Tally synchronization will be halted.</p>
              <p>• All findings, working papers, and evidence from {activeCompany.name} will be completely unmounted.</p>
              <p>• Fresh isolated database partition for {pendingCompanyToSwitch.name} will be loaded cleanly.</p>
            </div>

            <div className="flex items-center justify-end gap-2 pt-2">
              <button
                type="button"
                onClick={() => {
                  setIsSwitchCompanyModalOpen(false);
                  setPendingCompanyToSwitch(null);
                }}
                className="px-3.5 py-1.5 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded text-xs font-semibold cursor-pointer"
              >
                Cancel
              </button>
              <button
                type="button"
                onClick={handleConfirmSwitch}
                className="px-4 py-1.5 bg-teal-600 hover:bg-teal-500 text-white rounded text-xs font-bold shadow cursor-pointer"
              >
                Confirm &amp; Load Workspace
              </button>
            </div>
          </div>
        </div>
      )}

      {/* MODAL 2: ADD / ONBOARD NEW COMPANY */}
      {isOnboardModalOpen && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-[#0f172a] rounded-lg border border-slate-700 w-full max-w-lg shadow-2xl p-5 space-y-4">
            <div className="flex items-center justify-between border-b border-slate-800 pb-3">
              <div className="flex items-center gap-2">
                <Building2 className="w-5 h-5 text-teal-400" />
                <h3 className="text-base font-bold text-white">Onboard New Audit Client / Company</h3>
              </div>
              <button onClick={() => setIsOnboardModalOpen(false)} className="text-slate-400 hover:text-white">
                ✕
              </button>
            </div>

            <form onSubmit={handleCreateCompanySubmit} className="space-y-3 text-xs">
              <div>
                <label className="block text-slate-300 font-medium mb-1">Company Display Name *</label>
                <input
                  type="text"
                  required
                  value={newCompanyName}
                  onChange={(e) => setNewCompanyName(e.target.value)}
                  placeholder="e.g. Acme Precision Components Pvt Ltd"
                  className="w-full bg-[#070b14] border border-slate-800 rounded px-3 py-1.5 text-white focus:outline-none focus:border-teal-500"
                />
              </div>

              <div>
                <label className="block text-slate-300 font-medium mb-1">Full Legal Entity Name</label>
                <input
                  type="text"
                  value={newLegalName}
                  onChange={(e) => setNewLegalName(e.target.value)}
                  placeholder="e.g. Acme Precision Components Private Limited"
                  className="w-full bg-[#070b14] border border-slate-800 rounded px-3 py-1.5 text-white focus:outline-none focus:border-teal-500"
                />
              </div>

              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block text-slate-300 font-medium mb-1">GSTIN</label>
                  <input
                    type="text"
                    value={newGstin}
                    onChange={(e) => setNewGstin(e.target.value.toUpperCase())}
                    placeholder="27AABCA1234F1Z5"
                    className="w-full bg-[#070b14] border border-slate-800 rounded px-3 py-1.5 text-white font-mono uppercase focus:outline-none focus:border-teal-500"
                  />
                </div>
                <div>
                  <label className="block text-slate-300 font-medium mb-1">PAN</label>
                  <input
                    type="text"
                    value={newPan}
                    onChange={(e) => setNewPan(e.target.value.toUpperCase())}
                    placeholder="AABCA1234F"
                    className="w-full bg-[#070b14] border border-slate-800 rounded px-3 py-1.5 text-white font-mono uppercase focus:outline-none focus:border-teal-500"
                  />
                </div>
              </div>

              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block text-slate-300 font-medium mb-1">Tally Company Number</label>
                  <input
                    type="text"
                    value={newTallyNumber}
                    onChange={(e) => setNewTallyNumber(e.target.value)}
                    placeholder="10005"
                    className="w-full bg-[#070b14] border border-slate-800 rounded px-3 py-1.5 text-white font-mono focus:outline-none focus:border-teal-500"
                  />
                </div>
                <div>
                  <label className="block text-slate-300 font-medium mb-1">State Jurisdiction</label>
                  <select
                    value={newState}
                    onChange={(e) => setNewState(e.target.value)}
                    className="w-full bg-[#070b14] border border-slate-800 rounded px-3 py-1.5 text-white focus:outline-none focus:border-teal-500"
                  >
                    <option value="Maharashtra">Maharashtra (27)</option>
                    <option value="Delhi">Delhi (07)</option>
                    <option value="Gujarat">Gujarat (24)</option>
                    <option value="Karnataka">Karnataka (29)</option>
                    <option value="Tamil Nadu">Tamil Nadu (33)</option>
                    <option value="Uttar Pradesh">Uttar Pradesh (09)</option>
                  </select>
                </div>
              </div>

              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block text-slate-300 font-medium mb-1">Industry / Sector</label>
                  <input
                    type="text"
                    value={newIndustry}
                    onChange={(e) => setNewIndustry(e.target.value)}
                    placeholder="e.g. Pharmaceuticals / Auto Components"
                    className="w-full bg-[#070b14] border border-slate-800 rounded px-3 py-1.5 text-white focus:outline-none focus:border-teal-500"
                  />
                </div>
                <div>
                  <label className="block text-slate-300 font-medium mb-1">Initial Audit Period</label>
                  <select
                    value={newStartFy}
                    onChange={(e) => setNewStartFy(e.target.value)}
                    className="w-full bg-[#070b14] border border-slate-800 rounded px-3 py-1.5 text-white focus:outline-none focus:border-teal-500"
                  >
                    <option value="FY 2025-26">FY 2025-26</option>
                    <option value="FY 2024-25">FY 2024-25</option>
                    <option value="FY 2026-27">FY 2026-27</option>
                  </select>
                </div>
              </div>

              <div>
                <label className="block text-slate-300 font-medium mb-1">Statutory Engagement Partner</label>
                <input
                  type="text"
                  value={newAuditPartner}
                  onChange={(e) => setNewAuditPartner(e.target.value)}
                  placeholder="CA. Sanjiv (Senior Partner)"
                  className="w-full bg-[#070b14] border border-slate-800 rounded px-3 py-1.5 text-white focus:outline-none focus:border-teal-500"
                />
              </div>

              <div className="flex items-center justify-end gap-2 pt-3 border-t border-slate-800">
                <button
                  type="button"
                  onClick={() => setIsOnboardModalOpen(false)}
                  className="px-3.5 py-1.5 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded font-semibold cursor-pointer"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="px-4 py-1.5 bg-teal-600 hover:bg-teal-500 text-white rounded font-bold shadow cursor-pointer"
                >
                  Onboard &amp; Initialize Partition
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* MODAL 3: ROLL-FORWARD TO NEW FINANCIAL YEAR */}
      {isRollForwardModalOpen && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-[#0f172a] rounded-lg border border-indigo-500/80 w-full max-w-md shadow-2xl p-5 space-y-4">
            <div className="flex items-center gap-3">
              <div className="w-10 h-10 rounded-full bg-indigo-500/20 border border-indigo-500/40 flex items-center justify-center text-indigo-300 shrink-0">
                <RotateCcw className="w-5 h-5" />
              </div>
              <div>
                <h3 className="text-base font-bold text-white">Roll-Forward Audit Engagement</h3>
                <p className="text-xs text-slate-400">Carry forward plan, materiality &amp; recurring risks to next FY</p>
              </div>
            </div>

            <form onSubmit={handleRollForwardSubmit} className="space-y-3 text-xs">
              <div className="bg-[#070b14] p-3 rounded border border-slate-800 space-y-1.5">
                <div className="flex justify-between">
                  <span className="text-slate-400">Entity:</span>
                  <span className="font-semibold text-white">{activeCompany.name}</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-slate-400">Source Period:</span>
                  <span className="font-mono text-slate-200">{activeFyObj?.label}</span>
                </div>
              </div>

              <div>
                <label className="block text-slate-300 font-medium mb-1">Target Financial Year to Open *</label>
                <select
                  value={rollForwardTargetFy}
                  onChange={(e) => setRollForwardTargetFy(e.target.value)}
                  className="w-full bg-[#070b14] border border-slate-800 rounded px-3 py-2 text-white font-bold focus:outline-none focus:border-indigo-500"
                >
                  <option value="FY 2026-27">FY 2026-27 (01-Apr-2026 to 31-Mar-2027)</option>
                  <option value="FY 2027-28">FY 2027-28 (01-Apr-2027 to 31-Mar-2028)</option>
                </select>
              </div>

              <div className="bg-indigo-950/40 border border-indigo-800/80 p-3 rounded text-[11px] text-indigo-200 space-y-1">
                <strong className="text-indigo-100 block">Automated Roll-Forward Assets:</strong>
                <p>✓ Carries forward materiality policy &amp; threshold formulas</p>
                <p>✓ Transfers open audit risks &amp; standard testing procedures</p>
                <p>✓ Flags {yearDataStore.recurringFindings.length} recurring findings as priority focus areas</p>
                <p>✓ Preserves historical {activeFyObj?.label} file in archived read-only state</p>
              </div>

              <div className="flex items-center justify-end gap-2 pt-2">
                <button
                  type="button"
                  onClick={() => setIsRollForwardModalOpen(false)}
                  className="px-3.5 py-1.5 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded font-semibold cursor-pointer"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="px-4 py-1.5 bg-indigo-600 hover:bg-indigo-500 text-white rounded font-bold shadow cursor-pointer"
                >
                  Execute Roll-Forward
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
