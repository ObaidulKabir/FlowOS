const axios = require('axios');

const API_URL = 'http://localhost:5183/api';
const TENANT_ID = '22222222-2222-2222-2222-222222222222';
const MOCK_ROLE = 'Admin';

const api = axios.create({
  baseURL: API_URL,
  headers: {
    'x-tenant-id': TENANT_ID,
    'X-Mock-Role': MOCK_ROLE,
    'Content-Type': 'application/json'
  }
});

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
    return wf;
  } catch (error) {
    console.error('Error getting status:', error.response ? error.response.data : error.message);
  }
}

async function main() {
  try {
    console.log('Running Automated Demo...');
    
    // 1. Start
    const workflowId = await startWorkflow();
    await getWorkflowStatus(workflowId);

    // 2. Submit
    await new Promise(r => setTimeout(r, 1000));
    await publishEvent(workflowId, 'EVT-SUBMIT', { amount: 500, reason: "Demo Request" });
    await getWorkflowStatus(workflowId);

    // 3. Approve
    await new Promise(r => setTimeout(r, 1000));
    await publishEvent(workflowId, 'EVT-APPROVE', { approver: "AutoBot" });
    await getWorkflowStatus(workflowId);

    console.log('\nDemo Completed Successfully.');
  } catch (err) {
    console.error('Unexpected error:', err);
  }
}

main();
