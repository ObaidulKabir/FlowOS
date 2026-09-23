export type FitScore = 0 | 1 | 2;

export const OS1_PILLARS = [
  "Identity",
  "Dual-kernel",
  "Policy",
  "Inbox",
  "Integrations",
  "AI loop",
  "Simulation",
  "Ops",
  "Commercial",
] as const;

export type Os1Pillar = (typeof OS1_PILLARS)[number];

export type ComparisonPlatform = {
  id: string;
  name: string;
  category: string;
  scores: FitScore[];
  pickWhen: string;
  theyWin: string;
  theyLose: string;
};

export const COMPARISON_PLATFORMS: ComparisonPlatform[] = [
  {
    id: "flowos",
    name: "FlowOS",
    category: "Business automation OS",
    scores: [2, 2, 2, 2, 2, 2, 2, 2, 2],
    pickWhen:
      "Tenant SaaS where AI agents must author, simulate, and run legal work under dual-kernel Law plus a HumanTask inbox, over MCP.",
    theyWin:
      "Declarative JSON blueprints, native MCP, fail-closed Law, hosted DecisionPacket / autoCommit, paid-plan FlowOS OpenAI (flowos-hosted, daily cap) plus optional BYO, side-effect-free simulation, .NET 8 + Postgres footprint.",
    theyLose:
      "Not BPMN. Not polyglot durable-execution at Temporal/Zeebe scale. No 200+ AWS-native service catalog. OIDC, Stripe checkout, OTEL, and SCIM are later.",
  },
  {
    id: "temporal",
    name: "Temporal.io",
    category: "Durable execution (code-as-workflow)",
    scores: [1, 0, 1, 1, 1, 1, 1, 2, 1],
    pickWhen:
      "Engineering teams writing Go/TS/Java/Python workflows that must survive crashes with deterministic replay.",
    theyWin:
      "Battle-tested durable execution, multi-language SDKs, automatic retries, proven hyperscale, an official Code Exchange MCP server, and first-party guidance for building durable MCP tools.",
    theyLose:
      "Deterministic-code tax. No separate FSM Law or tenant HumanTask OS. Its MCP surface is primarily operational; bounded legal-event decisions, policy-parity simulation, and hosted LLM governance remain application code.",
  },
  {
    id: "camunda",
    name: "Camunda 8",
    category: "BPMN / DMN process suite",
    scores: [2, 1, 2, 2, 2, 1, 1, 2, 2],
    pickWhen:
      "Enterprises with business analysts standardized on BPMN 2.0, DMN, and a Tasklist portal.",
    theyWin:
      "Industry notation, visual modeler, mature Tasklist and Operate, connector catalog, built-in Orchestration Cluster MCP from 8.9, and process-as-tool MCP exposure in 8.10.",
    theyLose:
      "Verbose BPMN XML is a harder LLM authoring target. Dual-kernel Law and Work remain one BPMN token model. MCP invocation is strong, but it does not provide FlowOS's shared legal-event live/simulation evaluator.",
  },
  {
    id: "aws",
    name: "AWS Step Functions",
    category: "Managed cloud orchestration",
    scores: [2, 1, 1, 1, 2, 1, 1, 2, 2],
    pickWhen:
      "AWS-only shops gluing Lambda, SQS, DynamoDB, and EventBridge with zero servers.",
    theyWin:
      "Zero infra, IAM, 200+ AWS integrations, console visualization, pay-per-transition, and an AWS Labs MCP server that exposes allowlisted state machines as tools.",
    theyLose:
      "AWS lock-in. ASL is a single state machine, not Law+Work. Human wait is callback tokens. MCP uses AWS credentials and allowlists; bounded tenant DecisionPacket policy remains custom.",
  },
  {
    id: "conductor",
    name: "Conductor / Orkes",
    category: "Microservice DAG orchestrator",
    scores: [1, 0, 0, 1, 1, 1, 1, 2, 1],
    pickWhen:
      "High-throughput worker DAGs (fork/join microservices) in the Netflix/Orkes model.",
    theyWin:
      "Visual DAGs, multi-language workers, proven task queues, Conductor MCP for workflow creation/operations, and an MCP Gateway for exposing workflows as tools.",
    theyLose:
      "Polling workers. No decoupled FSM Law. MCP is capable, but legal-event filtering, deterministic policy-parity simulation, inbox fallback, and hosted quota governance are not first-class kernels.",
  },
  {
    id: "n8n",
    name: "n8n / Zapier",
    category: "iPaaS / departmental automation",
    scores: [1, 0, 0, 1, 2, 1, 1, 1, 1],
    pickWhen:
      "Departmental SaaS glue (Slack, Sheets, CRM) without a legal state machine or tenant OS.",
    theyWin:
      "Fast connector catalog, visual recipes, low training cost, built-in instance MCP, and MCP client/server nodes for exposing or consuming tools.",
    theyLose:
      "Not a legal-state process OS. OAuth/API-key MCP exposure is useful, but there is no dual-kernel Law, governed HumanTask fallback, or fail-closed live/simulation agent policy.",
  },
];

