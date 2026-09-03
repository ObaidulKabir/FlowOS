import { WorkflowClass, CreateDraftRequest, CopyRequest, ValidationResult, WorkflowClassScope, WorkflowClassStatus, WorkflowInstance } from '../types';

const API_BASE = '/api/workflow-classes';

let currentTenantId = '22222222-2222-2222-2222-222222222222';

export const getActiveTenantId = () => currentTenantId;
export const setActiveTenantId = (id: string) => {
  if (id) currentTenantId = id;
};

export const getHeaders = (_role: 'Tenant' | 'Admin' = 'Tenant') => ({
  'Content-Type': 'application/json',
  'x-tenant-id': currentTenantId,
  'X-Mock-Role': 'Admin'
});

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
        } catch (e) {
            // Ignore parsing error, use status
        }
        throw new Error(`${errorMessage}${errorDetails}`);
    }
    return response.json();
};

export const api = {
  list: async (scope?: WorkflowClassScope, status?: WorkflowClassStatus, role: 'Tenant' | 'Admin' = 'Tenant'): Promise<WorkflowClass[]> => {
    const headers = getHeaders(role);
    console.log('Fetching workflows with headers:', headers);
    const params = new URLSearchParams();
    params.append('tenantId', currentTenantId);
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

  get: async (id: string, role: 'Tenant' | 'Admin' = 'Tenant'): Promise<WorkflowClass> => {
    const headers = getHeaders(role);
    const response = await fetch(`${API_BASE}/${id}?tenantId=${currentTenantId}`, { headers });
    return handleResponse(response, 'Failed to get workflow class');
  },

  createDraft: async (req: CreateDraftRequest, role: 'Tenant' | 'Admin' = 'Tenant'): Promise<WorkflowClass> => {
    const headers = getHeaders(role);
    const response = await fetch(`${API_BASE}?tenantId=${currentTenantId}`, {
      method: 'POST',
      headers,
      body: JSON.stringify(req)
    });
    return handleResponse(response, 'Failed to create draft');
  },

  updateDraft: async (id: string, req: CreateDraftRequest, role: 'Tenant' | 'Admin' = 'Tenant'): Promise<WorkflowClass> => {
    const headers = getHeaders(role);
    const response = await fetch(`${API_BASE}/${id}?tenantId=${currentTenantId}`, {
      method: 'PUT',
      headers,
      body: JSON.stringify(req)
    });
    return handleResponse(response, 'Failed to update draft');
  },

  validate: async (id: string, role: 'Tenant' | 'Admin' = 'Tenant'): Promise<ValidationResult> => {
    const headers = getHeaders(role);
    const response = await fetch(`${API_BASE}/${id}/validate?tenantId=${currentTenantId}`, { method: 'POST', headers });
    return handleResponse(response, 'Failed to validate');
  },

  publish: async (id: string, role: 'Tenant' | 'Admin' = 'Tenant'): Promise<WorkflowClass> => {
    const headers = getHeaders(role);
    const response = await fetch(`${API_BASE}/${id}/publish?tenantId=${currentTenantId}`, { method: 'POST', headers });
    return handleResponse(response, 'Failed to publish');
  },

  submit: async (id: string, role: 'Tenant' | 'Admin' = 'Tenant'): Promise<WorkflowClass> => {
    const headers = getHeaders(role);
    const response = await fetch(`${API_BASE}/${id}/submit?tenantId=${currentTenantId}`, { method: 'POST', headers });
    return handleResponse(response, 'Failed to submit');
  },

  withdraw: async (id: string, role: 'Tenant' | 'Admin' = 'Tenant'): Promise<WorkflowClass> => {
    const headers = getHeaders(role);
    const response = await fetch(`${API_BASE}/${id}/withdraw?tenantId=${currentTenantId}`, { method: 'POST', headers });
    return handleResponse(response, 'Failed to withdraw');
  },

  deprecate: async (id: string, role: 'Tenant' | 'Admin' = 'Tenant'): Promise<WorkflowClass> => {
    const headers = getHeaders(role);
    const response = await fetch(`${API_BASE}/${id}/deprecate?tenantId=${currentTenantId}`, { method: 'POST', headers });
    return handleResponse(response, 'Failed to deprecate');
  },

  abandon: async (id: string, role: 'Tenant' | 'Admin' = 'Tenant'): Promise<WorkflowClass> => {
    const headers = getHeaders(role);
    const response = await fetch(`${API_BASE}/${id}/abandon?tenantId=${currentTenantId}`, { method: 'POST', headers });
    return handleResponse(response, 'Failed to abandon');
  },

  approve: async (id: string): Promise<WorkflowClass> => {
    const headers = getHeaders('Admin');
    const response = await fetch(`${API_BASE}/${id}/approve?tenantId=${currentTenantId}`, { method: 'POST', headers });
    return handleResponse(response, 'Failed to approve');
  },

  newVersion: async (id: string, role: 'Tenant' | 'Admin' = 'Tenant'): Promise<WorkflowClass> => {
    const headers = getHeaders(role);
    const response = await fetch(`${API_BASE}/${id}/new-version?tenantId=${currentTenantId}`, { method: 'POST', headers });
    return handleResponse(response, 'Failed to create new version');
  },

  delete: async (id: string, role: 'Tenant' | 'Admin' = 'Tenant'): Promise<void> => {
    const headers = getHeaders(role);
    const response = await fetch(`${API_BASE}/${id}?tenantId=${currentTenantId}`, { method: 'DELETE', headers });
    return handleResponse(response, 'Failed to delete');
  },

  copy: async (id: string, req: CopyRequest, role: 'Tenant' | 'Admin' = 'Tenant'): Promise<WorkflowClass> => {
    const headers = getHeaders(role);
    const response = await fetch(`${API_BASE}/${id}/copy?tenantId=${currentTenantId}`, { 
        method: 'POST', 
        headers,
        body: JSON.stringify(req)
    });
    return handleResponse(response, 'Failed to copy');
  },

  listInstances: async (role: 'Tenant' | 'Admin' = 'Tenant'): Promise<WorkflowInstance[]> => {
    const headers = getHeaders(role);
    const response = await fetch(`/api/workflows?tenantId=${currentTenantId}`, { headers });
    return handleResponse(response, 'Failed to list workflow instances');
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
    const headers = getHeaders('Admin');
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
    const headers = getHeaders('Admin');
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
    const headers = getHeaders('Admin');
    const response = await fetch(`/api/tenants/${tenantId}/keys/${keyId}`, {
      method: 'DELETE',
      headers
    });
    if (!response.ok) {
      throw new Error(`Failed to revoke API key (HTTP ${response.status})`);
    }
  }
};

