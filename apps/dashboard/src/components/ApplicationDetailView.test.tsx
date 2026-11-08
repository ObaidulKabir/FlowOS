import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom';
import { vi } from 'vitest';
import { ApplicationDetailView } from './ApplicationDetailView';
import { api } from '../api/client';

// Applications (4/4): the mockup's Tenant -> Application -> Workflows/AI Context/API Keys
// drill-down. ApplicationWorkspace and AiContextView are exercised by their own test files -
// here we stub them to verify ApplicationDetailView wires applicationId/lockedApplicationId
// through correctly, and test the API Keys tab (new in this view) directly.
vi.mock('../api/client', () => ({
  api: {
    listTenantKeys: vi.fn(),
    generateTenantKey: vi.fn(),
    revokeTenantKey: vi.fn(),
  },
}));

vi.mock('./ApplicationWorkspace', () => ({
  ApplicationWorkspace: (props: any) => (
    <div data-testid="workspace-stub">locked:{props.lockedApplicationId}</div>
  ),
}));

vi.mock('./AiContextView', () => ({
  AiContextView: (props: any) => (
    <div data-testid="ai-context-stub">app:{props.applicationId}</div>
  ),
}));

const mockApplication = {
  id: 'app-1',
  tenantId: 't-1',
  name: 'Customer Portal',
  description: 'Self-serve portal',
  createdAtUtc: '2026-01-01T00:00:00Z',
  updatedAtUtc: '2026-01-01T00:00:00Z',
  activeKeyCount: 1,
  workflowCount: 2,
};

const baseProps = {
  application: mockApplication,
  tenantId: 't-1',
  tenantName: 'Test Tenant',
  blueprints: [],
  instances: [],
  onBack: vi.fn(),
  onView: vi.fn(),
  onEdit: vi.fn(),
  onCreate: vi.fn(),
  onDelete: vi.fn(),
  onPublish: vi.fn(),
  onSubmit: vi.fn(),
  onWithdraw: vi.fn(),
  onDeprecate: vi.fn(),
  onAbandon: vi.fn(),
  onNewVersion: vi.fn(),
  onSimulate: vi.fn(),
  onLaunch: vi.fn(),
};

describe('ApplicationDetailView', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    (api.listTenantKeys as any).mockResolvedValue([]);
  });

  test('shows the breadcrumb, header stats, and locks the Workflows tab to this application', async () => {
    render(<ApplicationDetailView {...baseProps} />);

    expect(screen.getByText('Customer Portal')).toBeInTheDocument();
    expect(screen.getByText('Self-serve portal')).toBeInTheDocument();
    expect(screen.getByText('2')).toBeInTheDocument(); // workflowCount stat pill
    expect(screen.getByText('1')).toBeInTheDocument(); // activeKeyCount stat pill

    expect(screen.getByTestId('workspace-stub').textContent).toBe('locked:app-1');

    fireEvent.click(screen.getByText('Applications'));
    expect(baseProps.onBack).toHaveBeenCalled();
  });

  test('AI Context tab renders AiContextView pre-filtered to this application', async () => {
    render(<ApplicationDetailView {...baseProps} />);

    fireEvent.click(screen.getByText('AI Context'));
    expect(screen.getByTestId('ai-context-stub').textContent).toBe('app:app-1');
  });

  test('API Keys tab lists only this application\'s keys from the tenant-wide key list', async () => {
    (api.listTenantKeys as any).mockResolvedValue([
      { id: 'key-1', name: 'App Key', applicationId: 'app-1', maskedKey: 'flowos_live_****', createdAt: '2026-01-01T00:00:00Z', isRevoked: false },
      { id: 'key-2', name: 'Other App Key', applicationId: 'app-2', maskedKey: 'flowos_live_****', createdAt: '2026-01-01T00:00:00Z', isRevoked: false },
      { id: 'key-3', name: 'Unassigned Key', maskedKey: 'flowos_live_****', createdAt: '2026-01-01T00:00:00Z', isRevoked: false },
    ]);
    render(<ApplicationDetailView {...baseProps} />);

    fireEvent.click(screen.getByText('API Keys'));

    await waitFor(() => expect(screen.getByText('App Key')).toBeInTheDocument());
    expect(screen.queryByText('Other App Key')).not.toBeInTheDocument();
    expect(screen.queryByText('Unassigned Key')).not.toBeInTheDocument();
  });

  test('generating a key in the API Keys tab sends this application\'s id and shows the one-time key', async () => {
    (api.generateTenantKey as any).mockResolvedValue({
      id: 'key-new',
      tenantId: 't-1',
      name: 'App Key',
      applicationName: 'Customer Portal',
      applicationId: 'app-1',
      isApplicationRestricted: true,
      environment: 'Production',
      scopes: ['*'],
      apiKey: 'flowos_live_full_key_value',
      maskedKey: 'flowos_live_****',
      createdAt: '2026-01-01T00:00:00Z',
    });
    const onRefreshApplications = vi.fn();
    render(<ApplicationDetailView {...baseProps} onRefreshApplications={onRefreshApplications} />);

    fireEvent.click(screen.getByText('API Keys'));
    await waitFor(() => expect(api.listTenantKeys).toHaveBeenCalled());

    fireEvent.click(screen.getByText('Generate key'));
    fireEvent.click(screen.getByText('Generate Key'));

    await waitFor(() => expect(api.generateTenantKey).toHaveBeenCalledWith(
      't-1', 'App Key', undefined, 'Production', expect.any(Array), undefined, 'app-1'
    ));
    await waitFor(() => expect(screen.getByText('flowos_live_full_key_value')).toBeInTheDocument());
    await waitFor(() => expect(onRefreshApplications).toHaveBeenCalled());
  });

  test('revoking a key confirms, then calls revokeTenantKey', async () => {
    (api.listTenantKeys as any).mockResolvedValue([
      { id: 'key-1', name: 'App Key', applicationId: 'app-1', maskedKey: 'flowos_live_****', createdAt: '2026-01-01T00:00:00Z', isRevoked: false },
    ]);
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    (api.revokeTenantKey as any).mockResolvedValue(undefined);
    render(<ApplicationDetailView {...baseProps} />);

    fireEvent.click(screen.getByText('API Keys'));
    await waitFor(() => expect(screen.getByText('App Key')).toBeInTheDocument());

    fireEvent.click(screen.getByText('Revoke'));

    expect(window.confirm).toHaveBeenCalled();
    await waitFor(() => expect(api.revokeTenantKey).toHaveBeenCalledWith('t-1', 'key-1'));
  });
});
