import { WorkflowClass, CreateDraftRequest, CopyRequest, ValidationResult, WorkflowClassScope, WorkflowClassStatus, WorkflowInstance } from '../types';

const API_BASE = '/api/workflow-classes';

// Simulating a logged-in Tenant (matches the one in E2E tests for convenience)
const TENANT_ID = '22222222-2222-2222-2222-222222222222'; 
const ROLE = 'Designer';

const headers = {
  'Content-Type': 'application/json',
  'x-tenant-id': TENANT_ID,
  'X-Mock-Role': ROLE
};

export const api = {
  list: async (scope?: WorkflowClassScope, status?: WorkflowClassStatus): Promise<WorkflowClass[]> => {
    const params = new URLSearchParams();
    if (scope !== undefined) params.append('scope', scope.toString());
    if (status !== undefined) params.append('status', status.toString());
    
    const response = await fetch(`${API_BASE}?${params.toString()}`, { headers });
    if (!response.ok) throw new Error('Failed to list workflow classes');
    return response.json();
  },

  get: async (id: string): Promise<WorkflowClass> => {
    const response = await fetch(`${API_BASE}/${id}`, { headers });
    if (!response.ok) throw new Error('Failed to get workflow class');
    return response.json();
  },

  createDraft: async (req: CreateDraftRequest): Promise<WorkflowClass> => {
    const response = await fetch(API_BASE, {
      method: 'POST',
      headers,
      body: JSON.stringify(req)
    });
    if (!response.ok) throw new Error('Failed to create draft');
    return response.json();
  },

  updateDraft: async (id: string, req: CreateDraftRequest): Promise<WorkflowClass> => {
    const response = await fetch(`${API_BASE}/${id}`, {
      method: 'PUT',
      headers,
      body: JSON.stringify(req)
    });
    if (!response.ok) throw new Error('Failed to update draft');
    return response.json();
  },

  validate: async (id: string): Promise<ValidationResult> => {
    const response = await fetch(`${API_BASE}/${id}/validate`, { method: 'POST', headers });
    if (!response.ok) throw new Error('Failed to validate');
    return response.json();
  },

  publish: async (id: string): Promise<WorkflowClass> => {
    const response = await fetch(`${API_BASE}/${id}/publish`, { method: 'POST', headers });
    if (!response.ok) {
        const error = await response.json();
        throw new Error(JSON.stringify(error));
    }
    return response.json();
  },

  submit: async (id: string): Promise<WorkflowClass> => {
    const response = await fetch(`${API_BASE}/${id}/submit`, { method: 'POST', headers });
    if (!response.ok) throw new Error('Failed to submit');
    return response.json();
  },

  withdraw: async (id: string): Promise<WorkflowClass> => {
    const response = await fetch(`${API_BASE}/${id}/withdraw`, { method: 'POST', headers });
    if (!response.ok) throw new Error('Failed to withdraw');
    return response.json();
  },

  deprecate: async (id: string): Promise<WorkflowClass> => {
    const response = await fetch(`${API_BASE}/${id}/deprecate`, { method: 'POST', headers });
    if (!response.ok) throw new Error('Failed to deprecate');
    return response.json();
  },

  abandon: async (id: string): Promise<WorkflowClass> => {
    const response = await fetch(`${API_BASE}/${id}/abandon`, { method: 'POST', headers });
    if (!response.ok) throw new Error('Failed to abandon');
    return response.json();
  },

  approve: async (id: string): Promise<WorkflowClass> => {
    const response = await fetch(`${API_BASE}/${id}/approve`, { method: 'POST', headers });
    if (!response.ok) throw new Error('Failed to approve');
    return response.json();
  },

  newVersion: async (id: string): Promise<WorkflowClass> => {
    const response = await fetch(`${API_BASE}/${id}/new-version`, { method: 'POST', headers });
    if (!response.ok) throw new Error('Failed to create new version');
    return response.json();
  },

  delete: async (id: string): Promise<void> => {
    const response = await fetch(`${API_BASE}/${id}`, { method: 'DELETE', headers });
    if (!response.ok) throw new Error('Failed to delete');
  },

  copy: async (id: string, req: CopyRequest): Promise<WorkflowClass> => {
    const response = await fetch(`${API_BASE}/${id}/copy`, { 
        method: 'POST', 
        headers,
        body: JSON.stringify(req)
    });
    if (!response.ok) throw new Error('Failed to copy');
    return response.json();
  },

  listInstances: async (): Promise<WorkflowInstance[]> => {
    const response = await fetch('/api/workflows', { headers });
    if (!response.ok) throw new Error('Failed to list workflow instances');
    return response.json();
  }
};
