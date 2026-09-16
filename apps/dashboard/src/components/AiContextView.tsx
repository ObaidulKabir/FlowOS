import React, { useEffect, useState } from 'react';
import { Bot, Brain, Database, KeyRound, Plus, RefreshCw, Wrench, MessageSquarePlus } from 'lucide-react';
import { api, PluginBindingDto } from '../api/client';
import { AgentPromptManager } from './AgentPromptManager';
import { WorkflowClass } from '../types';

interface Props {
  tenantName: string;
  onOpenBusinessContext?: () => void;
  workflowClass?: WorkflowClass;
  compact?: boolean;
}

type AiSubTab = 'compose' | 'prompts' | 'providers' | 'tools';

const PROVIDERS = ['openai', 'anthropic', 'azure-openai', 'google', 'custom', 'flowos-risk'] as const;
const TOOL_PLUGINS = ['LookupRecord', 'QueryRecords', 'FetchDocument', 'SearchKnowledge', 'CheckPolicy'] as const;

const asRecord = (value: unknown): Record<string, unknown> =>
  value && typeof value === 'object' ? (value as Record<string, unknown>) : {};

const str = (value: unknown) => (typeof value === 'string' ? value : '');

const pick = (obj: any, ...keys: string[]) => {
  if (!obj || typeof obj !== 'object') return undefined;
  for (const key of keys) {
    if (obj[key] !== undefined) return obj[key];
    const match = Object.keys(obj).find(k => k.toLowerCase() === key.toLowerCase());
    if (match) return obj[match];
  }
  return undefined;
};

const declaredAgentAliases = (workflowClass?: WorkflowClass) => {
  const steps = pick(pick(workflowClass?.definition, 'Workflow', 'workflow'), 'Steps', 'steps') || [];
  const prompts = new Set<string>();
  const providers = new Set<string>();
  const tools = new Set<string>();
  if (!Array.isArray(steps)) return { prompts, providers, tools, stepCount: 0 };
  for (const step of steps) {
    const prompt = str(pick(step, 'agentPrompt', 'AgentPrompt'));
    const provider = str(pick(step, 'agentProvider', 'AgentProvider'));
    const stepTools = pick(step, 'agentTools', 'AgentTools');
    if (prompt) prompts.add(prompt);
    if (provider) providers.add(provider);
    if (Array.isArray(stepTools)) stepTools.forEach((t: unknown) => { if (str(t)) tools.add(str(t)); });
  }
  return { prompts, providers, tools, stepCount: steps.length };
};

