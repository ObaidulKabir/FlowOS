import React, { useEffect, useState } from 'react';
import { api } from '../api/client';
import { CreateKeyResponse, TenantApplicationDto, WorkflowClass } from '../types';
import {
  AppWindow, Plus, RefreshCw, Trash2, Pencil, AlertCircle, X,
  ChevronRight, ChevronDown, Workflow, Key, Check, Copy, ShieldCheck, LockKeyhole, ArrowRight
} from 'lucide-react';
import { ApiKeyScopePicker } from './ApiKeyScopePicker';
import { API_KEY_SCOPE_PRESETS } from '../lib/apiKeyScopes';

interface Props {
  tenantId: string;
  tenantName: string;
  /** The tenant's workflows, already loaded by the parent workspace (api.list()). */
  blueprints: WorkflowClass[];
  /** Re-fetches blueprints after an assign/unassign so WorkflowClass.applicationId stays in sync elsewhere. */
  onBlueprintsChanged?: () => void | Promise<void>;
  /** Applications (4/4): opens the scoped ApplicationDetailView (Workflows/AI Context/API Keys) for this row. */
  onOpenApplication?: (app: TenantApplicationDto) => void;
}

/**
 * Applications (1/4-4/4): manage the tenant's TenantApplication rows (ApplicationsController), assign
 * workflows to one (WorkflowClassesController's POST {id}/application), and issue application-scoped
 * API keys (TenantsController's POST /api/tenants/{id}/keys with applicationId) - see
 * docs/14-api-reference.md sections 8, 11 and 12. There was previously no dashboard UI for any of this;
 * it is REST-only and, per FlowOsMcpGuidance.cs, also has no MCP tool.
 */
