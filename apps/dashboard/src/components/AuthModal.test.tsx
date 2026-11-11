import { fireEvent, render, screen } from '@testing-library/react';
import '@testing-library/jest-dom';
import { beforeEach, describe, expect, test, vi } from 'vitest';
import { AuthModal, dashboardRoleForLogin } from './AuthModal';
import { clearAuthSession, getStoredSession, PLATFORM_TENANT_ID } from '../api/client';

describe('API key sign-in', () => {
  beforeEach(() => {
    localStorage.clear();
    clearAuthSession();
  });

  test('does not create an unauthenticated real-tenant session without a key', () => {
    const onSuccess = vi.fn();
    render(
      <AuthModal
        isOpen
        onClose={vi.fn()}
        onSuccess={onSuccess}
        onLaunchSandbox={vi.fn()}
      />
    );
    fireEvent.click(screen.getByRole('button', { name: 'API Key / UUID' }));
    fireEvent.change(screen.getByPlaceholderText('<YOUR_TENANT_ID>'), {
      target: { value: 'd3e8e2fd-1a8d-416a-987e-0585937090fd' }
    });
    const keyInput = screen.getByPlaceholderText('<YOUR_MCP_API_KEY>');
    expect(keyInput).toHaveAttribute('type', 'password');
    expect(keyInput).toBeRequired();

    fireEvent.submit(keyInput.closest('form')!);

    expect(screen.getByText('Tenant API key is required.')).toBeInTheDocument();
    expect(onSuccess).not.toHaveBeenCalled();
    expect(getStoredSession()).toBeNull();
  });
});

describe('sign-in notice and dashboard routing', () => {
  test('shows the notice it is given on the sign-in form', () => {
    render(
      <AuthModal
        isOpen
        notice="Your platform admin session expired. Sign in again to continue."
        onClose={vi.fn()}
        onSuccess={vi.fn()}
        onLaunchSandbox={vi.fn()}
      />
    );

    expect(screen.getByRole('status')).toHaveTextContent('Your platform admin session expired');
  });

  test('opens the admin panel for Admin and SuperAdmin on the platform tenant only', () => {
    expect(dashboardRoleForLogin('Admin', PLATFORM_TENANT_ID)).toBe('Admin');
    expect(dashboardRoleForLogin('SuperAdmin', PLATFORM_TENANT_ID.toUpperCase())).toBe('Admin');
    expect(dashboardRoleForLogin('Admin', 'd3e8e2fd-1a8d-416a-987e-0585937090fd')).toBe('Tenant');
    expect(dashboardRoleForLogin('Member', PLATFORM_TENANT_ID)).toBe('Tenant');
  });
});

// Organizational team access (see docs/29-organizational-team-access.md): a teammate who was invited
// into an existing tenant accepts their invite and sets their own password from this modal.
describe('accept teammate invite', () => {
  test('the sign-in form links to the accept-invite form, which requires an email, code and password', () => {
    render(
      <AuthModal
        isOpen
        onClose={vi.fn()}
        onSuccess={vi.fn()}
        onLaunchSandbox={vi.fn()}
      />
    );

    fireEvent.click(screen.getByText('Accept your invite'));

    expect(screen.getByText('Accept Teammate Invite')).toBeInTheDocument();
    expect(screen.getByPlaceholderText('you@yourcompany.com')).toBeRequired();
    expect(screen.getByPlaceholderText('123456')).toBeRequired();

    fireEvent.click(screen.getByText('Accept Invite & Sign In'));
    // Required-field validation stops the native form submit before our handler runs.
    expect(screen.queryByText('Invite accepted! Signing you in...')).not.toBeInTheDocument();
  });
});
