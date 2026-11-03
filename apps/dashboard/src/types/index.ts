export enum WorkflowClassScope {
  Private = 0,
  Shared = 1,
  Public = 2
}

export enum WorkflowClassStatus {
  Draft = 0,
  Published = 1,
  Shared = 2,
  Public = 3,
  Deprecated = 4,
  Abandoned = 5
}

export interface ValidationError {
  code: string;
  category: string;
  message: string;
  element: string;
}

export interface ValidationResult {
  isValid: boolean;
  errors: ValidationError[];
}

export interface WorkflowClass {
  id: string;
  tenantId: string;
  name: string;
  description?: string;
  version: string;
  scope: WorkflowClassScope;
  status: WorkflowClassStatus;
  createdAt: string;
  publishedAt?: string;
  previousVersionId?: string;
  flowOsVersion?: string;
  flowOsCompatibility?: string;
  definition: any; // We can type this strictly later if needed, for now 'any' allows rendering JSON
}

export interface CreateDraftRequest {
  name: string;
  description?: string;
  version: string;
  definition: any;
}

export interface CopyRequest {
  newTenantId: string;
}

export interface WorkflowInstance {
  id?: string;
  tenantId?: string;
  workflowId?: string;
  workflowClassId?: string;
  workflowClassName?: string;
  correlationId?: string;
  currentStep?: string;
  currentStepId?: string;
  currentState?: string;
  status: any;
  createdAt: string;
  completedAt?: string;
}

export interface AgentLatencyMetrics {
  samples: number;
  averageMs?: number;
  p50Ms?: number;
  p95Ms?: number;
}

export interface AgentTokenMetrics {
  inputTokens: number;
  outputTokens: number;
}

export interface AgentConfidenceBin {
  label: string;
  lowerBound: number;
  upperBound: number;
  upperBoundInclusive: boolean;
  samples: number;
  matchedOutcomes: number;
  meanConfidence?: number;
  observedMatchRate?: number;
  brierScore?: number;
}

export interface AgentConfidenceCalibration {
  samples: number;
  brierScore?: number;
  bins: AgentConfidenceBin[];
}

export interface AgentProviderModelMetrics {
  providerAlias?: string;
  providerName?: string;
  model?: string;
  runs: number;
  succeededRuns: number;
  failedRuns: number;
  commits: number;
  parks: number;
  hostedQuotaDenials: number;
  overrides: number;
  suggestions: number;
  evaluatedOutcomes: number;
  matchedOutcomes: number;
  latency: AgentLatencyMetrics;
  tokens: AgentTokenMetrics;
}

export interface AgentEvaluationMetrics {
  fromUtc: string;
  toUtc: string;
  runs: number;
  succeededRuns: number;
  failedRuns: number;
  cancelledRuns: number;
  runningRuns: number;
  commits: number;
  parks: number;
  hostedQuotaDenials: number;
  overrides: number;
  suggestions: number;
  evaluatedOutcomes: number;
  unevaluatedOutcomes: number;
  matchedOutcomes: number;
  unmatchedOutcomes: number;
  latency: AgentLatencyMetrics;
  tokens: AgentTokenMetrics;
  confidenceCalibration: AgentConfidenceCalibration;
  providerModelBreakdown: AgentProviderModelMetrics[];
}

export interface TenantApiKeyDto {
  id: string;
  name: string;
  applicationName?: string;
  environment?: string;
  scopes?: string[];
  maskedKey: string;
  keyPrefix: string;
  createdAt: string;
  expiresAt?: string;
  lastUsedAt?: string;
  isRevoked: boolean;
  isExpired?: boolean;
  isActive?: boolean;
}

export interface PlanRequestDto {
  requestedPlan: string;
  commercialTier?: string;
  paymentReference?: string;
  billingContactEmail?: string;
  requestedAt: string;
  notes?: string;
  status: 'Pending' | 'Approved' | 'Rejected';
  reviewedAt?: string;
  reviewedBy?: string;
  adminNotes?: string;
}

export interface TenantPlanRequestItem {
  tenantId: string;
  tenantName: string;
  currentPlan: string;
  currentBillingStatus: string;
  canRunRuntime: boolean;
  planRequest: PlanRequestDto;
}

export interface TenantDto {
  tenantId: string;
  name: string;
  status: string;
  plan?: string;
  billingStatus?: string;
  canRunRuntime?: boolean;
  createdAt: string;
  planRequest?: PlanRequestDto;
  keyCount: number;
  keys: TenantApiKeyDto[];
}