export const TenantApplicationManager: React.FC<Props> = ({ tenantId, tenantName, blueprints, onBlueprintsChanged, onOpenApplication }) => {
  const [applications, setApplications] = useState<TenantApplicationDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [assignTargetByApp, setAssignTargetByApp] = useState<Record<string, string>>({});
  const [assigning, setAssigning] = useState<string | null>(null);

  // Create / edit modal
  const [showFormModal, setShowFormModal] = useState(false);
  const [editingApp, setEditingApp] = useState<TenantApplicationDto | null>(null);
  const [formName, setFormName] = useState('');
  const [formDescription, setFormDescription] = useState('');
  const [formSubmitting, setFormSubmitting] = useState(false);

  // Application-scoped key modal
  const [keyModalAppId, setKeyModalAppId] = useState<string | null>(null);
  const [keyName, setKeyName] = useState('App Key');
  const [keyEnvironment, setKeyEnvironment] = useState('Production');
  const [keyScopes, setKeyScopes] = useState<string[]>([...API_KEY_SCOPE_PRESETS.operator]);
  const [keyExpiresInDays, setKeyExpiresInDays] = useState(0);
  const [keySubmitting, setKeySubmitting] = useState(false);
  const [createdKey, setCreatedKey] = useState<CreateKeyResponse | null>(null);
  const [copiedId, setCopiedId] = useState<string | null>(null);

  const loadApplications = async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await api.listApplications();
      setApplications(data);
    } catch (err: any) {
      setError(err.message || 'Failed to load applications');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadApplications();
  }, [tenantId]);

  const workflowsFor = (applicationId: string) => blueprints.filter(bp => bp.applicationId === applicationId);
  const unassignedOrOtherWorkflows = (applicationId: string) =>
    blueprints.filter(bp => bp.applicationId !== applicationId);

  const openCreateModal = () => {
    setEditingApp(null);
    setFormName('');
    setFormDescription('');
    setShowFormModal(true);
  };

  const openEditModal = (app: TenantApplicationDto) => {
    setEditingApp(app);
    setFormName(app.name);
    setFormDescription(app.description || '');
    setShowFormModal(true);
  };

  const handleSaveApplication = async (e: React.FormEvent) => {
    e.preventDefault();
    setFormSubmitting(true);
    try {
      if (editingApp) {
        await api.updateApplication(editingApp.id, { name: formName, description: formDescription || undefined });
      } else {
        await api.createApplication({ name: formName, description: formDescription || undefined });
      }
      setShowFormModal(false);
      await loadApplications();
      // Keeps the parent's own applications list (used by ApplicationWorkspace's filter) in sync too.
      if (onBlueprintsChanged) await onBlueprintsChanged();
    } catch (err: any) {
      alert(`Failed to save application: ${err.message}`);
    } finally {
      setFormSubmitting(false);
    }
  };

  const handleDelete = async (app: TenantApplicationDto) => {
    if (!confirm(
      `Delete "${app.name}"? This only works while it has no active key, workflow, or agent prompt/profile binding - ` +
      `revoke keys, move or unassign workflows, and delete bindings first.`
    )) {
      return;
    }
    try {
      await api.deleteApplication(app.id);
      await loadApplications();
      if (onBlueprintsChanged) await onBlueprintsChanged();
    } catch (err: any) {
      alert(err.message);
    }
  };

  const handleAssign = async (applicationId: string) => {
    const workflowClassId = assignTargetByApp[applicationId];
    if (!workflowClassId) return;
    setAssigning(workflowClassId);
    try {
      await api.assignWorkflowApplication(workflowClassId, applicationId);
      setAssignTargetByApp(prev => ({ ...prev, [applicationId]: '' }));
      await loadApplications();
      if (onBlueprintsChanged) await onBlueprintsChanged();
    } catch (err: any) {
      alert(`Failed to assign workflow: ${err.message}`);
    } finally {
      setAssigning(null);
    }
  };

  const handleUnassign = async (workflowClassId: string) => {
    setAssigning(workflowClassId);
    try {
      await api.assignWorkflowApplication(workflowClassId, null);
      await loadApplications();
      if (onBlueprintsChanged) await onBlueprintsChanged();
    } catch (err: any) {
      alert(`Failed to unassign workflow: ${err.message}`);
    } finally {
      setAssigning(null);
    }
  };

  const openKeyModal = (appId: string) => {
    setKeyModalAppId(appId);
    setKeyName('App Key');
    setKeyEnvironment('Production');
    setKeyScopes([...API_KEY_SCOPE_PRESETS.operator]);
    setKeyExpiresInDays(0);
    setCreatedKey(null);
  };

  const handleGenerateKey = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!keyModalAppId) return;
    setKeySubmitting(true);
    try {
      const res = await api.generateTenantKey(
        tenantId,
        keyName,
        undefined,
        keyEnvironment,
        keyScopes,
        keyExpiresInDays > 0 ? keyExpiresInDays : undefined,
        keyModalAppId
      );
      if (!res.apiKey || res.apiKey === res.maskedKey || res.apiKey.includes('•')) {
        throw new Error('The API did not return the one-time full key. Revoke this key and generate a replacement.');
      }
      setCreatedKey(res);
      await loadApplications();
    } catch (err: any) {
      alert(String(err?.message || '').includes('HTTP 403')
        ? 'Only tenant Admins can generate API keys for this tenant.'
        : `Failed to generate application key: ${err.message}`);
    } finally {
      setKeySubmitting(false);
    }
  };

  const handleCopy = async (text: string, id: string) => {
    try {
      await navigator.clipboard.writeText(text);
      setCopiedId(id);
      setTimeout(() => setCopiedId(null), 2000);
    } catch {
      setError('Unable to access the clipboard. Select and copy the one-time key manually.');
    }
  };

  const keyModalApp = applications.find(a => a.id === keyModalAppId) || null;

  return (
    <div className="space-y-6">
      <div className="bg-slate-850 p-6 rounded-2xl border border-slate-700/80 flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <div className="flex items-center gap-2 mb-1">
            <AppWindow className="text-blue-400" size={20} />
            <h3 className="text-lg font-bold text-white">Applications</h3>
          </div>
          <p className="text-xs text-slate-400">
            Group <strong>{tenantName}</strong>&apos;s workflows and FlowOS API keys by the application that uses them
            (your CRM, a customer portal, an internal tool). A key issued for an application is confined to that
            application&apos;s workflows.
          </p>
        </div>
        <div className="flex items-center gap-2.5">
          <button
            onClick={loadApplications}
            className="p-2 bg-slate-800 hover:bg-slate-750 text-slate-300 border border-slate-700 rounded-xl transition-colors"
            title="Refresh applications"
          >
            <RefreshCw size={14} className={loading ? 'animate-spin text-blue-400' : ''} />
          </button>
          <button
            onClick={openCreateModal}
            className="px-4 py-2 bg-blue-600 hover:bg-blue-500 text-white font-semibold rounded-xl text-xs flex items-center gap-2 shadow-lg shadow-blue-500/25 transition-all"
          >
            <Plus size={15} />
            <span>New Application</span>
          </button>
        </div>
      </div>

      {error && (
        <div className="p-4 bg-rose-900/30 border border-rose-700 rounded-xl text-xs text-rose-300 flex items-center gap-2">
          <AlertCircle size={16} />
          <span>{error}</span>
        </div>
      )}

      <div className="bg-slate-800 border border-slate-700 rounded-2xl overflow-hidden shadow-xl">
        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs text-slate-300">
            <thead className="bg-slate-900 text-slate-400 uppercase text-[10px] tracking-wider border-b border-slate-700">
              <tr>
                <th className="py-3.5 px-4 w-8"></th>
                <th className="py-3.5 px-4">Application</th>
                <th className="py-3.5 px-4">Workflows</th>
                <th className="py-3.5 px-4">Active Keys</th>
                <th className="py-3.5 px-4">Created</th>
                <th className="py-3.5 px-4 text-right">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-700/60">
              {applications.map(app => {
                const isExpanded = expandedId === app.id;
                const ownWorkflows = workflowsFor(app.id);
                const assignable = unassignedOrOtherWorkflows(app.id);
                return (
                  <React.Fragment key={app.id}>
                    <tr className="hover:bg-slate-750 transition-colors">
                      <td className="py-3.5 px-4">
                        <button
                          onClick={() => setExpandedId(isExpanded ? null : app.id)}
                          className="text-slate-400 hover:text-white"
                          title={isExpanded ? 'Collapse' : 'Expand'}
                        >
                          {isExpanded ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
                        </button>
                      </td>
                      <td className="py-3.5 px-4">
                        {onOpenApplication ? (
                          <button
                            onClick={() => onOpenApplication(app)}
                            className="font-bold text-white text-xs hover:text-blue-300 hover:underline text-left"
                          >
                            {app.name}
                          </button>
                        ) : (
                          <div className="font-bold text-white text-xs">{app.name}</div>
                        )}
                        {app.description && <div className="text-[11px] text-slate-400">{app.description}</div>}
                      </td>
                      <td className="py-3.5 px-4 font-mono">{app.workflowCount}</td>
                      <td className="py-3.5 px-4 font-mono">{app.activeKeyCount}</td>
                      <td className="py-3.5 px-4 text-slate-400 text-[11px]">
                        {new Date(app.createdAtUtc).toLocaleDateString()}
                      </td>
                      <td className="py-3.5 px-4 text-right">
                        <div className="flex items-center justify-end gap-1.5">
                          {onOpenApplication && (
                            <button
                              onClick={() => onOpenApplication(app)}
                              className="px-2.5 py-1.5 bg-blue-600 hover:bg-blue-500 text-white rounded-lg text-[11px] font-semibold flex items-center gap-1"
                              title="Open workflows, AI context and keys for this application"
                            >
                              Open <ArrowRight size={12} />
                            </button>
                          )}
                          <button
                            onClick={() => openKeyModal(app.id)}
                            className="p-1.5 text-amber-400 hover:text-amber-300 hover:bg-amber-500/10 rounded-lg transition-colors"
                            title="Generate an application-scoped key"
                          >
                            <Key size={14} />
                          </button>
                          <button
                            onClick={() => openEditModal(app)}
                            className="p-1.5 text-slate-300 hover:text-white hover:bg-slate-700/60 rounded-lg transition-colors"
                            title="Rename / edit"
                          >
                            <Pencil size={14} />
                          </button>
                          <button
                            onClick={() => handleDelete(app)}
                            className="p-1.5 text-rose-400 hover:text-rose-300 hover:bg-rose-500/10 rounded-lg transition-colors"
                            title="Delete application"
                          >
                            <Trash2 size={14} />
                          </button>
                        </div>
                      </td>
                    </tr>

                    {isExpanded && (
                      <tr>
                        <td colSpan={6} className="bg-slate-900/60 px-4 py-4">
                          <div className="space-y-3">
                            <div className="flex items-center gap-1.5 text-[11px] font-semibold text-slate-300">
                              <Workflow size={13} className="text-blue-400" />
                              <span>Workflows in this application</span>
                            </div>
                            {ownWorkflows.length === 0 ? (
                              <p className="text-[11px] text-slate-500">No workflows assigned yet.</p>
                            ) : (
                              <ul className="space-y-1">
                                {ownWorkflows.map(wf => (
                                  <li key={wf.id} className="flex items-center justify-between bg-slate-950 border border-slate-800 rounded-lg px-3 py-1.5">
                                    <span className="font-mono text-[11px] text-slate-300">{wf.name} <span className="text-slate-500">v{wf.version}</span></span>
                                    <button
                                      onClick={() => handleUnassign(wf.id)}
                                      disabled={assigning === wf.id}
                                      className="text-[10px] text-slate-400 hover:text-rose-300"
                                    >
                                      {assigning === wf.id ? 'Unassigning…' : 'Unassign'}
                                    </button>
                                  </li>
                                ))}
                              </ul>
                            )}

                            {assignable.length > 0 && (
                              <div className="flex items-center gap-2 pt-1">
                                <select
                                  value={assignTargetByApp[app.id] || ''}
                                  onChange={e => setAssignTargetByApp(prev => ({ ...prev, [app.id]: e.target.value }))}
                                  className="flex-1 bg-slate-950 border border-slate-700 rounded-lg px-2.5 py-1.5 text-[11px] text-white focus:outline-none focus:border-blue-500"
                                >
                                  <option value="">Assign an existing workflow…</option>
                                  {assignable.map(wf => (
                                    <option key={wf.id} value={wf.id}>
                                      {wf.name} v{wf.version}{wf.applicationId ? ' (moves it here)' : ' (unassigned)'}
                                    </option>
                                  ))}
                                </select>
                                <button
                                  onClick={() => handleAssign(app.id)}
                                  disabled={!assignTargetByApp[app.id] || assigning === assignTargetByApp[app.id]}
                                  className="px-3 py-1.5 bg-blue-600 hover:bg-blue-500 disabled:opacity-50 disabled:cursor-not-allowed text-white rounded-lg text-[11px] font-semibold"
                                >
                                  Assign
                                </button>
                              </div>
                            )}
                          </div>
                        </td>
                      </tr>
                    )}
                  </React.Fragment>
                );
              })}

              {applications.length === 0 && !loading && (
                <tr>
                  <td colSpan={6} className="py-12 text-center text-slate-500 text-xs">
                    No applications yet. Click "New Application" to group a set of workflows and keys together.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      {/* Create / Edit modal */}
      {showFormModal && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-slate-900 border border-slate-700 rounded-2xl max-w-md w-full p-6 shadow-2xl space-y-4">
            <div className="flex items-center justify-between border-b border-slate-800 pb-3">
              <h3 className="text-base font-bold text-white flex items-center gap-2">
                <AppWindow className="text-blue-400" size={18} />
                {editingApp ? 'Edit Application' : 'New Application'}
              </h3>
              <button onClick={() => setShowFormModal(false)} className="text-slate-400 hover:text-white">
                <X size={18} />
              </button>
            </div>
            <form onSubmit={handleSaveApplication} className="space-y-4 text-xs">
              <div className="space-y-1">
                <label className="text-slate-300 font-medium">Name</label>
                <input
                  type="text"
                  required
                  maxLength={150}
                  placeholder="e.g. Customer Portal"
                  value={formName}
                  onChange={e => setFormName(e.target.value)}
                  className="w-full bg-slate-950 border border-slate-700 rounded-xl px-3 py-2 text-white focus:outline-none focus:border-blue-500"
                />
              </div>
              <div className="space-y-1">
                <label className="text-slate-300 font-medium">Description (optional)</label>
                <input
                  type="text"
                  placeholder="e.g. Self-serve portal for loan applicants"
                  value={formDescription}
                  onChange={e => setFormDescription(e.target.value)}
                  className="w-full bg-slate-950 border border-slate-700 rounded-xl px-3 py-2 text-white focus:outline-none focus:border-blue-500"
                />
              </div>
              <div className="pt-2 flex justify-end gap-2 border-t border-slate-800">
                <button
                  type="button"
                  onClick={() => setShowFormModal(false)}
                  className="px-4 py-2 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded-xl font-semibold"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={formSubmitting}
                  className="px-4 py-2 bg-blue-600 hover:bg-blue-500 text-white rounded-xl font-semibold"
                >
                  {formSubmitting ? 'Saving...' : editingApp ? 'Save Changes' : 'Create Application'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Application-scoped key modal */}
      {keyModalAppId && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-slate-900 border border-slate-700 rounded-2xl max-w-md w-full p-6 shadow-2xl space-y-4 max-h-[90vh] overflow-y-auto">
            <div className="flex items-center justify-between border-b border-slate-800 pb-3">
              <h3 className="text-base font-bold text-white flex items-center gap-2">
                <Key className="text-amber-400" size={18} />
                Key for {keyModalApp?.name || 'application'}
              </h3>
              <button onClick={() => setKeyModalAppId(null)} className="text-slate-400 hover:text-white">
                <X size={18} />
              </button>
            </div>

            {createdKey ? (
              <div className="space-y-3">
                <div className="flex items-center gap-2 text-emerald-300 font-bold text-xs">
                  <ShieldCheck size={16} className="text-emerald-400" />
                  <span>Key generated - confined to this application</span>
                </div>
                <p className="text-[11px] text-slate-300">
                  Copy it now; FlowOS stores only its hash and cannot show it again.
                </p>
                <div className="p-3 bg-slate-950 border border-emerald-500/30 rounded-xl flex items-center justify-between font-mono text-[11px] text-emerald-300">
                  <span className="break-all select-all font-bold">{createdKey.apiKey}</span>
                  <button
                    onClick={() => handleCopy(createdKey.apiKey, 'app-created-key')}
                    className="ml-3 px-3 py-1.5 bg-emerald-600 hover:bg-emerald-500 text-white rounded-lg flex items-center gap-1.5 shrink-0 transition-colors"
                  >
                    {copiedId === 'app-created-key' ? <Check size={13} /> : <Copy size={13} />}
                    <span>{copiedId === 'app-created-key' ? 'Copied' : 'Copy Key'}</span>
                  </button>
                </div>
                <div className="pt-2 flex justify-end border-t border-slate-800">
                  <button
                    onClick={() => setKeyModalAppId(null)}
                    className="px-4 py-2 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded-xl font-semibold text-xs"
                  >
                    Done
                  </button>
                </div>
              </div>
            ) : (
              <form onSubmit={handleGenerateKey} className="space-y-4 text-xs">
                <div className="flex items-start gap-2 bg-amber-950/30 border border-amber-800/60 rounded-lg px-3 py-2 text-[11px] text-amber-200">
                  <LockKeyhole size={13} className="mt-0.5 shrink-0" />
                  <span>
                    This key will only reach {keyModalApp?.name || 'this application'}&apos;s workflows on the
                    WorkflowClass API (unassigned or other applications&apos; workflows return 404).
                  </span>
                </div>
                <div className="space-y-1">
                  <label className="text-slate-300 font-medium">Key Description / Name</label>
                  <input
                    type="text"
                    required
                    value={keyName}
                    onChange={e => setKeyName(e.target.value)}
                    className="w-full bg-slate-950 border border-slate-700 rounded-xl px-3 py-2 text-white focus:outline-none focus:border-blue-500"
                  />
                </div>
                <div className="space-y-1">
                  <label className="text-slate-300 font-medium">Environment</label>
                  <select
                    value={keyEnvironment}
                    onChange={e => setKeyEnvironment(e.target.value)}
                    className="w-full bg-slate-950 border border-slate-700 rounded-xl px-3 py-2 text-white focus:outline-none focus:border-blue-500"
                  >
                    <option value="Production">Production</option>
                    <option value="Staging">Staging</option>
                    <option value="Development">Development</option>
                  </select>
                </div>
                <ApiKeyScopePicker value={keyScopes} onChange={setKeyScopes} />
                <div className="space-y-1">
                  <label className="text-slate-300 font-medium">Expires In (Days)</label>
                  <input
                    type="number"
                    min="0"
                    value={keyExpiresInDays}
                    onChange={e => setKeyExpiresInDays(parseInt(e.target.value) || 0)}
                    className="w-full bg-slate-950 border border-slate-700 rounded-xl px-3 py-2 text-white focus:outline-none focus:border-blue-500"
                  />
                  <span className="text-[10px] text-slate-500">Enter 0 for never expires</span>
                </div>
                <div className="pt-2 flex justify-end gap-2 border-t border-slate-800">
                  <button
                    type="button"
                    onClick={() => setKeyModalAppId(null)}
                    className="px-4 py-2 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded-xl font-semibold"
                  >
                    Cancel
                  </button>
                  <button
                    type="submit"
                    disabled={keySubmitting}
                    className="px-4 py-2 bg-blue-600 hover:bg-blue-500 text-white rounded-xl font-semibold"
                  >
                    {keySubmitting ? 'Generating...' : 'Generate Key'}
                  </button>
                </div>
              </form>
            )}
          </div>
        </div>
      )}
    </div>
  );
};
