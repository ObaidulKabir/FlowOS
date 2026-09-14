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
  apiKey: string;
  maskedKey: string;
  createdAt: string;
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
  mode?: 'create' | 'refine';
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
}

export interface TimeTravelReplay {
  workflowInstanceId: string;
  workflowClassName: string;
  workflowVersion: number;
  status: string;
  totalSteps: number;
  snapshots: TimeTravelSnapshot[];
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
}


