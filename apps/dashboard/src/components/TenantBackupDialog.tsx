import React, { useState } from 'react';
import { createPortal } from 'react-dom';
import { Download, Upload, ShieldAlert, X } from 'lucide-react';
import { api } from '../api/client';

interface Props {
  tenantId: string;
  tenantName: string;
  onClose: () => void;
  onRestored: () => Promise<void> | void;
}

type BackupPreview = {
  format: string;
  flowOsVersion: string;
  takenAtUtc: string;
  sourceTenantId: string;
  sourceTenantName: string;
  workflowClasses: number;
  contextBindings: number;
  pluginBindings: number;
  workflowInstances: number;
  events: number;
};

const countOf = (value: unknown) => Array.isArray(value) ? value.length : 0;

const readPreview = (json: string): BackupPreview => {
  const parsed = JSON.parse(json) as Record<string, unknown>;
  if (parsed.format !== 'flowos-tenant-backup' || parsed.formatVersion !== 1) {
    throw new Error('This file is not a FlowOS tenant backup.');
  }
  return {
    format: String(parsed.format),
    flowOsVersion: String(parsed.flowOsVersion ?? ''),
    takenAtUtc: String(parsed.takenAtUtc ?? ''),
    sourceTenantId: String(parsed.sourceTenantId ?? ''),
    sourceTenantName: String(parsed.sourceTenantName ?? ''),
    workflowClasses: countOf(parsed.workflowClasses),
    contextBindings: countOf(parsed.contextBindings),
    pluginBindings: countOf(parsed.pluginBindings),
    workflowInstances: countOf(parsed.workflowInstances),
    events: countOf(parsed.events)
  };
};

