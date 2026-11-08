import React, { useEffect, useState } from 'react';
import {
  ArrowLeft, AppWindow, Workflow, Key, Brain, RefreshCw, Plus, Check, Copy,
  ShieldCheck, LockKeyhole, X, AlertCircle
} from 'lucide-react';
import { api } from '../api/client';
import { TenantApplicationDto, TenantApiKeyDto, CreateKeyResponse } from '../types';
import { ApplicationWorkspace } from './ApplicationWorkspace';
import { AiContextView } from './AiContextView';
import { ApiKeyScopePicker } from './ApiKeyScopePicker';
import { API_KEY_SCOPE_PRESETS } from '../lib/apiKeyScopes';

type DetailTab = 'workflows' | 'ai' | 'keys';

// Everything ApplicationWorkspace needs except the two props this view supplies itself
// (applications isn't needed since lockedApplicationId hides its picker regardless).
type WorkspaceHandlers = Omit<React.ComponentProps<typeof ApplicationWorkspace>, 'applications' | 'lockedApplicationId'>;

interface Props extends WorkspaceHandlers {
  application: TenantApplicationDto;
  tenantId: string;
  onBack: () => void;
  /**
   * Re-fetches the tenant's applications (so this header's stat pills and the Workflows tab's
   * ApplicationWorkspace reflect a key generated/revoked or a workflow assigned here).
   */
  onRefreshApplications?: () => void | Promise<void>;
}

/**
 * Applications (1/4-4/4): the mockup's Tenant -> Application -> Workflows/AI Context/API Keys
 * drill-down, scoped to one application via applicationId. Reuses the existing workflow design
 * studio (ApplicationWorkspace, locked to this application) and tenant-wide AI Context view
 * (AiContextView, pre-filtered to this application's prompt/profile bindings) rather than
 * rebuilding either; the API Keys tab is new here, filtered to this application's keys. The
 * mockup's key-rotation wizard and tenant-shared-agent override toggle are out of scope - see
 * TenantApplicationManager for the flat application list this view is opened from.
 */
export const ApplicationDetailView: React.FC<Props> = ({
  application,
  tenantId,
  onBack,
  onRefreshApplications,
  ...workspaceProps
}) => {
  const [tab, setTab] = useState<DetailTab>('workflows');

  const tabs: { id: DetailTab; label: string; icon: typeof Workflow }[] = [
    { id: 'workflows', label: 'Workflows', icon: Workflow },
    { id: 'ai', label: 'AI Context', icon: Brain },
    { id: 'keys', label: 'API Keys', icon: Key }
  ];

  return (
    <div className="space-y-4">
      <button
        onClick={onBack}
        className="flex items-center gap-1.5 text-xs text-slate-400 hover:text-white transition-colors"
      >
        <ArrowLeft size={13} /> Applications
      </button>

      <div className="rounded-2xl border border-blue-500/30 bg-gradient-to-r from-blue-950/50 via-slate-900 to-violet-950/40 p-5">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div>
            <div className="flex items-center gap-2">
              <AppWindow className="text-blue-400" size={20} />
              <h2 className="text-lg font-bold text-white">{application.name}</h2>
            </div>
            {application.description && (
              <p className="text-xs text-slate-400 mt-1 max-w-2xl">{application.description}</p>
            )}
          </div>
          <div className="flex items-center gap-3">
            <div className="text-center px-4 py-1.5 bg-slate-900/60 border border-slate-700 rounded-xl min-w-[88px]">
              <div className="text-base font-bold text-white font-mono">{application.workflowCount}</div>
              <div className="text-[10px] text-slate-500 uppercase tracking-wide">Workflows</div>
            </div>
            <div className="text-center px-4 py-1.5 bg-slate-900/60 border border-slate-700 rounded-xl min-w-[88px]">
              <div className="text-base font-bold text-white font-mono">{application.activeKeyCount}</div>
              <div className="text-[10px] text-slate-500 uppercase tracking-wide">Active Keys</div>
            </div>
          </div>
        </div>
      </div>

      <div className="flex bg-slate-900 rounded-xl p-1 border border-slate-800 text-xs w-fit">
        {tabs.map(t => {
          const Icon = t.icon;
          return (
            <button
              key={t.id}
              onClick={() => setTab(t.id)}
              className={`px-3.5 py-1.5 rounded-lg font-semibold flex items-center gap-1.5 transition-all ${
                tab === t.id ? 'bg-blue-600 text-white shadow' : 'text-slate-400 hover:text-white'
              }`}
            >
              <Icon size={13} /> {t.label}
            </button>
          );
        })}
      </div>

      {tab === 'workflows' && (
        <ApplicationWorkspace {...workspaceProps} lockedApplicationId={application.id} />
      )}

      {tab === 'ai' && (
        <AiContextView tenantName={workspaceProps.tenantName} applicationId={application.id} compact />
      )}

      {tab === 'keys' && (
        <ApplicationKeysPanel tenantId={tenantId} application={application} onChanged={onRefreshApplications} />
      )}
    </div>
  );
};