export const AiContextView: React.FC<Props> = ({ tenantName, onOpenBusinessContext, workflowClass, compact = false }) => {
  const [subTab, setSubTab] = useState<AiSubTab>('compose');
  const [prompts, setPrompts] = useState<PluginBindingDto[]>([]);
  const [providers, setProviders] = useState<PluginBindingDto[]>([]);
  const [tools, setTools] = useState<PluginBindingDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = async () => {
    setLoading(true);
    setError(null);
    try {
      const [promptList, providerList, toolList] = await Promise.all([
        api.listPluginBindings('prompt'),
        api.listPluginBindings('agent'),
        api.listPluginBindings('action')
      ]);
      setPrompts(promptList.bindings);
      setProviders(providerList.bindings);
      setTools(toolList.bindings);
    } catch (err: any) {
      setError(err.message || 'Failed to load AI Context');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    load();
  }, []);

  const declared = declaredAgentAliases(workflowClass);
  const usedPrompts = prompts.filter(p => declared.prompts.has(p.sourceName));
  const usedProviders = providers.filter(p => declared.providers.has(p.sourceName));
  const usedTools = tools.filter(t =>
    declared.tools.has(t.sourceName) ||
    [...declared.tools].some(alias => alias.toLowerCase().includes(t.sourceName.toLowerCase()))
  );

  return (
    <div className="space-y-5">
      <div className="rounded-2xl border border-violet-500/30 bg-gradient-to-r from-violet-950/40 via-slate-900 to-sky-950/30 p-5">
        <div className="flex items-start justify-between gap-4">
          <div>
            <div className="flex items-center gap-2 mb-1">
              <Brain className="text-violet-300" size={18} />
              <h3 className="text-lg font-bold text-white">AI Context</h3>
            </div>
            <p className="text-xs text-slate-400 max-w-3xl">
              {workflowClass
                ? <>Composed for <strong className="text-white">{workflowClass.name}</strong> v{workflowClass.version}: Prompt + Data (Business Context) + Tools + Provider.</>
                : <>FlowOS composes one Agent Context for deciding steps: <strong className="text-sky-300">Prompt</strong>,
              <strong className="text-amber-300"> Data</strong>, <strong className="text-emerald-300"> Tools</strong>, and
              <strong className="text-violet-300"> Provider</strong>.</>}
              {' '}The model never sees API keys or tenant URLs.
              {compact ? null : <> Owned by <strong>{tenantName}</strong>.</>}
            </p>
          </div>
          <button
            onClick={load}
            className="p-2 bg-slate-800 hover:bg-slate-750 text-slate-300 border border-slate-700 rounded-xl"
            title="Refresh AI Context"
          >
            <RefreshCw size={14} className={loading ? 'animate-spin text-violet-300' : ''} />
          </button>
        </div>

        <div className="grid grid-cols-2 md:grid-cols-4 gap-2.5 mt-4">
          <ComposeCard
            icon={<MessageSquarePlus size={14} />}
            label="Prompt"
            count={workflowClass ? `${usedPrompts.length}/${prompts.length}` : prompts.length}
            hint="Named tenant prompts"
            color="sky"
            onClick={() => setSubTab('prompts')}
          />
          <ComposeCard
            icon={<Database size={14} />}
            label="Data"
            count="live"
            hint="Business Context + case facts"
            color="amber"
            onClick={onOpenBusinessContext}
          />
          <ComposeCard
            icon={<Wrench size={14} />}
            label="Tools"
            count={workflowClass ? `${usedTools.length}/${tools.length}` : tools.length}
            hint="Tenant resource plugins"
            color="emerald"
            onClick={() => setSubTab('tools')}
          />
          <ComposeCard
            icon={<Bot size={14} />}
            label="Provider"
            count={workflowClass ? `${usedProviders.length}/${providers.length}` : providers.length}
            hint="BYO model binding"
            color="violet"
            onClick={() => setSubTab('providers')}
          />
        </div>
      </div>

      {error && (
        <div className="p-3 bg-rose-900/30 border border-rose-700 rounded-xl text-xs text-rose-300">{error}</div>
      )}

      <div className="flex bg-slate-900 rounded-xl p-1 border border-slate-700 text-xs">
        {([
          ['compose', 'Compose'],
          ['prompts', 'Prompts'],
          ['providers', 'Providers'],
          ['tools', 'Tools']
        ] as const).map(([id, label]) => (
          <button
            key={id}
            onClick={() => setSubTab(id)}
            className={`flex-1 px-3 py-1.5 rounded-lg font-semibold transition-all ${
              subTab === id ? 'bg-violet-600 text-white shadow-sm' : 'text-slate-400 hover:text-slate-200'
            }`}
          >
            {label}
          </button>
        ))}
      </div>

      {subTab === 'compose' && (
        <div className="space-y-3 text-xs text-slate-400">
          {workflowClass && (
            <div className="rounded-lg border border-violet-500/25 bg-violet-950/20 px-3 py-2 text-[11px] text-violet-100">
              Declared on this workflow’s steps:
              prompt [{[...declared.prompts].join(', ') || '—'}] ·
              provider [{[...declared.providers].join(', ') || '—'}] ·
              tools [{[...declared.tools].join(', ') || '—'}]
            </div>
          )}
          <p>
            Point a waiting step (<code className="text-sky-300">actor: Agent</code> or <code className="text-sky-300">Either</code>) at these aliases.
            Design-time inspect with MCP <code className="text-violet-300">preview_agent_context</code>; live inspect with
            <code className="text-violet-300"> get_agent_context</code> — neither runs the agent.
          </p>
          <div className="grid md:grid-cols-2 gap-3">
            <BindingList title="Prompts" empty="No prompts yet." items={prompts.map(p => ({
              alias: p.sourceName,
              detail: str(asRecord(p.configuration).title) || str(asRecord(p.configuration).Title) || 'markdown'
            }))} />
            <BindingList title="Providers" empty="No LLM providers yet. Add a BYO key on the Providers tab." items={providers.map(p => {
              const cfg = asRecord(p.configuration);
              const hasKey = Boolean(cfg.hasApiKey ?? cfg.HasApiKey);
              return {
                alias: p.sourceName,
                detail: `${p.providerName}${str(cfg.model || cfg.Model) ? ` · ${str(cfg.model || cfg.Model)}` : ''}${hasKey ? ' · key set' : ''}`
              };
            })} />
            <BindingList title="Tools" empty="No resource tools yet. Bind LookupRecord / QueryRecords capabilities." items={tools.map(t => ({
              alias: t.sourceName,
              detail: t.providerName
            }))} />
            <div className="rounded-xl border border-slate-800 bg-slate-950/60 p-4">
              <div className="text-[11px] uppercase tracking-wide text-amber-400 font-bold mb-2">Data</div>
              <p className="text-slate-400 leading-relaxed">
                Canonical case facts come from <strong className="text-amber-200">Business Context</strong> bindings
                (entity type, event aliases, payload mapping). SLA reminders and tool prefetch results are added at run time.
              </p>
              {onOpenBusinessContext && (
                <button
                  onClick={onOpenBusinessContext}
                  className="mt-3 px-3 py-1.5 rounded-lg bg-amber-600/80 hover:bg-amber-500 text-white font-semibold"
                >
                  Open Business Context
                </button>
              )}
            </div>
          </div>
        </div>
      )}

      {subTab === 'prompts' && (
        <AgentPromptManager tenantName={tenantName} compact />
      )}

      {subTab === 'providers' && (
        <ProviderBindings items={providers} onChanged={load} />
      )}

      {subTab === 'tools' && (
        <ToolBindings items={tools} onChanged={load} />
      )}
    </div>
  );
};

