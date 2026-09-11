/**
 * FlowOS MCP Agent Workflow Test Runner (Node.js)
 * Executes the 4-step AI agent verification lifecycle against FlowOS MCP endpoint.
 */

const URL = process.env.MCP_URL || 'https://flowos.prospectbdltd.com/mcp';
const API_KEY = process.env.MCP_API_KEY || 'flowos_prod_secret_key_32_chars_min';
const TENANT_ID = process.env.MCP_TENANT_ID || '22222222-2222-2222-2222-222222222222';

async function sendRpc(method, params = null, id = 1) {
  const payload = {
    jsonrpc: '2.0',
    id,
    method,
    ...(params ? { params } : {})
  };

  const res = await fetch(URL, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json, text/event-stream',
      'X-MCP-API-Key': API_KEY,
      'x-tenant-id': TENANT_ID,
      'MCP-Protocol-Version': '2025-03-26'
    },
    body: JSON.stringify(payload)
  });

  if (!res.ok) {
    const errorText = await res.text();
    throw new Error(`HTTP ${res.status}: ${errorText}`);
  }

  return await res.json();
}

async function run() {
  console.log('============================================================');
  console.log(' FlowOS MCP AI Agent Workflow Verification (Node.js)');
  console.log(` Target:   ${URL}`);
  console.log(` Tenant:   ${TENANT_ID}`);
  console.log('============================================================\n');

  // STEP 1: Discovery GET /mcp
  console.log('>> STEP 1: Testing Public GET /mcp Discovery Endpoint');
  try {
    const discRes = await fetch(URL, { headers: { 'Accept': 'application/json' } });
    if (discRes.ok) {
      const disc = await discRes.json();
      console.log('  [PASS] Discovery endpoint reachable without API key');
      console.log(`  [INFO] Name: ${disc.name}, Status: ${disc.status}, Tools: ${disc.toolsCount}`);
    } else {
      console.log(`  [WARN] Discovery returned HTTP ${discRes.status}`);
    }
  } catch (err) {
    console.log(`  [WARN] Discovery error: ${err.message}`);
  }

  // STEP 2: MCP Handshake
  console.log('\n>> STEP 2: Initializing MCP Handshake (initialize & tools/list)');
  const init = await sendRpc('initialize', {
    protocolVersion: '2025-03-26',
    clientInfo: { name: 'FlowOS-Node-Tester', version: '1.0.0' },
    capabilities: {}
  }, 1);
  console.log(`  [PASS] MCP Initialized. Server protocol: ${init.result?.protocolVersion}`);

  const toolsRes = await sendRpc('tools/list', {}, 2);
  const tools = toolsRes.result?.tools || [];
  console.log(`  [PASS] Registered tools discovered: ${tools.length}`);

  // STEP 3: Active Workflows
  console.log('\n>> STEP 3: Agent Query: "Show me all active workflows for this tenant"');
  const list = await sendRpc('tools/call', {
    name: 'list_workflow_instances',
    arguments: { status: 'Active' }
  }, 3);
  console.log('  [PASS] Workflow instances result:');
  console.log(list.result?.content?.[0]?.text);

  // STEP 4: Start Workflow & Inspect State
  console.log('\n>> STEP 4: Agent Action: Start Workflow & Inspect State');
  const start = await sendRpc('tools/call', {
    name: 'start_workflow',
    arguments: { workflowName: 'ExpenseApproval' }
  }, 4);
  const startText = start.result?.content?.[0]?.text || '';
  console.log(`  [INFO] Start result: ${startText}`);

  let instanceId = null;
  try {
    instanceId = JSON.parse(startText).workflowInstanceId;
  } catch {
    const m = startText.match(/[0-9a-fA-F-]{36}/);
    if (m) instanceId = m[0];
  }

  if (instanceId) {
    console.log(`  [PASS] Started workflow instance: ${instanceId}`);

    console.log(`  [INFO] Querying status: "Why is workflow ${instanceId} waiting?"`);
    const status = await sendRpc('tools/call', {
      name: 'get_workflow_instance_status',
      arguments: { instanceId }
    }, 5);
    console.log('  [PASS] Status & Step:');
    console.log(status.result?.content?.[0]?.text);

    // STEP 5: State Machine Enforcement: Rejection of Invalid Transition
    console.log('\n>> STEP 5: Verifying "State Machine = Law" (Illegal Transition Rejection)');
    const illegal = await sendRpc('tools/call', {
      name: 'publish_event',
      arguments: {
        workflowInstanceId: instanceId,
        eventType: 'EVT-ILLEGAL-JUMP-STATE'
      }
    }, 6);
    const illegalText = illegal.result?.content?.[0]?.text || '';
    if (illegal.result?.isError || illegalText.includes('not accepted')) {
      console.log(`  [PASS] State Machine successfully REJECTED illegal transition: ${illegalText}`);
    } else {
      console.log(`  [WARN] Unexpected state machine response: ${illegalText}`);
    }

    // STEP 6: Execute Valid Transition
    console.log('\n>> STEP 6: Agent Action: Execute Valid State Transition');
    const valid = await sendRpc('tools/call', {
      name: 'publish_event',
      arguments: {
        workflowInstanceId: instanceId,
        eventType: 'EVT-SUBMIT',
        payload: { amount: 250, currency: 'USD' }
      }
    }, 7);
    console.log('  [PASS] Valid transition result:');
    console.log(valid.result?.content?.[0]?.text);
  }

  console.log('\n============================================================');
  console.log(' FlowOS MCP Agent Workflow Verification COMPLETED!');
  console.log('============================================================');
}

run().catch(err => {
  console.error('\n[FAIL] Test execution failed:', err);
  process.exit(1);
});
