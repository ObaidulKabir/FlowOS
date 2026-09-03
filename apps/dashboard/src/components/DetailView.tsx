import React from 'react';
import { WorkflowClass, ValidationResult, WorkflowClassScope, WorkflowClassStatus } from '../types';
import { X, CheckCircle, AlertTriangle, ShieldCheck, FileCode, Tag } from 'lucide-react';

interface Props {
  item: WorkflowClass;
  validation?: ValidationResult | null;
  onClose: () => void;
  onValidate: () => void;
}

export const DetailView: React.FC<Props> = ({ item, validation, onClose, onValidate }) => {
  return (
    <div className="fixed inset-0 bg-black/75 backdrop-blur-sm overflow-y-auto h-full w-full flex justify-center items-center z-50 p-4">
      <div className="bg-slate-900 border border-slate-700 p-6 md:p-8 rounded-2xl shadow-2xl max-w-4xl w-full max-h-[90vh] overflow-y-auto space-y-6">
        
        {/* Header */}
        <div className="flex justify-between items-start border-b border-slate-800 pb-4">
          <div>
            <div className="flex items-center gap-2">
              <FileCode className="text-blue-400" size={24} />
              <h2 className="text-xl font-bold text-white">{item.name}</h2>
              <span className="px-2 py-0.5 rounded bg-slate-800 text-slate-300 font-mono text-xs border border-slate-700">
                v{item.version}
              </span>
            </div>
            <p className="text-xs text-slate-400 mt-1">UUID: <span className="font-mono text-slate-500">{item.id}</span></p>
          </div>
          <button onClick={onClose} className="text-slate-400 hover:text-white p-1 rounded transition-colors">
            <X size={20} />
          </button>
        </div>

        {/* Metadata & Governance */}
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          <div className="bg-slate-800/60 border border-slate-700/60 p-4 rounded-xl space-y-2 text-xs">
            <h3 className="text-sm font-semibold text-white flex items-center gap-1.5 mb-3">
              <Tag size={15} className="text-blue-400" /> Blueprint Governance
            </h3>
            <div className="flex justify-between py-1 border-b border-slate-800">
              <span className="text-slate-400">Access Scope</span>
              <span className="text-white font-medium">{WorkflowClassScope[item.scope]}</span>
            </div>
            <div className="flex justify-between py-1 border-b border-slate-800">
              <span className="text-slate-400">Lifecycle Status</span>
              <span className="text-white font-medium">{WorkflowClassStatus[item.status]}</span>
            </div>
            <div className="flex justify-between py-1 border-b border-slate-800">
              <span className="text-slate-400">Created At</span>
              <span className="text-slate-300">{new Date(item.createdAt).toLocaleString()}</span>
            </div>
            {item.publishedAt && (
              <div className="flex justify-between py-1">
                <span className="text-slate-400">Published At</span>
                <span className="text-emerald-300 font-medium">{new Date(item.publishedAt).toLocaleString()}</span>
              </div>
            )}
          </div>

          <div className="bg-slate-800/60 border border-slate-700/60 p-4 rounded-xl space-y-3 text-xs flex flex-col justify-between">
            <div>
              <div className="flex justify-between items-center mb-2">
                <h3 className="text-sm font-semibold text-white flex items-center gap-1.5">
                  <ShieldCheck size={15} className="text-emerald-400" /> Blueprint Validation
                </h3>
                {item.status === WorkflowClassStatus.Draft && (
                  <button 
                    onClick={onValidate} 
                    className="px-2.5 py-1 bg-blue-600 hover:bg-blue-500 text-white rounded text-xs font-semibold transition-colors"
                  >
                    Run Validation
                  </button>
                )}
              </div>
              
              {validation ? (
                validation.isValid ? (
                  <div className="flex items-center text-emerald-300 bg-emerald-500/10 border border-emerald-500/30 p-3 rounded-lg text-xs gap-2">
                    <CheckCircle size={16} className="text-emerald-400 shrink-0" />
                    <span>Authoritative blueprint validation passed without errors.</span>
                  </div>
                ) : (
                  <div className="bg-rose-500/10 border border-rose-500/30 p-3 rounded-lg space-y-2">
                    <div className="flex items-center text-rose-300 font-bold gap-1.5">
                      <AlertTriangle size={16} className="text-rose-400 shrink-0" />
                      <span>Validation Failed ({validation.errors.length} violations)</span>
                    </div>
                    <ul className="list-disc list-inside text-[11px] text-rose-300 space-y-1">
                      {validation.errors.map((err, idx) => (
                        <li key={idx}>[{err.category}] {err.message} (in {err.element})</li>
                      ))}
                    </ul>
                  </div>
                )
              ) : (
                <p className="text-slate-500 italic">No validation result available yet.</p>
              )}
            </div>

            <div className="text-[11px] text-slate-400 pt-2 border-t border-slate-800">
              Ensures decoupled state machine transitions and command steps are mathematically sound.
            </div>
          </div>
        </div>

        {/* JSON Blueprint Definition */}
        <div className="space-y-2">
          <div className="flex items-center justify-between">
            <h3 className="text-sm font-semibold text-slate-200">Raw JSON Blueprint</h3>
            <span className="text-[10px] text-slate-500 font-mono">Declarative DSL</span>
          </div>
          <pre className="bg-slate-950 border border-slate-800 p-4 rounded-xl overflow-x-auto text-xs font-mono text-blue-300/90 leading-relaxed max-h-72">
            {JSON.stringify(item.definition, null, 2)}
          </pre>
        </div>

        {/* Footer */}
        <div className="pt-2 border-t border-slate-800 flex justify-end">
          <button
            onClick={onClose}
            className="px-4 py-1.5 bg-slate-800 hover:bg-slate-700 text-white rounded-lg text-xs font-semibold transition-colors"
          >
            Close
          </button>
        </div>
      </div>
    </div>
  );
};
