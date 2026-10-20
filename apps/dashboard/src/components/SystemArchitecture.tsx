import React, { useEffect, useId, useRef, useState } from 'react';
import mermaid from 'mermaid';

const actionChart = `sequenceDiagram
  participant App as Business application
  participant OS as FlowOS
  participant Agent as Agent worker
  participant Human as Inbox
  App->>OS: Publish an event or complete a task
  OS->>OS: Check policy, Law, and Work
  OS->>Agent: DecisionPacket
  Agent->>OS: Suggested legal event
  alt Confidence is inside auto-commit
    OS->>OS: Publish as the agent
  else A person must confirm
    OS->>Human: Park a Smart Action
    Human->>OS: Confirm
  end
  OS-->>App: New state, step, and history`;

let renderQueue: Promise<void> = Promise.resolve();

const MermaidGraph: React.FC<{ chart: string; label: string }> = ({ chart, label }) => {
  const reactId = useId().replace(/:/g, '');
  const containerRef = useRef<HTMLDivElement>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    const render = async () => {
      try {
        mermaid.initialize({
          startOnLoad: false,
          theme: 'base',
          securityLevel: 'loose',
          fontFamily: 'ui-sans-serif, system-ui, sans-serif',
          flowchart: { htmlLabels: true, curve: 'basis', padding: 12, nodeSpacing: 24, rankSpacing: 36, useMaxWidth: true },
          themeVariables: {
            darkMode: true,
            background: '#020617',
            fontSize: '14px',
            lineColor: '#e2e8f0',
            textColor: '#f8fafc',
            actorBkg: '#1e3a8a',
            actorTextColor: '#eff6ff',
            actorBorder: '#93c5fd',
            signalColor: '#e2e8f0',
            signalTextColor: '#f8fafc',
            labelBoxBkgColor: '#0f172a',
            labelTextColor: '#f8fafc'
          }
        });
        const result = await mermaid.render(`architecture-${reactId}-${label}`, chart);
        if (!cancelled && containerRef.current) {
          containerRef.current.innerHTML = result.svg;
          const svg = containerRef.current.querySelector('svg');
          if (svg) {
            svg.removeAttribute('height');
            svg.style.maxWidth = '100%';
            svg.style.height = 'auto';
          }
          setError(null);
        }
      } catch (exception) {
        if (!cancelled) {
          setError(exception instanceof Error ? exception.message : 'The architecture graph could not be drawn.');
        }
      }
    };

    renderQueue = renderQueue.then(render, render);
    return () => {
      cancelled = true;
    };
  }, [chart, label, reactId]);

  return (
    <div className="overflow-x-auto rounded-2xl border border-slate-700 bg-slate-950 p-3">
      {error && (
        <p className="p-4 text-sm text-rose-300">{error}</p>
      )}
      <div ref={containerRef} className="flex min-h-[280px] w-full justify-center [&_svg]:mx-auto" />
    </div>
  );
};

const DiagramNode: React.FC<{
  x: number;
  y: number;
  width: number;
  height: number;
  title: string;
  detail?: string;
  fill: string;
  stroke: string;
}> = ({ x, y, width, height, title, detail, fill, stroke }) => {
  const left = Number(x);
  const top = Number(y);
  const boxWidth = Number(width);
  const boxHeight = Number(height);
  return (
  <g>
    <rect x={left} y={top} width={boxWidth} height={boxHeight} rx={10} fill={fill} stroke={stroke} strokeWidth={1.5} />
    <text x={left + boxWidth / 2} y={top + (detail ? 36 : boxHeight / 2 + 6)} textAnchor="middle" fill="#f8fafc" fontSize={18} fontWeight={700}>
      {title}
    </text>
    {detail && (
      <text x={left + boxWidth / 2} y={top + 60} textAnchor="middle" fill="#e2e8f0" fontSize={14}>
        {detail}
      </text>
    )}
  </g>
  );
};

