import React, { useState, useEffect } from 'react';
import { api } from '../api/client';
import { TenantApiKeyDto } from '../types';
import { Key, Plus, Copy, Check, Trash2, ShieldCheck, Terminal, AlertCircle, RefreshCw, Code2 } from 'lucide-react';

interface Props {
  tenantId: string;
  tenantName: string;
}

export const TenantApiKeyManager: React.FC<Props> = ({ tenantId, tenantName }) => {
  const [keys, setKeys] = useState<TenantApiKeyDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [copiedId, setCopiedId] = useState<string | null>(null);

  // Modal State
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [name, setName] = useState('My Application Key');
  const [applicationName, setApplicationName] = useState('Web App Backend');
  const [environment, setEnvironment] = useState('Production');
  const [scopes] = useState<string[]>(['*']);
  const [expiresInDays, setExpiresInDays] = useState(0);
  const [submitting, setSubmitting] = useState(false);

  // Alert for newly created key
  const [createdKey, setCreatedKey] = useState<string | null>(null);

  const loadKeys = async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await api.listTenantKeys(tenantId);
      setKeys(data);
    } catch (err: any) {
      setError(err.message || 'Failed to load application API keys');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadKeys();
  }, [tenantId]);

  const handleCopy = (text: string, id: string) => {
    navigator.clipboard.writeText(text);
    setCopiedId(id);
    setTimeout(() => setCopiedId(null), 2000);
  };

  const handleGenerateKey = async (e: React.FormEvent) => {
    e.preventDefault();
    setSubmitting(true);
    try {
      const res = await api.generateTenantKey(
        tenantId,
        name,
        applicationName,
        environment,
        scopes,
        expiresInDays > 0 ? expiresInDays : undefined
      );

      setCreatedKey(res.apiKey);
      setShowCreateModal(false);
      await loadKeys();
    } catch (err: any) {
      alert(`Failed to create API key: ${err.message}`);
    } finally {
      setSubmitting(false);
    }
  };

  const handleRevokeKey = async (keyId: string) => {
    if (!confirm('Are you sure you want to revoke this API key? Applications using this key will immediately lose access.')) {
      return;
    }
    try {
      await api.revokeTenantKey(tenantId, keyId);
      await loadKeys();
    } catch (err: any) {
      alert(`Failed to revoke key: ${err.message}`);
    }
  };

  return (
    <div className="space-y-6">
      {/* Header Banner */}
      <div className="bg-slate-850 p-6 rounded-2xl border border-slate-700/80 flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <div className="flex items-center gap-2 mb-1">
            <Key className="text-amber-400" size={20} />
            <h3 className="text-lg font-bold text-white">Application API Keys & Integration</h3>
          </div>
          <p className="text-xs text-slate-400">
            Manage credentials for your external applications (CRM, Billing, Customer Portal, AI Agents) to securely interact with FlowOS under <strong>{tenantName}</strong>.
          </p>
        </div>

        <div className="flex items-center gap-2.5">
          <button
            onClick={loadKeys}
            className="p-2 bg-slate-800 hover:bg-slate-750 text-slate-300 border border-slate-700 rounded-xl transition-colors"
            title="Refresh keys"
          >
            <RefreshCw size={14} className={loading ? 'animate-spin text-blue-400' : ''} />
          </button>
          <button
            onClick={() => setShowCreateModal(true)}
            className="px-4 py-2 bg-blue-600 hover:bg-blue-500 text-white font-semibold rounded-xl text-xs flex items-center gap-2 shadow-lg shadow-blue-500/25 transition-all"
          >
            <Plus size={15} />
            <span>Generate New Application Key</span>
          </button>
        </div>
      </div>

      {/* Newly Created Key Banner Alert */}
      {createdKey && (
        <div className="p-5 bg-emerald-950/60 border border-emerald-500/40 rounded-2xl space-y-3 animate-fadeIn">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-2 text-emerald-300 font-bold text-sm">
              <ShieldCheck size={18} className="text-emerald-400" />
              <span>New Application API Key Generated Successfully!</span>
            </div>
            <button 
              onClick={() => setCreatedKey(null)}
              className="text-xs text-slate-400 hover:text-white"
            >
              Dismiss
            </button>
          </div>
          <p className="text-xs text-slate-300">
            Please copy this key now. For security purposes, FlowOS hashes the key with SHA-256 and will never display it again.
          </p>
          <div className="p-3 bg-slate-950 border border-emerald-500/30 rounded-xl flex items-center justify-between font-mono text-xs text-emerald-300">
            <span className="break-all select-all font-bold">{createdKey}</span>
            <button
              onClick={() => handleCopy(createdKey, 'created')}
              className="ml-3 px-3 py-1.5 bg-emerald-600 hover:bg-emerald-500 text-white rounded-lg flex items-center gap-1.5 shrink-0 transition-colors"
            >
              {copiedId === 'created' ? <Check size={13} /> : <Copy size={13} />}
              <span>{copiedId === 'created' ? 'Copied' : 'Copy Key'}</span>
            </button>
          </div>
        </div>
      )}

      {error && (
        <div className="p-4 bg-rose-900/30 border border-rose-700 rounded-xl text-xs text-rose-300 flex items-center gap-2">
          <AlertCircle size={16} />
          <span>{error}</span>
        </div>
      )}

      {/* API Keys Table */}
      <div className="bg-slate-800 border border-slate-700 rounded-2xl overflow-hidden shadow-xl">
        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs text-slate-300">
            <thead className="bg-slate-900 text-slate-400 uppercase text-[10px] tracking-wider border-b border-slate-700">
              <tr>
                <th className="py-3.5 px-4">Application / Name</th>
                <th className="py-3.5 px-4">Key Token</th>
                <th className="py-3.5 px-4">Environment</th>
                <th className="py-3.5 px-4">Scopes</th>
                <th className="py-3.5 px-4">Created / Last Used</th>
                <th className="py-3.5 px-4 text-right">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-700/60">
              {keys.map((k) => (
                <tr key={k.id} className="hover:bg-slate-750 transition-colors">
                  <td className="py-3.5 px-4">
                    <div className="font-bold text-white text-xs">{k.name}</div>
                    <div className="text-[11px] text-slate-400">{k.applicationName || 'Default'}</div>
                  </td>
                  <td className="py-3.5 px-4">
                    <div className="flex items-center gap-2 font-mono text-[11px] text-slate-300 bg-slate-900/80 px-2.5 py-1 rounded-lg border border-slate-800 w-fit">
                      <span>{k.maskedKey}</span>
                      <button
                        onClick={() => handleCopy(k.maskedKey, k.id)}
                        className="text-slate-500 hover:text-white"
                        title="Copy Prefix"
                      >
                        {copiedId === k.id ? <Check size={12} className="text-emerald-400" /> : <Copy size={12} />}
                      </button>
                    </div>
                  </td>
                  <td className="py-3.5 px-4">
                    <span className={`px-2 py-0.5 rounded-full text-[10px] font-bold ${
                      k.environment === 'Production'
                        ? 'bg-emerald-500/20 text-emerald-300 border border-emerald-500/30'
                        : k.environment === 'Staging'
                        ? 'bg-amber-500/20 text-amber-300 border border-amber-500/30'
                        : 'bg-blue-500/20 text-blue-300 border border-blue-500/30'
                    }`}>
                      {k.environment}
                    </span>
                  </td>
                  <td className="py-3.5 px-4">
                    <div className="flex flex-wrap gap-1">
                      {(k.scopes || []).map((s, idx) => (
                        <span key={idx} className="px-1.5 py-0.5 bg-slate-900 border border-slate-800 rounded font-mono text-[10px] text-slate-300">
                          {s}
                        </span>
                      ))}
                    </div>
                  </td>
                  <td className="py-3.5 px-4 text-slate-400 text-[11px]">
                    <div>Created: {new Date(k.createdAt).toLocaleDateString()}</div>
                    <div className="text-slate-500 text-[10px]">
                      {k.lastUsedAt ? `Used: ${new Date(k.lastUsedAt).toLocaleDateString()}` : 'Never used'}
                    </div>
                  </td>
                  <td className="py-3.5 px-4 text-right">
                    <button
                      onClick={() => handleRevokeKey(k.id)}
                      className="p-1.5 text-rose-400 hover:text-rose-300 hover:bg-rose-500/10 rounded-lg transition-colors"
                      title="Revoke API key"
                    >
                      <Trash2 size={15} />
                    </button>
                  </td>
                </tr>
              ))}

              {keys.length === 0 && !loading && (
                <tr>
                  <td colSpan={6} className="py-12 text-center text-slate-500 text-xs">
                    No active API keys found for this tenant. Click "Generate New Application Key" above to connect your apps.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      {/* Integration Code Snippets Card */}
      <div className="bg-slate-850 border border-slate-700/80 rounded-2xl p-6 space-y-4">
        <div className="flex items-center gap-2">
          <Code2 className="text-purple-400" size={20} />
          <h4 className="text-sm font-bold text-white">How to Authenticate from Your Applications</h4>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 gap-4 text-xs font-mono">
          <div className="bg-slate-950 p-4 rounded-xl border border-slate-800 space-y-2">
            <div className="text-slate-400 font-sans font-semibold flex items-center gap-1">
              <Terminal size={13} className="text-blue-400" /> cURL / HTTP
            </div>
            <pre className="text-slate-300 overflow-x-auto text-[11px] leading-relaxed">
{`curl -X POST https://flowos.internal/api/events/publish \\
  -H "X-API-Key: YOUR_API_KEY" \\
  -H "Content-Type: application/json" \\
  -d '{
    "workflowInstanceId": "UUID",
    "eventType": "EVT-SUBMIT",
    "payload": { "amount": 2500 }
  }'`}
            </pre>
          </div>

          <div className="bg-slate-950 p-4 rounded-xl border border-slate-800 space-y-2">
            <div className="text-slate-400 font-sans font-semibold flex items-center gap-1">
              <Terminal size={13} className="text-emerald-400" /> Node.js / Python
            </div>
            <pre className="text-slate-300 overflow-x-auto text-[11px] leading-relaxed">
{`// Header required for all tenant calls:
headers = {
  "X-API-Key": "YOUR_API_KEY",
  "Content-Type": "application/json"
}
# Tenant boundary is automatically enforced!`}
            </pre>
          </div>
        </div>
      </div>

      {/* Create Key Modal */}
      {showCreateModal && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-slate-900 border border-slate-700 rounded-2xl max-w-md w-full p-6 shadow-2xl space-y-4">
            <div className="flex items-center justify-between border-b border-slate-800 pb-3">
              <h3 className="text-base font-bold text-white flex items-center gap-2">
                <Key className="text-amber-400" size={18} />
                Generate Application API Key
              </h3>
              <button onClick={() => setShowCreateModal(false)} className="text-slate-400 hover:text-white">
                &times;
              </button>
            </div>

            <form onSubmit={handleGenerateKey} className="space-y-4 text-xs">
              <div className="space-y-1">
                <label className="text-slate-300 font-medium">Key Description / Name</label>
                <input
                  type="text"
                  required
                  placeholder="e.g. ERP Invoicing Sync"
                  value={name}
                  onChange={e => setName(e.target.value)}
                  className="w-full bg-slate-950 border border-slate-700 rounded-xl px-3 py-2 text-white focus:outline-none focus:border-blue-500"
                />
              </div>

              <div className="space-y-1">
                <label className="text-slate-300 font-medium">Application Name</label>
                <input
                  type="text"
                  required
                  placeholder="e.g. BillingService"
                  value={applicationName}
                  onChange={e => setApplicationName(e.target.value)}
                  className="w-full bg-slate-950 border border-slate-700 rounded-xl px-3 py-2 text-white focus:outline-none focus:border-blue-500"
                />
              </div>

              <div className="space-y-1">
                <label className="text-slate-300 font-medium">Environment</label>
                <select
                  value={environment}
                  onChange={e => setEnvironment(e.target.value)}
                  className="w-full bg-slate-950 border border-slate-700 rounded-xl px-3 py-2 text-white focus:outline-none focus:border-blue-500"
                >
                  <option value="Production">Production</option>
                  <option value="Staging">Staging</option>
                  <option value="Development">Development</option>
                </select>
              </div>

              <div className="space-y-1">
                <label className="text-slate-300 font-medium">Expires In (Days)</label>
                <input
                  type="number"
                  min="0"
                  value={expiresInDays}
                  onChange={e => setExpiresInDays(parseInt(e.target.value) || 0)}
                  className="w-full bg-slate-950 border border-slate-700 rounded-xl px-3 py-2 text-white focus:outline-none focus:border-blue-500"
                />
                <span className="text-[10px] text-slate-500">Enter 0 for never expires</span>
              </div>

              <div className="pt-2 flex justify-end gap-2 border-t border-slate-800">
                <button
                  type="button"
                  onClick={() => setShowCreateModal(false)}
                  className="px-4 py-2 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded-xl font-semibold"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={submitting}
                  className="px-4 py-2 bg-blue-600 hover:bg-blue-500 text-white rounded-xl font-semibold flex items-center gap-1.5"
                >
                  {submitting ? 'Generating...' : 'Generate Key'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
