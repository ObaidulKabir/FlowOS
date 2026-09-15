import React, { useState } from 'react';
import { 
  Scale, CheckCircle2, XCircle, 
  Layers, Zap
} from 'lucide-react';
import { usePlatformMetrics } from '../platformMetrics';

export const CompetitiveComparison: React.FC = () => {
  const { mcpTools } = usePlatformMetrics();
  const [selectedCompetitor, setSelectedCompetitor] = useState<'all' | 'temporal' | 'camunda' | 'stepfunctions' | 'conductor'>('all');

  const competitors = [
    {
      id: 'temporal',
      name: 'Temporal.io',
      category: 'Code-as-Workflows (Durable Execution)',
      verdict: 'Superior for pure developer code-centric backends, but heavy infra and zero native AI/MCP authoring.',
      strengths: [
        'Durable execution with automatic local state persistence and retries.',
        'Supports multiple programming languages (Go, TypeScript, Python, Java, .NET).',
        'Strong community and massive scale battle-tested in hyper-growth tech companies.'
      ],
      limitations: [
        'Requires deterministic code replay (standard DateTime.Now, random numbers, or direct network calls break execution).',
        'High infrastructure footprint: requires Cassandra/PostgreSQL, Elasticsearch, and multiple Temporal Server services.',
        'No visual declarative authoring or human-friendly JSON blueprints; impossible for non-engineers or direct LLM JSON drafting.',
        'No built-in native Model Context Protocol (MCP) server or in-memory simulation API.'
      ],
      flowosAdvantage: `FlowOS offers pure declarative JSON blueprints, zero-code dry-run simulation via MCP (${mcpTools} tools), guaranteed zero dual-write via transactional outbox, and lightweight single-binary deployment.`
    },
    {
      id: 'camunda',
      name: 'Camunda 8 (Zeebe)',
      category: 'BPMN 2.0 / DMN Enterprise Suite',
      verdict: 'Enterprise standard for legacy BPMN/DMN business analysts, but heavy XML specs and complex cluster management.',
      strengths: [
        'Strict compliance with industry-standard BPMN 2.0 and DMN notation.',
        'Camunda Web Modeler allows business analysts and enterprise architects to collaborate visually.',
        'High throughput log-centric event streaming engine (Zeebe) backed by Raft.'
      ],
      limitations: [
        'Extremely verbose and complex BPMN XML definitions that LLM agents struggle to generate reliably.',
        'Substantial enterprise licensing cost and heavy cluster infrastructure (Zeebe, Elasticsearch, Keycloak, Operate, Tasklist).',
        'Lifecycle hooks and external webhooks require manual job worker polling over gRPC.',
        'Multi-tenancy and advanced features are gated behind costly enterprise licensing tiers.'
      ],
      flowosAdvantage: 'FlowOS uses clean, concise camelCase JSON schemas tailored for LLMs and developers, provides built-in multi-tenancy by default, and runs on a lightweight modern .NET 8 / PostgreSQL core.'
    },
    {
      id: 'stepfunctions',
      name: 'AWS Step Functions',
      category: 'Serverless Cloud Orchestration',
      verdict: 'Best for native AWS-only serverless pipelines, but proprietary cloud lock-in and high invocation pricing.',
      strengths: [
        'Zero infrastructure maintenance; fully managed serverless scaling.',
        'Seamless integration with AWS primitives (Lambda, DynamoDB, SQS, SNS, EventBridge).',
        'Visual workflow execution monitoring in AWS Management Console.'
      ],
      limitations: [
        'Strict vendor lock-in to Amazon Web Services; cannot be self-hosted on-premise, hybrid cloud, or edge.',
        'Proprietary Amazon States Language (ASL) JSON dialect.',
        'Can become extremely expensive at high transaction volume ($0.025 per 1,000 state transitions).',
        'No native Model Context Protocol (MCP) server or tenant-isolated cryptographic webhook secret management.'
      ],
      flowosAdvantage: 'FlowOS is fully portable and self-hostable anywhere (on-prem, hybrid, private cloud), provides zero dual-write guarantees via transactional outbox, and includes built-in AI Agent orchestration.'
    },
    {
      id: 'conductor',
      name: 'Netflix Conductor (Orkes)',
      category: 'Microservice Task DAG Orchestrator',
      verdict: 'Strong for high-scale microservice task queues, but relies on continuous worker polling and lacks decoupled state machines.',
      strengths: [
        'Battle-tested at Netflix for high-throughput distributed microservice choreographies.',
        'Supports visual DAG task flows with dynamic forks and joins.',
        'Decent REST API and multi-language SDK client support.'
      ],
      limitations: [
        'Worker polling model: external workers must poll Conductor queues over HTTP/gRPC, introducing latency and queue churn.',
        'Lacks a pure, decoupled finite state machine (FSM) guard layer to govern legal domain transitions.',
        'Infrastructure complexity requires running Redis/Dynomite, Elasticsearch, and relational databases.',
        'No native Model Context Protocol (MCP) server for autonomous AI agent tool-calling.'
      ],
      flowosAdvantage: `FlowOS decouples pure state machine guards from workflow step execution, uses atomic transactional outbox dispatching rather than poll churn, and features native MCP ${mcpTools}-tool integration.`
    }
  ];

  return (
    <div className="space-y-6">
      {/* Header Banner */}
      <div className="bg-gradient-to-r from-slate-900 via-indigo-950/60 to-purple-950/40 border border-indigo-500/30 p-6 rounded-3xl shadow-2xl relative overflow-hidden">
        <div className="flex flex-col md:flex-row md:items-center justify-between gap-6 relative z-10">
          <div>
            <div className="flex items-center gap-2 mb-2">
              <span className="px-3 py-1 rounded-full text-xs font-bold bg-indigo-500/20 text-indigo-300 border border-indigo-500/30 flex items-center gap-1.5">
                <Scale size={13} className="text-amber-400" /> Competitive Architecture Benchmark
              </span>
              <span className="text-xs text-slate-500">•</span>
              <span className="text-xs text-slate-400">FlowOS vs. Top 4 Industry Engines</span>
            </div>
            <h2 className="text-2xl md:text-3xl font-extrabold text-white tracking-tight">
              FlowOS vs. Major Competitors
            </h2>
            <p className="text-xs md:text-sm text-slate-300 mt-1 max-w-3xl leading-relaxed">
              Detailed technical and architectural comparison across <strong>Temporal.io</strong>, <strong>Camunda 8</strong>, 
              <strong>AWS Step Functions</strong>, and <strong>Netflix Conductor</strong>.
            </p>
          </div>

          <div className="flex items-center gap-2 bg-slate-950/80 p-3 rounded-2xl border border-slate-800 shrink-0">
            <div className="text-right">
              <div className="text-[11px] text-slate-400">Unique Differentiator</div>
              <div className="text-sm font-bold text-amber-400 font-mono">Native MCP + Zero Dual-Write</div>
            </div>
          </div>
        </div>
      </div>

      {/* Competitor Filter Tabs */}
      <div className="flex flex-wrap gap-2">
        <button
          onClick={() => setSelectedCompetitor('all')}
          className={`px-3 py-1.5 rounded-xl text-xs font-semibold transition-all ${
            selectedCompetitor === 'all'
              ? 'bg-blue-600 text-white shadow-md'
              : 'bg-slate-800 text-slate-400 hover:text-white hover:bg-slate-750'
          }`}
        >
          All 4 Competitors
        </button>
        {competitors.map(c => (
          <button
            key={c.id}
            onClick={() => setSelectedCompetitor(c.id as any)}
            className={`px-3 py-1.5 rounded-xl text-xs font-semibold transition-all ${
              selectedCompetitor === c.id
                ? 'bg-blue-600 text-white shadow-md'
                : 'bg-slate-800 text-slate-400 hover:text-white hover:bg-slate-750'
            }`}
          >
            {c.name}
          </button>
        ))}
      </div>

      {/* Comparison Matrix Table */}
      <div className="bg-slate-900 border border-slate-800 rounded-3xl overflow-hidden shadow-xl">
        <div className="p-4 border-b border-slate-800 bg-slate-950/60 flex items-center justify-between">
          <span className="text-xs font-bold text-slate-200 uppercase tracking-wider flex items-center gap-2">
            <Layers size={14} className="text-blue-400" /> Architectural Matrix Breakdown
          </span>
          <span className="text-[11px] text-slate-400">Updated for 2026</span>
        </div>
        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs border-collapse">
            <thead>
              <tr className="bg-slate-950 text-slate-400 text-[11px] uppercase border-b border-slate-800">
                <th className="p-3.5 pl-5 font-bold">Dimension</th>
                <th className="p-3.5 font-bold text-blue-400 bg-blue-950/20 border-x border-blue-500/20">FlowOS</th>
                <th className="p-3.5 font-semibold">Temporal.io</th>
                <th className="p-3.5 font-semibold">Camunda 8 (Zeebe)</th>
                <th className="p-3.5 font-semibold">AWS Step Functions</th>
                <th className="p-3.5 pr-5 font-semibold">Netflix Conductor</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-800/80 font-mono text-[11px]">
              <tr className="hover:bg-slate-850/50">
                <td className="p-3.5 pl-5 font-sans font-bold text-slate-300">Authoring Model</td>
                <td className="p-3.5 font-bold text-blue-300 bg-blue-950/20 border-x border-blue-500/20">Declarative JSON Blueprint</td>
                <td className="p-3.5 text-slate-400 font-sans">Imperative Code (TS/Go/Java/.NET)</td>
                <td className="p-3.5 text-slate-400 font-sans">BPMN 2.0 XML + DMN Tables</td>
                <td className="p-3.5 text-slate-400 font-sans">Amazon States Language (ASL)</td>
                <td className="p-3.5 pr-5 text-slate-400 font-sans">JSON DAG Task Definition</td>
              </tr>
              <tr className="hover:bg-slate-850/50">
                <td className="p-3.5 pl-5 font-sans font-bold text-slate-300">Dual-Write Safety</td>
                <td className="p-3.5 font-bold text-emerald-400 bg-blue-950/20 border-x border-blue-500/20">Guaranteed (Transactional Outbox)</td>
                <td className="p-3.5 text-slate-400 font-sans">User Code Responsibility (Activities)</td>
                <td className="p-3.5 text-slate-400 font-sans">External Worker Polling / Acks</td>
                <td className="p-3.5 text-slate-400 font-sans">User Responsibility (SQS / DynamoDB)</td>
                <td className="p-3.5 pr-5 text-slate-400 font-sans">Worker Idempotency Responsibility</td>
              </tr>
              <tr className="hover:bg-slate-850/50">
                <td className="p-3.5 pl-5 font-sans font-bold text-slate-300">AI Agent / MCP Native</td>
                <td className="p-3.5 font-bold text-purple-300 bg-blue-950/20 border-x border-blue-500/20">{mcpTools} Native MCP Tools (Govern, Bind, Simulate, Operate)</td>
                <td className="p-3.5 text-rose-400 font-sans">None (Custom API glue required)</td>
                <td className="p-3.5 text-rose-400 font-sans">None (Custom LangChain connectors)</td>
                <td className="p-3.5 text-slate-400 font-sans">AWS Bedrock / Lambda glue</td>
                <td className="p-3.5 pr-5 text-rose-400 font-sans">None (Custom worker polling)</td>
              </tr>
              <tr className="hover:bg-slate-850/50">
                <td className="p-3.5 pl-5 font-sans font-bold text-slate-300">Dry-Run Simulation</td>
                <td className="p-3.5 font-bold text-cyan-300 bg-blue-950/20 border-x border-blue-500/20">In-Memory Simulator with Failure Injection</td>
                <td className="p-3.5 text-slate-400 font-sans">Code unit test harness (Replay only)</td>
                <td className="p-3.5 text-slate-400 font-sans">Basic Token Simulator (Desktop Modeler)</td>
                <td className="p-3.5 text-slate-400 font-sans">Cloud console test executions only</td>
                <td className="p-3.5 pr-5 text-slate-400 font-sans">Swagger API execution dry-run</td>
              </tr>
              <tr className="hover:bg-slate-850/50">
                <td className="p-3.5 pl-5 font-sans font-bold text-slate-300">Saga Compensation</td>
                <td className="p-3.5 font-bold text-emerald-400 bg-blue-950/20 border-x border-blue-500/20">Declarative OnFailure + Outbox Rollback</td>
                <td className="p-3.5 text-slate-400 font-sans">Try/Catch Compensating Activities in Code</td>
                <td className="p-3.5 text-slate-400 font-sans">BPMN Boundary Compensation Events</td>
                <td className="p-3.5 text-slate-400 font-sans">Catch blocks to Compensating Lambda</td>
                <td className="p-3.5 pr-5 text-slate-400 font-sans">Failure workflow triggers</td>
              </tr>
              <tr className="hover:bg-slate-850/50">
                <td className="p-3.5 pl-5 font-sans font-bold text-slate-300">Infrastructure Footprint</td>
                <td className="p-3.5 font-bold text-emerald-300 bg-blue-950/20 border-x border-blue-500/20">Lightweight (.NET 8 + Postgres)</td>
                <td className="p-3.5 text-amber-400 font-sans">Heavy (Server cluster + ES/Cassandra)</td>
                <td className="p-3.5 text-amber-400 font-sans">Heavy (Zeebe + ES + Keycloak + WebApps)</td>
                <td className="p-3.5 text-emerald-400 font-sans">Zero-Infra (AWS Serverless)</td>
                <td className="p-3.5 pr-5 text-amber-400 font-sans">Medium-Heavy (Conductor + Redis + ES)</td>
              </tr>
              <tr className="hover:bg-slate-850/50">
                <td className="p-3.5 pl-5 font-sans font-bold text-slate-300">Multi-Tenancy & Security</td>
                <td className="p-3.5 font-bold text-blue-300 bg-blue-950/20 border-x border-blue-500/20">Native Tenant Isolation + HMAC Secrets</td>
                <td className="p-3.5 text-slate-400 font-sans">Logical Namespaces only</td>
                <td className="p-3.5 text-amber-400 font-sans">Enterprise-license only</td>
                <td className="p-3.5 text-slate-400 font-sans">AWS Account / IAM scoping</td>
                <td className="p-3.5 pr-5 text-slate-400 font-sans">Domain grouping</td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      {/* Deep-Dive Competitor Cards */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
        {competitors
          .filter(c => selectedCompetitor === 'all' || selectedCompetitor === c.id)
          .map(c => (
            <div key={c.id} className="bg-slate-900 border border-slate-800 rounded-3xl p-6 shadow-xl space-y-4">
              <div className="flex items-start justify-between border-b border-slate-800 pb-3">
                <div>
                  <h3 className="text-lg font-bold text-white flex items-center gap-2">
                    {c.name}
                  </h3>
                  <span className="text-[11px] text-slate-400 font-medium">{c.category}</span>
                </div>
                <span className="text-[10px] px-2.5 py-1 rounded-full font-bold bg-slate-800 text-slate-300 border border-slate-700">
                  Head-to-Head
                </span>
              </div>

              <p className="text-xs text-slate-300 italic bg-slate-950/80 p-3 rounded-xl border border-slate-800/80">
                "{c.verdict}"
              </p>

              {/* Strengths */}
              <div>
                <span className="text-[11px] font-bold text-emerald-400 uppercase tracking-wider flex items-center gap-1.5 mb-1.5">
                  <CheckCircle2 size={13} /> Where {c.name} Excels
                </span>
                <ul className="space-y-1 text-xs text-slate-400">
                  {c.strengths.map((s, idx) => (
                    <li key={idx} className="flex items-start gap-1.5">
                      <span className="text-emerald-500">•</span>
                      <span>{s}</span>
                    </li>
                  ))}
                </ul>
              </div>

              {/* Limitations */}
              <div>
                <span className="text-[11px] font-bold text-rose-400 uppercase tracking-wider flex items-center gap-1.5 mb-1.5">
                  <XCircle size={13} /> Key Limitations vs FlowOS
                </span>
                <ul className="space-y-1 text-xs text-slate-400">
                  {c.limitations.map((l, idx) => (
                    <li key={idx} className="flex items-start gap-1.5">
                      <span className="text-rose-500">•</span>
                      <span>{l}</span>
                    </li>
                  ))}
                </ul>
              </div>

              {/* FlowOS Decisive Advantage */}
              <div className="pt-2 border-t border-slate-800">
                <span className="text-[11px] font-bold text-amber-400 uppercase tracking-wider flex items-center gap-1.5 mb-1">
                  <Zap size={13} /> The FlowOS Advantage
                </span>
                <p className="text-xs text-amber-200/90 bg-amber-950/20 p-3 rounded-xl border border-amber-500/20">
                  {c.flowosAdvantage}
                </p>
              </div>
            </div>
          ))}
      </div>
    </div>
  );
};
