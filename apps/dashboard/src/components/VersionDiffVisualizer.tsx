import React, { useRef, useEffect } from 'react';
import mermaid from 'mermaid';
import { FileDiff, AlertCircle } from 'lucide-react';

interface Props {
  baseDef: any;
  newDef: any;
}

const MermaidViewer: React.FC<{ chart: string }> = ({ chart }) => {
  const containerRef = useRef<HTMLDivElement>(null);
  
  useEffect(() => {
    mermaid.initialize({
      startOnLoad: false,
      theme: 'dark',
      securityLevel: 'loose',
      fontFamily: 'ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace',
    });
    
    if (containerRef.current) {
      const id = 'mermaid-diff-' + Math.random().toString(36).substring(7);
      mermaid.render(id, chart).then((result) => {
        if (containerRef.current) {
          containerRef.current.innerHTML = result.svg;
        }
      }).catch(err => {
        console.error('Mermaid render error', err);
        if (containerRef.current) {
          containerRef.current.innerHTML = `<div class="text-rose-400 text-xs p-4 bg-rose-900/30 rounded-lg">Error rendering diff: ${err.message}</div>`;
        }
      });
    }
  }, [chart]);

  return <div ref={containerRef} className="flex justify-center p-4 overflow-auto min-h-[400px] w-full" />;
};

