import React from 'react';
import { WorkflowInstance } from '../types';

interface Props {
  items: WorkflowInstance[];
}

export const WorkflowInstanceTable: React.FC<Props> = ({ items }) => {
  return (
    <div className="overflow-x-auto">
      <table className="min-w-full bg-white border border-gray-200">
        <thead className="bg-gray-50">
          <tr>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Instance ID</th>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Workflow Class</th>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Current Step</th>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Legal State</th>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Status</th>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Created</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-gray-200">
          {items.map((item, idx) => {
            const id = item.id || item.workflowId || `instance-${idx}`;
            const className = item.workflowClassName || item.workflowClassId || 'Workflow';
            const step = item.currentStepId || item.currentStep || 'Start';
            const state = item.currentState || 'Draft';
            const status = (item.status === 0 || item.status === 'Running') ? 'Running' :
                           (item.status === 1 || item.status === 'Completed') ? 'Completed' : 'Terminated';

            return (
              <tr key={id}>
                <td className="px-6 py-4 whitespace-nowrap text-sm font-mono text-blue-600">{id.substring(0, 8)}...</td>
                <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">{className}</td>
                <td className="px-6 py-4 whitespace-nowrap text-sm text-blue-600 font-semibold">{step}</td>
                <td className="px-6 py-4 whitespace-nowrap text-sm text-green-600 font-semibold">{state}</td>
                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                  <span className={`px-2 py-0.5 inline-flex text-xs leading-5 font-semibold rounded-full 
                      ${status === 'Completed' ? 'bg-green-100 text-green-800' : 
                        status === 'Terminated' ? 'bg-red-100 text-red-800' : 
                        'bg-blue-100 text-blue-800'}`}>
                      {status}
                  </span>
                </td>
                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">{new Date(item.createdAt).toLocaleString()}</td>
              </tr>
            );
          })}
          {items.length === 0 && (
              <tr>
                  <td colSpan={6} className="px-6 py-4 text-center text-sm text-gray-500 italic">No running workflows found for this tenant.</td>
              </tr>
          )}
        </tbody>
      </table>
    </div>
  );
};
