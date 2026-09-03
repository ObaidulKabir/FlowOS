import React from 'react';
import { WorkflowClass, WorkflowClassScope, WorkflowClassStatus } from '../types';
import { Eye, Trash2, CheckCircle, Copy, Archive, Send, RotateCcw, Edit, AlertOctagon, XCircle, FileCode } from 'lucide-react';

interface Props {
  items: WorkflowClass[];
  onView: (id: string) => void;
  onEdit?: (id: string) => void;
  onDelete?: (id: string) => void;
  onPublish?: (id: string) => void;
  onSubmit?: (id: string) => void;
  onWithdraw?: (id: string) => void;
  onApprove?: (id: string) => void; // Admin
  onDeprecate?: (id: string) => void;
  onCopy?: (id: string) => void;
  onAbandon?: (id: string) => void;
  onNewVersion?: (id: string) => void;
  currentTab: string;
  isAdmin?: boolean;
}

export const WorkflowTable: React.FC<Props> = ({ 
  items, onView, onEdit, onDelete, onPublish, onSubmit, onWithdraw, onApprove, onDeprecate, onCopy, onAbandon, onNewVersion, currentTab, isAdmin 
}) => {
  const getStatusBadge = (status: WorkflowClassStatus) => {
    switch (status) {
      case WorkflowClassStatus.Published:
        return (
          <span className="px-2 py-0.5 rounded-full text-[11px] font-semibold bg-emerald-500/20 text-emerald-300 border border-emerald-500/30 inline-flex items-center gap-1">
            <CheckCircle size={12} /> Published
          </span>
        );
      case WorkflowClassStatus.Draft:
        return (
          <span className="px-2 py-0.5 rounded-full text-[11px] font-semibold bg-amber-500/20 text-amber-300 border border-amber-500/30 inline-flex items-center gap-1">
            Draft
          </span>
        );
      case WorkflowClassStatus.Deprecated:
        return (
          <span className="px-2 py-0.5 rounded-full text-[11px] font-semibold bg-orange-500/20 text-orange-300 border border-orange-500/30 inline-flex items-center gap-1">
            <Archive size={12} /> Deprecated
          </span>
        );
      case WorkflowClassStatus.Abandoned:
        return (
          <span className="px-2 py-0.5 rounded-full text-[11px] font-semibold bg-rose-500/20 text-rose-300 border border-rose-500/30 inline-flex items-center gap-1">
            <AlertOctagon size={12} /> Abandoned
          </span>
        );
      default:
        return <span className="text-slate-400 text-xs">{WorkflowClassStatus[status]}</span>;
    }
  };

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-left text-xs text-slate-300">
        <thead className="bg-slate-800/70 text-[11px] text-slate-400 uppercase tracking-wider">
          <tr>
            <th className="py-3 px-4 rounded-l-lg">WorkflowClass Name</th>
            <th className="py-3 px-4">Version</th>
            <th className="py-3 px-4">Scope</th>
            <th className="py-3 px-4">Status</th>
            <th className="py-3 px-4 text-right rounded-r-lg">Actions</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-800">
          {items.length === 0 ? (
            <tr>
              <td colSpan={5} className="py-16 text-center text-slate-400">
                <FileCode size={32} className="mx-auto mb-2 text-slate-600 opacity-60" />
                <div className="font-semibold text-slate-300">No {currentTab === 'All' ? '' : currentTab} WorkflowClasses found.</div>
                {currentTab === 'Drafts' && (
                  <p className="text-xs text-slate-500 mt-1">Click &quot;+ New WorkflowClass&quot; above to create a draft, or check the &quot;Published&quot; tab.</p>
                )}
                {currentTab === 'Published' && (
                  <p className="text-xs text-slate-500 mt-1">Workflow classes published from &quot;Drafts&quot; will appear here.</p>
                )}
                {currentTab === 'All' && (
                  <p className="text-xs text-slate-500 mt-1">Click &quot;+ New WorkflowClass&quot; above to create your first workflow blueprint.</p>
                )}
              </td>
            </tr>
          ) : (
            items.map((item) => (
              <tr key={item.id} className="hover:bg-slate-800/40 transition-colors">
                <td className="py-3 px-4 font-semibold text-white">
                  <span>{item.name}</span>
                </td>
                <td className="py-3 px-4 font-mono text-slate-400">{item.version}</td>
                <td className="py-3 px-4">
                  <span className="px-2 py-0.5 rounded bg-slate-800 text-slate-300 border border-slate-700 text-[11px]">
                    {WorkflowClassScope[item.scope]}
                  </span>
                </td>
                <td className="py-3 px-4">
                  {getStatusBadge(item.status)}
                </td>
                <td className="py-3 px-4 text-right space-x-1.5 whitespace-nowrap">
                  <button 
                    onClick={() => onView(item.id)} 
                    className="p-1.5 text-blue-400 hover:text-white hover:bg-blue-600/20 rounded transition-colors" 
                    title="View Details"
                  >
                    <Eye size={15} />
                  </button>

                  {/* Contextual Actions based on Tab/Status */}
                  {currentTab === 'Drafts' && (
                    <>
                      {onEdit && (
                        <button 
                          onClick={() => onEdit(item.id)} 
                          className="p-1.5 text-slate-300 hover:text-white hover:bg-slate-700 rounded transition-colors" 
                          title="Edit Blueprint"
                        >
                          <Edit size={15} />
                        </button>
                      )}
                      <button 
                        onClick={() => onPublish?.(item.id)} 
                        className="p-1.5 text-emerald-400 hover:text-white hover:bg-emerald-600/20 rounded transition-colors" 
                        title="Publish to Engine"
                      >
                        <CheckCircle size={15} />
                      </button>
                      <button 
                        onClick={() => onDelete?.(item.id)} 
                        className="p-1.5 text-rose-400 hover:text-white hover:bg-rose-600/20 rounded transition-colors" 
                        title="Delete Draft"
                      >
                        <Trash2 size={15} />
                      </button>
                    </>
                  )}

                  {currentTab === 'Published' && (
                    <>
                      <button 
                        onClick={() => onSubmit?.(item.id)} 
                        className="p-1.5 text-purple-400 hover:text-white hover:bg-purple-600/20 rounded transition-colors" 
                        title="Submit for Review"
                      >
                        <Send size={15} />
                      </button>
                      <button 
                        onClick={() => onNewVersion?.(item.id)} 
                        className="p-1.5 text-teal-400 hover:text-white hover:bg-teal-600/20 rounded transition-colors inline-flex items-center gap-0.5" 
                        title="Create New Draft Version"
                      >
                        <Copy size={14} />
                        <span className="text-[10px] font-bold">+</span>
                      </button>
                      <button 
                        onClick={() => onCopy?.(item.id)} 
                        className="p-1.5 text-blue-400 hover:text-white hover:bg-blue-600/20 rounded transition-colors" 
                        title="Fork Blueprint"
                      >
                        <Copy size={15} />
                      </button>
                      <button 
                        onClick={() => onDeprecate?.(item.id)} 
                        className="p-1.5 text-amber-400 hover:text-white hover:bg-amber-600/20 rounded transition-colors" 
                        title="Deprecate Version"
                      >
                        <Archive size={15} />
                      </button>
                      <button 
                        onClick={() => onAbandon?.(item.id)} 
                        className="p-1.5 text-rose-400 hover:text-white hover:bg-rose-600/20 rounded transition-colors" 
                        title="Abandon"
                      >
                        <Trash2 size={15} />
                      </button>
                    </>
                  )}

                  {currentTab === 'Shared' && (
                    isAdmin ? (
                      <>
                        <button onClick={() => onApprove?.(item.id)} className="p-1.5 text-emerald-400 hover:bg-emerald-600/20 rounded" title="Approve (Make Public)">
                          <CheckCircle size={15} />
                        </button>
                        <button onClick={() => onWithdraw?.(item.id)} className="p-1.5 text-rose-400 hover:bg-rose-600/20 rounded" title="Reject (Return to Private)">
                          <XCircle size={15} />
                        </button>
                      </>
                    ) : (
                      <button onClick={() => onWithdraw?.(item.id)} className="p-1.5 text-amber-400 hover:bg-amber-600/20 rounded" title="Withdraw">
                        <RotateCcw size={15} />
                      </button>
                    )
                  )}

                  {currentTab === 'Public' && (
                    <button onClick={() => onCopy?.(item.id)} className="p-1.5 text-indigo-400 hover:text-white hover:bg-indigo-600/20 rounded" title="Copy to Tenant">
                      <Copy size={15} />
                    </button>
                  )}
                </td>
              </tr>
            ))
          )}
        </tbody>
      </table>
    </div>
  );
};