export const AI_TASK_AUTOMATION_ROWS: Array<{
  question: string;
  answers: Record<string, string>;
}> = [
  {
    question: "Who hosts decide → commit?",
    answers: {
      flowos: "Native — DecisionPacket → tenant or FlowOS OpenAI → autoCommit or park",
      temporal: "Custom — you write an activity",
      camunda: "Custom — process/connector logic around the model",
      aws: "Custom — Bedrock in a state",
      conductor: "Custom — AI/task workers inside the DAG",
      n8n: "Custom — AI Agent node, not a legal-event kernel",
    },
  },
  {
    question: "Run without the tenant’s LLM key?",
    answers: {
      flowos: "Yes on paid plan — flowos-hosted, daily cap (default 200)",
      temporal: "Only if you wire a key in worker code",
      camunda: "Vendor AI add-ons; not a tenant OS default",
      aws: "Uses the AWS account bill",
      conductor: "Only through configured model/task providers",
      n8n: "Bring a key or n8n Cloud credits",
    },
  },
  {
    question: "Can the model invent a transition?",
    answers: {
      flowos: "No — illegal nextSteps dropped; TimeoutEvent never auto-commits",
      temporal: "Your code must reject it",
      camunda: "Your process must reject it",
      aws: "Your state machine must reject it",
      conductor: "Your worker must reject it",
      n8n: "The recipe can fire any connected action",
    },
  },
  {
    question: "Design-time proof without live tokens?",
    answers: {
      flowos: "simulate_* + AutoCommitEvaluator; no OpenAI call",
      temporal: "Unit tests / time-skipping",
      camunda: "Play / Operate, not a side-effect-free OS sim",
      aws: "State-machine testing, not shared agent-policy simulation",
      conductor: "Workflow test harness",
      n8n: "Manual pin data; still a recipe runner",
    },
  },
];

export const MCP_CONTROL_PLANE_ROWS: Array<{
  capability: string;
  whyItMatters: string;
  answers: Record<string, string>;
}> = [
  {
    capability: "Product MCP surface",
    whyItMatters: "Protocol availability is now table stakes.",
    answers: {
      flowos: "Built in — Streamable HTTP control plane; 79 verified lifecycle tools",
      temporal: "Official Code Exchange operations server plus docs MCP",
      camunda: "Built in — cluster MCP (8.9) and processes MCP (8.10)",
      aws: "AWS Labs Step Functions and Serverless MCP packages",
      conductor: "Conductor MCP server plus Orkes MCP Gateway",
      n8n: "Built-in instance MCP plus client/server nodes",
    },
  },
  {
    capability: "Author through MCP",
    whyItMatters: "Agents need more than start/status operations.",
    answers: {
      flowos: "Draft, update, lint, validate, publish, bind, and simulate",
      temporal: "Workflow code remains SDK-owned; MCP is mainly operational",
      camunda: "Operational MCP; BPMN modeling/deployment remains a separate surface",
      aws: "Serverless MCP can create and update ASL resources",
      conductor: "Create and update workflow definitions",
      n8n: "Build, test, and run workflows from supported clients",
    },
  },
  {
    capability: "Expose each workflow as a tool",
    whyItMatters: "Business capabilities should be easy for agents to invoke.",
    answers: {
      flowos: "Catalog + start_workflow; not yet one dynamic MCP tool per workflow",
      temporal: "Custom thin MCP wrapper starts a workflow",
      camunda: "Native process-as-tool registration with MCP start events",
      aws: "Allowlisted state machines become tools",
      conductor: "Gateway routes map tools to workflows",
      n8n: "Instance MCP or MCP Server Trigger exposes selected workflows",
    },
  },
  {
    capability: "Deterministic dry run",
    whyItMatters: "Safe agents need proof before mutation.",
    answers: {
      flowos: "Native simulate_*; no model call or side effect",
      temporal: "Time-skipping/unit tests, not MCP policy parity",
      camunda: "Play/test facilities, not the same agent commit evaluator",
      aws: "State-machine tests, not bounded-agent policy parity",
      conductor: "Workflow test harness",
      n8n: "Pinned/manual test data; recipe execution model",
    },
  },
  {
    capability: "Shared live/simulation policy",
    whyItMatters: "Dry-run approval must predict live behavior.",
    answers: {
      flowos: "Same legal-event, confidence, timer, commit/park evaluator",
      temporal: "Application code",
      camunda: "Process/connector code",
      aws: "State and IAM policy code",
      conductor: "Worker/task code",
      n8n: "Workflow/node configuration",
    },
  },
  {
    capability: "Law separate from Work",
    whyItMatters: "Agents should not invent business transitions.",
    answers: {
      flowos: "Native dual-kernel; class-backed execution fails closed",
      temporal: "Workflow code is both orchestration and authority",
      camunda: "BPMN token model combines process authority and work",
      aws: "ASL state machine combines both",
      conductor: "DAG/workflow definition combines both",
      n8n: "Recipe graph; no independent legal-state kernel",
    },
  },
  {
    capability: "MCP mutation governance",
    whyItMatters: "Authentication alone does not make agent writes safe.",
    answers: {
      flowos: "Tenant scope, capabilities, risk/side-effect metadata, confirmation, idempotency",
      temporal: "mTLS/API key; business guardrails remain application-owned",
      camunda: "Cluster auth/authorization; operation semantics are tool-specific",
      aws: "IAM plus state-machine/tag allowlists",
      conductor: "Application keys and Gateway auth; business policy is custom",
      n8n: "OAuth/API key and explicit workflow exposure",
    },
  },
  {
    capability: "Durable hosted agent execution",
    whyItMatters: "The LLM decision itself must survive retries safely.",
    answers: {
      flowos: "Postgres jobs, claims, leases, quota, idempotency, audit",
      temporal: "Excellent durability; LLM decision policy is custom",
      camunda: "Durable process jobs; LLM decision policy is custom",
      aws: "Durable executions; LLM call/governance is custom",
      conductor: "Durable tasks; model provider/governance is custom",
      n8n: "Execution persistence; no bounded-agent lease/quota kernel",
    },
  },
  {
    capability: "Agent outcome evidence",
    whyItMatters: "Buyers need more than tool-call logs.",
    answers: {
      flowos: "Runs, commits, parks, failures, quota, overrides, tokens, outcome matching",
      temporal: "Rich workflow history; AI calibration is custom",
      camunda: "Operate/incidents/history; AI outcome metrics are custom",
      aws: "Execution history/CloudWatch; AI outcome metrics are custom",
      conductor: "Execution history; AI outcome metrics are custom",
      n8n: "Execution history; AI calibration and legal outcomes are custom",
    },
  },
];

