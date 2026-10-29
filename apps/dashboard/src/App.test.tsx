import { render, screen } from '@testing-library/react';
import '@testing-library/jest-dom';
import { beforeEach, describe, expect, test, vi } from 'vitest';
import App from './App';
import { clearAuthSession, setAuthSession } from './api/client';

vi.mock('./mcpUrl', () => ({
  mcpRpcUrl: () => 'https://example.test/mcp'
}));

vi.mock('./components/LandingPage', () => ({
  LandingPage: () => <div>Landing</div>
}));

vi.mock('./components/AuthModal', () => ({
  AuthModal: () => null
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
  TenantDashboard: ({ session }: { session: { tenantId: string; tenantName: string } }) => (
    <div>
      <div data-testid="tenant-id">{session.tenantId}</div>
      <div data-testid="tenant-name">{session.tenantName}</div>
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
});
