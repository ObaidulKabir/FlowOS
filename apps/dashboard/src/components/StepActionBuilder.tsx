import React, { useState } from 'react';
import { Zap, Send, Bell, Radio, Plus, Trash2, Check, AlertCircle } from 'lucide-react';

export interface StepAction {
  actionType: string;
  target?: string;
  url?: string;
  method?: string;
  template?: string;
  payloadMapping?: Record<string, string>;
  condition?: string;
}

interface Props {
  stepId?: string;
  onEntry: StepAction[];
  onExit: StepAction[];
  availableEvents: { eventId: string; name: string }[];
  onChange: (onEntry: StepAction[], onExit: StepAction[]) => void;
}

export const StepActionBuilder: React.FC<Props> = ({
  stepId: _stepId,
  onEntry = [],
  onExit = [],
  availableEvents = [],
  onChange
}) => {
  const [activeHook, setActiveHook] = useState<'onEntry' | 'onExit'>('onEntry');
  const [isAdding, setIsAdding] = useState(false);
  const [editingIndex, setEditingIndex] = useState<number | null>(null);

  // New Action Form State
  const [actionType, setActionType] = useState<string>('Webhook');
  const [target, setTarget] = useState<string>('');
  const [url, setUrl] = useState<string>('');
  const [method, setMethod] = useState<string>('POST');
  const [template, setTemplate] = useState<string>('');
  const [condition, setCondition] = useState<string>('');
  const [mappingPairs, setMappingPairs] = useState<{ key: string; expr: string }[]>([
    { key: '', expr: '' }
  ]);
  const [formError, setFormError] = useState<string | null>(null);

  const currentList = activeHook === 'onEntry' ? onEntry : onExit;
  const totalHooks = onEntry.length + onExit.length;

  const resetForm = () => {
    setActionType('Webhook');
    setTarget('');
    setUrl('');
    setMethod('POST');
    setTemplate('');
    setCondition('');
    setMappingPairs([{ key: '', expr: '' }]);
    setFormError(null);
    setIsAdding(false);
    setEditingIndex(null);
  };

  const startEdit = (index: number) => {
    const act = currentList[index];
    if (!act) return;
    setActionType(act.actionType || 'Webhook');
    setTarget(act.target || '');
    setUrl(act.url || (act.actionType === 'Webhook' ? act.target || '' : ''));
    setMethod(act.method || 'POST');
    setTemplate(act.template || '');
    setCondition(act.condition || '');
    
    if (act.payloadMapping && Object.keys(act.payloadMapping).length > 0) {
      setMappingPairs(Object.entries(act.payloadMapping).map(([k, v]) => ({ key: k, expr: v })));
    } else {
      setMappingPairs([{ key: '', expr: '' }]);
    }
    
    setFormError(null);
    setEditingIndex(index);
    setIsAdding(true);
  };

  const handleSaveAction = () => {
    setFormError(null);

    let finalTarget = target.trim();
    let finalUrl = url.trim();

    if (actionType === 'Webhook') {
      if (!finalUrl && !finalTarget) {
        setFormError('Webhook URL is required.');
        return;
      }
      if (!finalUrl) finalUrl = finalTarget;
      if (!finalTarget) finalTarget = finalUrl;
    } else if (actionType === 'Notification') {
      if (!finalTarget) {
        setFormError('Target recipient is required (e.g. Submitter, Managers, or user ID).');
        return;
      }
    } else if (actionType === 'PublishEvent') {
      if (!finalTarget) {
        setFormError('Please select a domain event to publish.');
        return;
      }
    }

    const payloadMapping: Record<string, string> = {};
    mappingPairs.forEach(p => {
      const k = p.key.trim();
      const v = p.expr.trim();
      if (k && v) {
        payloadMapping[k] = v;
      }
    });

    const newAction: StepAction = {
      actionType,
      target: finalTarget,
      url: actionType === 'Webhook' ? finalUrl : undefined,
      method: actionType === 'Webhook' ? method : undefined,
      template: template.trim() || undefined,
      payloadMapping: Object.keys(payloadMapping).length > 0 ? payloadMapping : undefined,
      condition: condition.trim() || undefined
    };

    if (activeHook === 'onEntry') {
      const updated = [...onEntry];
      if (editingIndex !== null && editingIndex >= 0 && editingIndex < updated.length) {
        updated[editingIndex] = newAction;
      } else {
        updated.push(newAction);
      }
      onChange(updated, onExit);
    } else {
      const updated = [...onExit];
      if (editingIndex !== null && editingIndex >= 0 && editingIndex < updated.length) {
        updated[editingIndex] = newAction;
      } else {
        updated.push(newAction);
      }
      onChange(onEntry, updated);
    }

    resetForm();
  };

  const handleDeleteAction = (index: number) => {
    if (activeHook === 'onEntry') {
      const updated = onEntry.filter((_, i) => i !== index);
      onChange(updated, onExit);
    } else {
      const updated = onExit.filter((_, i) => i !== index);
      onChange(onEntry, updated);
    }
  };

  const insertToken = (token: string) => {
    setTemplate(prev => prev + ` {{${token}}}`);
  };

  return (
    <div className="mt-3 pt-3 border-t border-slate-100 dark:border-slate-800 space-y-2.5">
      {/* Header with Hook Tabs */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-1.5">
          <Zap size={13} className="text-amber-500" />
          <span className="text-[11px] font-bold text-slate-800 dark:text-slate-200 uppercase tracking-wider">
            Lifecycle Hooks ({totalHooks})
          </span>
        </div>
        <div className="flex rounded-lg bg-slate-100 dark:bg-slate-900 p-0.5 border border-slate-200 dark:border-slate-800 text-[10px]">
          <button
            type="button"
            onClick={() => { setActiveHook('onEntry'); resetForm(); }}
            className={`px-2 py-0.5 rounded font-semibold transition-all ${
              activeHook === 'onEntry'
                ? 'bg-emerald-600 text-white shadow-xs'
                : 'text-slate-500 hover:text-slate-900 dark:hover:text-white'
            }`}
          >
            OnEntry ({onEntry.length})
          </button>
          <button
            type="button"
            onClick={() => { setActiveHook('onExit'); resetForm(); }}
            className={`px-2 py-0.5 rounded font-semibold transition-all ${
              activeHook === 'onExit'
                ? 'bg-amber-600 text-white shadow-xs'
                : 'text-slate-500 hover:text-slate-900 dark:hover:text-white'
            }`}
          >
            OnExit ({onExit.length})
          </button>
        </div>
      </div>

      {/* Action Items List */}
      <div className="space-y-1.5">
        {currentList.length === 0 && !isAdding && (
          <p className="text-[11px] text-slate-600 dark:text-slate-300 italic py-1 bg-slate-50 dark:bg-slate-900/50 p-2 rounded border border-dashed border-slate-200 dark:border-slate-800">
            No {activeHook} hooks configured. Side-effects run automatically during step transitions.
          </p>
        )}

        {currentList.map((act, idx) => {
          const isWebhook = act.actionType === 'Webhook';
          const isNotif = act.actionType === 'Notification';
          return (
            <div
              key={idx}
              className="p-2 rounded-lg bg-slate-50 dark:bg-slate-900 border border-slate-200 dark:border-slate-800 text-xs space-y-1 group hover:border-slate-300 dark:hover:border-slate-700 transition-all"
            >
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-1.5 truncate">
                  <span
                    className={`px-1.5 py-0.5 rounded text-[9px] font-bold uppercase flex items-center gap-1 ${
                      isWebhook
                        ? 'bg-cyan-500/15 text-cyan-700 dark:text-cyan-300 border border-cyan-500/30'
                        : isNotif
                        ? 'bg-amber-500/15 text-amber-700 dark:text-amber-300 border border-amber-500/30'
                        : 'bg-indigo-500/15 text-indigo-700 dark:text-indigo-300 border border-indigo-500/30'
                    }`}
                  >
                    {isWebhook ? <Send size={9} /> : isNotif ? <Bell size={9} /> : <Radio size={9} />}
                    {act.actionType}
                  </span>
                  <span className="font-mono text-slate-700 dark:text-slate-200 truncate max-w-[180px]">
                    {act.target || act.url}
                  </span>
                </div>
                <div className="flex items-center gap-1 shrink-0">
                  {act.condition && (
                    <span className="px-1.5 py-0.5 rounded bg-purple-500/10 text-purple-700 dark:text-purple-300 text-[9px] font-mono border border-purple-500/20">
                      if: {act.condition}
                    </span>
                  )}
                  <button
                    type="button"
                    onClick={() => startEdit(idx)}
                    className="p-1 text-slate-400 hover:text-blue-500 rounded text-[10px]"
                    title="Edit action"
                  >
                    Edit
                  </button>
                  <button
                    type="button"
                    onClick={() => handleDeleteAction(idx)}
                    className="p-1 text-slate-400 hover:text-rose-500 rounded"
                    title="Delete action"
                  >
                    <Trash2 size={12} />
                  </button>
                </div>
              </div>

              {act.template && (
                <div className="text-[10px] text-slate-700 dark:text-slate-300 pl-1 font-mono truncate">
                  <span className="text-slate-600 dark:text-slate-300">Msg:</span> "{act.template}"
                </div>
              )}

              {act.payloadMapping && Object.keys(act.payloadMapping).length > 0 && (
                <div className="text-[10px] text-cyan-800 dark:text-cyan-300/90 pl-1 font-mono truncate">
                  <span className="text-slate-600 dark:text-slate-300">Payload:</span>{' '}
                  {Object.entries(act.payloadMapping)
                    .map(([k, v]) => `${k}➔${v}`)
                    .join(', ')}
                </div>
              )}
            </div>
          );
        })}
      </div>

      {/* Inline Form / Add Trigger */}
      {!isAdding ? (
        <button
          type="button"
          onClick={() => setIsAdding(true)}
          className="text-xs text-amber-700 dark:text-amber-400 hover:text-amber-800 dark:hover:text-amber-300 font-semibold flex items-center gap-1 pt-1"
        >
          <Plus size={13} /> Add {activeHook === 'onEntry' ? 'OnEntry' : 'OnExit'} Action Hook
        </button>
      ) : (
        <div className="p-3 bg-slate-50 dark:bg-slate-900 border border-amber-500/30 rounded-xl space-y-3 mt-2 shadow-xs">
          <div className="flex items-center justify-between">
            <span className="text-xs font-bold text-amber-700 dark:text-amber-300 flex items-center gap-1.5">
              <Zap size={13} /> {editingIndex !== null ? 'Edit' : 'Configure'} {activeHook} Hook
            </span>
            <button
              type="button"
              onClick={resetForm}
              className="text-[11px] text-slate-600 dark:text-slate-300 hover:text-slate-900 dark:hover:text-white"
            >
              Cancel
            </button>
          </div>

          {formError && (
            <div className="p-2 rounded bg-rose-500/10 border border-rose-500/30 text-rose-700 dark:text-rose-300 text-[11px] flex items-center gap-1.5">
              <AlertCircle size={13} className="shrink-0" />
              <span>{formError}</span>
            </div>
          )}

          {/* Action Type Selector */}
          <div className="grid grid-cols-3 gap-1.5">
            {[
              { id: 'Webhook', label: 'Webhook', icon: Send, color: 'text-cyan-600 dark:text-cyan-400' },
              { id: 'Notification', label: 'Notification', icon: Bell, color: 'text-amber-600 dark:text-amber-400' },
              { id: 'PublishEvent', label: 'PublishEvent', icon: Radio, color: 'text-indigo-600 dark:text-indigo-400' }
            ].map(t => {
              const Icon = t.icon;
              const isSel = actionType === t.id;
              return (
                <button
                  key={t.id}
                  type="button"
                  onClick={() => setActionType(t.id)}
                  className={`py-1.5 px-2 rounded-lg border text-xs font-semibold flex items-center justify-center gap-1.5 transition-all ${
                    isSel
                      ? 'bg-amber-500/15 border-amber-500 text-slate-900 dark:text-white shadow-xs'
                      : 'border-slate-200 dark:border-slate-800 text-slate-700 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800/50'
                  }`}
                >
                  <Icon size={12} className={t.color} />
                  <span>{t.label}</span>
                </button>
              );
            })}
          </div>

          {/* Condition Guard */}
          <div>
            <div className="flex items-center justify-between mb-1">
              <label className="text-[10px] font-bold text-slate-600 dark:text-slate-300 uppercase">
                Execution Guard Condition (Optional)
              </label>
              <span className="text-[9px] text-slate-600 dark:text-slate-300 font-mono">e.g. Amount &gt; 1000</span>
            </div>
            <input
              type="text"
              value={condition}
              onChange={e => setCondition(e.target.value)}
              placeholder="e.g. Amount > 500 && Urgent == true"
              className="w-full bg-white dark:bg-slate-950 border border-slate-300 dark:border-slate-800 rounded p-1.5 text-xs text-purple-700 dark:text-purple-300 font-mono focus:outline-none focus:border-purple-500"
            />
          </div>

          {/* Type-Specific Fields */}
          {actionType === 'Webhook' && (
            <div className="space-y-2.5">
              <div className="grid grid-cols-4 gap-2">
                <div className="col-span-3">
                  <label className="text-[10px] font-bold text-slate-600 dark:text-slate-300 uppercase block mb-1">
                    Destination URL
                  </label>
                  <input
                    type="text"
                    value={url}
                    onChange={e => { setUrl(e.target.value); setTarget(e.target.value); }}
                    placeholder="https://api.example.com/orders/{{OrderId}}"
                    className="w-full bg-white dark:bg-slate-950 border border-slate-300 dark:border-slate-800 rounded p-1.5 text-xs font-mono text-cyan-700 dark:text-cyan-300 focus:outline-none focus:border-cyan-500"
                  />
                </div>
                <div>
                  <label className="text-[10px] font-bold text-slate-600 dark:text-slate-300 uppercase block mb-1">
                    Method
                  </label>
                  <select
                    value={method}
                    onChange={e => setMethod(e.target.value)}
                    className="w-full bg-white dark:bg-slate-950 border border-slate-300 dark:border-slate-800 rounded p-1.5 text-xs font-bold text-slate-800 dark:text-slate-200 focus:outline-none"
                  >
                    <option value="POST">POST</option>
                    <option value="PUT">PUT</option>
                    <option value="GET">GET</option>
                  </select>
                </div>
              </div>

              {/* Payload Mapping Table */}
              <div>
                <div className="flex items-center justify-between mb-1">
                  <label className="text-[10px] font-bold text-slate-600 dark:text-slate-300 uppercase">
                    Payload Mapping (Dynamic Transformation)
                  </label>
                  <button
                    type="button"
                    onClick={() => setMappingPairs([...mappingPairs, { key: '', expr: '' }])}
                    className="text-[10px] text-cyan-700 dark:text-cyan-400 hover:underline flex items-center gap-0.5"
                  >
                    + Add Field
                  </button>
                </div>
                <div className="space-y-1.5 max-h-28 overflow-y-auto">
                  {mappingPairs.map((pair, pIdx) => (
                    <div key={pIdx} className="flex gap-1.5 items-center">
                      <input
                        type="text"
                        value={pair.key}
                        onChange={e => {
                          const updated = [...mappingPairs];
                          updated[pIdx].key = e.target.value;
                          setMappingPairs(updated);
                        }}
                        placeholder="Key (e.g. orderTotal)"
                        className="w-1/2 bg-white dark:bg-slate-950 border border-slate-300 dark:border-slate-800 rounded p-1 text-[11px] font-mono"
                      />
                      <span className="text-slate-600 dark:text-slate-300 text-xs">➔</span>
                      <input
                        type="text"
                        value={pair.expr}
                        onChange={e => {
                          const updated = [...mappingPairs];
                          updated[pIdx].expr = e.target.value;
                          setMappingPairs(updated);
                        }}
                        placeholder="Expr (e.g. Amount * 1.15)"
                        className="w-1/2 bg-white dark:bg-slate-950 border border-slate-300 dark:border-slate-800 rounded p-1 text-[11px] font-mono text-cyan-700 dark:text-cyan-300"
                      />
                      {mappingPairs.length > 1 && (
                        <button
                          type="button"
                          onClick={() => setMappingPairs(mappingPairs.filter((_, i) => i !== pIdx))}
                          className="text-slate-400 hover:text-rose-500 text-xs px-1"
                        >
                          ×
                        </button>
                      )}
                    </div>
                  ))}
                </div>
              </div>
            </div>
          )}

          {actionType === 'Notification' && (
            <div className="space-y-2.5">
              <div>
                <label className="text-[10px] font-bold text-slate-600 dark:text-slate-300 uppercase block mb-1">
                  Recipient Target (Role or User ID)
                </label>
                <input
                  type="text"
                  value={target}
                  onChange={e => setTarget(e.target.value)}
                  placeholder="e.g. Submitter, Managers, or user UUID"
                  className="w-full bg-white dark:bg-slate-950 border border-slate-300 dark:border-slate-800 rounded p-1.5 text-xs text-amber-700 dark:text-amber-300 font-medium focus:outline-none focus:border-amber-500"
                />
              </div>

              <div>
                <div className="flex items-center justify-between mb-1">
                  <label className="text-[10px] font-bold text-slate-600 dark:text-slate-300 uppercase">
                    Message Template (Supports tokens)
                  </label>
                  <div className="flex gap-1 text-[9px]">
                    <span className="text-slate-600 dark:text-slate-300">Quick Insert:</span>
                    {['OrderId', 'Amount', 'Requester'].map(t => (
                      <button
                        key={t}
                        type="button"
                        onClick={() => insertToken(t)}
                        className="bg-slate-200 dark:bg-slate-800 text-slate-800 dark:text-slate-200 px-1 rounded hover:bg-slate-300 dark:hover:bg-slate-700 font-mono"
                      >
                        +{t}
                      </button>
                    ))}
                  </div>
                </div>
                <textarea
                  value={template}
                  onChange={e => setTemplate(e.target.value)}
                  placeholder="e.g. Order {{OrderId}} for ${{Amount}} has been processed."
                  rows={2}
                  className="w-full bg-white dark:bg-slate-950 border border-slate-300 dark:border-slate-800 rounded p-1.5 text-xs font-mono text-slate-800 dark:text-slate-200 focus:outline-none focus:border-amber-500"
                />
              </div>
            </div>
          )}

          {actionType === 'PublishEvent' && (
            <div>
              <label className="text-[10px] font-bold text-slate-600 dark:text-slate-300 uppercase block mb-1">
                Declared Event To Publish
              </label>
              <select
                value={target}
                onChange={e => setTarget(e.target.value)}
                className="w-full bg-white dark:bg-slate-950 border border-slate-300 dark:border-slate-800 rounded p-1.5 text-xs font-bold text-indigo-700 dark:text-indigo-300 focus:outline-none"
              >
                <option value="">(Select Domain Event)</option>
                {availableEvents.map(e => (
                  <option key={e.eventId} value={e.eventId}>
                    {e.name} ({e.eventId})
                  </option>
                ))}
              </select>
            </div>
          )}

          {/* Action Form Footer */}
          <div className="flex justify-end gap-2 pt-2 border-t border-slate-200 dark:border-slate-800">
            <button
              type="button"
              onClick={resetForm}
              className="px-3 py-1 text-xs text-slate-600 dark:text-slate-300 hover:text-slate-900 dark:hover:text-white rounded"
            >
              Cancel
            </button>
            <button
              type="button"
              onClick={handleSaveAction}
              className="px-3 py-1 bg-amber-600 hover:bg-amber-500 text-white rounded text-xs font-bold shadow-xs flex items-center gap-1 transition-all"
            >
              <Check size={12} /> {editingIndex !== null ? 'Update Action' : 'Save Action'}
            </button>
          </div>
        </div>
      )}
    </div>
  );
};
