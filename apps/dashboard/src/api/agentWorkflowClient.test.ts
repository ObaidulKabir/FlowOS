import { beforeEach, describe, expect, test, vi } from 'vitest';
import { agentWorkflowApi, clearAuthSession, setAuthSession } from './client';
import { AgentWorkflowBlueprint, WorkflowClassStatus } from '../types';

const tenantId = 'd3e8e2fd-1a8d-416a-987e-0585937090fd';
const planId = '7c1f0a52-9d44-4f0e-9a3b-2b8c6f5e1d90';

const signIn = () =>
  setAuthSession({
    role: 'Tenant',
    tenantId,
    tenantName: 'Tenant',
    apiKey: 'test-tenant-key',
    isSandbox: false
  });

const blueprint = (): AgentWorkflowBlueprint => ({
  objective: 'Assess quarterly portfolio risk',
  startStepId: 'gather',
  coordinationRules: {
    maxGlobalRetries: 3,
    rollbackStrategy: 'Abandon',
    maxParallelSteps: 0
  },
  steps: [
    {
      stepId: 'gather',
      stepType: 'Agent',
      description: 'Gather risk data',
      teamRole: 'DataAnalyst',
      nextSteps: { Gathered: 'END' },
      branches: [],
      joinPolicy: 'WaitAll',
      inboundSteps: [],
      consequential: false,
      agentTools: [],
      pathLimits: {}
    }
  ]
});

// A 204 must be constructed with a null body; an empty string is rejected.
const mockFetch = (body: string | null, status = 200) =>
  vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(body, { status }));

const urlOf = (mock: ReturnType<typeof mockFetch>, call = 0) => String(mock.mock.calls[call][0]);