const ArchitectureMap: React.FC = () => (
  <svg viewBox="0 0 960 760" role="img" aria-label="FlowOS application architecture" className="h-auto w-full">
    <defs>
      <marker id="arch-arrow" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="7" markerHeight="7" orient="auto">
        <path d="M0 0 L10 5 L0 10 Z" fill="#e2e8f0" />
      </marker>
    </defs>
    <rect x="8" y="8" width="944" height="150" rx="16" fill="#172554" stroke="#60a5fa" />
    <text x="24" y="32" fill="#dbeafe" fontSize="16" fontWeight="700">APPLICATIONS ON FLOWOS</text>
    <DiagramNode x={20} y={48} width={222} height={92} title="Designed App" detail="blueprint and AI context" fill="#1e3a8a" stroke="#93c5fd" />
    <DiagramNode x={254} y={48} width={222} height={92} title="Business application" detail="screens and records" fill="#1e3a8a" stroke="#93c5fd" />
    <DiagramNode x={488} y={48} width={222} height={92} title="Dashboard" detail="author and simulate" fill="#1e3a8a" stroke="#93c5fd" />
    <DiagramNode x={722} y={48} width={214} height={92} title="MCP client" detail="design and operate" fill="#1e3a8a" stroke="#93c5fd" />

    <path d="M480 158 L480 214" fill="none" stroke="#e2e8f0" strokeWidth="1.5" markerEnd="url(#arch-arrow)" />
    <text x="492" y="190" fill="#e2e8f0" fontSize="15">intent</text>

    <rect x="8" y="196" width="680" height="360" rx="16" fill="#2e1065" stroke="#c084fc" />
    <text x="24" y="222" fill="#f3e8ff" fontSize="16" fontWeight="700">FLOWOS</text>
    <DiagramNode x={24} y={236} width={648} height={52} title="API and MCP · policy gate" fill="#5b21b6" stroke="#d8b4fe" />
    <DiagramNode x={24} y={304} width={206} height={84} title="Law" detail="what is legal" fill="#5b21b6" stroke="#d8b4fe" />
    <DiagramNode x={242} y={304} width={206} height={84} title="Work" detail="what happens next" fill="#5b21b6" stroke="#d8b4fe" />
    <DiagramNode x={460} y={304} width={212} height={84} title="Truth" detail="event log" fill="#5b21b6" stroke="#d8b4fe" />
    <DiagramNode x={24} y={404} width={206} height={84} title="Inbox" detail="human task" fill="#5b21b6" stroke="#d8b4fe" />
    <DiagramNode x={242} y={404} width={206} height={84} title="Commit" detail="publish or park" fill="#5b21b6" stroke="#d8b4fe" />
    <DiagramNode x={460} y={404} width={212} height={84} title="DecisionPacket" detail="prompt, data, tools" fill="#5b21b6" stroke="#d8b4fe" />

    <rect x="704" y="196" width="248" height="292" rx="16" fill="#083344" stroke="#67e8f9" />
    <text x="720" y="222" fill="#cffafe" fontSize="16" fontWeight="700">AGENT WORKER</text>
    <DiagramNode x={720} y={238} width={216} height={92} title="Any machine" detail="runs the model" fill="#155e75" stroke="#67e8f9" />
    <path d="M672 430 L704 430" fill="none" stroke="#e2e8f0" strokeWidth="1.5" markerEnd="url(#arch-arrow)" />
    <path d="M704 456 L672 456" fill="none" stroke="#e2e8f0" strokeWidth="1.5" markerEnd="url(#arch-arrow)" />
    <text x="828" y="360" textAnchor="middle" fill="#ecfeff" fontSize="15">context in</text>
    <text x="828" y="384" textAnchor="middle" fill="#ecfeff" fontSize="15">suggestion out</text>

    <rect x="8" y="590" width="944" height="150" rx="16" fill="#431407" stroke="#fdba74" />
    <text x="24" y="616" fill="#ffedd5" fontSize="16" fontWeight="700">OUTSIDE SYSTEMS</text>
    <DiagramNode x={24} y={632} width={280} height={88} title="PostgreSQL" detail="definitions, instances, audit" fill="#9a3412" stroke="#fdba74" />
    <DiagramNode x={320} y={632} width={280} height={88} title="Tenant systems" detail="CRM, payments, documents" fill="#9a3412" stroke="#fdba74" />
    <DiagramNode x={704} y={632} width={228} height={88} title="Models" detail="hosted, BYO, or local" fill="#9a3412" stroke="#fdba74" />
    <path d="M127 488 L127 632" fill="none" stroke="#e2e8f0" strokeWidth="1.5" markerEnd="url(#arch-arrow)" />
    <text x="136" y="560" fill="#e2e8f0" fontSize="15">records</text>
    <path d="M566 488 L566 632" fill="none" stroke="#e2e8f0" strokeWidth="1.5" markerEnd="url(#arch-arrow)" />
    <text x="574" y="560" fill="#e2e8f0" fontSize="15">reads</text>
    <path d="M828 488 L828 632" fill="none" stroke="#e2e8f0" strokeWidth="1.5" markerEnd="url(#arch-arrow)" />
    <text x="836" y="530" fill="#e2e8f0" fontSize="15">calls</text>
  </svg>
);

const legend = [
  { color: 'bg-blue-500', label: 'Applications' },
  { color: 'bg-purple-500', label: 'FlowOS' },
  { color: 'bg-cyan-500', label: 'Agent worker' },
  { color: 'bg-orange-500', label: 'Outside systems' }
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
      {legend.map(item => (
        <span key={item.label} className="inline-flex items-center gap-2 rounded-full border border-slate-700 bg-slate-900 px-3 py-1 text-xs font-semibold text-white">
          <span className={`h-2.5 w-2.5 rounded-full ${item.color}`} />
          {item.label}
        </span>
      ))}
    </div>

    <section className="space-y-3" aria-label="FlowOS and the applications on it">
      <h3 className="text-base font-bold text-white">Full application architecture</h3>
      <div className="overflow-x-auto rounded-2xl border border-slate-700 bg-slate-950 p-3">
        <ArchitectureMap />
      </div>
    </section>

    <section className="space-y-3" aria-label="How one application action moves">
      <h3 className="text-base font-bold text-white">How one action moves</h3>
      <MermaidGraph chart={actionChart} label="architecture-sequence" />
    </section>
  </div>
);
