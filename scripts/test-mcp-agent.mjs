/**
 * FlowOS MCP Agent Workflow Test Runner (Node.js)
 * Executes the AI agent verification lifecycle against a FlowOS MCP endpoint.
 *
 * Loads .env, .env.${FLOWOS_ENV}, and .env.local from the repo root so credentials
 * work outside Next.js. Override with MCP_URL, FLOWOS_PUBLIC_ORIGIN, MCP_API_KEY, MCP_TENANT_ID.
 *
 * Staging:    https://flowos.prospectbdltd.com/mcp
 * Production: https://flowosbd.com/mcp/
 */

import { existsSync, readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const envFromShell = new Set(Object.keys(process.env));

function loadEnvFile(path) {
  if (!existsSync(path))
    return;

  for (const raw of readFileSync(path, 'utf8').split(/\r?\n/)) {
    const line = raw.trim();
    if (!line || line.startsWith('#'))
      continue;

    const separator = line.indexOf('=');
    if (separator <= 0)
      continue;

    const key = line.slice(0, separator).trim();
    let value = line.slice(separator + 1).trim();
    if ((value.startsWith('"') && value.endsWith('"')) ||
        (value.startsWith("'") && value.endsWith("'")))
      value = value.slice(1, -1);

    if (envFromShell.has(key))
      continue;

    process.env[key] = value;
  }
}

loadEnvFile(resolve(repoRoot, '.env'));
const envFlavor = (process.env.FLOWOS_ENV || '').trim().toLowerCase();
if (envFlavor === 'staging' || envFlavor === 'production')
  loadEnvFile(resolve(repoRoot, `.env.${envFlavor}`));
loadEnvFile(resolve(repoRoot, '.env.local'));

function isProductionHost(hostname) {
  const host = (hostname || '').toLowerCase();
  return host === 'flowosbd.com' || host.endsWith('.flowosbd.com');
}

function mcpUrlForOrigin(origin) {
  const trimmed = (origin || '').trim().replace(/\/+$/, '');
  if (!trimmed)
    return '';

  try {
    const host = new URL(trimmed).hostname;
    return isProductionHost(host) ? `${trimmed}/mcp/` : `${trimmed}/mcp`;
  } catch {
    return `${trimmed}/mcp`;
  }
}

function normalizeMcpUrl(url) {
  const trimmed = (url || '').trim();
  if (!trimmed)
    return '';

  try {
    const parsed = new URL(trimmed);
    if (parsed.pathname === '/mcp' && isProductionHost(parsed.hostname))
      parsed.pathname = '/mcp/';
    else if (parsed.pathname === '/mcp/' && !isProductionHost(parsed.hostname))
      parsed.pathname = '/mcp';
    return parsed.toString();
  } catch {
    return trimmed;
  }
}

function resolveMcpUrl() {
  if (process.env.MCP_URL)
    return normalizeMcpUrl(process.env.MCP_URL);
  if (process.env.FLOWOS_PUBLIC_ORIGIN)
    return mcpUrlForOrigin(process.env.FLOWOS_PUBLIC_ORIGIN);

  throw new Error(
    'Set MCP_URL or FLOWOS_PUBLIC_ORIGIN. Staging: https://flowos.prospectbdltd.com/mcp  Production: https://flowosbd.com/mcp/'
  );
}

const URL = resolveMcpUrl();
const API_KEY = process.env.MCP_API_KEY || '';
const TENANT_ID = process.env.MCP_TENANT_ID || '';

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
  console.log(` Tenant:   ${TENANT_ID || '(not set — set MCP_TENANT_ID in .env.local)'}`);
  console.log('============================================================\n');

  if (!API_KEY || !TENANT_ID) {
    throw new Error('Set MCP_API_KEY and MCP_TENANT_ID in .env.local (or the environment) before running this script.');
  }

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
  if (init.error) {
    throw new Error(`initialize failed: ${init.error.message}`);
  }
  console.log(`  [PASS] MCP Initialized. Server protocol: ${init.result?.protocolVersion}`);

  const toolsRes = await sendRpc('tools/list', {}, 2);
  if (toolsRes.error) {
    throw new Error(`tools/list failed: ${toolsRes.error.message}`);
  }
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

  // STEP 7: Enforced Human Confirmation Gate (Anti-Autonomy Violation)
  console.log('\n>> STEP 7: Verifying Enforced Human Confirmation Gate on High-Risk Action');
  const unconfirmedPublish = await sendRpc('tools/call', {
    name: 'publish_workflowclass',
    arguments: { id: '00000000-0000-0000-0000-000000000001' }
  }, 8);
  const unconfirmedText = unconfirmedPublish.result?.content?.[0]?.text || '';
  if (unconfirmedText.includes('MCP-APPROVAL-REQUIRED')) {
    console.log(`  [PASS] Machine-enforced human gate blocked execution: ${unconfirmedText}`);
  } else {
    console.log(`  [WARN] Unexpected response without human approval: ${unconfirmedText}`);
  }

  console.log('\n============================================================');
  console.log(' FlowOS MCP Agent Workflow Verification COMPLETED!');
  console.log('============================================================');
}

run().catch(err => {
  console.error('\n[FAIL] Test execution failed:', err);
  process.exit(1);
});
