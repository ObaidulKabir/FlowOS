const express = require('express');
const sqlite3 = require('sqlite3').verbose();
const cors = require('cors');
const axios = require('axios');
const bodyParser = require('body-parser');
const crypto = require('crypto');

const app = express();
const PORT = 3001;

app.use(cors());
app.use(bodyParser.json());

// Database Setup
const db = new sqlite3.Database('./expenses.db');

db.serialize(() => {
  db.run(`
    CREATE TABLE IF NOT EXISTS expenses (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      amount REAL,
      description TEXT,
      status TEXT,
      workflow_instance_id TEXT
    )
  `);
});

// FlowOS Configuration
const FLOWOS_API_URL = 'http://localhost:5005/api';
const TENANT_ID = '22222222-2222-2222-2222-222222222222'; // Matches DataSeeder Demo Client
const DEFAULT_MOCK_ROLE = 'Admin'; 

// Middleware to extract role from header
const roleMiddleware = (req, res, next) => {
    req.mockRole = req.headers['x-mock-role'] || DEFAULT_MOCK_ROLE;
    console.log(`[Request] ${req.method} ${req.path} - Role: ${req.mockRole}`);
    next();
};

app.use(roleMiddleware);

// Debug: List all workflow classes
app.get('/api/debug/workflows', async (req, res) => {
    try {
        const response = await axios.get(`${FLOWOS_API_URL}/workflow-classes`, {
            headers: { 'x-tenant-id': TENANT_ID, 'X-Mock-Role': req.mockRole }
        });
        res.json(response.data);
    } catch (error) {
        res.status(500).json({ error: error.message, details: error.response?.data });
    }
});

// Helper to get Workflow Class ID
let WORKFLOW_CLASS_ID = null;

async function getWorkflowClassId(role) {
  if (WORKFLOW_CLASS_ID) return WORKFLOW_CLASS_ID;
  try {
    const response = await axios.get(`${FLOWOS_API_URL}/workflow-classes`, {
      headers: {
        'x-tenant-id': TENANT_ID,
        'X-Mock-Role': 'Admin' // Use Admin to discover classes initially
      }
    });
    
    console.log('Available Workflow Classes:', response.data.map(w => `${w.name} (${w.status})`));

    // 1. Try V2
    const v2 = response.data.find(w => w.name === 'ExpenseApprovalV2');
    if (v2) {
        WORKFLOW_CLASS_ID = v2.id;
        console.log(`Selected V2: ${v2.name} (${v2.status})`);
        return WORKFLOW_CLASS_ID;
    }

    // 2. Try V1 (ExpenseApproval)
    const v1 = response.data.find(w => w.name === 'ExpenseApproval');
    if (v1) {
        WORKFLOW_CLASS_ID = v1.id;
        console.log(`Selected V1: ${v1.name} (${v1.status})`);
        return WORKFLOW_CLASS_ID;
    }

    // 3. Fallback
    const any = response.data[0];
    if (any) {
        WORKFLOW_CLASS_ID = any.id;
        console.log(`Selected Fallback: ${any.name} (${any.status})`);
        return WORKFLOW_CLASS_ID;
    }

  } catch (error) {
    console.error('Error fetching workflow classes:', error.message);
  }
  return WORKFLOW_CLASS_ID;
}

// Routes

// Get all expenses
app.get('/api/expenses', (req, res) => {
  db.all('SELECT * FROM expenses ORDER BY id DESC', [], async (err, rows) => {
    if (err) return res.status(500).json({ error: err.message });
    
    // Enrich with current step from FlowOS (Optional but good for sync)
    // For now just return local DB state
    res.json(rows);
  });
});

// Create Expense
app.post('/api/expenses', async (req, res) => {
  const { amount, description } = req.body;
  
  try {
    const classId = await getWorkflowClassId();
    // if (!classId) return res.status(500).json({ error: 'Workflow Class not available' });
    // Retry finding class ID if null (sometimes async seeding takes time)
    if (!classId) {
        console.log('Workflow Class not found initially, retrying...');
        WORKFLOW_CLASS_ID = null; // Clear cache
        const retryClassId = await getWorkflowClassId();
        if (!retryClassId) return res.status(500).json({ error: 'Workflow Class not available after retry' });
    }
    
    const finalClassId = WORKFLOW_CLASS_ID;

    // Start Workflow
    const startCmd = {
      tenantId: TENANT_ID,
      workflowClassId: finalClassId,
      correlationId: crypto.randomUUID()
    };

    console.log('Starting workflow with:', startCmd);

    const wfResponse = await axios.post(`${FLOWOS_API_URL}/workflows/start`, startCmd, {
      headers: {
        'x-tenant-id': TENANT_ID,
        'X-Mock-Role': req.mockRole // Use role from frontend
      }
    });

    const workflowInstanceId = wfResponse.data.workflowInstanceId;
    console.log('Workflow Started:', workflowInstanceId);

    // Auto-Submit (Draft -> PendingManager)
    // Only if role is Employee or Admin (or allow Manager to self-submit?)
    console.log(`Auto-submitting EVT-SUBMIT for instance ${workflowInstanceId}`);
    try {
        await axios.post(`${FLOWOS_API_URL}/events/publish`, {
            tenantId: TENANT_ID,
            workflowInstanceId: workflowInstanceId,
            eventType: 'EVT-SUBMIT'
        }, {
            headers: {
                'x-tenant-id': TENANT_ID,
                'X-Mock-Role': req.mockRole // Use role from frontend
            }
        });
    } catch (subErr) {
        console.error('Auto-submit failed:', subErr.response?.data || subErr.message);
        // If employee lacks permission, this will fail.
        // But Employee has EVT-SUBMIT permission seeded.
    }

    // Save to DB
    const stmt = db.prepare('INSERT INTO expenses (amount, description, status, workflow_instance_id) VALUES (?, ?, ?, ?)');
    stmt.run(amount, description, 'PendingManager', workflowInstanceId, function(err) {
      if (err) return res.status(500).json({ error: err.message });
      res.json({ id: this.lastID, amount, description, status: 'PendingManager', workflow_instance_id: workflowInstanceId });
    });
    stmt.finalize();

  } catch (error) {
    console.error('Create Expense Error:', error.response?.data || error.message);
    res.status(500).json({ error: 'Failed to create expense' });
  }
});

