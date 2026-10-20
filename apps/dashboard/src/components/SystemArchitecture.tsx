import React from 'react';
import { ArrowDown, ArrowRight, Bot, Building2, Database, LayoutDashboard, Scale, Server, Sparkles, Workflow } from 'lucide-react';

type Tone = 'blue' | 'purple' | 'cyan' | 'orange';

const toneClass: Record<Tone, { band: string; card: string; chip: string; text: string }> = {
  blue: {
    band: 'border-blue-500/40 bg-blue-500/10',
    card: 'border-blue-400/50 bg-blue-950/70',
    chip: 'bg-blue-500',
    text: 'text-blue-200'
  },
  purple: {
    band: 'border-purple-500/40 bg-purple-500/10',
    card: 'border-purple-400/50 bg-purple-950/70',
    chip: 'bg-purple-500',
    text: 'text-purple-200'
  },
  cyan: {
    band: 'border-cyan-500/40 bg-cyan-500/10',
    card: 'border-cyan-400/50 bg-cyan-950/70',
    chip: 'bg-cyan-500',
    text: 'text-cyan-200'
  },
  orange: {
    band: 'border-orange-500/40 bg-orange-500/10',
    card: 'border-orange-400/50 bg-orange-950/70',
    chip: 'bg-orange-500',
    text: 'text-orange-200'
  }
};

const NodeCard: React.FC<{
  tone: Tone;
  title: string;
  detail: string;
}> = ({ tone, title, detail }) => (
  <div className={`rounded-xl border px-3 py-3 ${toneClass[tone].card}`}>
    <p className="text-sm font-bold text-white">{title}</p>
    <p className={`mt-1 text-xs leading-relaxed ${toneClass[tone].text}`}>{detail}</p>
  </div>
);

const BandLabel: React.FC<{ tone: Tone; children: React.ReactNode }> = ({ tone, children }) => (
  <p className="mb-3 flex items-center gap-2 text-[11px] font-bold uppercase tracking-wider text-white">
    <span className={`h-2.5 w-2.5 rounded-full ${toneClass[tone].chip}`} />
    {children}
  </p>
);

const FlowLabel: React.FC<{ label: string }> = ({ label }) => (
  <div className="flex items-center justify-center gap-2 py-3 text-xs font-semibold text-slate-200">
    <ArrowDown size={14} className="text-slate-400" />
    <span className="rounded-full border border-slate-600 bg-slate-950 px-2.5 py-1">{label}</span>
  </div>
);

const applications = [
  { title: 'Designed App', detail: 'Blueprint, business context, and AI context.' },
  { title: 'Business application', detail: 'Owns its screens and records, then sends an intent.' },
  { title: 'Dashboard', detail: 'Authors, simulates, and observes through the API.' },
  { title: 'MCP client', detail: 'Designs and operates through FlowOS tools.' }
];

const kernelTop = [
  { title: 'Law', detail: 'State machine. What is legal.' },
  { title: 'Work', detail: 'Workflow. What happens next.' },
  { title: 'Truth', detail: 'Append-only event log.' }
];

const kernelBottom = [
  { title: 'Inbox', detail: 'Human task and Smart Actions.' },
  { title: 'Packet', detail: 'Prompt, data, tools, and legal events.' },
  { title: 'Commit', detail: 'Auto-commit or park on the Law host.' }
];

const outside = [
  { title: 'PostgreSQL', detail: 'Definitions, instances, jobs, and audit.', tag: 'records' },
  { title: 'Tenant systems', detail: 'CRM, payments, and documents.', tag: 'reads' },
  { title: 'Models', detail: 'Hosted, bring-your-own, or local.', tag: 'calls' }
];

const cycle = [
  { tone: 'blue' as Tone, title: 'Application', detail: 'Publishes an event or completes a task.' },
  { tone: 'purple' as Tone, title: 'FlowOS', detail: 'Checks policy, Law, and the step graph.' },
  { tone: 'cyan' as Tone, title: 'Agent worker', detail: 'Reads the DecisionPacket and suggests one legal event.' },
  { tone: 'purple' as Tone, title: 'Commit', detail: 'Publishes when confidence is in policy, or parks a Smart Action.' },
  { tone: 'blue' as Tone, title: 'Application', detail: 'Reads the new state, step, and event history.' }
];

