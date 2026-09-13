import React, { useEffect, useMemo, useState } from 'react';
import {
  AlertCircle,
  CheckCircle2,
  Clock,
  GitBranch,
  Pause,
  Play,
  Rewind,
  SkipBack,
  SkipForward,
  Zap
} from 'lucide-react';
import { api } from '../api/client';
import { TimeTravelForkResult, TimeTravelReplay, TimeTravelSnapshot } from '../types';
import { WorkflowGraphVisualizer } from './WorkflowGraphVisualizer';

interface Props {
  workflowInstanceId: string;
  definition?: any;
  onSnapshotChange?: (snapshot: TimeTravelSnapshot | null) => void;
}

const formatValue = (value: unknown) => {
  if (value === null || value === undefined) return 'null';
  if (typeof value === 'object') {
    try {
      return JSON.stringify(value);
    } catch {
      return String(value);
    }
  }
  return String(value);
};

const variableDiff = (previous: TimeTravelSnapshot | undefined, current: TimeTravelSnapshot) => {
  const prev = previous?.variables ?? {};
  const curr = current.variables ?? {};
  const keys = Array.from(new Set([...Object.keys(prev), ...Object.keys(curr)])).sort();

  return keys.map((key) => {
    const before = prev[key];
    const after = curr[key];
    const existed = Object.prototype.hasOwnProperty.call(prev, key);
    const exists = Object.prototype.hasOwnProperty.call(curr, key);
    const changed = !existed || !exists || formatValue(before) !== formatValue(after);
    return { key, before, after, changed, existed, exists };
  });
};

