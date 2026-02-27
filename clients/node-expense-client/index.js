const axios = require('axios');
const readline = require('readline');

const API_URL = 'http://localhost:5183/api';
const TENANT_ID = '22222222-2222-2222-2222-222222222222';
const MOCK_ROLE = 'Admin';

const rl = readline.createInterface({
  input: process.stdin,
  output: process.stdout
});

const api = axios.create({
  baseURL: API_URL,
  headers: {
    'x-tenant-id': TENANT_ID,
    'X-Mock-Role': MOCK_ROLE,
    'Content-Type': 'application/json'
  }
});

function ask(question) {
  return new Promise(resolve => rl.question(question, resolve));
}

async function startWorkflow() {
  try {
    console.log('\n--- Starting ExpenseApproval Workflow ---');
    const response = await api.post('/workflows/start', {
      workflowName: 'ExpenseApproval',
      version: 1,
      tenantId: TENANT_ID
    });
    console.log('Workflow Started! ID:', response.data.workflowInstanceId);
    return response.data.workflowInstanceId;
  } catch (error) {
    console.error('Error starting workflow:', error.response ? error.response.data : error.message);
    throw error;
  }
}

async function publishEvent(workflowId, eventType, payload = {}) {
  try {
    console.log(`\n--- Publishing Event: ${eventType} ---`);
    const response = await api.post('/events/publish', {
      tenantId: TENANT_ID,
      workflowInstanceId: workflowId,
      eventType: eventType,
      payload: payload
    });
    console.log('Event Published:', response.data);
  } catch (error) {
    console.error(`Error publishing event ${eventType}:`, error.response ? error.response.data : error.message);
  }
}

async function getWorkflowStatus(workflowId) {
  try {
    console.log('\n--- Checking Workflow Status ---');
    const response = await api.get(`/admin/workflows/${workflowId}`);
    const wf = response.data;
    console.log(`Status: ${wf.status}`);
    console.log(`Current Step: ${wf.currentStepId}`);
    if (wf.timeline && wf.timeline.length > 0) {
        console.log('Timeline:');
        wf.timeline.forEach(t => console.log(`  - ${t.timestamp}: ${t.summary}`));
    }
    return wf;
  } catch (error) {
    console.error('Error getting status:', error.response ? error.response.data : error.message);
  }
}

async function main() {
  try {
    console.log('FlowOS Node.js Client - Expense Approval Demo');
    console.log('---------------------------------------------');

    // 1. Start
    await ask('Press Enter to START a new workflow...');
    const workflowId = await startWorkflow();
    await getWorkflowStatus(workflowId);

    // 2. Submit
    await ask('\nPress Enter to SUBMIT the expense request (EVT-SUBMIT)...');
    await publishEvent(workflowId, 'EVT-SUBMIT', { amount: 100, reason: "Office Supplies" });
    await getWorkflowStatus(workflowId);

    // 3. Approve or Reject
    const action = await ask('\nChoose action: (A)pprove or (R)eject? [A/R]: ');
    
    if (action.toUpperCase() === 'R') {
        await publishEvent(workflowId, 'EVT-REJECT', { reason: "Too expensive" });
    } else {
        await publishEvent(workflowId, 'EVT-APPROVE', { approver: "Manager" });
    }

    // 4. Final Status
    await getWorkflowStatus(workflowId);

    console.log('\nDemo Completed.');
  } catch (err) {
    console.error('Unexpected error:', err);
  } finally {
    rl.close();
  }
}

main();
