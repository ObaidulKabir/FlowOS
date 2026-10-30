import React, { useState } from 'react';
import { BellRing, CheckCircle2, XCircle } from 'lucide-react';
import { TenantPlanRequestItem } from '../types';

interface PlanRequestInboxProps {
  requests: TenantPlanRequestItem[];
  busyTenantId: string | null;
  error: string | null;
  onApprove: (item: TenantPlanRequestItem) => void;
  onReject: (item: TenantPlanRequestItem, reason: string) => void;
}

/** Live list of tenants waiting for a plan, with one-click approval. */
export const PlanRequestInbox: React.FC<PlanRequestInboxProps> = ({
  requests,
  busyTenantId,
  error,
  onApprove,
  onReject
}) => {
  const [rejectingId, setRejectingId] = useState<string | null>(null);
  const [reason, setReason] = useState('');

  if (requests.length === 0 && !error) return null;

  return (
    <section
      aria-label="Plan activation requests"
      className="p-4 bg-amber-900/20 border border-amber-600/40 rounded-xl space-y-3"
    >
      <div className="flex items-center gap-2 text-xs font-semibold text-amber-200">
        <BellRing size={14} />
        <span>
          {requests.length} tenant{requests.length === 1 ? '' : 's'} waiting for plan activation
        </span>
        <span className="text-[10px] font-normal text-amber-300/70">Updates automatically</span>
      </div>

      {error && <p role="alert" className="text-xs text-rose-300">{error}</p>}

      {requests.map(item => {
        const busy = busyTenantId === item.tenantId;
        const rejecting = rejectingId === item.tenantId;
        return (
          <div
            key={item.tenantId}
            className="bg-slate-950/60 border border-slate-800 rounded-lg px-3 py-2 space-y-2"
          >
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div className="text-xs text-slate-300">
                <div className="font-semibold text-white">{item.tenantName}</div>
                <div>
                  Requests <span className="text-amber-300">{item.planRequest.requestedPlan}</span>
                  {item.planRequest.paymentReference ? ` · ref ${item.planRequest.paymentReference}` : ' · no payment reference'}
                  {item.planRequest.requestedAt ? ` · ${new Date(item.planRequest.requestedAt).toLocaleString()}` : ''}
                </div>
              </div>
              <div className="flex gap-2">
                <button
                  type="button"
                  disabled={busy}
                  onClick={() => onApprove(item)}
                  className="px-3 py-1.5 bg-emerald-600 hover:bg-emerald-500 disabled:opacity-60 text-white rounded text-[11px] font-semibold inline-flex items-center gap-1"
                  title={`Activate ${item.planRequest.requestedPlan} with billing Active`}
                >
                  <CheckCircle2 size={12} />
                  {busy ? 'Working…' : `Approve ${item.planRequest.requestedPlan}`}
                </button>
                <button
                  type="button"
                  disabled={busy}
                  onClick={() => { setRejectingId(rejecting ? null : item.tenantId); setReason(''); }}
                  className="px-3 py-1.5 bg-slate-700 hover:bg-slate-600 disabled:opacity-60 text-slate-100 rounded text-[11px] font-semibold inline-flex items-center gap-1"
                >
                  <XCircle size={12} />
                  Reject
                </button>
              </div>
            </div>
            {rejecting && (
              <div className="flex gap-2">
                <input
                  aria-label={`Rejection reason for ${item.tenantName}`}
                  value={reason}
                  onChange={e => setReason(e.target.value)}
                  placeholder="Reason shown to the tenant (optional)"
                  className="flex-1 bg-slate-900 border border-slate-700 rounded px-2 py-1 text-xs text-slate-100"
                />
                <button
                  type="button"
                  disabled={busy}
                  onClick={() => { onReject(item, reason.trim()); setRejectingId(null); setReason(''); }}
                  className="px-3 py-1 bg-rose-600 hover:bg-rose-500 disabled:opacity-60 text-white rounded text-[11px] font-semibold"
                >
                  Confirm reject
                </button>
              </div>
            )}
          </div>
        );
      })}
    </section>
  );
};