export const TimeTravelPlayer: React.FC<Props> = ({
  workflowInstanceId,
  definition,
  onSnapshotChange
}) => {
  const [replay, setReplay] = useState<TimeTravelReplay | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [index, setIndex] = useState(0);
  const [playing, setPlaying] = useState(false);
  const [altEvent, setAltEvent] = useState('EVT-REJECT');
  const [altPayload, setAltPayload] = useState('{\n  "amount": 0\n}');
  const [forking, setForking] = useState(false);
  const [forkResult, setForkResult] = useState<TimeTravelForkResult | null>(null);
  const [forkError, setForkError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    const load = async () => {
      setLoading(true);
      setError(null);
      setPlaying(false);
      setForkResult(null);
      try {
        const data = await api.getTimeTravelReplay(workflowInstanceId);
        if (cancelled) return;
        setReplay(data);
        setIndex(Math.max(0, (data.snapshots?.length ?? 1) - 1));
      } catch (err: any) {
        if (!cancelled) {
          setError(err.message || 'Failed to reconstruct replay timeline');
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    };

    load();
    return () => {
      cancelled = true;
    };
  }, [workflowInstanceId]);

  const snapshot = replay?.snapshots?.[index];
  const previous = index > 0 ? replay?.snapshots[index - 1] : undefined;

  useEffect(() => {
    onSnapshotChange?.(snapshot ?? null);
  }, [snapshot, onSnapshotChange]);

  useEffect(() => {
    if (!playing || !replay?.snapshots.length) return;
    const timer = window.setInterval(() => {
      setIndex((current) => {
        if (current >= replay.snapshots.length - 1) {
          setPlaying(false);
          return current;
        }
        return current + 1;
      });
    }, 900);
    return () => window.clearInterval(timer);
  }, [playing, replay]);

  const diffs = useMemo(
    () => (snapshot ? variableDiff(previous, snapshot) : []),
    [previous, snapshot]
  );

  const completedSteps = useMemo(() => {
    if (!replay) return [];
    return Array.from(new Set(
      replay.snapshots.slice(0, index + 1).map((step) => step.toStepId).filter(Boolean)
    ));
  }, [replay, index]);

  const handleFork = async () => {
    if (!snapshot) return;
    let payload: unknown = undefined;
    const trimmed = altPayload.trim();
    if (trimmed) {
      try {
        payload = JSON.parse(trimmed);
      } catch {
        setForkError('Alternative payload must be valid JSON.');
        return;
      }
    }

    setForking(true);
    setForkError(null);
    try {
      const result = await api.simulateFork(workflowInstanceId, snapshot.stepIndex, altEvent.trim(), payload);
      setForkResult(result);
    } catch (err: any) {
      setForkError(err.message || 'Fork simulation failed');
    } finally {
      setForking(false);
    }
  };

  if (loading) {
    return (
      <div className="py-12 text-center text-slate-400 text-xs">
        <div className="animate-spin inline-block w-6 h-6 border-2 border-cyan-500 border-t-transparent rounded-full mb-2" />
        <div>Reconstructing event-store snapshots...</div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="p-4 bg-rose-500/10 border border-rose-500/30 rounded-xl text-xs text-rose-300">
        {error}
      </div>
    );
  }

  if (!replay || !snapshot) {
    return (
      <div className="py-12 text-center text-slate-500 text-xs italic">
        No historical snapshots are available for this instance yet.
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3 bg-slate-950/70 border border-slate-800 rounded-xl p-3">
        <div className="flex items-center gap-2 text-cyan-300">
          <Rewind size={16} />
          <div>
            <div className="text-xs font-bold text-white">Time-Travel Debugger</div>
            <div className="text-[10px] text-slate-500">
              {replay.workflowClassName} v{replay.workflowVersion} · {replay.totalSteps} snapshots · {replay.status}
            </div>
          </div>
        </div>
        <div className="flex items-center gap-1.5">
          <button
            onClick={() => { setPlaying(false); setIndex((i) => Math.max(0, i - 1)); }}
            className="p-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-200"
            title="Step back"
          >
            <SkipBack size={14} />
          </button>
          <button
            onClick={() => setPlaying((value) => !value)}
            className="p-1.5 rounded-lg bg-cyan-600 hover:bg-cyan-500 text-white"
            title={playing ? 'Pause' : 'Play'}
          >
            {playing ? <Pause size={14} /> : <Play size={14} />}
          </button>
          <button
            onClick={() => { setPlaying(false); setIndex((i) => Math.min(replay.snapshots.length - 1, i + 1)); }}
            className="p-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-200"
            title="Step forward"
          >
            <SkipForward size={14} />
          </button>
        </div>
      </div>

      <div>
        <input
          type="range"
          min={0}
          max={Math.max(0, replay.snapshots.length - 1)}
          value={index}
          onChange={(event) => {
            setPlaying(false);
            setIndex(Number(event.target.value));
          }}
          className="w-full accent-cyan-500"
        />
        <div className="flex justify-between text-[10px] text-slate-500 mt-1 font-mono">
          <span>Step 0</span>
          <span>{snapshot.eventType} @ {new Date(snapshot.timestamp).toLocaleString()}</span>
          <span>Step {replay.snapshots.length - 1}</span>
        </div>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <div className="space-y-3">
          <div className="bg-slate-950/60 border border-slate-800 rounded-xl p-3 space-y-2">
            <div className="text-[10px] uppercase tracking-wider text-slate-500 font-semibold">Snapshot inspector</div>
            <p className="text-xs text-slate-300">{snapshot.summary}</p>
            <div className="flex flex-wrap gap-1.5">
              <span className="px-2 py-0.5 rounded bg-blue-500/10 text-blue-300 border border-blue-500/20 font-mono text-[10px]">
                {snapshot.toStepId}
              </span>
              {(snapshot.activeStepIds || []).map((stepId) => (
                <span key={stepId} className="px-2 py-0.5 rounded bg-cyan-500/10 text-cyan-300 border border-cyan-500/20 font-mono text-[10px]">
                  token:{stepId}
                </span>
              ))}
              <span className="px-2 py-0.5 rounded bg-emerald-500/10 text-emerald-300 border border-emerald-500/20 font-mono text-[10px]">
                {snapshot.toState}
              </span>
              {snapshot.actorId && (
                <span className="px-2 py-0.5 rounded bg-slate-800 text-slate-300 border border-slate-700 font-mono text-[10px]">
                  actor:{snapshot.actorId}
                </span>
              )}
            </div>
            <div className="text-[10px] text-slate-500 flex items-center gap-1">
              <Clock size={11} />
              {snapshot.fromStepId} → {snapshot.toStepId} · {snapshot.fromState} → {snapshot.toState}
            </div>
          </div>

          <div className="bg-slate-950/60 border border-slate-800 rounded-xl p-3">
            <div className="text-[10px] uppercase tracking-wider text-slate-500 font-semibold mb-2">Variable mutations</div>
            {diffs.length === 0 ? (
              <div className="text-[11px] text-slate-500 italic">No payload variables recorded at this milestone.</div>
            ) : (
              <div className="space-y-1 max-h-40 overflow-y-auto">
                {diffs.map((row) => (
                  <div
                    key={row.key}
                    className={`flex justify-between gap-3 text-[11px] font-mono px-2 py-1 rounded ${
                      row.changed ? 'bg-amber-500/10 text-amber-200' : 'text-slate-400'
                    }`}
                  >
                    <span>{row.key}</span>
                    <span className="truncate text-right">
                      {row.existed ? formatValue(row.before) : '∅'} → {row.exists ? formatValue(row.after) : '∅'}
                    </span>
                  </div>
                ))}
              </div>
            )}
          </div>

          <div className="bg-slate-950/60 border border-slate-800 rounded-xl p-3">
            <div className="text-[10px] uppercase tracking-wider text-slate-500 font-semibold mb-2 flex items-center gap-1">
              <Zap size={11} /> Actions at this milestone
            </div>
            {(snapshot.actionLogs || []).length === 0 ? (
              <div className="text-[11px] text-slate-500 italic">No correlated lifecycle actions.</div>
            ) : (
              <div className="space-y-1.5">
                {snapshot.actionLogs.map((log) => (
                  <div key={log.id} className="text-[11px] text-slate-300 border border-slate-800 rounded-lg px-2 py-1.5">
                    <span className="text-cyan-300">{log.triggerPhase}</span> {log.actionType}
                    {log.target ? ` → ${log.target}` : ''} · {log.status} · {log.durationMs}ms
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>

        <div className="space-y-3">
          {definition ? (
            <WorkflowGraphVisualizer
              definition={definition}
              currentStepId={snapshot.toStepId}
              currentState={snapshot.toState}
              completedSteps={completedSteps}
              initialView="flowchart"
            />
          ) : (
            <div className="py-10 text-center text-slate-500 text-xs italic bg-slate-950/40 rounded-xl border border-slate-800">
              Graph definition is resolving...
            </div>
          )}

          <div className="bg-slate-950/60 border border-cyan-500/20 rounded-xl p-3 space-y-2">
            <div className="flex items-center gap-2 text-cyan-300 text-xs font-semibold">
              <GitBranch size={14} />
              What-If branch fork
            </div>
            <p className="text-[10px] text-slate-500">
              Evaluates an alternative event from this snapshot in an isolated sandbox. Production outbox and audit history stay untouched.
            </p>
            <input
              value={altEvent}
              onChange={(event) => setAltEvent(event.target.value)}
              className="w-full bg-slate-900 border border-slate-700 rounded-lg px-2 py-1.5 text-xs text-white font-mono"
              placeholder="EVT-REJECT"
            />
            <textarea
              value={altPayload}
              onChange={(event) => setAltPayload(event.target.value)}
              rows={4}
              className="w-full bg-slate-900 border border-slate-700 rounded-lg px-2 py-1.5 text-[11px] text-emerald-300 font-mono"
            />
            <button
              onClick={handleFork}
              disabled={forking || !altEvent.trim()}
              className="px-3 py-1.5 bg-cyan-600 hover:bg-cyan-500 disabled:opacity-50 text-white rounded-lg text-xs font-semibold"
            >
              {forking ? 'Simulating...' : 'Test Alternative Branch'}
            </button>
            {forkError && (
              <div className="text-[11px] text-rose-300 flex items-center gap-1">
                <AlertCircle size={12} /> {forkError}
              </div>
            )}
            {forkResult && (
              <div className={`p-2 rounded-lg border text-[11px] ${
                forkResult.isAllowed
                  ? 'bg-emerald-500/10 border-emerald-500/30 text-emerald-200'
                  : 'bg-rose-500/10 border-rose-500/30 text-rose-200'
              }`}>
                <div className="flex items-center gap-1 font-semibold mb-1">
                  {forkResult.isAllowed ? <CheckCircle2 size={12} /> : <AlertCircle size={12} />}
                  {forkResult.baseStepId} → {forkResult.projectedStepId} ({forkResult.projectedState})
                </div>
                <div>{forkResult.reason}</div>
                {forkResult.projectedActions?.length > 0 && (
                  <ul className="mt-1 list-disc pl-4 text-slate-300">
                    {forkResult.projectedActions.map((action) => (
                      <li key={action}>{action}</li>
                    ))}
                  </ul>
                )}
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};
