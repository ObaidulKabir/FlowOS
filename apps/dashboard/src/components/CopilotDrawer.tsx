import React, { useState } from 'react';
import { Sparkles, ArrowRight, CheckCircle2, AlertCircle, RefreshCw, X, HelpCircle } from 'lucide-react';
import { GenerateBlueprintCopilotResponse } from '../types';
import { api } from '../api/client';

interface CopilotDrawerProps {
  isOpen: boolean;
  onClose: () => void;
  currentBlueprint: any;
  onApplyBlueprint: (generated: GenerateBlueprintCopilotResponse) => void;
}

const PRESET_PROMPTS = [
  {
    title: 'Parallel KYC Verification',
    desc: 'Fork/Join concurrent biometric ID & financial screening with 24h SLA',
    prompt: 'Create a parallel KYC verification workflow that forks identity document verification and financial risk scoring concurrently, waits for both via WaitAll join barrier, requires compliance officer signoff, and has a 24h SLA escalation.'
  },
  {
    title: 'Loan Underwriting Decision Rules',
    desc: 'Dynamic LINQ credit score rules with fast-track disbursement & underwriter review',
    prompt: 'Commercial loan underwriting with intake, dynamic LINQ risk score decision rules (Score >= 720 fast-tracks disbursement, Score < 580 declines, otherwise routes to Underwriter human task with 48h SLA).'
  },
  {
    title: 'Insurance Claim with Saga Rollback',
    desc: 'Damage assessment, claim evaluation, payout webhook, and compensation revert on failure',
    prompt: 'Auto insurance claim process with vehicle damage assessment, medical evaluation, payout webhook notification, and automatic saga compensation rollback webhook on claim failure or dispute.'
  },
  {
    title: 'SecOps Access Request with SLA',
    desc: 'Access grant, manager approval with 24h timeout, and automated IAM webhook provisioning',
    prompt: 'Privileged access governance workflow where employee requests database access, manager gets human task with 24h SLA timeout escalating to SecOps Director, and approval triggers automated IAM provisioning webhook.'
  }
];

