import { useEffect, useState } from 'react';
import { AuthSession } from './types';
import {
  getStoredSession, setAuthSession, clearAuthSession, getDefaultSandboxSession, getAuthSession,
  platformAdminHomeSession, onSessionExpired, takeSessionExpiredNotice
} from './api/client';
import { LandingPage } from './components/LandingPage';
import { AuthModal, AuthModalMode } from './components/AuthModal';
import { TenantDashboard } from './components/TenantDashboard';
import { AdminDashboard } from './components/AdminDashboard';
import { mcpRpcUrl } from './mcpUrl';

const ADMIN_SESSION_EXPIRED_NOTICE = 'Your platform admin session expired. Sign in again to continue.';
const ADMIN_SIGN_IN_NEEDED_NOTICE = 'The platform admin panel needs a current administrator sign-in. Sign in again to continue.';

function App() {
  const mcpUrl = mcpRpcUrl();
  const [session, setSession] = useState<AuthSession>(() => setAuthSession(getAuthSession()));

  const [currentView, setCurrentView] = useState<'landing' | 'dashboard'>(() => {
    const stored = getStoredSession();
    return stored && !stored.isSandbox ? 'dashboard' : 'landing';
  });

  const [isAuthModalOpen, setIsAuthModalOpen] = useState(false);
  const [authModalMode, setAuthModalMode] = useState<AuthModalMode>('login');
  const [authNotice, setAuthNotice] = useState<string | undefined>();

  useEffect(() => {
    window.scrollTo(0, 0);
    if (currentView === 'landing') {
      document.title = 'FlowOS — Build reliable workflows with your AI agent';
    } else if (session.role === 'Admin') {
      document.title = 'Platform Admin Dashboard — FlowOS';
    } else {
      document.title = `${session.tenantName || 'Tenant'} Workspace — FlowOS`;
    }
  }, [currentView, session.role, session.tenantName]);

  const openAuth = (mode: AuthModalMode, notice?: string) => {
    setAuthModalMode(mode);
    setAuthNotice(notice);
    setIsAuthModalOpen(true);
  };

  useEffect(() => {
    const handleAdminSessionExpired = () => {
      setSession(getAuthSession());
      setCurrentView('landing');
      setAuthModalMode('login');
      setAuthNotice(ADMIN_SESSION_EXPIRED_NOTICE);
      setIsAuthModalOpen(true);
    };
    if (takeSessionExpiredNotice()) handleAdminSessionExpired();
    return onSessionExpired(handleAdminSessionExpired);
  }, []);

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
    const adminSession = platformAdminHomeSession(session);
    if (!adminSession) {
      openAuth('login', ADMIN_SIGN_IN_NEEDED_NOTICE);
      return;
    }
    setSession(setAuthSession(adminSession));
    setCurrentView('dashboard');
  };

  const handleSwitchToTenant = (tenantContext?: { tenantId: string; tenantName?: string }) => {
    const current = getAuthSession();
    const sandbox = getDefaultSandboxSession();
    const hasPortableTenantContext = Boolean(current.token?.trim()) && current.isSandbox !== true;
    const hasNamedTenant = Boolean(current.tenantName) && current.tenantName !== 'Platform Administrator';
    const tenantName =
      tenantContext?.tenantName ||
      (hasNamedTenant
        ? current.tenantName
        : hasPortableTenantContext
          ? 'Platform tenant'
          : sandbox.tenantName);
    const tenantSession: AuthSession = {
      role: 'Tenant',
      tenantId: hasPortableTenantContext
        ? (tenantContext?.tenantId || current.tenantId || sandbox.tenantId)
        : sandbox.tenantId,
      tenantName,
      apiKey: hasPortableTenantContext ? current.apiKey : sandbox.apiKey,
      token: hasPortableTenantContext ? current.token : undefined,
      username: current.username || 'demo-tenant-user',
      isSandbox: hasPortableTenantContext ? current.isSandbox : true,
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
        notice={authNotice}
        onClose={() => { setIsAuthModalOpen(false); setAuthNotice(undefined); }}
        onSuccess={handleAuthSuccess}
        onLaunchSandbox={handleLaunchSandbox}
      />

    </div>
  );
}

export default App;