export interface RegisterTenantResponse {
  tenant: TenantDto;
  apiKey: string;
}

export interface CreateKeyResponse {
  id: string;
  tenantId: string;
  name: string;
  applicationName: string;
  environment: string;
  scopes: string[];
  apiKey: string;
  maskedKey: string;
  createdAt: string;
  expiresAt?: string;
}

export interface TenantRoleDto {
  id: string;
  name: string;
  capabilities: string[];
}

export interface PublishedEventDto {
  eventId: string;
  tenantId: string;
  eventType: string;
  correlationId?: string;
  timestamp: string;
  metadata: Record<string, string>;
  payloadJson?: string;
}

export interface AuthSession {
  role: 'Admin' | 'Tenant';
  tenantId: string;
  tenantName: string;
  apiKey?: string;
  username?: string;
  token?: string;
  isSandbox?: boolean;
  email?: string;
  isEmailVerified?: boolean;
  plan?: string;
  billingStatus?: string;
  canRunRuntime?: boolean;
}

export interface RegisterTenantUserRequest {
  tenantName: string;
  email: string;
  password: string;
  fullName?: string;
}

export interface RegisterTenantUserResponse {
  ok: boolean;
  message: string;
  tenantId?: string;
  tenantName?: string;
  email?: string;
  isEmailVerified?: boolean;
  verificationToken?: string;
}

export interface VerifyEmailRequest {
  email: string;
  token: string;
}

export interface VerifyEmailWithPasswordRequest {
  email: string;
  password: string;
}

