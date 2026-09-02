import React from 'react';
import { WorkflowClass, WorkflowClassScope, WorkflowClassStatus } from '../types';
import { Eye, Trash2, CheckCircle, Copy, Archive, Send, RotateCcw, Edit, AlertOctagon, XCircle } from 'lucide-react';

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
          case WorkflowClassStatus.Deprecated:
              return (
                  <span className="flex items-center text-orange-600">
                      <Archive size={16} className="mr-1" />
                      Deprecated
                  </span>
              );
          case WorkflowClassStatus.Abandoned:
              return (
                  <span className="flex items-center text-red-600">
                      <AlertOctagon size={16} className="mr-1" />
                      Abandoned
                  </span>
              );
          default:
              return WorkflowClassStatus[status];
      }
  };

  return (
    <div className="overflow-x-auto">
      <table className="min-w-full bg-white border border-gray-200">
        <thead className="bg-gray-50">
          <tr>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Name</th>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Version</th>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Scope</th>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Status</th>
            <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">Actions</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-gray-200">
          {items.length === 0 ? (
            <tr>
              <td colSpan={5} className="px-6 py-12 text-center text-slate-500 text-sm">
                No <strong>{currentTab}</strong> workflow classes found.
                {currentTab === 'Drafts' && (
                  <p className="text-xs text-slate-400 mt-1">Click &quot;+ New WorkflowClass&quot; above to create a draft, or check the &quot;Published&quot; tab.</p>
                )}
                {currentTab === 'Published' && (
                  <p className="text-xs text-slate-400 mt-1">Workflow classes published from &quot;Drafts&quot; will appear here.</p>
                )}
              </td>
            </tr>
          ) : (
            items.map((item) => (
              <tr key={item.id}>
              <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">{item.name}</td>
              <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">{item.version}</td>
              <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">{WorkflowClassScope[item.scope]}</td>
              <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                  {getStatusBadge(item.status)}
              </td>
              <td className="px-6 py-4 whitespace-nowrap text-right text-sm font-medium space-x-2">
                
                <button onClick={() => onView(item.id)} className="text-blue-600 hover:text-blue-900" title="View">
                  <Eye size={18} />
                </button>

                {/* Contextual Actions based on Tab/Status */}
                
                {currentTab === 'Drafts' && (
                    <>
                        {onEdit && (
                            <button onClick={() => onEdit(item.id)} className="text-gray-600 hover:text-gray-900" title="Edit">
                                <Edit size={18} />
                            </button>
                        )}
                        <button onClick={() => onPublish?.(item.id)} className="text-green-600 hover:text-green-900" title="Publish">
                            <CheckCircle size={18} />
                        </button>
                        <button onClick={() => onDelete?.(item.id)} className="text-red-600 hover:text-red-900" title="Delete">
                            <Trash2 size={18} />
                        </button>
                    </>
                )}

                {currentTab === 'Published' && (
                    <>
                        <button onClick={() => onSubmit?.(item.id)} className="text-purple-600 hover:text-purple-900" title="Submit for Review">
                            <Send size={18} />
                        </button>
                        <button onClick={() => onDeprecate?.(item.id)} className="text-orange-600 hover:text-orange-900" title="Deprecate">
                            <Archive size={18} />
                        </button>
                        <button onClick={() => onAbandon?.(item.id)} className="text-gray-500 hover:text-red-900" title="Abandon">
                            <Trash2 size={18} />
                        </button>
                        <button onClick={() => onCopy?.(item.id)} className="text-blue-600 hover:text-blue-900" title="Copy to Draft">
                            <Copy size={18} />
                        </button>
                        <button onClick={() => onNewVersion?.(item.id)} className="text-teal-600 hover:text-teal-900" title="Create New Draft Version">
                            <span className="flex items-center">
                                <Copy size={18} className="mr-1"/>
                                <span className="text-xs font-bold">+</span>
                            </span>
                        </button>
                    </>
                )}

                {currentTab === 'Shared' && (
                    isAdmin ? (
                        <>
                            <button onClick={() => onApprove?.(item.id)} className="text-green-600 hover:text-green-900" title="Approve (Make Public)">
                                <CheckCircle size={18} />
                            </button>
                            <button onClick={() => onWithdraw?.(item.id)} className="text-red-600 hover:text-red-900" title="Reject (Return to Private)">
                                <XCircle size={18} />
                            </button>
                        </>
                    ) : (
                        <button onClick={() => onWithdraw?.(item.id)} className="text-yellow-600 hover:text-yellow-900" title="Withdraw">
                            <RotateCcw size={18} />
                        </button>
                    )
                )}

                {currentTab === 'Public' && (
                    <button onClick={() => onCopy?.(item.id)} className="text-indigo-600 hover:text-indigo-900" title="Copy to Tenant">
                        <Copy size={18} />
                    </button>
                )}

              </td>
            </tr>
          )))}
        </tbody>
      </table>
    </div>
  );
};
