import React, { useState, useEffect } from 'react';
import { AuthSession, WorkflowClass, WorkflowInstance, ValidationResult, CreateDraftRequest, TenantDto } from '../types';
import { api, setActiveTenantId } from '../api/client';
import { WorkflowInstanceTable } from './WorkflowInstanceTable';
import { EventAuditViewer } from './EventAuditViewer';
import { TenantApiKeyManager } from './TenantApiKeyManager';
import { DetailView } from './DetailView';
import { EditorView } from './EditorView';
import { CapabilitiesShowcase } from './CapabilitiesShowcase';
import { CompetitiveComparison } from './CompetitiveComparison';
import { ApplicationWorkspace } from './ApplicationWorkspace';
import { ContextSimulationStudio } from './ContextSimulationStudio';
import { 
  Building2, Plus, RefreshCw, Key, Activity, 
  Copy, Check, Filter, Sparkles, Scale, Layers, FlaskConical
} from 'lucide-react';

interface Props {
  session: AuthSession;
  onSwitchWorkspace: () => void;
  onTenantChange?: (newTenantId: string, newTenantName: string) => void;
}

export const TenantDashboard: React.FC<Props> = ({ session, onSwitchWorkspace, onTenantChange }) => {
  const [activeTab, setActiveTab] = useState<'Application' | 'Instances' | 'Events' | 'Keys' | 'Simulator' | 'Capabilities' | 'Comparison'>('Application');
  
  const [blueprints, setBlueprints] = useState<WorkflowClass[]>([]);
  const [instances, setInstances] = useState<WorkflowInstance[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [copiedTenantId, setCopiedTenantId] = useState(false);

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
  const [startWorkflowName, setStartWorkflowName] = useState('ExpenseApprovalV2');
  const [startingInstance, setStartingInstance] = useState(false);

  const [simulationTarget, setSimulationTarget] = useState<{
    bindingId?: string;
    revision?: 'draft' | 'active';
  }>({});

  const loadData = async () => {
    setLoading(true);
    setError(null);
    try {
      if (activeTab === 'Instances' || activeTab === 'Application') {
        const [instResult, bpResult] = await Promise.allSettled([
          api.listInstances('Tenant'),
          api.list(undefined, undefined, 'Tenant')
        ]);
        if (instResult.status === 'fulfilled') setInstances(instResult.value);
        if (bpResult.status === 'fulfilled') setBlueprints(bpResult.value);
        const failed = [instResult, bpResult].find(result => result.status === 'rejected') as PromiseRejectedResult | undefined;
        if (failed && instResult.status === 'rejected' && bpResult.status === 'rejected') {
          throw failed.reason;
        }
        if (failed) setError(failed.reason?.message || 'Failed to load workspace data');
      }
    } catch (err: any) {
      setError(err.message || 'Failed to load workspace data');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadData();
  }, [activeTab, session.tenantId]);

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
      setActiveTab('Instances');
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

  return (
    <div className="space-y-6">
      
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
              <span className="text-slate-500 ml-1">
                (Tenant filter active for multi-tenant testing; final release will automatically bind to authenticated user organization).
              </span>
            </p>
          </div>

          {/* Quick Actions */}
          <div className="flex flex-wrap items-center gap-2.5">
            <button
              onClick={() => setShowStartModal(true)}
              className="px-4 py-2.5 bg-gradient-to-r from-emerald-600 to-teal-600 hover:from-emerald-500 hover:to-teal-500 text-white font-bold rounded-xl text-xs flex items-center gap-2 shadow-[0_0_15px_rgba(16,185,129,0.4)] transition-all animate-pulse hover:animate-none border border-emerald-400/30"
            >
              <Sparkles size={14} className="text-emerald-200" />
              <span>Launch Demo Workflow</span>
            </button>
            <button
              onClick={() => setIsCreatingBlueprint(true)}
              className="px-4 py-2.5 bg-blue-600 hover:bg-blue-500 text-white font-semibold rounded-xl text-xs flex items-center gap-2 shadow-lg shadow-blue-500/25 transition-all"
            >
              <Plus size={14} />
              <span>New Blueprint</span>
            </button>
            <button
              onClick={() => setActiveTab('Keys')}
              className="px-3.5 py-2.5 bg-slate-800 hover:bg-slate-700 border border-slate-700 text-slate-300 rounded-xl text-xs font-semibold flex items-center gap-1.5 transition-colors"
            >
              <Key size={14} className="text-amber-400" />
              <span>API Keys</span>
            </button>
            <button
              onClick={loadData}
              className="p-2.5 bg-slate-800 hover:bg-slate-700 border border-slate-700 text-slate-300 rounded-xl transition-colors"
              title="Refresh workspace"
            >
              <RefreshCw size={14} className={loading ? 'animate-spin text-blue-400' : ''} />
            </button>
          </div>
        </div>

        {/* 4 Stat Counters */}
        <div className="grid grid-cols-2 md:grid-cols-4 gap-3 mt-6 pt-6 border-t border-slate-800/80">
          <div className="bg-slate-900/60 p-3 rounded-xl border border-slate-800">
            <div className="text-[11px] text-slate-400">Running Instances</div>
            <div className="text-lg font-bold text-white mt-0.5">{instances.length}</div>
          </div>
          <div className="bg-slate-900/60 p-3 rounded-xl border border-slate-800">
            <div className="text-[11px] text-slate-400">Applications</div>
            <div className="text-lg font-bold text-blue-400 mt-0.5">{blueprints.length}</div>
          </div>
          <div className="bg-slate-900/60 p-3 rounded-xl border border-slate-800">
            <div className="text-[11px] text-slate-400">Tenant Authority</div>
            <div className="text-lg font-bold text-emerald-400 mt-0.5">Enforced</div>
          </div>
          <div className="bg-slate-900/60 p-3 rounded-xl border border-slate-800">
            <div className="text-[11px] text-slate-400">Kernel Role</div>
            <div className="text-lg font-bold text-slate-200 mt-0.5">Tenant</div>
          </div>
        </div>
      </div>

      {error && (
        <div className="p-4 bg-rose-900/30 border border-rose-700 rounded-xl text-xs text-rose-300 flex items-center gap-2">
          <span>{error}</span>
        </div>
      )}

      {/* Tenant Navigation Tabs */}
      <div className="bg-slate-800 border border-slate-700 rounded-2xl overflow-hidden shadow-xl">
        <div className="border-b border-slate-700 bg-slate-850">
          <nav className="flex flex-wrap text-xs font-semibold">
            <button
              onClick={() => setActiveTab('Application')}
              className={`py-3.5 px-4 text-center transition-all flex items-center justify-center gap-2 min-w-[140px] flex-1 ${
                activeTab === 'Application' ? 'bg-blue-600 text-white' : 'text-slate-400 hover:text-white hover:bg-slate-750'
              }`}
            >
              <Layers size={15} />
              <span>Application</span>
            </button>
            <button
              onClick={() => setActiveTab('Instances')}
              className={`py-3.5 px-4 text-center transition-all flex items-center justify-center gap-2 min-w-[140px] flex-1 ${
                activeTab === 'Instances' ? 'bg-blue-600 text-white' : 'text-slate-400 hover:text-white hover:bg-slate-750'
              }`}
            >
              <Activity size={15} />
              <span>⚡ Live Instances ({instances.length})</span>
            </button>
            <button
              onClick={() => setActiveTab('Events')}
              className={`py-3.5 px-4 text-center transition-all flex items-center justify-center gap-2 min-w-[140px] flex-1 ${
                activeTab === 'Events' ? 'bg-blue-600 text-white' : 'text-slate-400 hover:text-white hover:bg-slate-750'
              }`}
            >
              <Activity size={15} />
              <span>📡 Event Stream & Payloads</span>
            </button>
            <button
              onClick={() => setActiveTab('Keys')}
              className={`py-3.5 px-4 text-center transition-all flex items-center justify-center gap-2 min-w-[140px] flex-1 ${
                activeTab === 'Keys' ? 'bg-blue-600 text-white' : 'text-slate-400 hover:text-white hover:bg-slate-750'
              }`}
            >
              <Key size={15} />
              <span>🔑 Applications & API Keys</span>
            </button>
            <button
              onClick={() => setActiveTab('Simulator')}
              className={`py-3.5 px-4 text-center transition-all flex items-center justify-center gap-2 min-w-[140px] flex-1 ${
                activeTab === 'Simulator' ? 'bg-gradient-to-r from-emerald-600 to-teal-600 text-white font-bold shadow-inner' : 'text-emerald-400 hover:text-emerald-300 hover:bg-slate-750 font-bold'
              }`}
            >
              <FlaskConical size={15} />
              <span>🧪 Try Demo Simulator</span>
            </button>
            <button
              onClick={() => setActiveTab('Capabilities')}
              className={`py-3.5 px-4 text-center transition-all flex items-center justify-center gap-2 min-w-[140px] flex-1 ${
                activeTab === 'Capabilities' ? 'bg-gradient-to-r from-blue-600 to-indigo-600 text-white' : 'text-amber-400 hover:text-white hover:bg-slate-750'
              }`}
            >
              <Sparkles size={15} className="text-amber-400" />
              <span>✨ Engine Capabilities</span>
            </button>
            <button
              onClick={() => setActiveTab('Comparison')}
              className={`py-3.5 px-4 text-center transition-all flex items-center justify-center gap-2 min-w-[140px] flex-1 ${
                activeTab === 'Comparison' ? 'bg-gradient-to-r from-indigo-600 to-purple-600 text-white' : 'text-indigo-400 hover:text-white hover:bg-slate-750'
              }`}
            >
              <Scale size={15} className="text-indigo-400" />
              <span>⚖️ vs Competitors</span>
            </button>
          </nav>
        </div>

        {/* Tab Contents */}
        <div className="p-6">
          {activeTab === 'Capabilities' && (
            <CapabilitiesShowcase />
          )}
          {activeTab === 'Comparison' && (
            <CompetitiveComparison />
          )}
          {activeTab === 'Instances' && (
            <div className="space-y-4">
              <div className="flex justify-between items-center text-xs text-slate-400">
                <span>Showing workflow executions owned by <strong>{session.tenantName}</strong></span>
                <button
                  onClick={() => setShowStartModal(true)}
                  className="px-3 py-1.5 bg-gradient-to-r from-emerald-600 to-teal-600 hover:from-emerald-500 hover:to-teal-500 text-white rounded-lg text-xs font-bold flex items-center gap-1.5 shadow-md shadow-emerald-500/20"
                >
                  <Sparkles size={13} /> Launch Demo Workflow
                </button>
              </div>
              <WorkflowInstanceTable items={instances} blueprints={blueprints} />
            </div>
          )}

          {activeTab === 'Application' && (
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
                setActiveTab('Simulator');
              }}
              onLaunch={() => setShowStartModal(true)}
            />
          )}

          {activeTab === 'Events' && (
            <EventAuditViewer role="Tenant" onInspectWorkflow={() => setActiveTab('Instances')} />
          )}

          {activeTab === 'Keys' && (
            <TenantApiKeyManager tenantId={session.tenantId} tenantName={session.tenantName} />
          )}

          {activeTab === 'Simulator' && (
            <ContextSimulationStudio
              role="Tenant"
              initialBindingId={simulationTarget.bindingId}
              initialRevision={simulationTarget.revision}
            />
          )}
        </div>
      </div>

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
                <label className="text-slate-300 font-bold block mb-2 text-sm text-emerald-400 flex items-center gap-1.5"><Sparkles size={14}/> Select Example Demo Workflow</label>
                <div className="grid grid-cols-1 gap-2 max-h-56 overflow-y-auto pr-1">
                  {[
                    {
                      name: 'OrderSagaFulfillment',
                      title: 'Order Saga Fulfillment',
                      badge: 'Distributed Sagas',
                      badgeColor: 'bg-amber-500/20 text-amber-300 border-amber-500/30',
                      desc: 'Stripe payment hold, warehouse inventory locking, and OnFailure rollback compensations.'
                    },
                    {
                      name: 'LoanUnderwritingFlow',
                      title: 'Loan Underwriting Flow',
                      badge: 'Decision Engine & HMAC',
                      badgeColor: 'bg-emerald-500/20 text-emerald-300 border-emerald-500/30',
                      desc: 'Credit score / DTI routing rules, 48h SLA underwriter tasks, and HMAC-SHA256 signed wire webhooks.'
                    },
                    {
                      name: 'SecOpsAccessGovernance',
                      title: 'SecOps Access Governance',
                      badge: 'Zero-Trust SLAs',
                      badgeColor: 'bg-indigo-500/20 text-indigo-300 border-indigo-500/30',
                      desc: 'Privileged access with 24h SLA auto-escalation to SecOps Director and 8h automated revocation timers.'
                    },
                    {
                      name: 'ExpenseApprovalV2',
                      title: 'Expense Approval V2',
                      badge: 'Multi-Tier Approval',
                      badgeColor: 'bg-blue-500/20 text-blue-300 border-blue-500/30',
                      desc: 'Multi-tier manager/director approval with role permissions and audit trail logging.'
                    }
                  ].map(preset => (
                    <div
                      key={preset.name}
                      onClick={() => setStartWorkflowName(preset.name)}
                      className={`p-2.5 rounded-xl border cursor-pointer transition-all ${
                        startWorkflowName === preset.name
                          ? 'bg-blue-900/30 border-blue-500 shadow-sm'
                          : 'bg-slate-950 border-slate-800 hover:border-slate-700'
                      }`}
                    >
                      <div className="flex items-center justify-between mb-1">
                        <span className="font-bold text-white text-xs">{preset.title}</span>
                        <span className={`text-[10px] px-2 py-0.5 rounded-full border font-semibold ${preset.badgeColor}`}>
                          {preset.badge}
                        </span>
                      </div>
                      <p className="text-slate-400 text-[11px] leading-relaxed">{preset.desc}</p>
                    </div>
                  ))}
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

    </div>
  );
};