const ComposeCard: React.FC<{
  icon: React.ReactNode;
  label: string;
  count: number | string;
  hint: string;
  color: 'sky' | 'amber' | 'emerald' | 'violet';
  onClick?: () => void;
}> = ({ icon, label, count, hint, color, onClick }) => {
  const tones = {
    sky: 'border-sky-500/30 hover:border-sky-400/60 text-sky-300',
    amber: 'border-amber-500/30 hover:border-amber-400/60 text-amber-300',
    emerald: 'border-emerald-500/30 hover:border-emerald-400/60 text-emerald-300',
    violet: 'border-violet-500/30 hover:border-violet-400/60 text-violet-300'
  };
  return (
    <button
      type="button"
      onClick={onClick}
      className={`text-left rounded-xl border bg-slate-950/50 p-3 transition-all ${tones[color]}`}
    >
      <div className="flex items-center justify-between">
        <span className="flex items-center gap-1.5 text-[11px] font-bold uppercase tracking-wide">{icon}{label}</span>
        <span className="text-sm font-extrabold text-white">{count}</span>
      </div>
      <div className="text-[10px] text-slate-500 mt-1">{hint}</div>
    </button>
  );
};

const BindingList: React.FC<{ title: string; empty: string; items: { alias: string; detail: string }[] }> = ({
  title,
  empty,
  items
}) => (
  <div className="rounded-xl border border-slate-800 bg-slate-950/60 p-4">
    <div className="text-[11px] uppercase tracking-wide text-slate-500 font-bold mb-2">{title}</div>
    {items.length === 0 ? (
      <p className="text-slate-500">{empty}</p>
    ) : (
      <ul className="space-y-1.5">
        {items.map(item => (
          <li key={item.alias} className="flex justify-between gap-2">
            <code className="text-sky-300">{item.alias}</code>
            <span className="text-slate-500 truncate">{item.detail}</span>
          </li>
        ))}
      </ul>
    )}
  </div>
);

