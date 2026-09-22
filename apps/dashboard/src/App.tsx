import { useState } from 'react';
import { AuthSession } from './types';
import { getStoredSession, setAuthSession, clearAuthSession, getDefaultSandboxSession, getAuthSession } from './api/client';
import { LandingPage } from './components/LandingPage';
import { AuthModal, AuthModalMode } from './components/AuthModal';
import { TenantDashboard } from './components/TenantDashboard';
import { AdminDashboard } from './components/AdminDashboard';
import { McpAgentGuideline } from './components/McpAgentGuideline';
import { mcpRpcPath } from './mcpUrl';
import { 
  Shield, Building2, LogOut, Bot, Home, 
  Sparkles, CheckCircle2
} from 'lucide-react';

function App() {
  const mcpPath = mcpRpcPath();
  const [session, setSession] = useState<AuthSession>(() => setAuthSession(getAuthSession()));

  const [currentView, setCurrentView] = useState<'landing' | 'dashboard'>(() => {
    const stored = getStoredSession();
    return stored && !stored.isSandbox ? 'dashboard' : 'landing';
  });

  const [isAuthModalOpen, setIsAuthModalOpen] = useState(false);
  const [authModalMode, setAuthModalMode] = useState<AuthModalMode>('login');

  const openAuth = (mode: AuthModalMode) => {
    setAuthModalMode(mode);
    setIsAuthModalOpen(true);
  };

  const handleLaunchSandbox = () => {
    setSession(setAuthSession(getDefaultSandboxSession()));
    setCurrentView('dashboard');
  };

  const handleAuthSuccess = (newSession: AuthSession) => {
    const stored = setAuthSession(newSession);
    setSession(stored);
    setCurrentView('dashboard');
  };

  const handleSignOut = () => {
    clearAuthSession();
    setSession(getDefaultSandboxSession());
    setCurrentView('landing');
  };

  const handleSwitchToAdmin = () => {
    const sandbox = getDefaultSandboxSession();
    const apiKey = session.apiKey?.trim();
    const adminSession: AuthSession = {
      role: 'Admin',
      tenantId: apiKey ? session.tenantId : sandbox.tenantId,
      tenantName: 'Platform Administrator',
      username: 'superadmin@flowos.internal',
      apiKey: apiKey || sandbox.apiKey,
      token: apiKey ? session.token : undefined,
      isSandbox: apiKey ? session.isSandbox : true,
      isEmailVerified: true,
      plan: 'Enterprise',
      billingStatus: 'Active',
      canRunRuntime: true
    };
    setSession(setAuthSession(adminSession));
    setCurrentView('dashboard');
  };

  const handleSwitchToTenant = () => {
    const tenantSession: AuthSession = {
      role: 'Tenant',
      tenantId: '22222222-2222-2222-2222-222222222222',
      tenantName: 'Demo Client Tenant',
      apiKey: 'flowos_prod_secret_key_32_chars_min',
      username: 'demo-tenant-user',
      isSandbox: true,
      isEmailVerified: true
    };
    setSession(setAuthSession(tenantSession));
    setCurrentView('dashboard');
  };

  return (
    <div className="min-h-screen bg-slate-900 text-slate-100 font-sans selection:bg-blue-500 selection:text-white">
      
      {/* LANDING PAGE VIEW */}
      {currentView === 'landing' ? (
        <LandingPage 
          onOpenAuth={openAuth}
          onLaunchSandbox={handleLaunchSandbox}
        />
      ) : (
        /* DASHBOARD VIEW */
        <div className="min-h-screen flex flex-col">
          
          {/* Trial / unpaid runtime banner */}
          {!session.isSandbox && session.role === 'Tenant' && (session.plan === 'Trial' || session.billingStatus === 'Unpaid' || session.canRunRuntime === false) && (
            <div className="bg-gradient-to-r from-amber-900/90 via-slate-900 to-orange-900/90 border-b border-amber-500/40 px-6 py-2.5 text-xs text-amber-100 flex flex-wrap items-center justify-between gap-3 shadow-md z-50">
              <div className="flex items-center gap-2">
                <span className="flex h-2 w-2 rounded-full bg-amber-400 animate-pulse" />
                <span className="font-bold">Trial / unpaid tenant:</span>
                <span className="text-slate-200 hidden sm:inline">
                  Runtime execution is blocked (start, publish, complete). Design-time simulate, lint, and drafts still work. MCP is included once a Managed Cloud or Enterprise plan is activated.
                </span>
              </div>
              <a
                href="mailto:admin@flowosbd.com?subject=FlowOS%20Managed%20Cloud%20activation"
                className="px-3 py-1 bg-amber-500 hover:bg-amber-400 text-slate-950 font-bold rounded-lg transition-all shadow"
              >
                Contact admin@flowosbd.com
              </a>
            </div>
          )}

          {/* Guest / Sandbox Banner */}
          {session.isSandbox && (
            <div className="bg-gradient-to-r from-emerald-900/90 via-slate-900 to-blue-900/90 border-b border-emerald-500/40 px-6 py-2.5 text-xs text-emerald-200 flex flex-wrap items-center justify-between gap-3 shadow-md z-50">
              <div className="flex items-center gap-2">
                <span className="flex h-2 w-2 rounded-full bg-emerald-400 animate-pulse" />
                <span className="font-bold">🎮 Interactive Sandbox Playground:</span>
                <span className="text-slate-300 hidden sm:inline">
                  You are exploring FlowOS as an unregistered guest user. State transitions, workflows, and simulations are active.
                </span>
              </div>
              <div className="flex items-center gap-3">
                <button
                  onClick={() => openAuth('register')}
                  className="px-3 py-1 bg-emerald-500 hover:bg-emerald-400 text-slate-950 font-bold rounded-lg transition-all flex items-center gap-1.5 shadow"
                >
                  <Sparkles size={12} />
                  <span>Register Real Tenant</span>
                </button>
                <button
                  onClick={() => setCurrentView('landing')}
                  className="text-slate-400 hover:text-white underline text-[11px]"
                >
                  Exit Sandbox
                </button>
              </div>
            </div>
          )}

          {/* Top Navigation */}
          <nav className="border-b border-slate-800 bg-slate-900/90 backdrop-blur sticky top-0 z-40">
            <div className="max-w-7xl mx-auto px-6 h-16 flex items-center justify-between">
              
              <div className="flex items-center space-x-3">
                <button
                  onClick={() => setCurrentView('landing')}
                  className="relative w-10 h-10 rounded-xl flex items-center justify-center shadow-lg bg-gradient-to-br from-slate-950 to-cyan-950 border border-cyan-500/30 shadow-cyan-500/10 hover:scale-105 transition-all p-0.5"
                  title="Return to FlowOS Landing Page"
                >
                  <img
                    src="/brand/flowos-icon.png"
                    alt="FlowOS"
                    className="w-full h-full object-contain drop-shadow-md"
                  />
                  {session.role === 'Admin' && (
                    <span
                      aria-label="Platform administrator"
                      className="absolute -top-1.5 -right-1.5 text-[11px] leading-none"
                    >
                      👑
                    </span>
                  )}
                </button>
                <div>
                  <div className="flex items-center gap-2">
                    <span className="text-lg font-bold tracking-tight text-white">Flow<span className="text-blue-500">OS</span></span>
                    
                    {session.isSandbox ? (
                      <span className="px-2 py-0.5 text-[10px] font-bold rounded-full bg-emerald-500/20 text-emerald-300 border border-emerald-500/30 flex items-center gap-1">
                        <Sparkles size={10} />
                        <span>Sandbox Guest</span>
                      </span>
                    ) : (
                      <span className={`px-2 py-0.5 text-[10px] font-bold rounded-full border flex items-center gap-1 ${
                        session.role === 'Admin'
                          ? 'bg-purple-500/20 text-purple-300 border-purple-500/30'
                          : 'bg-blue-500/20 text-blue-300 border-blue-500/30'
                      }`}>
                        <CheckCircle2 size={10} className="text-blue-400" />
                        <span>{session.role === 'Admin' ? 'Platform Governance' : 'Live Verified Tenant'}</span>
                      </span>
                    )}
                  </div>
                  <div className="text-[10px] text-slate-400 flex items-center gap-1">
                    <span className="font-semibold text-slate-300">{session.tenantName}</span>
                    <span className="font-mono text-slate-500">({session.tenantId.substring(0, 8)}...)</span>
                  </div>
                </div>
              </div>

              <div className="flex items-center space-x-3">
                
                {/* Back to Homepage */}
                <button
                  onClick={() => setCurrentView('landing')}
                  className="hidden md:inline-flex px-3 py-1.5 text-xs font-medium text-slate-300 hover:text-white bg-slate-800/80 hover:bg-slate-750 border border-slate-700 rounded-xl transition-all items-center gap-1.5"
                  title="Back to Landing Page"
                >
                  <Home size={13} />
                  <span>Homepage</span>
                </button>

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

                {/* Sign Out / Switch Tenant */}
                <button
                  onClick={handleSignOut}
                  className="px-3 py-1.5 text-xs font-medium text-slate-300 hover:text-white bg-slate-800 hover:bg-slate-750 border border-slate-700 rounded-xl transition-colors flex items-center gap-1.5"
                  title="Sign out and return to landing page"
                >
                  <LogOut size={13} />
                  <span>{session.isSandbox ? 'Exit Sandbox' : 'Sign Out'}</span>
                </button>

                <a 
                  href={mcpPath} 
                  target="_blank" 
                  className="inline-flex px-3 py-1.5 text-xs font-semibold text-emerald-300 bg-emerald-500/10 hover:bg-emerald-500/20 border border-emerald-500/30 rounded-xl transition-all items-center gap-1.5 shadow-sm"
                  title="Access Model Context Protocol (MCP) tool discovery & catalog"
                >
                  <Bot size={13} />
                  <span>MCP Tools ↗</span>
                </a>

                <a 
                  href="/swagger" 
                  target="_blank" 
                  className="hidden sm:inline-flex px-3 py-1.5 text-xs font-medium text-slate-400 hover:text-white bg-slate-900 border border-slate-800 rounded-xl transition-all"
                >
                  Swagger ↗
                </a>
              </div>
            </div>
          </nav>

          {/* Main Dashboard Scope */}
          <main className="max-w-7xl mx-auto px-6 py-8 flex-1 w-full">
            {session.role === 'Admin' ? (
              <AdminDashboard 
                session={session} 
                onSwitchWorkspace={() => openAuth('login')} 
              />
            ) : (
              <TenantDashboard 
                session={session} 
                onSwitchWorkspace={() => openAuth('login')} 
                onTenantChange={(newTenantId, newTenantName) => {
                  setSession(setAuthSession({
                    ...session,
                    tenantId: newTenantId,
                    tenantName: newTenantName
                  }));
                }}
              />
            )}
          </main>

          {/* AI Agent & Browser Guideline Section */}
          <section className="max-w-7xl mx-auto px-6 mb-8 w-full">
            <McpAgentGuideline />
          </section>

          {/* Footer */}
          <footer className="py-8 border-t border-slate-800 text-center text-xs text-slate-500 bg-slate-950/50">
            <div className="max-w-7xl mx-auto px-6 flex flex-col sm:flex-row justify-between items-center gap-4">
              <div>© 2026 FlowOS — Prospect BD Ltd. Official system email: <a href="mailto:admin@flowosbd.com" className="text-blue-400 hover:underline">admin@flowosbd.com</a></div>
              <div className="flex space-x-6">
                <a href="/swagger" target="_blank" className="hover:underline">Swagger Docs</a>
                <a href={mcpPath} target="_blank" className="hover:underline">MCP Endpoint</a>
                <a href="https://github.com/ObaidulKabir/FlowOS" target="_blank" className="hover:underline">GitHub</a>
              </div>
            </div>
          </footer>

        </div>
      )}

      {/* Global Unified Auth Modal */}
      <AuthModal
        isOpen={isAuthModalOpen}
        initialMode={authModalMode}
        onClose={() => setIsAuthModalOpen(false)}
        onSuccess={handleAuthSuccess}
        onLaunchSandbox={handleLaunchSandbox}
      />

    </div>
  );
}

export default App;
