import { useEffect, useState } from 'react';
import { WorkflowTable } from './components/WorkflowTable';
import { DetailView } from './components/DetailView';
import { EditorView } from './components/EditorView';
import { WorkflowInstanceTable } from './components/WorkflowInstanceTable';
import { api } from './api/client';
import { WorkflowClass, WorkflowClassScope, WorkflowClassStatus, ValidationResult, CreateDraftRequest, WorkflowInstance } from './types';
import { AlertCircle, Plus } from 'lucide-react';

function App() {
  const [activeTab, setActiveTab] = useState<'Drafts' | 'Published' | 'Shared' | 'Public' | 'Instances'>('Drafts');
  const [items, setItems] = useState<WorkflowClass[]>([]);
  const [instances, setInstances] = useState<WorkflowInstance[]>([]);
  const [selectedItem, setSelectedItem] = useState<WorkflowClass | null>(null);
  const [editorItem, setEditorItem] = useState<WorkflowClass | null>(null); // For editing
  const [isCreating, setIsCreating] = useState(false); // For new creation
  const [validationResult, setValidationResult] = useState<ValidationResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [role, setRole] = useState<'Tenant' | 'Admin'>('Tenant'); // Simulation role

  const loadData = async () => {
    setLoading(true);
    setError(null);
    try {
      if (activeTab === 'Instances') {
          const data = await api.listInstances();
          setInstances(data);
      } else {
          let data: WorkflowClass[] = [];
          switch (activeTab) {
            case 'Drafts':
              data = await api.list(undefined, WorkflowClassStatus.Draft);
              break;
            case 'Published':
              data = await api.list(undefined, WorkflowClassStatus.Published);
              break;
            case 'Shared':
              data = await api.list(WorkflowClassScope.Shared, undefined);
              break;
            case 'Public':
              data = await api.list(WorkflowClassScope.Public, undefined);
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
  }, [activeTab]);

  const handleView = async (id: string) => {
    try {
        const item = await api.get(id);
        // If draft, open editor? No, user explicitly requested 'View'. 
        // We will add an 'Edit' button in the table for Drafts.
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
  }

  const handleSaveDraft = async (req: CreateDraftRequest) => {
      try {
          let savedItem: WorkflowClass;
          if (editorItem) {
              savedItem = await api.updateDraft(editorItem.id, req);
          } else {
              savedItem = await api.createDraft(req);
          }
          
          // Auto-validate on save
          const result = await api.validate(savedItem.id);
          setValidationResult(result);
          
          // If valid, close editor? No, prompt says "Validation Panel (Always Visible)". 
          // So we keep editor open if there are errors, or maybe always keep open until explicit close?
          // "Validation is blocking for Publish" - so for Draft save, we just show errors.
          
          // We need to update the editorItem to the saved one so subsequent saves are updates
          setEditorItem(savedItem);
          setIsCreating(false); // Switch to "Edit" mode
          
          await loadData();
      } catch (err: any) {
          setError(err.message);
          alert(`Save Failed: ${err.message}`);
      }
  }

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
    <div className="min-h-screen bg-gray-100 p-8">
      <header className="mb-8 flex justify-between items-center">
        <div>
            <h1 className="text-3xl font-bold text-gray-900">FlowOS {role} Dashboard</h1>
            <p className="text-gray-600 mt-2">Manage your WorkflowClass lifecycle and scope. Server enforces all rules.</p>
        </div>
        <div className="flex gap-4 items-center">
            <div className="bg-white rounded border p-1 flex text-sm">
                <button 
                    onClick={() => { setRole('Tenant'); setActiveTab('Drafts'); }} 
                    className={`px-3 py-1 rounded ${role === 'Tenant' ? 'bg-blue-100 text-blue-800 font-medium' : 'text-gray-600'}`}
                >Tenant</button>
                <button 
                    onClick={() => { setRole('Admin'); setActiveTab('Shared'); }} 
                    className={`px-3 py-1 rounded ${role === 'Admin' ? 'bg-purple-100 text-purple-800 font-medium' : 'text-gray-600'}`}
                >Admin</button>
            </div>
            {activeTab === 'Drafts' && role === 'Tenant' && (
                <button onClick={() => setIsCreating(true)} className="bg-blue-600 text-white px-4 py-2 rounded flex items-center gap-2 hover:bg-blue-700">
                    <Plus size={20} /> New Workflow
                </button>
            )}
        </div>
      </header>

      {error && (
        <div className="bg-red-100 border border-red-400 text-red-700 px-4 py-3 rounded relative mb-4 flex items-center">
            <AlertCircle className="mr-2" />
            <span>{error}</span>
            <button className="absolute top-0 bottom-0 right-0 px-4 py-3" onClick={() => setError(null)}>
                <span className="text-xl">&times;</span>
            </button>
        </div>
      )}

      <div className="bg-white rounded-lg shadow">
        <div className="border-b border-gray-200">
          <nav className="-mb-px flex">
            {['Drafts', 'Published', 'Shared', 'Public', 'Instances'].map((tab) => (
              <button
                key={tab}
                onClick={() => setActiveTab(tab as any)}
                className={`flex-1 py-4 px-1 text-center border-b-2 font-medium text-sm ${
                  activeTab === tab
                    ? 'border-blue-500 text-blue-600'
                    : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                }`}
              >
                {tab === 'Instances' ? 'Running Workflows' : tab}
              </button>
            ))}
          </nav>
        </div>

        <div className="p-6">
          {loading ? (
            <div className="text-center py-10 text-gray-500">Loading...</div>
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
                onPublish={(id) => handleAction(() => api.publish(id))}
                onSubmit={(id) => handleAction(() => api.submit(id))}
                onWithdraw={(id) => handleAction(() => api.withdraw(id))}
                onApprove={(id) => handleAction(() => api.approve(id))}
                onDeprecate={(id) => handleAction(() => api.deprecate(id))}
                onAbandon={(id) => handleAction(() => api.abandon(id))}
                onNewVersion={async (id) => {
                    await handleAction(async () => {
                        const newVersion = await api.newVersion(id);
                        setEditorItem(newVersion); // Open editor immediately
                        setActiveTab('Drafts');
                        return newVersion;
                    });
                }}
                onCopy={(id) => handleAction(() => api.copy(id, { newTenantId: '22222222-2222-2222-2222-222222222222' }))} // Hardcoded tenant for demo
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
            validation={validationResult} // Pass validation result
            onClose={() => { setEditorItem(null); setIsCreating(false); setValidationResult(null); }}
            onSave={handleSaveDraft}
          />
      )}
    </div>
  );
}

export default App;
