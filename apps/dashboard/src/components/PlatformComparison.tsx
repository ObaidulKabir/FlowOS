import React from "react";
import { Bot, Cpu, Server, ShieldCheck } from "lucide-react";
import { usePlatformMetrics } from "../platformMetrics";
import { OsReleaseComparisonMatrix } from "./OsReleaseComparisonMatrix";

export const PlatformComparison: React.FC = () => {
  const { mcpTools } = usePlatformMetrics();

  return (
    <div className="space-y-8">
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <div className="p-4 rounded-xl bg-slate-950 border border-slate-800 space-y-2">
          <div className="w-8 h-8 rounded-lg bg-emerald-500/10 text-emerald-400 flex items-center justify-center">
            <ShieldCheck size={18} />
          </div>
          <h4 className="text-sm font-bold text-white">Dual-kernel Law + Work</h4>
          <p className="text-xs text-slate-400 leading-relaxed">
            State machine is Law. Workflow is Work. Class-backed live advance fails closed without a resolvable machine.
          </p>
        </div>
        <div className="p-4 rounded-xl bg-slate-950 border border-slate-800 space-y-2">
          <div className="w-8 h-8 rounded-lg bg-blue-500/10 text-blue-400 flex items-center justify-center">
            <Bot size={18} />
          </div>
          <h4 className="text-sm font-bold text-white">Native MCP control plane</h4>
          <p className="text-xs text-slate-400 leading-relaxed">
            {mcpTools} tools for design, simulate, bind, operate, and hosted DecisionPacket / autoCommit. Agents dry-run before publish.
          </p>
        </div>
        <div className="p-4 rounded-xl bg-slate-950 border border-slate-800 space-y-2">
          <div className="w-8 h-8 rounded-lg bg-purple-500/10 text-purple-400 flex items-center justify-center">
            <Cpu size={18} />
          </div>
          <h4 className="text-sm font-bold text-white">OS-1 GREEN</h4>
          <p className="text-xs text-slate-400 leading-relaxed">
            Tenant identity, role-filtered inbox, deny-only policy, capability bindings, simulation, paid runtime entitlement.
          </p>
        </div>
        <div className="p-4 rounded-xl bg-slate-950 border border-slate-800 space-y-2">
          <div className="w-8 h-8 rounded-lg bg-amber-500/10 text-amber-400 flex items-center justify-center">
            <Server size={18} />
          </div>
          <h4 className="text-sm font-bold text-white">Lightweight footprint</h4>
          <p className="text-xs text-slate-400 leading-relaxed">
            .NET 8 + PostgreSQL. No Zeebe, Cassandra, or Elasticsearch cluster required to run the OS kernels.
          </p>
        </div>
      </div>

      <OsReleaseComparisonMatrix />
    </div>
  );
};