const ProviderBindings: React.FC<{ items: PluginBindingDto[]; onChanged: () => Promise<void> }> = ({ items, onChanged }) => {
  const [form, setForm] = useState({
    sourceName: '',
    providerName: 'openai',
    model: '',
    endpoint: '',
    apiKey: '',
    isEnabled: true
  });
  const [saving, setSaving] = useState(false);

  const save = async (e: React.FormEvent) => {
    e.preventDefault();
    setSaving(true);
    try {
      await api.upsertPluginBinding({
        bindingType: 'agent',
        sourceName: form.sourceName,
        providerName: form.providerName,
        isEnabled: form.isEnabled,
        configuration: {
          model: form.model || undefined,
          endpoint: form.endpoint || undefined,
          apiKey: form.apiKey || undefined
        }
      });
      setForm({ ...form, apiKey: '', sourceName: form.sourceName });
      await onChanged();
    } catch (err: any) {
      alert(`Failed to save provider: ${err.message}`);
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="grid md:grid-cols-[1fr_320px] gap-4">
      <div className="space-y-2">
        {items.length === 0 && (
          <div className="text-sm text-slate-500 border border-dashed border-slate-700 rounded-xl p-8 text-center">
            No providers yet. Bind openai / anthropic / custom with a tenant key. The key never appears in Agent Context.
          </div>
        )}
        {items.map(item => {
          const cfg = asRecord(item.configuration);
          return (
            <div key={item.id} className="rounded-xl border border-slate-800 bg-slate-950/70 p-4 flex justify-between gap-3">
              <div>
                <div className="text-sm font-semibold text-white">{item.sourceName}</div>
                <div className="text-[11px] text-slate-500 mt-0.5">
                  {item.providerName}
                  {str(cfg.model || cfg.Model) ? ` · ${str(cfg.model || cfg.Model)}` : ''}
                  {str(cfg.endpoint || cfg.Endpoint) ? ` · ${str(cfg.endpoint || cfg.Endpoint)}` : ''}
                </div>
              </div>
              <span className={`text-[10px] font-bold px-2 py-1 rounded-full border h-fit ${
                cfg.hasApiKey || cfg.HasApiKey
                  ? 'text-emerald-300 border-emerald-500/40 bg-emerald-500/10'
                  : 'text-slate-400 border-slate-700'
              }`}>
                {cfg.hasApiKey || cfg.HasApiKey ? 'key set' : 'no key'}
              </span>
            </div>
          );
        })}
      </div>
      <form onSubmit={save} className="rounded-xl border border-slate-700 bg-slate-900 p-4 space-y-3 h-fit">
        <div className="text-xs font-bold text-white flex items-center gap-2">
          <KeyRound size={14} className="text-violet-300" /> Add / update provider
        </div>
        <input
          required
          value={form.sourceName}
          onChange={e => setForm({ ...form, sourceName: e.target.value })}
          placeholder="alias (step.agentProvider)"
          className="w-full bg-slate-800 border border-slate-700 rounded-lg px-3 py-2 text-xs text-white"
        />
        <select
          value={form.providerName}
          onChange={e => setForm({ ...form, providerName: e.target.value })}
          className="w-full bg-slate-800 border border-slate-700 rounded-lg px-3 py-2 text-xs text-white"
        >
          {PROVIDERS.map(p => <option key={p} value={p}>{p}</option>)}
        </select>
        <input
          value={form.model}
          onChange={e => setForm({ ...form, model: e.target.value })}
          placeholder="model (gpt-4o-mini)"
          className="w-full bg-slate-800 border border-slate-700 rounded-lg px-3 py-2 text-xs text-white"
        />
        <input
          value={form.endpoint}
          onChange={e => setForm({ ...form, endpoint: e.target.value })}
          placeholder="endpoint (optional, custom/azure)"
          className="w-full bg-slate-800 border border-slate-700 rounded-lg px-3 py-2 text-xs text-white"
        />
        <input
          type="password"
          value={form.apiKey}
          onChange={e => setForm({ ...form, apiKey: e.target.value })}
          placeholder="API key (stored, never shown)"
          className="w-full bg-slate-800 border border-slate-700 rounded-lg px-3 py-2 text-xs text-white"
        />
        <button
          type="submit"
          disabled={saving}
          className="w-full px-3 py-2 rounded-lg bg-violet-600 hover:bg-violet-500 text-white text-xs font-semibold disabled:opacity-60"
        >
          {saving ? 'Saving…' : 'Save provider'}
        </button>
      </form>
    </div>
  );
};

const ToolBindings: React.FC<{ items: PluginBindingDto[]; onChanged: () => Promise<void> }> = ({ items, onChanged }) => {
  const [form, setForm] = useState({ sourceName: '', providerName: 'LookupRecord', isEnabled: true });
  const [saving, setSaving] = useState(false);

  const save = async (e: React.FormEvent) => {
    e.preventDefault();
    setSaving(true);
    try {
      await api.upsertPluginBinding({
        bindingType: 'action',
        sourceName: form.sourceName,
        providerName: form.providerName,
        isEnabled: form.isEnabled
      });
      await onChanged();
    } catch (err: any) {
      alert(`Failed to save tool: ${err.message}`);
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="grid md:grid-cols-[1fr_320px] gap-4">
      <div className="space-y-2">
        {items.length === 0 && (
          <div className="text-sm text-slate-500 border border-dashed border-slate-700 rounded-xl p-8 text-center">
            No tools yet. Bind a capability alias to LookupRecord / QueryRecords / FetchDocument / SearchKnowledge / CheckPolicy.
          </div>
        )}
        {items.map(item => (
          <div key={item.id} className="rounded-xl border border-slate-800 bg-slate-950/70 p-4">
            <div className="text-sm font-semibold text-white">{item.sourceName}</div>
            <div className="text-[11px] text-slate-500 mt-0.5">plugin {item.providerName} · URLs stay on the host</div>
          </div>
        ))}
      </div>
      <form onSubmit={save} className="rounded-xl border border-slate-700 bg-slate-900 p-4 space-y-3 h-fit">
        <div className="text-xs font-bold text-white flex items-center gap-2">
          <Plus size={14} className="text-emerald-300" /> Bind resource tool
        </div>
        <input
          required
          value={form.sourceName}
          onChange={e => setForm({ ...form, sourceName: e.target.value })}
          placeholder="capability alias (crm.customer.get.v1)"
          className="w-full bg-slate-800 border border-slate-700 rounded-lg px-3 py-2 text-xs text-white"
        />
        <select
          value={form.providerName}
          onChange={e => setForm({ ...form, providerName: e.target.value })}
          className="w-full bg-slate-800 border border-slate-700 rounded-lg px-3 py-2 text-xs text-white"
        >
          {TOOL_PLUGINS.map(p => <option key={p} value={p}>{p}</option>)}
        </select>
        <button
          type="submit"
          disabled={saving}
          className="w-full px-3 py-2 rounded-lg bg-emerald-600 hover:bg-emerald-500 text-white text-xs font-semibold disabled:opacity-60"
        >
          {saving ? 'Saving…' : 'Save tool'}
        </button>
      </form>
    </div>
  );
};