const roles = [
  { name: 'Designed App', keeps: 'The process: WorkflowClass, business context, AI context', leaves: 'Live state' },
  { name: 'Business application', keeps: 'Its own screens and records, then an intent', leaves: 'The next legal state' },
  { name: 'Dashboard', keeps: 'Authoring, simulation, and tenant operations', leaves: 'Business decisions' },
  { name: 'Agent worker', keeps: 'Model calls and context processing', leaves: 'publish_event authority' },
  { name: 'FlowOS', keeps: 'Law, Work, Truth, policy, inbox, and commit', leaves: 'The tenant product UI' }
];

export const SystemArchitecture: React.FC = () => (
  <div className="space-y-8">
    <div>
      <h2 className="text-xl font-bold text-white">System architecture</h2>
      <p className="mt-2 max-w-3xl text-sm leading-relaxed text-slate-300">
        FlowOS is the operating system. A Designed App is the process that runs on it. The application sends intent. FlowOS decides whether that intent is legal and what work happens next.
      </p>
    </div>

    <div className="flex flex-wrap gap-2" aria-label="Architecture legend">
      {([
        ['blue', 'Applications'],
        ['purple', 'FlowOS'],
        ['cyan', 'Agent worker'],
        ['orange', 'Outside systems']
      ] as Array<[Tone, string]>).map(([tone, label]) => (
        <span key={label} className={`inline-flex items-center gap-2 rounded-full border px-3 py-1 text-xs font-semibold text-white ${toneClass[tone].band}`}>
          <span className={`h-2 w-2 rounded-full ${toneClass[tone].chip}`} />
          {label}
        </span>
      ))}
    </div>

    <section aria-label="FlowOS and the applications on it" className="space-y-0">
      <div className={`rounded-2xl border p-4 ${toneClass.blue.band}`}>
        <BandLabel tone="blue">Applications on FlowOS</BandLabel>
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          {applications.map(item => (
            <NodeCard key={item.title} tone="blue" title={item.title} detail={item.detail} />
          ))}
        </div>
      </div>

      <FlowLabel label="intent" />

      <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_240px]">
        <div className={`rounded-2xl border p-4 ${toneClass.purple.band}`}>
          <BandLabel tone="purple">FlowOS</BandLabel>
          <div className={`mb-3 rounded-xl border px-3 py-2 ${toneClass.purple.card}`}>
            <p className="text-sm font-bold text-white">API and MCP</p>
            <p className="mt-1 text-xs text-purple-200">Same engine. Tenant and capability checks. Policy gate.</p>
          </div>
          <div className="grid gap-3 md:grid-cols-3">
            {kernelTop.map(item => (
              <NodeCard key={item.title} tone="purple" title={item.title} detail={item.detail} />
            ))}
          </div>
          <div className="mt-3 grid gap-3 md:grid-cols-3">
            {kernelBottom.map(item => (
              <NodeCard key={item.title} tone="purple" title={item.title} detail={item.detail} />
            ))}
          </div>
        </div>

        <div className={`flex flex-col justify-between rounded-2xl border p-4 ${toneClass.cyan.band}`}>
          <div>
            <BandLabel tone="cyan">Agent worker</BandLabel>
            <NodeCard tone="cyan" title="Server, tenant box, or client" detail="Runs the model. Receives context. Returns a suggestion." />
          </div>
          <div className="mt-4 space-y-2 text-xs font-semibold">
            <p className="rounded-lg border border-cyan-400/40 bg-slate-950/70 px-3 py-2 text-cyan-100">context in</p>
            <p className="rounded-lg border border-cyan-400/40 bg-slate-950/70 px-3 py-2 text-cyan-100">suggestion out</p>
          </div>
        </div>
      </div>

      <FlowLabel label="records, reads, and calls" />

      <div className={`rounded-2xl border p-4 ${toneClass.orange.band}`}>
        <BandLabel tone="orange">Outside systems</BandLabel>
        <div className="grid gap-3 md:grid-cols-3">
          {outside.map(item => (
            <div key={item.title}>
              <p className="mb-2 text-[11px] font-bold uppercase tracking-wider text-orange-200">{item.tag}</p>
              <NodeCard tone="orange" title={item.title} detail={item.detail} />
            </div>
          ))}
        </div>
      </div>
    </section>

    <section aria-label="How one application action moves" className="space-y-3">
      <h3 className="text-base font-bold text-white">How one action moves</h3>
      <ol className="grid gap-3 lg:grid-cols-5">
        {cycle.map((step, index) => (
          <li key={`${step.title}-${index}`} className={`relative rounded-xl border p-3 ${toneClass[step.tone].band}`}>
            <div className="mb-2 flex items-center justify-between">
              <span className={`flex h-6 w-6 items-center justify-center rounded-full text-xs font-bold text-slate-950 ${toneClass[step.tone].chip}`}>
                {index + 1}
              </span>
              {index < cycle.length - 1 && (
                <ArrowRight size={14} className="hidden text-slate-400 lg:block" />
              )}
            </div>
            <p className="text-sm font-bold text-white">{step.title}</p>
            <p className="mt-1 text-xs leading-relaxed text-slate-300">{step.detail}</p>
          </li>
        ))}
      </ol>
    </section>

    <section aria-label="What each part keeps" className="overflow-hidden rounded-2xl border border-slate-700">
      <div className="border-b border-slate-700 bg-slate-900/80 px-4 py-3">
        <h3 className="text-base font-bold text-white">What each part keeps</h3>
      </div>
      <div className="overflow-x-auto">
        <table className="w-full min-w-[640px] text-left text-sm">
          <thead className="bg-slate-950 text-xs uppercase tracking-wider text-slate-400">
            <tr>
              <th className="px-4 py-3 font-semibold">Part</th>
              <th className="px-4 py-3 font-semibold">Keeps</th>
              <th className="px-4 py-3 font-semibold">FlowOS keeps instead</th>
            </tr>
          </thead>
          <tbody>
            {roles.map(role => (
              <tr key={role.name} className="border-t border-slate-800">
                <td className="px-4 py-3 font-semibold text-white">{role.name}</td>
                <td className="px-4 py-3 text-slate-300">{role.keeps}</td>
                <td className="px-4 py-3 text-slate-300">{role.leaves}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>

    <section className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
      {[
        { icon: Workflow, title: 'Designed App', detail: 'One process template pinned to a FlowOS release.' },
        { icon: Building2, title: 'Business application', detail: 'ExpenseApp or a tenant UI. It reacts after FlowOS decides.' },
        { icon: LayoutDashboard, title: 'This dashboard', detail: 'Governance and simulation. State still changes only through commands.' },
        { icon: Bot, title: 'Agent path', detail: 'A worker can run on another machine. Only FlowOS commits.' }
      ].map(item => (
        <div key={item.title} className="rounded-xl border border-slate-700 bg-slate-950 p-4">
          <item.icon size={16} className="text-slate-300" />
          <p className="mt-2 text-sm font-bold text-white">{item.title}</p>
          <p className="mt-1 text-xs leading-relaxed text-slate-400">{item.detail}</p>
        </div>
      ))}
    </section>

    <p className="flex flex-wrap items-center gap-3 text-xs text-slate-500">
      <span className="inline-flex items-center gap-1"><Server size={12} /> Law host</span>
      <span className="inline-flex items-center gap-1"><Database size={12} /> PostgreSQL</span>
      <span className="inline-flex items-center gap-1"><Scale size={12} /> Policy gate</span>
      <span className="inline-flex items-center gap-1"><Sparkles size={12} /> DecisionPacket</span>
    </p>
  </div>
);