export const CopilotDrawer: React.FC<CopilotDrawerProps> = ({
  isOpen,
  onClose,
  currentBlueprint,
  onApplyBlueprint
}) => {
  const [prompt, setPrompt] = useState('');
  const [mode, setMode] = useState<'create' | 'refine' | 'template'>('create');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<GenerateBlueprintCopilotResponse | null>(null);

  if (!isOpen) return null;

  const handleGenerate = async (targetPrompt?: string) => {
    const promptToUse = targetPrompt || prompt;
    if (!promptToUse.trim()) return;

    setLoading(true);
    setError(null);

    try {
      const response = await api.copilotGenerate(
        promptToUse,
        mode === 'refine' ? currentBlueprint : undefined,
        mode
      );
      setResult(response);
    } catch (err: any) {
      setError(err.message || 'Failed to synthesize blueprint with AI Copilot');
    } finally {
      setLoading(false);
    }
  };

  const handleSelectPreset = (presetPrompt: string) => {
    setPrompt(presetPrompt);
    handleGenerate(presetPrompt);
  };

  const handleApply = () => {
    if (result) {
      onApplyBlueprint(result);
      onClose();
    }
  };

  return (
    <div className="fixed inset-y-0 right-0 w-full max-w-xl bg-slate-900 border-l border-slate-800 shadow-2xl z-50 flex flex-col transition-all duration-300">
      {/* Header */}
      <div className="p-4 border-b border-slate-800 flex items-center justify-between bg-slate-950/70 backdrop-blur">
        <div className="flex items-center space-x-2">
          <div className="w-8 h-8 rounded-lg bg-indigo-500/20 text-indigo-400 flex items-center justify-center border border-indigo-500/30">
            <Sparkles className="w-4 h-4 animate-pulse" />
          </div>
          <div>
            <h3 className="text-sm font-semibold text-white flex items-center gap-1.5">
              FlowOS AI Copilot
              <span className="px-2 py-0.5 text-[10px] font-medium bg-indigo-500/20 text-indigo-300 rounded border border-indigo-500/30">
                Blueprint Engine
              </span>
            </h3>
            <p className="text-xs text-slate-400">Natural Language to Production-Ready Workflow</p>
          </div>
        </div>
        <button
          onClick={onClose}
          className="p-1.5 text-slate-400 hover:text-white rounded-md hover:bg-slate-800 transition"
        >
          <X className="w-5 h-5" />
        </button>
      </div>

      {/* Content Body */}
      <div className="flex-1 overflow-y-auto p-5 space-y-5">
        {/* Mode Toggle */}
        <div className="flex bg-slate-950 p-1 rounded-lg border border-slate-800">
          <button
            onClick={() => setMode('create')}
            className={`flex-1 py-1.5 px-3 text-xs font-medium rounded-md transition ${
              mode === 'create'
                ? 'bg-indigo-600 text-white shadow-sm'
                : 'text-slate-400 hover:text-slate-200'
            }`}
          >
            Create New Blueprint
          </button>
          <button
            onClick={() => setMode('refine')}
            className={`flex-1 py-1.5 px-3 text-xs font-medium rounded-md transition ${
              mode === 'refine'
                ? 'bg-indigo-600 text-white shadow-sm'
                : 'text-slate-400 hover:text-slate-200'
            }`}
          >
            Refine Current Editor Blueprint
          </button>
          <button
            onClick={() => setMode('template')}
            className={`flex-1 py-1.5 px-3 text-xs font-medium rounded-md transition ${
              mode === 'template'
                ? 'bg-indigo-600 text-white shadow-sm'
                : 'text-slate-400 hover:text-slate-200'
            }`}
          >
            Reusable Template
          </button>
        </div>

        {/* Input Box */}
        <div className="space-y-2">
          <label className="text-xs font-semibold uppercase tracking-wider text-slate-400 flex items-center justify-between">
            <span>Describe Requirements</span>
            <span className="text-[11px] text-slate-500 font-normal">Supports Fork/Join, SLAs, Decision rules, Outbox</span>
          </label>
          <textarea
            value={prompt}
            onChange={(e) => setPrompt(e.target.value)}
            placeholder={
              mode === 'create'
                ? 'e.g., Create a high-value payment approval flow with parallel fraud and AML compliance checks, 48h SLA on manager signoff, and refund compensation webhook on failure...'
                : mode === 'template'
                  ? 'e.g., Create a generic two-level approval template with canonical Amount and Description fields...'
                : 'e.g., Add a 24h SLA timeout to the human task and attach a rollback webhook compensation hook...'
            }
            className="w-full h-28 bg-slate-950 border border-slate-800 rounded-lg p-3 text-xs text-white placeholder-slate-500 focus:outline-none focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500 transition resize-none font-mono"
          />
          <button
            disabled={loading || !prompt.trim()}
            onClick={() => handleGenerate()}
            className="w-full py-2.5 px-4 bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 disabled:cursor-not-allowed text-white text-xs font-semibold rounded-lg shadow-sm flex items-center justify-center space-x-2 transition"
          >
            {loading ? (
              <>
                <RefreshCw className="w-3.5 h-3.5 animate-spin" />
                <span>Synthesizing Blueprint Architecture...</span>
              </>
            ) : (
              <>
                <Sparkles className="w-3.5 h-3.5" />
                <span>{mode === 'refine' ? 'Refine Current Blueprint' : mode === 'template' ? 'Generate Reusable Template' : 'Generate Workflow Blueprint'}</span>
              </>
            )}
          </button>
        </div>

        {/* Error Display */}
        {error && (
          <div className="p-3 bg-red-950/40 border border-red-800/60 rounded-lg text-xs text-red-300 flex items-start space-x-2">
            <AlertCircle className="w-4 h-4 text-red-400 shrink-0 mt-0.5" />
            <div>
              <p className="font-semibold">Synthesis Error</p>
              <p className="text-red-300/80 mt-0.5">{error}</p>
            </div>
          </div>
        )}

        {/* Preset Prompt Cards */}
        <div className="space-y-2">
          <label className="text-xs font-semibold uppercase tracking-wider text-slate-400 flex items-center space-x-1.5">
            <HelpCircle className="w-3.5 h-3.5 text-slate-500" />
            <span>Preset Blueprint Templates</span>
          </label>
          <div className="grid grid-cols-1 gap-2">
            {PRESET_PROMPTS.map((preset, idx) => (
              <button
                key={idx}
                onClick={() => handleSelectPreset(preset.prompt)}
                disabled={loading}
                className="text-left p-2.5 bg-slate-950/70 hover:bg-slate-800/80 border border-slate-800/90 hover:border-slate-700 rounded-lg transition group"
              >
                <div className="flex items-center justify-between">
                  <span className="text-xs font-medium text-slate-200 group-hover:text-indigo-300 transition">
                    {preset.title}
                  </span>
                  <ArrowRight className="w-3 h-3 text-slate-600 group-hover:text-indigo-400 transform group-hover:translate-x-0.5 transition" />
                </div>
                <p className="text-[11px] text-slate-400 mt-1 line-clamp-1">{preset.desc}</p>
              </button>
            ))}
          </div>
        </div>

        {/* Synthesis Result Preview */}
        {result && (
          <div className="space-y-3 pt-2 border-t border-slate-800">
            <div className="flex items-center justify-between">
              <span className="text-xs font-semibold uppercase tracking-wider text-emerald-400 flex items-center space-x-1.5">
                <CheckCircle2 className="w-4 h-4" />
                <span>Generated Architecture</span>
              </span>
              <span className="text-[11px] font-mono px-2 py-0.5 bg-slate-800 text-slate-300 rounded border border-slate-700">
                {result.suggestedName} v{result.suggestedVersion}
              </span>
            </div>

            <div className="bg-slate-950 rounded-lg p-3 border border-slate-800 space-y-2 text-xs">
              <p className="font-medium text-slate-200">{result.summary}</p>
              <div className="text-slate-400 text-[11px] whitespace-pre-line leading-relaxed font-mono bg-slate-900/60 p-2.5 rounded border border-slate-800/60">
                {result.explanation}
              </div>

              {/* Validation Status */}
              <div className="flex items-center justify-between pt-2 border-t border-slate-800/80 text-[11px]">
                <span className="text-slate-400">FlowOS Validation Engine:</span>
                {result.validation.isValid ? (
                  <span className="text-emerald-400 font-medium flex items-center gap-1">
                    <CheckCircle2 className="w-3.5 h-3.5" /> 100% Valid (0 Errors)
                  </span>
                ) : (
                  <span className="text-amber-400 font-medium flex items-center gap-1">
                    <AlertCircle className="w-3.5 h-3.5" /> {result.validation.errors?.length} Warnings
                  </span>
                )}
              </div>
            </div>

            {/* Apply Button */}
            <button
              onClick={handleApply}
              className="w-full py-2.5 px-4 bg-emerald-600 hover:bg-emerald-500 text-white text-xs font-semibold rounded-lg shadow-sm flex items-center justify-center space-x-2 transition"
            >
              <ArrowRight className="w-4 h-4" />
              <span>Apply Blueprint to In-Memory Editor</span>
            </button>
          </div>
        )}
      </div>
    </div>
  );
};