// Approve Expense
app.post('/api/expenses/:id/approve', async (req, res) => {
    const { id } = req.params;
    
    db.get('SELECT * FROM expenses WHERE id = ?', [id], async (err, row) => {
        if (err || !row) return res.status(404).json({ error: 'Expense not found' });

        const amount = row.amount;
        let eventType = 'EVT-APPROVE';
        let newStatus = 'Approved';

        if (row.status === 'PendingManager') {
            if (amount > 100) {
                // If amount > 100, Manager must escalate to Director
                eventType = 'EVT-ESCALATE';
                newStatus = 'PendingDirector';
            } else {
                // If amount <= 100, Manager can approve directly
                eventType = 'EVT-APPROVE';
                newStatus = 'Approved';
            }
        } else if (row.status === 'PendingDirector') {
            eventType = 'EVT-DIRECTOR-APPROVE';
            newStatus = 'Approved';
        }

        console.log(`[Backend Logic] Amount: ${amount}, Status: ${row.status} -> Event: ${eventType}, NewStatus: ${newStatus}`);

        handleExpenseActionLogic(res, row, eventType, newStatus, req.mockRole);
    });
});

// Reject Expense
app.post('/api/expenses/:id/reject', async (req, res) => {
    const { id } = req.params;
    
    db.get('SELECT * FROM expenses WHERE id = ?', [id], async (err, row) => {
        if (err || !row) return res.status(404).json({ error: 'Expense not found' });

        let eventType = 'EVT-REJECT';
        let newStatus = 'Rejected';

        if (row.status === 'PendingDirector') {
             eventType = 'EVT-DIRECTOR-REJECT';
        }

        handleExpenseActionLogic(res, row, eventType, newStatus, req.mockRole);
    });
});

async function handleExpenseActionLogic(res, row, eventType, newStatus, mockRole) {
    try {
        console.log(`Publishing event ${eventType} for instance ${row.workflow_instance_id}`);
        // Publish Event to FlowOS
        const publishCmd = {
            tenantId: TENANT_ID,
            workflowInstanceId: row.workflow_instance_id,
            eventType: eventType
        };
        
        await axios.post(`${FLOWOS_API_URL}/events/publish`, publishCmd, {
                headers: {
                'x-tenant-id': TENANT_ID,
                'X-Mock-Role': mockRole // Use role from frontend
            }
        });
        
        // Update Local DB
        db.run('UPDATE expenses SET status = ? WHERE id = ?', [newStatus, row.id], (updateErr) => {
            if (updateErr) return res.status(500).json({ error: updateErr.message });
            res.json({ ...row, status: newStatus });
        });
        
    } catch (error) {
            console.error(`Action ${eventType} Error:`, error.response?.data || error.message);
            res.status(500).json({ 
                error: `Failed to ${eventType}`, 
                details: error.response?.data || error.message 
            });
    }
}

// Get Expense History
app.get('/api/expenses/:id/history', (req, res) => {
    const { id } = req.params;
    
    db.get('SELECT * FROM expenses WHERE id = ?', [id], async (err, row) => {
      if (err || !row) return res.status(404).json({ error: 'Expense not found' });
      
      try {
          // Use Admin API to get full history/timeline
          const response = await axios.get(`${FLOWOS_API_URL}/admin/workflows/${row.workflow_instance_id}`, {
              headers: {
                  'x-tenant-id': TENANT_ID,
                  'X-Mock-Role': req.mockRole
              }
          });
          res.json(response.data);
      } catch (error) {
          console.error('History Fetch Error:', error.response?.data || error.message);
          res.status(500).json({ error: 'Failed to fetch workflow history' });
      }
    });
  });

app.listen(PORT, async () => {
  console.log(`Server running on http://localhost:${PORT}`);
  await getWorkflowClassId();
});
