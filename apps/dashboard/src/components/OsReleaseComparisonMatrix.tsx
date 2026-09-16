import React, { useState } from "react";
import { CheckCircle2, Layers, Scale, XCircle, Zap } from "lucide-react";
import { usePlatformMetrics } from "../platformMetrics";
import {
  COMPARISON_LOSSES,
  COMPARISON_PLATFORMS,
  OS1_PILLARS,
  fitClass,
  fitLabel,
  type ComparisonPlatform,
} from "../comparisonMatrix";

export const OsReleaseComparisonMatrix: React.FC = () => {
  const { mcpTools } = usePlatformMetrics();
  const [selectedId, setSelectedId] = useState(COMPARISON_PLATFORMS[1].id);
  const selected =
    COMPARISON_PLATFORMS.find((p) => p.id === selectedId) ?? COMPARISON_PLATFORMS[1];

  return (
    <div className="space-y-6">
      <div className="bg-slate-900 border border-indigo-500/30 p-6 rounded-3xl">
        <div className="flex flex-col md:flex-row md:items-center justify-between gap-6">
          <div>
            <div className="flex items-center gap-2 mb-2">
              <span className="px-3 py-1 rounded-full text-xs font-bold bg-indigo-500/20 text-indigo-300 border border-indigo-500/30 flex items-center gap-1.5">
                <Scale size={13} />
                OS-1 Honesty Gate · GREEN
              </span>
            </div>
            <h2 className="text-2xl md:text-3xl font-extrabold text-white tracking-tight">
              FlowOS vs other orchestration platforms
            </h2>
            <p className="text-xs md:text-sm text-slate-300 mt-2 max-w-3xl leading-relaxed">
              Native = first-class kernel. Custom = possible in app code. Dash = not that product's job.
              Scored against FlowOS OS-1, not market share or hyperscale. Camunda is the closest OS-shaped rival; n8n is the wrong class.
            </p>
          </div>
          <div className="bg-slate-950/80 p-3 rounded-2xl border border-slate-800 shrink-0">
            <div className="text-[11px] text-slate-400">FlowOS differentiator</div>
            <div className="text-sm font-bold text-amber-400 font-mono">
              Dual-kernel Law + {mcpTools}-tool MCP
            </div>
          </div>
        </div>
      </div>

      <div className="bg-slate-900 border border-slate-800 rounded-3xl overflow-hidden">
        <div className="p-4 border-b border-slate-800 bg-slate-950/60 flex items-center justify-between">
          <span className="text-xs font-bold text-slate-200 uppercase tracking-wider flex items-center gap-2">
            <Layers size={14} className="text-blue-400" />
            OS-1 capability matrix
          </span>
          <span className="text-[11px] text-slate-400">Native / Custom / —</span>
        </div>
        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs border-collapse">
            <thead>
              <tr className="bg-slate-950 text-slate-400 text-[11px] uppercase border-b border-slate-800">
                <th className="p-3.5 pl-5 font-bold">OS-1 pillar</th>
                {COMPARISON_PLATFORMS.map((platform) => (
                  <th
                    key={platform.id}
                    className={`p-3.5 font-semibold ${
                      platform.id === "flowos"
                        ? "text-blue-400 bg-blue-950/20 border-x border-blue-500/20"
                        : ""
                    }`}
                  >
                    {platform.name}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-800/80">
              {OS1_PILLARS.map((pillar, index) => (
                <tr key={pillar} className="hover:bg-slate-850/50">
                  <td className="p-3.5 pl-5 font-bold text-slate-300">{pillar}</td>
                  {COMPARISON_PLATFORMS.map((platform) => {
                    const score = platform.scores[index];
                    return (
                      <td
                        key={`${platform.id}-${pillar}`}
                        className={`p-3.5 ${
                          platform.id === "flowos" ? "bg-blue-950/20 border-x border-blue-500/20" : ""
                        } ${fitClass(score, platform.id === "flowos")}`}
                      >
                        {fitLabel(score)}
                      </td>
                    );
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      <div>
        <p className="text-[11px] font-bold text-slate-400 uppercase tracking-wider mb-2">
          When to pick
        </p>
        <div className="flex flex-wrap gap-2">
          {COMPARISON_PLATFORMS.map((platform) => (
            <button
              key={platform.id}
              type="button"
              onClick={() => setSelectedId(platform.id)}
              className={`px-3 py-1.5 rounded-xl text-xs font-semibold transition-all ${
                selectedId === platform.id
                  ? "bg-blue-600 text-white"
                  : "bg-slate-800 text-slate-400 hover:text-white hover:bg-slate-750"
              }`}
            >
              {platform.name}
            </button>
          ))}
        </div>
      </div>

      <PlatformDeepDive platform={selected} />

      <div className="bg-slate-900 border border-slate-800 rounded-3xl overflow-hidden">
        <div className="p-4 border-b border-slate-800 bg-slate-950/60">
          <span className="text-xs font-bold text-slate-200 uppercase tracking-wider">
            Honest losses for FlowOS
          </span>
        </div>
        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs">
            <thead>
              <tr className="bg-slate-950 text-slate-400 text-[11px] uppercase border-b border-slate-800">
                <th className="p-3.5 pl-5">If the buyer needs</th>
                <th className="p-3.5">Buy instead</th>
                <th className="p-3.5 pr-5">Why</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-800/80 text-slate-300">
              {COMPARISON_LOSSES.map((row) => (
                <tr key={row.buy}>
                  <td className="p-3.5 pl-5 font-semibold text-slate-200">{row.need}</td>
                  <td className="p-3.5 text-amber-300 font-semibold">{row.buy}</td>
                  <td className="p-3.5 pr-5 text-slate-400">{row.why}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
};

const PlatformDeepDive: React.FC<{ platform: ComparisonPlatform }> = ({ platform }) => (
  <div className="bg-slate-900 border border-slate-800 rounded-3xl p-6 space-y-4">
    <div className="flex items-start justify-between border-b border-slate-800 pb-3">
      <div>
        <h3 className="text-lg font-bold text-white">{platform.name}</h3>
        <span className="text-[11px] text-slate-400 font-medium">{platform.category}</span>
      </div>
    </div>
    <p className="text-xs text-slate-300 bg-slate-950/80 p-3 rounded-xl border border-slate-800/80">
      {platform.pickWhen}
    </p>
    <div>
      <span className="text-[11px] font-bold text-emerald-400 uppercase tracking-wider flex items-center gap-1.5 mb-1.5">
        <CheckCircle2 size={13} /> They win
      </span>
      <p className="text-xs text-slate-400">{platform.theyWin}</p>
    </div>
    <div>
      <span className="text-[11px] font-bold text-rose-400 uppercase tracking-wider flex items-center gap-1.5 mb-1.5">
        <XCircle size={13} /> They lose vs the FlowOS OS bar
      </span>
      <p className="text-xs text-slate-400">{platform.theyLose}</p>
    </div>
    {platform.id !== "flowos" && (
      <div className="pt-2 border-t border-slate-800">
        <span className="text-[11px] font-bold text-amber-400 uppercase tracking-wider flex items-center gap-1.5 mb-1">
          <Zap size={13} /> FlowOS is for
        </span>
        <p className="text-xs text-amber-200/90 bg-amber-950/20 p-3 rounded-xl border border-amber-500/20">
          {COMPARISON_PLATFORMS[0].pickWhen}
        </p>
      </div>
    )}
  </div>
);
