import React, { useEffect, useState } from 'react';
import { AlertCircle, Plus, RefreshCw, Shield, Trash2 } from 'lucide-react';
import { api } from '../api/client';
import { TenantRoleDto } from '../types';

const RESERVED_ROLES = new Set(['admin', 'apikey']);
const STARTER_CAPABILITIES = [
  'workflow.start',
  'workflow.read',
  'event.publish',
  'task.complete',
  'iam.read',
  'iam.manage'
];

export const TenantRuntimeRolesPanel: React.FC = () => {
  const [roles, setRoles] = useState<TenantRoleDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [roleName, setRoleName] = useState('');
  const [capabilityDrafts, setCapabilityDrafts] = useState<Record<string, string>>({});
  const [submitting, setSubmitting] = useState(false);

  const loadRoles = async () => {
    setLoading(true);
    setError(null);
    try {
      setRoles(await api.listRoles());
    } catch (err: any) {
      setError(err.message || 'Failed to load tenant runtime roles. Admin or iam.manage is required.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadRoles();
  }, []);

  const handleCreateRole = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!roleName.trim()) return;
    setSubmitting(true);
    try {
      await api.createRole(roleName.trim());
      setRoleName('');
      await loadRoles();
    } catch (err: any) {
      setError(err.message || 'Failed to create role');
    } finally {
      setSubmitting(false);
    }
  };

  const handleGrant = async (role: TenantRoleDto) => {
    const capability = (capabilityDrafts[role.id] || '').trim();
    if (!capability) return;
    try {
      await api.addRoleCapability(role.id, capability);
      setCapabilityDrafts(current => ({ ...current, [role.id]: '' }));
      await loadRoles();
    } catch (err: any) {
      setError(err.message || 'Failed to grant capability');
    }
  };

  const handleRevoke = async (role: TenantRoleDto, capability: string) => {
    try {
      await api.removeRoleCapability(role.id, capability);
      await loadRoles();
    } catch (err: any) {
      setError(err.message || 'Failed to revoke capability');
    }
  };

  return (
    <div className="space-y-4 rounded-2xl border border-slate-700/80 bg-slate-850 p-6">
      <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
        <div>
          <div className="mb-1 flex items-center gap-2">
            <Shield className="text-cyan-400" size={18} />
            <h3 className="text-sm font-bold text-white">Runtime roles and capabilities</h3>
          </div>
          <p className="text-xs text-slate-400">
            `workflow.start` is a tenant role capability. API-key scopes such as `workflow:start` only narrow that grant.
            Reserved Admin and ApiKey roles are provisioned automatically.
          </p>
        </div>
        <button
          type="button"
          onClick={loadRoles}
          className="rounded-xl border border-slate-700 bg-slate-800 p-2 text-slate-300 hover:bg-slate-750"
          title="Refresh roles"
        >
          <RefreshCw size={14} className={loading ? 'animate-spin text-blue-400' : ''} />
        </button>
      </div>

      {error && (
        <div className="flex items-start gap-2 rounded-xl border border-rose-700 bg-rose-900/30 p-3 text-xs text-rose-300">
          <AlertCircle size={16} className="mt-0.5 shrink-0" />
          <span>{error}</span>
        </div>
      )}

      <form onSubmit={handleCreateRole} className="flex flex-col gap-2 sm:flex-row">
        <input
          type="text"
          value={roleName}
          onChange={event => setRoleName(event.target.value)}
          placeholder="New role name, e.g. SalesFlowOperator"
          className="flex-1 rounded-xl border border-slate-700 bg-slate-950 px-3 py-2 text-xs text-white focus:border-blue-500 focus:outline-none"
        />
        <button
          type="submit"
          disabled={submitting || !roleName.trim()}
          className="inline-flex items-center justify-center gap-1.5 rounded-xl bg-blue-600 px-3 py-2 text-xs font-semibold text-white hover:bg-blue-500 disabled:opacity-50"
        >
          <Plus size={14} />
          Create role
        </button>
      </form>

      <div className="space-y-3">
        {roles.map(role => {
          const reserved = RESERVED_ROLES.has(role.name.toLowerCase());
          return (
            <div key={role.id} className="rounded-xl border border-slate-800 bg-slate-950/70 p-4">
              <div className="mb-2 flex items-center justify-between gap-2">
                <div className="text-sm font-semibold text-white">{role.name}</div>
                {reserved && (
                  <span className="rounded-full border border-cyan-800 bg-cyan-950/50 px-2 py-0.5 text-[10px] font-semibold text-cyan-300">
                    Reserved
                  </span>
                )}
              </div>
              <div className="mb-3 flex flex-wrap gap-1">
                {role.capabilities.length === 0 && (
                  <span className="text-[11px] text-slate-500">No capabilities granted.</span>
                )}
                {role.capabilities.map(capability => (
                  <span
                    key={capability}
                    className="inline-flex items-center gap-1 rounded border border-slate-800 bg-slate-900 px-1.5 py-0.5 font-mono text-[10px] text-slate-300"
                  >
                    {capability}
                    <button
                      type="button"
                      onClick={() => handleRevoke(role, capability)}
                      className="text-rose-400 hover:text-rose-300"
                      title={`Revoke ${capability}`}
                    >
                      <Trash2 size={10} />
                    </button>
                  </span>
                ))}
              </div>
              <div className="flex flex-col gap-2 sm:flex-row">
                <input
                  list={`capability-suggestions-${role.id}`}
                  value={capabilityDrafts[role.id] || ''}
                  onChange={event => setCapabilityDrafts(current => ({ ...current, [role.id]: event.target.value }))}
                  placeholder="Grant capability, e.g. workflow.start"
                  className="flex-1 rounded-lg border border-slate-800 bg-slate-900 px-2.5 py-1.5 font-mono text-[11px] text-white focus:border-blue-500 focus:outline-none"
                />
                <datalist id={`capability-suggestions-${role.id}`}>
                  {STARTER_CAPABILITIES.map(capability => (
                    <option key={capability} value={capability} />
                  ))}
                </datalist>
                <button
                  type="button"
                  onClick={() => handleGrant(role)}
                  className="rounded-lg bg-slate-800 px-3 py-1.5 text-[11px] font-semibold text-slate-200 hover:bg-slate-700"
                >
                  Grant
                </button>
              </div>
            </div>
          );
        })}

        {roles.length === 0 && !loading && !error && (
          <p className="text-xs text-slate-500">No tenant roles were returned. Refresh after the tenant security backfill completes.</p>
        )}
      </div>
    </div>
  );
};
