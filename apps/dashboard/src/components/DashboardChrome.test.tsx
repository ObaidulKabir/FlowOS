import { render, screen } from '@testing-library/react';
import '@testing-library/jest-dom';
import { beforeEach, describe, expect, test, vi } from 'vitest';
import { DashboardChrome } from './DashboardChrome';
import { AuthSession } from '../types';
import { PLATFORM_TENANT_ID } from '../auth/platformAdminSession';

const jwt = (payload: Record<string, unknown>) =>
  `header.${btoa(JSON.stringify(payload))}.signature`;

const session = (overrides: Partial<AuthSession>): AuthSession => ({
  role: 'Tenant',
  tenantId: 'd3e8e2fd-1a8d-416a-987e-0585937090fd',
  tenantName: 'Acme',
  isSandbox: false,
  ...overrides
});

const renderChrome = (current: AuthSession) => render(
  <DashboardChrome
    session={current}
    onGoHome={vi.fn()}
    onSwitchRole={vi.fn()}
    onRegister={vi.fn()}
    onSignOut={vi.fn()}
    mcpUrl="https://example.test/mcp"
    nav={<div>Nav</div>}
  />
);

describe('DashboardChrome account switch', () => {
  beforeEach(() => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(null, { status: 404 }));
  });

  test('sandbox has no admin switch', () => {
    renderChrome(session({
      tenantId: '22222222-2222-2222-2222-222222222222',
      tenantName: 'Demo Client Tenant (Sandbox)',
      apiKey: 'flowos_prod_secret_key_32_chars_min',
      isSandbox: true
    }));

    expect(screen.queryByRole('button', { name: /admin/i })).not.toBeInTheDocument();
    expect(screen.getByText('Sandbox Guest')).toBeInTheDocument();
  });

  test('a tenant sign-in has no admin switch', () => {
    renderChrome(session({
      token: jwt({ exp: 4_000_000_000, tenant_id: 'd3e8e2fd-1a8d-416a-987e-0585937090fd', role: 'Admin' })
    }));

    expect(screen.queryByRole('button', { name: /admin/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /tenant workspace/i })).not.toBeInTheDocument();
  });

  test('a platform administrator browsing a tenant can return to the admin panel', () => {
    renderChrome(session({
      token: jwt({
        exp: 4_000_000_000,
        tenant_id: PLATFORM_TENANT_ID,
        role: 'Admin',
        tenant_name: 'Default Tenant'
      })
    }));

    expect(screen.getByRole('button', { name: 'Platform admin' })).toBeInTheDocument();
  });

  test('the admin panel offers a tenant workspace instead of a second login', () => {
    renderChrome(session({
      role: 'Admin',
      tenantId: PLATFORM_TENANT_ID,
      tenantName: 'Default Tenant',
      token: jwt({ exp: 4_000_000_000, tenant_id: PLATFORM_TENANT_ID, role: 'Admin' })
    }));

    expect(screen.getByRole('button', { name: 'Tenant workspace' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Platform admin' })).not.toBeInTheDocument();
  });
});
