import React, { useState } from 'react';
import { 
  Sparkles, RotateCcw, ShieldCheck, RefreshCw, 
  Clock, Cpu, FileCheck2, ArrowRight,
  Zap, CheckCircle2, ChevronRight, GitBranch
} from 'lucide-react';
import { usePlatformMetrics } from '../platformMetrics';

export const CapabilitiesShowcase: React.FC = () => {
  const { mcpTools, tests } = usePlatformMetrics();
  const [selectedPillar, setSelectedPillar] = useState<number>(0);

  const capabilities = [
    {
      id: 1,
      title: "Distributed Saga Orchestration & Rollback",
      badge: "Saga Pattern",
      badgeColor: "bg-rose-500/20 text-rose-300 border-rose-500/30",
      icon: RotateCcw,
      summary: "Guaranteed compensating actions without complex 2-phase commits (2PC).",
      situation: "Multi-service distributed transactions across payments, inventory, and logistics where middle steps can fail.",
      solution: "Declarative OnFailure hooks write rollback actions (refunds, lock releases, alerts) directly into the atomic transactional outbox during the fault transaction. Compensations execute with exponential retry even across crashes.",
      example: "Payment authorized ($500) -> Inventory reserved -> Shipping label generation fails -> Engine automatically triggers payment refund webhook and releases inventory hold without leaving orphaned resources.",
      tags: ["OnFailure Hooks", "Compensating Transactions", "Zero Dual-Write", "Transactional Outbox"]
    },
    {
      id: 2,
      title: "Bank-Grade Webhooks with Non-Repudiation",
      badge: "HMAC-SHA256",
      badgeColor: "bg-cyan-500/20 text-cyan-300 border-cyan-500/30",
      icon: ShieldCheck,
      summary: "Cryptographically signed event dispatching with per-tenant secret isolation.",
      situation: "Enterprise B2B integrations requiring strict proof of payload authenticity and anti-replay defense.",
      solution: "Automated HMAC-SHA256 signature injection (X-FlowOS-Signature: t={ts},v1={hash}) with anti-replay timestamp headers. Tenant secrets can be rotated independently without downtime.",
      example: "Loan underwriting workflow triggers partner banking ledger webhook with custom dynamic auth tokens. Partner verifies the cryptographic signature before executing funds transfer.",
      tags: ["HMAC-SHA256", "Anti-Replay", "Secret Rotation", "Custom HTTP Headers"]
    },
    {
      id: 3,
      title: "Downstream Outage Survival & Self-Healing DLQ",
      badge: "High Resilience",
      badgeColor: "bg-amber-500/20 text-amber-300 border-amber-500/30",
      icon: RefreshCw,
      summary: "Survives third-party API downtime with intelligent backoff and Dead Letter Queue.",
      situation: "External vendor APIs (Salesforce, Stripe, SAP) experience transient outages or rate-limiting (HTTP 429/503).",
      solution: "Configurable exponential backoff with jitter prevents thundering herd. Exhausted attempts gracefully land in the Dead Letter Queue (DLQ), where autonomous AI agents or engineers can diagnose and re-dispatch in 1-click.",
      example: "CRM sync webhook hits Salesforce during scheduled maintenance. FlowOS retries automatically over increasing intervals and alerts operators via DLQ without dropping customer events.",
      tags: ["Exponential Backoff", "Dead Letter Queue", "Jitter", "Self-Healing MCP"]
    },
    {
      id: 4,
      title: "Proactive Multi-Tier SLAs & Dynamic Timers",
      badge: "SLA Governance",
      badgeColor: "bg-emerald-500/20 text-emerald-300 border-emerald-500/30",
      icon: Clock,
      summary: "Multi-tier countdown warnings, payload-driven relative timers, and automatic cancellation.",
      situation: "High-value approvals and time-sensitive operations (CapEx approvals, appointments, patient check-ins) that risk stalling without proactive warnings.",
      solution: "Declarative SLAs combine hard timeout events with intermediate multi-tier countdown reminders (-24h, -2h). Standalone Timer steps compute dynamic UTC schedules from instance payload properties (e.g. appointmentDate - 24h). Completing the task atomically cancels all intermediate timer jobs in the database.",
      example: "High-priority contract approval: FlowOS alerts the manager 24 hours and 2 hours before deadline. If approved at 22 hours, the 2-hour reminder and 48-hour timeout jobs are cancelled atomically. If neglected, EVT-ESCALATE transfers the task to executive authority.",
      tags: ["Multi-Tier Reminders", "Payload-Driven Timers", "Atomic Timer Cancellation", "Deterministic Escalation"]
    },
    {
      id: 5,
      title: `Autonomous AI Agent Co-Pilot (MCP ${mcpTools} Tools)`,
      badge: "AI Native",
      badgeColor: "bg-purple-500/20 text-purple-300 border-purple-500/30",
      icon: Cpu,
      summary: "End-to-end workflow design, simulation, and self-healing via Model Context Protocol.",
      situation: "Operations teams wanting AI agents (Claude, ChatGPT) to design, validate, and troubleshoot enterprise workflows without manual YAML/JSON wrangling.",
      solution: `${mcpTools} registered MCP tools allowing LLM agents to attach lifecycle hooks, simulate failure paths in-memory (simulateFailureAtStep), query execution audit logs, and retry failed outbox messages.`,
      example: "Autonomous AI agent analyzes error rate, diagnoses an expired partner webhook token via get_instance_action_history, updates the hook configuration, and re-dispatches failed tasks.",
      tags: ["Model Context Protocol", "Pre-Flight Simulation", "Failure Injection", "Agentic Ops"]
    },
    {
      id: 6,
      title: "Microsecond Telemetry & Audit Compliance",
      badge: "Forensics & Audit",
      badgeColor: "bg-indigo-500/20 text-indigo-300 border-indigo-500/30",
      icon: FileCheck2,
      summary: "Full forensic traceability with execution latency and sanitized payloads.",
      situation: "Strict compliance requirements (SOC-2, HIPAA, ISO-27001) demanding immutable logs of external integrations.",
      solution: "WorkflowActionExecutionLog records exact execution timestamps, duration in ms, HTTP response codes, sanitized request/response snippets, and correlated instance state.",
      example: "Compliance auditor requests verification of patient record access revocation. Dashboard displays exact timestamp, 84ms latency, and HTTP 200 response from the identity provider.",
      tags: ["Latency Tracking", "HTTP Forensics", "SOC-2 / HIPAA Ready", "Interactive Drawer"]
    },
    {
      id: 7,
      title: "Zero-Downtime Workflow Version Control & Safe Rollback",
      badge: "Version Governance",
      badgeColor: "bg-teal-500/20 text-teal-300 border-teal-500/30",
      icon: GitBranch,
      summary: "Semantic versioning (Major/Minor/Patch) with deterministic instance pinning and 1-click safe rollback.",
      situation: "Constantly evolving business requirements (shortened SLAs, updated approval tiers, restructured steps) where thousands of in-flight workflow instances must complete safely without corruption.",
      solution: "Workflows enforce strict domain immutability after publication. Every running instance is permanently pinned to its exact version definition. Teams publish non-breaking policy changes via Patch versions (1.0.0 -> 1.0.1) or Context Binding Revisions, while 1-click rollback deprecates flawed versions without stranding running instances.",
      example: "Company reduces standard procurement SLA from 48h to 24h: Admin publishes Patch v1.0.1 with changelog. Existing in-flight orders safely finish on v1.0.0 under the original 48h terms, while all new orders immediately enforce the 24h SLA. If needed, 1-click rollback safely restores prior standards.",
      tags: ["SemVer (Major/Minor/Patch)", "Deterministic Instance Pinning", "1-Click Safe Rollback", "Context Parameterization", "ChangeLog Audit"]
    }
  ];

  const current = capabilities[selectedPillar];
  const CurrentIcon = current.icon;

  return (
    <div className="space-y-6">
      {/* Hero Header Banner */}
      <div className="bg-gradient-to-r from-blue-950/70 via-indigo-950/50 to-slate-900 border border-blue-500/30 p-6 rounded-3xl shadow-2xl relative overflow-hidden">
        <div className="relative z-10 flex flex-col md:flex-row md:items-center justify-between gap-6">
          <div>
            <div className="flex items-center gap-2 mb-2">
              <span className="px-3 py-1 rounded-full text-xs font-bold bg-blue-500/20 text-blue-300 border border-blue-500/30 flex items-center gap-1.5">
                <Sparkles size={13} className="text-amber-400" /> Platform Architecture & Capabilities
              </span>
              <span className="text-xs text-slate-500">•</span>
              <span className="text-xs text-emerald-400 font-semibold flex items-center gap-1">
                <CheckCircle2 size={13} /> {capabilities.length} Enterprise Pillars Online
              </span>
            </div>
            <h2 className="text-2xl md:text-3xl font-extrabold text-white tracking-tight">
              Enterprise Business Scenarios Handled by FlowOS
            </h2>
            <p className="text-xs md:text-sm text-slate-300 mt-1 max-w-3xl leading-relaxed">
              FlowOS combines pure finite state machines with transactional outbox dispatching, 
              cryptographic security, and native AI Agent (MCP) governance to solve complex, mission-critical distributed orchestration challenges.
            </p>
          </div>

          <div className="flex items-center gap-2 shrink-0 bg-slate-950/80 p-3 rounded-2xl border border-slate-800">
            <div className="text-right">
              <div className="text-[11px] text-slate-400">MCP Tool Registry</div>
              <div className="text-lg font-bold text-blue-400 font-mono">{mcpTools} Registered</div>
            </div>
            <div className="h-8 w-px bg-slate-800 mx-2" />
            <div className="text-right">
              <div className="text-[11px] text-slate-400">Release Verification</div>
              <div className="text-lg font-bold text-emerald-400 font-mono">{tests.total} Passing</div>
            </div>
          </div>
        </div>
      </div>

      {/* Interactive Grid & Detail View */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-6">
        {/* Left Column: Capability Selector Cards */}
        <div className="lg:col-span-5 space-y-2.5">
          <div className="text-xs font-bold text-slate-400 uppercase tracking-wider px-1">
            Explore Business Capabilities ({capabilities.length})
          </div>
          {capabilities.map((cap, idx) => {
            const Icon = cap.icon;
            const isSelected = selectedPillar === idx;
            return (
              <div
                key={cap.id}
                onClick={() => setSelectedPillar(idx)}
                className={`p-3.5 rounded-2xl border cursor-pointer transition-all ${
                  isSelected
                    ? 'bg-slate-800/90 border-blue-500/80 shadow-lg shadow-blue-500/10'
                    : 'bg-slate-900/60 border-slate-800 hover:border-slate-700 hover:bg-slate-850'
                }`}
              >
                <div className="flex items-start justify-between gap-3">
                  <div className="flex items-start gap-3">
                    <div className={`p-2 rounded-xl shrink-0 mt-0.5 ${
                      isSelected ? 'bg-blue-500/20 text-blue-300' : 'bg-slate-800 text-slate-400'
                    }`}>
                      <Icon size={18} />
                    </div>
                    <div>
                      <div className="flex items-center gap-2">
                        <span className="text-xs font-bold text-white leading-tight">
                          {cap.title}
                        </span>
                      </div>
                      <p className="text-[11px] text-slate-400 mt-1 line-clamp-2 leading-normal">
                        {cap.summary}
                      </p>
                    </div>
                  </div>
                  <ChevronRight size={16} className={`shrink-0 transition-transform mt-1 ${
                    isSelected ? 'text-blue-400 translate-x-0.5' : 'text-slate-600'
                  }`} />
                </div>
              </div>
            );
          })}
        </div>

        {/* Right Column: Deep-Dive Card */}
        <div className="lg:col-span-7">
          <div className="bg-slate-900/90 border border-slate-800 rounded-3xl p-6 shadow-xl space-y-6 sticky top-6">
            <div className="flex items-center justify-between border-b border-slate-800/80 pb-4">
              <div className="flex items-center gap-3">
                <div className="p-2.5 rounded-2xl bg-blue-500/20 text-blue-400 border border-blue-500/30">
                  <CurrentIcon size={22} />
                </div>
                <div>
                  <span className={`px-2 py-0.5 rounded-md text-[10px] font-bold uppercase tracking-wider border ${current.badgeColor}`}>
                    {current.badge}
                  </span>
                  <h3 className="text-lg font-bold text-white mt-1">
                    {current.title}
                  </h3>
                </div>
              </div>
            </div>

            {/* The Business Situation */}
            <div className="space-y-2">
              <span className="text-xs font-bold text-amber-400 uppercase tracking-wider flex items-center gap-1.5">
                <Zap size={14} /> The Complex Business Situation
              </span>
              <p className="text-xs text-slate-300 bg-slate-950/70 p-3.5 rounded-2xl border border-slate-800/80 leading-relaxed">
                {current.situation}
              </p>
            </div>

            {/* FlowOS Solution */}
            <div className="space-y-2">
              <span className="text-xs font-bold text-emerald-400 uppercase tracking-wider flex items-center gap-1.5">
                <CheckCircle2 size={14} /> How FlowOS Handles It
              </span>
              <p className="text-xs text-slate-300 bg-emerald-950/20 p-3.5 rounded-2xl border border-emerald-500/20 leading-relaxed">
                {current.solution}
              </p>
            </div>

            {/* Real-World Concrete Example */}
            <div className="space-y-2">
              <span className="text-xs font-bold text-cyan-400 uppercase tracking-wider flex items-center gap-1.5">
                <ArrowRight size={14} /> Concrete Enterprise Example
              </span>
              <div className="text-xs text-cyan-200/90 font-mono bg-slate-950 p-4 rounded-2xl border border-cyan-500/30 leading-relaxed shadow-inner">
                {current.example}
              </div>
            </div>

            {/* Feature Badges */}
            <div className="pt-2 border-t border-slate-800 flex flex-wrap gap-2">
              {current.tags.map(tag => (
                <span key={tag} className="px-2.5 py-1 bg-slate-800 border border-slate-700 text-slate-300 rounded-lg text-[10px] font-semibold">
                  #{tag}
                </span>
              ))}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};
