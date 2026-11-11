import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom';
import { vi } from 'vitest';
import { TenantTeamManager } from './TenantTeamManager';
import { api } from '../api/client';

// Organizational team access (see docs/29-organizational-team-access.md): invite/list/remove
// teammate logins (TenantUsers) of one tenant, distinct from TenantApiKeyManager's shared keys.
vi.mock('../api/client', () => ({
  api: {
    listTeammates: vi.fn(),
    inviteTeammate: vi.fn(),
    removeTeammate: vi.fn(),
  },
}));

const mockTeammates = [
  {
    id: 'user-1',
    email: 'admin@acme.com',
    fullName: 'Acme Admin',
    role: 'Admin',
    isEmailVerified: true,
    createdAt: '2026-01-01T00:00:00Z',
    lastLoginAt: '2026-01-05T00:00:00Z',
  },
  {
    id: 'user-2',
    email: 'pending@acme.com',
    fullName: 'Pending Teammate',
    role: 'Employee',
    isEmailVerified: false,
    createdAt: '2026-01-02T00:00:00Z',
  },
];

describe('TenantTeamManager', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    (api.listTeammates as any).mockResolvedValue(mockTeammates);
  });

  test('lists teammates with role and verification status', async () => {
    render(<TenantTeamManager tenantId="t-1" tenantName="Acme Corp" currentUserRole="Admin" />);

    await waitFor(() => expect(screen.getByText('Acme Admin')).toBeInTheDocument());
    expect(screen.getByText('Pending Teammate')).toBeInTheDocument();
    expect(screen.getByText('Active')).toBeInTheDocument();
    expect(screen.getByText('Invite Pending')).toBeInTheDocument();
  });

  test('an Admin can invite a new teammate and sees the invite code', async () => {
    (api.inviteTeammate as any).mockResolvedValue({
      ok: true,
      message: 'Invited',
      userId: 'user-3',
      email: 'new@acme.com',
      inviteToken: 'ABC123',
    });
    render(<TenantTeamManager tenantId="t-1" tenantName="Acme Corp" currentUserRole="Admin" />);
    await waitFor(() => expect(api.listTeammates).toHaveBeenCalledTimes(1));

    fireEvent.click(screen.getByText('Invite Teammate'));
    fireEvent.change(screen.getByPlaceholderText('coworker@yourcompany.com'), { target: { value: 'new@acme.com' } });
    fireEvent.click(screen.getByText('Send Invite'));

    await waitFor(() => expect(api.inviteTeammate).toHaveBeenCalledWith('t-1', {
      email: 'new@acme.com',
      fullName: '',
      role: 'Employee',
    }));
    await waitFor(() => expect(screen.getByText('ABC123')).toBeInTheDocument());
    await waitFor(() => expect(api.listTeammates).toHaveBeenCalledTimes(2));
  });

  test('a non-admin teammate cannot invite or remove, but can still see the roster', async () => {
    render(<TenantTeamManager tenantId="t-1" tenantName="Acme Corp" currentUserRole="Employee" />);
    await waitFor(() => expect(screen.getByText('Acme Admin')).toBeInTheDocument());

    expect(screen.getByText('Invite Teammate').closest('button')).toBeDisabled();
    const removeButtons = screen.getAllByTitle('Only tenant Admins can remove teammates');
    expect(removeButtons.length).toBeGreaterThan(0);
    removeButtons.forEach(btn => expect(btn).toBeDisabled());
  });

  test('removing a teammate confirms, then calls removeTeammate', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    (api.removeTeammate as any).mockResolvedValue(undefined);
    render(<TenantTeamManager tenantId="t-1" tenantName="Acme Corp" currentUserRole="Admin" />);
    await waitFor(() => expect(screen.getByText('Pending Teammate')).toBeInTheDocument());

    const row = screen.getByText('Pending Teammate').closest('tr')!;
    fireEvent.click(row.querySelector('button')!);

    expect(window.confirm).toHaveBeenCalled();
    await waitFor(() => expect(api.removeTeammate).toHaveBeenCalledWith('t-1', 'user-2'));
  });

  test('a 403 from the API shows the friendly forbidden message instead of a raw error', async () => {
    (api.listTeammates as any).mockRejectedValue(new Error('Failed to list teammates (HTTP 403 Forbidden)'));
    render(<TenantTeamManager tenantId="t-1" tenantName="Acme Corp" currentUserRole="Employee" />);

    await waitFor(() => expect(screen.getByText(/Only tenant Admins can manage teammates/)).toBeInTheDocument());
  });
});
