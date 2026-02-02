import React from 'react';
import { WorkflowInstance } from '../types';
import { PlayCircle, CheckCircle, Clock } from 'lucide-react';

interface Props {
  items: WorkflowInstance[];
}

export const WorkflowInstanceTable: React.FC<Props> = ({ items }) => {
  return (
    <div className="overflow-x-auto">
      <table className="min-w-full bg-white border border-gray-200">
        <thead className="bg-gray-50">
          <tr>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Workflow Class</th>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Correlation ID</th>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Current Step</th>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Status</th>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Created</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-gray-200">
          {items.map((item) => (
            <tr key={item.workflowId}>
              <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">{item.workflowClassName}</td>
              <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500 font-mono">{item.correlationId}</td>
              <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">{item.currentStep}</td>
              <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                <span className={`px-2 inline-flex text-xs leading-5 font-semibold rounded-full 
                    ${item.status === 'Completed' ? 'bg-green-100 text-green-800' : 
                      item.status === 'Failed' ? 'bg-red-100 text-red-800' : 
                      'bg-blue-100 text-blue-800'}`}>
                    {item.status}
                </span>
              </td>
              <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">{new Date(item.createdAt).toLocaleString()}</td>
            </tr>
          ))}
          {items.length === 0 && (
              <tr>
                  <td colSpan={5} className="px-6 py-4 text-center text-sm text-gray-500 italic">No running workflows found.</td>
              </tr>
          )}
        </tbody>
      </table>
    </div>
  );
};
