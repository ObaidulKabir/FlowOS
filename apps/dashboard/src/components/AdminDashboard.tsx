import React, { useState, useEffect } from 'react';
import { AuthSession, WorkflowClass, WorkflowClassScope, WorkflowInstance, ValidationResult, TenantDto, WorkflowClassStatus } from '../types';
import { api } from '../api/client';
import { TenantManager } from './TenantManager';
import { WorkflowTable } from './WorkflowTable';
import { resolveWorkflowInstanceId } from '../audit/resolveWorkflowInstance';
import { WorkflowInstanceTable } from './WorkflowInstanceTable';
import { EventAuditViewer } from './EventAuditViewer';
import { DetailView } from './DetailView';
import { DeadLetterQueueViewer } from './DeadLetterQueueViewer';
import { CapabilitiesShowcase } from './CapabilitiesShowcase';
import { CompetitiveComparison } from './CompetitiveComparison';
import { DashboardChrome, sidebarItemClass } from './DashboardChrome';
import { McpAgentGuideline } from './McpAgentGuideline';
import { usePlatformMetrics } from '../platformMetrics';
import { legalEntity, sellerIdentity } from '../legalEntity';
import { LegalFooterLinks } from './LegalFooterLinks';
import { 
  Shield, Building2, Plus, RefreshCw, Activity, 
  Cpu, Clock, Terminal, AlertTriangle, Sparkles, Scale, Layers
} from 'lucide-react';

interface Props {
  session: AuthSession;
  onGoHome: () => void;
  onSwitchRole: (tenantContext?: { tenantId: string; tenantName?: string }) => void;
  onRegister: () => void;
  onSignOut: () => void;
  mcpUrl: string;
}

