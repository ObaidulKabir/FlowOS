import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom';
import { beforeEach, describe, expect, test, vi } from 'vitest';
import { TenantManager } from './TenantManager';

const { apiMock, setActiveTenantIdMock } = vi.hoisted(() => ({
  apiMock: {
    listTenants: vi.fn(),
    listPlanRequests: vi.fn()
  },
  setActiveTenantIdMock: vi.fn()
}));

vi.mock('../api/client', () => ({
  api: apiMock,
  getActiveTenantId: () => '11111111-1111-1111-1111-111111111111',
  setActiveTenantId: setActiveTenantIdMock
}));

vi.mock('./ApiKeyScopePicker', () => ({ ApiKeyScopePicker: () => null }));

describe('TenantManager admin buttons', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    apiMock.listTenants.mockResolvedValue([
      { tenantId: 'aaaaaaaa-0000-0000-0000-000000000001', name: 'Acme Ltd', status: 'Active', plan: 'Managed', billingStatus: 'Active', canRunRuntime: true, apiKeys: [] }
    ]);
    apiMock.listPlanRequests.mockResolvedValue([]);
  });

  test('View Workflows scopes the admin view without rewriting the admin session', async () => {
    const onViewTenantWorkflows = vi.fn();
    const onTenantChange = vi.fn();
    render(<TenantManager onViewTenantWorkflows={onViewTenantWorkflows} onTenantChange={onTenantChange} />);

    fireEvent.click(await screen.findByRole('button', { name: /view workflows/i }));

    expect(onViewTenantWorkflows).toHaveBeenCalledWith('aaaaaaaa-0000-0000-0000-000000000001');
    expect(onTenantChange).toHaveBeenCalledWith('aaaaaaaa-0000-0000-0000-000000000001');
    expect(setActiveTenantIdMock).not.toHaveBeenCalled();
  });

  test('Select tenant only marks the tenant as in scope', async () => {
    render(<TenantManager />);

    fireEvent.click(await screen.findByRole('button', { name: /select tenant/i }));

    await waitFor(() => expect(screen.getByText(/in scope/i)).toBeInTheDocument());
    expect(setActiveTenantIdMock).not.toHaveBeenCalled();
  });
});
