import React, { useEffect, useState } from 'react';
import {
  Bot,
  Brain,
  Database,
  KeyRound,
  Plus,
  RefreshCw,
  Wrench,
  MessageSquarePlus,
  Star,
  X,
  Edit2,
  Shield,
  Sparkles,
  Cpu
} from 'lucide-react';
import { api, PluginBindingDto } from '../api/client';
import { AgentPromptManager } from './AgentPromptManager';
import { WorkflowClass } from '../types';

interface Props {
  tenantName: string;
  onOpenBusinessContext?: () => void;
  workflowClass?: WorkflowClass;
  compact?: boolean;
}

type AiSubTab = 'compose' | 'agents' | 'prompts' | 'providers' | 'tools';

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
  const roles = new Set<string>();
  if (!Array.isArray(steps)) return { prompts, providers, tools, roles, stepCount: 0 };
  for (const step of steps) {
    const prompt = str(pick(step, 'agentPrompt', 'AgentPrompt'));
    const provider = str(pick(step, 'agentProvider', 'AgentProvider'));
    const stepTools = pick(step, 'agentTools', 'AgentTools');
    const stepRoles = pick(step, 'allowedRoles', 'AllowedRoles') || pick(step, 'requiredRoles', 'RequiredRoles');
    if (prompt) prompts.add(prompt);
    if (provider) providers.add(provider);
    if (Array.isArray(stepTools)) stepTools.forEach((t: unknown) => { if (str(t)) tools.add(str(t)); });
    if (Array.isArray(stepRoles)) stepRoles.forEach((r: unknown) => { if (str(r)) roles.add(str(r)); });
  }
  return { prompts, providers, tools, roles, stepCount: steps.length };
};