export const MCP_STRATEGIC_PRIORITIES = [
  {
    priority: "P0",
    title: "Contract truth",
    outcome: "Generate tool counts, schemas, examples, errors, and deprecations from one registry.",
  },
  {
    priority: "P0",
    title: "Simulation → execution proof",
    outcome: "Make dry-run, diff, confirmation, idempotency, and live audit one visible safety chain.",
  },
  {
    priority: "P1",
    title: "Guided tool packs",
    outcome: "Group 79 tools into lifecycle journeys so agents select outcomes, not isolated endpoints.",
  },
  {
    priority: "P1",
    title: "Measured reliability",
    outcome: "Track task success, invalid calls, p95 latency, overrides, and outcome match rate by client.",
  },
] as const;

export const MCP_COMPARISON_SOURCES = [
  {
    label: "Temporal MCP",
    href: "https://temporal.io/code-exchange/temporal-mcp-server",
  },
  {
    label: "Camunda Cluster MCP",
    href: "https://docs.camunda.io/docs/next/apis-tools/orchestration-cluster-api-mcp/orchestration-cluster-api-mcp-overview/",
  },
  {
    label: "AWS Step Functions MCP",
    href: "https://awslabs.github.io/mcp/servers/stepfunctions-tool-mcp-server",
  },
  {
    label: "Orkes MCP Gateway",
    href: "https://orkes.io/content/developer-guides/mcp-gateway",
  },
  {
    label: "n8n MCP",
    href: "https://docs.n8n.io/connect/connect-to-n8n-mcp-server",
  },
] as const;

export const COMPARISON_LOSSES: Array<{ need: string; buy: string; why: string }> = [
  {
    need: "Polyglot durable code + replay",
    buy: "Temporal",
    why: "Workflows are real programs; FlowOS is JSON + dual-kernel, not a worker SDK.",
  },
  {
    need: "BPMN 2.0 / DMN + analyst Tasklist",
    buy: "Camunda 8",
    why: "Camunda is the notation standard; FlowOS inbox is role-filtered REST, not Camunda Tasklist.",
  },
  {
    need: "Zero-ops AWS service mesh",
    buy: "Step Functions",
    why: "IAM + 200 AWS integrations; FlowOS bindings are tenant plugins, not an AWS catalog.",
  },
  {
    need: "Hyperscale worker DAGs",
    buy: "Conductor / Orkes",
    why: "Polling task queues at Netflix scale; FlowOS is a tenant process OS, not a DAG fabric.",
  },
  {
    need: "Departmental SaaS recipes",
    buy: "n8n / Zapier",
    why: "Connector speed; they are not legal-state operating systems.",
  },
];

export const fitLabel = (score: FitScore): string =>
  score === 2 ? "Native" : score === 1 ? "Custom" : "—";

export const fitClass = (score: FitScore, isFlowos: boolean): string => {
  if (score === 2) return isFlowos ? "text-emerald-400 font-bold" : "text-emerald-300";
  if (score === 1) return "text-amber-300";
  return "text-slate-500";
};