describe('agentWorkflowApi', () => {
  beforeEach(() => {
    localStorage.clear();
    clearAuthSession();
    vi.restoreAllMocks();
    signIn();
  });

  // ── Reads ───────────────────────────────────────────────────────────────────

  test('list sends the tenant, paging and the status name', async () => {
    const fetchMock = mockFetch('[]');

    await expect(agentWorkflowApi.list(WorkflowClassStatus.Draft, 0, 25)).resolves.toEqual([]);

    const url = urlOf(fetchMock);
    expect(url).toContain('/api/agent-workflow-classes?');
    expect(url).toContain(`tenantId=${tenantId}`);
    // The API has no string-enum converter, but ASP.NET binds the name too, and
    // the name is what the existing client sends.
    expect(url).toContain('status=Draft');
    expect(url).toContain('skip=0');
    expect(url).toContain('take=25');
  });

  test('list defaults paging when the caller omits it', async () => {
    const fetchMock = mockFetch('[]');

    await agentWorkflowApi.list();

    const url = urlOf(fetchMock);
    expect(url).toContain('skip=0');
    expect(url).toContain('take=50');
    expect(url).not.toContain('status=');
  });

  test('list sends the API key with the active tenant', async () => {
    const fetchMock = mockFetch('[]');

    await agentWorkflowApi.list();

    expect(fetchMock).toHaveBeenCalledWith(expect.any(String), expect.objectContaining({
      headers: expect.objectContaining({
        'x-tenant-id': tenantId,
        'X-API-Key': 'test-tenant-key'
      })
    }));
  });

  test('get returns the plan with its blueprint', async () => {
    const plan = {
      id: planId,
      tenantId,
      name: 'QuarterlyRiskPlan',
      version: '1.0.0',
      status: WorkflowClassStatus.Draft,
      scope: 0,
      createdAt: '2026-11-03T10:00:00Z',
      flowOsVersion: '1.1.0',
      definition: blueprint()
    };
    mockFetch(JSON.stringify(plan));

    const result = await agentWorkflowApi.get(planId);

    expect(result.name).toBe('QuarterlyRiskPlan');
    expect(result.definition.startStepId).toBe('gather');
    expect(result.definition.steps[0].stepType).toBe('Agent');
  });

  test('lineage sends maxDepth', async () => {
    const fetchMock = mockFetch('[]');

    await agentWorkflowApi.lineage(planId, 3);

    const url = urlOf(fetchMock);
    expect(url).toContain(`/api/agent-workflow-classes/${planId}/lineage?`);
    expect(url).toContain('maxDepth=3');
  });

  // ── Writes ──────────────────────────────────────────────────────────────────

  test('createDraft posts the blueprint and carries previousVersionId', async () => {
    const fetchMock = mockFetch('{}', 201);

    await agentWorkflowApi.createDraft({
      name: 'LineageV2',
      version: '2.0.0',
      definition: blueprint(),
      previousVersionId: planId
    });

    expect(urlOf(fetchMock)).toBe('/api/agent-workflow-classes');
    const [, options] = fetchMock.mock.calls[0];
    expect(options?.method).toBe('POST');
    const sent = JSON.parse(String(options?.body));
    expect(sent.name).toBe('LineageV2');
    expect(sent.previousVersionId).toBe(planId);
    expect(sent.definition.startStepId).toBe('gather');
  });

  test('updateDraft uses PUT on the plan route', async () => {
    const fetchMock = mockFetch('{}');

    await agentWorkflowApi.updateDraft(planId, {
      name: 'Renamed',
      version: '1.0.1',
      definition: blueprint()
    });

    expect(urlOf(fetchMock)).toBe(`/api/agent-workflow-classes/${planId}`);
    expect(fetchMock.mock.calls[0][1]?.method).toBe('PUT');
  });

  test('publish posts with no body and no tenant query', async () => {
    // Writes take the tenant from the caller's identity, so a query would be noise.
    const fetchMock = mockFetch('{}');

    await agentWorkflowApi.publish(planId);

    expect(urlOf(fetchMock)).toBe(`/api/agent-workflow-classes/${planId}/publish`);
    expect(fetchMock.mock.calls[0][1]?.method).toBe('POST');
  });

  test('deprecate sends the reason and migration target', async () => {
    const fetchMock = mockFetch('{}');

    await agentWorkflowApi.deprecate(planId, {
      reason: 'Superseded',
      migrationTargetId: planId
    });

    const sent = JSON.parse(String(fetchMock.mock.calls[0][1]?.body));
    expect(sent.reason).toBe('Superseded');
    expect(sent.migrationTargetId).toBe(planId);
  });

  test('deprecate with no argument still sends a body', async () => {
    const fetchMock = mockFetch('{}');

    await agentWorkflowApi.deprecate(planId);

    expect(JSON.parse(String(fetchMock.mock.calls[0][1]?.body))).toEqual({});
  });

  test('abandon posts to the abandon route', async () => {
    const fetchMock = mockFetch('{}');

    await agentWorkflowApi.abandon(planId);

    expect(urlOf(fetchMock)).toBe(`/api/agent-workflow-classes/${planId}/abandon`);
  });

  // ── Delete: 204 has no body to parse ───────────────────────────────────────

  test('delete resolves on 204 No Content', async () => {
    const fetchMock = mockFetch(null, 204);

    await expect(agentWorkflowApi.delete(planId)).resolves.toBeUndefined();

    expect(urlOf(fetchMock)).toBe(`/api/agent-workflow-classes/${planId}`);
    expect(fetchMock.mock.calls[0][1]?.method).toBe('DELETE');
  });

  test('delete surfaces a governance guard rejection', async () => {
    mockFetch(JSON.stringify({ Error: 'GOV-DEL-001: Only Draft plans can be hard-deleted.' }), 400);

    await expect(agentWorkflowApi.delete(planId)).rejects.toThrow(/GOV-DEL-001/);
  });

  // ── Validate ────────────────────────────────────────────────────────────────

  test('validate wraps the blueprint in a definition envelope', async () => {
    const fetchMock = mockFetch(JSON.stringify({ isValid: true, errors: [] }));

    const result = await agentWorkflowApi.validate(blueprint());

    expect(urlOf(fetchMock)).toBe('/api/agent-workflow-classes/validate');
    const sent = JSON.parse(String(fetchMock.mock.calls[0][1]?.body));
    expect(sent.definition.objective).toBe('Assess quarterly portfolio risk');
    expect(result.isValid).toBe(true);
  });

  test('validate resolves with the error list for a broken blueprint', async () => {
    // An invalid blueprint is a successful call reporting problems, not a failure:
    // the author gets every error at once.
    mockFetch(JSON.stringify({
      isValid: false,
      errors: [{
        code: 'AWF-COMP-002',
        category: 'Completeness',
        message: "startStepId 'nowhere' does not match any declared step.",
        element: 'Blueprint'
      }]
    }));

    const result = await agentWorkflowApi.validate({ ...blueprint(), startStepId: 'nowhere' });

    expect(result.isValid).toBe(false);
    expect(result.errors[0].code).toBe('AWF-COMP-002');
  });

  // ── Failure surfacing ──────────────────────────────────────────────────────

  test('createDraft surfaces the validation error list from a 400', async () => {
    mockFetch(JSON.stringify({
      errors: [{ code: 'AWF-TEAM-001', category: 'TeamBinding', message: 'binds no team', element: 'Steps' }]
    }), 400);

    await expect(agentWorkflowApi.createDraft({
      name: 'Broken',
      version: '1.0.0',
      definition: blueprint()
    })).rejects.toThrow(/AWF-TEAM-001/);
  });

  test('get surfaces a 404 as a thrown error', async () => {
    mockFetch(null, 404);

    await expect(agentWorkflowApi.get(planId)).rejects.toThrow(/Failed to get agent workflow class/);
  });
});