/**
 * Applications (4/4): this application's own API keys. Mirrors TenantApplicationManager's
 * per-application key-generation modal, but as this tab's own list + revoke rather than a modal
 * opened from a flat table row. No backend "rotate" endpoint exists, so there is no rotation
 * wizard here - generate a replacement key and revoke the old one once it's unused.
 */
const ApplicationKeysPanel: React.FC<{
  tenantId: string;
  application: TenantApplicationDto;
  onChanged?: () => void | Promise<void>;
}> = ({ tenantId, application, onChanged }) => {
  const [keys, setKeys] = useState<TenantApiKeyDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [showCreate, setShowCreate] = useState(false);
  const [keyName, setKeyName] = useState('App Key');
  const [keyEnvironment, setKeyEnvironment] = useState('Production');
  const [keyScopes, setKeyScopes] = useState<string[]>([...API_KEY_SCOPE_PRESETS.operator]);
  const [keyExpiresInDays, setKeyExpiresInDays] = useState(0);
  const [submitting, setSubmitting] = useState(false);
  const [createdKey, setCreatedKey] = useState<CreateKeyResponse | null>(null);
  const [copied, setCopied] = useState(false);
  const [revokingId, setRevokingId] = useState<string | null>(null);

  const load = async () => {
    setLoading(true);
    setError(null);
    try {
      const all = await api.listTenantKeys(tenantId);
      setKeys(all.filter(k => k.applicationId === application.id));
    } catch (err: any) {
      setError(err.message || 'Failed to load keys');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId, application.id]);

  const openCreate = () => {
    setKeyName('App Key');
    setKeyEnvironment('Production');
    setKeyScopes([...API_KEY_SCOPE_PRESETS.operator]);
    setKeyExpiresInDays(0);
    setCreatedKey(null);
    setShowCreate(true);
  };

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    setSubmitting(true);
    try {
      const res = await api.generateTenantKey(
        tenantId,
        keyName,
        undefined,
        keyEnvironment,
        keyScopes,
        keyExpiresInDays > 0 ? keyExpiresInDays : undefined,
        application.id
      );
      if (!res.apiKey || res.apiKey === res.maskedKey || res.apiKey.includes('•')) {
        throw new Error('The API did not return the one-time full key. Revoke this key and generate a replacement.');
      }
      setCreatedKey(res);
      await load();
      if (onChanged) await onChanged();
    } catch (err: any) {
      alert(`Failed to generate key: ${err.message}`);
    } finally {
      setSubmitting(false);
    }
  };

  const handleRevoke = async (key: TenantApiKeyDto) => {
    if (!confirm(`Revoke "${key.name}" (${key.maskedKey})? This cannot be undone.`)) return;
    setRevokingId(key.id);
    try {
      await api.revokeTenantKey(tenantId, key.id);
      await load();
      if (onChanged) await onChanged();
    } catch (err: any) {
      alert(err.message);
    } finally {
      setRevokingId(null);
    }
  };

  const handleCopy = async (text: string) => {
    try {
      await navigator.clipboard.writeText(text);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      // Clipboard unavailable - the key text is still select-all in the panel.
    }
  };

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-xs text-slate-400 max-w-xl">
          Keys issued here are confined to <strong>{application.name}</strong>&apos;s workflows on the WorkflowClass
          API (unassigned or other applications&apos; workflows return 404).
        </p>
        <div className="flex items-center gap-2">
          <button
            onClick={load}
            className="p-2 bg-slate-800 hover:bg-slate-750 text-slate-300 border border-slate-700 rounded-xl transition-colors"
            title="Refresh keys"
          >
            <RefreshCw size={14} className={loading ? 'animate-spin text-blue-400' : ''} />
          </button>
          <button
            onClick={openCreate}
            className="px-3.5 py-2 bg-amber-600 hover:bg-amber-500 text-white font-semibold rounded-xl text-xs flex items-center gap-1.5"
          >
            <Plus size={14} /> Generate key
          </button>
        </div>
      </div>

      {error && (
        <div className="p-3 bg-rose-900/30 border border-rose-700 rounded-xl text-xs text-rose-300 flex items-center gap-2">
          <AlertCircle size={14} /> {error}
        </div>
      )}

      <div className="bg-slate-800 border border-slate-700 rounded-2xl overflow-hidden shadow-xl">
        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs text-slate-300">
            <thead className="bg-slate-900 text-slate-400 uppercase text-[10px] tracking-wider border-b border-slate-700">
              <tr>
                <th className="py-3 px-4">Name</th>
                <th className="py-3 px-4">Key</th>
                <th className="py-3 px-4">Created</th>
                <th className="py-3 px-4">Last used</th>
                <th className="py-3 px-4">Expires</th>
                <th className="py-3 px-4">Status</th>
                <th className="py-3 px-4 text-right">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-700/60">
              {keys.map(key => {
                const status = key.isRevoked ? 'Revoked' : key.isExpired ? 'Expired' : 'Active';
                return (
                  <tr key={key.id} className="hover:bg-slate-750 transition-colors">
                    <td className="py-3 px-4 font-semibold text-white">{key.name}</td>
                    <td className="py-3 px-4 font-mono text-slate-400">{key.maskedKey}</td>
                    <td className="py-3 px-4 text-slate-400">{new Date(key.createdAt).toLocaleDateString()}</td>
                    <td className="py-3 px-4 text-slate-400">
                      {key.lastUsedAt ? new Date(key.lastUsedAt).toLocaleDateString() : 'never'}
                    </td>
                    <td className="py-3 px-4 text-slate-400">
                      {key.expiresAt ? new Date(key.expiresAt).toLocaleDateString() : 'never'}
                    </td>
                    <td className="py-3 px-4">
                      <span
                        className={`text-[10px] font-semibold px-2 py-0.5 rounded-full border ${
                          status === 'Active'
                            ? 'bg-emerald-500/10 text-emerald-300 border-emerald-500/30'
                            : status === 'Expired'
                            ? 'bg-amber-500/10 text-amber-300 border-amber-500/30'
                            : 'bg-slate-700 text-slate-400 border-slate-600'
                        }`}
                      >
                        {status}
                      </span>
                    </td>
                    <td className="py-3 px-4 text-right">
                      {!key.isRevoked && (
                        <button
                          onClick={() => handleRevoke(key)}
                          disabled={revokingId === key.id}
                          className="text-rose-400 hover:text-rose-300 text-[11px] font-semibold disabled:opacity-50"
                        >
                          {revokingId === key.id ? 'Revoking…' : 'Revoke'}
                        </button>
                      )}
                    </td>
                  </tr>
                );
              })}
              {keys.length === 0 && !loading && (
                <tr>
                  <td colSpan={7} className="py-10 text-center text-slate-500 text-xs">
                    No keys issued for this application yet.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      {showCreate && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-slate-900 border border-slate-700 rounded-2xl max-w-md w-full p-6 shadow-2xl space-y-4 max-h-[90vh] overflow-y-auto">
            <div className="flex items-center justify-between border-b border-slate-800 pb-3">
              <h3 className="text-base font-bold text-white flex items-center gap-2">
                <Key className="text-amber-400" size={18} /> Key for {application.name}
              </h3>
              <button onClick={() => setShowCreate(false)} className="text-slate-400 hover:text-white">
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
                    onClick={() => handleCopy(createdKey.apiKey)}
                    className="ml-3 px-3 py-1.5 bg-emerald-600 hover:bg-emerald-500 text-white rounded-lg flex items-center gap-1.5 shrink-0 transition-colors"
                  >
                    {copied ? <Check size={13} /> : <Copy size={13} />}
                    <span>{copied ? 'Copied' : 'Copy Key'}</span>
                  </button>
                </div>
                <div className="pt-2 flex justify-end border-t border-slate-800">
                  <button
                    onClick={() => setShowCreate(false)}
                    className="px-4 py-2 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded-xl font-semibold text-xs"
                  >
                    Done
                  </button>
                </div>
              </div>
            ) : (
              <form onSubmit={handleCreate} className="space-y-4 text-xs">
                <div className="flex items-start gap-2 bg-amber-950/30 border border-amber-800/60 rounded-lg px-3 py-2 text-[11px] text-amber-200">
                  <LockKeyhole size={13} className="mt-0.5 shrink-0" />
                  <span>
                    This key will only reach {application.name}&apos;s workflows (unassigned or other
                    applications&apos; workflows return 404).
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
                    onClick={() => setShowCreate(false)}
                    className="px-4 py-2 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded-xl font-semibold"
                  >
                    Cancel
                  </button>
                  <button
                    type="submit"
                    disabled={submitting}
                    className="px-4 py-2 bg-blue-600 hover:bg-blue-500 text-white rounded-xl font-semibold"
                  >
                    {submitting ? 'Generating...' : 'Generate Key'}
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
