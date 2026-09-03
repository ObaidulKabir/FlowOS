import { 
  WorkflowClass, CreateDraftRequest, CopyRequest, ValidationResult, 
  WorkflowClassScope, WorkflowClassStatus, WorkflowInstance, AuthSession 
} from '../types';

const API_BASE = '/api/workflow-classes';
const AUTH_STORAGE_KEY = 'flowos_auth_session';

export const getDefaultSession = (): AuthSession => ({
  role: 'Tenant',
  tenantId: '22222222-2222-2222-2222-222222222222',
  tenantName: 'Demo Client Tenant',
  apiKey: 'flowos_prod_secret_key_32_chars_min',
  username: 'demo-tenant-user'
});

export const getAuthSession = (): AuthSession => {
  try {
    const raw = localStorage.getItem(AUTH_STORAGE_KEY);
    if (raw) {
      return JSON.parse(raw);
    }
  } catch (e) {
    console.error('Failed to parse auth session from localStorage', e);
  }
  return getDefaultSession();
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
  }
};
