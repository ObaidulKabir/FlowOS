import { render, screen, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom';
import { beforeEach, describe, expect, test, vi } from 'vitest';
import App from './App';
import { clearAuthSession, getAuthSession, setAuthSession } from './api/client';

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
  AdminDashboard: ({ onSwitchRole }: { onSwitchRole: () => void }) => (
    <button type="button" onClick={onSwitchRole}>
      Switch To Tenant
    </button>
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

  test('preserves the latest selected tenant when switching from admin to tenant view', () => {
    setAuthSession({
      role: 'Admin',
      tenantId: '22222222-2222-2222-2222-222222222222',
      tenantName: 'Platform Administrator',
      apiKey: 'flowos_prod_secret_key_32_chars_min',
      username: 'superadmin@flowos.internal',
      isSandbox: false,
      isEmailVerified: true
    });

    render(<App />);

    setAuthSession({
      ...getAuthSession(),
      role: 'Admin',
      tenantId: 'd3e8e2fd-1a8d-416a-987e-0585937090fd',
      tenantName: 'Prospectbd Software',
      apiKey: 'flowos_prod_secret_key_32_chars_min',
      isSandbox: false
    });

    fireEvent.click(screen.getByRole('button', { name: /switch to tenant/i }));

    expect(screen.getByTestId('tenant-id')).toHaveTextContent('d3e8e2fd-1a8d-416a-987e-0585937090fd');
    expect(screen.getByTestId('tenant-name')).toHaveTextContent('Prospectbd Software');
  });
});
