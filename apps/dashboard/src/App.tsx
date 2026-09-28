import { useEffect, useState } from 'react';
import { AuthSession } from './types';
import { getStoredSession, setAuthSession, clearAuthSession, getDefaultSandboxSession, getAuthSession } from './api/client';
import { LandingPage } from './components/LandingPage';
import { AuthModal, AuthModalMode } from './components/AuthModal';
import { TenantDashboard } from './components/TenantDashboard';
import { AdminDashboard } from './components/AdminDashboard';
import { mcpRpcUrl } from './mcpUrl';

function App() {
  const mcpUrl = mcpRpcUrl();
  const [session, setSession] = useState<AuthSession>(() => setAuthSession(getAuthSession()));

  const [currentView, setCurrentView] = useState<'landing' | 'dashboard'>(() => {
    const stored = getStoredSession();
    return stored && !stored.isSandbox ? 'dashboard' : 'landing';
  });

  const [isAuthModalOpen, setIsAuthModalOpen] = useState(false);
  const [authModalMode, setAuthModalMode] = useState<AuthModalMode>('login');

  useEffect(() => {
    window.scrollTo(0, 0);
  }, [currentView, session.role]);

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
        <div
          className="h-screen w-full flex flex-row flex-nowrap overflow-hidden"
          style={{ display: 'flex', flexDirection: 'row', height: '100vh' }}
        >
          {session.role === 'Admin' ? (
            <AdminDashboard
              session={session}
              onGoHome={() => setCurrentView('landing')}
              onSwitchRole={handleSwitchToTenant}
              onRegister={() => openAuth('register')}
              onSignOut={handleSignOut}
              mcpUrl={mcpUrl}
            />
          ) : (
            <TenantDashboard
              session={session}
              onSwitchWorkspace={() => openAuth('login')}
              onGoHome={() => setCurrentView('landing')}
              onSwitchRole={handleSwitchToAdmin}
              onRegister={() => openAuth('register')}
              onSignOut={handleSignOut}
              mcpUrl={mcpUrl}
              onTenantChange={(newTenantId, newTenantName) => {
                setSession(setAuthSession({
                  ...session,
                  tenantId: newTenantId,
                  tenantName: newTenantName
                }));
              }}
            />
          )}
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
