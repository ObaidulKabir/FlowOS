import React, { useEffect, useState } from 'react';
import { api, PluginBindingDto } from '../api/client';
import { MessageSquarePlus, Plus, Pencil, RefreshCw, Check, X } from 'lucide-react';

interface Props {
  tenantName: string;
  compact?: boolean;
}

interface PromptForm {
  sourceName: string;
  title: string;
  system: string;
  instructions: string;
}

const emptyForm = (): PromptForm => ({
  sourceName: '',
  title: '',
  system: '',
  instructions: ''
});

const promptBody = (binding: PluginBindingDto) => {
  const config = (binding.configuration ?? (binding as { Configuration?: Record<string, unknown> }).Configuration) as
    | { title?: string; Title?: string; system?: string; System?: string; instructions?: string; Instructions?: string }
    | undefined;
  return {
    title: config?.title ?? config?.Title ?? '',
    system: config?.system ?? config?.System ?? '',
    instructions: config?.instructions ?? config?.Instructions ?? ''
  };
};

export const AgentPromptManager: React.FC<Props> = ({ tenantName, compact = false }) => {
  const [prompts, setPrompts] = useState<PluginBindingDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState<PromptForm | null>(null);
  const [isCreate, setIsCreate] = useState(true);
  const [saving, setSaving] = useState(false);
  const [savedAlias, setSavedAlias] = useState<string | null>(null);

  const loadPrompts = async () => {
    setLoading(true);
    setError(null);
    try {
      const result = await api.listPluginBindings('prompt');
      setPrompts(result.bindings);
    } catch (err: any) {
      setError(err.message || 'Failed to load prompts');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadPrompts();
  }, []);

  const openCreate = () => {
    setIsCreate(true);
    setEditing(emptyForm());
  };

  const openEdit = (binding: PluginBindingDto) => {
    const body = promptBody(binding);
    setIsCreate(false);
    setEditing({
      sourceName: binding.sourceName,
      title: body.title,
      system: body.system,
      instructions: body.instructions
    });
  };

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!editing) return;
    setSaving(true);
    try {
      await api.upsertPluginBinding({
        bindingType: 'prompt',
        sourceName: editing.sourceName,
        providerName: 'markdown',
        isEnabled: true,
        configuration: {
          title: editing.title,
          system: editing.system,
          instructions: editing.instructions
        }
      });
      setSavedAlias(editing.sourceName);
      setEditing(null);
      await loadPrompts();
      setTimeout(() => setSavedAlias(null), 2500);
    } catch (err: any) {
      alert(`Failed to save prompt: ${err.message}`);
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="space-y-6">
      {!compact && (
      <div className="bg-slate-850 p-6 rounded-2xl border border-slate-700/80 flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <div className="flex items-center gap-2 mb-1">
            <MessageSquarePlus className="text-sky-400" size={20} />
            <h3 className="text-lg font-bold text-white">Agent prompts</h3>
          </div>
          <p className="text-xs text-slate-400 max-w-2xl">
            Create and edit named prompts for <strong>{tenantName}</strong>. Point a waiting step at one with
            <code className="mx-1 text-sky-300">agentPrompt</code>. FlowOS loads the text into Agent Context — not into the workflow template.
          </p>
        </div>
        <div className="flex items-center gap-2.5">
          <button
            onClick={loadPrompts}
            className="p-2 bg-slate-800 hover:bg-slate-750 text-slate-300 border border-slate-700 rounded-xl transition-colors"
            title="Refresh prompts"
          >
            <RefreshCw size={14} className={loading ? 'animate-spin text-blue-400' : ''} />
          </button>
          <button
            onClick={openCreate}
            className="px-4 py-2.5 bg-sky-600 hover:bg-sky-500 text-white font-semibold rounded-xl text-xs flex items-center gap-2"
          >
            <Plus size={14} />
            New prompt
          </button>
        </div>
      </div>
      )}
      {compact && (
        <div className="flex justify-end">
          <button
            onClick={openCreate}
            className="px-4 py-2 bg-sky-600 hover:bg-sky-500 text-white font-semibold rounded-xl text-xs flex items-center gap-2"
          >
            <Plus size={14} />
            New prompt
          </button>
        </div>
      )}

      {error && (
        <div className="p-4 bg-rose-900/30 border border-rose-700 rounded-xl text-xs text-rose-300">{error}</div>
      )}

      {savedAlias && (
        <div className="p-3 bg-emerald-900/30 border border-emerald-700 rounded-xl text-xs text-emerald-300 flex items-center gap-2">
          <Check size={14} /> Saved <code className="text-emerald-200">{savedAlias}</code>. Set step <code>agentPrompt</code> to this alias.
        </div>
      )}

      <div className="space-y-3">
        {prompts.length === 0 && !loading && (
          <div className="text-sm text-slate-500 border border-dashed border-slate-700 rounded-xl p-8 text-center">
            No prompts yet. Create one, then set <code className="text-sky-300">agentPrompt</code> on an Agent/Either step.
          </div>
        )}
        {prompts.map(prompt => {
          const body = promptBody(prompt);
          return (
            <div key={prompt.id} className="bg-slate-900/70 border border-slate-800 rounded-xl p-4 flex flex-col gap-2">
              <div className="flex items-start justify-between gap-3">
                <div>
                  <div className="text-sm font-semibold text-white">{body.title || prompt.sourceName}</div>
                  <div className="text-[11px] text-slate-500 mt-0.5">
                  alias <code className="text-sky-300">{prompt.sourceName || (prompt as { SourceName?: string }).SourceName}</code>
                  </div>
                </div>
                <button
                  onClick={() => openEdit(prompt)}
                  className="px-3 py-1.5 bg-slate-800 hover:bg-slate-700 border border-slate-700 text-slate-200 rounded-lg text-xs font-semibold flex items-center gap-1.5"
                >
                  <Pencil size={13} /> Edit
                </button>
              </div>
              {body.system && (
                <p className="text-[11px] text-slate-400 line-clamp-2">{body.system}</p>
              )}
              <p className="text-xs text-slate-300 whitespace-pre-wrap line-clamp-4">{body.instructions}</p>
            </div>
          );
        })}
      </div>

      {editing && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <form
            onSubmit={handleSave}
            className="bg-slate-900 border border-slate-700 rounded-2xl max-w-2xl w-full p-6 shadow-2xl space-y-4"
          >
            <div className="flex items-center justify-between border-b border-slate-800 pb-3">
              <h3 className="text-base font-bold text-white">{isCreate ? 'Create prompt' : 'Edit prompt'}</h3>
              <button type="button" onClick={() => setEditing(null)} className="text-slate-400 hover:text-white">
                <X size={16} />
              </button>
            </div>
            <label className="block space-y-1">
              <span className="text-[11px] uppercase tracking-wide text-slate-500">Alias (step.agentPrompt)</span>
              <input
                required
                disabled={!isCreate}
                value={editing.sourceName}
                onChange={e => setEditing({ ...editing, sourceName: e.target.value })}
                placeholder="quote-approval"
                className="w-full bg-slate-800 border border-slate-700 rounded-lg px-3 py-2 text-sm text-white disabled:opacity-60"
              />
            </label>
            <label className="block space-y-1">
              <span className="text-[11px] uppercase tracking-wide text-slate-500">Title</span>
              <input
                value={editing.title}
                onChange={e => setEditing({ ...editing, title: e.target.value })}
                placeholder="Quote approval"
                className="w-full bg-slate-800 border border-slate-700 rounded-lg px-3 py-2 text-sm text-white"
              />
            </label>
            <label className="block space-y-1">
              <span className="text-[11px] uppercase tracking-wide text-slate-500">System</span>
              <textarea
                value={editing.system}
                onChange={e => setEditing({ ...editing, system: e.target.value })}
                placeholder="You are a service advisor assistant. Only suggest legal nextSteps events."
                rows={3}
                className="w-full bg-slate-800 border border-slate-700 rounded-lg px-3 py-2 text-sm text-white"
              />
            </label>
            <label className="block space-y-1">
              <span className="text-[11px] uppercase tracking-wide text-slate-500">Instructions</span>
              <textarea
                required={isCreate}
                value={editing.instructions}
                onChange={e => setEditing({ ...editing, instructions: e.target.value })}
                placeholder="Approve if the quote is within 15% of estimate; otherwise request revision. Never approve missing labor hours."
                rows={8}
                className="w-full bg-slate-800 border border-slate-700 rounded-lg px-3 py-2 text-sm text-white"
              />
            </label>
            <div className="flex justify-end gap-2 pt-2">
              <button
                type="button"
                onClick={() => setEditing(null)}
                className="px-4 py-2 text-xs font-semibold text-slate-300 bg-slate-800 rounded-lg"
              >
                Cancel
              </button>
              <button
                type="submit"
                disabled={saving}
                className="px-4 py-2 text-xs font-semibold text-white bg-sky-600 hover:bg-sky-500 rounded-lg disabled:opacity-60"
              >
                {saving ? 'Saving…' : isCreate ? 'Create prompt' : 'Save changes'}
              </button>
            </div>
          </form>
        </div>
      )}
    </div>
  );
};
