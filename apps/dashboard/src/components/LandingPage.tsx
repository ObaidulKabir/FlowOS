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
import { PRICING_METERS, PRICING_TIERS } from '../pricingLadder';

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
            <div className="w-12 h-12 rounded-2xl bg-gradient-to-br from-slate-900 to-cyan-950/70 border border-cyan-500/20 flex items-center justify-center shadow-lg shadow-cyan-500/10 p-0.5">
              <img
                src="/brand/flowos-icon.png"
                alt=""
                aria-hidden="true"
                className="w-full h-full object-contain drop-shadow-md"
              />
            </div>
            <div>
              <div className="flex items-center gap-2">
                <span className="text-xl font-extrabold tracking-tight text-white">
                  Flow<span className="text-blue-500">OS</span>
                </span>
                <span className="px-2 py-0.5 text-[10px] font-bold uppercase rounded-full bg-blue-500/15 text-blue-400 border border-blue-500/30">
                  Agent control plane
                </span>
              </div>
              <div className="text-[11px] text-slate-400">
                Law · Work · Truth
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
        <img
          src="/brand/flowos-logo.png"
          alt="FlowOS"
          className="h-24 sm:h-28 w-auto object-contain mx-auto mb-5 drop-shadow-[0_18px_35px_rgba(34,211,238,0.16)]"
        />
        
        {/* Status Pill */}
        <div className="inline-flex items-center gap-2 px-3.5 py-1.5 rounded-full bg-slate-900/90 border border-slate-800 text-xs font-medium text-slate-300 mb-6 shadow-sm">
          <span className="flex h-2 w-2 rounded-full bg-emerald-400 animate-pulse" />
          <span className="text-emerald-400 font-bold">FlowOS:</span>
          <span>Design, validate, simulate, execute, recover, and govern</span>
        </div>

        {/* Hero Title */}
        <h1 className="text-4xl sm:text-5xl lg:text-6xl font-extrabold tracking-tight text-white max-w-4xl mx-auto leading-[1.15]">
          Build reliable workflows with{' '}
          <span className="bg-gradient-to-r from-blue-400 via-indigo-300 to-purple-400 bg-clip-text text-transparent">
            your AI agent
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
          Design, validate, simulate, execute, recover, and govern workflows through MCP.
          {mcpTools} capabilities cover that lifecycle. The dual-kernel split — state authority plus workflow orchestration — is how FlowOS keeps an agent inside what the system allows.
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
            <span>Law, Work, and Truth</span>
          </div>
          <h2 className="text-2xl sm:text-3xl font-extrabold text-white">
            The agent decides. FlowOS decides what is allowed.
          </h2>
          <p className="text-sm text-slate-400 max-w-2xl mx-auto mt-2">
            State is Law, the workflow is Work, and the event log is Truth. Under the hood that split is a dual-kernel architecture: state authority stays separate from step orchestration.
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
        "x-tenant-id": "<YOUR_TENANT_ID>",
        "X-MCP-API-Key": "<YOUR_MCP_API_KEY>",
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
            Detailed OS-1 comparison against Temporal.io, Camunda 8, AWS Step Functions, Conductor/Orkes, and n8n/Zapier. Native kernel vs custom work vs not that product.
          </p>
        </div>

        <PlatformComparison />
      </section>

      {/* Pricing Matrix */}
      <section id="pricing" className="py-16 px-6 max-w-7xl mx-auto border-t border-slate-900">
        <div className="text-center mb-10">
          <h2 className="text-2xl sm:text-3xl font-extrabold text-white">
            Try, build, then grow with usage
          </h2>
          <p className="text-sm text-slate-400 max-w-2xl mx-auto mt-2">
            Free → Starter → Builder → Team → Growth → Scale → Enterprise.
            You pay for publications, events, and generous MCP calls — not seats first, and not every retry or simulation.
          </p>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-10">
          {PRICING_METERS.map(meter => (
            <div key={meter.title} className="bg-slate-900/80 border border-slate-800 rounded-2xl p-5">
              <div className="text-[10px] font-bold uppercase tracking-wider text-emerald-400 mb-1">{meter.weight}</div>
              <h3 className="text-sm font-bold text-white mb-2">{meter.title}</h3>
              <p className="text-xs text-slate-400 leading-relaxed">{meter.body}</p>
            </div>
          ))}
        </div>

        <div className="grid grid-cols-1 md:grid-cols-3 gap-5 mb-10 text-xs">
          <div className="bg-slate-900 border border-slate-800 p-6 rounded-3xl flex flex-col justify-between">
            <div>
              <div className="inline-block px-2.5 py-1 bg-slate-800 rounded-lg text-slate-300 font-semibold mb-3">Explore</div>
              <h3 className="text-lg font-bold text-white mb-1">Free</h3>
              <div className="text-2xl font-extrabold text-white mb-4">
                $0 <span className="text-xs font-normal text-slate-400">/ month</span>
              </div>
              <ul className="space-y-2.5 text-slate-400 mb-6">
                <li className="flex items-center gap-2"><Check size={14} className="text-emerald-400 shrink-0" /> Launch Live Sandbox with no signup</li>
                <li className="flex items-center gap-2"><Check size={14} className="text-emerald-400 shrink-0" /> 2 publications · 2.5K events · 2.5K MCP calls</li>
                <li className="flex items-center gap-2"><Check size={14} className="text-emerald-400 shrink-0" /> Simulate, lint, validate, and generate a first workflow</li>
                <li className="flex items-center gap-2"><Check size={14} className="text-emerald-400 shrink-0" /> Register stays design-time until a paid package is activated</li>
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

          <div className="bg-slate-900 border-2 border-blue-500 p-6 rounded-3xl flex flex-col justify-between relative shadow-2xl shadow-blue-500/10">
            <div className="absolute -top-3.5 right-6 bg-blue-600 text-white text-[10px] font-extrabold px-3 py-1 rounded-full uppercase tracking-wider shadow">
              Most popular
            </div>
            <div>
              <div className="inline-block px-2.5 py-1 bg-blue-500/20 text-blue-300 rounded-lg font-semibold mb-3">Ship</div>
              <h3 className="text-lg font-bold text-white mb-1">Builder</h3>
              <div className="text-2xl font-extrabold text-white mb-4">
                $29 <span className="text-xs font-normal text-slate-400">/ month</span>
              </div>
              <ul className="space-y-2.5 text-slate-300 mb-6">
                <li className="flex items-center gap-2"><Check size={14} className="text-emerald-400 shrink-0" /> 30 publications · 75K events · 75K MCP calls</li>
                <li className="flex items-center gap-2"><Check size={14} className="text-emerald-400 shrink-0" /> Simulation, replay, compensation, and DLQ included</li>
                <li className="flex items-center gap-2"><Check size={14} className="text-emerald-400 shrink-0" /> Soft overage, then a Team upgrade — not a hard cutoff</li>
                <li className="flex items-center gap-2"><Check size={14} className="text-emerald-400 shrink-0" /> Starter is $9 if you only need a first real product</li>
              </ul>
            </div>
            <button
              onClick={() => onOpenAuth('register')}
              className="w-full py-3 bg-gradient-to-r from-blue-600 to-indigo-600 hover:from-blue-500 hover:to-indigo-500 text-white font-bold rounded-xl text-center transition-all shadow-lg shadow-blue-500/25 flex items-center justify-center gap-1.5"
            >
              <span>Register and request Builder</span>
              <ArrowRight size={14} />
            </button>
          </div>

          <div className="bg-slate-900 border border-slate-800 p-6 rounded-3xl flex flex-col justify-between">
            <div>
              <div className="inline-block px-2.5 py-1 bg-purple-500/20 text-purple-300 rounded-lg font-semibold mb-3">Strategic</div>
              <h3 className="text-lg font-bold text-white mb-1">Enterprise</h3>
              <div className="text-2xl font-extrabold text-white mb-1">Custom</div>
              <p className="text-[11px] text-slate-500 mb-4">Typically $1,500–$5,000+ / month</p>
              <ul className="space-y-2.5 text-slate-400 mb-6">
                <li className="flex items-center gap-2"><Check size={14} className="text-emerald-400 shrink-0" /> Private or VPC deploy, SSO, custom retention</li>
                <li className="flex items-center gap-2"><Check size={14} className="text-emerald-400 shrink-0" /> Custom limits, MCP policies, and data residency</li>
                <li className="flex items-center gap-2"><Check size={14} className="text-emerald-400 shrink-0" /> Security review, SLA, and dedicated support</li>
                <li className="flex items-center gap-2"><Check size={14} className="text-emerald-400 shrink-0" /> Student / local developer plans via the same inbox</li>
              </ul>
            </div>
            <a
              href="mailto:admin@flowosbd.com?subject=FlowOS%20Enterprise%20or%20paid%20plan"
              className="w-full py-3 bg-slate-800 hover:bg-slate-700 text-white font-bold rounded-xl text-center transition-all flex items-center justify-center gap-1.5"
            >
              <Mail size={14} />
              <span>Talk through the right package</span>
            </a>
          </div>
        </div>

        <div className="overflow-x-auto border border-slate-800 rounded-2xl bg-slate-950/60">
          <table className="w-full min-w-[860px] text-left text-[11px] text-slate-300">
            <thead>
              <tr className="border-b border-slate-800 text-slate-500 uppercase tracking-wider">
                <th className="px-4 py-3 font-semibold">Package</th>
                {PRICING_TIERS.map(tier => (
                  <th key={tier.id} className={`px-3 py-3 font-semibold ${tier.highlight ? 'text-blue-300' : 'text-white'}`}>
                    {tier.name}
                    <div className="normal-case tracking-normal text-[10px] text-slate-500 font-normal">{tier.stage}</div>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              <tr className="border-b border-slate-800/80">
                <td className="px-4 py-2.5 text-slate-500">Price</td>
                {PRICING_TIERS.map(tier => (
                  <td key={tier.id} className="px-3 py-2.5">
                    <div className="font-bold text-white">{tier.price}</div>
                    <div className="text-[10px] text-slate-500">{tier.priceNote}</div>
                  </td>
                ))}
              </tr>
              {[
                ['Publications', 'publications'],
                ['Events / month', 'events'],
                ['MCP calls / month', 'mcpCalls'],
                ['Active workflows', 'activeWorkflows'],
                ['Projects', 'projects'],
                ['Concurrent runs', 'concurrency'],
                ['Event retention', 'retention'],
                ['Team members', 'members'],
                ['Support', 'support']
              ].map(([label, key]) => (
                <tr key={key} className="border-b border-slate-800/60 last:border-0">
                  <td className="px-4 py-2 text-slate-500">{label}</td>
                  {PRICING_TIERS.map(tier => (
                    <td key={tier.id} className="px-3 py-2">{tier[key as keyof typeof tier]}</td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <p className="text-[11px] text-slate-500 mt-4 max-w-4xl leading-relaxed">
          Simulation, recovery, and MCP stay on every package. Higher tiers add volume, retention, concurrency, collaboration, and support — not the core product.
          Annual billing is two months free. Soft overage is the intended path past a limit; we will not charge per retry, per simulation, or per transition.
          Register is Free (design-time). A platform Admin activates Starter through Scale as a paid runtime plan, or Enterprise for custom terms, until self-serve checkout and the usage dashboard ship.
          Students and local developers can request a modest ৳ plan through the same inbox — an acquisition path, not a permanently cheaper edition.
        </p>
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
