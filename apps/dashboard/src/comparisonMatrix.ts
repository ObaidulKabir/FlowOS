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
      "Battle-tested durable execution, multi-language SDKs, automatic retries, proven hyperscale.",
    theyLose:
      "Deterministic-code tax. No separate FSM Law. No native HumanTask OS. No MCP authoring/simulation surface. AI is an activity you write, not a hosted DecisionPacket / autoCommit loop. Heavy server + Elasticsearch footprint.",
  },
  {
    id: "camunda",
    name: "Camunda 8",
    category: "BPMN / DMN process suite",
    scores: [2, 1, 2, 2, 2, 1, 1, 2, 2],
    pickWhen:
      "Enterprises with business analysts standardized on BPMN 2.0, DMN, and a Tasklist portal.",
    theyWin:
      "Industry notation, visual modeler, mature Tasklist, Operate, Keycloak identity, connector catalog.",
    theyLose:
      "Verbose BPMN XML is a poor LLM authoring target. Cluster sprawl. Dual-kernel purity is mixed into one BPMN token graph. Copilot/connectors are not a legal-event autoCommit kernel.",
  },
  {
    id: "aws",
    name: "AWS Step Functions",
    category: "Managed cloud orchestration",
    scores: [2, 1, 1, 1, 2, 1, 1, 2, 2],
    pickWhen:
      "AWS-only shops gluing Lambda, SQS, DynamoDB, and EventBridge with zero servers.",
    theyWin:
      "Zero infra, IAM, 200+ AWS integrations, console visualization, pay-per-transition.",
    theyLose:
      "AWS lock-in. ASL is a single state machine, not Law+Work. Human wait is callback tokens. Bedrock in a state is not a tenant MCP DecisionPacket loop.",
  },
  {
    id: "conductor",
    name: "Conductor / Orkes",
    category: "Microservice DAG orchestrator",
    scores: [1, 0, 0, 1, 1, 0, 1, 2, 1],
    pickWhen:
      "High-throughput worker DAGs (fork/join microservices) in the Netflix/Orkes model.",
    theyWin:
      "Visual DAGs, multi-language workers, proven Netflix-scale task queues.",
    theyLose:
      "Polling workers. No decoupled FSM Law. No MCP. Inbox and policy are not first-class OS kernels.",
  },
  {
    id: "n8n",
    name: "n8n / Zapier",
    category: "iPaaS / departmental automation",
    scores: [1, 0, 0, 1, 2, 1, 1, 1, 1],
    pickWhen:
      "Departmental SaaS glue (Slack, Sheets, CRM) without a legal state machine or tenant OS.",
    theyWin: "Fast connector catalog, visual recipes, low training cost.",
    theyLose:
      "Not a process OS. Weak isolation, no dual-kernel Law, no governed HumanTask inbox, no fail-closed legal simulation. AI nodes call HTTP; they do not restrict to legal nextSteps or auto-commit under Law.",
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
      camunda: "Custom — Copilot / connectors",
      aws: "Custom — Bedrock in a state",
      conductor: "—",
      n8n: "Custom — AI node, not legal nextSteps",
    },
  },
  {
    question: "Run without the tenant’s LLM key?",
    answers: {
      flowos: "Yes on paid plan — flowos-hosted, daily cap (default 200)",
      temporal: "Only if you wire a key in worker code",
      camunda: "Vendor AI add-ons; not a tenant OS default",
      aws: "Uses the AWS account bill",
      conductor: "—",
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
      aws: "Express/Standard test executions still bill",
      conductor: "Workflow test harness",
      n8n: "Manual pin data; still a recipe runner",
    },
  },
];

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
