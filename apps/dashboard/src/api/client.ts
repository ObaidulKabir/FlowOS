import { 
  WorkflowClass, CreateDraftRequest, CopyRequest, ValidationResult, 
  WorkflowClassScope, WorkflowClassStatus, WorkflowInstance, AuthSession, DeadLetterDto,
  TimeTravelReplay, TimeTravelForkResult,
  RegisterTenantUserRequest, RegisterTenantUserResponse,
  VerifyEmailRequest, VerifyEmailResponse,
  LoginRequest, LoginResponse,
  ResendVerificationResponse, TenantUserDto
} from '../types';

const API_BASE = '/api/workflow-classes';
const AUTH_STORAGE_KEY = 'flowos_auth_session';

export const getDefaultSandboxSession = (): AuthSession => ({
  role: 'Tenant',
  tenantId: '22222222-2222-2222-2222-222222222222',
  tenantName: 'Demo Client Tenant (Sandbox)',
  apiKey: 'flowos_prod_secret_key_32_chars_min',
  username: 'demo-tenant-user',
  isSandbox: true,
  isEmailVerified: true
});

export const getDefaultSession = (): AuthSession => getDefaultSandboxSession();

export const getStoredSession = (): AuthSession | null => {
  try {
    const raw = localStorage.getItem(AUTH_STORAGE_KEY);
    if (raw) {
      return JSON.parse(raw);
    }
  } catch (e) {
    console.error('Failed to parse auth session from localStorage', e);
  }
  return null;
};

export const getAuthSession = (): AuthSession => {
  return getStoredSession() || getDefaultSandboxSession();
};

export const setAuthSession = (session: AuthSession) => {
  localStorage.setItem(AUTH_STORAGE_KEY, JSON.stringify(session));
};

export const clearAuthSession = () => {
  localStorage.removeItem(AUTH_STORAGE_KEY);
};

export const getActiveTenantId = (): string => {
  return getAuthSession().tenantId;
};

export const setActiveTenantId = (id: string, name?: string) => {
  const current = getAuthSession();
  const updated: AuthSession = {
    ...current,
    tenantId: id,
    tenantName: name || current.tenantName
  };
  setAuthSession(updated);
};

export const getHeaders = (roleOverride?: 'Tenant' | 'Admin') => {
  const session = getAuthSession();
  const role = roleOverride || session.role;
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    'x-tenant-id': session.tenantId,
    'X-Mock-Role': role,
    'X-Mock-UserId': session.username || (role === 'Admin' ? 'superadmin' : 'tenant-user')
  };

  if (session.token) {
    headers['Authorization'] = `Bearer ${session.token}`;
  }

  if (session.apiKey) {
    headers['X-API-Key'] = session.apiKey;
  }

  return headers;
};

const handleResponse = async (response: Response, errorMessage: string) => {
  if (!response.ok) {
    let errorDetails = ` (HTTP ${response.status} ${response.statusText})`;
    try {
      const errorBody = await response.text();
      if (errorBody) {
        try {
          const errorJson = JSON.parse(errorBody);
          const detail = errorJson.detail || errorJson.error || errorJson.title || (errorJson.errors ? JSON.stringify(errorJson.errors) : errorBody);
          errorDetails += `: ${detail}`;
        } catch {
          errorDetails += `: ${errorBody}`;
        }
      }
    } catch {
      // Ignore parsing error, use status
    }
    throw new Error(`${errorMessage}${errorDetails}`);
  }
  return response.json();
};

