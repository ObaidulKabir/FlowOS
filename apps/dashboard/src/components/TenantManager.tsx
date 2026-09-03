import React, { useState, useEffect } from 'react';
import { api, getActiveTenantId, setActiveTenantId } from '../api/client';
import { TenantDto } from '../types';
import { Key, Copy, Check, Plus, RefreshCw, AlertCircle, Trash2, CheckCircle2, ShieldCheck, Globe, UserCheck } from 'lucide-react';

interface TenantManagerProps {
  onTenantChange?: (newTenantId: string) => void;
  openRegisterModal?: boolean;
  onRegisterModalClosed?: () => void;
}

export const TenantManager: React.FC<TenantManagerProps> = ({ 
  onTenantChange, 
  openRegisterModal = false,
  onRegisterModalClosed 
}) => {
  const [tenants, setTenants] = useState<TenantDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Active Tenant
  const [activeTenant, setActiveTenant] = useState<string>(getActiveTenantId());

  // Registration Modal State
  const [showRegisterModal, setShowRegisterModal] = useState(openRegisterModal);
  const [newTenantName, setNewTenantName] = useState('');
  const [newKeyName, setNewKeyName] = useState('Primary Key');
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    if (openRegisterModal) {
      setShowRegisterModal(true);
    }
  }, [openRegisterModal]);

  const closeRegisterModal = () => {
    setShowRegisterModal(false);
    if (onRegisterModalClosed) {
      onRegisterModalClosed();
    }
  };

  // Key Generation Modal State
  const [keyGenTenant, setKeyGenTenant] = useState<TenantDto | null>(null);
  const [generateKeyName, setGenerateKeyName] = useState('Production Key');

  // Key Generated Alert Modal
  const [latestKeyInfo, setLatestKeyInfo] = useState<{
    tenantId: string;
    tenantName: string;
    apiKey: string;
    keyName: string;
  } | null>(null);

  // Copy Feedback State
  const [copiedKey, setCopiedKey] = useState<string | null>(null);
  const [codeTab, setCodeTab] = useState<'appsettings' | 'python' | 'curl'>('appsettings');

  const loadTenants = async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await api.listTenants();
      setTenants(data);
    } catch (err: any) {
      setError(err.message || 'Failed to load tenants');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadTenants();
  }, []);

  const handleCopy = (text: string, id: string) => {
    navigator.clipboard.writeText(text);
    setCopiedKey(id);
    setTimeout(() => setCopiedKey(null), 2500);
  };

  const handleSelectTenant = (tenantId: string) => {
    setActiveTenant(tenantId);
    setActiveTenantId(tenantId);
    if (onTenantChange) {
      onTenantChange(tenantId);
    }
  };

  const handleRegisterTenant = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newTenantName.trim()) return;

    setSubmitting(true);
    setError(null);
    try {
      const res = await api.registerTenant(newTenantName.trim(), newKeyName.trim() || 'Primary Key');
      closeRegisterModal();
      setNewTenantName('');
      setNewKeyName('Primary Key');
      setLatestKeyInfo({
        tenantId: res.tenant.tenantId,
        tenantName: res.tenant.name,
        apiKey: res.apiKey,
        keyName: res.tenant.keys?.[0]?.name || 'Primary Key'
      });
      await loadTenants();
      handleSelectTenant(res.tenant.tenantId);
    } catch (err: any) {
      setError(err.message || 'Failed to register tenant');
    } finally {
      setSubmitting(false);
    }
  };

  const handleGenerateKey = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!keyGenTenant) return;

    setSubmitting(true);
    setError(null);
    try {
      const res = await api.generateTenantKey(keyGenTenant.tenantId, generateKeyName.trim() || 'API Key');
      const currentTarget = keyGenTenant;
      setKeyGenTenant(null);
      setGenerateKeyName('Production Key');
      setLatestKeyInfo({
        tenantId: currentTarget.tenantId,
        tenantName: currentTarget.name,
        apiKey: res.apiKey,
        keyName: res.name
      });
      await loadTenants();
    } catch (err: any) {
      setError(err.message || 'Failed to generate API key');
    } finally {
      setSubmitting(false);
    }
  };

  const handleRevokeKey = async (tenantId: string, keyId: string) => {
    if (!window.confirm('Are you sure you want to revoke this API key? Applications using it will no longer be able to authenticate.')) {
      return;
    }
    try {
      await api.revokeTenantKey(tenantId, keyId);
      await loadTenants();
    } catch (err: any) {
      setError(err.message || 'Failed to revoke API key');
    }
  };

  return (
    <div className="space-y-6">
      {/* Header with actions */}
      <div className="flex flex-col sm:flex-row justify-between items-start sm:items-center gap-4 bg-slate-900/60 p-4 rounded-xl border border-slate-800">
        <div>
          <h3 className="text-lg font-bold text-white flex items-center gap-2">
            <Key className="text-amber-400" size={20} />
            Tenant Registry & API Key Governance
          </h3>
          <p className="text-xs text-slate-400 mt-0.5">
            Register new tenants, provision scoped API keys, and copy pre-built deployment configs.
          </p>
        </div>

        <div className="flex items-center gap-2">
          <button
            onClick={loadTenants}
            className="p-2 text-slate-400 hover:text-white bg-slate-800 hover:bg-slate-700 border border-slate-700 rounded-lg text-xs flex items-center gap-1.5 transition-all"
            title="Refresh tenants"
          >
            <RefreshCw size={14} /> Refresh
          </button>
          <button
            onClick={() => setShowRegisterModal(true)}
            className="px-3.5 py-2 bg-blue-600 hover:bg-blue-500 text-white rounded-lg text-xs font-semibold flex items-center gap-1.5 shadow-lg shadow-blue-600/20 transition-all"
          >
            <Plus size={15} /> Register New Tenant
          </button>
        </div>
      </div>

      {error && (
        <div className="bg-rose-900/40 border border-rose-700 text-rose-300 px-4 py-3 rounded-xl flex items-center justify-between text-xs">
          <div className="flex items-center gap-2">
            <AlertCircle size={16} />
            <span>{error}</span>
          </div>
          <button onClick={() => setError(null)} className="text-lg font-bold">&times;</button>
        </div>
      )}

      {/* Newly Generated Key Alert / Modal */}
      {latestKeyInfo && (
        <div className="bg-slate-900 border-2 border-emerald-500/70 rounded-2xl p-6 shadow-2xl space-y-4">
          <div className="flex justify-between items-start">
            <div className="flex items-center gap-3">
              <div className="w-10 h-10 rounded-full bg-emerald-500/20 border border-emerald-500/40 flex items-center justify-center text-emerald-400">
                <ShieldCheck size={22} />
              </div>
              <div>
                <h4 className="text-base font-bold text-white">
                  API Key Successfully Generated for {latestKeyInfo.tenantName}!
                </h4>
                <p className="text-xs text-amber-300 flex items-center gap-1 mt-0.5">
                  <AlertCircle size={12} />
                  Copy this key now. For your security, it will not be displayed again.
                </p>
              </div>
            </div>
            <button
              onClick={() => setLatestKeyInfo(null)}
              className="text-slate-400 hover:text-white text-sm px-2 py-1 bg-slate-800 rounded-md border border-slate-700"
            >
              Dismiss
            </button>
          </div>

          {/* Raw Key Display */}
          <div className="bg-black/90 p-4 rounded-xl border border-emerald-500/30 flex flex-col sm:flex-row justify-between items-start sm:items-center gap-3">
            <div className="font-mono text-sm text-emerald-400 font-bold break-all">
              {latestKeyInfo.apiKey}
            </div>
            <button
              onClick={() => handleCopy(latestKeyInfo.apiKey, 'raw-key')}
              className="px-4 py-2 bg-emerald-600 hover:bg-emerald-500 text-white rounded-lg text-xs font-semibold flex items-center gap-1.5 shrink-0 shadow transition-all"
            >
              {copiedKey === 'raw-key' ? (
                <>
                  <Check size={14} className="text-emerald-200" /> Copied!
                </>
              ) : (
                <>
                  <Copy size={14} /> Copy API Key
                </>
              )}
            </button>
          </div>

          {/* Pre-built Configuration Snippets */}
          <div className="bg-slate-950 rounded-xl border border-slate-800 overflow-hidden">
            <div className="flex border-b border-slate-800 bg-slate-900/80 px-3 pt-2 text-xs font-medium">
              <button
                onClick={() => setCodeTab('appsettings')}
                className={`px-3 py-1.5 border-b-2 transition-all ${
                  codeTab === 'appsettings'
                    ? 'border-blue-500 text-blue-400 font-bold'
                    : 'border-transparent text-slate-400 hover:text-slate-200'
                }`}
              >
                appsettings.json
              </button>
              <button
                onClick={() => setCodeTab('python')}
                className={`px-3 py-1.5 border-b-2 transition-all ${
                  codeTab === 'python'
                    ? 'border-blue-500 text-blue-400 font-bold'
                    : 'border-transparent text-slate-400 hover:text-slate-200'
                }`}
              >
                Python Deployment
              </button>
              <button
                onClick={() => setCodeTab('curl')}
                className={`px-3 py-1.5 border-b-2 transition-all ${
                  codeTab === 'curl'
                    ? 'border-blue-500 text-blue-400 font-bold'
                    : 'border-transparent text-slate-400 hover:text-slate-200'
                }`}
              >
                cURL
              </button>
            </div>

            <div className="p-4 relative">
              <button
                onClick={() => {
                  let text = '';
                  if (codeTab === 'appsettings') {
                    text = JSON.stringify({
                      ApiKey: latestKeyInfo.apiKey,
                      TenantId: latestKeyInfo.tenantId,
                      BaseUrl: window.location.origin
                    }, null, 2);
                  } else if (codeTab === 'python') {
                    text = `# Python deployment config
API_KEY = "${latestKeyInfo.apiKey}"
TENANT_ID = "${latestKeyInfo.tenantId}"
BASE_URL = "${window.location.origin}"

headers = {
    "X-API-Key": API_KEY,
    "x-tenant-id": TENANT_ID,
    "Content-Type": "application/json"
}`;
                  } else {
                    text = `curl -X POST ${window.location.origin}/mcp \\
  -H "X-API-Key: ${latestKeyInfo.apiKey}" \\
  -H "x-tenant-id: ${latestKeyInfo.tenantId}" \\
  -H "Content-Type: application/json"`;
                  }
                  handleCopy(text, 'snippet');
                }}
                className="absolute top-3 right-3 px-2.5 py-1 bg-slate-800 hover:bg-slate-700 border border-slate-700 text-slate-300 rounded text-xs flex items-center gap-1"
              >
                {copiedKey === 'snippet' ? <Check size={12} className="text-emerald-400" /> : <Copy size={12} />}
                {copiedKey === 'snippet' ? 'Copied' : 'Copy'}
              </button>

              <pre className="font-mono text-xs text-slate-300 overflow-x-auto">
                {codeTab === 'appsettings' &&
                  JSON.stringify(
                    {
                      ApiKey: latestKeyInfo.apiKey,
                      TenantId: latestKeyInfo.tenantId,
                      BaseUrl: window.location.origin
                    },
                    null,
                    2
                  )}

                {codeTab === 'python' &&
                  `# Python deployment config
API_KEY = "${latestKeyInfo.apiKey}"
TENANT_ID = "${latestKeyInfo.tenantId}"
BASE_URL = "${window.location.origin}"

headers = {
    "X-API-Key": API_KEY,
    "x-tenant-id": TENANT_ID,
    "Content-Type": "application/json"
}`}

                {codeTab === 'curl' &&
                  `curl -X POST ${window.location.origin}/mcp \\
  -H "X-API-Key: ${latestKeyInfo.apiKey}" \\
  -H "x-tenant-id: ${latestKeyInfo.tenantId}" \\
  -H "Content-Type: application/json"`}
              </pre>
            </div>
          </div>
        </div>
      )}

      {/* Tenants Cards / List */}
      {loading && tenants.length === 0 ? (
        <div className="text-center py-16 text-slate-400 text-xs">
          <span className="inline-block animate-spin mr-2">↻</span> Loading registered tenants...
        </div>
      ) : tenants.length === 0 ? (
        <div className="bg-slate-800/40 border border-dashed border-slate-700 rounded-2xl p-12 text-center">
          <Globe className="mx-auto text-slate-500 mb-3" size={36} />
          <h4 className="text-base font-bold text-white mb-1">No Tenants Registered Yet</h4>
          <p className="text-xs text-slate-400 max-w-sm mx-auto mb-4">
            Create your first tenant to start managing isolated workflows, state machines, and scoped API keys.
          </p>
          <button
            onClick={() => setShowRegisterModal(true)}
            className="px-4 py-2 bg-blue-600 hover:bg-blue-500 text-white rounded-lg text-xs font-semibold inline-flex items-center gap-1.5"
          >
            <Plus size={15} /> Register First Tenant
          </button>
        </div>
      ) : (
        <div className="grid grid-cols-1 gap-4">
          {tenants.map((t) => {
            const isActive = t.tenantId === activeTenant;
            return (
              <div
                key={t.tenantId}
                className={`bg-slate-800/90 rounded-2xl border transition-all ${
                  isActive
                    ? 'border-blue-500/80 shadow-lg shadow-blue-500/10'
                    : 'border-slate-700 hover:border-slate-600'
                }`}
              >
                <div className="p-5 flex flex-col md:flex-row md:items-center justify-between gap-4 border-b border-slate-700/60">
                  <div className="space-y-1">
                    <div className="flex items-center gap-2.5">
                      <h4 className="text-base font-bold text-white">{t.name}</h4>
                      {isActive && (
                        <span className="px-2 py-0.5 rounded-full text-[10px] font-bold bg-blue-500/20 text-blue-300 border border-blue-500/40 flex items-center gap-1">
                          <UserCheck size={11} /> Current Dashboard Context
                        </span>
                      )}
                      <span className="px-2 py-0.5 rounded-full text-[10px] font-medium bg-emerald-500/20 text-emerald-300 border border-emerald-500/30">
                        {t.status}
                      </span>
                    </div>

                    <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-slate-400">
                      <span className="flex items-center gap-1.5 font-mono">
                        Tenant ID: <span className="text-slate-200">{t.tenantId}</span>
                        <button
                          onClick={() => handleCopy(t.tenantId, `tenant-${t.tenantId}`)}
                          className="hover:text-white p-0.5 rounded transition-colors"
                          title="Copy Tenant ID"
                        >
                          {copiedKey === `tenant-${t.tenantId}` ? (
                            <Check size={13} className="text-emerald-400" />
                          ) : (
                            <Copy size={13} />
                          )}
                        </button>
                      </span>
                      <span>•</span>
                      <span>Created: {new Date(t.createdAt).toLocaleDateString()}</span>
                      <span>•</span>
                      <span className="text-amber-400 font-semibold">{t.keyCount} Active Key{t.keyCount === 1 ? '' : 's'}</span>
                    </div>
                  </div>

                  <div className="flex items-center gap-2 self-start md:self-auto">
                    {!isActive ? (
                      <button
                        onClick={() => handleSelectTenant(t.tenantId)}
                        className="px-3 py-1.5 bg-slate-700 hover:bg-slate-600 text-white rounded-lg text-xs font-semibold flex items-center gap-1.5 transition-all"
                      >
                        <UserCheck size={13} /> Switch Context
                      </button>
                    ) : (
                      <span className="text-xs text-blue-400 font-semibold flex items-center gap-1">
                        <CheckCircle2 size={14} /> Selected
                      </span>
                    )}

                    <button
                      onClick={() => setKeyGenTenant(t)}
                      className="px-3 py-1.5 bg-blue-600/20 hover:bg-blue-600/30 border border-blue-500/40 text-blue-300 rounded-lg text-xs font-semibold flex items-center gap-1.5 transition-all"
                    >
                      <Plus size={13} /> New Key
                    </button>
                  </div>
                </div>

                {/* API Keys Table */}
                <div className="p-5 bg-slate-900/40 rounded-b-2xl">
                  <div className="text-xs font-bold uppercase tracking-wider text-slate-400 mb-3 flex items-center gap-1.5">
                    <Key size={13} className="text-amber-400" /> Provisioned API Keys
                  </div>

                  {(!t.keys || t.keys.length === 0) ? (
                    <div className="text-xs text-slate-500 italic py-2">
                      No API keys currently active for this tenant. Click "+ New Key" to create one.
                    </div>
                  ) : (
                    <div className="overflow-x-auto">
                      <table className="w-full text-left text-xs text-slate-300">
                        <thead className="bg-slate-800/70 text-[11px] text-slate-400 uppercase tracking-wider">
                          <tr>
                            <th className="py-2.5 px-3 rounded-l-lg">Key Name</th>
                            <th className="py-2.5 px-3">Masked Secret</th>
                            <th className="py-2.5 px-3">Created</th>
                            <th className="py-2.5 px-3">Last Used</th>
                            <th className="py-2.5 px-3 text-right rounded-r-lg">Action</th>
                          </tr>
                        </thead>
                        <tbody className="divide-y divide-slate-800">
                          {t.keys.map((k) => (
                            <tr key={k.id} className="hover:bg-slate-800/40 transition-colors">
                              <td className="py-2.5 px-3 font-medium text-white">{k.name}</td>
                              <td className="py-2.5 px-3 font-mono text-slate-400">{k.maskedKey}</td>
                              <td className="py-2.5 px-3 text-slate-400">{new Date(k.createdAt).toLocaleDateString()}</td>
                              <td className="py-2.5 px-3 text-slate-400">
                                {k.lastUsedAt ? new Date(k.lastUsedAt).toLocaleString() : 'Never'}
                              </td>
                              <td className="py-2.5 px-3 text-right">
                                <button
                                  onClick={() => handleRevokeKey(t.tenantId, k.id)}
                                  className="text-rose-400 hover:text-rose-300 p-1 rounded hover:bg-rose-500/10 transition-colors"
                                  title="Revoke Key"
                                >
                                  <Trash2 size={14} />
                                </button>
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      )}

      {/* Register Tenant Modal */}
      {showRegisterModal && (
        <div className="fixed inset-0 bg-black/70 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-slate-900 border border-slate-700 rounded-2xl max-w-md w-full p-6 shadow-2xl space-y-4">
            <h3 className="text-lg font-bold text-white flex items-center gap-2">
              <Globe className="text-blue-400" size={20} />
              Register New Tenant
            </h3>
            <p className="text-xs text-slate-400">
              Create a dedicated tenant environment. An initial API key will be automatically provisioned for immediate integration.
            </p>

            <form onSubmit={handleRegisterTenant} className="space-y-4 pt-2">
              <div>
                <label className="block text-xs font-bold text-slate-300 mb-1">Tenant Name *</label>
                <input
                  type="text"
                  required
                  placeholder="e.g. Acme Logistics, FinOps Core"
                  value={newTenantName}
                  onChange={(e) => setNewTenantName(e.target.value)}
                  className="w-full bg-slate-800 border border-slate-700 rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-blue-500"
                />
              </div>

              <div>
                <label className="block text-xs font-bold text-slate-300 mb-1">Initial API Key Name</label>
                <input
                  type="text"
                  placeholder="e.g. Production Deployment Key"
                  value={newKeyName}
                  onChange={(e) => setNewKeyName(e.target.value)}
                  className="w-full bg-slate-800 border border-slate-700 rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-blue-500"
                />
              </div>

              <div className="flex justify-end gap-3 pt-3 border-t border-slate-800">
                <button
                  type="button"
                  onClick={closeRegisterModal}
                  className="px-4 py-2 text-xs font-semibold text-slate-400 hover:text-white"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={submitting || !newTenantName.trim()}
                  className="px-4 py-2 bg-blue-600 hover:bg-blue-500 disabled:opacity-50 text-white rounded-lg text-xs font-semibold flex items-center gap-1.5"
                >
                  {submitting ? 'Registering...' : 'Register & Provision Key'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Generate Additional Key Modal */}
      {keyGenTenant && (
        <div className="fixed inset-0 bg-black/70 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-slate-900 border border-slate-700 rounded-2xl max-w-md w-full p-6 shadow-2xl space-y-4">
            <h3 className="text-lg font-bold text-white flex items-center gap-2">
              <Key className="text-amber-400" size={20} />
              Generate API Key for {keyGenTenant.name}
            </h3>
            <p className="text-xs text-slate-400">
              Provide a descriptive label for this key (e.g., CI/CD Pipeline, Python Deployment Script, MCP Runner).
            </p>

            <form onSubmit={handleGenerateKey} className="space-y-4 pt-2">
              <div>
                <label className="block text-xs font-bold text-slate-300 mb-1">Key Label *</label>
                <input
                  type="text"
                  required
                  placeholder="e.g. Python Deployment Runner"
                  value={generateKeyName}
                  onChange={(e) => setGenerateKeyName(e.target.value)}
                  className="w-full bg-slate-800 border border-slate-700 rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-blue-500"
                />
              </div>

              <div className="flex justify-end gap-3 pt-3 border-t border-slate-800">
                <button
                  type="button"
                  onClick={() => setKeyGenTenant(null)}
                  className="px-4 py-2 text-xs font-semibold text-slate-400 hover:text-white"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={submitting}
                  className="px-4 py-2 bg-emerald-600 hover:bg-emerald-500 disabled:opacity-50 text-white rounded-lg text-xs font-semibold flex items-center gap-1.5"
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