export const VersionDiffVisualizer: React.FC<Props> = ({ baseDef, newDef }) => {
  // Safe case-insensitive helper to extract properties
  const getProp = (obj: any, ...keys: string[]) => {
    if (!obj) return undefined;
    for (const key of keys) {
      if (obj[key] !== undefined) return obj[key];
      const lower = key.toLowerCase();
      if (obj[lower] !== undefined) return obj[lower];
      const camel = key.charAt(0).toLowerCase() + key.slice(1);
      if (obj[camel] !== undefined) return obj[camel];
    }
    return undefined;
  };

  const extractSteps = (def: any) => {
    const wfObj = getProp(def, 'workflow', 'Workflow') || def;
    const rawSteps: any[] = getProp(wfObj, 'steps', 'Steps') || [];
    return rawSteps.map(s => ({
        stepId: getProp(s, 'stepId', 'StepId') || '',
        stepType: (getProp(s, 'stepType', 'StepType') || 'Command').toString(),
        label: getProp(s, 'label', 'Label') || getProp(s, 'stepId', 'StepId') || '',
        roles: (getProp(s, 'allowedRoles', 'AllowedRoles', 'requiredRoles', 'RequiredRoles') || []).join(','),
        nextSteps: getProp(s, 'nextSteps', 'NextSteps') || {},
        slaStr: JSON.stringify(getProp(s, 'sla', 'Sla', 'SLA') || {})
    }));
  };

  const baseSteps = extractSteps(baseDef);
  const newSteps = extractSteps(newDef);

  const safeId = (id: string) => (id || '').toString().replace(/[^a-zA-Z0-9_]/g, '_');

  const buildDiffMermaid = () => {
    let chart = 'flowchart TD\n';
    chart += 'classDef added fill:#064e3b,stroke:#10b981,color:#a7f3d0,stroke-width:2px,stroke-dasharray: 5 5\n';
    chart += 'classDef removed fill:#4c1d95,stroke:#a855f7,color:#e9d5ff,stroke-width:2px,stroke-dasharray: 5 5\n'; // Using purple for removed for better dark mode visibility
    chart += 'classDef modified fill:#1e3a8a,stroke:#3b82f6,color:#bfdbfe,stroke-width:2px\n';
    chart += 'classDef unchanged fill:#1e293b,stroke:#334155,color:#94a3b8\n\n';

    const allStepIds = new Set([
      ...baseSteps.map(s => s.stepId.toLowerCase()),
      ...newSteps.map(s => s.stepId.toLowerCase())
    ]);

    const edges: { from: string, to: string, label: string, status: 'added' | 'removed' | 'unchanged' }[] = [];

    allStepIds.forEach(idLower => {
        const b = baseSteps.find(s => s.stepId.toLowerCase() === idLower);
        const n = newSteps.find(s => s.stepId.toLowerCase() === idLower);

        let status = 'unchanged';
        let displayNode = n || b;

        if (b && !n) {
            status = 'removed';
        } else if (!b && n) {
            status = 'added';
        } else if (b && n) {
            // Check if modified
            if (b.stepType !== n.stepType || b.roles !== n.roles || b.slaStr !== n.slaStr) {
                status = 'modified';
            }
        }

        if (!displayNode) return;

        let displayLabel = displayNode.roles.length > 0 ? `${displayNode.label}<br/>(Role: ${displayNode.roles})` : displayNode.label;
        if (status === 'removed') displayLabel = `[REMOVED]<br/>${displayLabel}`;
        if (status === 'added') displayLabel = `[NEW]<br/>${displayLabel}`;
        if (status === 'modified') displayLabel = `[MODIFIED]<br/>${displayLabel}`;

        let shapeStart = '(['; let shapeEnd = '])';
        const t = displayNode.stepType.toLowerCase();
        if (t.includes('decision') || t.includes('choice')) { shapeStart = '{'; shapeEnd = '}'; }

        chart += `  ${safeId(displayNode.stepId)}${shapeStart}"${displayLabel}"${shapeEnd}:::${status}\n`;

        // Edges logic
        if (b) {
            Object.entries(b.nextSteps).forEach(([evt, target]) => {
                const stillExists = n && Object.keys(n.nextSteps).includes(evt) && n.nextSteps[evt] === target;
                edges.push({
                    from: b.stepId,
                    to: target as string,
                    label: evt,
                    status: stillExists ? 'unchanged' : 'removed'
                });
            });
        }
        if (n) {
            Object.entries(n.nextSteps).forEach(([evt, target]) => {
                const existedBefore = b && Object.keys(b.nextSteps).includes(evt) && b.nextSteps[evt] === target;
                if (!existedBefore) {
                    edges.push({
                        from: n.stepId,
                        to: target as string,
                        label: evt,
                        status: 'added'
                    });
                }
            });
        }
    });

    chart += '\n';

    // Draw Edges
    let edgeIndex = 0;
    edges.forEach(e => {
        chart += `  ${safeId(e.from)} -->|"${e.label}"| ${safeId(e.to)}\n`;
        if (e.status === 'added') {
            chart += `  linkStyle ${edgeIndex} stroke:#10b981,stroke-width:2px\n`;
        } else if (e.status === 'removed') {
            chart += `  linkStyle ${edgeIndex} stroke:#a855f7,stroke-width:2px,stroke-dasharray: 5 5\n`;
        } else {
            chart += `  linkStyle ${edgeIndex} stroke:#475569,stroke-width:1px\n`;
        }
        edgeIndex++;
    });

    return chart;
  };

  return (
    <div className="bg-slate-900 border border-slate-800 rounded-xl overflow-hidden flex flex-col">
      <div className="p-3 bg-slate-950 border-b border-slate-800 flex justify-between items-center">
        <h3 className="text-sm font-bold text-white flex items-center gap-2">
          <FileDiff size={16} className="text-blue-400" /> Version Control Diff Viewer
        </h3>
        <div className="flex gap-4 text-[10px] font-bold uppercase tracking-wider">
            <span className="text-emerald-400 flex items-center gap-1"><div className="w-2 h-2 rounded bg-emerald-400"></div> Added</span>
            <span className="text-purple-400 flex items-center gap-1"><div className="w-2 h-2 rounded bg-purple-400"></div> Removed</span>
            <span className="text-blue-400 flex items-center gap-1"><div className="w-2 h-2 rounded bg-blue-400"></div> Modified</span>
            <span className="text-slate-400 flex items-center gap-1"><div className="w-2 h-2 rounded bg-slate-500"></div> Unchanged</span>
        </div>
      </div>
      <div className="bg-slate-950/50 p-2">
        <MermaidViewer chart={buildDiffMermaid()} />
      </div>
    </div>
  );
};
