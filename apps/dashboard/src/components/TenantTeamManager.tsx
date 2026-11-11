import React, { useEffect, useState } from 'react';
import { api } from '../api/client';
import { TenantTeammateDto } from '../types';
import { isPrivilegedRole } from '../auth/platformAdminSession';
import { Users, UserPlus, Trash2, AlertCircle, RefreshCw, ShieldCheck, Copy, Check, Mail, Clock, Lock } from 'lucide-react';

interface Props {
  tenantId: string;
  tenantName: string;
  /** The signed-in TenantUser's own Role within this tenant (AuthSession.userRole); undefined for a
   * shared-API-key session, which has no individual teammate identity to check. */
  currentUserRole?: string;
}

/** Organizational team access: invite/list/remove teammate logins (TenantUsers) of one tenant - see
 * docs/29-organizational-team-access.md. Distinct from TenantApiKeyManager's shared TenantApiKey(s). */
export const TenantTeamManager: React.FC<Props> = ({ tenantId, tenantName, currentUserRole }) => {
  const [teammates, setTeammates] = useState<TenantTeammateDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [forbidden, setForbidden] = useState(false);

  const [showInviteModal, setShowInviteModal] = useState(false);
  const [inviteEmail, setInviteEmail] = useState('');
  const [inviteFullName, setInviteFullName] = useState('');
  const [inviteRole, setInviteRole] = useState('Employee');
  const [submitting, setSubmitting] = useState(false);
  const [lastInviteToken, setLastInviteToken] = useState<string | null>(null);
  const [lastInviteEmail, setLastInviteEmail] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);

  const canManage = isPrivilegedRole(currentUserRole);

  const loadTeammates = async () => {
    setLoading(true);
    setError(null);
    setForbidden(false);
    try {
      const data = await api.listTeammates(tenantId);
      setTeammates(data);
    } catch (err: any) {
      if (String(err?.message || '').includes('HTTP 403')) {
        setForbidden(true);
      } else {
        setError(err.message || 'Failed to load teammates');
      }
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    setLastInviteToken(null);
    setLastInviteEmail(null);
    loadTeammates();
  }, [tenantId]);

  const handleInvite = async (e: React.FormEvent) => {
    e.preventDefault();
    setSubmitting(true);
    try {
      const res = await api.inviteTeammate(tenantId, {
        email: inviteEmail.trim(),
        fullName: inviteFullName.trim(),
        role: inviteRole
      });
      if (!res.ok) {
        alert(`Failed to invite teammate: ${res.message}`);
        return;
      }
      setLastInviteToken(res.inviteToken || null);
      setLastInviteEmail(res.email || inviteEmail.trim());
      setShowInviteModal(false);
      setInviteEmail('');
      setInviteFullName('');
      setInviteRole('Employee');
      await loadTeammates();
    } catch (err: any) {
      alert(`Failed to invite teammate: ${err.message}`);
    } finally {
      setSubmitting(false);
    }
  };

  const handleRemove = async (userId: string, email: string) => {
    if (!confirm(`Remove ${email} from ${tenantName}? They will immediately lose access.`)) {
      return;
    }
    try {
      await api.removeTeammate(tenantId, userId);
      await loadTeammates();
    } catch (err: any) {
      alert(`Failed to remove teammate: ${err.message}`);
    }
  };

  const handleCopyToken = async () => {
    if (!lastInviteToken) return;
    try {
      await navigator.clipboard.writeText(lastInviteToken);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      // Clipboard access can fail silently in some browsers/contexts; the code is still shown below.
    }
  };

  return (
    <div className="space-y-6">
      {/* Header Banner */}
      <div className="bg-slate-850 p-6 rounded-2xl border border-slate-700/80 flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <div className="flex items-center gap-2 mb-1">
            <Users className="text-indigo-400" size={20} />
            <h3 className="text-lg font-bold text-white">Teammates & Organizational Access</h3>
          </div>
          <p className="text-xs text-slate-400">
            Invite coworkers to their own individual login under <strong>{tenantName}</strong>. Each
            teammate sets their own password - distinct from the shared API keys on the other tab.
          </p>
        </div>

        <div className="flex items-center gap-2.5">
          <button
            onClick={loadTeammates}
            className="p-2 bg-slate-800 hover:bg-slate-750 text-slate-300 border border-slate-700 rounded-xl transition-colors"
            title="Refresh teammates"
          >
            <RefreshCw size={14} className={loading ? 'animate-spin text-blue-400' : ''} />
          </button>
          <button
            onClick={() => setShowInviteModal(true)}
            disabled={!canManage}
            title={canManage ? undefined : 'Only tenant Admins can invite teammates'}
            className="px-4 py-2 bg-indigo-600 hover:bg-indigo-500 disabled:opacity-40 disabled:cursor-not-allowed text-white font-semibold rounded-xl text-xs flex items-center gap-2 shadow-lg shadow-indigo-500/25 transition-all"
          >
            <UserPlus size={15} />
            <span>Invite Teammate</span>
          </button>
        </div>
      </div>

      {!canManage && (
        <div className="p-3 bg-amber-500/10 border border-amber-500/20 rounded-xl text-xs text-amber-200 flex items-start gap-2">
          <Lock size={14} className="mt-0.5 shrink-0" />
          <span>Only tenant Admins can invite or remove teammates. You can still see who has access below.</span>
        </div>
      )}

      {forbidden && (
        <div className="p-4 bg-rose-900/30 border border-rose-700 rounded-xl text-xs text-rose-300 flex items-center gap-2">
          <AlertCircle size={16} />
          <span>Only tenant Admins can manage teammates for this tenant.</span>
        </div>
      )}

      {error && (
        <div className="p-4 bg-rose-900/30 border border-rose-700 rounded-xl text-xs text-rose-300 flex items-center gap-2">
          <AlertCircle size={16} />
          <span>{error}</span>
        </div>
      )}

      {/* Newly Invited Teammate Banner */}
      {lastInviteToken && lastInviteEmail && (
        <div className="p-5 bg-emerald-950/60 border border-emerald-500/40 rounded-2xl space-y-3 animate-fadeIn">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-2 text-emerald-300 font-bold text-sm">
              <ShieldCheck size={18} className="text-emerald-400" />
              <span>Invited {lastInviteEmail}!</span>
            </div>
            <button onClick={() => setLastInviteToken(null)} className="text-xs text-slate-400 hover:text-white">
              Dismiss
            </button>
          </div>
          <p className="text-xs text-slate-300">
            An invite email was sent. If it doesn&apos;t arrive, share this code and tell them to open
            &quot;Invited by a teammate?&quot; on the sign-in screen with email <strong>{lastInviteEmail}</strong>.
          </p>
          <div className="p-3 bg-slate-950 border border-emerald-500/30 rounded-xl flex items-center justify-between font-mono text-xs text-emerald-300">
            <span className="break-all select-all font-bold tracking-widest">{lastInviteToken}</span>
            <button
              onClick={handleCopyToken}
              className="ml-3 px-3 py-1.5 bg-emerald-600 hover:bg-emerald-500 text-white rounded-lg flex items-center gap-1.5 shrink-0 transition-colors"
            >
              {copied ? <Check size={13} /> : <Copy size={13} />}
              <span>{copied ? 'Copied' : 'Copy Code'}</span>
            </button>
          </div>
        </div>
      )}

      {/* Teammates Table */}
      <div className="bg-slate-800 border border-slate-700 rounded-2xl overflow-hidden shadow-xl">
        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs text-slate-300">
            <thead className="bg-slate-900 text-slate-400 uppercase text-[10px] tracking-wider border-b border-slate-700">
              <tr>
                <th className="py-3.5 px-4">Teammate</th>
                <th className="py-3.5 px-4">Role</th>
                <th className="py-3.5 px-4">Status</th>
                <th className="py-3.5 px-4">Joined / Last Login</th>
                <th className="py-3.5 px-4 text-right">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-700/60">
              {teammates.map((t) => (
                <tr key={t.id} className="hover:bg-slate-750 transition-colors">
                  <td className="py-3.5 px-4">
                    <div className="font-bold text-white text-xs">{t.fullName}</div>
                    <div className="text-[11px] text-slate-400 flex items-center gap-1">
                      <Mail size={11} /> {t.email}
                    </div>
                  </td>
                  <td className="py-3.5 px-4">
                    <span className={`px-2 py-0.5 rounded-full text-[10px] font-bold ${
                      isPrivilegedRole(t.role)
                        ? 'bg-indigo-500/20 text-indigo-300 border border-indigo-500/30'
                        : 'bg-slate-700/40 text-slate-300 border border-slate-600/40'
                    }`}>
                      {t.role}
                    </span>
                  </td>
                  <td className="py-3.5 px-4">
                    {t.isEmailVerified ? (
                      <span className="px-2 py-0.5 rounded-full text-[10px] font-bold bg-emerald-500/20 text-emerald-300 border border-emerald-500/30">
                        Active
                      </span>
                    ) : (
                      <span className="px-2 py-0.5 rounded-full text-[10px] font-bold bg-amber-500/20 text-amber-300 border border-amber-500/30">
                        Invite Pending
                      </span>
                    )}
                  </td>
                  <td className="py-3.5 px-4 text-slate-400 text-[11px]">
                    <div>Joined: {new Date(t.createdAt).toLocaleDateString()}</div>
                    <div className="text-slate-500 text-[10px] flex items-center gap-1">
                      <Clock size={10} />
                      {t.lastLoginAt ? new Date(t.lastLoginAt).toLocaleDateString() : 'Never signed in'}
                    </div>
                  </td>
                  <td className="py-3.5 px-4 text-right">
                    <button
                      onClick={() => handleRemove(t.id, t.email)}
                      disabled={!canManage}
                      title={canManage ? 'Remove teammate' : 'Only tenant Admins can remove teammates'}
                      className="p-1.5 text-rose-400 hover:text-rose-300 hover:bg-rose-500/10 disabled:opacity-30 disabled:cursor-not-allowed rounded-lg transition-colors"
                    >
                      <Trash2 size={15} />
                    </button>
                  </td>
                </tr>
              ))}

              {teammates.length === 0 && !loading && !forbidden && (
                <tr>
                  <td colSpan={5} className="py-12 text-center text-slate-500 text-xs">
                    No teammates yet. Click "Invite Teammate" above to give a coworker their own login.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      {/* Invite Modal */}
      {showInviteModal && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-slate-900 border border-slate-700 rounded-2xl max-w-md w-full p-6 shadow-2xl space-y-4">
            <div className="flex items-center justify-between border-b border-slate-800 pb-3">
              <h3 className="text-base font-bold text-white flex items-center gap-2">
                <UserPlus className="text-indigo-400" size={18} />
                Invite Teammate
              </h3>
              <button onClick={() => setShowInviteModal(false)} className="text-slate-400 hover:text-white">
                &times;
              </button>
            </div>

            <form onSubmit={handleInvite} className="space-y-4 text-xs">
              <div className="space-y-1">
                <label className="text-slate-300 font-medium">Email Address</label>
                <input
                  type="email"
                  required
                  placeholder="coworker@yourcompany.com"
                  value={inviteEmail}
                  onChange={e => setInviteEmail(e.target.value)}
                  className="w-full bg-slate-950 border border-slate-700 rounded-xl px-3 py-2 text-white focus:outline-none focus:border-indigo-500"
                />
              </div>

              <div className="space-y-1">
                <label className="text-slate-300 font-medium">Full Name</label>
                <input
                  type="text"
                  placeholder="e.g. Jordan Lee"
                  value={inviteFullName}
                  onChange={e => setInviteFullName(e.target.value)}
                  className="w-full bg-slate-950 border border-slate-700 rounded-xl px-3 py-2 text-white focus:outline-none focus:border-indigo-500"
                />
              </div>

              <div className="space-y-1">
                <label className="text-slate-300 font-medium">Role</label>
                <select
                  value={inviteRole}
                  onChange={e => setInviteRole(e.target.value)}
                  className="w-full bg-slate-950 border border-slate-700 rounded-xl px-3 py-2 text-white focus:outline-none focus:border-indigo-500"
                >
                  <option value="Employee">Employee</option>
                  <option value="Admin">Admin</option>
                </select>
                <span className="text-[10px] text-slate-500">Admins can manage API keys and other teammates.</span>
              </div>

              <div className="pt-2 flex justify-end gap-2 border-t border-slate-800">
                <button
                  type="button"
                  onClick={() => setShowInviteModal(false)}
                  className="px-4 py-2 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded-xl font-semibold"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={submitting}
                  className="px-4 py-2 bg-indigo-600 hover:bg-indigo-500 text-white rounded-xl font-semibold flex items-center gap-1.5"
                >
                  {submitting ? 'Inviting...' : 'Send Invite'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
