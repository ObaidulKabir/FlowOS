import React, { useState } from "react";
import { CheckCircle2, Layers, Scale, XCircle, Zap } from "lucide-react";
import { usePlatformMetrics } from "../platformMetrics";
import {
  AI_TASK_AUTOMATION_ROWS,
  COMPARISON_LOSSES,
  COMPARISON_PLATFORMS,
  MCP_COMPARISON_SOURCES,
  MCP_CONTROL_PLANE_ROWS,
  MCP_STRATEGIC_PRIORITIES,
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
              Governed MCP + dual-kernel Law
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

      <div className="bg-slate-900 border border-cyan-500/20 rounded-3xl overflow-hidden">
        <div className="p-4 border-b border-slate-800 bg-slate-950/60 flex flex-col md:flex-row md:items-center justify-between gap-2">
          <div>
            <span className="text-xs font-bold text-slate-200 uppercase tracking-wider flex items-center gap-2">
              <Zap size={14} className="text-cyan-400" />
              MCP competitive reality
            </span>
            <p className="text-[11px] text-slate-400 mt-1">
              Every serious rival now has an MCP story. FlowOS must win on governed outcomes, not protocol presence or raw tool count.
            </p>
          </div>
          <span className="text-[11px] text-cyan-300 border border-cyan-500/20 bg-cyan-950/30 px-2.5 py-1 rounded-lg shrink-0">
            FlowOS registry: {mcpTools} discoverable tools
          </span>
        </div>
        <div className="overflow-x-auto">
          <table className="w-full min-w-[1280px] text-left text-xs border-collapse">
            <thead>
              <tr className="bg-slate-950 text-slate-400 text-[11px] uppercase border-b border-slate-800">
                <th className="p-3.5 pl-5 font-bold w-44">MCP capability</th>
                <th className="p-3.5 font-bold w-52">Why it matters</th>
                {COMPARISON_PLATFORMS.map((platform) => (
                  <th
                    key={`mcp-${platform.id}`}
                    className={`p-3.5 font-semibold min-w-44 ${
                      platform.id === "flowos"
                        ? "text-cyan-300 bg-cyan-950/20 border-x border-cyan-500/20"
                        : ""
                    }`}
                  >
                    {platform.name}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-800/80">
              {MCP_CONTROL_PLANE_ROWS.map((row) => (
                <tr key={row.capability} className="hover:bg-slate-850/50">
                  <td className="p-3.5 pl-5 font-bold text-slate-200 align-top">
                    {row.capability}
                  </td>
                  <td className="p-3.5 text-slate-500 align-top">{row.whyItMatters}</td>
                  {COMPARISON_PLATFORMS.map((platform) => (
                    <td
                      key={`${platform.id}-${row.capability}`}
                      className={`p-3.5 text-slate-400 align-top leading-relaxed ${
                        platform.id === "flowos"
                          ? "bg-cyan-950/20 border-x border-cyan-500/20 text-emerald-200"
                          : ""
                      }`}
                    >
                      {row.answers[platform.id]}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <div className="p-4 border-t border-slate-800 bg-slate-950/30">
          <div className="grid sm:grid-cols-2 xl:grid-cols-4 gap-3">
            {MCP_STRATEGIC_PRIORITIES.map((item) => (
              <div key={item.title} className="rounded-xl border border-slate-800 bg-slate-950/70 p-3">
                <div className="flex items-center gap-2">
                  <span className="text-[10px] font-bold text-cyan-300 bg-cyan-950/60 border border-cyan-500/20 px-1.5 py-0.5 rounded">
                    {item.priority}
                  </span>
                  <span className="text-xs font-bold text-slate-200">{item.title}</span>
                </div>
                <p className="text-[11px] text-slate-500 mt-2 leading-relaxed">{item.outcome}</p>
              </div>
            ))}
          </div>
          <div className="flex flex-wrap items-center gap-x-3 gap-y-1 mt-3 text-[10px] text-slate-500">
            <span>Official sources checked 2026-09-21:</span>
            {MCP_COMPARISON_SOURCES.map((source) => (
              <a
                key={source.href}
                href={source.href}
                target="_blank"
                rel="noreferrer"
                className="text-cyan-400 hover:text-cyan-300"
              >
                {source.label}
              </a>
            ))}
          </div>
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

      <div className="bg-slate-900 border border-slate-800 rounded-3xl overflow-hidden">
        <div className="p-4 border-b border-slate-800 bg-slate-950/60">
          <span className="text-xs font-bold text-slate-200 uppercase tracking-wider">
            AI task automation
          </span>
          <p className="text-[11px] text-slate-400 mt-1">
            After FlowOS hosted OpenAI: paid tenants run Agent/Either steps without a BYO key. Scored as product behavior, not marketing.
          </p>
        </div>
        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs border-collapse">
            <thead>
              <tr className="bg-slate-950 text-slate-400 text-[11px] uppercase border-b border-slate-800">
                <th className="p-3.5 pl-5 font-bold">Question</th>
                {COMPARISON_PLATFORMS.map((platform) => (
                  <th
                    key={`ai-${platform.id}`}
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
              {AI_TASK_AUTOMATION_ROWS.map((row) => (
                <tr key={row.question} className="hover:bg-slate-850/50">
                  <td className="p-3.5 pl-5 font-bold text-slate-300 align-top">{row.question}</td>
                  {COMPARISON_PLATFORMS.map((platform) => (
                    <td
                      key={`${platform.id}-${row.question}`}
                      className={`p-3.5 text-slate-400 align-top ${
                        platform.id === "flowos"
                          ? "bg-blue-950/20 border-x border-blue-500/20 text-emerald-200"
                          : ""
                      }`}
                    >
                      {row.answers[platform.id]}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
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