export const AiContextView: React.FC<Props> = ({ tenantName, onOpenBusinessContext, workflowClass, compact = false }) => {
  const [subTab, setSubTab] = useState<AiSubTab>('compose');
  const [agents, setAgents] = useState<PluginBindingDto[]>([]);
  const [prompts, setPrompts] = useState<PluginBindingDto[]>([]);
  const [providers, setProviders] = useState<PluginBindingDto[]>([]);
  const [tools, setTools] = useState<PluginBindingDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = async () => {
    setLoading(true);
    setError(null);
    try {
      const [promptList, providerList, toolList, agentList] = await Promise.all([
        api.listPluginBindings('prompt'),
        api.listPluginBindings('agent'),
        api.listPluginBindings('action'),
        api.listPluginBindings('profile')
      ]);
      setPrompts(promptList.bindings);
      setProviders(providerList.bindings);
      setTools(toolList.bindings);
      setAgents(agentList.bindings);
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
  const usedAgents = agents.filter(a =>
    declared.providers.has(a.sourceName) ||
    declared.roles.has(a.providerName) ||
    declared.roles.has(str(asRecord(a.configuration).role))
  );
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
                ? <>Composed for <strong className="text-white">{workflowClass.name}</strong> v{workflowClass.version}: Agents + Prompt + Data (Business Context) + Tools + Providers.</>
                : <>FlowOS composes AI Context for deciding steps: <strong className="text-purple-300">Agents</strong>, <strong className="text-sky-300">Prompt</strong>,
              <strong className="text-amber-300"> Data</strong>, <strong className="text-emerald-300"> Tools</strong>, and
              <strong className="text-violet-300"> Providers</strong>.</>}
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

        <div className="grid grid-cols-2 md:grid-cols-5 gap-2.5 mt-4">
          <ComposeCard
            icon={<Bot size={14} />}
            label="Agents"
            count={workflowClass ? `${usedAgents.length}/${agents.length}` : agents.length}
            hint="Composite personas"
            color="purple"
            onClick={() => setSubTab('agents')}
          />
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
            hint="Business Context facts"
            color="amber"
            onClick={onOpenBusinessContext}
          />
          <ComposeCard
            icon={<Wrench size={14} />}
            label="Tools"
            count={workflowClass ? `${usedTools.length}/${tools.length}` : tools.length}
            hint="Resource action plugins"
            color="emerald"
            onClick={() => setSubTab('tools')}
          />
          <ComposeCard
            icon={<KeyRound size={14} />}
            label="Providers"
            count={workflowClass ? `${usedProviders.length}/${providers.length}` : providers.length}
            hint="BYO model bindings"
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
          ['agents', 'Agents (Personas)'],
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
              agents [{[...declared.providers].filter(p => agents.some(a => a.sourceName === p)).join(', ') || '—'}] ·
              roles [{[...declared.roles].join(', ') || '—'}] ·
              prompt [{[...declared.prompts].join(', ') || '—'}] ·
              tools [{[...declared.tools].join(', ') || '—'}]
            </div>
          )}
          <p>
            Point a waiting step (<code className="text-sky-300">actor: Agent</code> or <code className="text-sky-300">Either</code>) at an Agent Persona alias or matching Role.
            Design-time inspect with MCP <code className="text-violet-300">preview_agent_context</code>; live inspect with
            <code className="text-violet-300"> get_agent_context</code> — neither runs the agent.
          </p>
          <div className="grid md:grid-cols-2 gap-3">
            <BindingList
              title="Agents (Composite Personas)"
              empty="No agent personas yet. Create a composite agent persona on the Agents tab."
              items={agents.map(a => {
                const cfg = asRecord(a.configuration);
                const role = str(cfg.role) || a.providerName;
                const prov = str(cfg.providerAlias) || 'default';
                const prompt = str(cfg.promptAlias) || 'none';
                const toolCount = Array.isArray(cfg.toolAliases) ? cfg.toolAliases.length : 0;
                const auto = cfg.autoCommitThreshold != null ? `${Math.round(Number(cfg.autoCommitThreshold) * 100)}% auto` : 'manual';
                return {
                  alias: a.sourceName,
                  detail: `Role: ${role} · Provider: ${prov} · Prompt: ${prompt} · Tools: ${toolCount} · ${auto}`
                };
              })}
            />
            <BindingList title="Prompts" empty="No prompts yet." items={prompts.map(p => ({
              alias: p.sourceName,
              detail: `${str(asRecord(p.configuration).title) || str(asRecord(p.configuration).Title) || 'markdown'}${p.flowOsVersion ? ` · FlowOS ${p.flowOsVersion}` : ''}`
            }))} />
            <BindingList title="Providers" empty="No LLM providers yet. Add a BYO key on the Providers tab." items={providers.map(p => {
              const cfg = asRecord(p.configuration);
              const hasKey = Boolean(cfg.hasApiKey ?? cfg.HasApiKey);
              const isDefault = Boolean(cfg.isDefault ?? cfg.IsDefault);
              return {
                alias: p.sourceName,
                detail: `${isDefault ? '★ default · ' : ''}${p.providerName}${str(cfg.model || cfg.Model) ? ` · ${str(cfg.model || cfg.Model)}` : ''}${hasKey ? ' · key set' : ''}${p.flowOsVersion ? ` · FlowOS ${p.flowOsVersion}` : ''}`
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

      {subTab === 'agents' && (
        <AgentProfileManager
          items={agents}
          providers={providers}
          prompts={prompts}
          tools={tools}
          onChanged={load}
        />
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
  color: 'purple' | 'sky' | 'amber' | 'emerald' | 'violet';
  onClick?: () => void;
}> = ({ icon, label, count, hint, color, onClick }) => {
  const tones = {
    purple: 'border-purple-500/30 hover:border-purple-400/60 text-purple-300',
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

const AgentProfileManager: React.FC<{
  items: PluginBindingDto[];
  providers: PluginBindingDto[];
  prompts: PluginBindingDto[];
  tools: PluginBindingDto[];
  onChanged: () => Promise<void>;
}> = ({ items, providers, prompts, tools, onChanged }) => {
  const [editingId, setEditingId] = useState<string | null>(null);
  const [form, setForm] = useState({
    sourceName: '',
    role: '',
    description: '',
    providerAlias: '',
    promptAlias: '',
    toolAliases: [] as string[],
    autoCommitThreshold: '',
    allowedEvents: '',
    isEnabled: true
  });
  const [validationError, setValidationError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const resetForm = () => {
    setEditingId(null);
    setValidationError(null);
    setForm({
      sourceName: '',
      role: '',
      description: '',
      providerAlias: '',
      promptAlias: '',
      toolAliases: [],
      autoCommitThreshold: '',
      allowedEvents: '',
      isEnabled: true
    });
  };

  const startEdit = (item: PluginBindingDto) => {
    const cfg = asRecord(item.configuration);
    setEditingId(item.id);
    setValidationError(null);
    setForm({
      sourceName: item.sourceName,
      role: str(cfg.role) || item.providerName || '',
      description: str(cfg.description) || '',
      providerAlias: str(cfg.providerAlias) || '',
      promptAlias: str(cfg.promptAlias) || '',
      toolAliases: Array.isArray(cfg.toolAliases) ? cfg.toolAliases.map(t => String(t)) : [],
      autoCommitThreshold: cfg.autoCommitThreshold != null ? String(cfg.autoCommitThreshold) : '',
      allowedEvents: Array.isArray(cfg.allowedEvents) ? cfg.allowedEvents.join(', ') : '',
      isEnabled: item.isEnabled
    });
  };

  const toggleTool = (toolName: string) => {
    setForm(prev => {
      const exists = prev.toolAliases.includes(toolName);
      return {
        ...prev,
        toolAliases: exists
          ? prev.toolAliases.filter(t => t !== toolName)
          : [...prev.toolAliases, toolName]
      };
    });
  };

  const save = async (e: React.FormEvent) => {
    e.preventDefault();
    setValidationError(null);

    const alias = form.sourceName.trim();
    if (!alias) {
      setValidationError('Agent alias (Name) is required.');
      return;
    }

    const role = form.role.trim();
    if (!role) {
      setValidationError('Role / Identity is required (e.g. LoanOfficer, Underwriter, TriageOfficer).');
      return;
    }

    let threshold: number | undefined = undefined;
    if (form.autoCommitThreshold.trim()) {
      const parsed = parseFloat(form.autoCommitThreshold.trim());
      if (isNaN(parsed) || parsed < 0 || parsed > 1) {
        setValidationError('Auto-commit threshold must be between 0.0 and 1.0 (e.g. 0.85).');
        return;
      }
      threshold = parsed;
    }

    const events = form.allowedEvents
      .split(',')
      .map(e => e.trim())
      .filter(Boolean);

    setSaving(true);
    try {
      await api.upsertPluginBinding({
        bindingType: 'profile',
        sourceName: alias,
        providerName: role,
        isEnabled: form.isEnabled,
        configuration: {
          role,
          description: form.description.trim() || undefined,
          providerAlias: form.providerAlias.trim() || undefined,
          promptAlias: form.promptAlias.trim() || undefined,
          toolAliases: form.toolAliases,
          autoCommitThreshold: threshold,
          allowedEvents: events.length > 0 ? events : undefined
        }
      });
      resetForm();
      await onChanged();
    } catch (err: any) {
      setValidationError(err.message || 'Failed to save agent persona.');
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="grid md:grid-cols-[1fr_380px] gap-4">
      <div className="space-y-3">
        {items.length === 0 && (
          <div className="text-sm text-slate-500 border border-dashed border-slate-700 rounded-xl p-8 text-center space-y-2">
            <Bot size={32} className="mx-auto text-purple-400/50 mb-2" />
            <p className="font-semibold text-slate-300">No Agent Personas defined yet.</p>
            <p className="text-xs text-slate-400 max-w-md mx-auto">
              Agents bundle a <strong className="text-purple-300">Role</strong>, a bound <strong className="text-violet-300">LLM Provider</strong>,
              a <strong className="text-sky-300">Prompt</strong>, and curated business <strong className="text-emerald-300">Tools</strong> into
              a reusable autonomous persona. Workflow steps matching the role or alias automatically hydrate this context.
            </p>
          </div>
        )}
        {items.map(item => {
          const cfg = asRecord(item.configuration);
          const role = str(cfg.role) || item.providerName;
          const description = str(cfg.description);
          const providerAlias = str(cfg.providerAlias) || 'tenant default';
          const promptAlias = str(cfg.promptAlias) || 'none';
          const toolAliases = Array.isArray(cfg.toolAliases) ? (cfg.toolAliases as string[]) : [];
          const threshold = cfg.autoCommitThreshold != null ? Number(cfg.autoCommitThreshold) : null;
          const allowedEvents = Array.isArray(cfg.allowedEvents) ? (cfg.allowedEvents as string[]) : [];
          const isEditing = editingId === item.id;

          return (
            <div
              key={item.id}
              onClick={() => startEdit(item)}
              className={`rounded-xl border p-4 cursor-pointer transition-all ${
                isEditing
                  ? 'border-purple-500 bg-purple-950/30 ring-1 ring-purple-500/50'
                  : 'border-slate-800 bg-slate-950/70 hover:border-slate-700 hover:bg-slate-900/60'
              }`}
            >
              <div className="flex items-start justify-between gap-3">
                <div className="flex items-start gap-3">
                  <div className="p-2 rounded-lg bg-purple-500/10 border border-purple-500/20 text-purple-300 mt-0.5">
                    <Bot size={18} />
                  </div>
                  <div>
                    <div className="flex items-center gap-2">
                      <span className="text-sm font-bold text-white">{item.sourceName}</span>
                      <span className="inline-flex items-center gap-1 text-[10px] font-semibold px-2 py-0.5 rounded-full bg-purple-500/10 text-purple-300 border border-purple-500/30">
                        <Shield size={10} /> {role}
                      </span>
                      {!item.isEnabled && (
                        <span className="text-[10px] text-slate-500 bg-slate-800 px-1.5 py-0.5 rounded">Disabled</span>
                      )}
                    </div>
                    {description && (
                      <p className="text-xs text-slate-400 mt-1 line-clamp-2">{description}</p>
                    )}
                  </div>
                </div>
                <button
                  type="button"
                  onClick={e => { e.stopPropagation(); startEdit(item); }}
                  className="text-slate-500 hover:text-slate-300 p-1"
                >
                  <Edit2 size={13} />
                </button>
              </div>

              <div className="grid grid-cols-2 gap-2 mt-3 pt-3 border-t border-slate-800/80 text-[11px]">
                <div className="flex items-center gap-1.5 text-slate-400 truncate">
                  <Cpu size={12} className="text-violet-400 shrink-0" />
                  <span className="text-slate-500">Provider:</span>
                  <span className="text-slate-300 font-medium truncate">{providerAlias}</span>
                </div>
                <div className="flex items-center gap-1.5 text-slate-400 truncate">
                  <MessageSquarePlus size={12} className="text-sky-400 shrink-0" />
                  <span className="text-slate-500">Prompt:</span>
                  <span className="text-slate-300 font-medium truncate">{promptAlias}</span>
                </div>
                <div className="flex items-center gap-1.5 text-slate-400 truncate">
                  <Wrench size={12} className="text-emerald-400 shrink-0" />
                  <span className="text-slate-500">Tools:</span>
                  <span className="text-slate-300 font-medium">{toolAliases.length} assigned</span>
                </div>
                <div className="flex items-center gap-1.5 text-slate-400 truncate">
                  <Sparkles size={12} className="text-amber-400 shrink-0" />
                  <span className="text-slate-500">Autonomy:</span>
                  <span className="text-slate-300 font-medium">
                    {threshold != null ? `${Math.round(threshold * 100)}% auto-commit` : 'Manual review'}
                  </span>
                </div>
              </div>

              {toolAliases.length > 0 && (
                <div className="flex flex-wrap gap-1 mt-2.5">
                  {toolAliases.map(t => (
                    <span key={t} className="text-[10px] font-mono px-1.5 py-0.5 rounded bg-slate-900 border border-slate-800 text-emerald-300">
                      {t}
                    </span>
                  ))}
                </div>
              )}

              {allowedEvents.length > 0 && (
                <div className="flex items-center gap-1.5 mt-2 text-[10px] text-slate-500">
                  <span>Auto-events:</span>
                  {allowedEvents.map(e => (
                    <span key={e} className="px-1.5 py-0.5 rounded bg-amber-500/10 text-amber-300 border border-amber-500/20 font-mono">
                      {e}
                    </span>
                  ))}
                </div>
              )}
            </div>
          );
        })}
      </div>

      <form onSubmit={save} className="rounded-xl border border-slate-700 bg-slate-900 p-4 space-y-3 h-fit">
        <div className="flex items-center justify-between pb-1 border-b border-slate-800">
          <div className="text-xs font-bold text-white flex items-center gap-2">
            <Bot size={14} className="text-purple-400" />
            {editingId ? 'Edit Agent Persona' : 'Define Agent Persona'}
          </div>
          {editingId && (
            <button
              type="button"
              onClick={resetForm}
              className="text-[11px] text-slate-400 hover:text-slate-200"
            >
              + New Agent
            </button>
          )}
        </div>

        {validationError && (
          <div className="p-2.5 rounded-lg bg-rose-950/50 border border-rose-700 text-rose-300 text-xs">
            {validationError}
          </div>
        )}

        <div className="space-y-1">
          <label className="text-[11px] font-semibold text-slate-300">Agent Alias (Unique Name)</label>
          <input
            required
            value={form.sourceName}
            onChange={e => setForm({ ...form, sourceName: e.target.value })}
            placeholder="e.g. CreditUnderwriter, TriageBot"
            className="w-full bg-slate-800 border border-slate-700 rounded-lg px-3 py-1.5 text-xs text-white placeholder-slate-500"
          />
          <p className="text-[10px] text-slate-500">Used as step.agentProvider or for direct attribution.</p>
        </div>

        <div className="space-y-1">
          <label className="text-[11px] font-semibold text-slate-300">Workflow Role / Identity</label>
          <input
            required
            value={form.role}
            onChange={e => setForm({ ...form, role: e.target.value })}
            placeholder="e.g. LoanOfficer, RiskAnalyst, SupportTier1"
            className="w-full bg-slate-800 border border-slate-700 rounded-lg px-3 py-1.5 text-xs text-white placeholder-slate-500"
          />
          <p className="text-[10px] text-slate-500">Workflow steps requiring this role automatically bind to this Agent.</p>
        </div>

        <div className="space-y-1">
          <label className="text-[11px] font-semibold text-slate-300">Description (Optional)</label>
          <textarea
            rows={2}
            value={form.description}
            onChange={e => setForm({ ...form, description: e.target.value })}
            placeholder="What does this agent persona do and decide?"
            className="w-full bg-slate-800 border border-slate-700 rounded-lg px-3 py-1.5 text-xs text-white placeholder-slate-500 resize-none"
          />
        </div>

        <div className="space-y-1">
          <label className="text-[11px] font-semibold text-slate-300">Bound LLM Provider</label>
          <select
            value={form.providerAlias}
            onChange={e => setForm({ ...form, providerAlias: e.target.value })}
            className="w-full bg-slate-800 border border-slate-700 rounded-lg px-3 py-1.5 text-xs text-white"
          >
            <option value="">(Tenant Default Provider)</option>
            <option value="flowos-hosted">flowos-hosted (FlowOS OpenAI)</option>
            <option value="flowos-risk">flowos-risk (Risk fixture)</option>
            {providers.map(p => (
              <option key={p.id} value={p.sourceName}>
                {p.sourceName} ({p.providerName})
              </option>
            ))}
          </select>
        </div>

        <div className="space-y-1">
          <label className="text-[11px] font-semibold text-slate-300">Bound Prompt</label>
          <select
            value={form.promptAlias}
            onChange={e => setForm({ ...form, promptAlias: e.target.value })}
            className="w-full bg-slate-800 border border-slate-700 rounded-lg px-3 py-1.5 text-xs text-white"
          >
            <option value="">(None / Step Template Default)</option>
            {prompts.map(p => {
              const title = str(asRecord(p.configuration).title) || str(asRecord(p.configuration).Title) || 'markdown';
              return (
                <option key={p.id} value={p.sourceName}>
                  {p.sourceName} ({title})
                </option>
              );
            })}
          </select>
        </div>

        <div className="space-y-1">
          <label className="text-[11px] font-semibold text-slate-300">Curated Business Tools</label>
          {tools.length === 0 ? (
            <p className="text-[10px] text-slate-500 italic bg-slate-950/40 p-2 rounded-lg border border-slate-800">
              No capability tools registered yet. Add resource actions in the Tools tab.
            </p>
          ) : (
            <div className="max-h-32 overflow-y-auto space-y-1 bg-slate-950/50 p-2 rounded-lg border border-slate-800 text-xs">
              {tools.map(t => {
                const checked = form.toolAliases.includes(t.sourceName);
                return (
                  <label key={t.id} className="flex items-center gap-2 cursor-pointer hover:bg-slate-900/60 p-1 rounded">
                    <input
                      type="checkbox"
                      checked={checked}
                      onChange={() => toggleTool(t.sourceName)}
                      className="rounded border-slate-700 text-purple-600 focus:ring-purple-500 bg-slate-800"
                    />
                    <span className="font-mono text-emerald-300 text-[11px] truncate">{t.sourceName}</span>
                    <span className="text-[10px] text-slate-500 truncate ml-auto">{t.providerName}</span>
                  </label>
                );
              })}
            </div>
          )}
        </div>

        <div className="pt-2 border-t border-slate-800 space-y-2">
          <div className="text-[11px] font-bold text-amber-300 flex items-center gap-1.5">
            <Sparkles size={12} /> Autonomy Policy
          </div>
          <div className="grid grid-cols-2 gap-2">
            <div className="space-y-0.5">
              <label className="text-[10px] text-slate-400 font-medium">Confidence Threshold</label>
              <input
                type="number"
                step="0.05"
                min="0"
                max="1"
                value={form.autoCommitThreshold}
                onChange={e => setForm({ ...form, autoCommitThreshold: e.target.value })}
                placeholder="e.g. 0.85 (or empty)"
                className="w-full bg-slate-800 border border-slate-700 rounded-lg px-2.5 py-1 text-xs text-white placeholder-slate-500"
              />
            </div>
            <div className="space-y-0.5">
              <label className="text-[10px] text-slate-400 font-medium">Allowed Events</label>
              <input
                value={form.allowedEvents}
                onChange={e => setForm({ ...form, allowedEvents: e.target.value })}
                placeholder="Approve, Reject"
                className="w-full bg-slate-800 border border-slate-700 rounded-lg px-2.5 py-1 text-xs text-white placeholder-slate-500"
              />
            </div>
          </div>
          <p className="text-[10px] text-slate-500 leading-tight">
            If model confidence exceeds threshold, FlowOS auto-publishes legal events without human parking.
          </p>
        </div>

        <div className="flex gap-2 pt-2">
          {editingId && (
            <button
              type="button"
              onClick={resetForm}
              className="flex-1 px-3 py-2 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-300 text-xs font-semibold"
            >
              Cancel
            </button>
          )}
          <button
            type="submit"
            disabled={saving}
            className="flex-1 px-3 py-2 rounded-lg bg-purple-600 hover:bg-purple-500 text-white text-xs font-semibold disabled:opacity-60 transition-colors"
          >
            {saving ? 'Saving…' : editingId ? 'Update Agent' : 'Save Agent'}
          </button>
        </div>
      </form>
    </div>
  );
};

const ProviderBindings: React.FC<{ items: PluginBindingDto[]; onChanged: () => Promise<void> }> = ({ items, onChanged }) => {
  const [editingId, setEditingId] = useState<string | null>(null);
  const [form, setForm] = useState({
    sourceName: 'flowos-hosted',
    providerName: 'flowos-hosted',
    model: '',
    endpoint: '',
    apiKey: '',
    isDefault: false,
    isEnabled: true
  });
  const [hasExistingKey, setHasExistingKey] = useState(false);
  const [validationError, setValidationError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const resetForm = () => {
    setEditingId(null);
    setHasExistingKey(false);
    setValidationError(null);
    setForm({
      sourceName: '',
      providerName: 'openai',
      model: '',
      endpoint: '',
      apiKey: '',
      isDefault: items.length === 0,
      isEnabled: true
    });
  };

  const startEdit = (item: PluginBindingDto) => {
    const cfg = asRecord(item.configuration);
    const keySet = Boolean(cfg.hasApiKey ?? cfg.HasApiKey);
    const isDef = Boolean(cfg.isDefault ?? cfg.IsDefault);
    setEditingId(item.id);
    setHasExistingKey(keySet);
    setValidationError(null);
    setForm({
      sourceName: item.sourceName,
      providerName: item.providerName,
      model: str(cfg.model ?? cfg.Model),
      endpoint: str(cfg.endpoint ?? cfg.Endpoint),
      apiKey: '',
      isDefault: isDef,
      isEnabled: item.isEnabled
    });
  };

  const save = async (e: React.FormEvent) => {
    e.preventDefault();
    setValidationError(null);

    const alias = form.sourceName.trim();
    if (!alias) {
      setValidationError('Provider alias is required.');
      return;
    }

    const provider = form.providerName;
    if (provider === 'azure-openai' || provider === 'custom') {
      if (!form.endpoint.trim()) {
        setValidationError(`Endpoint URL is required for ${provider === 'azure-openai' ? 'Azure OpenAI' : 'Custom'} provider.`);
        return;
      }
      if (!/^https?:\/\//i.test(form.endpoint.trim())) {
        setValidationError('Endpoint URL must start with http:// or https://');
        return;
      }
    }

    if (provider === 'custom' && !form.model.trim()) {
      setValidationError('Model name is required for Custom provider (e.g. llama3.2, deepseek-chat).');
      return;
    }

    if (['openai', 'anthropic', 'google', 'azure-openai'].includes(provider)) {
      if (!hasExistingKey && !form.apiKey.trim()) {
        setValidationError(`API Key is required when configuring ${provider}.`);
        return;
      }
    }

    setSaving(true);
    try {
      await api.upsertPluginBinding({
        bindingType: 'agent',
        sourceName: alias,
        providerName: provider,
        isEnabled: form.isEnabled,
        configuration: provider === 'flowos-hosted' || provider === 'flowos-risk'
          ? { isDefault: form.isDefault }
          : {
              model: form.model.trim() || undefined,
              endpoint: form.endpoint.trim() || undefined,
              apiKey: form.apiKey.trim() || undefined,
              isDefault: form.isDefault
            }
      });
      resetForm();
      await onChanged();
    } catch (err: any) {
      setValidationError(err.message || 'Failed to save provider.');
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="grid md:grid-cols-[1fr_360px] gap-4">
      <div className="space-y-2">
        {items.length === 0 && (
          <div className="text-sm text-slate-500 border border-dashed border-slate-700 rounded-xl p-8 text-center space-y-2">
            <p>No LLM providers registered yet for this tenant.</p>
            <p className="text-xs text-slate-400">
              Paid tenants can use <strong className="text-violet-300">flowos-hosted</strong> (FlowOS OpenAI, no key required). You can also bring your own keys for OpenAI, Claude, Azure OpenAI, Google Gemini, or local Ollama.
            </p>
          </div>
        )}
        {items.map(item => {
          const cfg = asRecord(item.configuration);
          const hasKey = Boolean(cfg.hasApiKey ?? cfg.HasApiKey);
          const isDefault = Boolean(cfg.isDefault ?? cfg.IsDefault);
          const isEditing = editingId === item.id;

          return (
            <div
              key={item.id}
              onClick={() => startEdit(item)}
              className={`rounded-xl border p-4 flex justify-between items-start gap-3 cursor-pointer transition-all ${
                isEditing
                  ? 'border-violet-500 bg-violet-950/30 ring-1 ring-violet-500/50'
                  : 'border-slate-800 bg-slate-950/70 hover:border-slate-700 hover:bg-slate-900/60'
              }`}
            >
              <div className="space-y-1 flex-1 min-w-0">
                <div className="flex items-center gap-2">
                  <span className="text-sm font-semibold text-white truncate">{item.sourceName}</span>
                  {isDefault && (
                    <span className="inline-flex items-center gap-1 text-[10px] font-bold px-2 py-0.5 rounded-full text-amber-300 bg-amber-500/10 border border-amber-500/30">
                      <Star size={10} className="fill-amber-300 text-amber-300" /> Default
                    </span>
                  )}
                </div>
                <div className="text-[11px] text-slate-400 truncate">
                  <span className="font-medium text-slate-300">{item.providerName}</span>
                  {str(cfg.model || cfg.Model) ? ` · ${str(cfg.model || cfg.Model)}` : ''}
                  {str(cfg.endpoint || cfg.Endpoint) ? ` · ${str(cfg.endpoint || cfg.Endpoint)}` : ''}
                </div>
              </div>
              <div className="flex items-center gap-2">
                <span className={`text-[10px] font-bold px-2 py-1 rounded-full border h-fit ${
                  item.providerName === 'flowos-hosted'
                    ? 'text-sky-300 border-sky-500/40 bg-sky-500/10'
                    : item.providerName === 'flowos-risk'
                    ? 'text-indigo-300 border-indigo-500/40 bg-indigo-500/10'
                    : hasKey
                    ? 'text-emerald-300 border-emerald-500/40 bg-emerald-500/10'
                    : 'text-slate-400 border-slate-700'
                }`}>
                  {item.providerName === 'flowos-hosted'
                    ? 'hosted quota'
                    : item.providerName === 'flowos-risk'
                    ? 'rule engine'
                    : hasKey
                    ? 'key set'
                    : 'no key'}
                </span>
                <button
                  type="button"
                  onClick={(e) => {
                    e.stopPropagation();
                    startEdit(item);
                  }}
                  className="p-1 text-slate-400 hover:text-white rounded hover:bg-slate-800"
                  title="Edit provider"
                >
                  <Edit2 size={12} />
                </button>
              </div>
            </div>
          );
        })}
      </div>

      <form onSubmit={save} className="rounded-xl border border-slate-700 bg-slate-900 p-4 space-y-3 h-fit">
        <div className="flex items-center justify-between">
          <div className="text-xs font-bold text-white flex items-center gap-2">
            <KeyRound size={14} className="text-violet-300" />
            {editingId ? `Edit provider: ${form.sourceName}` : 'Add LLM Provider'}
          </div>
          {editingId && (
            <button
              type="button"
              onClick={resetForm}
              className="text-[11px] text-slate-400 hover:text-slate-200 flex items-center gap-1"
            >
              <X size={12} /> New
            </button>
          )}
        </div>

        {validationError && (
          <div className="p-2.5 bg-rose-900/40 border border-rose-700/60 rounded-lg text-[11px] text-rose-300 leading-tight">
            {validationError}
          </div>
        )}

        <div>
          <label className="block text-[11px] font-medium text-slate-400 mb-1">
            Provider Alias (Required)
          </label>
          <input
            required
            value={form.sourceName}
            onChange={e => setForm({ ...form, sourceName: e.target.value })}
            placeholder="e.g. fast-triage, deep-reasoner, or default"
            className="w-full bg-slate-800 border border-slate-700 rounded-lg px-3 py-2 text-xs text-white placeholder-slate-500 focus:outline-none focus:border-violet-500"
          />
          <p className="text-[10px] text-slate-500 mt-0.5">Matched by workflow steps (step.agentProvider)</p>
        </div>

        <div>
          <label className="block text-[11px] font-medium text-slate-400 mb-1">
            Provider Engine (Required)
          </label>
          <select
            value={form.providerName}
            onChange={e => setForm({ ...form, providerName: e.target.value })}
            className="w-full bg-slate-800 border border-slate-700 rounded-lg px-3 py-2 text-xs text-white focus:outline-none focus:border-violet-500"
          >
            <option value="flowos-hosted">FlowOS Hosted OpenAI (Platform Managed)</option>
            <option value="openai">OpenAI (BYO Key)</option>
            <option value="anthropic">Anthropic Claude (BYO Key)</option>
            <option value="azure-openai">Azure OpenAI (Custom Endpoint)</option>
            <option value="google">Google Gemini (BYO Key)</option>
            <option value="custom">Custom OpenAI-Compatible (Ollama, DeepSeek, Groq)</option>
            <option value="flowos-risk">FlowOS Risk (Deterministic Rule Engine)</option>
          </select>
        </div>

        {form.providerName === 'flowos-hosted' && (
          <div className="p-2.5 rounded-lg bg-violet-950/40 border border-violet-800/40 text-[11px] text-violet-200/90 leading-relaxed">
            FlowOS executes OpenAI (gpt-4o-mini) using the platform credentials with built-in daily quota. Zero tenant keys required.
          </div>
        )}

        {form.providerName === 'flowos-risk' && (
          <div className="p-2.5 rounded-lg bg-indigo-950/40 border border-indigo-800/40 text-[11px] text-indigo-200/90 leading-relaxed">
            Built-in deterministic heuristic rule engine running in-process. Validates next steps instantly with zero latency and no external LLM credentials.
          </div>
        )}

        {form.providerName !== 'flowos-hosted' && form.providerName !== 'flowos-risk' && (
          <>
            <div>
              <label className="block text-[11px] font-medium text-slate-400 mb-1">
                Model {form.providerName === 'custom' ? <span className="text-amber-400">(Required)</span> : <span className="text-slate-500">(Optional)</span>}
              </label>
              <input
                value={form.model}
                onChange={e => setForm({ ...form, model: e.target.value })}
                placeholder={
                  form.providerName === 'openai'
                    ? 'gpt-4o-mini (default), gpt-4o, o3-mini'
                    : form.providerName === 'anthropic'
                    ? 'claude-3-5-sonnet-20241022 (default), claude-3-5-haiku'
                    : form.providerName === 'google'
                    ? 'gemini-1.5-flash (default), gemini-1.5-pro'
                    : form.providerName === 'azure-openai'
                    ? 'Deployment name (optional)'
                    : 'e.g. llama3.2, deepseek-chat, mixtral'
                }
                className="w-full bg-slate-800 border border-slate-700 rounded-lg px-3 py-2 text-xs text-white placeholder-slate-500 focus:outline-none focus:border-violet-500"
              />
            </div>

            <div>
              <label className="block text-[11px] font-medium text-slate-400 mb-1">
                Endpoint URL {form.providerName === 'azure-openai' || form.providerName === 'custom' ? <span className="text-amber-400">(Required)</span> : <span className="text-slate-500">(Optional)</span>}
              </label>
              <input
                value={form.endpoint}
                onChange={e => setForm({ ...form, endpoint: e.target.value })}
                placeholder={
                  form.providerName === 'azure-openai'
                    ? 'https://<resource>.openai.azure.com/openai/deployments/...'
                    : form.providerName === 'custom'
                    ? 'http://localhost:11434/v1/chat/completions'
                    : 'Leave blank for official default endpoint'
                }
                className="w-full bg-slate-800 border border-slate-700 rounded-lg px-3 py-2 text-xs text-white placeholder-slate-500 focus:outline-none focus:border-violet-500"
              />
            </div>

            <div>
              <label className="block text-[11px] font-medium text-slate-400 mb-1">
                API Key {form.providerName === 'custom' ? <span className="text-slate-500">(Optional for local Ollama)</span> : hasExistingKey ? <span className="text-emerald-400">(Stored — leave blank to keep)</span> : <span className="text-amber-400">(Required)</span>}
              </label>
              <input
                type="password"
                value={form.apiKey}
                onChange={e => setForm({ ...form, apiKey: e.target.value })}
                placeholder={hasExistingKey ? 'Leave blank to retain stored key' : 'API Key (stored write-only, never shown)'}
                className="w-full bg-slate-800 border border-slate-700 rounded-lg px-3 py-2 text-xs text-white placeholder-slate-500 focus:outline-none focus:border-violet-500"
              />
            </div>
          </>
        )}

        <div className="pt-1 border-t border-slate-800">
          <label className="flex items-start gap-2 cursor-pointer select-none">
            <input
              type="checkbox"
              checked={form.isDefault}
              onChange={e => setForm({ ...form, isDefault: e.target.checked })}
              className="mt-0.5 rounded border-slate-700 text-violet-600 focus:ring-violet-500 bg-slate-800"
            />
            <div className="text-[11px]">
              <span className="font-semibold text-slate-200">Set as Tenant Default Provider</span>
              <p className="text-[10px] text-slate-500">
                Steps without an explicit agentProvider will automatically resolve to this provider.
              </p>
            </div>
          </label>
        </div>

        <div className="flex gap-2 pt-2">
          {editingId && (
            <button
              type="button"
              onClick={resetForm}
              className="flex-1 px-3 py-2 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-300 text-xs font-semibold"
            >
              Cancel
            </button>
          )}
          <button
            type="submit"
            disabled={saving}
            className="flex-1 px-3 py-2 rounded-lg bg-violet-600 hover:bg-violet-500 text-white text-xs font-semibold disabled:opacity-60 transition-colors"
          >
            {saving ? 'Saving…' : editingId ? 'Update Provider' : 'Save Provider'}
          </button>
        </div>
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
