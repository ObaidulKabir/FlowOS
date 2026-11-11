import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom';
import { vi } from 'vitest';
import { TenantApiKeyManager } from './TenantApiKeyManager';
import { api } from '../api/client';

// Organizational team access (see docs/29-organizational-team-access.md): a key now records who
// generated it, and a non-admin teammate is forbidden from managing tenant API keys at all.
vi.mock('../api/client', () => ({
  api: {
    listTenantKeys: vi.fn(),
    generateTenantKey: vi.fn(),
    revokeTenantKey: vi.fn(),
  },
}));

vi.mock('../mcpUrl', () => ({ mcpRpcUrl: () => 'https://example.test/mcp' }));

const mockKeys = [
  {
    id: 'key-1',
    name: 'Billing Sync',
    applicationName: 'Default Application',
    environment: 'Production',
    scopes: ['*'],
    maskedKey: 'flw_live_••••••••abcd',
    keyPrefix: 'flw_live_',
    createdAt: '2026-01-01T00:00:00Z',
    isRevoked: false,
    isExpired: false,
    isActive: true,
    createdByUserId: 'user-1',
    createdByName: 'Acme Admin',
    createdByEmail: 'admin@acme.com',
  },
  {
    id: 'key-2',
    name: 'Legacy Key',
    applicationName: 'Default Application',
    environment: 'Production',
    scopes: ['*'],
    maskedKey: 'flw_live_••••••••wxyz',
    keyPrefix: 'flw_live_',
    createdAt: '2025-06-01T00:00:00Z',
    isRevoked: false,
    isExpired: false,
    isActive: true,
  },
];

describe('TenantApiKeyManager', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  test('shows who generated each key, or "Shared credential" when there is no creator on record', async () => {
    (api.listTenantKeys as any).mockResolvedValue(mockKeys);
    render(<TenantApiKeyManager tenantId="t-1" tenantName="Acme Corp" />);

    await waitFor(() => expect(screen.getByText('Billing Sync')).toBeInTheDocument());
    expect(screen.getByText('Acme Admin')).toBeInTheDocument();
    expect(screen.getByText('admin@acme.com')).toBeInTheDocument();
    expect(screen.getByText('Shared credential')).toBeInTheDocument();
  });

  test('a 403 from the API disables key management and shows a friendly message', async () => {
    (api.listTenantKeys as any).mockRejectedValue(new Error('Failed to list tenant keys (HTTP 403 Forbidden)'));
    render(<TenantApiKeyManager tenantId="t-1" tenantName="Acme Corp" />);

    await waitFor(() => expect(screen.getByText(/Only tenant Admins can manage API keys/)).toBeInTheDocument());
    expect(screen.getByText('Generate New Application Key').closest('button')).toBeDisabled();
  });
});
