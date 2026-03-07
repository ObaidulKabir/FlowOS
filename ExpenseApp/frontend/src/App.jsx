import React, { useState, useEffect } from 'react';
import axios from 'axios';
import ExpenseForm from './components/ExpenseForm';
import ExpenseList from './components/ExpenseList';

function App() {
  const [expenses, setExpenses] = useState([]);
  const [loading, setLoading] = useState(true);
  const [currentRole, setCurrentRole] = useState('Admin'); // Default role

  // Setup Axios Interceptor for Role Header
  useEffect(() => {
    const interceptor = axios.interceptors.request.use(config => {
      config.headers['X-Mock-Role'] = currentRole;
      return config;
    });
    return () => axios.interceptors.request.eject(interceptor);
  }, [currentRole]);

  const fetchExpenses = async () => {
    try {
      const res = await axios.get('http://localhost:3001/api/expenses');
      setExpenses(res.data);
    } catch (err) {
      console.error(err);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchExpenses();
  }, []);

  const handleExpenseAdded = (newExpense) => {
    setExpenses([newExpense, ...expenses]);
  };

  const handleStatusUpdate = (updatedExpense) => {
      setExpenses(expenses.map(e => e.id === updatedExpense.id ? updatedExpense : e));
  };

  return (
    <div className="min-h-screen bg-gray-100 p-8">
      <div className="max-w-4xl mx-auto">
        <div className="flex justify-between items-center mb-8">
          <h1 className="text-3xl font-bold text-gray-800">FlowOS Expense Approval</h1>
          
          <div className="flex items-center gap-2 bg-white p-2 rounded shadow-sm">
            <span className="text-sm text-gray-500 font-medium">Simulate Role:</span>
            <select 
              value={currentRole}
              onChange={(e) => setCurrentRole(e.target.value)}
              className="border-gray-300 rounded-md text-sm focus:ring-blue-500 focus:border-blue-500 p-1 bg-gray-50"
            >
              <option value="Admin">Admin (Full Access)</option>
              <option value="Employee">Employee (Submit Only)</option>
              <option value="Manager">Manager (Approve &lt; $100)</option>
              <option value="Director">Director (Approve &gt; $100)</option>
            </select>
          </div>
        </div>
        
        <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
          <div className="md:col-span-1">
            <ExpenseForm onExpenseAdded={handleExpenseAdded} />
          </div>
          
          <div className="md:col-span-2">
            {loading ? (
              <p>Loading...</p>
            ) : (
              <ExpenseList expenses={expenses} onStatusUpdate={handleStatusUpdate} currentRole={currentRole} />
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

export default App;