export const TenantBackupDialog: React.FC<Props> = ({ tenantId, tenantName, onClose, onRestored }) => {
  const [mode, setMode] = useState<'download' | 'restore'>('download');
  const [acknowledged, setAcknowledged] = useState(false);
  const [phrase, setPhrase] = useState('');
  const [fileName, setFileName] = useState('');
  const [fileJson, setFileJson] = useState('');
  const [preview, setPreview] = useState<BackupPreview | null>(null);
  const [fileError, setFileError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [failure, setFailure] = useState<string | null>(null);

  const canRestore = acknowledged && phrase === 'RESTORE' && !!preview && !busy;

  const chooseFile = async (file: File | undefined) => {
    setNotice(null);
    setFailure(null);
    setPreview(null);
    setFileJson('');
    setFileName('');
    setPhrase('');
    setAcknowledged(false);
    if (!file) return;
    setFileName(file.name);
    try {
      const json = await file.text();
      setPreview(readPreview(json));
      setFileJson(json);
      setFileError(null);
    } catch (err: any) {
      setFileError(err.message || 'Could not read that backup file.');
    }
  };

  const download = async () => {
    if (!acknowledged) {
      setFailure('Check the confidential-file box before downloading.');
      return;
    }
    setBusy(true);
    setFailure(null);
    setNotice(null);
    try {
      await api.downloadTenantBackup(tenantId);
      setNotice('Download started. Check your browser downloads folder. Store the file outside git and outside chat. It contains this tenant’s AI provider keys.');
    } catch (err: any) {
      setFailure(err.message || 'Failed to download backup');
    } finally {
      setBusy(false);
    }
  };

  const restore = async () => {
    if (!canRestore) {
      setFailure('Choose a backup file, confirm the checkbox, and type RESTORE.');
      return;
    }
    setBusy(true);
    setFailure(null);
    setNotice(null);
    try {
      const result = await api.restoreTenantBackup(tenantId, fileJson);
      setNotice(`Restored ${result.workflowClasses} workflows and ${result.workflowInstances} instances from FlowOS ${result.flowOsVersion} into ${tenantName}.`);
      setFileJson('');
      setPreview(null);
      setPhrase('');
      setAcknowledged(false);
      await onRestored();
    } catch (err: any) {
      const message = err.message || 'Failed to restore backup';
      setFailure(
        /BACKUP-CONFLICT|already exists/.test(message)
          ? `${message} Restore is for a different FlowOS site (or an empty tenant), not a round-trip on this same tenant.`
          : message
      );
    } finally {
      setBusy(false);
    }
  };

  return createPortal(
    <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-[100] flex items-center justify-center p-4">
      <div className="bg-slate-900 border border-slate-700 rounded-2xl max-w-lg w-full p-6 shadow-2xl space-y-4 max-h-[90vh] overflow-y-auto">
        <div className="flex items-center justify-between border-b border-slate-800 pb-3">
          <h3 className="text-base font-bold text-white flex items-center gap-2">
            <ShieldAlert className="text-amber-300" size={18} />
            Site backup
          </h3>
          <button type="button" onClick={onClose} className="text-slate-400 hover:text-white" aria-label="Close site backup">
            <X size={16} />
          </button>
        </div>

        <p className="text-xs text-slate-400 leading-relaxed">
          Point-in-time copy of <strong className="text-slate-200">{tenantName}</strong> for another FlowOS site.
          Designed Apps, running instances, and the event log move with the file. API keys, passwords, and the platform hosted model key stay here.
        </p>

        <div className="grid grid-cols-2 gap-2">
          <button
            type="button"
            onClick={() => { setMode('download'); setAcknowledged(false); setPhrase(''); setFailure(null); setNotice(null); }}
            className={`px-3 py-2 rounded-xl text-xs font-semibold border ${mode === 'download' ? 'bg-emerald-900/40 border-emerald-500 text-emerald-200' : 'bg-slate-950 border-slate-800 text-slate-400'}`}
          >
            Download
          </button>
          <button
            type="button"
            onClick={() => { setMode('restore'); setAcknowledged(false); setPhrase(''); setFailure(null); setNotice(null); }}
            className={`px-3 py-2 rounded-xl text-xs font-semibold border ${mode === 'restore' ? 'bg-sky-900/40 border-sky-500 text-sky-200' : 'bg-slate-950 border-slate-800 text-slate-400'}`}
          >
            Restore
          </button>
        </div>

        {mode === 'download' ? (
          <div className="space-y-3 text-xs">
            <div className="p-3 bg-amber-950/40 border border-amber-700/50 rounded-xl text-amber-100 leading-relaxed">
              This file includes the tenant’s own AI provider keys. Treat it as a secret. Pending timers are saved but will not fire again after restore.
            </div>
            <label className="flex items-start gap-2 text-slate-300">
              <input
                type="checkbox"
                className="mt-0.5"
                checked={acknowledged}
                onChange={event => setAcknowledged(event.target.checked)}
              />
              <span>I understand this download contains confidential AI keys and I will not paste it into chat or commit it.</span>
            </label>
            <button
              type="button"
              onClick={() => void download()}
              disabled={busy}
              className="px-4 py-2 bg-emerald-600 hover:bg-emerald-500 disabled:opacity-40 text-white rounded-xl font-semibold flex items-center gap-1.5"
            >
              <Download size={14} />
              {busy ? 'Preparing…' : 'Download backup'}
            </button>
            {!acknowledged && (
              <p className="text-slate-500">Check the box above to confirm, then download.</p>
            )}
          </div>
        ) : (
          <div className="space-y-3 text-xs">
            <div className="p-3 bg-slate-950 border border-slate-800 rounded-xl text-slate-400 leading-relaxed">
              Restore writes this file into <strong className="text-slate-200">{tenantName}</strong> on this site.
              Record ids are kept. If this tenant already has those workflows, restore is refused — use an empty tenant on another FlowOS host.
            </div>
            <label className="block text-slate-300 font-semibold">
              Backup file
              <input
                type="file"
                accept="application/json,.json"
                className="mt-1 block w-full text-slate-400"
                onChange={event => {
                  const file = event.target.files?.[0];
                  event.target.value = '';
                  void chooseFile(file);
                }}
              />
            </label>
            {fileName && <div className="text-slate-500">{fileName}</div>}
            {fileError && <div className="text-rose-300">{fileError}</div>}
            {preview && (
              <div className="p-3 bg-slate-950 border border-slate-800 rounded-xl text-slate-300 space-y-1">
                <div>Source: {preview.sourceTenantName || 'unknown tenant'} <span className="font-mono text-slate-500">{preview.sourceTenantId}</span></div>
                <div>Taken: {preview.takenAtUtc || 'unknown time'} · FlowOS {preview.flowOsVersion || 'unknown'}</div>
                <div>{preview.workflowClasses} workflows · {preview.contextBindings} business contexts · {preview.pluginBindings} AI bindings · {preview.workflowInstances} instances · {preview.events} events</div>
                <div className="text-amber-200 pt-1">Destination is {tenantName}. Record ids are kept. This does not roll this site back. Timers will not fire again.</div>
              </div>
            )}
            <label className="flex items-start gap-2 text-slate-300">
              <input
                type="checkbox"
                className="mt-0.5"
                checked={acknowledged}
                onChange={event => setAcknowledged(event.target.checked)}
                disabled={!preview}
              />
              <span>I reviewed this file and accept writing its Designed Apps, instances, and AI keys into {tenantName}.</span>
            </label>
            <label className="block text-slate-300">
              Type RESTORE to confirm
              <input
                value={phrase}
                onChange={event => setPhrase(event.target.value)}
                disabled={!preview}
                className="mt-1 w-full bg-slate-950 border border-slate-700 rounded-xl px-3 py-2 text-white font-mono focus:outline-none focus:border-sky-500"
                autoComplete="off"
              />
            </label>
            <button
              type="button"
              onClick={() => void restore()}
              disabled={busy}
              className="px-4 py-2 bg-sky-600 hover:bg-sky-500 disabled:opacity-40 text-white rounded-xl font-semibold flex items-center gap-1.5"
            >
              <Upload size={14} />
              {busy ? 'Restoring…' : 'Restore into this tenant'}
            </button>
          </div>
        )}

        {notice && <div className="p-3 bg-emerald-950/40 border border-emerald-700/40 rounded-xl text-xs text-emerald-200">{notice}</div>}
        {failure && <div className="p-3 bg-rose-950/40 border border-rose-700/40 rounded-xl text-xs text-rose-200">{failure}</div>}
      </div>
    </div>,
    document.body
  );
};
