import React, { useState, useEffect } from 'react';
import { AuthSession, WorkflowClass, WorkflowInstance, ValidationResult } from '../types';
import { api } from '../api/client';
import { TenantManager } from './TenantManager';
import { WorkflowTable } from './WorkflowTable';
import { WorkflowInstanceTable } from './WorkflowInstanceTable';
import { EventAuditViewer } from './EventAuditViewer';
import { DetailView } from './DetailView';
import { 
  Shield, Building2, Plus, RefreshCw, Activity, 
  Globe, Cpu, Clock, Terminal, AlertTriangle
} from 'lucide-react';

interface Props {
  session: AuthSession;
  onSwitchWorkspace: () => void;
}

export const AdminDashboard: React.FC<Props> = ({ session }) => {
  const [activeTab, setActiveTab] = useState<'Tenants' | 'Catalog' | 'ReviewQueue' | 'Instances' | 'Events' | 'Kernel'>('Tenants');
  const [catalogSubTab, setCatalogSubTab] = useState<'All' | 'Public' | 'Shared' | 'Published'>('All');

  const [tenantsCount, setTenantsCount] = useState(0);
  const [blueprints, setBlueprints] = useState<WorkflowClass[]>([]);
  const [instances, setInstances] = useState<WorkflowInstance[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Register Modal Trigger for TenantManager
  const [openRegisterModal, setOpenRegisterModal] = useState(false);

  // Detail View Modal
  const [selectedBlueprint, setSelectedBlueprint] = useState<WorkflowClass | null>(null);
  const [validationResult, setValidationResult] = useState<ValidationResult | null>(null);

  const loadData = async () => {
    setLoading(true);
    setError(null);
    try {
      // Load Tenants count
      const tList = await api.listTenants();
      setTenantsCount(tList.length);

      // Load Blueprints
      const bpList = await api.list(undefined, undefined, 'Admin');
      setBlueprints(bpList);

      // Load Fleet Instances
      const instList = await api.listInstances('Admin');
      setInstances(instList);
    } catch (err: any) {
      setError(err.message || 'Failed to load platform data');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadData();
  }, [activeTab]);

  // Blueprints waiting for admin approval (Shared scope in Submitted/Shared status)
  const pendingApprovals = blueprints.filter(b => b.scope === 1 || b.status === 2);

  const handleApprove = async (id: string) => {
    try {
      await api.approve(id);
      alert('Workflow approved for global platform catalog!');
      await loadData();
    } catch (err: any) {
      alert(`Approval failed: ${err.message}`);
    }
  };

  const handleDeprecate = async (id: string) => {
    try {
      await api.deprecate(id, 'Admin');
      await loadData();
    } catch (err: any) {
      alert(`Deprecate failed: ${err.message}`);
    }
  };

  const handleAbandon = async (id: string) => {
    try {
      await api.abandon(id, 'Admin');
      await loadData();
    } catch (err: any) {
      alert(`Abandon failed: ${err.message}`);
    }
  };

  return (
    <div className="space-y-6">
      
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
              Cluster-wide control plane: manage multi-tenancy, approve tenant workflow submissions for the public catalog, and audit security telemetry.
            </p>
          </div>

          <div className="flex flex-wrap items-center gap-2.5">
            <button
              onClick={() => {
                setActiveTab('Tenants');
                setOpenRegisterModal(true);
              }}
              className="px-4 py-2.5 bg-blue-600 hover:bg-blue-500 text-white font-semibold rounded-xl text-xs flex items-center gap-2 shadow-lg shadow-blue-500/25 transition-all"
            >
              <Plus size={15} />
              <span>Register New Tenant</span>
            </button>
            <button
              onClick={loadData}
              className="p-2.5 bg-slate-800 hover:bg-slate-750 border border-slate-700 text-slate-300 rounded-xl transition-colors"
              title="Refresh platform telemetry"
            >
              <RefreshCw size={14} className={loading ? 'animate-spin text-purple-400' : ''} />
            </button>
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

          <div className="bg-slate-900/80 p-3 rounded-xl border border-slate-800">
            <div className="text-[11px] text-slate-400 flex items-center gap-1.5">
              <Globe size={12} className="text-emerald-400" />
              <span>Catalog Blueprints</span>
            </div>
            <div className="text-xl font-bold text-emerald-400 mt-0.5">{blueprints.length}</div>
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
        <div className="border-b border-slate-700 bg-slate-850">
          <nav className="flex divide-x divide-slate-700 text-xs font-semibold">
            <button
              onClick={() => setActiveTab('Tenants')}
              className={`flex-1 py-3.5 px-4 text-center transition-all flex items-center justify-center gap-2 ${
                activeTab === 'Tenants' ? 'bg-purple-600 text-white' : 'text-slate-400 hover:text-white hover:bg-slate-750'
              }`}
            >
              <Building2 size={15} />
              <span>🏢 Multi-Tenant Fleet ({tenantsCount})</span>
            </button>
            <button
              onClick={() => setActiveTab('ReviewQueue')}
              className={`flex-1 py-3.5 px-4 text-center transition-all flex items-center justify-center gap-2 ${
                activeTab === 'ReviewQueue' ? 'bg-purple-600 text-white' : 'text-slate-400 hover:text-white hover:bg-slate-750'
              }`}
            >
              <AlertTriangle size={15} className={pendingApprovals.length > 0 ? 'text-amber-400' : ''} />
              <span>📋 Review Queue ({pendingApprovals.length})</span>
            </button>
            <button
              onClick={() => setActiveTab('Catalog')}
              className={`flex-1 py-3.5 px-4 text-center transition-all flex items-center justify-center gap-2 ${
                activeTab === 'Catalog' ? 'bg-purple-600 text-white' : 'text-slate-400 hover:text-white hover:bg-slate-750'
              }`}
            >
              <Globe size={15} />
              <span>🌐 Blueprint Catalog</span>
            </button>
            <button
              onClick={() => setActiveTab('Instances')}
              className={`flex-1 py-3.5 px-4 text-center transition-all flex items-center justify-center gap-2 ${
                activeTab === 'Instances' ? 'bg-purple-600 text-white' : 'text-slate-400 hover:text-white hover:bg-slate-750'
              }`}
            >
              <Activity size={15} />
              <span>⚡ Fleet Instances ({instances.length})</span>
            </button>
            <button
              onClick={() => setActiveTab('Events')}
              className={`flex-1 py-3.5 px-4 text-center transition-all flex items-center justify-center gap-2 ${
                activeTab === 'Events' ? 'bg-purple-600 text-white' : 'text-slate-400 hover:text-white hover:bg-slate-750'
              }`}
            >
              <Terminal size={15} />
              <span>📡 Global Event Audit</span>
            </button>
            <button
              onClick={() => setActiveTab('Kernel')}
              className={`flex-1 py-3.5 px-4 text-center transition-all flex items-center justify-center gap-2 ${
                activeTab === 'Kernel' ? 'bg-purple-600 text-white' : 'text-slate-400 hover:text-white hover:bg-slate-750'
              }`}
            >
              <Cpu size={15} />
              <span>⚙️ Engine Kernel</span>
            </button>
          </nav>
        </div>

        {/* Tab Contents */}
        <div className="p-6">
          {activeTab === 'Tenants' && (
            <TenantManager
              openRegisterModal={openRegisterModal}
              onRegisterModalClosed={() => setOpenRegisterModal(false)}
              onTenantChange={() => loadData()}
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
                  const item = await api.get(id, 'Admin');
                  setSelectedBlueprint(item);
                }}
                onApprove={handleApprove}
                onDeprecate={handleDeprecate}
                onAbandon={handleAbandon}
              />
            </div>
          )}

          {activeTab === 'Catalog' && (
            <div className="space-y-4">
              <div className="flex bg-slate-900 rounded-xl p-1 border border-slate-700 text-xs w-fit">
                {(['All', 'Public', 'Shared', 'Published'] as const).map(sub => (
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

              <WorkflowTable
                items={blueprints}
                currentTab={catalogSubTab}
                isAdmin={true}
                onView={async (id) => {
                  const item = await api.get(id, 'Admin');
                  setSelectedBlueprint(item);
                }}
                onApprove={handleApprove}
                onDeprecate={handleDeprecate}
                onAbandon={handleAbandon}
              />
            </div>
          )}

          {activeTab === 'Instances' && (
            <div className="space-y-4">
              <div className="text-xs text-slate-400">
                Fleet-wide workflow instance executions across all tenant clusters.
              </div>
              <WorkflowInstanceTable items={instances} />
            </div>
          )}

          {activeTab === 'Events' && (
            <EventAuditViewer role="Admin" onInspectWorkflow={() => setActiveTab('Instances')} />
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
                      <span className="text-purple-300 font-bold font-mono">21 Tools Registered</span>
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
              const res = await api.validate(selectedBlueprint.id, 'Admin');
              setValidationResult(res);
            }
          }}
        />
      )}

    </div>
  );
};
