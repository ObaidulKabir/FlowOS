import React from 'react';
import {
  API_KEY_SCOPE_DEFINITIONS,
  API_KEY_SCOPE_PRESETS,
  effectiveCapabilitiesForScopes
} from '../lib/apiKeyScopes';

interface Props {
  value: string[];
  onChange: (scopes: string[]) => void;
}

export const ApiKeyScopePicker: React.FC<Props> = ({ value, onChange }) => {
  const capabilities = effectiveCapabilitiesForScopes(value);

  const toggle = (scope: string, checked: boolean) => {
    if (scope === '*') {
      onChange(checked ? ['*'] : [...API_KEY_SCOPE_PRESETS.readOnly]);
      return;
    }

    const withoutWildcard = value.filter(item => item !== '*' && item !== 'admin:*');
    const next = checked
      ? Array.from(new Set([...withoutWildcard, scope]))
      : withoutWildcard.filter(item => item !== scope);
    onChange(next.length > 0 ? next : [...API_KEY_SCOPE_PRESETS.readOnly]);
  };

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between gap-3">
        <label className="text-xs font-bold text-slate-300">Permission scopes</label>
        <div className="flex gap-1 text-[10px]">
          <button type="button" onClick={() => onChange([...API_KEY_SCOPE_PRESETS.full])} className="text-blue-400 hover:underline">
            Full
          </button>
          <span className="text-slate-600">•</span>
          <button type="button" onClick={() => onChange([...API_KEY_SCOPE_PRESETS.operator])} className="text-blue-400 hover:underline">
            Operator
          </button>
          <span className="text-slate-600">•</span>
          <button type="button" onClick={() => onChange([...API_KEY_SCOPE_PRESETS.readOnly])} className="text-blue-400 hover:underline">
            Read-only
          </button>
        </div>
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-2 gap-2 rounded-lg border border-slate-700/60 bg-slate-800/60 p-3">
        {API_KEY_SCOPE_DEFINITIONS.map(definition => {
          const wildcard = value.includes('*') || value.includes('admin:*');
          const checked = wildcard || value.includes(definition.id);
          return (
            <label key={definition.id} className="flex cursor-pointer items-start gap-2 text-slate-300">
              <input
                type="checkbox"
                checked={checked}
                disabled={wildcard && definition.id !== '*'}
                onChange={event => toggle(definition.id, event.target.checked)}
                className="mt-0.5 rounded border-slate-700 bg-slate-900 text-blue-500 focus:ring-0"
              />
              <span>
                <span className="block text-[11px] font-semibold">{definition.label}</span>
                <span className="block text-[9px] leading-4 text-slate-500">{definition.description}</span>
              </span>
            </label>
          );
        })}
      </div>

      <div className="rounded-lg border border-cyan-900/60 bg-cyan-950/20 p-2.5">
        <div className="mb-1 text-[10px] font-semibold text-cyan-300">Effective capability boundary</div>
        <div className="flex flex-wrap gap-1">
          {capabilities.map(capability => (
            <span key={capability} className="rounded border border-cyan-900 bg-slate-950 px-1.5 py-0.5 font-mono text-[9px] text-cyan-200">
              {capability}
            </span>
          ))}
        </div>
        <p className="mt-1.5 text-[9px] leading-4 text-slate-500">
          Scopes restrict the tenant role grant; they never expand it. Scope changes require key rotation.
        </p>
      </div>
    </div>
  );
};
