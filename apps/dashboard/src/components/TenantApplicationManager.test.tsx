import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom';
import { vi } from 'vitest';
import { TenantApplicationManager } from './TenantApplicationManager';
import { api } from '../api/client';
import { WorkflowClassStatus } from '../types';

// Applications (1/4-4/4): the dashboard previously had no UI at all for TenantApplication CRUD,
// workflow-to-application assignment, or application-scoped key issuance - see
// docs/14-api-reference.md sections 8, 11, 12 and FlowOsMcpGuidance.cs's "Application scope" note
// that none of this is reachable over MCP either.
vi.mock('../api/client', () => ({
  api: {
    listApplications: vi.fn(),
    createApplication: vi.fn(),
    updateApplication: vi.fn(),
    deleteApplication: vi.fn(),
    assignWorkflowApplication: vi.fn(),
    generateTenantKey: vi.fn(),
  },
}));

const mockApplications = [
  {
    id: 'app-1',
    tenantId: 't-1',
    name: 'Customer Portal',
    description: 'Self-serve portal',
    createdAtUtc: '2026-01-01T00:00:00Z',
    updatedAtUtc: '2026-01-01T00:00:00Z',
    activeKeyCount: 1,
    workflowCount: 1,
  },
];

const mockBlueprints = [
  { id: 'wf-1', tenantId: 't-1', name: 'LoanApproval', version: '1.0.0', status: WorkflowClassStatus.Published, applicationId: 'app-1' },
  { id: 'wf-2', tenantId: 't-1', name: 'KycCheck', version: '1.0.0', status: WorkflowClassStatus.Draft },
] as any;

describe('TenantApplicationManager', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    (api.listApplications as any).mockResolvedValue(mockApplications);
  });

  test('lists applications with their workflow and key counts', async () => {
    render(<TenantApplicationManager tenantId="t-1" tenantName="Test Tenant" blueprints={mockBlueprints} />);

    await waitFor(() => expect(screen.getByText('Customer Portal')).toBeInTheDocument());
    expect(screen.getByText('Self-serve portal')).toBeInTheDocument();
    // workflowCount and activeKeyCount from the DTO, not recomputed from blueprints client-side.
    const row = screen.getByText('Customer Portal').closest('tr')!;
    expect(row.textContent).toContain('1'); // workflowCount
  });

  test('creating an application calls the API and refreshes the list', async () => {
    (api.createApplication as any).mockResolvedValue({ ...mockApplications[0], id: 'app-2', name: 'New App' });
    render(<TenantApplicationManager tenantId="t-1" tenantName="Test Tenant" blueprints={mockBlueprints} />);
    await waitFor(() => expect(api.listApplications).toHaveBeenCalledTimes(1));

    fireEvent.click(screen.getByText('New Application'));
    fireEvent.change(screen.getByPlaceholderText('e.g. Customer Portal'), { target: { value: 'New App' } });
    fireEvent.click(screen.getByText('Create Application'));

    await waitFor(() => expect(api.createApplication).toHaveBeenCalledWith({ name: 'New App', description: undefined }));
    await waitFor(() => expect(api.listApplications).toHaveBeenCalledTimes(2));
  });

  test('deleting an application confirms, then calls deleteApplication', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    (api.deleteApplication as any).mockResolvedValue(undefined);
    render(<TenantApplicationManager tenantId="t-1" tenantName="Test Tenant" blueprints={mockBlueprints} />);
    await waitFor(() => expect(screen.getByText('Customer Portal')).toBeInTheDocument());

    fireEvent.click(screen.getByTitle('Delete application'));

    expect(window.confirm).toHaveBeenCalled();
    await waitFor(() => expect(api.deleteApplication).toHaveBeenCalledWith('app-1'));
  });

  test('assigning an unassigned workflow calls assignWorkflowApplication with the application id', async () => {
    (api.assignWorkflowApplication as any).mockResolvedValue({ workflowClassId: 'wf-2', applicationId: 'app-1', versionsMoved: 1 });
    const onBlueprintsChanged = vi.fn();
    render(
      <TenantApplicationManager
        tenantId="t-1"
        tenantName="Test Tenant"
        blueprints={mockBlueprints}
        onBlueprintsChanged={onBlueprintsChanged}
      />
    );
    await waitFor(() => expect(screen.getByText('Customer Portal')).toBeInTheDocument());

    // Expand the row to reveal the assign control.
    fireEvent.click(screen.getByTitle('Expand'));
    fireEvent.change(screen.getByText('Assign an existing workflow…').closest('select')!, { target: { value: 'wf-2' } });
    fireEvent.click(screen.getByText('Assign'));

    await waitFor(() => expect(api.assignWorkflowApplication).toHaveBeenCalledWith('wf-2', 'app-1'));
    await waitFor(() => expect(onBlueprintsChanged).toHaveBeenCalled());
  });

  test('generating an application-scoped key sends the application id and shows the one-time key', async () => {
    (api.generateTenantKey as any).mockResolvedValue({
      id: 'key-1',
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
    render(<TenantApplicationManager tenantId="t-1" tenantName="Test Tenant" blueprints={mockBlueprints} />);
    await waitFor(() => expect(screen.getByText('Customer Portal')).toBeInTheDocument());

    fireEvent.click(screen.getByTitle('Generate an application-scoped key'));
    fireEvent.click(screen.getByText('Generate Key'));

    await waitFor(() => expect(api.generateTenantKey).toHaveBeenCalledWith(
      't-1', 'App Key', undefined, 'Production', expect.any(Array), undefined, 'app-1'
    ));
    await waitFor(() => expect(screen.getByText('flowos_live_full_key_value')).toBeInTheDocument());
  });
});
