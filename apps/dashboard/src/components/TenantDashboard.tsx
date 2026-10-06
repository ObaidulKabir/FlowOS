import React, { useState, useEffect, useMemo } from 'react';
import {
  AgentEvaluationMetrics,
  AuthSession,
  WorkflowClass,
  WorkflowClassStatus,
  WorkflowInstance,
  ValidationResult,
  CreateDraftRequest,
  TenantDto
} from '../types';
import { api, setActiveTenantId } from '../api/client';
import { resolveWorkflowInstanceId } from '../audit/resolveWorkflowInstance';
import { WorkflowInstanceTable } from './WorkflowInstanceTable';
import { EventAuditViewer } from './EventAuditViewer';
import { TenantApiKeyManager } from './TenantApiKeyManager';
import { TenantRuntimeRolesPanel } from './TenantRuntimeRolesPanel';
import { DetailView } from './DetailView';
import { EditorView } from './EditorView';
import { CapabilitiesShowcase } from './CapabilitiesShowcase';
import { CompetitiveComparison } from './CompetitiveComparison';
import { ApplicationWorkspace } from './ApplicationWorkspace';
import { DemoVisualSimulator } from './DemoVisualSimulator';
import { TenantMcpConfiguration } from './TenantMcpConfiguration';
import { TenantBackupDialog } from './TenantBackupDialog';
import { HumanInTheLoopInbox } from './HumanInTheLoopInbox';
import { DeadLetterQueueViewer } from './DeadLetterQueueViewer';
import { AiContextView } from './AiContextView';
import { legalEntity, sellerIdentity } from '../legalEntity';
import { LegalFooterLinks } from './LegalFooterLinks';
import { DashboardChrome, sidebarItemClass } from './DashboardChrome';
import { McpAgentGuideline } from './McpAgentGuideline';
import { 
  Building2, Plus, RefreshCw, Key, Activity, 
  Copy, Check, Filter, Sparkles, Scale, Layers, FlaskConical,
  Bot, AlertTriangle, ShieldAlert,
  UserCheck, Clock, Shield
} from 'lucide-react';

interface Props {
  session: AuthSession;
  onSwitchWorkspace: () => void;
  onTenantChange?: (newTenantId: string, newTenantName: string) => void;
  onGoHome: () => void;
  onSwitchRole: () => void;
  onRegister: () => void;
  onSignOut: () => void;
  mcpUrl: string;
}

type HubId = 'design' | 'agents' | 'operations' | 'governance';
type DesignTab = 'workflows' | 'simulator';
type OpsTab = 'instances' | 'inbox' | 'events' | 'dlq';
type GovTab = 'keys' | 'mcp' | 'capabilities' | 'comparison';

const countFormatter = new Intl.NumberFormat();

const formatCount = (value?: number) =>
  value === undefined ? '—' : countFormatter.format(value);

const blueprintStatusName = (status: WorkflowClass['status']) =>
  (typeof status === 'number' ? WorkflowClassStatus[status] : String(status)).toLowerCase();

const isLaunchableBlueprint = (status: WorkflowClass['status']) =>
  ['published', 'shared', 'public'].includes(blueprintStatusName(status));


const summarizeInstances = (instances: WorkflowInstance[]) => {
  const counts = { running: 0, waiting: 0, completed: 0, failed: 0 };
  instances.forEach(instance => {
    const status = typeof instance.status === 'number'
      ? ['running', 'waiting', 'completed', 'failed'][instance.status]
      : String(instance.status ?? '').toLowerCase();

    if (status in counts) {
      counts[status as keyof typeof counts] += 1;
    }
  });
  return { ...counts, active: counts.running + counts.waiting };
};

