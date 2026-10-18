import { beforeEach, describe, expect, test, vi } from 'vitest';
import { api, clearAuthSession, setAuthSession } from './client';

const tenantId = 'd3e8e2fd-1a8d-416a-987e-0585937090fd';

describe('workflow instance authentication', () => {
  beforeEach(() => {
    localStorage.clear();
    clearAuthSession();
    vi.restoreAllMocks();
  });

  test('sends the API key with the active tenant when listing instances', async () => {
    setAuthSession({
      role: 'Tenant',
      tenantId,
      tenantName: 'Tenant',
      apiKey: 'test-tenant-key',
      isSandbox: false
    });
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response('[]', { status: 200 })
    );

    await expect(api.listInstances('Tenant')).resolves.toEqual([]);

    expect(fetchMock).toHaveBeenCalledWith(`/api/workflows?tenantId=${tenantId}`, expect.objectContaining({
      headers: expect.objectContaining({
        'x-tenant-id': tenantId,
        'X-API-Key': 'test-tenant-key'
      })
    }));
  });

  test('explains an unauthenticated instance request after an expired JWT is removed', async () => {
    const payload = btoa(JSON.stringify({ exp: 1, tenant_id: tenantId }));
    setAuthSession({
      role: 'Tenant',
      tenantId,
      tenantName: 'Tenant',
      token: `header.${payload}.signature`,
      isSandbox: false
    });
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(null, { status: 401 })
    );

    await expect(api.listInstances('Tenant')).rejects.toThrow(
      'Failed to list workflow instances (HTTP 401 ): No active credential. Sign in again to continue.'
    );
    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [, options] = fetchMock.mock.calls[0];
    expect(options?.headers).not.toHaveProperty('Authorization');
    expect(options?.headers).not.toHaveProperty('X-API-Key');
  });
});
