import { 
  WorkflowClass, CreateDraftRequest, CopyRequest, ValidationResult, 
  WorkflowClassScope, WorkflowClassStatus, WorkflowInstance, AuthSession, DeadLetterDto,
  TimeTravelReplay, TimeTravelForkResult,
  RegisterTenantUserRequest, RegisterTenantUserResponse,
  VerifyEmailRequest, VerifyEmailResponse,
  LoginRequest, LoginResponse,
  ResendVerificationResponse, TenantUserDto, TenantDto,
  WorkflowContextBinding, WorkflowContextBindingDefinition, CreateContextBindingRequest,
  WorkflowContextSimulationRequest, WorkflowContextSimulationResult,
  TenantApiKeyDto, CreateKeyResponse, TenantRoleDto, AgentEvaluationMetrics
} from '../types';

const API_BASE = '/api/workflow-classes';
const AUTH_STORAGE_KEY = 'flowos_auth_session';

export const DEMO_TENANT_ID = '22222222-2222-2222-2222-222222222222';
export const PLATFORM_TENANT_ID = '11111111-1111-1111-1111-111111111111';
export const DEMO_API_KEY = 'flowos_prod_secret_key_32_chars_min';

export const getDefaultSandboxSession = (): AuthSession => ({
  role: 'Tenant',
  tenantId: DEMO_TENANT_ID,
  tenantName: 'Demo Client Tenant (Sandbox)',
  apiKey: DEMO_API_KEY,
  username: 'demo-tenant-user',
  isSandbox: true,
  isEmailVerified: true,
  plan: 'Managed',
  billingStatus: 'Active',
  canRunRuntime: true
});

const isDemoApiKey = (key?: string) =>
  key === DEMO_API_KEY ||
  key === 'local-development-key-change-me' ||
  key === 'YOUR_PRODUCTION_API_KEY';

const isPlaygroundTenant = (tenantId?: string) =>
  !tenantId || tenantId === DEMO_TENANT_ID || tenantId === PLATFORM_TENANT_ID;

const tokenIsUnusable = (token?: string): boolean => {
  if (!token) return true;
  const parts = token.split('.');
  if (parts.length !== 3) return true;
  try {
    const json = atob(parts[1].replace(/-/g, '+').replace(/_/g, '/'));
    const payload = JSON.parse(json);
    return typeof payload.exp === 'number' && payload.exp * 1000 < Date.now();
  } catch {
    return true;
  }
};

const isPlaygroundSession = (session: AuthSession, apiKey?: string, tenantId?: string) =>
  Boolean(
    session.isSandbox ||
    isDemoApiKey(apiKey) ||
    isPlaygroundTenant(tenantId) ||
    (session.role === 'Admin' && !apiKey)
  );

/** Playground/admin leftover JWTs skip the demo key and production then 401s GET /api/workflows. */
export const withSessionCredentials = (session: AuthSession): AuthSession => {
  let token = session.token?.trim() || undefined;
  let apiKey = session.apiKey?.trim() || undefined;
  let tenantId = session.tenantId;

  if (token && tokenIsUnusable(token)) {
    token = undefined;
  }

  const realTenantJwt =
    Boolean(token) &&
    session.isSandbox !== true &&
    !isPlaygroundTenant(tenantId);
  if (realTenantJwt && isDemoApiKey(apiKey)) {
    apiKey = undefined;
  }

  if (isPlaygroundSession(session, apiKey, tenantId) || (!token && !apiKey && isPlaygroundTenant(tenantId))) {
    apiKey = apiKey || DEMO_API_KEY;
    tenantId = DEMO_TENANT_ID;
    token = undefined;
  } else if (isDemoApiKey(apiKey)) {
    tenantId = DEMO_TENANT_ID;
    token = undefined;
  }

  return { ...session, token, apiKey, tenantId };
};

export const applyTenantEntitlement = (
  session: AuthSession,
  user?: Pick<AuthSession, 'plan' | 'billingStatus' | 'canRunRuntime'> | TenantUserDto | null
): AuthSession => {
  if (!user) return session;
  return {
    ...session,
    plan: user.plan ?? session.plan,
    billingStatus: user.billingStatus ?? session.billingStatus,
    canRunRuntime: user.canRunRuntime ?? session.canRunRuntime
  };
};

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
  const stored = getStoredSession();
  const normalized = withSessionCredentials(stored || getDefaultSandboxSession());
  if (
    !stored ||
    stored.apiKey !== normalized.apiKey ||
    stored.token !== normalized.token ||
    stored.tenantId !== normalized.tenantId
  ) {
    try {
      localStorage.setItem(AUTH_STORAGE_KEY, JSON.stringify(normalized));
    } catch {
      // Ignore quota / private-mode write failures; headers still use the repaired session.
    }
  }
  return normalized;
};

