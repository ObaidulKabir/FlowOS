import React from 'react';
import { WorkflowClass, ValidationResult, WorkflowClassScope, WorkflowClassStatus } from '../types';
import { X, CheckCircle, AlertTriangle } from 'lucide-react';

interface Props {
  item: WorkflowClass;
  validation?: ValidationResult | null;
  onClose: () => void;
  onValidate: () => void;
}

export const DetailView: React.FC<Props> = ({ item, validation, onClose, onValidate }) => {
  return (
    <div className="fixed inset-0 bg-gray-600 bg-opacity-50 overflow-y-auto h-full w-full flex justify-center items-center">
      <div className="bg-white p-8 rounded-lg shadow-xl w-3/4 max-h-[90vh] overflow-y-auto">
        <div className="flex justify-between items-center mb-6">
          <h2 className="text-2xl font-bold text-gray-800">{item.name} v{item.version}</h2>
          <button onClick={onClose} className="text-gray-500 hover:text-gray-700">
            <X size={24} />
          </button>
        </div>

        <div className="grid grid-cols-2 gap-6 mb-6">
          <div>
            <h3 className="text-lg font-semibold mb-2">Metadata</h3>
            <p><strong>Scope:</strong> {WorkflowClassScope[item.scope]}</p>
            <p><strong>Status:</strong> {WorkflowClassStatus[item.status]}</p>
            <p><strong>Created:</strong> {new Date(item.createdAt).toLocaleString()}</p>
            {item.publishedAt && <p><strong>Published:</strong> {new Date(item.publishedAt).toLocaleString()}</p>}
          </div>
          <div>
            <div className="flex justify-between items-center mb-2">
                <h3 className="text-lg font-semibold">Validation</h3>
                {item.status === WorkflowClassStatus.Draft && (
                    <button onClick={onValidate} className="px-3 py-1 bg-blue-500 text-white rounded hover:bg-blue-600 text-sm">
                        Run Validation
                    </button>
                )}
            </div>
            
            {validation ? (
                validation.isValid ? (
                    <div className="flex items-center text-green-600 bg-green-50 p-2 rounded">
                        <CheckCircle className="mr-2" size={20} />
                        <span>Valid</span>
                    </div>
                ) : (
                    <div className="bg-red-50 p-3 rounded">
                        <div className="flex items-center text-red-600 mb-2">
                            <AlertTriangle className="mr-2" size={20} />
                            <span className="font-bold">Validation Failed</span>
                        </div>
                        <ul className="list-disc list-inside text-sm text-red-700">
                            {validation.errors.map((err, idx) => (
                                <li key={idx}>[{err.category}] {err.message} (in {err.element})</li>
                            ))}
                        </ul>
                    </div>
                )
            ) : (
                <p className="text-gray-500 italic">No validation result available.</p>
            )}
          </div>
        </div>

        <div className="mb-6">
            <h3 className="text-lg font-semibold mb-2">Definition</h3>
            <pre className="bg-gray-100 p-4 rounded overflow-x-auto text-xs">
                {JSON.stringify(item.definition, null, 2)}
            </pre>
        </div>
      </div>
    </div>
  );
};
