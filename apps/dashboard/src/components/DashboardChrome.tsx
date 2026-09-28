import React from 'react';
import { AuthSession } from '../types';
import {
  Shield, Building2, LogOut, Bot, Home,
  Sparkles, CheckCircle2, FileCode
} from 'lucide-react';

interface DashboardChromeProps {
  session: AuthSession;
  onGoHome: () => void;
  onSwitchRole: () => void;
  onRegister: () => void;
  onSignOut: () => void;
  mcpUrl: string;
  nav: React.ReactNode;
}

export const sidebarItemClass = (active: boolean, activeClass = 'bg-blue-600 text-white') =>
  `w-full px-3 py-2 rounded-lg text-left text-xs font-semibold flex items-center gap-2 transition-colors ${
    active ? activeClass : 'text-slate-300 hover:bg-slate-800 hover:text-white'
  }`;

export const DashboardChrome: React.FC<DashboardChromeProps> = ({
  session,
  onGoHome,
  onSwitchRole,
  onRegister,
  onSignOut,
  mcpUrl,
  nav
}) => (
  <aside
    className="flowos-rail border-r border-slate-800 bg-slate-950 overflow-hidden"
    style={{
      position: 'fixed',
      top: 0,
      left: 0,
      zIndex: 40,
      width: '15rem',
      height: '100vh',
      display: 'flex',
      flexDirection: 'column',
    }}
  >
    <div className="px-4 py-4 border-b border-slate-800 shrink-0">
      <button
        onClick={onGoHome}
        className="flex items-center gap-3 w-full text-left"
        title="Return to FlowOS Landing Page"
      >
        <span className="relative w-10 h-10 rounded-xl flex items-center justify-center shadow-lg bg-gradient-to-br from-slate-950 to-cyan-950 border border-cyan-500/30 shadow-cyan-500/10 shrink-0">
          <img
            src="/brand/flowos-icon.png"
            alt="FlowOS"
            className="w-full h-full object-contain drop-shadow-md"
          />
          {session.role === 'Admin' && (
            <span aria-label="Platform administrator" className="absolute -top-1.5 -right-1.5 text-[11px] leading-none">👑</span>
          )}
        </span>
        <span>
          <span className="block text-lg font-bold tracking-tight text-white leading-none">Flow<span className="text-blue-500">OS</span></span>
          <span className="block text-[10px] text-slate-400 mt-1 truncate max-w-[9.5rem]">{session.tenantName}</span>
        </span>
      </button>
      <div className="mt-3">
        {session.isSandbox ? (
          <span className="px-2 py-0.5 text-[10px] font-bold rounded-full bg-emerald-500/20 text-emerald-300 border border-emerald-500/30 inline-flex items-center gap-1">
            <Sparkles size={10} />
            Sandbox Guest
          </span>
        ) : (
          <span className={`px-2 py-0.5 text-[10px] font-bold rounded-full border inline-flex items-center gap-1 ${
            session.role === 'Admin'
              ? 'bg-purple-500/20 text-purple-300 border-purple-500/30'
              : 'bg-blue-500/20 text-blue-300 border-blue-500/30'
          }`}>
            <CheckCircle2 size={10} />
            {session.role === 'Admin' ? 'Platform Governance' : 'Live Verified Tenant'}
          </span>
        )}
      </div>
    </div>

    <nav className="flex-1 px-3 py-3 space-y-4 overflow-y-auto min-h-0">
      {nav}
    </nav>

    <div className="px-3 py-3 border-t border-slate-800 space-y-1 shrink-0">
      <p className="px-2 pb-1 text-[10px] font-semibold uppercase tracking-wider text-slate-500">Account</p>
      <button
        onClick={onGoHome}
        className="w-full px-3 py-2 rounded-lg text-xs font-medium text-slate-300 hover:text-white hover:bg-slate-800 flex items-center gap-2"
      >
        <Home size={14} />
        Homepage
      </button>
      {session.role === 'Tenant' ? (
        <button
          onClick={onSwitchRole}
          className="w-full px-3 py-2 rounded-lg text-xs font-semibold text-purple-300 hover:bg-purple-500/15 flex items-center gap-2"
          title="Switch to Platform Administrator control plane"
        >
          <Shield size={14} />
          Admin View
        </button>
      ) : (
        <button
          onClick={onSwitchRole}
          className="w-full px-3 py-2 rounded-lg text-xs font-semibold text-blue-300 hover:bg-blue-500/15 flex items-center gap-2"
          title="Switch to Tenant Workspace view"
        >
          <Building2 size={14} />
          Tenant View
        </button>
      )}
      {session.isSandbox && (
        <button
          onClick={onRegister}
          className="w-full px-3 py-2 rounded-lg text-xs font-semibold text-emerald-300 hover:bg-emerald-500/15 flex items-center gap-2"
        >
          <Sparkles size={14} />
          Register Real Tenant
        </button>
      )}
      <a
        href={mcpUrl}
        target="_blank"
        className="w-full px-3 py-2 rounded-lg text-xs font-semibold text-emerald-300 hover:bg-emerald-500/15 flex items-center gap-2"
        title="Access Model Context Protocol (MCP) tool discovery & catalog"
      >
        <Bot size={14} />
        MCP Tools
      </a>
      <a
        href="/swagger"
        target="_blank"
        className="w-full px-3 py-2 rounded-lg text-xs font-medium text-slate-400 hover:text-white hover:bg-slate-800 flex items-center gap-2"
      >
        <FileCode size={14} />
        Swagger
      </a>
      <button
        onClick={onSignOut}
        className="w-full px-3 py-2 rounded-lg text-xs font-medium text-slate-300 hover:text-white hover:bg-slate-800 flex items-center gap-2"
        title="Sign out and return to landing page"
      >
        <LogOut size={14} />
        {session.isSandbox ? 'Exit Sandbox' : 'Sign Out'}
      </button>
    </div>
  </aside>
);
