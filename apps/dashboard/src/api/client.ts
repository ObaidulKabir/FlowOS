import { WorkflowClass, CreateDraftRequest, CopyRequest, ValidationResult, WorkflowClassScope, WorkflowClassStatus, WorkflowInstance } from '../types';

const API_BASE = '/api/workflow-classes';

// Simulating a logged-in Tenant (matches the one in E2E tests for convenience)
const TENANT_ID = '22222222-2222-2222-2222-222222222222'; 
const ROLE = 'Admin';

const headers = {
  'Content-Type': 'application/json',
  'x-tenant-id': TENANT_ID,
  'X-Mock-Role': ROLE
};

const handleResponse = async (response: Response, errorMessage: string) => {
    if (!response.ok) {
        let errorDetails = '';
        try {
            const errorBody = await response.text();
            errorDetails = errorBody ? `: ${errorBody}` : '';
            // Try to parse JSON if possible for cleaner message
            const errorJson = JSON.parse(errorBody);
            if (errorJson.error) errorDetails = `: ${errorJson.error}`;
            if (errorJson.errors) errorDetails = `: ${JSON.stringify(errorJson.errors)}`;
            if (errorJson.title) errorDetails = `: ${errorJson.title}`;
        } catch (e) {
            // Ignore parsing error, use raw text
        }
        throw new Error(`${errorMessage}${errorDetails}`);
    }
    return response.json();
};

export const api = {
  list: async (scope?: WorkflowClassScope, status?: WorkflowClassStatus): Promise<WorkflowClass[]> => {
    console.log('Fetching workflows with headers:', headers);
    const params = new URLSearchParams();
    if (scope !== undefined) params.append('scope', scope.toString());
    if (status !== undefined) params.append('status', status.toString());
    
    const response = await fetch(`${API_BASE}?${params.toString()}`, { headers });
    return handleResponse(response, 'Failed to list workflow classes');
  },

  get: async (id: string): Promise<WorkflowClass> => {
    const response = await fetch(`${API_BASE}/${id}`, { headers });
    return handleResponse(response, 'Failed to get workflow class');
  },

  createDraft: async (req: CreateDraftRequest): Promise<WorkflowClass> => {
    const response = await fetch(API_BASE, {
      method: 'POST',
      headers,
      body: JSON.stringify(req)
    });
    return handleResponse(response, 'Failed to create draft');
  },

  updateDraft: async (id: string, req: CreateDraftRequest): Promise<WorkflowClass> => {
    const response = await fetch(`${API_BASE}/${id}`, {
      method: 'PUT',
      headers,
      body: JSON.stringify(req)
    });
    return handleResponse(response, 'Failed to update draft');
  },

  validate: async (id: string): Promise<ValidationResult> => {
    const response = await fetch(`${API_BASE}/${id}/validate`, { method: 'POST', headers });
    return handleResponse(response, 'Failed to validate');
  },

  publish: async (id: string): Promise<WorkflowClass> => {
    const response = await fetch(`${API_BASE}/${id}/publish`, { method: 'POST', headers });
    return handleResponse(response, 'Failed to publish');
  },

  submit: async (id: string): Promise<WorkflowClass> => {
    const response = await fetch(`${API_BASE}/${id}/submit`, { method: 'POST', headers });
    return handleResponse(response, 'Failed to submit');
  },

  withdraw: async (id: string): Promise<WorkflowClass> => {
    const response = await fetch(`${API_BASE}/${id}/withdraw`, { method: 'POST', headers });
    return handleResponse(response, 'Failed to withdraw');
  },

  deprecate: async (id: string): Promise<WorkflowClass> => {
    const response = await fetch(`${API_BASE}/${id}/deprecate`, { method: 'POST', headers });
    return handleResponse(response, 'Failed to deprecate');
  },

  abandon: async (id: string): Promise<WorkflowClass> => {
    const response = await fetch(`${API_BASE}/${id}/abandon`, { method: 'POST', headers });
    return handleResponse(response, 'Failed to abandon');
  },

  approve: async (id: string): Promise<WorkflowClass> => {
    const response = await fetch(`${API_BASE}/${id}/approve`, { method: 'POST', headers });
    return handleResponse(response, 'Failed to approve');
  },

  newVersion: async (id: string): Promise<WorkflowClass> => {
    const response = await fetch(`${API_BASE}/${id}/new-version`, { method: 'POST', headers });
    return handleResponse(response, 'Failed to create new version');
  },

  delete: async (id: string): Promise<void> => {
    const response = await fetch(`${API_BASE}/${id}`, { method: 'DELETE', headers });
    if (!response.ok) {
         let errorDetails = '';
        try {
            const errorBody = await response.text();
             errorDetails = errorBody ? `: ${errorBody}` : '';
        } catch (e) {}
        throw new Error(`Failed to delete${errorDetails}`);
    }
  },

  copy: async (id: string, req: CopyRequest): Promise<WorkflowClass> => {
    const response = await fetch(`${API_BASE}/${id}/copy`, { 
        method: 'POST', 
        headers,
        body: JSON.stringify(req)
    });
    return handleResponse(response, 'Failed to copy');
  },

  listInstances: async (): Promise<WorkflowInstance[]> => {
    const response = await fetch('/api/workflows', { headers });
    return handleResponse(response, 'Failed to list workflow instances');
  }
};