export const TenantDashboard: React.FC<Props> = ({
  session,
  onSwitchWorkspace,
  onTenantChange,
  onGoHome,
  onSwitchRole,
  onRegister,
  onSignOut,
  mcpUrl
}) => {
  // 4 Primary Functional Hubs
  const [activeHub, setActiveHub] = useState<HubId>('design');
  const [designTab, setDesignTab] = useState<DesignTab>('workflows');
  const [opsTab, setOpsTab] = useState<OpsTab>('instances');
  const [govTab, setGovTab] = useState<GovTab>('keys');

  const [blueprints, setBlueprints] = useState<WorkflowClass[]>([]);
  const [instances, setInstances] = useState<WorkflowInstance[]>([]);
  const [agentMetrics, setAgentMetrics] = useState<AgentEvaluationMetrics | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [copiedTenantId, setCopiedTenantId] = useState(false);
  const [auditFocus, setAuditFocus] = useState<{ instanceId: string; requestId: number } | null>(null);

  // Tenant Filter for development / testing
  const [availableTenants, setAvailableTenants] = useState<TenantDto[]>([]);

  useEffect(() => {
    const fetchTenants = async () => {
      try {
        const list = await api.listTenants();
        setAvailableTenants(list);
      } catch (err) {
        console.warn('Could not fetch tenants list in tenant view', err);
      }
    };
    fetchTenants();
  }, []);

  const handleSelectTenant = (newTenantId: string) => {
    const matched = availableTenants.find(t => t.tenantId === newTenantId);
    const newTenantName = matched ? matched.name : 'Tenant ' + newTenantId.substring(0, 8);
    setActiveTenantId(newTenantId, newTenantName);
    if (onTenantChange) {
      onTenantChange(newTenantId, newTenantName);
    }
  };

  // Modals
  const [selectedBlueprint, setSelectedBlueprint] = useState<WorkflowClass | null>(null);
  const [editorBlueprint, setEditorBlueprint] = useState<WorkflowClass | null>(null);
  const [isCreatingBlueprint, setIsCreatingBlueprint] = useState(false);
  const [validationResult, setValidationResult] = useState<ValidationResult | null>(null);

  // Start Instance Modal
  const [showStartModal, setShowStartModal] = useState(false);
  const [showBackupDialog, setShowBackupDialog] = useState(false);
  const [startWorkflowName, setStartWorkflowName] = useState('ExpenseApprovalV2');
  const [startingInstance, setStartingInstance] = useState(false);

  // Pre-fill the selected blueprint name when launching from the workspace
  useEffect(() => {
    if (showStartModal && selectedBlueprint && !isCreatingBlueprint) {
      setStartWorkflowName(selectedBlueprint.name);
    }
  }, [showStartModal, selectedBlueprint, isCreatingBlueprint]);

  const [simulationTarget, setSimulationTarget] = useState<{
    bindingId?: string;
    revision?: 'draft' | 'active';
  }>({});

  const loadData = async () => {
    setLoading(true);
    setError(null);
    setAgentMetrics(null);
    try {
      const toUtc = new Date();
      const fromUtc = new Date(toUtc.getTime() - 30 * 24 * 60 * 60 * 1000);
      const [instResult, bpResult, metricsResult] = await Promise.allSettled([
        api.listInstances('Tenant'),
        api.list(undefined, undefined, 'Tenant'),
        api.getAgentEvaluationMetrics(fromUtc.toISOString(), toUtc.toISOString(), 'Tenant')
      ]);

      if (instResult.status === 'fulfilled') setInstances(instResult.value);
      if (bpResult.status === 'fulfilled') setBlueprints(bpResult.value);
      if (metricsResult.status === 'fulfilled') setAgentMetrics(metricsResult.value);

      const results = [instResult, bpResult, metricsResult];
      const failed = results.find(result => result.status === 'rejected') as PromiseRejectedResult | undefined;
      if (failed && results.every(result => result.status === 'rejected')) {
        throw failed.reason;
      }
      if (failed) setError(failed.reason?.message || 'Some workspace counts could not be loaded');
    } catch (err: any) {
      setError(err.message || 'Failed to load workspace data');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadData();
  }, [activeHub, session.tenantId]);

  const handleCopyTenantId = () => {
    navigator.clipboard.writeText(session.tenantId);
    setCopiedTenantId(true);
    setTimeout(() => setCopiedTenantId(false), 2000);
  };

  const handleStartInstance = async () => {
    setStartingInstance(true);
    try {
      const res = await api.startInstance(startWorkflowName, undefined, undefined, session.role === 'Admin' ? 'Admin' : 'Tenant');
      const instanceId = res.WorkflowInstanceId || res.workflowInstanceId || res.Id || res.id;
      alert(`Workflow Instance Started Successfully!\nInstance ID: ${instanceId}`);
      setShowStartModal(false);
      setActiveHub('operations');
      setOpsTab('instances');
      await loadData();
    } catch (err: any) {
      alert(`Failed to start instance: ${err.message}`);
    } finally {
      setStartingInstance(false);
    }
  };

  // Blueprint Handlers
  const handleViewBlueprint = async (id: string) => {
    try {
      const item = await api.get(id, 'Tenant');
      setSelectedBlueprint(item);
      setValidationResult(null);
    } catch (err: any) {
      setError(err.message);
    }
  };

  const handleEditBlueprint = async (id: string) => {
    try {
      const item = await api.get(id, 'Tenant');
      setEditorBlueprint(item);
    } catch (err: any) {
      setError(err.message);
    }
  };

  const handleSaveDraft = async (req: CreateDraftRequest) => {
    try {
      let savedItem: WorkflowClass;
      if (editorBlueprint) {
        savedItem = await api.updateDraft(editorBlueprint.id, req, 'Tenant');
      } else {
        savedItem = await api.createDraft(req, 'Tenant');
      }
      const result = await api.validate(savedItem.id, 'Tenant');
      setValidationResult(result);
      setEditorBlueprint(null);
      setIsCreatingBlueprint(false);
      await loadData();
    } catch (err: any) {
      alert(`Failed to save draft: ${err.message}`);
    }
  };

  const instanceCounts = summarizeInstances(instances);
  const waitingInstances = useMemo(() => {
    return instances.filter(i => {
      if (i.status === 1 || i.status === 'Waiting') return true;
      if (typeof i.status === 'string' && i.status.toLowerCase().includes('wait')) return true;
      return false;
    });
  }, [instances]);

  const publishedBlueprints = blueprints.filter(item => isLaunchableBlueprint(item.status)).length;
  const draftBlueprints = blueprints.filter(item =>
    blueprintStatusName(item.status) === 'draft'
  ).length;
  const totalAgentTokens = agentMetrics
    ? agentMetrics.tokens.inputTokens + agentMetrics.tokens.outputTokens
    : undefined;

  // Sidebar organized by 4 Functional Hubs & Quick Actions
  const sidebarNav = (
    <div className="space-y-4">
      {/* Quick Actions */}
      <div className="space-y-1">
        <p className="px-2 text-[10px] font-semibold uppercase tracking-wider text-slate-500">Actions</p>
        <button
          type="button"
          onClick={() => setShowStartModal(true)}
          className={sidebarItemClass(false)}
        >
          <Sparkles size={14} />
          <span className="truncate">Launch Instance</span>
        </button>
        <button
          type="button"
          onClick={() => setIsCreatingBlueprint(true)}
          className={sidebarItemClass(false)}
        >
          <Plus size={14} />
          <span className="truncate">New Blueprint</span>
        </button>
        <button
          type="button"
          onClick={() => setShowBackupDialog(true)}
          className={sidebarItemClass(false)}
        >
          <ShieldAlert size={14} />
          <span className="truncate">Site Backup</span>
        </button>
        <button
          type="button"
          onClick={loadData}
          className={sidebarItemClass(false)}
        >
          <RefreshCw size={14} className={loading ? 'animate-spin text-blue-400' : ''} />
          <span className="truncate">Refresh</span>
        </button>
      </div>

      {/* Hub 1: Design Studio */}
      <div className="space-y-1">
        <p className="px-2 text-[10px] font-semibold uppercase tracking-wider text-slate-500 flex items-center justify-between">
          <span>Design Studio</span>
          <span className="text-[9px] text-slate-400 font-mono">{blueprints.length}</span>
        </p>
        <button
          type="button"
          onClick={() => {
            setActiveHub('design');
            setDesignTab('workflows');
          }}
          className={sidebarItemClass(activeHub === 'design' && designTab === 'workflows')}
        >
          <Layers size={14} />
          <span className="truncate">Workflows & DAG</span>
        </button>
        <button
          type="button"
          onClick={() => {
            setSimulationTarget({});
            setActiveHub('design');
            setDesignTab('simulator');
          }}
          className={sidebarItemClass(activeHub === 'design' && designTab === 'simulator', 'bg-emerald-600 text-white')}
        >
          <FlaskConical size={14} />
          <span className="truncate">Visual Simulator</span>
        </button>
      </div>

      {/* Hub 2: AI & Agent Studio */}
      <div className="space-y-1">
        <p className="px-2 text-[10px] font-semibold uppercase tracking-wider text-slate-500 flex items-center justify-between">
          <span>AI & Agents</span>
          <span className="text-[9px] text-slate-400 font-mono">{agentMetrics?.runs ?? 0}</span>
        </p>
        <button
          type="button"
          onClick={() => setActiveHub('agents')}
          className={sidebarItemClass(activeHub === 'agents', 'bg-cyan-600 text-white')}
        >
          <Bot size={14} />
          <span className="truncate">Agent Studio</span>
        </button>
      </div>

      {/* Hub 3: Operations & Runtime */}
      <div className="space-y-1">
        <p className="px-2 text-[10px] font-semibold uppercase tracking-wider text-slate-500 flex items-center justify-between">
          <span>Operations</span>
          <span className="text-[9px] text-slate-400 font-mono">{instances.length}</span>
        </p>
        <button
          type="button"
          onClick={() => {
            setActiveHub('operations');
            setOpsTab('instances');
          }}
          className={sidebarItemClass(activeHub === 'operations' && opsTab === 'instances')}
        >
          <Activity size={14} />
          <span className="truncate">Live Instances</span>
        </button>
        <button
          type="button"
          onClick={() => {
            setActiveHub('operations');
            setOpsTab('inbox');
          }}
          className={sidebarItemClass(activeHub === 'operations' && opsTab === 'inbox', 'bg-amber-600 text-white')}
        >
          <UserCheck size={14} />
          <span className="truncate">Human Review</span>
          {waitingInstances.length > 0 && (
            <span className="ml-auto text-[10px] font-bold px-1.5 py-0.2 rounded-full bg-amber-500 text-slate-950 font-mono animate-pulse">
              {waitingInstances.length}
            </span>
          )}
        </button>
        <button
          type="button"
          onClick={() => {
            setActiveHub('operations');
            setOpsTab('events');
          }}
          className={sidebarItemClass(activeHub === 'operations' && opsTab === 'events')}
        >
          <Clock size={14} />
          <span className="truncate">Event Stream</span>
        </button>
        <button
          type="button"
          onClick={() => {
            setActiveHub('operations');
            setOpsTab('dlq');
          }}
          className={sidebarItemClass(activeHub === 'operations' && opsTab === 'dlq')}
        >
          <AlertTriangle size={14} />
          <span className="truncate">Dead Letter Queue</span>
        </button>
      </div>

      {/* Hub 4: Platform Governance */}
      <div className="space-y-1">
        <p className="px-2 text-[10px] font-semibold uppercase tracking-wider text-slate-500">Governance</p>
        <button
          type="button"
          onClick={() => {
            setActiveHub('governance');
            setGovTab('keys');
          }}
          className={sidebarItemClass(activeHub === 'governance' && govTab === 'keys')}
        >
          <Key size={14} />
          <span className="truncate">API Keys & RBAC</span>
        </button>
        <button
          type="button"
          onClick={() => {
            setActiveHub('governance');
            setGovTab('mcp');
          }}
          className={sidebarItemClass(activeHub === 'governance' && govTab === 'mcp', 'bg-cyan-600 text-white')}
        >
          <Bot size={14} />
          <span className="truncate">MCP Server</span>
        </button>
        <button
          type="button"
          onClick={() => {
            setActiveHub('governance');
            setGovTab('capabilities');
          }}
          className={sidebarItemClass(activeHub === 'governance' && govTab === 'capabilities', 'bg-indigo-600 text-white')}
        >
          <Sparkles size={14} />
          <span className="truncate">Capabilities</span>
        </button>
        <button
          type="button"
          onClick={() => {
            setActiveHub('governance');
            setGovTab('comparison');
          }}
          className={sidebarItemClass(activeHub === 'governance' && govTab === 'comparison', 'bg-indigo-600 text-white')}
        >
          <Scale size={14} />
          <span className="truncate">vs Competitors</span>
        </button>
      </div>
    </div>
  );

  return (
    <>
      <DashboardChrome
        session={session}
        onGoHome={onGoHome}
        onSwitchRole={onSwitchRole}
        onRegister={onRegister}
        onSignOut={onSignOut}
        mcpUrl={mcpUrl}
        nav={sidebarNav}
      />
      <div className="flowos-rail-content h-full overflow-y-auto flex flex-col" style={{ marginLeft: '15rem' }}>
        {session.isSandbox && (
          <div className="bg-gradient-to-r from-emerald-900/90 via-slate-900 to-blue-900/90 border-b border-emerald-500/40 px-6 py-2.5 text-xs text-emerald-200 flex flex-wrap items-center gap-3 shadow-md">
            <span className="flex h-2 w-2 rounded-full bg-emerald-400 animate-pulse" />
            <span className="font-bold">Interactive Sandbox Playground:</span>
            <span className="text-slate-300 hidden sm:inline">
              You are exploring FlowOS as an unregistered guest user. State transitions, workflows, and simulations are active.
            </span>
          </div>
        )}
        {!session.isSandbox && (session.plan === 'Trial' || session.billingStatus === 'Unpaid' || session.canRunRuntime === false) && (
          <div className="bg-gradient-to-r from-amber-900/90 via-slate-900 to-orange-900/90 border-b border-amber-500/40 px-6 py-2.5 text-xs text-amber-100 flex flex-wrap items-center justify-between gap-3 shadow-md">
            <div className="flex items-center gap-2">
              <span className="flex h-2 w-2 rounded-full bg-amber-400 animate-pulse" />
              <span className="font-bold">Free / unpaid tenant:</span>
              <span className="text-slate-200 hidden sm:inline">
                Runtime execution is blocked (start, publish, complete). Design-time simulate, lint, and drafts still work. Ask admin@flowosbd.com to activate Starter through Scale, or Enterprise.
              </span>
            </div>
            <a
              href="mailto:admin@flowosbd.com?subject=FlowOS%20paid%20plan%20activation"
              className="px-3 py-1 bg-amber-500 hover:bg-amber-400 text-slate-950 font-bold rounded-lg transition-all shadow"
            >
              Contact admin@flowosbd.com
            </a>
          </div>
        )}
        <main className="max-w-7xl mx-auto px-6 py-8 flex-1 w-full space-y-6">
      
      {/* Workspace Banner */}
      <div className="bg-gradient-to-r from-blue-900/40 via-slate-900 to-slate-900 border border-blue-500/30 p-6 rounded-3xl shadow-xl relative overflow-hidden">
        <div className="flex flex-col md:flex-row md:items-center justify-between gap-6 relative z-10">
          <div>
            <div className="flex items-center gap-2 mb-2">
              <span className="px-2.5 py-1 rounded-full text-xs font-bold bg-blue-500/20 text-blue-300 border border-blue-500/30 flex items-center gap-1.5">
                <Building2 size={13} /> Tenant Isolated Workspace
              </span>
              <span className="text-xs text-slate-500">•</span>
              <span className="text-xs text-slate-400 font-mono flex items-center gap-1">
                UUID: <strong>{session.tenantId.substring(0, 13)}...</strong>
                <button 
                  onClick={handleCopyTenantId}
                  className="hover:text-white text-slate-500"
                  title="Copy Tenant UUID"
                >
                  {copiedTenantId ? <Check size={12} className="text-emerald-400" /> : <Copy size={12} />}
                </button>
                <button
                  onClick={onSwitchWorkspace}
                  className="ml-2 text-[10px] text-blue-400 hover:underline"
                >
                  Switch Account
                </button>
              </span>
            </div>

            <div className="flex flex-wrap items-center gap-3">
              <h1 className="text-2xl md:text-3xl font-extrabold text-white">
                {session.tenantName}
              </h1>

              {/* Interactive Tenant Filter Dropdown */}
              <div className="flex items-center gap-1.5 bg-slate-950/90 border border-blue-500/40 rounded-xl px-3 py-1.5 shadow-inner">
                <Filter size={13} className="text-blue-400 shrink-0" />
                <span className="text-[11px] text-slate-400">Filter Tenant:</span>
                <select
                  value={session.tenantId}
                  onChange={(e) => handleSelectTenant(e.target.value)}
                  className="bg-transparent text-white font-bold text-xs focus:outline-none cursor-pointer"
                  title="Filter and switch active tenant workspace"
                >
                  {availableTenants.map(t => (
                    <option key={t.tenantId} value={t.tenantId} className="bg-slate-900 text-white">
                      {t.name} ({t.tenantId.substring(0, 8)}...)
                    </option>
                  ))}
                  {availableTenants.length === 0 && (
                    <option value={session.tenantId} className="bg-slate-900 text-white">
                      {session.tenantName} ({session.tenantId.substring(0, 8)}...)
                    </option>
                  )}
                </select>
              </div>
            </div>

            <p className="text-xs text-slate-400 mt-1 max-w-2xl">
              Operating environment with strict zero-trust boundary. Currently filtered to <strong>{session.tenantName}</strong>.
            </p>
          </div>

          {/* Quick Hub Navigation Shortcuts */}
          <div className="flex items-center gap-2">
            {waitingInstances.length > 0 && (
              <button
                onClick={() => {
                  setActiveHub('operations');
                  setOpsTab('inbox');
                }}
                className="px-3 py-2 rounded-xl bg-amber-500/20 border border-amber-500/50 hover:bg-amber-500/30 text-amber-200 text-xs font-bold flex items-center gap-2 transition-all animate-pulse"
              >
                <UserCheck size={14} className="text-amber-400" />
                <span>{waitingInstances.length} Waiting Review</span>
              </button>
            )}
            <button
              onClick={() => setShowStartModal(true)}
              className="px-3.5 py-2 rounded-xl bg-emerald-600 hover:bg-emerald-500 text-white text-xs font-bold flex items-center gap-1.5 shadow-lg shadow-emerald-900/30 transition-all"
            >
              <Sparkles size={14} /> Launch Instance
            </button>
          </div>
        </div>

        {/* Cardinality-Driven Operational Counts Ribbon */}
        <div className="mt-6 pt-5 border-t border-slate-800/80">
          <div className="flex flex-wrap items-center justify-between gap-2 mb-3">
            <span className="text-[11px] font-semibold uppercase tracking-wider text-slate-500">
              Workspace Cardinality & Telemetry (Rolling 30-Day Window)
            </span>
            <span className="text-[10px] text-cyan-300 flex items-center gap-1.5">
              <Bot size={12} />
              Zero-trust tenant isolation active
            </span>
          </div>

          <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
            {/* Workflows Blueprint Cardinality */}
            <div 
              onClick={() => { setActiveHub('design'); setDesignTab('workflows'); }}
              className="bg-slate-900/60 hover:bg-slate-900/90 p-3 rounded-xl border border-slate-800 hover:border-blue-500/50 cursor-pointer transition-all"
            >
              <div className="text-[11px] text-slate-400 flex items-center justify-between">
                <span className="flex items-center gap-1.5">
                  <Layers size={12} className="text-blue-400" />
                  Workflows
                </span>
                <span className="text-[10px] text-blue-400">Design Studio →</span>
              </div>
              <div className="text-xl font-bold text-blue-300 mt-1">{formatCount(blueprints.length)}</div>
              <div className="text-[10px] text-slate-500 mt-1">
                {formatCount(publishedBlueprints)} published · {formatCount(draftBlueprints)} drafts
              </div>
            </div>

            {/* Workflow Instances Cardinality */}
            <div 
              onClick={() => { setActiveHub('operations'); setOpsTab('instances'); }}
              className="bg-slate-900/60 hover:bg-slate-900/90 p-3 rounded-xl border border-slate-800 hover:border-purple-500/50 cursor-pointer transition-all"
            >
              <div className="text-[11px] text-slate-400 flex items-center justify-between">
                <span className="flex items-center gap-1.5">
                  <Activity size={12} className="text-purple-400" />
                  Live Instances
                </span>
                <span className="text-[10px] text-purple-400">Runtime →</span>
              </div>
              <div className="text-xl font-bold text-white mt-1">{formatCount(instances.length)}</div>
              <div className="text-[10px] text-slate-500 mt-1">
                {formatCount(instanceCounts.active)} active · {formatCount(instanceCounts.completed)} completed
              </div>
            </div>

            {/* Human in the Loop Decisions */}
            <div 
              onClick={() => { setActiveHub('operations'); setOpsTab('inbox'); }}
              className={`p-3 rounded-xl border cursor-pointer transition-all ${
                waitingInstances.length > 0
                  ? 'bg-amber-950/30 border-amber-500/50 hover:bg-amber-950/50 shadow-sm'
                  : 'bg-slate-900/60 hover:bg-slate-900/90 border-slate-800'
              }`}
            >
              <div className="text-[11px] text-slate-400 flex items-center justify-between">
                <span className="flex items-center gap-1.5">
                  <UserCheck size={12} className={waitingInstances.length > 0 ? 'text-amber-400' : 'text-slate-400'} />
                  Human Review
                </span>
                <span className={`text-[10px] ${waitingInstances.length > 0 ? 'text-amber-400 font-bold' : 'text-slate-500'}`}>
                  Decision Center →
                </span>
              </div>
              <div className={`text-xl font-bold mt-1 ${waitingInstances.length > 0 ? 'text-amber-300' : 'text-slate-300'}`}>
                {formatCount(waitingInstances.length)}
              </div>
              <div className="text-[10px] text-slate-500 mt-1">
                {formatCount(instanceCounts.running)} executing · {formatCount(waitingInstances.length)} parked
              </div>
            </div>

            {/* Agent Autonomy & Token Usage */}
            <div 
              onClick={() => setActiveHub('agents')}
              className="bg-cyan-950/20 hover:bg-cyan-950/40 p-3 rounded-xl border border-cyan-900/50 cursor-pointer transition-all"
            >
              <div className="text-[11px] text-slate-400 flex items-center justify-between">
                <span className="flex items-center gap-1.5">
                  <Bot size={12} className="text-cyan-400" />
                  Agent Autonomy
                </span>
                <span className="text-[10px] text-cyan-400">Agent Studio →</span>
              </div>
              <div className="text-xl font-bold text-cyan-300 mt-1">
                {formatCount(agentMetrics?.commits)} commits
              </div>
              <div className="text-[10px] text-slate-500 mt-1">
                {formatCount(agentMetrics?.overrides)} overrides · {formatCount(totalAgentTokens)} tokens
              </div>
            </div>
          </div>
        </div>
      </div>

      {error && (
        <div className="p-4 bg-rose-900/30 border border-rose-700 rounded-xl text-xs text-rose-300 flex items-center gap-2">
          <AlertTriangle size={15} className="shrink-0 text-rose-400" />
          <span>{error}</span>
        </div>
      )}

      {/* 4 Functional Hub Navigation Switcher */}
      <div className="bg-slate-900/90 border border-slate-800 rounded-2xl p-1.5 flex flex-wrap gap-1 shadow-lg backdrop-blur">
        <button
          type="button"
          onClick={() => setActiveHub('design')}
          className={`flex-1 min-w-[140px] px-4 py-2.5 rounded-xl font-bold text-xs flex items-center justify-center gap-2 transition-all ${
            activeHub === 'design'
              ? 'bg-blue-600 text-white shadow-md shadow-blue-500/20'
              : 'text-slate-400 hover:text-white hover:bg-slate-800/60'
          }`}
        >
          <Layers size={15} className={activeHub === 'design' ? 'text-white' : 'text-blue-400'} />
          <span>Design Studio</span>
          <span className={`text-[10px] px-2 py-0.5 rounded-full font-mono ${
            activeHub === 'design' ? 'bg-blue-700 text-blue-100' : 'bg-slate-800 text-slate-400'
          }`}>
            {blueprints.length}
          </span>
        </button>

        <button
          type="button"
          onClick={() => setActiveHub('agents')}
          className={`flex-1 min-w-[140px] px-4 py-2.5 rounded-xl font-bold text-xs flex items-center justify-center gap-2 transition-all ${
            activeHub === 'agents'
              ? 'bg-cyan-600 text-white shadow-md shadow-cyan-500/20'
              : 'text-slate-400 hover:text-white hover:bg-slate-800/60'
          }`}
        >
          <Bot size={15} className={activeHub === 'agents' ? 'text-white' : 'text-cyan-400'} />
          <span>AI & Agent Studio</span>
          <span className={`text-[10px] px-2 py-0.5 rounded-full font-mono ${
            activeHub === 'agents' ? 'bg-cyan-700 text-cyan-100' : 'bg-slate-800 text-slate-400'
          }`}>
            {agentMetrics?.runs ?? 0} runs
          </span>
        </button>

        <button
          type="button"
          onClick={() => setActiveHub('operations')}
          className={`flex-1 min-w-[140px] px-4 py-2.5 rounded-xl font-bold text-xs flex items-center justify-center gap-2 transition-all relative ${
            activeHub === 'operations'
              ? 'bg-emerald-600 text-white shadow-md shadow-emerald-500/20'
              : 'text-slate-400 hover:text-white hover:bg-slate-800/60'
          }`}
        >
          <Activity size={15} className={activeHub === 'operations' ? 'text-white' : 'text-emerald-400'} />
          <span>Operations & Runtime</span>
          <span className={`text-[10px] px-2 py-0.5 rounded-full font-mono ${
            activeHub === 'operations' ? 'bg-emerald-700 text-emerald-100' : 'bg-slate-800 text-slate-400'
          }`}>
            {instances.length}
          </span>
          {waitingInstances.length > 0 && (
            <span className="flex h-2 w-2 relative">
              <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-amber-400 opacity-75"></span>
              <span className="relative inline-flex rounded-full h-2 w-2 bg-amber-500"></span>
            </span>
          )}
        </button>

        <button
          type="button"
          onClick={() => setActiveHub('governance')}
          className={`flex-1 min-w-[140px] px-4 py-2.5 rounded-xl font-bold text-xs flex items-center justify-center gap-2 transition-all ${
            activeHub === 'governance'
              ? 'bg-indigo-600 text-white shadow-md shadow-indigo-500/20'
              : 'text-slate-400 hover:text-white hover:bg-slate-800/60'
          }`}
        >
          <Shield size={15} className={activeHub === 'governance' ? 'text-white' : 'text-indigo-400'} />
          <span>Platform Governance</span>
        </button>
      </div>

      {/* Main Hub Body Area */}
      <div className="bg-slate-800/90 border border-slate-700/80 rounded-2xl overflow-hidden shadow-xl">
        <div className="p-6">

          {/* HUB 1: DESIGN STUDIO */}
          {activeHub === 'design' && (
            <div className="space-y-4">
              <div className="flex flex-wrap items-center justify-between border-b border-slate-700/80 pb-3 gap-2">
                <div className="flex items-center gap-2">
                  <button
                    onClick={() => setDesignTab('workflows')}
                    className={`px-3 py-1.5 rounded-lg text-xs font-semibold flex items-center gap-1.5 transition-all ${
                      designTab === 'workflows'
                        ? 'bg-blue-500/20 text-blue-300 border border-blue-500/40'
                        : 'text-slate-400 hover:text-white hover:bg-slate-800'
                    }`}
                  >
                    <Layers size={13} />
                    <span>Workflow Blueprints & DAG Studio</span>
                  </button>
                  <button
                    onClick={() => {
                      setSimulationTarget({});
                      setDesignTab('simulator');
                    }}
                    className={`px-3 py-1.5 rounded-lg text-xs font-semibold flex items-center gap-1.5 transition-all ${
                      designTab === 'simulator'
                        ? 'bg-emerald-500/20 text-emerald-300 border border-emerald-500/40'
                        : 'text-slate-400 hover:text-white hover:bg-slate-800'
                    }`}
                  >
                    <FlaskConical size={13} />
                    <span>Visual Demo Simulator</span>
                  </button>
                </div>
                <div className="flex items-center gap-2">
                  <button
                    onClick={() => setIsCreatingBlueprint(true)}
                    className="px-3 py-1.5 bg-blue-600 hover:bg-blue-500 text-white rounded-lg text-xs font-bold flex items-center gap-1.5 shadow transition-all"
                  >
                    <Plus size={13} /> New Blueprint
                  </button>
                </div>
              </div>

              {designTab === 'workflows' && (
                <ApplicationWorkspace
                  tenantName={session.tenantName}
                  blueprints={blueprints}
                  instances={instances}
                  onView={handleViewBlueprint}
                  onEdit={handleEditBlueprint}
                  onCreate={() => setIsCreatingBlueprint(true)}
                  onDelete={async (id) => {
                    if (confirm('Delete this draft?')) {
                      await api.delete(id, 'Tenant');
                      await loadData();
                    }
                  }}
                  onPublish={async (id) => {
                    await api.publish(id, 'Tenant');
                    await loadData();
                  }}
                  onSubmit={async (id) => {
                    await api.submit(id, 'Tenant');
                    await loadData();
                  }}
                  onWithdraw={async (id) => {
                    await api.withdraw(id, 'Tenant');
                    await loadData();
                  }}
                  onDeprecate={async (id) => {
                    await api.deprecate(id, 'Tenant');
                    await loadData();
                  }}
                  onAbandon={async (id) => {
                    await api.abandon(id, 'Tenant');
                    await loadData();
                  }}
                  onNewVersion={async (id) => {
                    const nv = await api.newVersion(id, 'Tenant');
                    setEditorBlueprint(nv);
                    await loadData();
                  }}
                  onSimulate={(bindingId, revision) => {
                    setSimulationTarget({ bindingId, revision });
                    setDesignTab('simulator');
                  }}
                  onLaunch={() => setShowStartModal(true)}
                  onOpenSimulator={() => {
                    setSimulationTarget({});
                    setDesignTab('simulator');
                  }}
                  onInspectInstance={(instanceId) => {
                    setAuditFocus({ instanceId, requestId: Date.now() });
                    setActiveHub('operations');
                    setOpsTab('instances');
                  }}
                />
              )}

              {designTab === 'simulator' && (
                <DemoVisualSimulator
                  blueprints={blueprints}
                  initialBindingId={simulationTarget.bindingId}
                  initialRevision={simulationTarget.revision}
                  preferContextMode={Boolean(simulationTarget.bindingId)}
                />
              )}
            </div>
          )}

          {/* HUB 2: AI & AGENT STUDIO */}
          {activeHub === 'agents' && (
            <div className="space-y-4">
              <div className="flex flex-wrap items-center justify-between border-b border-slate-700/80 pb-3 gap-2">
                <div>
                  <h2 className="text-base font-bold text-white flex items-center gap-2">
                    <Bot className="text-cyan-400" size={18} />
                    AI & Agent Studio
                  </h2>
                  <p className="text-xs text-slate-400 mt-0.5">
                    Governed Agent Personas, Curated Tool Registry, Versioned Prompt Catalog, and LLM Provider Bindings.
                  </p>
                </div>
                <div className="flex items-center gap-2">
                  <button
                    onClick={() => {
                      setActiveHub('governance');
                      setGovTab('mcp');
                    }}
                    className="px-3 py-1.5 bg-cyan-950/60 hover:bg-cyan-900/60 border border-cyan-700/40 text-cyan-300 rounded-lg text-xs font-semibold flex items-center gap-1.5 transition-all"
                  >
                    <Bot size={13} /> MCP Server Protocol
                  </button>
                  <button
                    onClick={() => {
                      setActiveHub('operations');
                      setOpsTab('inbox');
                    }}
                    className="px-3 py-1.5 bg-amber-950/60 hover:bg-amber-900/60 border border-amber-700/40 text-amber-300 rounded-lg text-xs font-semibold flex items-center gap-1.5 transition-all"
                  >
                    <UserCheck size={13} /> Human Decision Inbox ({waitingInstances.length})
                  </button>
                </div>
              </div>

              <AiContextView
                tenantName={session.tenantName}
                onOpenBusinessContext={() => {
                  setActiveHub('design');
                  setDesignTab('workflows');
                }}
              />
            </div>
          )}

          {/* HUB 3: OPERATIONS & RUNTIME */}
          {activeHub === 'operations' && (
            <div className="space-y-4">
              <div className="flex flex-wrap items-center justify-between border-b border-slate-700/80 pb-3 gap-2">
                <div className="flex flex-wrap items-center gap-2">
                  <button
                    onClick={() => setOpsTab('instances')}
                    className={`px-3 py-1.5 rounded-lg text-xs font-semibold flex items-center gap-1.5 transition-all ${
                      opsTab === 'instances'
                        ? 'bg-blue-500/20 text-blue-300 border border-blue-500/40'
                        : 'text-slate-400 hover:text-white hover:bg-slate-800'
                    }`}
                  >
                    <Activity size={13} />
                    <span>Live Instances</span>
                    <span className="text-[10px] px-1.5 py-0.2 bg-slate-800 text-slate-400 rounded-full font-mono">{instances.length}</span>
                  </button>

                  <button
                    onClick={() => setOpsTab('inbox')}
                    className={`px-3 py-1.5 rounded-lg text-xs font-semibold flex items-center gap-1.5 transition-all ${
                      opsTab === 'inbox'
                        ? 'bg-amber-500/20 text-amber-300 border border-amber-500/40'
                        : 'text-slate-400 hover:text-white hover:bg-slate-800'
                    }`}
                  >
                    <UserCheck size={13} />
                    <span>Human Decision Inbox</span>
                    {waitingInstances.length > 0 ? (
                      <span className="text-[10px] px-1.5 py-0.2 bg-amber-500 text-slate-950 rounded-full font-bold font-mono animate-pulse">
                        {waitingInstances.length} Waiting
                      </span>
                    ) : (
                      <span className="text-[10px] px-1.5 py-0.2 bg-slate-800 text-slate-400 rounded-full font-mono">0</span>
                    )}
                  </button>

                  <button
                    onClick={() => setOpsTab('events')}
                    className={`px-3 py-1.5 rounded-lg text-xs font-semibold flex items-center gap-1.5 transition-all ${
                      opsTab === 'events'
                        ? 'bg-purple-500/20 text-purple-300 border border-purple-500/40'
                        : 'text-slate-400 hover:text-white hover:bg-slate-800'
                    }`}
                  >
                    <Clock size={13} />
                    <span>Event Stream & Audit</span>
                  </button>

                  <button
                    onClick={() => setOpsTab('dlq')}
                    className={`px-3 py-1.5 rounded-lg text-xs font-semibold flex items-center gap-1.5 transition-all ${
                      opsTab === 'dlq'
                        ? 'bg-rose-500/20 text-rose-300 border border-rose-500/40'
                        : 'text-slate-400 hover:text-white hover:bg-slate-800'
                    }`}
                  >
                    <AlertTriangle size={13} />
                    <span>Dead Letter Queue (DLQ)</span>
                  </button>
                </div>

                <div className="flex items-center gap-2">
                  <button
                    onClick={() => setShowStartModal(true)}
                    className="px-3 py-1.5 bg-gradient-to-r from-emerald-600 to-teal-600 hover:from-emerald-500 hover:to-teal-500 text-white rounded-lg text-xs font-bold flex items-center gap-1.5 shadow transition-all"
                  >
                    <Sparkles size={13} /> Launch Workflow
                  </button>
                </div>
              </div>

              {opsTab === 'instances' && (
                <div className="space-y-4">
                  <div className="flex justify-between items-center text-xs text-slate-400">
                    <span>Showing workflow executions owned by <strong>{session.tenantName}</strong></span>
                    <button
                      onClick={() => setShowStartModal(true)}
                      className="px-3 py-1.5 bg-emerald-600 hover:bg-emerald-500 text-white rounded-lg text-xs font-bold flex items-center gap-1.5 transition-all"
                    >
                      <Sparkles size={13} /> Launch Demo Workflow
                    </button>
                  </div>
                  <WorkflowInstanceTable
                    key={auditFocus?.requestId ?? 'instances'}
                    items={instances}
                    blueprints={blueprints}
                    inspectInstanceId={auditFocus?.instanceId}
                    inspectRequestId={auditFocus?.requestId}
                    onInspectConsumed={() => setAuditFocus(null)}
                  />
                </div>
              )}

              {opsTab === 'inbox' && (
                <HumanInTheLoopInbox
                  instances={instances}
                  blueprints={blueprints}
                  onInspectInstance={(instanceId) => {
                    setAuditFocus({ instanceId, requestId: Date.now() });
                    setOpsTab('instances');
                  }}
                  onRefresh={loadData}
                  role="Tenant"
                />
              )}

              {opsTab === 'events' && (
                <EventAuditViewer
                  role="Tenant"
                  onInspectWorkflow={(instanceId) => {
                    const resolved = resolveWorkflowInstanceId(instances, instanceId);
                    if (!resolved) return;
                    setAuditFocus({ instanceId: resolved, requestId: Date.now() });
                    setOpsTab('instances');
                  }}
                />
              )}

              {opsTab === 'dlq' && (
                <DeadLetterQueueViewer
                  session={session}
                  tenantFilter={session.tenantId}
                />
              )}
            </div>
          )}

          {/* HUB 4: PLATFORM GOVERNANCE */}
          {activeHub === 'governance' && (
            <div className="space-y-4">
              <div className="flex flex-wrap items-center justify-between border-b border-slate-700/80 pb-3 gap-2">
                <div className="flex flex-wrap items-center gap-2">
                  <button
                    onClick={() => setGovTab('keys')}
                    className={`px-3 py-1.5 rounded-lg text-xs font-semibold flex items-center gap-1.5 transition-all ${
                      govTab === 'keys'
                        ? 'bg-blue-500/20 text-blue-300 border border-blue-500/40'
                        : 'text-slate-400 hover:text-white hover:bg-slate-800'
                    }`}
                  >
                    <Key size={13} />
                    <span>Scoped API Keys & Runtime RBAC</span>
                  </button>

                  <button
                    onClick={() => setGovTab('mcp')}
                    className={`px-3 py-1.5 rounded-lg text-xs font-semibold flex items-center gap-1.5 transition-all ${
                      govTab === 'mcp'
                        ? 'bg-cyan-500/20 text-cyan-300 border border-cyan-500/40'
                        : 'text-slate-400 hover:text-white hover:bg-slate-800'
                    }`}
                  >
                    <Bot size={13} />
                    <span>MCP Server Protocol</span>
                  </button>

                  <button
                    onClick={() => setGovTab('capabilities')}
                    className={`px-3 py-1.5 rounded-lg text-xs font-semibold flex items-center gap-1.5 transition-all ${
                      govTab === 'capabilities'
                        ? 'bg-indigo-500/20 text-indigo-300 border border-indigo-500/40'
                        : 'text-slate-400 hover:text-white hover:bg-slate-800'
                    }`}
                  >
                    <Sparkles size={13} />
                    <span>Platform Specifications</span>
                  </button>

                  <button
                    onClick={() => setGovTab('comparison')}
                    className={`px-3 py-1.5 rounded-lg text-xs font-semibold flex items-center gap-1.5 transition-all ${
                      govTab === 'comparison'
                        ? 'bg-indigo-500/20 text-indigo-300 border border-indigo-500/40'
                        : 'text-slate-400 hover:text-white hover:bg-slate-800'
                    }`}
                  >
                    <Scale size={13} />
                    <span>Competitive Comparison</span>
                  </button>
                </div>

                <div className="flex items-center gap-2">
                  <button
                    onClick={() => setShowBackupDialog(true)}
                    className="px-3 py-1.5 bg-slate-700 hover:bg-slate-600 text-white rounded-lg text-xs font-semibold flex items-center gap-1.5 transition-all"
                  >
                    <ShieldAlert size={13} /> Site Backup & Restore
                  </button>
                </div>
              </div>

              {govTab === 'keys' && (
                <div className="space-y-6">
                  <TenantApiKeyManager tenantId={session.tenantId} tenantName={session.tenantName} />
                  <TenantRuntimeRolesPanel />
                </div>
              )}

              {govTab === 'mcp' && (
                <TenantMcpConfiguration
                  tenantId={session.tenantId}
                  tenantName={session.tenantName}
                  sessionApiKey={session.apiKey}
                  onOpenApiKeys={() => setGovTab('keys')}
                />
              )}

              {govTab === 'capabilities' && (
                <CapabilitiesShowcase />
              )}

              {govTab === 'comparison' && (
                <CompetitiveComparison />
              )}
            </div>
          )}

        </div>
      </div>

      {showBackupDialog && (
        <TenantBackupDialog
          tenantId={session.tenantId}
          tenantName={session.tenantName}
          onClose={() => setShowBackupDialog(false)}
          onRestored={loadData}
        />
      )}

      {/* Start Instance Modal */}
      {showStartModal && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-slate-900 border border-slate-700 rounded-2xl max-w-lg w-full p-6 shadow-2xl space-y-4">
            <div className="flex items-center justify-between border-b border-slate-800 pb-3">
              <h3 className="text-base font-bold text-white flex items-center gap-2">
                <Sparkles className="text-emerald-400" size={18} />
                Launch Demo Workflow Instance
              </h3>
              <button onClick={() => setShowStartModal(false)} className="text-slate-400 hover:text-white text-lg">
                &times;
              </button>
            </div>

            <div className="space-y-4 text-xs">
              <div>
                <label className="text-slate-300 font-bold block mb-2 text-sm text-emerald-400 flex items-center gap-1.5">
                  <Sparkles size={14}/> Select Workflow Blueprint
                </label>
                <div className="grid grid-cols-1 gap-2 max-h-56 overflow-y-auto pr-1">
                  {blueprints
                    .filter(bp => isLaunchableBlueprint(bp.status))
                    .map(preset => (
                    <div
                      key={preset.id}
                      onClick={() => setStartWorkflowName(preset.name)}
                      className={`p-2.5 rounded-xl border cursor-pointer transition-all ${
                        startWorkflowName === preset.name
                          ? 'bg-blue-900/30 border-blue-500 shadow-sm'
                          : 'bg-slate-950 border-slate-800 hover:border-slate-700'
                      }`}
                    >
                      <div className="flex items-center justify-between mb-1">
                        <span className="font-bold text-white text-xs">{preset.name}</span>
                        <span className="text-[10px] px-2 py-0.5 rounded-full border font-semibold bg-emerald-500/20 text-emerald-300 border-emerald-500/30">
                          v{preset.version}
                        </span>
                      </div>
                      <p className="text-slate-400 text-[11px] leading-relaxed line-clamp-2">
                        {preset.description || 'No description provided.'}
                      </p>
                    </div>
                  ))}
                  {blueprints.filter(bp => isLaunchableBlueprint(bp.status)).length === 0 && (
                    <div className="p-3 text-xs text-slate-500 text-center border border-dashed border-slate-800 rounded-xl">
                      No launchable workflows available. Publish or share a workflow first.
                    </div>
                  )}
                </div>
              </div>

              <div>
                <label className="text-slate-300 font-medium block mb-1">Target Workflow Blueprint Name</label>
                <input
                  type="text"
                  value={startWorkflowName}
                  onChange={e => setStartWorkflowName(e.target.value)}
                  placeholder="e.g. OrderSagaFulfillment"
                  className="w-full bg-slate-950 border border-slate-700 rounded-xl px-3 py-2 text-white font-mono text-xs focus:outline-none focus:border-blue-500"
                />
              </div>

              <div className="p-3 bg-slate-950 border border-slate-800 rounded-xl text-slate-400 text-[11px]">
                Workflow instance will be created and bound to tenant <strong>{session.tenantName}</strong> (<span className="font-mono">{session.tenantId.substring(0, 8)}...</span>).
              </div>

              <div className="pt-2 flex justify-end gap-2 border-t border-slate-800">
                <button
                  onClick={() => setShowStartModal(false)}
                  className="px-4 py-2 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded-xl font-semibold"
                >
                  Cancel
                </button>
                <button
                  onClick={handleStartInstance}
                  disabled={startingInstance || !startWorkflowName.trim()}
                  className="px-5 py-2 bg-emerald-600 hover:bg-emerald-500 disabled:opacity-50 text-white rounded-xl font-semibold flex items-center gap-1.5 transition-all shadow-lg shadow-emerald-900/20"
                >
                  {startingInstance ? 'Starting...' : 'Launch Workflow'}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Blueprint Detail & Editor Modals */}
      {selectedBlueprint && (
        <DetailView 
          item={selectedBlueprint} 
          validation={validationResult}
          onClose={() => setSelectedBlueprint(null)}
          onValidate={async () => {
            if (selectedBlueprint) {
              const res = await api.validate(selectedBlueprint.id, 'Tenant');
              setValidationResult(res);
            }
          }}
        />
      )}

      {(isCreatingBlueprint || editorBlueprint) && (
        <EditorView 
          item={editorBlueprint || undefined}
          validation={validationResult}
          onClose={() => { setEditorBlueprint(null); setIsCreatingBlueprint(false); setValidationResult(null); }}
          onSave={handleSaveDraft}
        />
      )}
        </main>
        <section className="max-w-7xl mx-auto px-6 mb-8 w-full">
          <McpAgentGuideline />
        </section>
        <footer className="py-8 border-t border-slate-800 text-center text-xs text-slate-500 bg-slate-950/50">
          <div className="max-w-7xl mx-auto px-6 flex flex-col sm:flex-row justify-between items-center gap-4">
            <div>© 2026 {legalEntity.productName} — {sellerIdentity}. <a href={`mailto:${legalEntity.supportEmail}`} className="text-blue-400 hover:underline">{legalEntity.supportEmail}</a></div>
            <div className="flex flex-wrap justify-center gap-x-6 gap-y-2">
              <LegalFooterLinks className="hover:underline" />
              <a href="/swagger" target="_blank" className="hover:underline">Swagger Docs</a>
              <a href={mcpUrl} target="_blank" className="hover:underline">MCP Endpoint</a>
              <a href="https://github.com/ObaidulKabir/FlowOS" target="_blank" className="hover:underline">GitHub</a>
            </div>
          </div>
        </footer>
      </div>
    </>
  );
};

