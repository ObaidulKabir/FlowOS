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
  version: string;
  scope: WorkflowClassScope;
  status: WorkflowClassStatus;
  createdAt: string;
  publishedAt?: string;
  previousVersionId?: string;
  definition: any; // We can type this strictly later if needed, for now 'any' allows rendering JSON
}

export interface CreateDraftRequest {
  name: string;
  version: string;
  definition: any;
}

export interface CopyRequest {
  newTenantId: string;
}

export interface WorkflowInstance {
  id?: string;
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

export interface TenantDto {
  tenantId: string;
  name: string;
  status: string;
  plan?: string;
  billingStatus?: string;
  canRunRuntime?: boolean;
  createdAt: string;
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


