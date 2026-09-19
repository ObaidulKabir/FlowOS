import React, { useState } from 'react';
import { Bot, Terminal, Copy, Check, ExternalLink, ShieldCheck, Code, Sparkles, BookOpen } from 'lucide-react';
import { usePlatformMetrics } from '../platformMetrics';
import { mcpRpcUrl, mcpRpcPath, MCP_JSONRPC_RULE } from '../mcpUrl';

export const McpAgentGuideline: React.FC = () => {
  const { mcpTools, tests } = usePlatformMetrics();
  const [copiedTab, setCopiedTab] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState<'browser' | 'curl' | 'config' | 'javascript'>('browser');
  const mcpUrl = mcpRpcUrl();
  const mcpPath = mcpRpcPath();

  const copyToClipboard = (text: string, tabId: string) => {
    navigator.clipboard.writeText(text);
    setCopiedTab(tabId);
    setTimeout(() => setCopiedTab(null), 2000);
  };

  const curlDiscovery = `curl -s ${mcpUrl} -H "Accept: application/json"`;

  const curlToolList = `curl -s -X POST ${mcpUrl} \\
  -H "Content-Type: application/json" \\
  -H "Accept: application/json, text/event-stream" \\
  -H "x-tenant-id: 22222222-2222-2222-2222-222222222222" \\
  -H "X-MCP-API-Key: flowos_prod_secret_key_32_chars_min" \\
  -H "MCP-Protocol-Version: 2025-03-26" \\
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}'`;

  const mcpConfig = JSON.stringify({
    mcpServers: {
      flowos: {
        url: mcpUrl,
        headers: {
          "x-tenant-id": "22222222-2222-2222-2222-222222222222",
          "X-MCP-API-Key": "flowos_prod_secret_key_32_chars_min"
        }
      }
    }
  }, null, 2);

  const jsSnippet = `// 1. Zero-Auth Public Discovery
const discovery = await fetch('${mcpUrl}', {
  headers: { 'Accept': 'application/json' }
}).then(res => res.json());

console.log(\`FlowOS MCP: \${discovery.name} - \${discovery.toolsCount} tools registered\`);

// 2. Call an MCP Tool over Streamable JSON-RPC 2.0
const rpcResponse = await fetch('${mcpUrl}', {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json',
    'Accept': 'application/json, text/event-stream',
    'x-tenant-id': '22222222-2222-2222-2222-222222222222',
    'X-MCP-API-Key': 'flowos_prod_secret_key_32_chars_min',
    'MCP-Protocol-Version': '2025-03-26'
  },
  body: JSON.stringify({
    jsonrpc: '2.0',
    id: 1,
    method: 'tools/call',
    params: {
      name: 'list_public_workflowclasses',
      arguments: {}
    }
  })
}).then(res => res.json());

console.log('Result:', rpcResponse.result?.content?.[0]?.text);`;

  return (
    <div className="bg-slate-800/90 border border-slate-700/80 rounded-2xl p-6 shadow-xl mb-10 backdrop-blur-sm">
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 border-b border-slate-700/80 pb-5 mb-6">
        <div>
          <div className="flex items-center gap-2 mb-1">
            <span className="p-1.5 bg-blue-500/20 text-blue-400 rounded-lg border border-blue-500/30">
              <Bot size={18} />
            </span>
            <h2 className="text-xl font-bold text-white tracking-tight flex items-center gap-2">
              AI Agent & Browser Guideline: How to Get MCP Tools
            </h2>
            <span className="px-2 py-0.5 text-[10px] font-extrabold uppercase tracking-wider bg-emerald-500/20 text-emerald-300 border border-emerald-500/30 rounded-full">
              {mcpTools} Production Tools
            </span>
            <span className="px-2 py-0.5 text-[10px] font-extrabold uppercase tracking-wider bg-cyan-500/20 text-cyan-300 border border-cyan-500/30 rounded-full">
              {tests.total} Tests Passing
            </span>
          </div>
          <p className="text-xs text-slate-300 max-w-3xl leading-relaxed">
            FlowOS is an authoritative, multi-tenant workflow control plane. Autonomous AI agents, browser bots, and LLM crawlers can discover schemas, inspect invariants, and orchestrate stateful processes under zero-trust governance. JSON-RPC must be posted to <code className="text-blue-300">{mcpUrl}</code> exactly — {MCP_JSONRPC_RULE} A paid Managed Cloud or Enterprise tenant includes every MCP tool. Trial keys may discover, lint, validate, and simulate — they cannot start instances until activated.
          </p>
        </div>

        <div className="flex items-center gap-2 flex-shrink-0">
          <a
            href={mcpPath}
            target="_blank"
            rel="noopener noreferrer"
            className="px-3.5 py-2 bg-gradient-to-r from-blue-600 to-indigo-600 hover:from-blue-500 hover:to-indigo-500 text-white text-xs font-semibold rounded-xl flex items-center gap-1.5 shadow-lg shadow-blue-500/20 transition-all border border-blue-400/30"
          >
            <Sparkles size={14} />
            <span>Open {mcpPath} Portal</span>
            <ExternalLink size={12} />
          </a>
        </div>
      </div>

      {/* Safety Policy Badges */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 mb-6 text-xs">
        <div className="bg-slate-900/60 border border-slate-700/50 p-3 rounded-xl">
          <div className="text-blue-400 font-semibold mb-1 flex items-center gap-1.5">
            <Sparkles size={14} />
            <span>Public GET Discovery</span>
          </div>
          <p className="text-[11px] text-slate-400">
            Fetch <code className="text-blue-300 bg-slate-800 px-1 rounded">GET {mcpPath}</code> without credentials to read full schemas and security metadata.
          </p>
        </div>

        <div className="bg-slate-900/60 border border-slate-700/50 p-3 rounded-xl">
          <div className="text-purple-400 font-semibold mb-1 flex items-center gap-1.5">
            <ShieldCheck size={14} />
            <span>Anti-BOLA / IDOR</span>
          </div>
          <p className="text-[11px] text-slate-400">
            Object-level tenant isolation. Foreign tenant IDs yield identical <code className="text-purple-300 bg-slate-800 px-1 rounded">MCP-NOTFOUND-001</code> errors.
          </p>
        </div>

        <div className="bg-slate-900/60 border border-slate-700/50 p-3 rounded-xl">
          <div className="text-amber-400 font-semibold mb-1 flex items-center gap-1.5">
            <BookOpen size={14} />
            <span>Human Approval Gate</span>
          </div>
          <p className="text-[11px] text-slate-400">
            Publishing plus context-binding activation and archival mandate <code className="text-amber-300 bg-slate-800 px-1 rounded">confirmHumanApproval: true</code>.
          </p>
        </div>

        <div className="bg-slate-900/60 border border-slate-700/50 p-3 rounded-xl">
          <div className="text-emerald-400 font-semibold mb-1 flex items-center gap-1.5">
            <Code size={14} />
            <span>Public Blueprints</span>
          </div>
          <p className="text-[11px] text-slate-400">
            Launch shared public workflows cross-tenant while execution state remains private to caller.
          </p>
        </div>
      </div>

      {/* Tabs */}
      <div className="flex items-center space-x-2 border-b border-slate-700 pb-2 mb-4">
        <button
          onClick={() => setActiveTab('browser')}
          className={`px-3 py-1.5 text-xs font-semibold rounded-lg transition-all flex items-center gap-1.5 ${
            activeTab === 'browser'
              ? 'bg-blue-600 text-white shadow-sm'
              : 'text-slate-400 hover:text-slate-200 hover:bg-slate-700/50'
          }`}
        >
          <Bot size={13} />
          <span>AI Browser / Web Agent</span>
        </button>

        <button
          onClick={() => setActiveTab('curl')}
          className={`px-3 py-1.5 text-xs font-semibold rounded-lg transition-all flex items-center gap-1.5 ${
            activeTab === 'curl'
              ? 'bg-blue-600 text-white shadow-sm'
              : 'text-slate-400 hover:text-slate-200 hover:bg-slate-700/50'
          }`}
        >
          <Terminal size={13} />
          <span>cURL Commands</span>
        </button>

        <button
          onClick={() => setActiveTab('config')}
          className={`px-3 py-1.5 text-xs font-semibold rounded-lg transition-all flex items-center gap-1.5 ${
            activeTab === 'config'
              ? 'bg-blue-600 text-white shadow-sm'
              : 'text-slate-400 hover:text-slate-200 hover:bg-slate-700/50'
          }`}
        >
          <Code size={13} />
          <span>mcp.json (Cursor / Claude)</span>
        </button>

        <button
          onClick={() => setActiveTab('javascript')}
          className={`px-3 py-1.5 text-xs font-semibold rounded-lg transition-all flex items-center gap-1.5 ${
            activeTab === 'javascript'
              ? 'bg-blue-600 text-white shadow-sm'
              : 'text-slate-400 hover:text-slate-200 hover:bg-slate-700/50'
          }`}
        >
          <Code size={13} />
          <span>JavaScript Fetch</span>
        </button>
      </div>

      {/* Tab Content */}
      {activeTab === 'browser' && (
        <div className="bg-slate-900/80 rounded-xl p-4 border border-slate-800 text-xs">
          <h3 className="font-semibold text-white mb-2 flex items-center gap-2">
            <span>🌐 Browser Model & Crawler Discovery Procedure</span>
          </h3>
          <ol className="list-decimal list-inside space-y-2 text-slate-300 leading-relaxed">
            <li>
              <strong>Discover Endpoint:</strong> Navigate directly to <a href={mcpPath} className="text-blue-400 underline hover:text-blue-300">{mcpPath}</a> (or <code className="text-blue-300">{mcpUrl}</code>). In human/browser mode, it renders a live interactive HTML tool catalog. Use a tenant API key generated in <em>this</em> environment — staging keys authenticate only on flowos.prospectbdltd.com, production keys only on flowosbd.com.
            </li>
            <li>
              <strong>Machine-Readable Discovery:</strong> For programmatic bots, make an HTTP <code className="text-blue-300">GET {mcpPath}</code> with header <code className="text-slate-200 bg-slate-800 px-1 py-0.5 rounded">Accept: application/json</code>. The JSON <code className="text-blue-300">url</code> / <code className="text-blue-300">connection.jsonrpcUrl</code> is the exact JSON-RPC address. No API keys or authentication credentials are required for discovery.
            </li>
            <li>
              <strong>Ingest Tools & Constraints:</strong> The payload delivers all {mcpTools} tools, parameter types, <code className="text-blue-300">riskLevel</code> (<code className="text-emerald-300">low</code>, <code className="text-amber-300">medium</code>, <code className="text-rose-300">high</code>), <code className="text-blue-300">sideEffect</code>, and <code className="text-blue-300">requiresHumanConfirmation</code>.
            </li>
            <li>
              <strong>Execute via POST:</strong> Switch to <code className="text-blue-300">POST {mcpUrl}</code> for JSON-RPC 2.0. Keep a trailing slash if the URL has one. Do not follow 301 redirects for POST. Send tenant header <code className="text-slate-200 bg-slate-800 px-1 py-0.5 rounded">x-tenant-id</code> and <code className="text-slate-200 bg-slate-800 px-1 py-0.5 rounded">X-MCP-API-Key</code>.
            </li>
            <li>
              <strong>Plan entitlement:</strong> MCP is included in the tenant subscription. Trial keys return <code className="text-amber-300 bg-slate-800 px-1 rounded">MCP-PLAN-REQUIRED</code> (HTTP 402) for runtime tools such as <code className="text-blue-300">start_workflow</code>, <code className="text-blue-300">publish_event</code>, and <code className="text-blue-300">complete_task</code>.
            </li>
            <li>
              <strong>Dual-kernel design:</strong> Call MCP prompt <code className="text-blue-300">design_dual_kernel_workflow</code> or read <code className="text-blue-300">flowos://guides/dual-kernel-design</code>. Workflow <code className="text-blue-300">currentStep</code> and state-machine <code className="text-blue-300">currentState</code> are independent. A Decision <code className="text-blue-300">Default</code>/<code className="text-blue-300">true</code> auto-route does not consume a business event — still send <code className="text-blue-300">QUOTE_APPROVED</code> (or the unused SM trigger) in <code className="text-blue-300">simulate_workflowclass</code> / <code className="text-blue-300">simulate_context_binding</code>. If step is ahead of state, that missing event is the next call.
            </li>
            <li>
              <strong>SLA reminders and timeouts:</strong> Call MCP prompt <code className="text-blue-300">test_sla_reminders_in_simulator</code> or read <code className="text-blue-300">flowos://guides/sla-reminder-simulation</code>. Validate only proves the SLA JSON. A simulate-to-Paid run is not a wall clock — if <code className="text-blue-300">QUOTE_APPROVED</code> is in <code className="text-blue-300">events</code>, timeout must not fire; look for <code className="text-blue-300">[SLA Reminder Fired]</code> before that completing event. To prove overdue, set <code className="text-blue-300">autoAdvanceTimers: true</code> and omit the completing event so <code className="text-blue-300">QUOTE_RESPONSE_OVERDUE</code> / <code className="text-blue-300">REPAIR_OVERDUE</code> fire. Do not start a live instance and wait.
            </li>
            <li>
              <strong>AI task automation:</strong> Call MCP prompt <code className="text-blue-300">automate_waiting_task_with_ai_agent</code> or read <code className="text-blue-300">flowos://guides/ai-task-automation</code>. Paid tenants default to FlowOS hosted OpenAI (<code className="text-blue-300">flowos-hosted</code>, platform key <code className="text-blue-300">FLOWOS_HOSTED_LLM_API_KEY</code>, daily cap). BYO keys stay optional via <code className="text-blue-300">upsert_agent_provider</code>. Keep a waiting HumanTask/Command (never a Decision <code className="text-blue-300">Default</code> skip). Set <code className="text-blue-300">actor: Agent</code> or <code className="text-blue-300">Either</code>. Inspect with <code className="text-blue-300">preview_agent_context</code> or <code className="text-blue-300">get_agent_context</code>. <code className="text-blue-300">simulate_*</code> never calls the live LLM. On a live instance, publish the human gate then <code className="text-blue-300">run_agent_task</code>.
            </li>
            <li>
              <strong>Tenant resource tools:</strong> Expose tenant APIs with <code className="text-blue-300">register_connector</code>, then declare them on the waiting step as <code className="text-blue-300">LookupRecord:crm.customer.get.v1</code>, <code className="text-blue-300">QueryRecords:</code>, <code className="text-blue-300">FetchDocument:</code>, <code className="text-blue-300">SearchKnowledge:</code>, or <code className="text-blue-300">CheckPolicy:</code>. FlowOS prefetches those reads into Agent Context. Write connectors (<code className="text-blue-300">connector:payment.refund.v1</code>) and notify plugins are listed but not prefetched. The model never sees the URL.
            </li>
            <li>
              <strong>Compensation Safety Loop:</strong> Run <code className="text-blue-300">lint_draft_workflowclass</code>, then fix <code className="text-blue-300">WF-COMP-010</code> / <code className="text-blue-300">LINT-COMP-001</code> by attaching <code className="text-blue-300">OnFailure</code> hooks. Validate with <code className="text-blue-300">simulate_compensation_path</code> (design-time) and <code className="text-blue-300">plan_workflow_compensation_path</code> (runtime instance).
            </li>
            <li>
              <strong>Connector Setup Loop:</strong> Register with <code className="text-blue-300">register_connector</code>, verify with <code className="text-blue-300">validate_connector</code>, then wire <code className="text-blue-300">attach_step_action</code> using <code className="text-blue-300">actionType=InvokeConnector</code> and explicit <code className="text-blue-300">connector</code>. The older <code className="text-blue-300">*_capability_binding</code> tools and <code className="text-blue-300">InvokeCapability</code> still work.
            </li>
            <li>
              <strong>Plugin Discovery Loop:</strong> Start with <code className="text-blue-300">list_registered_plugins</code> to discover server providers, then map aliases via <code className="text-blue-300">register_plugin_binding</code>.
            </li>
            <li>
              <strong>Plugin Mapping Loop:</strong> Use <code className="text-blue-300">register_plugin_binding</code> to map blueprint aliases (for <code className="text-blue-300">actionType</code> or <code className="text-blue-300">decisionProvider</code>) to server plugins, inspect with <code className="text-blue-300">list_plugin_bindings</code>, and verify with <code className="text-blue-300">resolve_plugin_binding</code>.
            </li>
          </ol>
        </div>
      )}

      {activeTab === 'curl' && (
        <div className="bg-slate-900/80 rounded-xl p-4 border border-slate-800 text-xs space-y-4">
          <div>
            <div className="flex items-center justify-between mb-1.5">
              <span className="text-slate-300 font-semibold">1. Public Discovery (Zero Credentials Required):</span>
              <button
                onClick={() => copyToClipboard(curlDiscovery, 'curl1')}
                className="text-slate-400 hover:text-white flex items-center gap-1 text-[11px]"
              >
                {copiedTab === 'curl1' ? <Check size={12} className="text-emerald-400" /> : <Copy size={12} />}
                <span>{copiedTab === 'curl1' ? 'Copied' : 'Copy'}</span>
              </button>
            </div>
            <pre className="bg-slate-950 p-3 rounded-lg font-mono text-[11px] text-blue-300 overflow-x-auto border border-slate-800/80">
              {curlDiscovery}
            </pre>
          </div>

          <div>
            <div className="flex items-center justify-between mb-1.5">
              <span className="text-slate-300 font-semibold">2. Handshake & List Tools (Authenticated Streamable JSON-RPC):</span>
              <button
                onClick={() => copyToClipboard(curlToolList, 'curl2')}
                className="text-slate-400 hover:text-white flex items-center gap-1 text-[11px]"
              >
                {copiedTab === 'curl2' ? <Check size={12} className="text-emerald-400" /> : <Copy size={12} />}
                <span>{copiedTab === 'curl2' ? 'Copied' : 'Copy'}</span>
              </button>
            </div>
            <pre className="bg-slate-950 p-3 rounded-lg font-mono text-[11px] text-emerald-300 overflow-x-auto border border-slate-800/80">
              {curlToolList}
            </pre>
          </div>
        </div>
      )}

      {activeTab === 'config' && (
        <div className="bg-slate-900/80 rounded-xl p-4 border border-slate-800 text-xs">
          <div className="flex items-center justify-between mb-2">
            <span className="text-slate-300 font-semibold">Place in your Cursor or Claude Desktop mcp.json:</span>
            <button
              onClick={() => copyToClipboard(mcpConfig, 'mcpcfg')}
              className="text-slate-400 hover:text-white flex items-center gap-1 text-[11px]"
            >
              {copiedTab === 'mcpcfg' ? <Check size={12} className="text-emerald-400" /> : <Copy size={12} />}
              <span>{copiedTab === 'mcpcfg' ? 'Copied' : 'Copy'}</span>
            </button>
          </div>
          <pre className="bg-slate-950 p-3 rounded-lg font-mono text-[11px] text-purple-300 overflow-x-auto border border-slate-800/80">
            {mcpConfig}
          </pre>
        </div>
      )}

      {activeTab === 'javascript' && (
        <div className="bg-slate-900/80 rounded-xl p-4 border border-slate-800 text-xs">
          <div className="flex items-center justify-between mb-2">
            <span className="text-slate-300 font-semibold">Client Integration Snippet:</span>
            <button
              onClick={() => copyToClipboard(jsSnippet, 'jscode')}
              className="text-slate-400 hover:text-white flex items-center gap-1 text-[11px]"
            >
              {copiedTab === 'jscode' ? <Check size={12} className="text-emerald-400" /> : <Copy size={12} />}
              <span>{copiedTab === 'jscode' ? 'Copied' : 'Copy'}</span>
            </button>
          </div>
          <pre className="bg-slate-950 p-3 rounded-lg font-mono text-[11px] text-amber-300 overflow-x-auto border border-slate-800/80 max-h-64">
            {jsSnippet}
          </pre>
        </div>
      )}
    </div>
  );
};