export const AdminDashboard: React.FC<Props> = ({
  session,
  onGoHome,
  onSwitchRole,
  onRegister,
  onSignOut,
  mcpUrl
}) => {
  const { mcpTools, isLiveMcpCount, tests, verifiedOn } = usePlatformMetrics();
  const [activeTab, setActiveTab] = useState<'Tenants' | 'Catalog' | 'ReviewQueue' | 'Instances' | 'Events' | 'Kernel' | 'DeadLetters' | 'Capabilities' | 'Comparison'>('Tenants');
  const [catalogSubTab, setCatalogSubTab] = useState<'All' | 'Drafts' | 'Published' | 'Shared' | 'Public'>('All');

  const [tenants, setTenants] = useState<TenantDto[]>([]);
  const [selectedTenantFilter, setSelectedTenantFilter] = useState<string>('');
  const [tenantsCount, setTenantsCount] = useState(0);
  const [blueprints, setBlueprints] = useState<WorkflowClass[]>([]);
  const [instances, setInstances] = useState<WorkflowInstance[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [auditFocus, setAuditFocus] = useState<{ instanceId: string; requestId: number } | null>(null);

  // Register Modal Trigger for TenantManager
  const [openRegisterModal, setOpenRegisterModal] = useState(false);

  // Detail View Modal
  const [selectedBlueprint, setSelectedBlueprint] = useState<WorkflowClass | null>(null);
  const [validationResult, setValidationResult] = useState<ValidationResult | null>(null);

  const effectiveTenantScope = selectedTenantFilter || session.tenantId;
  const effectiveTenantName = selectedTenantFilter
    ? tenants.find(item => item.tenantId === selectedTenantFilter)?.name || selectedTenantFilter
    : session.tenantName || session.tenantId;

  const resolveBlueprintTenantScope = (blueprint?: WorkflowClass | null) =>
    selectedTenantFilter || blueprint?.tenantId || session.tenantId;

  const isSharedCatalogEntry = (blueprint: WorkflowClass) =>
    blueprint.scope === WorkflowClassScope.Shared || blueprint.status === WorkflowClassStatus.Shared;

  const isPublicCatalogEntry = (blueprint: WorkflowClass) =>
    blueprint.scope === WorkflowClassScope.Public || blueprint.status === WorkflowClassStatus.Public;

  const matchesCatalogSubTab = (blueprint: WorkflowClass) => {
    if (catalogSubTab === 'All') return true;
    if (catalogSubTab === 'Drafts') return blueprint.status === WorkflowClassStatus.Draft;
    if (catalogSubTab === 'Published') return blueprint.status === WorkflowClassStatus.Published;
    if (catalogSubTab === 'Shared') return isSharedCatalogEntry(blueprint);
    if (catalogSubTab === 'Public') return isPublicCatalogEntry(blueprint);
    return true;
  };

  const loadData = async (tenantIdOverride?: string) => {
    setLoading(true);
    setError(null);
    const notices: string[] = [];
    const targetTenantId = tenantIdOverride ?? effectiveTenantScope;

    try {
      const tList = await api.listTenants();
      setTenants(tList);
      setTenantsCount(tList.length);
    } catch (err: any) {
      notices.push(err.message || 'Failed to load tenants');
    }

    try {
      const bpList = await api.list(undefined, undefined, 'Admin', targetTenantId);
      setBlueprints(bpList);
    } catch (err: any) {
      setBlueprints([]);
      const message = err.message || 'Failed to list workflow classes';
      if (!/HTTP 401|HTTP 403/.test(message)) notices.push(message);
    }

    try {
      const instList = await api.listInstances('Admin');
      setInstances(instList);
    } catch (err: any) {
      setInstances([]);
      const message = err.message || 'Failed to list workflow instances';
      if (!/HTTP 401|HTTP 403/.test(message)) notices.push(message);
    }

    setError(notices[0] || null);
    setLoading(false);
  };

  const handleViewTenantWorkflows = (tenantId: string) => {
    setSelectedTenantFilter(tenantId);
    setActiveTab('Catalog');
    setCatalogSubTab('All');
  };

  useEffect(() => {
    void loadData(selectedTenantFilter || undefined);
  }, [activeTab, selectedTenantFilter]);

  // Blueprints waiting for admin approval (Shared scope in Submitted/Shared status)
  const pendingApprovals = blueprints.filter(isSharedCatalogEntry);

  const handleApprove = async (id: string) => {
    try {
      await api.approve(id, effectiveTenantScope);
      alert('Workflow approved for global platform catalog!');
      await loadData(effectiveTenantScope);
    } catch (err: any) {
      alert(`Approval failed: ${err.message}`);
    }
  };

  const handleDeprecate = async (id: string) => {
    try {
      await api.deprecate(id, 'Admin', effectiveTenantScope);
      await loadData(effectiveTenantScope);
    } catch (err: any) {
      alert(`Deprecate failed: ${err.message}`);
    }
  };

  const handleAbandon = async (id: string) => {
    try {
      await api.abandon(id, 'Admin', effectiveTenantScope);
      await loadData(effectiveTenantScope);
    } catch (err: any) {
      alert(`Abandon failed: ${err.message}`);
    }
  };

  const handleSwitchToTenantView = () => {
    if (!selectedTenantFilter) {
      onSwitchRole();
      return;
    }

    const selectedTenant = tenants.find(item => item.tenantId === selectedTenantFilter);
    onSwitchRole({ tenantId: selectedTenantFilter, tenantName: selectedTenant?.name });
  };

  return (
    <>
      <DashboardChrome
        session={session}
        onGoHome={onGoHome}
        onSwitchRole={handleSwitchToTenantView}
        onRegister={onRegister}
        onSignOut={onSignOut}
        mcpUrl={mcpUrl}
        nav={
          <div className="space-y-4">
            <div className="space-y-1">
              <p className="px-2 text-[10px] font-semibold uppercase tracking-wider text-slate-500">Actions</p>
              <button
                type="button"
                onClick={() => {
                  setActiveTab('Tenants');
                  setOpenRegisterModal(true);
                }}
                className={sidebarItemClass(false, 'bg-purple-600 text-white')}
              >
                <Plus size={14} />
                <span className="truncate">Register Tenant</span>
              </button>
              <button type="button" onClick={() => void loadData()} className={sidebarItemClass(false, 'bg-purple-600 text-white')}>
                <RefreshCw size={14} className={loading ? 'animate-spin text-purple-400' : ''} />
                <span className="truncate">Refresh</span>
              </button>
            </div>
            <div className="space-y-1">
              <p className="px-2 text-[10px] font-semibold uppercase tracking-wider text-slate-500">Workspace</p>
              <button type="button" onClick={() => setActiveTab('Tenants')} className={sidebarItemClass(activeTab === 'Tenants', 'bg-purple-600 text-white')}>
                <Building2 size={14} />
                <span className="truncate">Tenants ({tenantsCount})</span>
              </button>
              <button type="button" onClick={() => setActiveTab('ReviewQueue')} className={sidebarItemClass(activeTab === 'ReviewQueue', 'bg-purple-600 text-white')}>
                <AlertTriangle size={14} />
                <span className="truncate">Review Queue ({pendingApprovals.length})</span>
              </button>
              <button type="button" onClick={() => setActiveTab('Catalog')} className={sidebarItemClass(activeTab === 'Catalog', 'bg-purple-600 text-white')}>
                <Layers size={14} />
                <span className="truncate">Workflows ({blueprints.length})</span>
              </button>
              <button type="button" onClick={() => setActiveTab('Instances')} className={sidebarItemClass(activeTab === 'Instances', 'bg-purple-600 text-white')}>
                <Activity size={14} />
                <span className="truncate">Fleet Instances ({instances.length})</span>
              </button>
              <button type="button" onClick={() => setActiveTab('Events')} className={sidebarItemClass(activeTab === 'Events', 'bg-purple-600 text-white')}>
                <Terminal size={14} />
                <span className="truncate">Event Audit</span>
              </button>
              <button type="button" onClick={() => setActiveTab('Kernel')} className={sidebarItemClass(activeTab === 'Kernel', 'bg-purple-600 text-white')}>
                <Cpu size={14} />
                <span className="truncate">Engine Kernel</span>
              </button>
              <button type="button" onClick={() => setActiveTab('DeadLetters')} className={sidebarItemClass(activeTab === 'DeadLetters', 'bg-purple-600 text-white')}>
                <AlertTriangle size={14} />
                <span className="truncate">Dead Letters</span>
              </button>
              <button type="button" onClick={() => setActiveTab('Capabilities')} className={sidebarItemClass(activeTab === 'Capabilities', 'bg-purple-600 text-white')}>
                <Sparkles size={14} />
                <span className="truncate">Capabilities</span>
              </button>
              <button type="button" onClick={() => setActiveTab('Comparison')} className={sidebarItemClass(activeTab === 'Comparison', 'bg-purple-600 text-white')}>
                <Scale size={14} />
                <span className="truncate">vs Competitors</span>
              </button>
            </div>
          </div>
        }
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
        <main className="max-w-7xl mx-auto px-6 py-8 flex-1 w-full space-y-6">
      
      {/* Platform Admin Governance Banner */}
      <div className="bg-gradient-to-r from-purple-950/60 via-slate-900 to-indigo-950/40 border border-purple-500/30 p-6 rounded-3xl shadow-2xl relative overflow-hidden">
        <div className="flex flex-col md:flex-row md:items-center justify-between gap-6 relative z-10">
          <div>
            <div className="flex items-center gap-2 mb-2">
              <span className="px-2.5 py-1 rounded-full text-xs font-bold bg-purple-500/20 text-purple-300 border border-purple-500/30 flex items-center gap-1.5">
                <Shield size={13} /> Platform Governance & Control Plane
              </span>
              <span className="text-xs text-slate-500">•</span>
              <span className="text-xs text-slate-400 font-mono">SuperAdmin: {session.username || 'root'}</span>
            </div>

            <h1 className="text-2xl md:text-3xl font-extrabold text-white">
              Global Platform Administration
            </h1>
            <p className="text-xs text-slate-400 mt-1 max-w-2xl">
              Cluster-wide control plane: manage multi-tenancy, activate paid plans (Starter through Scale, or Enterprise), approve tenant workflow submissions for the public catalog, and audit security telemetry.
            </p>
          </div>

        </div>

        {/* 4 Stat Cards */}
        <div className="grid grid-cols-2 md:grid-cols-4 gap-3 mt-6 pt-6 border-t border-slate-800/80">
          <div className="bg-slate-900/80 p-3 rounded-xl border border-slate-800">
            <div className="text-[11px] text-slate-400 flex items-center gap-1.5">
              <Building2 size={12} className="text-blue-400" />
              <span>Registered Tenants</span>
            </div>
            <div className="text-xl font-bold text-white mt-0.5">{tenantsCount}</div>
          </div>

          <div 
            onClick={() => setActiveTab('Catalog')}
            className="bg-slate-900/80 p-3 rounded-xl border border-slate-800 cursor-pointer hover:border-purple-500/50 transition-colors"
          >
            <div className="text-[11px] text-slate-400 flex items-center gap-1.5">
              <Layers size={12} className="text-purple-400" />
              <span>Workflows & Blueprints</span>
            </div>
            <div className="text-xl font-bold text-purple-400 mt-0.5">{blueprints.length}</div>
          </div>

          <div className="bg-slate-900/80 p-3 rounded-xl border border-slate-800">
            <div className="text-[11px] text-slate-400 flex items-center gap-1.5">
              <AlertTriangle size={12} className="text-amber-400" />
              <span>Review Queue</span>
            </div>
            <div className="text-xl font-bold text-amber-400 mt-0.5">{pendingApprovals.length}</div>
          </div>

          <div className="bg-slate-900/80 p-3 rounded-xl border border-slate-800">
            <div className="text-[11px] text-slate-400 flex items-center gap-1.5">
              <Activity size={12} className="text-purple-400" />
              <span>Fleet Active Instances</span>
            </div>
            <div className="text-xl font-bold text-purple-300 mt-0.5">{instances.length}</div>
          </div>
        </div>
      </div>

      {error && (
        <div className="p-4 bg-rose-900/30 border border-rose-700 rounded-xl text-xs text-rose-300">
          {error}
        </div>
      )}

      {/* Admin Navigation Tabs */}
      <div className="bg-slate-800 border border-slate-700 rounded-2xl overflow-hidden shadow-xl">
        <div className="p-6">
          {activeTab === 'Capabilities' && (
            <CapabilitiesShowcase />
          )}
          {activeTab === 'Comparison' && (
            <CompetitiveComparison />
          )}
          {activeTab === 'Tenants' && (
            <TenantManager
              openRegisterModal={openRegisterModal}
              onRegisterModalClosed={() => setOpenRegisterModal(false)}
              onTenantChange={() => loadData()}
              onViewTenantWorkflows={handleViewTenantWorkflows}
            />
          )}

          {activeTab === 'ReviewQueue' && (
            <div className="space-y-4">
              <div className="p-4 bg-amber-500/10 border border-amber-500/20 rounded-2xl flex items-start gap-3 text-xs text-amber-300">
                <AlertTriangle size={18} className="mt-0.5 shrink-0" />
                <div>
                  <strong>Platform Workflow Review Queue</strong>: Tenants submit their verified workflow blueprints for platform-wide promotion. Approving a blueprint promotes it to the global Public catalog so all tenants can utilize it.
                </div>
              </div>

              <WorkflowTable
                items={pendingApprovals}
                currentTab="Shared"
                isAdmin={true}
                onView={async (id) => {
                  const bp = blueprints.find(b => b.id === id);
                  const item = await api.get(id, 'Admin', resolveBlueprintTenantScope(bp));
                  setSelectedBlueprint(item);
                }}
                onApprove={handleApprove}
                onDeprecate={handleDeprecate}
              />
            </div>
          )}

          {activeTab === 'Catalog' && (
            <div className="space-y-4">
              <div className="flex flex-wrap items-center justify-between gap-3 bg-slate-900/80 p-3 rounded-xl border border-slate-700/80">
                <div className="flex items-center gap-2 flex-wrap">
                  <span className="text-xs font-semibold text-slate-300 flex items-center gap-1.5">
                    <Building2 size={13} className="text-purple-400" /> Tenant Scope:
                  </span>
                  <select
                    value={selectedTenantFilter}
                    onChange={(e) => {
                      setSelectedTenantFilter(e.target.value);
                    }}
                    className="bg-slate-950 border border-slate-700 text-xs text-white rounded-lg px-2.5 py-1.5 focus:border-purple-500 focus:outline-none"
                  >
                    <option value="">{`Session Tenant: ${effectiveTenantName}`}</option>
                    {tenants.map(t => (
                      <option key={t.tenantId} value={t.tenantId}>
                        {t.name} ({t.tenantId.substring(0, 8)}...)
                      </option>
                    ))}
                  </select>
                  {selectedTenantFilter && (
                    <button
                      onClick={() => {
                        setSelectedTenantFilter('');
                      }}
                      className="text-[11px] text-purple-400 hover:text-purple-300 underline ml-1"
                    >
                      Reset Filter
                    </button>
                  )}
                </div>

                <div className="flex bg-slate-900 rounded-xl p-1 border border-slate-700 text-xs w-fit">
                  {(['All', 'Drafts', 'Published', 'Shared', 'Public'] as const).map(sub => (
                    <button
                      key={sub}
                      onClick={() => setCatalogSubTab(sub)}
                      className={`px-3 py-1.5 rounded-lg text-xs font-medium transition-all ${
                        catalogSubTab === sub ? 'bg-purple-600 text-white shadow-sm' : 'text-slate-400 hover:text-slate-200'
                      }`}
                    >
                      {sub}
                    </button>
                  ))}
                </div>
              </div>

              <WorkflowTable
                items={blueprints.filter(matchesCatalogSubTab)}
                currentTab={catalogSubTab}
                isAdmin={true}
                onView={async (id) => {
                  const bp = blueprints.find(b => b.id === id);
                  const item = await api.get(id, 'Admin', resolveBlueprintTenantScope(bp));
                  setSelectedBlueprint(item);
                }}
                onApprove={handleApprove}
                onDeprecate={handleDeprecate}
              />
            </div>
          )}

          {activeTab === 'Instances' && (
            <div className="space-y-4">
              <div className="text-xs text-slate-400">
                Fleet-wide workflow instance executions across all tenant clusters.
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

          {activeTab === 'Events' && (
            <EventAuditViewer
              role="Admin"
              onInspectWorkflow={(instanceId) => {
                const resolved = resolveWorkflowInstanceId(instances, instanceId);
                if (!resolved) return;
                setAuditFocus({ instanceId: resolved, requestId: Date.now() });
                setActiveTab('Instances');
              }}
            />
          )}

          {activeTab === 'Kernel' && (
            <div className="space-y-6">
              <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                <div className="bg-slate-900 p-5 rounded-2xl border border-slate-700 space-y-3">
                  <div className="flex items-center gap-2 text-blue-400 font-bold text-sm">
                    <Cpu size={18} />
                    <span>Dual-Kernel State Engine</span>
                  </div>
                  <div className="space-y-2 text-xs text-slate-300">
                    <div className="flex justify-between">
                      <span className="text-slate-400">State Enforcement:</span>
                      <span className="text-emerald-400 font-semibold font-mono">ACTIVE (Strict)</span>
                    </div>
                    <div className="flex justify-between">
                      <span className="text-slate-400">Orchestrator:</span>
                      <span className="text-white font-mono">FlowOS Step Engine</span>
                    </div>
                    <div className="flex justify-between">
                      <span className="text-slate-400">Tenant Boundary:</span>
                      <span className="text-emerald-400 font-mono">Zero-Trust Enforced</span>
                    </div>
                  </div>
                </div>

                <div className="bg-slate-900 p-5 rounded-2xl border border-slate-700 space-y-3">
                  <div className="flex items-center gap-2 text-purple-400 font-bold text-sm">
                    <Terminal size={18} />
                    <span>MCP Server Telemetry</span>
                  </div>
                  <div className="space-y-2 text-xs text-slate-300">
                    <div className="flex justify-between">
                      <span className="text-slate-400">Exposed Tools:</span>
                      <span className="text-purple-300 font-bold font-mono">{mcpTools} Registered</span>
                    </div>
                    <div className="flex justify-between">
                      <span className="text-slate-400">Tool Count Source:</span>
                      <span className="text-white font-mono">{isLiveMcpCount ? 'Live Discovery' : 'Verified Build'}</span>
                    </div>
                    <div className="flex justify-between">
                      <span className="text-slate-400">Automated Tests:</span>
                      <span className="text-emerald-300 font-bold font-mono">{tests.total} Passing</span>
                    </div>
                    <div className="flex justify-between gap-3">
                      <span className="text-slate-400">Test Breakdown:</span>
                      <span className="text-slate-300 font-mono text-right">
                        {tests.unit} unit / {tests.endToEnd} E2E / {tests.mcp} MCP
                      </span>
                    </div>
                    <div className="flex justify-between">
                      <span className="text-slate-400">Verified On:</span>
                      <span className="text-slate-300 font-mono">{verifiedOn}</span>
                    </div>
                    <div className="flex justify-between">
                      <span className="text-slate-400">Protocol:</span>
                      <span className="text-white font-mono">Model Context Protocol 1.0</span>
                    </div>
                    <div className="flex justify-between">
                      <span className="text-slate-400">Transport:</span>
                      <span className="text-slate-300 font-mono">SSE / STDIO / JSON-RPC</span>
                    </div>
                  </div>
                </div>

                <div className="bg-slate-900 p-5 rounded-2xl border border-slate-700 space-y-3">
                  <div className="flex items-center gap-2 text-emerald-400 font-bold text-sm">
                    <Clock size={18} />
                    <span>SLA & Timer Dispatcher</span>
                  </div>
                  <div className="space-y-2 text-xs text-slate-300">
                    <div className="flex justify-between">
                      <span className="text-slate-400">Background Worker:</span>
                      <span className="text-emerald-400 font-semibold font-mono">Running (5s interval)</span>
                    </div>
                    <div className="flex justify-between">
                      <span className="text-slate-400">Outbox Relay:</span>
                      <span className="text-white font-mono">PostgreSQL Transactional</span>
                    </div>
                    <div className="flex justify-between">
                      <span className="text-slate-400">Auto Escalation:</span>
                      <span className="text-emerald-400 font-mono">Zero-Zombie SLA</span>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          )}

          {activeTab === 'DeadLetters' && (
            <DeadLetterQueueViewer session={session} />
          )}
        </div>
      </div>

      {/* Blueprint Detail Modal */}
      {selectedBlueprint && (
        <DetailView 
          item={selectedBlueprint} 
          validation={validationResult}
          onClose={() => setSelectedBlueprint(null)}
          onValidate={async () => {
            if (selectedBlueprint) {
              const res = await api.validate(
                selectedBlueprint.id,
                'Admin',
                resolveBlueprintTenantScope(selectedBlueprint)
              );
              setValidationResult(res);
            }
          }}
          onApprove={activeTab === 'ReviewQueue' ? handleApprove : undefined}
          onReject={activeTab === 'ReviewQueue' ? handleAbandon : undefined}
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
