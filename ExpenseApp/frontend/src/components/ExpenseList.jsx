import React, { useState } from 'react';
import axios from 'axios';
import { CheckCircle, XCircle, Clock, X, FileText } from 'lucide-react';

export default function ExpenseList({ expenses, onStatusUpdate, currentRole }) {
  const [selectedHistory, setSelectedHistory] = useState(null);
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [loadingHistory, setLoadingHistory] = useState(false);

  const handleAction = async (id, action) => {
    try {
      const apiUrl = import.meta.env.VITE_EXPENSE_APP_API_URL || 'http://localhost:3001';
      const res = await axios.post(`${apiUrl}/api/expenses/${id}/${action}`);
      onStatusUpdate(res.data);
    } catch (err) {
      console.error(err);
      alert(`Failed to ${action}: ` + (err.response?.data?.details?.error || err.response?.data?.error || err.message));
    }
  };

  const getStatusIcon = (status) => {
    switch (status) {
      case 'Approved': return <CheckCircle className="w-5 h-5 text-green-500" />;
      case 'Rejected': return <XCircle className="w-5 h-5 text-red-500" />;
      default: return <Clock className="w-5 h-5 text-yellow-500" />;
    }
  };

  const getStatusColor = (status) => {
    switch (status) {
      case 'Approved': return 'text-green-700 bg-green-50';
      case 'Rejected': return 'text-red-700 bg-red-50';
      default: return 'text-yellow-700 bg-yellow-50';
    }
  };

  const handleViewHistory = async (id) => {
    console.log('View History Clicked for ID:', id);
    setLoadingHistory(true);
    setIsModalOpen(true);
    setSelectedHistory(null);
    try {
      console.log('Fetching history from backend...');
      const apiUrl = import.meta.env.VITE_EXPENSE_APP_API_URL || 'http://localhost:3001';
      const res = await axios.get(`${apiUrl}/api/expenses/${id}/history`);
      console.log('History received:', res.data);
      setSelectedHistory(res.data);
    } catch (err) {
      console.error('Error fetching history:', err);
      alert('Failed to fetch history: ' + (err.response?.data?.error || err.message));
      setIsModalOpen(false);
    } finally {
      setLoadingHistory(false);
    }
  };

  const closeModal = () => {
    setIsModalOpen(false);
    setSelectedHistory(null);
  };

  return (
    <>
      <div className="bg-white rounded-lg shadow-md overflow-hidden">
        <div className="px-6 py-4 border-b border-gray-200">
          <h2 className="text-xl font-semibold text-gray-800">Recent Expenses</h2>
        </div>
        <div className="divide-y divide-gray-200">
          {expenses.length === 0 ? (
            <div className="p-6 text-center text-gray-500">No expenses found. Create one to get started.</div>
          ) : (
            expenses.map((expense) => (
              <div key={expense.id} className="p-6 flex flex-col sm:flex-row justify-between items-start sm:items-center gap-4">
                <div className="flex-1">
                  <div className="flex items-center gap-2 mb-1">
                    <span className="text-lg font-bold text-gray-900">${expense.amount.toFixed(2)}</span>
                    <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${getStatusColor(expense.status)}`}>
                      {getStatusIcon(expense.status)}
                      <span className="ml-1">{expense.status}</span>
                    </span>
                  </div>
                  <p className="text-gray-600 text-sm">{expense.description}</p>
                  <div className="flex items-center gap-2 mt-1">
                    <span className="text-xs text-gray-400">ID: {expense.workflow_instance_id}</span>
                    <span className="text-gray-300">|</span>
                    <button
                      onClick={() => handleViewHistory(expense.id)}
                      className="text-blue-500 hover:text-blue-700 hover:underline flex items-center gap-1 text-xs bg-transparent border-none cursor-pointer"
                    >
                      <FileText size={12} />
                      View History
                    </button>
                  </div>
                </div>

                {(
                  ((expense.status === 'PendingManager' || expense.status === 'Pending') && (currentRole === 'Manager' || currentRole === 'Admin')) ||
                  (expense.status === 'PendingDirector' && (currentRole === 'Director' || currentRole === 'Admin'))
                ) && (
                    <div className="flex gap-2 w-full sm:w-auto">
                      <button
                        onClick={() => handleAction(expense.id, 'approve')}
                        className="flex-1 sm:flex-none justify-center inline-flex items-center px-3 py-2 border border-transparent text-sm leading-4 font-medium rounded-md text-green-700 bg-green-100 hover:bg-green-200 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-green-500"
                      >
                        Approve
                      </button>
                      <button
                        onClick={() => handleAction(expense.id, 'reject')}
                        className="flex-1 sm:flex-none justify-center inline-flex items-center px-3 py-2 border border-transparent text-sm leading-4 font-medium rounded-md text-red-700 bg-red-100 hover:bg-red-200 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-red-500"
                      >
                        Reject
                      </button>
                    </div>
                  )}
              </div>
            ))
          )}
        </div>
      </div>

      {isModalOpen && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-4">
          <div className="bg-white rounded-lg w-full max-w-2xl max-h-[80vh] flex flex-col shadow-xl">
            <div className="flex justify-between items-center p-6 border-b border-gray-200">
              <h3 className="text-xl font-bold text-gray-800">Workflow History</h3>
              <button onClick={closeModal} className="text-gray-500 hover:text-gray-700 transition-colors">
                <X size={24} />
              </button>
            </div>

            <div className="p-6 overflow-y-auto flex-1">
              {loadingHistory ? (
                <div className="flex justify-center items-center h-40">
                  <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-500"></div>
                </div>
              ) : selectedHistory ? (
                <pre className="text-xs bg-gray-100 p-4 rounded overflow-auto h-full font-mono text-gray-700 whitespace-pre-wrap break-all">
                  {JSON.stringify(selectedHistory, null, 2)}
                </pre>
              ) : (
                <div className="text-center text-gray-500 py-8">
                  No history data available.
                </div>
              )}
            </div>

            <div className="p-4 border-t border-gray-200 bg-gray-50 rounded-b-lg">
              <div className="text-xs text-gray-400 text-right">
                Workflow ID: {selectedHistory?.id}
              </div>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