export interface VerifyEmailResponse {
  ok: boolean;
  message: string;
  tenantId?: string;
  email?: string;
  isEmailVerified?: boolean;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface TenantUserDto {
  id: string;
  email: string;
  fullName: string;
  role: string;
  tenantId: string;
  tenantName: string;
  isEmailVerified: boolean;
  createdAt: string;
  lastLoginAt?: string;
  plan?: string;
  billingStatus?: string;
  canRunRuntime?: boolean;
}

export interface LoginResponse {
  ok: boolean;
  token?: string;
  tokenType?: string;
  expiresIn?: number;
  user?: TenantUserDto;
  errorCode?: string;
  message?: string;
}

export interface ResendVerificationResponse {
  ok: boolean;
  message: string;
  verificationToken?: string;
}


export interface DeadLetterDto {
  id: string;
  tenantId: string;
  type: string;
  payload: string;
  occurredOnUtc: string;
  processedOnUtc?: string;
  error?: string;
  retryCount: number;
  maxRetries: number;
  nextRetryUtc?: string;
  isDeadLetter: boolean;
  actionType?: string;
  targetUrl?: string;
  httpMethod?: string;
  stepId?: string;
}

export interface GenerateBlueprintCopilotRequest {
  prompt: string;
  currentBlueprint?: any;
  mode?: 'create' | 'refine' | 'template';
}

export interface WorkflowContextBindingDefinition {
  entityType: string;
  eventAliases: Record<string, string>;
  roleOverrides: Record<string, string>;
  capabilityOverrides: Record<string, string>;
  roleStaticMemberOverrides?: Record<string, string[]>;
  inputMapping: Record<string, string>;
  eventInputMappings: Record<string, Record<string, string>>;
  conditionParameters: Record<string, unknown>;
  decisionProviderOverrides: Record<string, string>;
  sourcePayloadSchema?: string;
  eventSourcePayloadSchemas: Record<string, string>;
  metadata: Record<string, string>;
}

export interface WorkflowContextBindingRevision {
  id: string;
  revision: number;
  sourceWorkflowClassId: string;
  sourceWorkflowClassVersion: string;
  status: string;
  definition: WorkflowContextBindingDefinition;
  workflowDefinitionId?: string;
  stateMachineDefinitionId?: string;
  contentHash?: string;
  createdAtUtc: string;
  activatedAtUtc?: string;
  supersededAtUtc?: string;
  flowOsVersion?: string;
}

export interface WorkflowContextBinding {
  id: string;
  tenantId: string;
  contextType: string;
  name: string;
  status: string;
  activeRevisionId?: string;
  draftRevisionId?: string;
  activeRevision?: WorkflowContextBindingRevision;
  draftRevision?: WorkflowContextBindingRevision;
  createdAtUtc: string;
  updatedAtUtc: string;
  archivedAtUtc?: string;
}

export interface CreateContextBindingRequest {
  sourceWorkflowClassId: string;
  contextType: string;
  name: string;
  definition: WorkflowContextBindingDefinition;
}

export interface WorkflowContextSimulationEventRequest {
  eventType: string;
  payload?: Record<string, unknown>;
  roles?: string[];
}

export interface WorkflowContextSimulationRequest {
  contextBindingId?: string;
  contextType?: string;
  revision: 'draft' | 'active';
  initialPayload?: Record<string, unknown>;
  roles?: string[];
  events?: WorkflowContextSimulationEventRequest[];
  maxSteps?: number;
}

export interface WorkflowContextSimulationProjectionItem {
  canonicalField: string;
  sourcePath?: string;
  value?: unknown;
  origin: string;
  wasResolved: boolean;
}

export interface WorkflowContextSimulationAction {
  stepId: string;
  phase: string;
  actionType: string;
  target?: string;
  capability?: string;
  condition?: string;
}

export interface WorkflowContextSimulationPendingWork {
  kind: string;
  stepId: string;
  triggerEvent?: string;
  description: string;
}

export interface WorkflowContextSimulationTrace {
  index: number;
  eventType: string;
  canonicalEventType?: string;
  roles: string[];
  fromStepId: string;
  toStepId: string;
  activeStepIds: string[];
  fromState: string;
  toState: string;
  isAllowed: boolean;
  outcome: string;
  reason?: string;
  sourcePayload: Record<string, unknown>;
  canonicalDelta: Record<string, unknown>;
  contextBefore: Record<string, unknown>;
  contextAfter: Record<string, unknown>;
  plannedActions: WorkflowContextSimulationAction[];
  pendingWork: WorkflowContextSimulationPendingWork[];
}

export interface WorkflowContextSimulationResult {
  contextBindingId: string;
  contextType: string;
  bindingName: string;
  revisionKind: 'draft' | 'active';
  contextBindingRevisionId: string;
  revision: number;
  sourceWorkflowClassId: string;
  sourceWorkflowClassVersion: string;
  isPersistedRuntime: boolean;
  sideEffectsSuppressed: boolean;
  status: string;
  initialStepId: string;
  currentStepId: string;
  activeStepIds: string[];
  initialState: string;
  currentState: string;
  availableRoles: string[];
  eventAliases: Record<string, string>;
  initialProjection: WorkflowContextSimulationProjectionItem[];
  initialCanonicalContext: Record<string, unknown>;
  finalCanonicalContext: Record<string, unknown>;
  trace: WorkflowContextSimulationTrace[];
  pendingWork: WorkflowContextSimulationPendingWork[];
  graph: {
    workflow: {
      name: string;
      version: number;
      startStepId: string;
      steps: any[];
    };
    stateMachine: {
      entityType: string;
      version: number;
      initialState: string;
      states: string[];
      transitions: any[];
    };
  };
}

export interface GenerateBlueprintCopilotResponse {
  suggestedName: string;
  suggestedVersion: string;
  summary: string;
  explanation: string;
  blueprint: any;
  validation: ValidationResult;
}

export interface TimeTravelActionLog {
  id: string;
  stepId: string;
  triggerPhase: string;
  actionType: string;
  target?: string;
  status: string;
  executedAtUtc: string;
  durationMs: number;
  httpStatusCode?: number;
  errorMessage?: string;
}

export interface TimeTravelSnapshot {
  stepIndex: number;
  timestamp: string;
  eventId: string;
  eventType: string;
  fromStepId: string;
  toStepId: string;
  activeStepIds: string[];
  fromState: string;
  toState: string;
  actorId?: string;
  variables: Record<string, unknown>;
  actionLogs: TimeTravelActionLog[];
  summary: string;
  contextualEventType?: string;
  canonicalEventType?: string;
  contextBindingRevisionId?: string;
}

export interface TimeTravelReplay {
  workflowInstanceId: string;
  workflowClassName: string;
  workflowVersion: number;
  status: string;
  totalSteps: number;
  snapshots: TimeTravelSnapshot[];
  contextBindingId?: string;
  contextBindingRevisionId?: string;
  contextType?: string;
  sourceSystem?: string;
  externalEntityId?: string;
}

export interface TimeTravelForkResult {
  forkFromStepIndex: number;
  baseStepId: string;
  baseState: string;
  alternativeEvent: string;
  projectedStepId: string;
  projectedState: string;
  isAllowed: boolean;
  reason?: string;
  projectedActions: string[];
  simulatedRoles?: string[];
  contextualEventType?: string;
  canonicalEventType?: string;
  contextBindingId?: string;
  contextBindingRevisionId?: string;
  contextType?: string;
  projectedCanonicalContext?: Record<string, unknown>;
}


// ─────────────────────────────────────────────────────────────────────────────
// AgentWorkflowClass — declarative multi-agent coordination plans (ADR-006)
//
// Mirrors src/FlowOS.Domain/Blueprints/AgentWorkflowBlueprint.cs. Status and
// scope reuse the WorkflowClassStatus / WorkflowClassScope enums above, because
// the C# entity uses those same enums and the API serialises them as numbers.
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Node kind. Declared rather than inferred: `branches` is meaningful only on a
 * Fork and `inboundSteps` only on a Join, and the API rejects either against the
 * wrong stepType. Omitted in stored JSON means 'Agent'.
 */
export type AgentStepType = 'Agent' | 'Fork' | 'Join' | 'End';

export type AgentJoinPolicy = 'WaitAll' | 'WaitAny';

/** 'Compensate' is accepted but inert until the Phase 4B engine ships. */
export type AgentRollbackStrategy = 'Abandon' | 'Compensate';

export interface AgentStepAutoCommit {
  /** Minimum agent confidence to advance without human review, in (0, 1]. */
  minConfidence: number;
  /** Must each be a `nextSteps` key, and never the SLA `timeoutEvent`. */
  allowedEvents: string[];
}

export interface AgentStepSla {
  /** ISO-8601 duration such as 'PT4H'; the shorthand '30m' / '4h' / '2d' is also accepted. */
  duration: string;
  timeoutEvent: string;
  escalationStepId?: string;
  escalationRole?: string;
  isInterrupting: boolean;
}

export interface AgentPathTravelLimit {
  /** Times this edge may be travelled, including the first pass. At least 1. */
  maxTravels: number;
  /** Step id or 'END' taken when the cap is exceeded. */
  onExceeded?: string;
}

export interface AgentStepBlueprint {
  stepId: string;
  stepType: AgentStepType;
  description: string;
  /** Static team binding. An 'Agent' step needs this or `teamRole`. */
  agentTeamId?: string;
  /** Dynamic binding the orchestrator resolves at dispatch. */
  teamRole?: string;
  /** Outcome event -> next step id. Route to 'END' to finish the plan. */
  nextSteps: Record<string, string>;
  /** Fork only: step ids to run concurrently. At least 2. */
  branches: string[];
  /** Join only. */
  joinPolicy: AgentJoinPolicy;
  /** Join only: steps that must converge here. At least 2. */
  inboundSteps: string[];
  maxAutonomyLevel?: string;
  /** Irreversible external change: an agent may propose, a person must decide. */
  consequential: boolean;
  autoCommit?: AgentStepAutoCommit;
  sla?: AgentStepSla;
  agentPrompt?: string;
  agentTools: string[];
  /** Keyed by a `nextSteps` event. */
  pathLimits: Record<string, AgentPathTravelLimit>;
}

export interface AgentCoordinationRules {
  maxGlobalRetries: number;
  rollbackStrategy: AgentRollbackStrategy;
  /** ISO-8601 duration; absent means no global deadline. */
  globalSla?: string;
  /** 0 means unlimited. */
  maxParallelSteps: number;
}

export interface AgentWorkflowBlueprint {
  objective: string;
  startStepId: string;
  /** JSON Schema for the shared working memory, as a string. */
  contextSchema?: string;
  coordinationRules: AgentCoordinationRules;
  steps: AgentStepBlueprint[];
}

export interface AgentWorkflowClass {
  id: string;
  tenantId: string;
  name: string;
  version: string;
  status: WorkflowClassStatus;
  scope: WorkflowClassScope;
  createdAt: string;
  publishedAt?: string;
  previousVersionId?: string;
  changeLog?: string;
  deprecationReason?: string;
  deprecationMigrationTargetId?: string;
  flowOsVersion: string;
  /** Immutable once published; create a new version to change it. */
  definition: AgentWorkflowBlueprint;
}

/** Body for create and draft update. `previousVersionId` starts a new version. */
export interface CreateAgentWorkflowClassRequest {
  name: string;
  version: string;
  definition: AgentWorkflowBlueprint;
  previousVersionId?: string;
  changeLog?: string;
}

export interface DeprecateAgentWorkflowClassRequest {
  reason?: string;
  migrationTargetId?: string;
}
