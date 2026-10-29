import { beforeEach, describe, expect, test, vi } from 'vitest';
import {
  api,
  clearAuthSession,
  getAuthSession,
  onSessionExpired,
  PLATFORM_TENANT_ID,
  setAuthSession,
  takeSessionExpiredNotice
} from './client';

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

  test('keeps a platform administrator JWT instead of replacing it with the sandbox key', () => {
    const payload = btoa(JSON.stringify({
      exp: 4_000_000_000,
      role: 'Admin',
      tenant_id: PLATFORM_TENANT_ID,
      tenant_name: 'Default Tenant'
    }));
    const token = `header.${payload}.signature`;
    const stored = setAuthSession({
      role: 'Admin',
      tenantId: PLATFORM_TENANT_ID,
      tenantName: 'Default Tenant',
      token,
      email: 'platform-admin@flowosbd.com',
      username: 'Platform Administrator',
      isSandbox: false,
      isEmailVerified: true
    });

    expect(stored.role).toBe('Admin');
    expect(stored.tenantId).toBe(PLATFORM_TENANT_ID);
    expect(stored.token).toBe(token);
    expect(stored.apiKey).toBeUndefined();
  });

  test('still sends the sandbox demo key for the local admin button', () => {
    const stored = setAuthSession({
      role: 'Admin',
      tenantId: PLATFORM_TENANT_ID,
      tenantName: 'Platform Administrator',
      apiKey: 'flowos_prod_secret_key_32_chars_min',
      isSandbox: false,
      isEmailVerified: true
    });

    expect(stored.role).toBe('Tenant');
    expect(stored.tenantId).toBe('22222222-2222-2222-2222-222222222222');
    expect(stored.apiKey).toBe('flowos_prod_secret_key_32_chars_min');
    expect(stored.token).toBeUndefined();
    expect(stored.isSandbox).toBe(true);
  });

  test('keeps a platform administrator on a tenant workspace until they return', () => {
    const payload = btoa(JSON.stringify({
      exp: 4_000_000_000,
      tenant_id: PLATFORM_TENANT_ID,
      role: 'Admin',
      tenant_name: 'Default Tenant'
    }));
    const token = `header.${payload}.signature`;
    const stored = setAuthSession({
      role: 'Tenant',
      tenantId,
      tenantName: 'Acme',
      token,
      isSandbox: false
    });

    expect(stored.role).toBe('Tenant');
    expect(stored.tenantId).toBe(tenantId);
    expect(stored.token).toBe(token);
  });
});

describe('expired platform administrator sign-in', () => {
  const storeRaw = (session: Record<string, unknown>) =>
    localStorage.setItem('flowos_auth_session', JSON.stringify(session));
  const adminToken = (exp: number) =>
    `header.${btoa(JSON.stringify({ exp, role: 'Admin', tenant_id: PLATFORM_TENANT_ID, tenant_name: 'Default Tenant' }))}.signature`;
  const adminSession = (exp: number) => ({
    role: 'Admin',
    tenantId: PLATFORM_TENANT_ID,
    tenantName: 'Default Tenant',
    token: adminToken(exp),
    isSandbox: false
  });

  beforeEach(() => {
    localStorage.clear();
    clearAuthSession();
    takeSessionExpiredNotice();
  });

  test('falls back to a sandbox session that carries the sandbox name, not the admin tenant name', () => {
    storeRaw(adminSession(1));

    const session = getAuthSession();

    expect(session.isSandbox).toBe(true);
    expect(session.role).toBe('Tenant');
    expect(session.tenantId).toBe('22222222-2222-2222-2222-222222222222');
    expect(session.tenantName).toBe('Demo Client Tenant (Sandbox)');
    expect(session.token).toBeUndefined();
  });

  test('reports the lost admin sign-in once to a listener', () => {
    storeRaw(adminSession(1));
    const listener = vi.fn();
    const stop = onSessionExpired(listener);

    getAuthSession();
    getAuthSession();

    expect(listener).toHaveBeenCalledTimes(1);
    stop();
  });

  test('remembers the lost admin sign-in when nobody is listening yet', () => {
    storeRaw(adminSession(1));

    getAuthSession();

    expect(takeSessionExpiredNotice()).toBe(true);
    expect(takeSessionExpiredNotice()).toBe(false);
  });

  test('stays quiet for a valid admin sign-in and for an expired tenant sign-in', () => {
    storeRaw(adminSession(4_000_000_000));
    getAuthSession();
    expect(takeSessionExpiredNotice()).toBe(false);

    localStorage.clear();
    storeRaw({
      role: 'Tenant',
      tenantId,
      tenantName: 'Acme',
      token: `header.${btoa(JSON.stringify({ exp: 1, tenant_id: tenantId, role: 'Admin' }))}.signature`,
      isSandbox: false
    });
    getAuthSession();
    expect(takeSessionExpiredNotice()).toBe(false);
  });
});