export const api = {
  list: async (scope?: WorkflowClassScope, status?: WorkflowClassStatus, role?: 'Tenant' | 'Admin'): Promise<WorkflowClass[]> => {
    const headers = getHeaders(role);
    const tenantId = getActiveTenantId();
    const params = new URLSearchParams();
    params.append('tenantId', tenantId);
    if (scope !== undefined) {
      const scopeName = WorkflowClassScope[scope] || scope.toString();
      params.append('scope', scopeName);
    }
    if (status !== undefined) {
      const statusName = WorkflowClassStatus[status] || status.toString();
      params.append('status', statusName);
    }
    
    const response = await fetch(`${API_BASE}?${params.toString()}`, { headers });
    return handleResponse(response, 'Failed to list workflow classes');
  },

  get: async (id: string, role?: 'Tenant' | 'Admin'): Promise<WorkflowClass> => {
    const headers = getHeaders(role);
    const tenantId = getActiveTenantId();
    const response = await fetch(`${API_BASE}/${id}?tenantId=${tenantId}`, { headers });
    return handleResponse(response, 'Failed to get workflow class');
  },

  createDraft: async (req: CreateDraftRequest, role?: 'Tenant' | 'Admin'): Promise<WorkflowClass> => {
    const headers = getHeaders(role);
    const tenantId = getActiveTenantId();
    const response = await fetch(`${API_BASE}?tenantId=${tenantId}`, {
      method: 'POST',
      headers,
      body: JSON.stringify(req)
    });
    return handleResponse(response, 'Failed to create draft');
  },

  updateDraft: async (id: string, req: CreateDraftRequest, role?: 'Tenant' | 'Admin'): Promise<WorkflowClass> => {
    const headers = getHeaders(role);
    const tenantId = getActiveTenantId();
    const response = await fetch(`${API_BASE}/${id}?tenantId=${tenantId}`, {
      method: 'PUT',
      headers,
      body: JSON.stringify(req)
    });
    return handleResponse(response, 'Failed to update draft');
  },

  validate: async (id: string, role?: 'Tenant' | 'Admin'): Promise<ValidationResult> => {
    const headers = getHeaders(role);
    const tenantId = getActiveTenantId();
    const response = await fetch(`${API_BASE}/${id}/validate?tenantId=${tenantId}`, { method: 'POST', headers });
    return handleResponse(response, 'Failed to validate');
  },

  publish: async (id: string, role?: 'Tenant' | 'Admin'): Promise<WorkflowClass> => {
    const headers = getHeaders(role);
    const tenantId = getActiveTenantId();
    const response = await fetch(`${API_BASE}/${id}/publish?tenantId=${tenantId}`, { method: 'POST', headers });
    return handleResponse(response, 'Failed to publish');
  },

  submit: async (id: string, role?: 'Tenant' | 'Admin'): Promise<WorkflowClass> => {
    const headers = getHeaders(role);
    const tenantId = getActiveTenantId();
    const response = await fetch(`${API_BASE}/${id}/submit?tenantId=${tenantId}`, { method: 'POST', headers });
    return handleResponse(response, 'Failed to submit');
  },

  withdraw: async (id: string, role?: 'Tenant' | 'Admin'): Promise<WorkflowClass> => {
    const headers = getHeaders(role);
    const tenantId = getActiveTenantId();
    const response = await fetch(`${API_BASE}/${id}/withdraw?tenantId=${tenantId}`, { method: 'POST', headers });
    return handleResponse(response, 'Failed to withdraw');
  },

  deprecate: async (id: string, role?: 'Tenant' | 'Admin'): Promise<WorkflowClass> => {
    const headers = getHeaders(role);
    const tenantId = getActiveTenantId();
    const response = await fetch(`${API_BASE}/${id}/deprecate?tenantId=${tenantId}`, { method: 'POST', headers });
    return handleResponse(response, 'Failed to deprecate');
  },

  abandon: async (id: string, role?: 'Tenant' | 'Admin'): Promise<WorkflowClass> => {
    const headers = getHeaders(role);
    const tenantId = getActiveTenantId();
    const response = await fetch(`${API_BASE}/${id}/abandon?tenantId=${tenantId}`, { method: 'POST', headers });
    return handleResponse(response, 'Failed to abandon');
  },

  approve: async (id: string): Promise<WorkflowClass> => {
    const headers = getHeaders('Admin');
    const tenantId = getActiveTenantId();
    const response = await fetch(`${API_BASE}/${id}/approve?tenantId=${tenantId}`, { method: 'POST', headers });
    return handleResponse(response, 'Failed to approve');
  },

  newVersion: async (id: string, role?: 'Tenant' | 'Admin'): Promise<WorkflowClass> => {
    const headers = getHeaders(role);
    const tenantId = getActiveTenantId();
    const response = await fetch(`${API_BASE}/${id}/new-version?tenantId=${tenantId}`, { method: 'POST', headers });
    return handleResponse(response, 'Failed to create new version');
  },

  delete: async (id: string, role?: 'Tenant' | 'Admin'): Promise<void> => {
    const headers = getHeaders(role);
    const tenantId = getActiveTenantId();
    const response = await fetch(`${API_BASE}/${id}?tenantId=${tenantId}`, { method: 'DELETE', headers });
    return handleResponse(response, 'Failed to delete');
  },

  copy: async (id: string, req: CopyRequest, role?: 'Tenant' | 'Admin'): Promise<WorkflowClass> => {
    const headers = getHeaders(role);
    const tenantId = getActiveTenantId();
    const response = await fetch(`${API_BASE}/${id}/copy?tenantId=${tenantId}`, { 
      method: 'POST', 
      headers,
      body: JSON.stringify(req)
    });
    return handleResponse(response, 'Failed to copy');
  },

  listInstances: async (role?: 'Tenant' | 'Admin'): Promise<WorkflowInstance[]> => {
    const headers = getHeaders(role);
    const tenantId = getActiveTenantId();
    const response = await fetch(`/api/workflows?tenantId=${tenantId}`, { headers });
    return handleResponse(response, 'Failed to list workflow instances');
  },

  startInstance: async (workflowName: string, version?: number, correlationId?: string, role?: 'Tenant' | 'Admin'): Promise<any> => {
    const headers = getHeaders(role);
    const tenantId = getActiveTenantId();
    const response = await fetch('/api/workflows/start', {
      method: 'POST',
      headers,
      body: JSON.stringify({
        tenantId,
        workflowName,
        version,
        correlationId
      })
    });
    return handleResponse(response, 'Failed to start workflow instance');
  },

  listEvents: async (workflowInstanceId?: string, limit: number = 50, role?: 'Tenant' | 'Admin'): Promise<any[]> => {
    const headers = getHeaders(role);
    const params = new URLSearchParams();
    if (workflowInstanceId) params.append('workflowInstanceId', workflowInstanceId);
    params.append('limit', limit.toString());
    const response = await fetch(`/api/events?${params.toString()}`, { headers });
    return handleResponse(response, 'Failed to list events');
  },

  getWorkflowAudit: async (instanceId: string, role?: 'Tenant' | 'Admin'): Promise<any> => {
    const headers = getHeaders(role);
    const response = await fetch(`/api/workflows/${instanceId}/audit`, { headers });
    return handleResponse(response, 'Failed to get workflow audit history');
  },

  listTenants: async (): Promise<any[]> => {
    const headers = getHeaders('Admin');
    const response = await fetch('/api/tenants', { headers });
    return handleResponse(response, 'Failed to list tenants');
  },

  registerTenant: async (
    name: string, 
    keyName?: string, 
    applicationName?: string, 
    environment?: string, 
    scopes?: string[], 
    expiresInDays?: number
  ): Promise<{ tenant: any; apiKey: string }> => {
    const headers = getHeaders('Admin');
    const response = await fetch('/api/tenants', {
      method: 'POST',
      headers,
      body: JSON.stringify({ name, keyName, applicationName, environment, scopes, expiresInDays })
    });
    return handleResponse(response, 'Failed to register tenant');
  },

  listTenantKeys: async (tenantId: string): Promise<any[]> => {
    const headers = getHeaders();
    const response = await fetch(`/api/tenants/${tenantId}/keys`, { headers });
    return handleResponse(response, 'Failed to list tenant keys');
  },

  generateTenantKey: async (
    tenantId: string, 
    name?: string, 
    applicationName?: string, 
    environment?: string, 
    scopes?: string[], 
    expiresInDays?: number
  ): Promise<any> => {
    const headers = getHeaders();
    const response = await fetch(`/api/tenants/${tenantId}/keys`, {
      method: 'POST',
      headers,
      body: JSON.stringify({ 
        name: name || 'API Key',
        applicationName: applicationName || 'Default Application',
        environment: environment || 'Production',
        scopes: scopes || ['*'],
        expiresInDays
      })
    });
    return handleResponse(response, 'Failed to generate API key');
  },

  revokeTenantKey: async (tenantId: string, keyId: string): Promise<void> => {
    const headers = getHeaders();
    const response = await fetch(`/api/tenants/${tenantId}/keys/${keyId}`, {
      method: 'DELETE',
      headers
    });
    if (!response.ok) {
      throw new Error(`Failed to revoke API key (HTTP ${response.status})`);
    }
  },

  listDeadLetters: async (tenantId?: string, type?: string, role?: 'Tenant' | 'Admin'): Promise<DeadLetterDto[]> => {
    const headers = getHeaders(role);
    const params = new URLSearchParams();
    if (tenantId) params.append('tenantId', tenantId);
    if (type) params.append('type', type);
    const query = params.toString() ? `?${params.toString()}` : '';
    const response = await fetch(`/api/dead-letters${query}`, { headers });
    return handleResponse(response, 'Failed to list dead letters');
  },

  retryDeadLetter: async (id: string, role?: 'Tenant' | 'Admin'): Promise<{ success: boolean; message: string }> => {
    const headers = getHeaders(role);
    const response = await fetch(`/api/dead-letters/${id}/retry`, {
      method: 'POST',
      headers
    });
    return handleResponse(response, 'Failed to retry dead letter');
  },

  retryAllDeadLetters: async (type?: string, role?: 'Tenant' | 'Admin'): Promise<{ success: boolean; replayedCount: number }> => {
    const headers = getHeaders(role);
    const params = new URLSearchParams();
    if (type) params.append('type', type);
    const query = params.toString() ? `?${params.toString()}` : '';
    const response = await fetch(`/api/dead-letters/retry-all${query}`, {
      method: 'POST',
      headers
    });
    return handleResponse(response, 'Failed to retry all dead letters');
  },

  purgeDeadLetter: async (id: string, role?: 'Tenant' | 'Admin'): Promise<{ success: boolean; message: string }> => {
    const headers = getHeaders(role);
    const response = await fetch(`/api/dead-letters/${id}`, {
      method: 'DELETE',
      headers
    });
    return handleResponse(response, 'Failed to purge dead letter');
  },

  copilotGenerate: async (
    prompt: string,
    currentBlueprint?: any,
    mode: 'create' | 'refine' = 'create',
    role?: 'Tenant' | 'Admin'
  ): Promise<import('../types').GenerateBlueprintCopilotResponse> => {
    const headers = getHeaders(role);
    const tenantId = getActiveTenantId();
    const response = await fetch(`${API_BASE}/copilot/generate?tenantId=${tenantId}`, {
      method: 'POST',
      headers,
      body: JSON.stringify({
        prompt,
        currentBlueprint,
        mode
      })
    });
    return handleResponse(response, 'Failed to generate blueprint with AI Copilot');
  },

  getTimeTravelReplay: async (id: string, role?: 'Tenant' | 'Admin'): Promise<TimeTravelReplay> => {
    const headers = getHeaders(role);
    const response = await fetch(`/api/workflows/${id}/time-travel`, { headers });
    return handleResponse(response, 'Failed to load time-travel replay');
  },

  simulateFork: async (
    id: string,
    stepIndex: number,
    event: string,
    payload?: unknown,
    role?: 'Tenant' | 'Admin'
  ): Promise<TimeTravelForkResult> => {
    const headers = getHeaders(role);
    const response = await fetch(`/api/workflows/${id}/time-travel/fork`, {
      method: 'POST',
      headers,
      body: JSON.stringify({
        targetStepIndex: stepIndex,
        alternativeEvent: event,
        alternativePayload: payload ?? null
      })
    });
    return handleResponse(response, 'Failed to simulate what-if fork');
  },

  // Auth & Tenant Management
  registerTenantUser: async (req: RegisterTenantUserRequest): Promise<RegisterTenantUserResponse> => {
    const response = await fetch('/api/auth/register-tenant', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(req)
    });
    return handleResponse(response, 'Registration failed');
  },

  verifyEmail: async (req: VerifyEmailRequest): Promise<VerifyEmailResponse> => {
    const response = await fetch('/api/auth/verify-email', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(req)
    });
    return handleResponse(response, 'Email verification failed');
  },

  loginTenantUser: async (req: LoginRequest): Promise<LoginResponse> => {
    const response = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(req)
    });
    return handleResponse(response, 'Login failed');
  },

  resendVerification: async (email: string): Promise<ResendVerificationResponse> => {
    const response = await fetch('/api/auth/resend-verification', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email })
    });
    return handleResponse(response, 'Failed to resend verification');
  },

  getCurrentUser: async (): Promise<{ ok: boolean; user: TenantUserDto }> => {
    const headers = getHeaders();
    const response = await fetch('/api/auth/me', { headers });
    return handleResponse(response, 'Failed to get current user profile');
  }
};


