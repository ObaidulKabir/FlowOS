import { act, fireEvent, render, screen } from '@testing-library/react';
import '@testing-library/jest-dom';
import { beforeEach, describe, expect, test, vi } from 'vitest';
import App from './App';
import { clearAuthSession, getAuthSession, PLATFORM_TENANT_ID, setAuthSession, takeSessionExpiredNotice } from './api/client';

vi.mock('./mcpUrl', () => ({
  mcpRpcUrl: () => 'https://example.test/mcp'
}));

vi.mock('./components/LandingPage', () => ({
  LandingPage: () => <div>Landing</div>
}));

vi.mock('./components/AuthModal', () => ({
  AuthModal: ({ isOpen, notice }: { isOpen: boolean; notice?: string }) =>
    isOpen ? <div role="dialog">{notice ?? 'Sign in'}</div> : null
}));

vi.mock('./components/AdminDashboard', () => ({
  AdminDashboard: ({ onSwitchRole }: { onSwitchRole: (tenantContext?: { tenantId: string; tenantName?: string }) => void }) => (
    <>
      <button type="button" onClick={() => onSwitchRole()}>
        Switch To Tenant
      </button>
    </>
  )
}));

vi.mock('./components/TenantDashboard', () => ({
  TenantDashboard: ({ session, onSwitchRole }: { session: { tenantId: string; tenantName: string }; onSwitchRole: () => void }) => (
    <div>
      <div data-testid="tenant-id">{session.tenantId}</div>
      <div data-testid="tenant-name">{session.tenantName}</div>
      <button type="button" onClick={onSwitchRole}>Return to admin</button>
    </div>
  )
}));

describe('App tenant context switching', () => {
  beforeEach(() => {
    localStorage.clear();
    clearAuthSession();
    window.scrollTo = vi.fn();
  });

  test('does not open the admin panel for a demo key labeled as admin', () => {
    setAuthSession({
      role: 'Admin',
      tenantId: '11111111-1111-1111-1111-111111111111',
      tenantName: 'Platform Administrator',
      apiKey: 'flowos_prod_secret_key_32_chars_min',
      username: 'superadmin@flowos.internal',
      isSandbox: false,
      isEmailVerified: true
    });

    render(<App />);

    expect(screen.getByText('Landing')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /switch to tenant/i })).not.toBeInTheDocument();
  });

  const jwt = (payload: Record<string, unknown>) =>
    `header.${btoa(JSON.stringify(payload))}.signature`;
  const storeRaw = (session: Record<string, unknown>) =>
    localStorage.setItem('flowos_auth_session', JSON.stringify(session));
  const adminSession = (exp: number, tenantName?: string) => ({
    role: 'Admin',
    tenantId: PLATFORM_TENANT_ID,
    tenantName: tenantName ?? 'Platform Administrator',
    token: jwt({ exp, role: 'Admin', tenant_id: PLATFORM_TENANT_ID, ...(tenantName ? { tenant_name: tenantName } : {}) }),
    isSandbox: false
  });

  test('tells an administrator whose sign-in expired before the page loaded to sign in again', () => {
    storeRaw(adminSession(1, 'Default Tenant'));
    takeSessionExpiredNotice();

    render(<App />);

    expect(screen.getByText('Landing')).toBeInTheDocument();
    expect(screen.getByRole('dialog')).toHaveTextContent('Your platform admin session expired');
  });

  test('tells an administrator whose sign-in expires while the panel is open', () => {
    storeRaw(adminSession(4_000_000_000, 'Default Tenant'));
    takeSessionExpiredNotice();
    render(<App />);
    expect(screen.getByRole('button', { name: /switch to tenant/i })).toBeInTheDocument();

    storeRaw(adminSession(1, 'Default Tenant'));
    act(() => { getAuthSession(); });

    expect(screen.getByText('Landing')).toBeInTheDocument();
    expect(screen.getByRole('dialog')).toHaveTextContent('Your platform admin session expired');
  });

  test('explains why the sign-in form opens when the admin panel is no longer available', () => {
    storeRaw({
      role: 'Tenant',
      tenantId: 'd3e8e2fd-1a8d-416a-987e-0585937090fd',
      tenantName: 'Acme',
      apiKey: 'tenant-key',
      isSandbox: false
    });
    render(<App />);

    fireEvent.click(screen.getByRole('button', { name: 'Return to admin' }));

    expect(screen.getByRole('dialog')).toHaveTextContent('administrator sign-in');
  });

  test('names the platform tenant workspace instead of showing the sandbox name', () => {
    storeRaw(adminSession(4_000_000_000));
    render(<App />);

    fireEvent.click(screen.getByRole('button', { name: /switch to tenant/i }));

    expect(screen.getByTestId('tenant-id')).toHaveTextContent(PLATFORM_TENANT_ID);
    expect(screen.getByTestId('tenant-name')).toHaveTextContent('Platform tenant');
  });

  test('keeps the tenant name the token carries', () => {
    storeRaw(adminSession(4_000_000_000, 'Default Tenant'));
    render(<App />);

    fireEvent.click(screen.getByRole('button', { name: /switch to tenant/i }));

    expect(screen.getByTestId('tenant-name')).toHaveTextContent('Default Tenant');
  });
});
