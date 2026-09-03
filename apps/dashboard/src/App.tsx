import { useState } from 'react';
import { AuthSession } from './types';
import { getAuthSession, setAuthSession } from './api/client';
import { LoginView } from './components/LoginView';
import { TenantDashboard } from './components/TenantDashboard';
import { AdminDashboard } from './components/AdminDashboard';
import { Shield, Building2, LogOut, CheckCircle } from 'lucide-react';

function App() {
  const [session, setSession] = useState<AuthSession>(getAuthSession());
  const [isLoggedOut, setIsLoggedOut] = useState<boolean>(false);

  const handleSwitchToAdmin = () => {
    const adminSession: AuthSession = {
      role: 'Admin',
      tenantId: '11111111-1111-1111-1111-111111111111',
      tenantName: 'Platform Administrator',
      username: 'superadmin@flowos.internal'
    };
    setAuthSession(adminSession);
    setSession(adminSession);
  };

  const handleSwitchToTenant = () => {
    const tenantSession: AuthSession = {
      role: 'Tenant',
      tenantId: '22222222-2222-2222-2222-222222222222',
      tenantName: 'Demo Client Tenant',
      apiKey: 'flowos_prod_secret_key_32_chars_min',
      username: 'demo-tenant-user'
    };
    setAuthSession(tenantSession);
    setSession(tenantSession);
  };

  const handleSignOut = () => {
    setIsLoggedOut(true);
  };

  if (isLoggedOut) {
    return (
      <LoginView 
        onLoginSuccess={(newSession) => {
          setSession(newSession);
          setIsLoggedOut(false);
        }} 
      />
    );
  }

  return (
    <div className="min-h-screen bg-slate-900 text-slate-100 font-sans selection:bg-blue-500 selection:text-white">
      
      {/* Top Navigation */}
      <nav className="border-b border-slate-800 bg-slate-900/90 backdrop-blur sticky top-0 z-50">
        <div className="max-w-7xl mx-auto px-6 h-16 flex items-center justify-between">
          <div className="flex items-center space-x-3">
            <div className={`w-9 h-9 rounded-xl flex items-center justify-center font-bold text-white shadow-lg ${
              session.role === 'Admin' ? 'bg-purple-600 shadow-purple-500/30' : 'bg-blue-600 shadow-blue-500/30'
            }`}>
              {session.role === 'Admin' ? '👑' : 'F'}
            </div>
            <div>
              <div className="flex items-center gap-2">
                <span className="text-lg font-bold tracking-tight text-white">Flow<span className="text-blue-500">OS</span></span>
                <span className={`px-2 py-0.5 text-[10px] font-bold rounded-full border ${
                  session.role === 'Admin'
                    ? 'bg-purple-500/20 text-purple-300 border-purple-500/30'
                    : 'bg-blue-500/20 text-blue-300 border-blue-500/30'
                }`}>
                  {session.role === 'Admin' ? 'Platform Governance Plane' : 'Tenant Workspace'}
                </span>
              </div>
              <div className="text-[10px] text-slate-400 flex items-center gap-1">
                <span>{session.tenantName}</span>
                <span className="font-mono text-slate-500">({session.tenantId.substring(0, 8)}...)</span>
              </div>
            </div>
          </div>

          <div className="flex items-center space-x-3">
            {session.role === 'Tenant' ? (
              <button
                onClick={handleSwitchToAdmin}
                className="px-3.5 py-1.5 text-xs font-semibold text-purple-300 bg-purple-500/10 hover:bg-purple-500/20 border border-purple-500/30 rounded-xl transition-all flex items-center gap-1.5"
                title="Switch to Platform Administrator control plane"
              >
                <Shield size={13} />
                <span className="hidden sm:inline">Switch to</span> Admin View
              </button>
            ) : (
              <button
                onClick={handleSwitchToTenant}
                className="px-3.5 py-1.5 text-xs font-semibold text-blue-300 bg-blue-500/10 hover:bg-blue-500/20 border border-blue-500/30 rounded-xl transition-all flex items-center gap-1.5"
                title="Switch to Tenant Workspace view"
              >
                <Building2 size={13} />
                <span className="hidden sm:inline">Switch to</span> Tenant View
              </button>
            )}

            <button
              onClick={handleSignOut}
              className="px-3 py-1.5 text-xs font-medium text-slate-300 hover:text-white bg-slate-800 hover:bg-slate-750 border border-slate-700 rounded-xl transition-colors flex items-center gap-1.5"
              title="Sign out or switch tenant"
            >
              <LogOut size={13} />
              <span>Switch User</span>
            </button>

            <a href="/swagger" target="_blank" className="hidden sm:inline-flex px-3 py-1.5 text-xs font-medium text-slate-400 hover:text-white bg-slate-900 border border-slate-800 rounded-xl transition-all">
              Swagger ↗
            </a>
          </div>
        </div>
      </nav>

      {/* Main Dashboard Scope */}
      <main className="max-w-7xl mx-auto px-6 py-8">
        {session.role === 'Admin' ? (
          <AdminDashboard 
            session={session} 
            onSwitchWorkspace={() => setIsLoggedOut(true)} 
          />
        ) : (
          <TenantDashboard 
            session={session} 
            onSwitchWorkspace={() => setIsLoggedOut(true)} 
            onTenantChange={(newTenantId, newTenantName) => {
              const updated: AuthSession = {
                ...session,
                tenantId: newTenantId,
                tenantName: newTenantName
              };
              setAuthSession(updated);
              setSession(updated);
            }}
          />
        )}
      </main>

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
                <td className="p-4 text-emerald-400 font-bold">✓ 21-Tool MCP Server</td>
                <td className="p-4 text-slate-400">Custom SDK Wrapper</td>
                <td className="p-4 text-slate-400">REST Connectors</td>
              </tr>
              <tr>
                <td className="p-4 font-semibold text-slate-200">Multi-Tenancy Isolation</td>
                <td className="p-4 text-emerald-400 font-bold">✓ Zero-Trust Isolation & API Keys</td>
                <td className="p-4 text-slate-400">Namespaces Only</td>
                <td className="p-4 text-slate-400">Tenant IDs in BPMN</td>
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
            <button onClick={() => setIsLoggedOut(true)} className="w-full py-2.5 bg-slate-700 hover:bg-slate-600 text-white font-semibold rounded-lg text-center transition-all">
              Try Sandbox
            </button>
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
