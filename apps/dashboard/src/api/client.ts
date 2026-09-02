import { WorkflowClass, CreateDraftRequest, CopyRequest, ValidationResult, WorkflowClassScope, WorkflowClassStatus, WorkflowInstance } from '../types';

const API_BASE = '/api/workflow-classes';

// Simulating a logged-in Tenant (matches the one in E2E tests for convenience)
const TENANT_ID = '22222222-2222-2222-2222-222222222222'; 

export const getHeaders = (_role: 'Tenant' | 'Admin' = 'Tenant') => ({
  'Content-Type': 'application/json',
  'x-tenant-id': TENANT_ID,
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
    params.append('tenantId', TENANT_ID);
    if (scope !== undefined) params.append('scope', scope.toString());
    if (status !== undefined) params.append('status', status.toString());
    
    const response = await fetch(`${API_BASE}?${params.toString()}`, { headers });
    return handleResponse(response, 'Failed to list workflow classes');
  },

  get: async (id: string, role: 'Tenant' | 'Admin' = 'Tenant'): Promise<WorkflowClass> => {
    const headers = getHeaders(role);
    const response = await fetch(`${API_BASE}/${id}?tenantId=${TENANT_ID}`, { headers });
    return handleResponse(response, 'Failed to get workflow class');
  },

  createDraft: async (req: CreateDraftRequest, role: 'Tenant' | 'Admin' = 'Tenant'): Promise<WorkflowClass> => {
    const headers = getHeaders(role);
    const response = await fetch(`${API_BASE}?tenantId=${TENANT_ID}`, {
      method: 'POST',
      headers,
      body: JSON.stringify(req)
    });
    return handleResponse(response, 'Failed to create draft');
  },

  updateDraft: async (id: string, req: CreateDraftRequest, role: 'Tenant' | 'Admin' = 'Tenant'): Promise<WorkflowClass> => {
    const headers = getHeaders(role);
    const response = await fetch(`${API_BASE}/${id}?tenantId=${TENANT_ID}`, {
      method: 'PUT',
      headers,
      body: JSON.stringify(req)
    });
    return handleResponse(response, 'Failed to update draft');
  },

  validate: async (id: string, role: 'Tenant' | 'Admin' = 'Tenant'): Promise<ValidationResult> => {
    const headers = getHeaders(role);
    const response = await fetch(`${API_BASE}/${id}/validate?tenantId=${TENANT_ID}`, { method: 'POST', headers });
    return handleResponse(response, 'Failed to validate');
  },

  publish: async (id: string, role: 'Tenant' | 'Admin' = 'Tenant'): Promise<WorkflowClass> => {
    const headers = getHeaders(role);
    const response = await fetch(`${API_BASE}/${id}/publish?tenantId=${TENANT_ID}`, { method: 'POST', headers });
    return handleResponse(response, 'Failed to publish');
  },

  submit: async (id: string, role: 'Tenant' | 'Admin' = 'Tenant'): Promise<WorkflowClass> => {
    const headers = getHeaders(role);
    const response = await fetch(`${API_BASE}/${id}/submit?tenantId=${TENANT_ID}`, { method: 'POST', headers });
    return handleResponse(response, 'Failed to submit');
  },

  withdraw: async (id: string, role: 'Tenant' | 'Admin' = 'Tenant'): Promise<WorkflowClass> => {
    const headers = getHeaders(role);
    const response = await fetch(`${API_BASE}/${id}/withdraw?tenantId=${TENANT_ID}`, { method: 'POST', headers });
    return handleResponse(response, 'Failed to withdraw');
  },

  deprecate: async (id: string, role: 'Tenant' | 'Admin' = 'Tenant'): Promise<WorkflowClass> => {
    const headers = getHeaders(role);
    const response = await fetch(`${API_BASE}/${id}/deprecate?tenantId=${TENANT_ID}`, { method: 'POST', headers });
    return handleResponse(response, 'Failed to deprecate');
  },

  abandon: async (id: string, role: 'Tenant' | 'Admin' = 'Tenant'): Promise<WorkflowClass> => {
    const headers = getHeaders(role);
    const response = await fetch(`${API_BASE}/${id}/abandon?tenantId=${TENANT_ID}`, { method: 'POST', headers });
    return handleResponse(response, 'Failed to abandon');
  },

  approve: async (id: string): Promise<WorkflowClass> => {
    const headers = getHeaders('Admin');
    const response = await fetch(`${API_BASE}/${id}/approve?tenantId=${TENANT_ID}`, { method: 'POST', headers });
    return handleResponse(response, 'Failed to approve');
  },

  newVersion: async (id: string, role: 'Tenant' | 'Admin' = 'Tenant'): Promise<WorkflowClass> => {
    const headers = getHeaders(role);
    const response = await fetch(`${API_BASE}/${id}/new-version?tenantId=${TENANT_ID}`, { method: 'POST', headers });
    return handleResponse(response, 'Failed to create new version');
  },

  delete: async (id: string, role: 'Tenant' | 'Admin' = 'Tenant'): Promise<void> => {
    const headers = getHeaders(role);
    const response = await fetch(`${API_BASE}/${id}?tenantId=${TENANT_ID}`, { method: 'DELETE', headers });
    return handleResponse(response, 'Failed to delete');
  },

  copy: async (id: string, req: CopyRequest, role: 'Tenant' | 'Admin' = 'Tenant'): Promise<WorkflowClass> => {
    const headers = getHeaders(role);
    const response = await fetch(`${API_BASE}/${id}/copy?tenantId=${TENANT_ID}`, { 
        method: 'POST', 
        headers,
        body: JSON.stringify(req)
    });
    return handleResponse(response, 'Failed to copy');
  },

  listInstances: async (role: 'Tenant' | 'Admin' = 'Tenant'): Promise<WorkflowInstance[]> => {
    const headers = getHeaders(role);
    const response = await fetch(`/api/workflows?tenantId=${TENANT_ID}`, { headers });
    return handleResponse(response, 'Failed to list workflow instances');
  }
};