export const setAuthSession = (session: AuthSession): AuthSession => {
  const normalized = withSessionCredentials(session);
  localStorage.setItem(AUTH_STORAGE_KEY, JSON.stringify(normalized));
  return normalized;
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

export const getHeaders = (
  roleOverride?: 'Tenant' | 'Admin',
  credentialMode: 'default' | 'token-only' | 'key-only' = 'default'
) => {
  const session = getAuthSession();
  const role = roleOverride || session.role;
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    'x-tenant-id': session.tenantId,
    'X-Mock-Role': role,
    'X-Mock-UserId': session.username || (role === 'Admin' ? 'superadmin' : 'tenant-user')
  };

  const sendKey = credentialMode !== 'token-only' && Boolean(session.apiKey);
  const sendToken = credentialMode !== 'key-only' && Boolean(session.token);
  if (sendKey && session.apiKey) {
    headers['X-API-Key'] = session.apiKey;
  }
  if (sendToken && session.token) {
    headers['Authorization'] = `Bearer ${session.token}`;
  }

  return headers;
};

const authorizedFetch = async (
  url: string,
  init: RequestInit = {},
  role?: 'Tenant' | 'Admin'
): Promise<Response> => {
  const merge = (headers: Record<string, string>) => ({
    ...headers,
    ...(init.headers as Record<string, string> | undefined)
  });
  let response = await fetch(url, { ...init, headers: merge(getHeaders(role)) });
  if (response.status !== 401) return response;

  const session = getAuthSession();
  if (session.apiKey && session.token) {
    response = await fetch(url, { ...init, headers: merge(getHeaders(role, 'token-only')) });
    if (response.status !== 401) return response;
    response = await fetch(url, { ...init, headers: merge(getHeaders(role, 'key-only')) });
    if (response.status !== 401) return response;
  }

  const alreadyDemo = isDemoApiKey(session.apiKey) && !session.token;
  if (alreadyDemo || !isPlaygroundSession(session, session.apiKey, session.tenantId)) {
    return response;
  }

  setAuthSession({
    ...session,
    token: undefined,
    apiKey: DEMO_API_KEY,
    tenantId: DEMO_TENANT_ID
  });
  return fetch(url, { ...init, headers: merge(getHeaders(role, 'key-only')) });
};

