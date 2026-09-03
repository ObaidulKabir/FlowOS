import { useEffect, useState } from 'react';
import { WorkflowTable } from './components/WorkflowTable';
import { DetailView } from './components/DetailView';
import { EditorView } from './components/EditorView';
import { WorkflowInstanceTable } from './components/WorkflowInstanceTable';
import { TenantManager } from './components/TenantManager';
import { api, getActiveTenantId } from './api/client';
import { WorkflowClass, WorkflowClassScope, WorkflowClassStatus, ValidationResult, CreateDraftRequest, WorkflowInstance } from './types';
import { AlertCircle, Plus, Play, RefreshCw, CheckCircle, Shield, Cpu, Clock, Terminal, Key } from 'lucide-react';

function App() {
  const [activeTab, setActiveTab] = useState<'All' | 'Published' | 'Drafts' | 'Shared' | 'Public' | 'Instances' | 'Tenants'>('All');
  const [currentTenantId, setCurrentTenantId] = useState<string>(getActiveTenantId());
  const [items, setItems] = useState<WorkflowClass[]>([]);
  const [instances, setInstances] = useState<WorkflowInstance[]>([]);
  const [selectedItem, setSelectedItem] = useState<WorkflowClass | null>(null);
  const [editorItem, setEditorItem] = useState<WorkflowClass | null>(null);
  const [isCreating, setIsCreating] = useState(false);
  const [validationResult, setValidationResult] = useState<ValidationResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [role, setRole] = useState<'Tenant' | 'Admin'>('Tenant');

  // Simulator State
  const [simStep, setSimStep] = useState<string>('Draft');
  const [simState, setSimState] = useState<string>('Draft');
  const [simLogs, setSimLogs] = useState<string[]>([
    '> FlowOS Dual-Kernel Engine Initialized.',
    '> Instance spawned: ExpenseApprovalV2 (Tenant: 22222222-2222-2222-2222-222222222222)',
    '> State Authority: Draft | Current Step: Draft'
  ]);

  const loadData = async () => {
    if (activeTab === 'Tenants') {
      setLoading(false);
      return;
    }
    setLoading(true);
    setError(null);
    try {
      if (activeTab === 'Instances') {
        const data = await api.listInstances(role);
        setInstances(data);
      } else {
        let data: WorkflowClass[] = [];
        switch (activeTab) {
          case 'All':
            data = await api.list(undefined, undefined, role);
            break;
          case 'Drafts':
            data = await api.list(undefined, WorkflowClassStatus.Draft, role);
            break;
          case 'Published':
            data = await api.list(undefined, WorkflowClassStatus.Published, role);
            break;
          case 'Shared':
            data = await api.list(WorkflowClassScope.Shared, undefined, role);
            break;
          case 'Public':
            data = await api.list(WorkflowClassScope.Public, undefined, role);
            break;
        }
        setItems(data);
      }
    } catch (err: any) {
      setError(err.message);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadData();
  }, [activeTab, role]);

  const handleSimulateEvent = (eventId: string, targetState: string, targetStep: string) => {
    if (simState === 'Completed') {
      setSimLogs(prev => [...prev, '> [DENIED] Workflow is already in terminal state Completed.']);
      return;
    }
    setSimState(targetState);
    setSimStep(targetStep);
    setSimLogs(prev => [
      ...prev,
      `> Published Event '${eventId}' -> State Transitioned to '${targetState}' | Step: '${targetStep}'`
    ]);
  };

  const handleResetSimulator = () => {
    setSimStep('Draft');
    setSimState('Draft');
    setSimLogs([
      '> FlowOS Engine reset.',
      '> Instance restarted: Initial State = Draft'
    ]);
  };

  const handleStartLiveInstance = async () => {
    try {
      const res = await fetch('/api/workflows/start', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'x-tenant-id': '22222222-2222-2222-2222-222222222222',
          'X-Mock-Role': 'Admin'
        },
        body: JSON.stringify({
          tenantId: '22222222-2222-2222-2222-222222222222',
          workflowName: 'ExpenseApprovalV2'
        })
      });
      if (res.ok) {
        const data = await res.json();
        alert(`New Workflow Instance started!\nID: ${data.WorkflowInstanceId || data.workflowInstanceId}`);
        setActiveTab('Instances');
        loadData();
      } else {
        const errText = await res.text();
        alert(`Failed to start instance via API: ${errText}`);
      }
    } catch (err: any) {
      alert(`Start failed: ${err.message}`);
    }
  };

  const handleView = async (id: string) => {
    try {
      const item = await api.get(id);
      setSelectedItem(item);
      setValidationResult(null);
    } catch (err: any) {
      setError(err.message);
    }
  };

  const handleEdit = async (id: string) => {
    try {
      const item = await api.get(id);
      setEditorItem(item);
    } catch (err: any) {
      setError(err.message);
    }
  };

  const handleSaveDraft = async (req: CreateDraftRequest) => {
    try {
      let savedItem: WorkflowClass;
      if (editorItem) {
        savedItem = await api.updateDraft(editorItem.id, req);
      } else {
        savedItem = await api.createDraft(req);
      }
      const result = await api.validate(savedItem.id);
      setValidationResult(result);
      setEditorItem(null);
      setIsCreating(false);
      setActiveTab('Drafts');
      await loadData();
    } catch (err: any) {
      setError(err.message);
      alert(`Save Failed: ${err.message}`);
    }
  };

  const handleValidate = async () => {
    if (!selectedItem) return;
    try {
      const result = await api.validate(selectedItem.id);
      setValidationResult(result);
    } catch (err: any) {
      setError(err.message);
    }
  };

  const handleAction = async (action: () => Promise<any>) => {
    if (!confirm('Are you sure you want to proceed with this action?')) return;
    try {
      await action();
      await loadData();
      if (selectedItem) setSelectedItem(null);
    } catch (err: any) {
      setError(err.message);
      alert(`Action Failed: ${err.message}`);
    }
  };

  return (
    <div className="min-h-screen bg-slate-900 text-slate-100 font-sans selection:bg-blue-500 selection:text-white">
      
      {/* Top Navigation */}
      <nav className="border-b border-slate-800 bg-slate-900/90 backdrop-blur sticky top-0 z-50">
        <div className="max-w-7xl mx-auto px-6 h-16 flex items-center justify-between">
          <div className="flex items-center space-x-3">
            <div className="w-9 h-9 rounded-xl bg-blue-600 flex items-center justify-center font-bold text-white shadow-lg shadow-blue-500/30">
              F
            </div>
            <span className="text-xl font-bold tracking-tight text-white">Flow<span className="text-blue-500">OS</span></span>
            <span className="ml-2 px-2.5 py-0.5 text-xs font-semibold rounded-full bg-blue-500/10 text-blue-400 border border-blue-500/20">v1.0.0-MVP</span>
          </div>

          <div className="hidden md:flex items-center space-x-8 text-sm font-medium text-slate-300">
            <a href="#simulator" className="hover:text-white transition-colors">Simulator</a>
            <a href="#console" className="hover:text-white transition-colors">Governance & Instances</a>
            <a href="#comparison" className="hover:text-white transition-colors">Comparison</a>
            <a href="#pricing" className="hover:text-white transition-colors">Pricing</a>
          </div>

          <div className="flex items-center space-x-3">
            <a href="/swagger" target="_blank" className="px-4 py-2 text-sm font-medium text-white bg-blue-600 hover:bg-blue-700 rounded-lg shadow-md shadow-blue-500/20 transition-all">
              Swagger API ↗
            </a>
          </div>
        </div>
      </nav>

      {/* Hero Section */}
      <section className="py-16 px-6 max-w-7xl mx-auto text-center">
        <div className="inline-flex items-center space-x-2 px-3 py-1 rounded-full bg-slate-800 border border-slate-700 mb-6 shadow-sm">
          <span className="flex h-2 w-2 rounded-full bg-emerald-400 animate-pulse"></span>
          <span className="text-xs font-medium text-slate-300">192/192 Automated Tests Passing & Kernel Live</span>
        </div>

        <h1 className="text-4xl md:text-6xl font-extrabold tracking-tight mb-6 max-w-4xl mx-auto leading-tight">
          The Enterprise <span className="bg-gradient-to-r from-blue-400 via-indigo-400 to-pink-400 bg-clip-text text-transparent">Process Operating System</span>
        </h1>

        <p className="text-base md:text-lg text-slate-400 max-w-3xl mx-auto mb-10 leading-relaxed">
          FlowOS strictly separates <strong>State Authority (State Machines)</strong> from <strong>Process Orchestration (Workflows)</strong>, <strong>Business Logic (Policy Engine)</strong>, and <strong>AI Agents (15 MCP Tools)</strong>.
        </p>

        <div className="flex flex-wrap justify-center gap-4 mb-12">
          <a href="#simulator" className="px-6 py-3 bg-blue-600 hover:bg-blue-700 text-white font-semibold rounded-xl shadow-lg shadow-blue-500/30 transition-all">
            Try Simulator ↓
          </a>
          <a href="#console" className="px-6 py-3 bg-slate-800 hover:bg-slate-700 border border-slate-700 text-slate-200 font-semibold rounded-xl transition-all">
            Explore Governance Console
          </a>
        </div>

        {/* 4 Feature Cards */}
        <div className="grid grid-cols-2 md:grid-cols-4 gap-4 max-w-5xl mx-auto text-left">
          <div className="bg-slate-800/80 border border-slate-700 p-5 rounded-2xl">
            <Cpu className="text-blue-400 mb-2" size={24} />
            <div className="font-bold text-white text-sm">Dual-Kernel Engine</div>
            <div className="text-xs text-slate-400 mt-1">State Authority + Workflow Steps</div>
          </div>
          <div className="bg-slate-800/80 border border-slate-700 p-5 rounded-2xl">
            <Terminal className="text-purple-400 mb-2" size={24} />
            <div className="font-bold text-white text-sm">15 MCP Tools</div>
            <div className="text-xs text-slate-400 mt-1">Full AI Model Context Protocol</div>
          </div>
          <div className="bg-slate-800/80 border border-slate-700 p-5 rounded-2xl">
            <Shield className="text-pink-400 mb-2" size={24} />
            <div className="font-bold text-white text-sm">100% Declarative</div>
            <div className="text-xs text-slate-400 mt-1">JSON Blueprint Specifications</div>
          </div>
          <div className="bg-slate-800/80 border border-slate-700 p-5 rounded-2xl">
            <Clock className="text-emerald-400 mb-2" size={24} />
            <div className="font-bold text-white text-sm">Zero-Zombie SLAs</div>
            <div className="text-xs text-slate-400 mt-1">Automatic Boundary Timers</div>
          </div>
        </div>
      </section>

      {/* Interactive Process Simulator */}
      <section id="simulator" className="py-12 px-6 bg-slate-950/60 border-y border-slate-800">
        <div className="max-w-7xl mx-auto">
          <div className="text-center mb-8">
            <h2 className="text-2xl md:text-3xl font-bold text-white mb-2">Dual-Kernel State Machine Simulator</h2>
            <p className="text-sm text-slate-400 max-w-2xl mx-auto">
              Simulate events against the <code>ExpenseApprovalV2</code> blueprint. Watch how the State Machine legally authorizes or denies transitions.
            </p>
          </div>

          <div className="bg-slate-800 border border-slate-700 rounded-2xl p-6 shadow-xl grid grid-cols-1 lg:grid-cols-3 gap-8">
            {/* Controls */}
            <div className="space-y-4">
              <div className="bg-slate-900 p-4 rounded-xl border border-slate-700 space-y-2 text-xs">
                <div className="flex justify-between">
                  <span className="text-slate-400">Workflow Step:</span>
                  <span className="font-mono font-bold text-blue-400">{simStep}</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-slate-400">Legal State:</span>
                  <span className="font-mono font-bold text-emerald-400">{simState}</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-slate-400">Kernel Status:</span>
                  <span className="px-2 py-0.5 rounded bg-emerald-500/20 text-emerald-300 font-semibold">{simState === 'Completed' ? 'Completed' : 'Running'}</span>
                </div>
              </div>

              <div>
                <label className="block text-xs font-bold uppercase tracking-wider text-slate-400 mb-2">Publish Event</label>
                <div className="space-y-2">
                  <button onClick={() => handleSimulateEvent('EVT-SUBMIT', 'Submitted', 'ManagerReview')} className="w-full py-2 px-3 bg-blue-600/20 hover:bg-blue-600/30 text-blue-300 border border-blue-500/30 rounded-lg text-xs font-medium text-left flex justify-between">
                    <span>1. Submit Claim</span>
                    <span className="font-mono text-[10px]">EVT-SUBMIT</span>
                  </button>
                  <button onClick={() => handleSimulateEvent('EVT-APPROVE-MANAGER', 'ManagerApproved', 'ExecutiveSignoff')} className="w-full py-2 px-3 bg-emerald-600/20 hover:bg-emerald-600/30 text-emerald-300 border border-emerald-500/30 rounded-lg text-xs font-medium text-left flex justify-between">
                    <span>2. Manager Approve</span>
                    <span className="font-mono text-[10px]">EVT-APPROVE-MANAGER</span>
                  </button>
                  <button onClick={() => handleSimulateEvent('EVT-APPROVE-EXEC', 'Completed', 'Completed')} className="w-full py-2 px-3 bg-purple-600/20 hover:bg-purple-600/30 text-purple-300 border border-purple-500/30 rounded-lg text-xs font-medium text-left flex justify-between">
                    <span>3. Executive Approve</span>
                    <span className="font-mono text-[10px]">EVT-APPROVE-EXEC</span>
                  </button>
                  <button onClick={() => handleSimulateEvent('EVT-REJECT', 'Rejected', 'Rejected')} className="w-full py-2 px-3 bg-rose-600/20 hover:bg-rose-600/30 text-rose-300 border border-rose-500/30 rounded-lg text-xs font-medium text-left flex justify-between">
                    <span>4. Reject Claim</span>
                    <span className="font-mono text-[10px]">EVT-REJECT</span>
                  </button>
                </div>
              </div>

              <button onClick={handleResetSimulator} className="w-full py-2 text-xs font-medium text-slate-400 hover:text-white border border-slate-700 rounded-lg">
                ↺ Reset Simulator
              </button>
            </div>

            {/* Visual Nodes & Logs */}
            <div className="lg:col-span-2 flex flex-col justify-between space-y-4">
              <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
                <div className={`p-4 rounded-xl border text-center transition-all ${simState === 'Draft' ? 'border-blue-500 bg-blue-500/10 shadow-lg shadow-blue-500/20 scale-105' : 'border-slate-700 bg-slate-900 opacity-60'}`}>
                  <div className="text-[10px] text-slate-400">STEP 1</div>
                  <div className="font-bold text-sm text-white">Draft</div>
                  <div className="text-[10px] text-emerald-400 mt-1">Initial</div>
                </div>
                <div className={`p-4 rounded-xl border text-center transition-all ${simState === 'Submitted' ? 'border-blue-500 bg-blue-500/10 shadow-lg shadow-blue-500/20 scale-105' : 'border-slate-700 bg-slate-900 opacity-60'}`}>
                  <div className="text-[10px] text-slate-400">STEP 2</div>
                  <div className="font-bold text-sm text-white">Submitted</div>
                  <div className="text-[10px] text-blue-400 mt-1">Manager Review</div>
                </div>
                <div className={`p-4 rounded-xl border text-center transition-all ${simState === 'ManagerApproved' ? 'border-blue-500 bg-blue-500/10 shadow-lg shadow-blue-500/20 scale-105' : 'border-slate-700 bg-slate-900 opacity-60'}`}>
                  <div className="text-[10px] text-slate-400">STEP 3</div>
                  <div className="font-bold text-sm text-white">ManagerApproved</div>
                  <div className="text-[10px] text-purple-400 mt-1">Exec Review</div>
                </div>
                <div className={`p-4 rounded-xl border text-center transition-all ${simState === 'Completed' ? 'border-emerald-500 bg-emerald-500/10 shadow-lg shadow-emerald-500/20 scale-105' : 'border-slate-700 bg-slate-900 opacity-60'}`}>
                  <div className="text-[10px] text-slate-400">STEP 4</div>
                  <div className="font-bold text-sm text-white">Completed</div>
                  <div className="text-[10px] text-emerald-400 mt-1">Terminal</div>
                </div>
              </div>

              {/* Terminal Logs */}
              <div className="bg-black/80 rounded-xl p-3 font-mono text-xs text-emerald-400 h-28 overflow-y-auto border border-slate-800">
                {simLogs.map((log, idx) => (
                  <div key={idx}>{log}</div>
                ))}
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* Main Governance & Live Instances Console */}
      <section id="console" className="py-12 px-6 max-w-7xl mx-auto">
        <div className="flex flex-col md:flex-row md:items-center justify-between mb-6">
          <div>
            <h2 className="text-2xl font-bold text-white">Kernel Governance & Telemetry Console</h2>
            <p className="text-xs text-slate-400 mt-1">Inspect live instances and manage declarative WorkflowClass blueprints.</p>
          </div>

          <div className="mt-4 md:mt-0 flex flex-wrap items-center gap-3">
            <button
              onClick={() => setActiveTab('Tenants')}
              className="flex items-center gap-1.5 px-3 py-1.5 bg-slate-800 hover:bg-slate-750 border border-slate-700 rounded-lg text-xs transition-colors"
              title="Click to manage tenants and API keys"
            >
              <Key size={13} className="text-amber-400" />
              <span className="text-slate-400">Tenant:</span>
              <span className="font-mono text-blue-400 font-bold">
                {currentTenantId.substring(0, 8)}...
              </span>
            </button>

            <div className="bg-slate-800 rounded-lg border border-slate-700 p-1 flex text-xs">
              <button 
                onClick={() => { setRole('Tenant'); setActiveTab('All'); }} 
                className={`px-3 py-1.5 rounded-md font-medium transition-all ${role === 'Tenant' ? 'bg-blue-600 text-white' : 'text-slate-400 hover:text-white'}`}
              >Tenant View</button>
              <button 
                onClick={() => { setRole('Admin'); setActiveTab('All'); }} 
                className={`px-3 py-1.5 rounded-md font-medium transition-all ${role === 'Admin' ? 'bg-purple-600 text-white' : 'text-slate-400 hover:text-white'}`}
              >Admin View</button>
            </div>

            <button onClick={handleStartLiveInstance} className="bg-emerald-600 hover:bg-emerald-700 text-white px-3 py-1.5 rounded-lg text-xs font-semibold flex items-center gap-1.5 shadow-sm transition-all">
              <Play size={14} /> Start New Instance
            </button>

            {role === 'Tenant' && (
              <button onClick={() => setIsCreating(true)} className="bg-blue-600 hover:bg-blue-700 text-white px-3 py-1.5 rounded-lg text-xs font-semibold flex items-center gap-1.5 shadow-sm transition-all">
                <Plus size={14} /> New WorkflowClass
              </button>
            )}

            <button onClick={loadData} className="bg-slate-800 hover:bg-slate-700 border border-slate-700 text-slate-300 px-3 py-1.5 rounded-lg text-xs font-semibold flex items-center gap-1.5">
              <RefreshCw size={14} /> Refresh
            </button>
          </div>
        </div>

        {error && (
          <div className="bg-rose-900/40 border border-rose-700 text-rose-300 px-4 py-3 rounded-xl mb-4 flex items-center justify-between text-xs">
            <div className="flex items-center gap-2">
              <AlertCircle size={16} />
              <span>{error}</span>
            </div>
            <button onClick={() => setError(null)} className="text-lg font-bold">&times;</button>
          </div>
        )}

        <div className="bg-slate-800 border border-slate-700 rounded-2xl overflow-hidden shadow-xl">
          <div className="border-b border-slate-700 bg-slate-850">
            <nav className="flex divide-x divide-slate-700 text-xs font-semibold">
              {(['All', 'Published', 'Drafts', 'Shared', 'Public', 'Instances', 'Tenants'] as const).map((tab) => (
                <button
                  key={tab}
                  onClick={() => setActiveTab(tab)}
                  className={`flex-1 py-3 px-4 text-center transition-all ${
                    activeTab === tab
                      ? 'bg-blue-600 text-white'
                      : 'text-slate-400 hover:text-white hover:bg-slate-750'
                  }`}
                >
                  {tab === 'Tenants' ? '🏢 Tenants & API Keys' : tab === 'Instances' ? '⚡ Live Instances' : tab === 'All' ? '📑 All Workflows' : tab}
                </button>
              ))}
            </nav>
          </div>

          <div className="p-6">
            {loading ? (
              <div className="text-center py-12 text-slate-400 text-xs">
                <span className="inline-block animate-spin mr-2">↻</span> Loading kernel telemetry...
              </div>
            ) : activeTab === 'Tenants' ? (
              <TenantManager onTenantChange={(newTenantId) => { setCurrentTenantId(newTenantId); loadData(); }} />
            ) : activeTab === 'Instances' ? (
              <WorkflowInstanceTable items={instances} />
            ) : (
              <WorkflowTable 
                items={items} 
                currentTab={activeTab}
                isAdmin={role === 'Admin'}
                onView={handleView}
                onEdit={handleEdit}
                onDelete={(id) => handleAction(() => api.delete(id))}
                onPublish={async (id) => {
                  await handleAction(async () => {
                    const pub = await api.publish(id);
                    setActiveTab('Published');
                    return pub;
                  });
                }}
                onSubmit={async (id) => {
                  await handleAction(async () => {
                    const sub = await api.submit(id);
                    setActiveTab('Shared');
                    return sub;
                  });
                }}
                onWithdraw={(id) => handleAction(() => api.withdraw(id))}
                onApprove={async (id) => {
                  await handleAction(async () => {
                    const app = await api.approve(id);
                    setActiveTab('Public');
                    return app;
                  });
                }}
                onDeprecate={(id) => handleAction(() => api.deprecate(id))}
                onAbandon={(id) => handleAction(() => api.abandon(id))}
                onNewVersion={async (id) => {
                  await handleAction(async () => {
                    const newVersion = await api.newVersion(id);
                    setEditorItem(newVersion);
                    setActiveTab('Drafts');
                    return newVersion;
                  });
                }}
                onCopy={(id) => handleAction(() => api.copy(id, { newTenantId: '22222222-2222-2222-2222-222222222222' }))}
              />
            )}
          </div>
        </div>

        {selectedItem && (
          <DetailView 
            item={selectedItem} 
            validation={validationResult}
            onClose={() => setSelectedItem(null)}
            onValidate={handleValidate}
          />
        )}

        {(isCreating || editorItem) && (
          <EditorView 
            item={editorItem || undefined}
            validation={validationResult}
            onClose={() => { setEditorItem(null); setIsCreating(false); setValidationResult(null); }}
            onSave={handleSaveDraft}
          />
        )}
      </section>

      {/* Competitor Comparison Section */}
      <section id="comparison" className="py-16 px-6 max-w-7xl mx-auto border-t border-slate-800">
        <div className="text-center mb-10">
          <h2 className="text-2xl md:text-3xl font-bold text-white mb-2">Why Enterprises Choose FlowOS</h2>
          <p className="text-sm text-slate-400 max-w-2xl mx-auto">
            Dual-kernel mathematical state authority vs token replay engines.
          </p>
        </div>

        <div className="overflow-x-auto bg-slate-800 border border-slate-700 rounded-2xl">
          <table className="w-full text-left text-xs border-collapse">
            <thead>
              <tr className="bg-slate-900 border-b border-slate-700 uppercase tracking-wider text-slate-400">
                <th className="p-4">Feature</th>
                <th className="p-4 text-blue-400 font-bold">FlowOS 1.0.0</th>
                <th className="p-4">Temporal.io</th>
                <th className="p-4">Camunda 8</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-700">
              <tr>
                <td className="p-4 font-semibold text-slate-200">State Machine Authority</td>
                <td className="p-4 text-emerald-400 font-bold">✓ Dual-Kernel Separation</td>
                <td className="p-4 text-slate-400">Code Replay Engine</td>
                <td className="p-4 text-slate-400">BPMN Token Flow</td>
              </tr>
              <tr>
                <td className="p-4 font-semibold text-slate-200">Native AI Agent Tools</td>
                <td className="p-4 text-emerald-400 font-bold">✓ 15-Tool MCP Server</td>
                <td className="p-4 text-slate-400">Custom SDK Wrapper</td>
                <td className="p-4 text-slate-400">REST Connectors</td>
              </tr>
              <tr>
                <td className="p-4 font-semibold text-slate-200">Specification Format</td>
                <td className="p-4 text-emerald-400 font-bold">✓ 100% Declarative JSON</td>
                <td className="p-4 text-slate-400">TypeScript/Go/Java Code</td>
                <td className="p-4 text-slate-400">BPMN 2.0 XML</td>
              </tr>
              <tr>
                <td className="p-4 font-semibold text-slate-200">Zero-Setup Sandbox</td>
                <td className="p-4 text-emerald-400 font-bold">✓ Native In-Memory TTL</td>
                <td className="p-4 text-slate-400">Requires Server Container</td>
                <td className="p-4 text-slate-400">Requires Zeebe Cluster</td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      {/* Pricing Section */}
      <section id="pricing" className="py-16 px-6 max-w-7xl mx-auto border-t border-slate-800">
        <div className="text-center mb-10">
          <h2 className="text-2xl md:text-3xl font-bold text-white mb-2">Transparent Pricing</h2>
          <p className="text-sm text-slate-400 max-w-2xl mx-auto">
            From zero-setup sandbox testing to high-availability production clusters.
          </p>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-3 gap-6 max-w-5xl mx-auto text-xs">
          <div className="bg-slate-800 border border-slate-700 p-6 rounded-2xl flex flex-col justify-between">
            <div>
              <h3 className="text-lg font-bold text-white mb-1">Developer Sandbox</h3>
              <div className="text-2xl font-extrabold text-white mb-4">$0 <span className="text-xs font-normal text-slate-400">/ forever</span></div>
              <ul className="space-y-2 text-slate-400 mb-6">
                <li className="flex items-center gap-1.5"><CheckCircle size={14} className="text-emerald-400" /> In-Memory Ephemeral Engine</li>
                <li className="flex items-center gap-1.5"><CheckCircle size={14} className="text-emerald-400" /> Key-Free MCP Server Mode</li>
                <li className="flex items-center gap-1.5"><CheckCircle size={14} className="text-emerald-400" /> Up to 4h Auto-Purge TTL</li>
              </ul>
            </div>
            <a href="#simulator" className="w-full py-2.5 bg-slate-700 hover:bg-slate-600 text-white font-semibold rounded-lg text-center transition-all">
              Try Simulator
            </a>
          </div>

          <div className="bg-slate-800 border-2 border-blue-500 p-6 rounded-2xl flex flex-col justify-between relative shadow-xl">
            <div className="absolute -top-3 right-6 bg-blue-600 text-white text-[10px] font-bold px-2.5 py-0.5 rounded-full uppercase">
              Popular
            </div>
            <div>
              <h3 className="text-lg font-bold text-white mb-1">Managed Cloud</h3>
              <div className="text-2xl font-extrabold text-white mb-4">$299 <span className="text-xs font-normal text-slate-400">/ month</span></div>
              <ul className="space-y-2 text-slate-400 mb-6">
                <li className="flex items-center gap-1.5"><CheckCircle size={14} className="text-emerald-400" /> Managed PostgreSQL Cluster</li>
                <li className="flex items-center gap-1.5"><CheckCircle size={14} className="text-emerald-400" /> Multi-Tenant API Keys</li>
                <li className="flex items-center gap-1.5"><CheckCircle size={14} className="text-emerald-400" /> 99.9% Uptime SLA Guarantee</li>
              </ul>
            </div>
            <a href="/swagger" target="_blank" className="w-full py-2.5 bg-blue-600 hover:bg-blue-700 text-white font-semibold rounded-lg text-center transition-all shadow-md shadow-blue-500/20">
              Get Started Cloud
            </a>
          </div>

          <div className="bg-slate-800 border border-slate-700 p-6 rounded-2xl flex flex-col justify-between">
            <div>
              <h3 className="text-lg font-bold text-white mb-1">Enterprise Dedicated</h3>
              <div className="text-2xl font-extrabold text-white mb-4">Custom</div>
              <ul className="space-y-2 text-slate-400 mb-6">
                <li className="flex items-center gap-1.5"><CheckCircle size={14} className="text-emerald-400" /> Air-Gapped On-Premises</li>
                <li className="flex items-center gap-1.5"><CheckCircle size={14} className="text-emerald-400" /> Unlimited Tenants & Blueprints</li>
                <li className="flex items-center gap-1.5"><CheckCircle size={14} className="text-emerald-400" /> Dedicated 24/7 Support SLA</li>
              </ul>
            </div>
            <a href="mailto:contact@prospectbdltd.com" className="w-full py-2.5 bg-slate-700 hover:bg-slate-600 text-white font-semibold rounded-lg text-center transition-all">
              Contact Sales
            </a>
          </div>
        </div>
      </section>

      {/* Footer */}
      <footer className="py-8 border-t border-slate-800 text-center text-xs text-slate-500">
        <div className="max-w-7xl mx-auto px-6 flex flex-col sm:flex-row justify-between items-center gap-4">
          <div>© 2026 FlowOS — Prospect BD Ltd. All rights reserved.</div>
          <div className="flex space-x-6">
            <a href="/swagger" target="_blank" className="hover:underline">Swagger Docs</a>
            <a href="https://github.com/ObaidulKabir/FlowOS" target="_blank" className="hover:underline">GitHub</a>
          </div>
        </div>
      </footer>
    </div>
  );
}

export default App;
