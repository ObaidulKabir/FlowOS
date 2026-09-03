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
  maskedKey: string;
  keyPrefix: string;
  createdAt: string;
  lastUsedAt?: string;
  isRevoked: boolean;
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