const handleResponse = async (response: Response, errorMessage: string) => {
  if (!response.ok) {
    let errorDetails = ` (HTTP ${response.status} ${response.statusText})`;
    try {
      const errorBody = await response.text();
      if (errorBody) {
        try {
          const errorJson = JSON.parse(errorBody);
          const detail = errorJson.detail || errorJson.error || errorJson.message || errorJson.code || errorJson.title || (errorJson.errors ? JSON.stringify(errorJson.errors) : errorBody);
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
    
    const response = await authorizedFetch(`${API_BASE}?${params.toString()}`, {}, role);
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
    const tenantId = getActiveTenantId();
    const response = await authorizedFetch(`/api/workflows?tenantId=${tenantId}`, {}, role);
    return handleResponse(response, 'Failed to list workflow instances');
  },

  getAgentEvaluationMetrics: async (
    fromUtc: string,
    toUtc: string,
    role?: 'Tenant' | 'Admin'
  ): Promise<AgentEvaluationMetrics> => {
    const params = new URLSearchParams({ fromUtc, toUtc });
    const response = await authorizedFetch(`/api/agents/metrics?${params.toString()}`, {}, role);
    return handleResponse(response, 'Failed to load agent evaluation metrics');
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
    const params = new URLSearchParams();
    if (workflowInstanceId) params.append('workflowInstanceId', workflowInstanceId);
    params.append('limit', limit.toString());
    const response = await authorizedFetch(`/api/events?${params.toString()}`, {}, role);
    return handleResponse(response, 'Failed to list events');
  },

  getWorkflowAudit: async (instanceId: string, role?: 'Tenant' | 'Admin'): Promise<any> => {
    const response = await authorizedFetch(`/api/workflows/${instanceId}/audit`, {}, role);
    return handleResponse(response, 'Failed to get workflow audit history');
  },

  getWorkflowActions: async (instanceId: string, role?: 'Tenant' | 'Admin'): Promise<any[]> => {
    const response = await authorizedFetch(`/api/workflows/${instanceId}/actions`, {}, role);
    const data = await handleResponse(response, 'Failed to load lifecycle action audit logs');
    if (Array.isArray(data)) return data;
    if (Array.isArray(data?.items)) return data.items;
    if (Array.isArray(data?.Items)) return data.Items;
    return [];
  },

  listTenants: async (): Promise<TenantDto[]> => {
    const response = await authorizedFetch('/api/tenants', {}, 'Admin');
    return handleResponse(response, 'Failed to list tenants');
  },

  setTenantPlan: async (
    tenantId: string,
    plan: 'Managed' | 'Enterprise' = 'Managed',
    billingStatus: 'Unpaid' | 'Active' | 'PastDue' | 'Canceled' = 'Active'
  ): Promise<{ tenantId: string; name: string; plan: string; billingStatus: string; canRunRuntime: boolean }> => {
    const headers = getHeaders('Admin');
    const response = await fetch(`/api/admin/tenants/${tenantId}/plan`, {
      method: 'POST',
      headers,
      body: JSON.stringify({ plan, billingStatus })
    });
    return handleResponse(response, 'Failed to set tenant plan');
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

  listTenantKeys: async (tenantId: string): Promise<TenantApiKeyDto[]> => {
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
  ): Promise<CreateKeyResponse> => {
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

  listRoles: async (): Promise<TenantRoleDto[]> => {
    const response = await authorizedFetch('/api/roles');
    const data = await handleResponse(response, 'Failed to list tenant roles');
    return (Array.isArray(data) ? data : []).map((role: any) => ({
      id: String(role.id ?? role.Id ?? ''),
      name: String(role.name ?? role.Name ?? ''),
      capabilities: Array.isArray(role.capabilities ?? role.Capabilities)
        ? (role.capabilities ?? role.Capabilities)
        : []
    }));
  },

  createRole: async (roleName: string): Promise<string> => {
    const response = await authorizedFetch('/api/roles', {
      method: 'POST',
      body: JSON.stringify({ roleName })
    });
    const data = await handleResponse(response, 'Failed to create tenant role');
    return String(data.id ?? data.Id ?? '');
  },

  addRoleCapability: async (roleId: string, capabilityCode: string): Promise<void> => {
    const response = await authorizedFetch(`/api/roles/${roleId}/capabilities`, {
      method: 'POST',
      body: JSON.stringify({ capabilityCode })
    });
    if (!response.ok) {
      await handleResponse(response, 'Failed to grant role capability');
    }
  },

  removeRoleCapability: async (roleId: string, capabilityCode: string): Promise<void> => {
    const response = await authorizedFetch(
      `/api/roles/${roleId}/capabilities/${encodeURIComponent(capabilityCode)}`,
      { method: 'DELETE' }
    );
    if (!response.ok) {
      await handleResponse(response, 'Failed to revoke role capability');
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

  listContextBindings: async (
    sourceWorkflowClassId?: string,
    role?: 'Tenant' | 'Admin'
  ): Promise<WorkflowContextBinding[]> => {
    const headers = getHeaders(role);
    const params = new URLSearchParams();
    if (sourceWorkflowClassId) params.set('sourceWorkflowClassId', sourceWorkflowClassId);
    const query = params.toString() ? `?${params.toString()}` : '';
    const response = await fetch(`/api/context-bindings${query}`, { headers });
    return handleResponse(response, 'Failed to list context bindings');
  },

  getContextBinding: async (id: string, role?: 'Tenant' | 'Admin'): Promise<WorkflowContextBinding> => {
    const response = await fetch(`/api/context-bindings/${id}`, { headers: getHeaders(role) });
    return handleResponse(response, 'Failed to get context binding');
  },

  createContextBinding: async (
    request: CreateContextBindingRequest,
    role?: 'Tenant' | 'Admin'
  ): Promise<WorkflowContextBinding> => {
    const response = await fetch('/api/context-bindings', {
      method: 'POST',
      headers: getHeaders(role),
      body: JSON.stringify(request)
    });
    return handleResponse(response, 'Failed to create context binding');
  },

  updateContextBindingDraft: async (
    id: string,
    definition: WorkflowContextBindingDefinition,
    sourceWorkflowClassId?: string,
    role?: 'Tenant' | 'Admin'
  ): Promise<WorkflowContextBinding> => {
    const response = await fetch(`/api/context-bindings/${id}/draft`, {
      method: 'PUT',
      headers: getHeaders(role),
      body: JSON.stringify({ definition, sourceWorkflowClassId })
    });
    return handleResponse(response, 'Failed to update context binding');
  },

  validateContextBinding: async (id: string, role?: 'Tenant' | 'Admin'): Promise<ValidationResult> => {
    const response = await fetch(`/api/context-bindings/${id}/validate`, {
      method: 'POST',
      headers: getHeaders(role)
    });
    return handleResponse(response, 'Failed to validate context binding');
  },

  activateContextBinding: async (id: string, role?: 'Tenant' | 'Admin'): Promise<WorkflowContextBinding> => {
    const response = await fetch(`/api/context-bindings/${id}/activate`, {
      method: 'POST',
      headers: getHeaders(role)
    });
    return handleResponse(response, 'Failed to activate context binding');
  },

  archiveContextBinding: async (id: string, role?: 'Tenant' | 'Admin'): Promise<WorkflowContextBinding> => {
    const response = await fetch(`/api/context-bindings/${id}/archive`, {
      method: 'POST',
      headers: getHeaders(role)
    });
    return handleResponse(response, 'Failed to archive context binding');
  },

  simulateContextBinding: async (
    request: WorkflowContextSimulationRequest,
    role?: 'Tenant' | 'Admin'
  ): Promise<WorkflowContextSimulationResult> => {
    const response = await fetch('/api/context-bindings/simulate', {
      method: 'POST',
      headers: getHeaders(role),
      body: JSON.stringify(request)
    });
    return handleResponse(response, 'Failed to simulate context binding');
  },

  startWorkflowByContext: async (
    selector: { contextBindingId?: string; contextType?: string },
    payload?: Record<string, unknown>,
    businessReference?: { sourceSystem?: string; externalEntityId?: string; metadata?: Record<string, string> },
    role?: 'Tenant' | 'Admin'
  ): Promise<{ workflowInstanceId: string }> => {
    const response = await fetch('/api/workflows/start', {
      method: 'POST',
      headers: getHeaders(role),
      body: JSON.stringify({
        tenantId: getActiveTenantId(),
        ...selector,
        payload,
        businessReference
      })
    });
    return handleResponse(response, 'Failed to start workflow by context');
  },

  copilotGenerate: async (
    prompt: string,
    currentBlueprint?: any,
    mode: 'create' | 'refine' | 'template' = 'create',
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
    const response = await authorizedFetch(`/api/workflows/${id}/time-travel`, {}, role);
    return handleResponse(response, 'Failed to load time-travel replay');
  },

  simulateFork: async (
    id: string,
    stepIndex: number,
    event: string,
    payload?: unknown,
    simulatedRoles?: string[],
    role?: 'Tenant' | 'Admin'
  ): Promise<TimeTravelForkResult> => {
    const response = await authorizedFetch(`/api/workflows/${id}/time-travel/fork`, {
      method: 'POST',
      body: JSON.stringify({
        targetStepIndex: stepIndex,
        alternativeEvent: event,
        alternativePayload: payload ?? null,
        simulatedRoles: simulatedRoles ?? []
      })
    }, role);
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

  verifyEmailWithPassword: async (req: { email: string; password: string }): Promise<VerifyEmailResponse> => {
    const response = await fetch('/api/auth/verify-with-password', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(req)
    });
    return handleResponse(response, 'Verification with password failed');
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
  },

  listPluginBindings: async (
    bindingType?: string,
    role?: 'Tenant' | 'Admin'
  ): Promise<{ totalCount: number; bindings: PluginBindingDto[] }> => {
    const headers = getHeaders(role);
    const params = new URLSearchParams();
    if (bindingType) params.append('bindingType', bindingType);
    const response = await fetch(`/api/plugin-bindings?${params.toString()}`, { headers });
    const data = await handleResponse(response, 'Failed to list plugin bindings');
    const raw = data.bindings ?? data.Bindings ?? [];
    return {
      totalCount: data.totalCount ?? data.TotalCount ?? raw.length,
      bindings: raw.map((item: Record<string, unknown>) => ({
        id: String(item.id ?? item.Id ?? ''),
        tenantId: String(item.tenantId ?? item.TenantId ?? ''),
        bindingType: String(item.bindingType ?? item.BindingType ?? ''),
        sourceName: String(item.sourceName ?? item.SourceName ?? ''),
        providerName: String(item.providerName ?? item.ProviderName ?? ''),
        isEnabled: Boolean(item.isEnabled ?? item.IsEnabled ?? true),
        configuration: (item.configuration ?? item.Configuration ?? null) as Record<string, unknown> | null,
        createdAtUtc: String(item.createdAtUtc ?? item.CreatedAtUtc ?? ''),
        updatedAtUtc: String(item.updatedAtUtc ?? item.UpdatedAtUtc ?? '')
      }))
    };
  },

  upsertPluginBinding: async (
    req: UpsertPluginBindingRequest,
    role?: 'Tenant' | 'Admin'
  ): Promise<PluginBindingDto> => {
    const headers = getHeaders(role);
    const response = await fetch('/api/plugin-bindings', {
      method: 'PUT',
      headers,
      body: JSON.stringify(req)
    });
    return handleResponse(response, 'Failed to save plugin binding');
  }
};

export interface PluginBindingDto {
  id: string;
  tenantId: string;
  bindingType: string;
  sourceName: string;
  providerName: string;
  isEnabled: boolean;
  configuration?: Record<string, unknown> | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface UpsertPluginBindingRequest {
  bindingType: string;
  sourceName: string;
  providerName?: string;
  isEnabled?: boolean;
  configuration?: Record<string, unknown>;
}


