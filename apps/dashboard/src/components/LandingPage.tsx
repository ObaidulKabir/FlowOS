import React from 'react';
import { 
  Building2, Shield, Bot, ArrowRight, Sparkles, 
  Terminal, Cpu, Layers, Check, Code2, Mail
} from 'lucide-react';
import { PlatformComparison } from './PlatformComparison';
import { CapabilitiesShowcase } from './CapabilitiesShowcase';
import { AuthModalMode } from './AuthModal';
import { usePlatformMetrics } from '../platformMetrics';
import { mcpRpcUrl, mcpRpcPath } from '../mcpUrl';

interface LandingPageProps {
  onOpenAuth: (mode: AuthModalMode) => void;
  onLaunchSandbox: () => void;
}

export const LandingPage: React.FC<LandingPageProps> = ({
  onOpenAuth,
  onLaunchSandbox
}) => {
  const { mcpTools, isLiveMcpCount, tests, verifiedOn } = usePlatformMetrics();
  const mcpUrl = mcpRpcUrl();
  const mcpPath = mcpRpcPath();

  return (
    <div className="min-h-screen bg-slate-950 text-slate-100 font-sans selection:bg-blue-500 selection:text-white relative overflow-hidden">
      
      {/* Background ambient lighting */}
      <div className="absolute top-0 left-1/2 -translate-x-1/2 w-[1000px] h-[550px] bg-gradient-to-b from-blue-600/15 via-indigo-600/10 to-transparent blur-[140px] pointer-events-none rounded-full" />
      <div className="absolute top-[800px] right-0 w-[500px] h-[500px] bg-purple-600/10 blur-[140px] pointer-events-none rounded-full" />

      {/* Top Navigation */}
      <nav className="border-b border-slate-800/80 bg-slate-950/85 backdrop-blur-md sticky top-0 z-40">
        <div className="max-w-7xl mx-auto px-6 h-18 flex items-center justify-between">
          
          {/* Logo */}
          <div className="flex items-center space-x-3">
            <div className="w-10 h-10 rounded-2xl bg-gradient-to-tr from-blue-600 to-indigo-600 flex items-center justify-center font-bold text-white shadow-lg shadow-blue-500/25">
              F
            </div>
            <div>
              <div className="flex items-center gap-2">
                <span className="text-xl font-extrabold tracking-tight text-white">
                  Flow<span className="text-blue-500">OS</span>
                </span>
                <span className="px-2 py-0.5 text-[10px] font-bold uppercase rounded-full bg-blue-500/15 text-blue-400 border border-blue-500/30">
                  Dual-Kernel Engine
                </span>
              </div>
              <div className="text-[11px] text-slate-400">
                Separating State from Execution
              </div>
            </div>
          </div>

          {/* Center Links */}
          <div className="hidden lg:flex items-center space-x-7 text-xs font-semibold text-slate-300">
            <a href="#architecture" className="hover:text-white transition-colors">Architecture</a>
            <a href="#capabilities" className="hover:text-white transition-colors">Capabilities</a>
            <a href="#mcp" className="hover:text-white transition-colors flex items-center gap-1.5">
              <span className="w-1.5 h-1.5 rounded-full bg-emerald-400"></span>
              <span>MCP AI Control Plane</span>
            </a>
            <a href="#comparison" className="hover:text-white transition-colors">Compare Platforms</a>
            <a href="#pricing" className="hover:text-white transition-colors">Pricing</a>
          </div>

          {/* Right Action CTAs */}
          <div className="flex items-center space-x-3">
            {/* 1-Click Sandbox Guest Mode */}
            <button
              onClick={onLaunchSandbox}
              className="px-3.5 py-2 text-xs font-bold text-emerald-300 bg-emerald-500/10 hover:bg-emerald-500/20 border border-emerald-500/30 rounded-xl transition-all flex items-center gap-1.5 shadow-sm hover:scale-[1.02]"
              title="Launch instant sandbox playground without registration"
            >
              <Sparkles size={14} className="text-emerald-400" />
              <span className="hidden sm:inline">Try Demo /</span> Sandbox
            </button>

            {/* Explicit Verify Email */}
            <button
              onClick={() => onOpenAuth('verify')}
              className="px-3.5 py-2 text-xs font-semibold text-cyan-300 hover:text-white bg-cyan-950/60 hover:bg-cyan-900/60 border border-cyan-700/60 rounded-xl transition-all flex items-center gap-1.5"
              title="Verify unverified account or enter code"
            >
              <Mail size={13} className="text-cyan-400" />
              <span>Verify Email</span>
            </button>

            {/* Tenant Sign In */}
            <button
              onClick={() => onOpenAuth('login')}
              className="px-3.5 py-2 text-xs font-semibold text-slate-200 hover:text-white bg-slate-900 hover:bg-slate-800 border border-slate-700 rounded-xl transition-all"
            >
              Sign In
            </button>

            {/* Register Tenant Workspace */}
            <button
              onClick={() => onOpenAuth('register')}
              className="px-4 py-2 text-xs font-bold text-white bg-gradient-to-r from-blue-600 to-indigo-600 hover:from-blue-500 hover:to-indigo-500 rounded-xl shadow-lg shadow-blue-500/25 transition-all flex items-center gap-1.5"
            >
              <span>Register Tenant</span>
              <ArrowRight size={14} />
            </button>
          </div>

        </div>
      </nav>

      {/* Hero Section */}
      <header className="relative pt-16 pb-20 px-6 max-w-7xl mx-auto text-center z-10">
        
        {/* Status Pill */}
        <div className="inline-flex items-center gap-2 px-3.5 py-1.5 rounded-full bg-slate-900/90 border border-slate-800 text-xs font-medium text-slate-300 mb-6 shadow-sm">
          <span className="flex h-2 w-2 rounded-full bg-emerald-400 animate-pulse" />
          <span className="text-emerald-400 font-bold">FlowOS 2.0 Live:</span>
          <span>Native {mcpTools}-Tool MCP Server & Verified Multi-Tenant Mesh</span>
        </div>

        {/* Hero Title */}
        <h1 className="text-4xl sm:text-5xl lg:text-6xl font-extrabold tracking-tight text-white max-w-4xl mx-auto leading-[1.15]">
          The Dual-Kernel Orchestration Platform for{' '}
          <span className="bg-gradient-to-r from-blue-400 via-indigo-300 to-purple-400 bg-clip-text text-transparent">
            Autonomous AI & Core Workflows
          </span>
        </h1>

        {/* Axiom Motto */}
        <div className="mt-4 flex items-center justify-center gap-2 sm:gap-4 flex-wrap text-sm sm:text-base font-semibold text-slate-300">
          <span className="text-blue-400">⚖️ State Machine = Law</span>
          <span className="text-slate-600">•</span>
          <span className="text-purple-400">⚙️ Workflow = Work</span>
          <span className="text-slate-600">•</span>
          <span className="text-emerald-400">📜 Event = Truth</span>
        </div>

        {/* Subtitle */}
        <p className="mt-5 text-base sm:text-lg text-slate-400 max-w-2xl mx-auto leading-relaxed">
          FlowOS separates mathematical finite state machine invariants from declarative step execution. 
          Equipped with zero-trust multi-tenancy, transactional event outboxes, and a native Model Context Protocol (MCP) control plane for Claude, Cursor, and autonomous agents.
        </p>

        {/* Primary CTAs */}
        <div className="mt-9 flex flex-col sm:flex-row items-center justify-center gap-3.5 max-w-xl mx-auto">
          
          {/* Instant Sandbox Button */}
          <button
            onClick={onLaunchSandbox}
            className="w-full sm:w-auto px-7 py-3.5 bg-gradient-to-r from-emerald-600 to-teal-600 hover:from-emerald-500 hover:to-teal-500 text-white font-bold rounded-2xl shadow-xl shadow-emerald-500/20 hover:scale-[1.02] transition-all flex items-center justify-center gap-2 text-sm"
          >
            <Sparkles size={18} />
            <span>Launch Live Sandbox (No Signup)</span>
            <ArrowRight size={16} />
          </button>

          {/* Real Tenant Registration Button */}
          <button
            onClick={() => onOpenAuth('register')}
            className="w-full sm:w-auto px-6 py-3.5 bg-slate-900 hover:bg-slate-850 text-white font-bold rounded-2xl border border-slate-700 hover:border-blue-500/50 transition-all flex items-center justify-center gap-2 text-sm"
          >
            <Building2 size={18} className="text-blue-400" />
            <span>Register Tenant Workspace</span>
          </button>

          {/* Connect MCP Button */}
          <a
            href={mcpUrl}
            target="_blank"
            className="w-full sm:w-auto px-5 py-3.5 bg-slate-900 hover:bg-slate-850 text-slate-300 hover:text-white font-semibold rounded-2xl border border-slate-800 transition-all flex items-center justify-center gap-2 text-sm"
          >
            <Bot size={18} className="text-purple-400" />
            <span>Connect MCP ↗</span>
          </a>

        </div>

        {/* Hero Features Bar */}
        <div className="mt-14 grid grid-cols-2 md:grid-cols-3 lg:grid-cols-5 gap-4 max-w-6xl mx-auto text-left">
          <div className="bg-slate-900/60 border border-slate-800 p-4 rounded-2xl">
            <div className="text-xs font-bold text-blue-400 uppercase tracking-wider mb-1">State Engine</div>
            <div className="text-white font-bold text-sm">Mathematical Invariants</div>
            <div className="text-[11px] text-slate-400 mt-0.5">Zero illegal state transitions guaranteed.</div>
          </div>
          <div className="bg-slate-900/60 border border-slate-800 p-4 rounded-2xl">
            <div className="text-xs font-bold text-purple-400 uppercase tracking-wider mb-1">AI Control Plane</div>
            <div className="text-white font-bold text-sm">{mcpTools} MCP Tools Registered</div>
            <div className="text-[11px] text-slate-400 mt-0.5">
              {isLiveMcpCount ? 'Live discovery' : 'Verified registry'} • JSON-RPC 2.0
            </div>
          </div>
          <div className="bg-slate-900/60 border border-slate-800 p-4 rounded-2xl">
            <div className="text-xs font-bold text-emerald-400 uppercase tracking-wider mb-1">Audit Trail</div>
            <div className="text-white font-bold text-sm">Transactional PostgreSQL</div>
            <div className="text-[11px] text-slate-400 mt-0.5">Microsecond time-travel & replay.</div>
          </div>
          <div className="bg-slate-900/60 border border-slate-800 p-4 rounded-2xl">
            <div className="text-xs font-bold text-amber-400 uppercase tracking-wider mb-1">Multi-Tenancy</div>
            <div className="text-white font-bold text-sm">Strict Zero-Trust RBAC</div>
            <div className="text-[11px] text-slate-400 mt-0.5">Isolated UUID partitions & API keys.</div>
          </div>
          <div
            className="bg-slate-900/60 border border-slate-800 p-4 rounded-2xl"
            title={`Verified ${verifiedOn}`}
          >
            <div className="text-xs font-bold text-cyan-400 uppercase tracking-wider mb-1">Release Quality</div>
            <div className="text-white font-bold text-sm">{tests.total} Tests Passing</div>
            <div className="text-[11px] text-slate-400 mt-0.5">
              {tests.unit} unit • {tests.endToEnd} E2E • {tests.mcp} MCP
            </div>
          </div>
        </div>

      </header>

      {/* Architecture Axioms Section */}
      <section id="architecture" className="py-16 px-6 max-w-7xl mx-auto border-t border-slate-900">
        <div className="text-center mb-12">
          <div className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full bg-blue-500/10 border border-blue-500/30 text-blue-300 text-xs font-semibold mb-3">
            <Layers size={13} />
            <span>Dual-Kernel Core Axioms</span>
          </div>
          <h2 className="text-2xl sm:text-3xl font-extrabold text-white">
            Engineered to Eliminate Race Conditions & Orphaned Steps
          </h2>
          <p className="text-sm text-slate-400 max-w-2xl mx-auto mt-2">
            Traditional workflow orchestrators blur the boundary between business state and step execution. FlowOS strictly decouples them into two orthogonal kernels.
          </p>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
          
          {/* Axiom 1 */}
          <div className="bg-slate-900/80 border border-slate-800 p-7 rounded-3xl relative overflow-hidden group hover:border-blue-500/40 transition-all">
            <div className="w-12 h-12 rounded-2xl bg-blue-500/15 border border-blue-500/30 flex items-center justify-center text-blue-400 mb-5">
              <Shield size={24} />
            </div>
            <h3 className="text-lg font-bold text-white mb-2">1. State Machine = Law</h3>
            <p className="text-xs text-slate-400 leading-relaxed">
              No worker, API call, or AI agent can alter entity state without formal mathematical FSM verification. Transitions are validated against strict guard conditions and state invariance matrices.
            </p>
            <div className="mt-5 pt-4 border-t border-slate-800/80 text-[11px] text-blue-300 font-mono">
              ✓ Invariant check before mutation<br />
              ✓ Zero unvalidated state changes<br />
              ✓ Deterministic replayability
            </div>
          </div>

          {/* Axiom 2 */}
          <div className="bg-slate-900/80 border border-slate-800 p-7 rounded-3xl relative overflow-hidden group hover:border-purple-500/40 transition-all">
            <div className="w-12 h-12 rounded-2xl bg-purple-500/15 border border-purple-500/30 flex items-center justify-center text-purple-400 mb-5">
              <Cpu size={24} />
            </div>
            <h3 className="text-lg font-bold text-white mb-2">2. Workflow = Work</h3>
            <p className="text-xs text-slate-400 leading-relaxed">
              Declarative, 100% JSON-defined blueprints executing multi-step business transactions. Built-in distributed saga patterns trigger compensating rollbacks on downstream faults.
            </p>
            <div className="mt-5 pt-4 border-t border-slate-800/80 text-[11px] text-purple-300 font-mono">
              ✓ Declarative JSON schema blueprints<br />
              ✓ Exponential backoff with jitter<br />
              ✓ OnFailure compensating transactions
            </div>
          </div>

          {/* Axiom 3 */}
          <div className="bg-slate-900/80 border border-slate-800 p-7 rounded-3xl relative overflow-hidden group hover:border-emerald-500/40 transition-all">
            <div className="w-12 h-12 rounded-2xl bg-emerald-500/15 border border-emerald-500/30 flex items-center justify-center text-emerald-400 mb-5">
              <Terminal size={24} />
            </div>
            <h3 className="text-lg font-bold text-white mb-2">3. Event = Truth</h3>
            <p className="text-xs text-slate-400 leading-relaxed">
              Every transition and step completion is committed to an immutable append-only PostgreSQL event log. Powers microsecond audit compliance and real-time SSE notification projections.
            </p>
            <div className="mt-5 pt-4 border-t border-slate-800/80 text-[11px] text-emerald-300 font-mono">
              ✓ Transactional outbox pattern<br />
              ✓ Microsecond event timestamps<br />
              ✓ Real-time SSE streaming updates
            </div>
          </div>

        </div>
      </section>

      {/* Model Context Protocol (MCP) Section */}
      <section id="mcp" className="py-16 px-6 max-w-7xl mx-auto border-t border-slate-900">
        <div className="bg-gradient-to-br from-indigo-950/50 via-slate-900 to-slate-950 border-2 border-indigo-500/30 rounded-3xl p-8 md:p-12 relative overflow-hidden">
          
          <div className="flex flex-col lg:flex-row items-start lg:items-center justify-between gap-8">
            <div className="max-w-2xl">
              <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-indigo-500/20 border border-indigo-500/40 text-indigo-300 text-xs font-semibold mb-3">
                <Bot size={14} />
                <span>AI Agent Control Plane</span>
              </div>
              <h2 className="text-2xl sm:text-3xl font-extrabold text-white">
                Native Model Context Protocol (MCP) Server
              </h2>
              <p className="text-sm text-slate-300 mt-3 leading-relaxed">
                Empower AI agents (Claude Desktop, Cursor, Custom LLM workers) to author, validate, execute, and troubleshoot enterprise workflows via {mcpTools} production tools over Streamable JSON-RPC 2.0.
              </p>

              <div className="mt-6 flex flex-wrap gap-3">
                <a
                  href={mcpUrl}
                  target="_blank"
                  className="px-4 py-2.5 bg-indigo-600 hover:bg-indigo-500 text-white font-bold rounded-xl text-xs flex items-center gap-1.5 shadow-lg shadow-indigo-500/20"
                >
                  <Bot size={14} />
                  <span>Open MCP Interactive Portal ({mcpPath}) ↗</span>
                </a>
                <a
                  href="/.well-known/mcp"
                  target="_blank"
                  className="px-4 py-2.5 bg-slate-800 hover:bg-slate-750 border border-slate-700 text-slate-200 font-semibold rounded-xl text-xs flex items-center gap-1.5"
                >
                  <Code2 size={14} />
                  <span>Discovery Manifest (/.well-known/mcp) ↗</span>
                </a>
              </div>
            </div>

            {/* MCP JSON-RPC snippet */}
            <div className="w-full lg:w-[420px] bg-slate-950 border border-slate-800 rounded-2xl p-4 font-mono text-[11px] text-slate-300 shadow-xl">
              <div className="flex items-center justify-between pb-2 mb-2 border-b border-slate-800 text-[10px] text-slate-500">
                <span>Claude / Cursor mcp.json config</span>
                <span className="text-emerald-400">Streamable HTTP</span>
              </div>
              <pre className="text-emerald-400 overflow-x-auto">
{`{
  "mcpServers": {
    "flowos": {
      "url": "${mcpUrl}",
      "headers": {
        "x-tenant-id": "YOUR_TENANT_ID",
        "X-MCP-API-Key": "YOUR_TENANT_API_KEY",
        "MCP-Protocol-Version": "2025-03-26"
      }
    }
  }
}`}
              </pre>
            </div>
          </div>

        </div>
      </section>

      {/* Enterprise Capabilities Showcase */}
      <section id="capabilities" className="py-16 px-6 max-w-7xl mx-auto border-t border-slate-900">
        <div className="text-center mb-12">
          <div className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full bg-emerald-500/10 border border-emerald-500/30 text-emerald-300 text-xs font-semibold mb-3">
            <Sparkles size={13} />
            <span>Mission-Critical Capabilities</span>
          </div>
          <h2 className="text-2xl sm:text-3xl font-extrabold text-white">
            Built for Extreme Reliability & Enterprise Integrations
          </h2>
          <p className="text-sm text-slate-400 max-w-2xl mx-auto mt-2">
            Explore the operational superpowers of FlowOS: from automated rollbacks and SLA timer escalations to communication plugins.
          </p>
        </div>

        <CapabilitiesShowcase />
      </section>

      {/* Instant Sandbox Teaser Banner */}
      <section className="py-12 px-6 max-w-7xl mx-auto">
        <div className="bg-gradient-to-r from-emerald-950/60 via-slate-900 to-blue-950/60 border border-emerald-500/30 rounded-3xl p-8 text-center sm:text-left flex flex-col sm:flex-row items-center justify-between gap-6 shadow-2xl">
          <div>
            <div className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full bg-emerald-500/20 text-emerald-300 text-[11px] font-bold uppercase mb-2">
              ⚡ Instant Zero-Registration Access
            </div>
            <h3 className="text-xl sm:text-2xl font-extrabold text-white">
              Experience the FlowOS Dashboard Playground in 1 Click
            </h3>
            <p className="text-xs sm:text-sm text-slate-300 mt-1 max-w-xl">
              No forms, no credit cards, no passwords. Launch directly into our sandboxed tenant workspace to simulate workflows, inspect the live execution graph, and test time-travel forks.
            </p>
          </div>

          <button
            onClick={onLaunchSandbox}
            className="shrink-0 px-7 py-3.5 bg-emerald-500 hover:bg-emerald-400 text-slate-950 font-extrabold rounded-2xl shadow-xl shadow-emerald-500/30 hover:scale-105 transition-all flex items-center gap-2 text-sm"
          >
            <Sparkles size={18} />
            <span>Enter Sandbox Playground ↗</span>
          </button>
        </div>
      </section>

      {/* Platform Comparison */}
      <section id="comparison" className="py-16 px-6 max-w-7xl mx-auto border-t border-slate-900">
        <div className="text-center mb-12">
          <div className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full bg-purple-500/10 border border-purple-500/30 text-purple-300 text-xs font-semibold mb-3">
            <Layers size={13} />
            <span>Platform Benchmark</span>
          </div>
          <h2 className="text-2xl sm:text-3xl font-extrabold text-white">
            FlowOS vs. Industry Orchestration Platforms
          </h2>
          <p className="text-sm text-slate-400 max-w-2xl mx-auto mt-2">
            Detailed comparison of architecture, state enforcement, AI readiness, and operational complexity against Temporal.io, Camunda 8, and AWS Step Functions.
          </p>
        </div>

        <PlatformComparison />
      </section>

      {/* Pricing Matrix */}
      <section id="pricing" className="py-16 px-6 max-w-7xl mx-auto border-t border-slate-900">
        <div className="text-center mb-12">
          <h2 className="text-2xl sm:text-3xl font-extrabold text-white">
            Simple, Transparent Pricing
          </h2>
          <p className="text-sm text-slate-400 max-w-2xl mx-auto mt-2">
            Charge a tenant subscription, not MCP usage. Playground is discovery and simulate. Register starts a design-time Trial. Managed Cloud $299/month includes the full MCP runtime with no per-call fee.
          </p>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-3 gap-6 max-w-5xl mx-auto text-xs">
          
          {/* Plan 1: Developer Sandbox */}
          <div className="bg-slate-900 border border-slate-800 p-6 rounded-3xl flex flex-col justify-between">
            <div>
              <div className="inline-block px-2.5 py-1 bg-slate-800 rounded-lg text-slate-300 font-semibold mb-3">
                Playground
              </div>
              <h3 className="text-lg font-bold text-white mb-1">Developer Sandbox</h3>
              <div className="text-2xl font-extrabold text-white mb-4">
                $0 <span className="text-xs font-normal text-slate-400">/ forever</span>
              </div>
              <ul className="space-y-2.5 text-slate-400 mb-6">
                <li className="flex items-center gap-2"><Check size={14} className="text-emerald-400 shrink-0" /> Guest sandbox dashboard, no signup</li>
                <li className="flex items-center gap-2"><Check size={14} className="text-emerald-400 shrink-0" /> Public GET discovery, initialize, tools/list</li>
                <li className="flex items-center gap-2"><Check size={14} className="text-emerald-400 shrink-0" /> Simulate, lint, and validate (no production keys)</li>
                <li className="flex items-center gap-2"><Check size={14} className="text-emerald-400 shrink-0" /> Time-Travel Fork Simulator</li>
              </ul>
            </div>
            <button
              onClick={onLaunchSandbox}
              className="w-full py-3 bg-slate-800 hover:bg-slate-700 text-emerald-300 font-bold rounded-xl text-center transition-all flex items-center justify-center gap-1.5"
            >
              <Sparkles size={14} />
              <span>Launch Free Sandbox</span>
            </button>
          </div>

          {/* Plan 2: Managed Cloud */}
          <div className="bg-slate-900 border-2 border-blue-500 p-6 rounded-3xl flex flex-col justify-between relative shadow-2xl shadow-blue-500/10">
            <div className="absolute -top-3.5 right-6 bg-blue-600 text-white text-[10px] font-extrabold px-3 py-1 rounded-full uppercase tracking-wider shadow">
              Most Popular
            </div>
            <div>
              <div className="inline-block px-2.5 py-1 bg-blue-500/20 text-blue-300 rounded-lg font-semibold mb-3">
                Production
              </div>
              <h3 className="text-lg font-bold text-white mb-1">Managed Cloud Tenant</h3>
              <div className="text-2xl font-extrabold text-white mb-4">
                $299 <span className="text-xs font-normal text-slate-400">/ month</span>
              </div>
              <ul className="space-y-2.5 text-slate-300 mb-6">
                <li className="flex items-center gap-2"><Check size={14} className="text-emerald-400 shrink-0" /> Dashboard, REST API, and tenant API keys</li>
                <li className="flex items-center gap-2"><Check size={14} className="text-emerald-400 shrink-0" /> Full MCP runtime included (start, publish, complete)</li>
                <li className="flex items-center gap-2"><Check size={14} className="text-emerald-400 shrink-0" /> No per-call or usage fees</li>
                <li className="flex items-center gap-2"><Check size={14} className="text-emerald-400 shrink-0" /> Official Email from admin@flowosbd.com</li>
                <li className="flex items-center gap-2"><Check size={14} className="text-emerald-400 shrink-0" /> 99.9% Uptime SLA & Zombie Recovery</li>
              </ul>
            </div>
            <button
              onClick={() => onOpenAuth('register')}
              className="w-full py-3 bg-gradient-to-r from-blue-600 to-indigo-600 hover:from-blue-500 hover:to-indigo-500 text-white font-bold rounded-xl text-center transition-all shadow-lg shadow-blue-500/25 flex items-center justify-center gap-1.5"
            >
              <span>Register Trial (design-only until activated)</span>
              <ArrowRight size={14} />
            </button>
          </div>

          {/* Plan 3: Enterprise Dedicated */}
          <div className="bg-slate-900 border border-slate-800 p-6 rounded-3xl flex flex-col justify-between">
            <div>
              <div className="inline-block px-2.5 py-1 bg-purple-500/20 text-purple-300 rounded-lg font-semibold mb-3">
                Custom
              </div>
              <h3 className="text-lg font-bold text-white mb-1">Enterprise Dedicated</h3>
              <div className="text-2xl font-extrabold text-white mb-4">Custom</div>
              <ul className="space-y-2.5 text-slate-400 mb-6">
                <li className="flex items-center gap-2"><Check size={14} className="text-emerald-400 shrink-0" /> Same MCP runtime entitlement as Managed Cloud</li>
                <li className="flex items-center gap-2"><Check size={14} className="text-emerald-400 shrink-0" /> Air-Gapped / VPC On-Premise Deployment</li>
                <li className="flex items-center gap-2"><Check size={14} className="text-emerald-400 shrink-0" /> Custom commercial terms via admin@flowosbd.com</li>
                <li className="flex items-center gap-2"><Check size={14} className="text-emerald-400 shrink-0" /> 24/7 Priority SLA & Dedicated Engineering</li>
              </ul>
            </div>
            <a
              href="mailto:admin@flowosbd.com?subject=FlowOS%20Enterprise%20Inquiry"
              className="w-full py-3 bg-slate-800 hover:bg-slate-700 text-white font-bold rounded-xl text-center transition-all flex items-center justify-center gap-1.5"
            >
              <Mail size={14} />
              <span>Contact Enterprise Sales</span>
            </a>
          </div>

        </div>
      </section>

      {/* Footer */}
      <footer className="py-12 border-t border-slate-800/80 bg-slate-950 text-xs text-slate-500">
        <div className="max-w-7xl mx-auto px-6 flex flex-col md:flex-row justify-between items-center gap-6">
          <div className="space-y-1 text-center md:text-left">
            <div className="text-slate-300 font-bold text-sm">
              FlowOS Orchestration Platform
            </div>
            <div>
              © 2026 FlowOS — Prospect BD Ltd. All rights reserved.
            </div>
            <div className="text-[11px] text-slate-400 flex items-center gap-1 justify-center md:justify-start">
              <span>Official System Email:</span>
              <a href="mailto:admin@flowosbd.com" className="text-blue-400 hover:underline">
                admin@flowosbd.com
              </a>
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-6">
            <button onClick={() => onOpenAuth('verify')} className="hover:text-cyan-400 text-slate-400 transition-colors">Verify Account Email</button>
            <a href={mcpUrl} target="_blank" className="hover:text-white transition-colors">MCP Portal</a>
            <a href="/.well-known/mcp" target="_blank" className="hover:text-white transition-colors">MCP Manifest</a>
            <a href="/swagger" target="_blank" className="hover:text-white transition-colors">Swagger API</a>
            <a href="/health" target="_blank" className="hover:text-white transition-colors">Health Endpoint</a>
            <a href="https://github.com/ObaidulKabir/FlowOS" target="_blank" className="hover:text-white transition-colors">GitHub</a>
          </div>
        </div>
      </footer>

    </div>
  );
};
