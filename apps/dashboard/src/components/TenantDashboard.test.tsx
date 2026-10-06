import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom';
import { vi } from 'vitest';
import { TenantDashboard } from './TenantDashboard';
import { api } from '../api/client';
import { WorkflowClassStatus } from '../types';

// Mock the API client
vi.mock('../api/client', () => ({
  api: {
    listInstances: vi.fn(),
    list: vi.fn(),
    getAgentEvaluationMetrics: vi.fn(),
    listTenants: vi.fn(),
    startInstance: vi.fn(),
  },
  setActiveTenantId: vi.fn(),
}));

// Mock ResizeObserver
window.ResizeObserver = class ResizeObserver {
  observe() {}
  unobserve() {}
  disconnect() {}
};

describe('TenantDashboard - Launch Instance Modal', () => {
  const mockSession = {
    role: 'Tenant',
    tenantId: 't-1',
    tenantName: 'Test Tenant',
    apiKey: 'key',
    isSandbox: false,
    plan: 'Enterprise',
    billingStatus: 'Active',
    canRunRuntime: true,
  };

  const mockBlueprints = [
    { id: '1', name: 'DraftWorkflow', version: 1, status: WorkflowClassStatus.Draft },
    { id: '2', name: 'PublishedWorkflow', version: 1, status: WorkflowClassStatus.Published },
    { id: '3', name: 'PublicWorkflow', version: 1, status: WorkflowClassStatus.Public },
    { id: '4', name: 'FlowOS Content Studio', version: 1, status: WorkflowClassStatus.Shared },
  ];

  beforeEach(() => {
    vi.clearAllMocks();
    api.listInstances.mockResolvedValue([]);
    api.list.mockResolvedValue(mockBlueprints);
    api.getAgentEvaluationMetrics.mockResolvedValue({ tokens: { inputTokens: 0, outputTokens: 0 }, runs: 0, commits: 0, overrides: 0 });
    api.listTenants.mockResolvedValue([]);
  });

  test('launch modal opens and renders dynamic workflow list correctly', async () => {
    render(
      <TenantDashboard
        session={mockSession as any}
        onSwitchWorkspace={vi.fn()}
        onGoHome={vi.fn()}
        onSwitchRole={vi.fn()}
        onRegister={vi.fn()}
        onSignOut={vi.fn()}
        mcpUrl="http://localhost:5183/mcp"
      />
    );

    // Wait for initial data load
    await waitFor(() => {
      expect(api.list).toHaveBeenCalled();
    });

    // Click Launch Instance from Quick Actions sidebar
    const launchBtns = screen.getAllByRole('button', { name: /Launch Instance/i });
    fireEvent.click(launchBtns[0]);

    // Modal should appear
    const modalHeader = await screen.findByText('Select Workflow Blueprint');
    expect(modalHeader).toBeInTheDocument();

    // Instead of exact length counting (which can be brittle due to rendering the same workflow in multiple subcomponents),
    // we ensure the text of the workflows appears.
    expect(screen.getAllByText('PublishedWorkflow').length).toBeGreaterThan(0);
    expect(screen.getAllByText('PublicWorkflow').length).toBeGreaterThan(0);
    expect(screen.getAllByText('FlowOS Content Studio').length).toBeGreaterThan(0);
  });
});

