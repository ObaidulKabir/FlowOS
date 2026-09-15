import React, { useState } from 'react';
import { 
  ShieldCheck, Bot, Cpu, Layers, Check, 
  HelpCircle, Server, Sparkles
} from 'lucide-react';

interface ComparisonRow {
  feature: string;
  category: 'architecture' | 'agent' | 'governance';
  flowos: { title: string; highlight: boolean; sub?: string };
  temporal: { title: string; sub?: string };
  camunda: { title: string; sub?: string };
  aws: { title: string; sub?: string };
}

export const PlatformComparison: React.FC = () => {
  const [activeCategory, setActiveCategory] = useState<'all' | 'architecture' | 'agent' | 'governance'>('all');
  const [selectedEngine, setSelectedEngine] = useState<'flowos' | 'temporal' | 'camunda' | 'aws'>('flowos');

  const rows: ComparisonRow[] = [
    {
      feature: 'State Authority & Execution',
      category: 'architecture',
      flowos: { 
        title: 'Dual-Kernel (FSM + Steps)', 
        highlight: true,
        sub: 'Mathematical legal states decoupled from operational steps' 
      },
      temporal: { 
        title: 'Deterministic Code Replay', 
        sub: 'Event sourcing; replay breaks if code has non-determinism' 
      },
      camunda: { 
        title: 'BPMN 2.0 Token Stream', 
        sub: 'Raft log-centric Zeebe broker polling' 
      },
      aws: { 
        title: 'Cloud-Managed ASL', 
        sub: 'Amazon States Language state machine' 
      }
    },
    {
      feature: 'Dual-Write Hazard Safety',
      category: 'architecture',
      flowos: { 
        title: 'Guaranteed (Transactional Outbox)', 
        highlight: true,
        sub: 'State transitions & action side-effects commit in 1 atomic unit of work' 
      },
      temporal: { 
        title: 'Manual Activity Idempotency', 
        sub: 'Developer must manage downstream idempotency tokens' 
      },
      camunda: { 
        title: 'External Worker Acks', 
        sub: 'At-least-once job delivery with worker deduplication' 
      },
      aws: { 
        title: 'Distributed Cloud Retry', 
        sub: 'SQS / DynamoDB transaction glue code' 
      }
    },
    {
      feature: 'AI Agent & LLM Native (MCP)',
      category: 'agent',
      flowos: { 
        title: 'Native 52-Tool MCP Server', 
        highlight: true,
        sub: 'Agents dry-run simulations with mock payloads before publishing' 
      },
      temporal: { 
        title: 'Custom SDK Wrapper Required', 
        sub: 'Must write imperative worker activities for LLM calls' 
      },
      camunda: { 
        title: 'REST / Connectors Required', 
        sub: 'External job workers or HTTP connectors' 
      },
      aws: { 
        title: 'Bedrock / Lambda Glue', 
        sub: 'Requires wiring IAM, Lambda, and Bedrock actions' 
      }
    },
    {
      feature: 'Lifecycle Hooks (Pre/Post)',
      category: 'architecture',
      flowos: { 
        title: 'Declarative OnEntry / OnExit', 
        highlight: true,
        sub: 'Webhooks, notifications, & events with payload condition guards' 
      },
      temporal: { 
        title: 'Imperative Code Statements', 
        sub: 'Code executed in sequence inside workflow methods' 
      },
      camunda: { 
        title: 'BPMN Execution Listeners', 
        sub: 'Java delegates or script extensions' 
      },
      aws: { 
        title: 'Custom Lambda Tasks', 
        sub: 'Extra state transitions in ASL definition' 
      }
    },
    {
      feature: 'Specification Format',
      category: 'governance',
      flowos: { 
        title: '100% Declarative JSON Blueprint', 
        highlight: true,
        sub: 'Human & LLM friendly schema with static validation' 
      },
      temporal: { 
        title: 'Pure Code (Go, TS, Java, Python)', 
        sub: 'Hard to inspect or generate programmatically by AI' 
      },
      camunda: { 
        title: 'BPMN 2.0 XML Standard', 
        sub: 'Verbose XML diagrams, complex for LLMs to generate' 
      },
      aws: { 
        title: 'Amazon States Language (JSON)', 
        sub: 'AWS-proprietary JSON schema' 
      }
    },
    {
      feature: 'Design & Publishing Governance',
      category: 'governance',
      flowos: { 
        title: 'Draft ➔ Validate ➔ Simulate ➔ Publish', 
        highlight: true,
        sub: 'Formal lifecycle with immutable version lineage' 
      },
      temporal: { 
        title: 'Git Version Control Only', 
        sub: 'Patching APIs require developer maintenance' 
      },
      camunda: { 
        title: 'Web Modeler ➔ Zeebe Deploy', 
        sub: 'Visual diagram deployment to cluster' 
      },
      aws: { 
        title: 'CloudFormation / CDK', 
        sub: 'Infrastructure-as-code deployments' 
      }
    },
    {
      feature: 'Human Tasks (HITL) & SLAs',
      category: 'governance',
      flowos: { 
        title: 'Native Role RBAC & Auto-Escalation', 
        highlight: true,
        sub: 'SLA timeout auto-escalation to backup roles' 
      },
      temporal: { 
        title: 'Custom Timers & Signals', 
        sub: 'Must write custom signal handlers in workflow code' 
      },
      camunda: { 
        title: 'Native User Tasks & Tasklist', 
        sub: 'Enterprise task portal with form builders' 
      },
      aws: { 
        title: 'Callback Task Tokens', 
        sub: 'waitForTaskToken requires custom resume webhook' 
      }
    },
    {
      feature: 'Infrastructure & Footprint',
      category: 'architecture',
      flowos: { 
        title: 'Lightweight (.NET 8 + Postgres/RAM)', 
        highlight: true,
        sub: 'Runs as single container or embedded; zero cluster sprawl' 
      },
      temporal: { 
        title: 'Heavy Cluster Sprawl', 
        sub: 'Requires Temporal Server, Cassandra/Postgres, Elasticsearch' 
      },
      camunda: { 
        title: 'Heavy Cluster Sprawl', 
        sub: 'Requires Zeebe Raft cluster, Elasticsearch, Keycloak' 
      },
      aws: { 
        title: 'Zero-Infra Serverless', 
        sub: 'Zero setup, but total proprietary cloud vendor lock-in' 
      }
    },
    {
      feature: 'Multi-Tenancy & Security',
      category: 'governance',
      flowos: { 
        title: 'Zero-Trust Isolation & Scoped Keys', 
        highlight: true,
        sub: 'Native tenant-scoped API keys with RBAC permissions' 
      },
      temporal: { 
        title: 'Namespaces (Logical Only)', 
        sub: 'No native tenant data encryption or multi-tenant keys' 
      },
      camunda: { 
        title: 'Enterprise License Required', 
        sub: 'Multi-tenancy gated behind enterprise tier' 
      },
      aws: { 
        title: 'AWS Account / IAM Scopes', 
        sub: 'Requires managing multiple AWS accounts or IAM policies' 
      }
    }
  ];

  const filteredRows = rows.filter(r => activeCategory === 'all' || r.category === activeCategory);

  const engineGuide = {
    flowos: {
      name: 'FlowOS',
      badge: 'Agentic & Transactional FSM',
      summary: 'Best for autonomous AI agents, transactional enterprise workflows, and modern SaaS applications.',
      strengths: [
        'Native Model Context Protocol (MCP) server for zero-friction AI agent orchestration.',
        'Zero Dual-Write Hazard via Transactional Outbox Pattern for webhooks and notifications.',
        'Strict separation of pure mathematical state authority from operational steps.',
        'Interactive real-time visual sandbox simulator for designers, testers, and agents.',
        'Lightweight, high-performance .NET 8 binary with native multi-tenancy and scoped API keys.'
      ]
    },
    temporal: {
      name: 'Temporal.io',
      badge: 'Code-as-Workflows',
      summary: 'Best for software engineering teams orchestrating complex code-level microservices in Go, Java, or TypeScript.',
      strengths: [
        'Developers write regular programming code with IDE auto-complete and refactoring.',
        'Battle-tested for high-throughput distributed microservice orchestration.',
        'Durable execution with automatic state recovery across server crashes.'
      ]
    },
    camunda: {
      name: 'Camunda 8 (Zeebe)',
      badge: 'BPMN 2.0 Enterprise Standard',
      summary: 'Best for large enterprises with dedicated business analysts standardized on BPMN & DMN.',
      strengths: [
        'Strict compliance with industry-standard BPMN 2.0 and DMN specifications.',
        'Mature visual Web Modeler and Tasklist portal for manual business operations.',
        'High-throughput append-only event stream engine (Zeebe).'
      ]
    },
    aws: {
      name: 'AWS Step Functions',
      badge: 'Serverless Cloud Native',
      summary: 'Best for organizations fully invested in the AWS ecosystem seeking zero infrastructure maintenance.',
      strengths: [
        'Native out-of-the-box integrations with 200+ AWS services (Lambda, DynamoDB, SQS).',
        'Zero infrastructure management or database provisioning.',
        'Pay-per-state-transition serverless pricing model.'
      ]
    }
  };

  return (
    <div className="space-y-8">
      {/* 4 Architectural Advantage Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <div className="p-4 rounded-xl bg-slate-950 border border-slate-800 hover:border-emerald-500/40 transition-all space-y-2">
          <div className="w-8 h-8 rounded-lg bg-emerald-500/10 text-emerald-400 flex items-center justify-center">
            <ShieldCheck size={18} />
          </div>
          <h4 className="text-sm font-bold text-white">Zero Dual-Write Hazard</h4>
          <p className="text-xs text-slate-400 leading-relaxed">
            Transactional Outbox Pattern guarantees that state changes and lifecycle hooks commit in the <strong>exact same database unit of work</strong>.
          </p>
        </div>

        <div className="p-4 rounded-xl bg-slate-950 border border-slate-800 hover:border-blue-500/40 transition-all space-y-2">
          <div className="w-8 h-8 rounded-lg bg-blue-500/10 text-blue-400 flex items-center justify-center">
            <Bot size={18} />
          </div>
          <h4 className="text-sm font-bold text-white">Native AI Agent (MCP)</h4>
          <p className="text-xs text-slate-400 leading-relaxed">
            First-class 52-tool MCP server. Agents can <strong>dry-run simulated workflows</strong> with mock payloads before publishing to production.
          </p>
        </div>

        <div className="p-4 rounded-xl bg-slate-950 border border-slate-800 hover:border-purple-500/40 transition-all space-y-2">
          <div className="w-8 h-8 rounded-lg bg-purple-500/10 text-purple-400 flex items-center justify-center">
            <Cpu size={18} />
          </div>
          <h4 className="text-sm font-bold text-white">Dual-Kernel Architecture</h4>
          <p className="text-xs text-slate-400 leading-relaxed">
            Decouples <strong>pure mathematical state machine rules</strong> from operational workflow steps (commands, decisions, timers, and human tasks).
          </p>
        </div>

        <div className="p-4 rounded-xl bg-slate-950 border border-slate-800 hover:border-amber-500/40 transition-all space-y-2">
          <div className="w-8 h-8 rounded-lg bg-amber-500/10 text-amber-400 flex items-center justify-center">
            <Server size={18} />
          </div>
          <h4 className="text-sm font-bold text-white">Zero Cluster Overhead</h4>
          <p className="text-xs text-slate-400 leading-relaxed">
            Runs as a lightweight, high-performance .NET 8 service with PostgreSQL or in-memory storage. No Zeebe, Cassandra, or Elasticsearch clusters.
          </p>
        </div>
      </div>

      {/* Filter Tabs */}
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-800 pb-3">
        <div className="flex items-center gap-1.5 bg-slate-950 p-1 rounded-xl border border-slate-800">
          <button
            onClick={() => setActiveCategory('all')}
            className={`px-3 py-1 text-xs font-semibold rounded-lg transition-all ${
              activeCategory === 'all' ? 'bg-slate-800 text-white shadow-sm' : 'text-slate-400 hover:text-slate-200'
            }`}
          >
            All Capabilities
          </button>
          <button
            onClick={() => setActiveCategory('architecture')}
            className={`px-3 py-1 text-xs font-semibold rounded-lg transition-all ${
              activeCategory === 'architecture' ? 'bg-slate-800 text-white shadow-sm' : 'text-slate-400 hover:text-slate-200'
            }`}
          >
            Architecture & Safety
          </button>
          <button
            onClick={() => setActiveCategory('agent')}
            className={`px-3 py-1 text-xs font-semibold rounded-lg transition-all ${
              activeCategory === 'agent' ? 'bg-slate-800 text-white shadow-sm' : 'text-slate-400 hover:text-slate-200'
            }`}
          >
            AI Agent (MCP)
          </button>
          <button
            onClick={() => setActiveCategory('governance')}
            className={`px-3 py-1 text-xs font-semibold rounded-lg transition-all ${
              activeCategory === 'governance' ? 'bg-slate-800 text-white shadow-sm' : 'text-slate-400 hover:text-slate-200'
            }`}
          >
            Governance & Human Tasks
          </button>
        </div>

        <span className="text-[11px] text-slate-500 font-mono">
          Showing {filteredRows.length} comparison criteria
        </span>
      </div>

      {/* Comparison Matrix Table */}
      <div className="overflow-x-auto bg-slate-900 border border-slate-800 rounded-2xl shadow-xl">
        <table className="w-full text-left text-xs border-collapse">
          <thead>
            <tr className="bg-slate-950 border-b border-slate-800 text-slate-400 uppercase tracking-wider text-[11px]">
              <th className="p-4 w-1/4">Criterion</th>
              <th className="p-4 w-1/4 bg-blue-950/40 border-x border-blue-500/20 text-blue-300 font-bold">
                <div className="flex items-center gap-1.5">
                  <Sparkles size={14} className="text-blue-400" />
                  <span>FlowOS 1.0</span>
                </div>
              </th>
              <th className="p-4 w-1/6">Temporal.io</th>
              <th className="p-4 w-1/6">Camunda 8 (Zeebe)</th>
              <th className="p-4 w-1/6">AWS Step Functions</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-800/70">
            {filteredRows.map((row, idx) => (
              <tr key={idx} className="hover:bg-slate-850/40 transition-colors">
                <td className="p-4 font-semibold text-slate-200">
                  <div className="flex items-center gap-2">
                    <Layers size={13} className="text-slate-500 shrink-0" />
                    <span>{row.feature}</span>
                  </div>
                </td>

                {/* FlowOS Column (Highlighted) */}
                <td className="p-4 bg-blue-950/20 border-x border-blue-500/20">
                  <div className="flex items-start gap-1.5">
                    <span className="text-emerald-400 font-bold mt-0.5">✓</span>
                    <div>
                      <span className="text-white font-bold text-xs block">{row.flowos.title}</span>
                      {row.flowos.sub && (
                        <span className="text-[10px] text-blue-300/80 leading-relaxed block mt-0.5">{row.flowos.sub}</span>
                      )}
                    </div>
                  </div>
                </td>

                {/* Temporal */}
                <td className="p-4 text-slate-300">
                  <span className="font-medium block text-slate-200">{row.temporal.title}</span>
                  {row.temporal.sub && (
                    <span className="text-[10px] text-slate-500 block mt-0.5">{row.temporal.sub}</span>
                  )}
                </td>

                {/* Camunda */}
                <td className="p-4 text-slate-300">
                  <span className="font-medium block text-slate-200">{row.camunda.title}</span>
                  {row.camunda.sub && (
                    <span className="text-[10px] text-slate-500 block mt-0.5">{row.camunda.sub}</span>
                  )}
                </td>

                {/* AWS */}
                <td className="p-4 text-slate-300">
                  <span className="font-medium block text-slate-200">{row.aws.title}</span>
                  {row.aws.sub && (
                    <span className="text-[10px] text-slate-500 block mt-0.5">{row.aws.sub}</span>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* When to Choose Which Platform Selector */}
      <div className="p-6 bg-slate-950 border border-slate-800 rounded-2xl space-y-4">
        <div className="flex items-center justify-between">
          <div>
            <h4 className="text-sm font-bold text-white flex items-center gap-2">
              <HelpCircle size={15} className="text-blue-400" /> Platform Decision Guide: When to choose which engine?
            </h4>
            <p className="text-xs text-slate-400 mt-0.5">
              Select a platform below to see its ideal architecture profile.
            </p>
          </div>
          <div className="flex gap-1.5">
            {(['flowos', 'temporal', 'camunda', 'aws'] as const).map(k => (
              <button
                key={k}
                onClick={() => setSelectedEngine(k)}
                className={`px-3 py-1 rounded-lg text-xs font-semibold capitalize transition-all ${
                  selectedEngine === k 
                    ? 'bg-blue-600 text-white shadow-md' 
                    : 'bg-slate-900 border border-slate-800 text-slate-400 hover:text-white'
                }`}
              >
                {k === 'flowos' ? 'FlowOS' : k === 'aws' ? 'AWS Step Functions' : k === 'camunda' ? 'Camunda 8' : 'Temporal'}
              </button>
            ))}
          </div>
        </div>

        <div className="p-4 rounded-xl bg-slate-900 border border-slate-800 text-xs space-y-3">
          <div className="flex items-center justify-between">
            <span className="text-sm font-bold text-white flex items-center gap-2">
              {engineGuide[selectedEngine].name}
              <span className="text-[10px] px-2 py-0.5 rounded-full bg-slate-800 text-blue-300 border border-slate-700 font-mono">
                {engineGuide[selectedEngine].badge}
              </span>
            </span>
            <span className="text-slate-400 italic text-[11px]">
              {engineGuide[selectedEngine].summary}
            </span>
          </div>

          <ul className="space-y-1.5 pt-2 border-t border-slate-800">
            {engineGuide[selectedEngine].strengths.map((str, idx) => (
              <li key={idx} className="flex items-start gap-2 text-slate-300">
                <Check size={13} className="text-emerald-400 mt-0.5 shrink-0" />
                <span>{str}</span>
              </li>
            ))}
          </ul>
        </div>
      </div>
    </div>
  );
};
