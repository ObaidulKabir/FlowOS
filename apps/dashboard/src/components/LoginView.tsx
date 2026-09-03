import React, { useState, useEffect } from 'react';
import { AuthSession, TenantDto } from '../types';
import { api, setAuthSession } from '../api/client';
import { Shield, Building2, Key, ArrowRight, Sparkles, Lock, Cpu, Terminal } from 'lucide-react';

interface LoginViewProps {
  onLoginSuccess: (session: AuthSession) => void;
}

export const LoginView: React.FC<LoginViewProps> = ({ onLoginSuccess }) => {
  const [activePortal, setActivePortal] = useState<'Tenant' | 'Admin'>('Tenant');
  const [tenants, setTenants] = useState<TenantDto[]>([]);
  const [loadingTenants, setLoadingTenants] = useState(false);

  // Tenant Login Form
  const [selectedTenantId, setSelectedTenantId] = useState<string>('22222222-2222-2222-2222-222222222222');
  const [customTenantId, setCustomTenantId] = useState<string>('');
  const [apiKey, setApiKey] = useState<string>('flowos_prod_secret_key_32_chars_min');
  const [useCustomTenant, setUseCustomTenant] = useState(false);

  // Admin Login Form
  const [adminUsername, setAdminUsername] = useState('superadmin@flowos.internal');
  const [adminPassword, setAdminPassword] = useState('flowos-admin-root');

  useEffect(() => {
    const fetchTenants = async () => {
      setLoadingTenants(true);
      try {
        const data = await api.listTenants();
        setTenants(data);
        if (data.length > 0 && !selectedTenantId) {
          setSelectedTenantId(data[0].tenantId);
        }
      } catch (err) {
        console.warn('Could not list tenants in login view, fallback to defaults', err);
      } finally {
        setLoadingTenants(false);
      }
    };
    fetchTenants();
  }, []);

  const handleTenantLogin = (tenantIdToUse?: string, tenantNameToUse?: string) => {
    const finalTenantId = tenantIdToUse || (useCustomTenant ? customTenantId.trim() : selectedTenantId);
    if (!finalTenantId) {
      alert('Please select or enter a valid Tenant ID.');
      return;
    }

    const matchedTenant = tenants.find(t => t.tenantId === finalTenantId);
    const tenantName = tenantNameToUse || matchedTenant?.name || 'Client Tenant';

    const session: AuthSession = {
      role: 'Tenant',
      tenantId: finalTenantId,
      tenantName: tenantName,
      apiKey: apiKey.trim() || undefined,
      username: `user@${tenantName.toLowerCase().replace(/\s+/g, '')}.com`
    };

    setAuthSession(session);
    onLoginSuccess(session);
  };

  const handleAdminLogin = () => {
    const session: AuthSession = {
      role: 'Admin',
      tenantId: '11111111-1111-1111-1111-111111111111',
      tenantName: 'Platform Administrator',
      username: adminUsername || 'admin@flowos.internal'
    };

    setAuthSession(session);
    onLoginSuccess(session);
  };

  return (
    <div className="min-h-screen bg-slate-950 text-slate-100 flex flex-col justify-center items-center px-4 py-12 relative overflow-hidden">
      {/* Background ambient glow */}
      <div className="absolute top-1/4 left-1/2 -translate-x-1/2 w-[600px] h-[350px] bg-gradient-to-tr from-blue-600/20 via-purple-600/20 to-pink-600/10 blur-[120px] pointer-events-none rounded-full" />

      {/* Brand Header */}
      <div className="text-center max-w-xl mx-auto mb-8 relative z-10">
        <div className="inline-flex items-center gap-2 px-3 py-1.5 rounded-full bg-slate-900 border border-slate-800 text-xs font-semibold text-slate-300 mb-4 shadow-sm">
          <span className="flex h-2 w-2 rounded-full bg-emerald-400 animate-pulse" />
          <span>FlowOS Enterprise Kernel v1.0</span>
        </div>
        <h1 className="text-3xl md:text-5xl font-extrabold tracking-tight text-white mb-3">
          Sign In to <span className="bg-gradient-to-r from-blue-400 via-indigo-300 to-purple-400 bg-clip-text text-transparent">FlowOS</span>
        </h1>
        <p className="text-sm text-slate-400">
          Select your portal to access your isolated tenant workspace or platform governance plane.
        </p>
      </div>

      {/* Main Authentication Card */}
      <div className="bg-slate-900 border border-slate-800 rounded-3xl max-w-xl w-full p-8 shadow-2xl relative z-10 space-y-6">
        
        {/* Portal Switcher Tabs */}
        <div className="grid grid-cols-2 p-1.5 bg-slate-950 rounded-2xl border border-slate-800 text-xs font-bold">
          <button
            onClick={() => setActivePortal('Tenant')}
            className={`py-3 px-4 rounded-xl flex items-center justify-center gap-2 transition-all ${
              activePortal === 'Tenant'
                ? 'bg-blue-600 text-white shadow-lg shadow-blue-500/30'
                : 'text-slate-400 hover:text-white'
            }`}
          >
            <Building2 size={16} />
            <span>Tenant Workspace</span>
          </button>
          <button
            onClick={() => setActivePortal('Admin')}
            className={`py-3 px-4 rounded-xl flex items-center justify-center gap-2 transition-all ${
              activePortal === 'Admin'
                ? 'bg-purple-600 text-white shadow-lg shadow-purple-500/30'
                : 'text-slate-400 hover:text-white'
            }`}
          >
            <Shield size={16} />
            <span>Platform Admin</span>
          </button>
        </div>

        {/* Portal 1: Tenant Login */}
        {activePortal === 'Tenant' && (
          <div className="space-y-5 animate-fadeIn">
            <div className="p-3 bg-blue-500/10 border border-blue-500/20 rounded-2xl flex items-start gap-3 text-xs text-blue-300">
              <Sparkles size={18} className="mt-0.5 text-blue-400 shrink-0" />
              <div>
                <strong>Tenant Self-Service Portal</strong>: Manage your private workflows, launch instances, inspect event payloads, and generate application API keys in total isolation.
              </div>
            </div>

            {/* Quick Demo Login Preset */}
            <div>
              <div className="text-xs font-semibold text-slate-400 mb-2">Quick 1-Click Demo Login:</div>
              <button
                onClick={() => handleTenantLogin('22222222-2222-2222-2222-222222222222', 'Demo Client Tenant')}
                className="w-full py-2.5 px-4 bg-slate-800 hover:bg-slate-750 border border-slate-700 rounded-xl text-xs font-semibold text-slate-200 flex items-center justify-between transition-colors group"
              >
                <div className="flex items-center gap-2.5">
                  <span className="w-2.5 h-2.5 rounded-full bg-emerald-400"></span>
                  <div className="text-left">
                    <div className="text-white font-bold">Demo Client Tenant</div>
                    <div className="text-[10px] text-slate-500 font-mono">22222222-2222-2222-2222-222222222222</div>
                  </div>
                </div>
                <ArrowRight size={14} className="text-slate-500 group-hover:text-blue-400 group-hover:translate-x-0.5 transition-all" />
              </button>
            </div>

            <div className="relative flex py-1 items-center">
              <div className="flex-grow border-t border-slate-800"></div>
              <span className="flex-shrink mx-4 text-[11px] text-slate-600 uppercase font-semibold">Or Select Tenant</span>
              <div className="flex-grow border-t border-slate-800"></div>
            </div>

            {/* Select from registered tenants */}
            {!useCustomTenant ? (
              <div className="space-y-1.5">
                <label className="text-xs font-medium text-slate-300 flex justify-between">
                  <span>Registered Tenant {loadingTenants ? '(Loading...)' : ''}</span>
                  <button 
                    onClick={() => setUseCustomTenant(true)}
                    className="text-[11px] text-blue-400 hover:underline"
                  >
                    Enter custom UUID
                  </button>
                </label>
                <select
                  value={selectedTenantId}
                  onChange={e => setSelectedTenantId(e.target.value)}
                  className="w-full bg-slate-950 border border-slate-700 rounded-xl px-3.5 py-2.5 text-xs text-white focus:outline-none focus:border-blue-500"
                >
                  {tenants.map(t => (
                    <option key={t.tenantId} value={t.tenantId}>
                      {t.name} ({t.tenantId.substring(0, 8)}...)
                    </option>
                  ))}
                  {tenants.length === 0 && (
                    <option value="22222222-2222-2222-2222-222222222222">Demo Client Tenant (22222222...)</option>
                  )}
                </select>
              </div>
            ) : (
              <div className="space-y-1.5">
                <label className="text-xs font-medium text-slate-300 flex justify-between">
                  <span>Custom Tenant UUID</span>
                  <button 
                    onClick={() => setUseCustomTenant(false)}
                    className="text-[11px] text-blue-400 hover:underline"
                  >
                    Select registered tenant
                  </button>
                </label>
                <input
                  type="text"
                  placeholder="e.g. 33333333-3333-3333-3333-333333333333"
                  value={customTenantId}
                  onChange={e => setCustomTenantId(e.target.value)}
                  className="w-full bg-slate-950 border border-slate-700 rounded-xl px-3.5 py-2.5 text-xs text-white font-mono focus:outline-none focus:border-blue-500"
                />
              </div>
            )}

            {/* API Key Input */}
            <div className="space-y-1.5">
              <label className="text-xs font-medium text-slate-300 flex items-center justify-between">
                <span className="flex items-center gap-1.5">
                  <Key size={13} className="text-amber-400" />
                  <span>Tenant Application API Key (Optional)</span>
                </span>
                <span className="text-[10px] text-slate-500">Auto-injects X-API-Key</span>
              </label>
              <input
                type="text"
                placeholder="flowos_live_..."
                value={apiKey}
                onChange={e => setApiKey(e.target.value)}
                className="w-full bg-slate-950 border border-slate-700 rounded-xl px-3.5 py-2.5 text-xs text-slate-200 font-mono focus:outline-none focus:border-blue-500"
              />
            </div>

            <button
              onClick={() => handleTenantLogin()}
              className="w-full py-3 bg-blue-600 hover:bg-blue-500 text-white font-semibold rounded-xl shadow-lg shadow-blue-500/25 transition-all flex items-center justify-center gap-2 text-sm mt-2"
            >
              <span>Enter Tenant Workspace</span>
              <ArrowRight size={16} />
            </button>
          </div>
        )}

        {/* Portal 2: Platform Admin Login */}
        {activePortal === 'Admin' && (
          <div className="space-y-5 animate-fadeIn">
            <div className="p-3 bg-purple-500/10 border border-purple-500/20 rounded-2xl flex items-start gap-3 text-xs text-purple-300">
              <Shield size={18} className="mt-0.5 text-purple-400 shrink-0" />
              <div>
                <strong>Platform Governance Plane (SuperAdmin)</strong>: Multi-tenant fleet management, global review queue for public workflows, and platform-wide security audits.
              </div>
            </div>

            <div className="space-y-1.5">
              <label className="text-xs font-medium text-slate-300">Admin Account</label>
              <div className="relative">
                <input
                  type="text"
                  value={adminUsername}
                  onChange={e => setAdminUsername(e.target.value)}
                  className="w-full bg-slate-950 border border-slate-700 rounded-xl px-3.5 py-2.5 text-xs text-white focus:outline-none focus:border-purple-500"
                />
              </div>
            </div>

            <div className="space-y-1.5">
              <label className="text-xs font-medium text-slate-300">Root Password</label>
              <div className="relative">
                <input
                  type="password"
                  value={adminPassword}
                  onChange={e => setAdminPassword(e.target.value)}
                  className="w-full bg-slate-950 border border-slate-700 rounded-xl px-3.5 py-2.5 text-xs text-white font-mono focus:outline-none focus:border-purple-500"
                />
              </div>
            </div>

            <button
              onClick={handleAdminLogin}
              className="w-full py-3 bg-purple-600 hover:bg-purple-500 text-white font-semibold rounded-xl shadow-lg shadow-purple-500/25 transition-all flex items-center justify-center gap-2 text-sm mt-2"
            >
              <Shield size={16} />
              <span>Enter Platform Governance Console</span>
              <ArrowRight size={16} />
            </button>

            <div className="text-center">
              <span className="text-[11px] text-slate-500">
                Authorized platform operators only. All operations are immutably logged to the kernel audit trail.
              </span>
            </div>
          </div>
        )}

      </div>

      {/* Footer Info */}
      <div className="mt-8 text-center text-xs text-slate-500 flex items-center gap-4">
        <span className="flex items-center gap-1.5">
          <Lock size={12} className="text-emerald-400" />
          Zero-Trust Tenant Boundary
        </span>
        <span>•</span>
        <span className="flex items-center gap-1.5">
          <Cpu size={12} className="text-blue-400" />
          Dual-Kernel State Machine
        </span>
        <span>•</span>
        <span className="flex items-center gap-1.5">
          <Terminal size={12} className="text-purple-400" />
          MCP Compliant
        </span>
      </div>

    </div>
  );
};
