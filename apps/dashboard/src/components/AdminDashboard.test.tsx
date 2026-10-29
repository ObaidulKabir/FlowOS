import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, test, vi } from 'vitest';
import { AdminDashboard } from './AdminDashboard';

const { apiMock } = vi.hoisted(() => ({
  apiMock: {
    listTenants: vi.fn(),
    list: vi.fn(),
    listInstances: vi.fn(),
    approve: vi.fn(),
    deprecate: vi.fn(),
    abandon: vi.fn(),
    get: vi.fn(),
    validate: vi.fn()
  }
}));

vi.mock('../api/client', () => ({
  api: apiMock
}));

vi.mock('./TenantManager', () => ({
  TenantManager: () => <div>Tenant Manager</div>
}));

vi.mock('./WorkflowTable', () => ({
  WorkflowTable: ({ items, onView }: { items: { id: string; name: string }[]; onView: (id: string) => void }) => (
    <div>
      {items.map(item => (
        <button key={item.id} type="button" onClick={() => void onView(item.id)}>
          {`View ${item.name}`}
        </button>
      ))}
    </div>
  )
}));

vi.mock('./WorkflowInstanceTable', () => ({
  WorkflowInstanceTable: () => <div>Workflow Instances</div>
}));

vi.mock('./EventAuditViewer', () => ({
  EventAuditViewer: () => <div>Event Audit</div>
}));

vi.mock('./DetailView', () => ({
  DetailView: ({ item, validation, onClose, onValidate }: {
    item: { name: string };
    validation?: { isValid: boolean } | null;
    onClose: () => void;
    onValidate: () => void;
  }) => (
    <div data-testid="detail">
      <span>{item.name}</span>
      <span data-testid="validation">{validation ? 'validated' : 'no-validation'}</span>
      <button type="button" onClick={() => void onValidate()}>Validate</button>
      <button type="button" onClick={onClose}>Close</button>
    </div>
  )
}));

vi.mock('./DeadLetterQueueViewer', () => ({
  DeadLetterQueueViewer: () => <div>Dead Letters</div>
}));

vi.mock('./CapabilitiesShowcase', () => ({
  CapabilitiesShowcase: () => <div>Capabilities</div>
}));

vi.mock('./CompetitiveComparison', () => ({
  CompetitiveComparison: () => <div>Comparison</div>
}));

vi.mock('./DashboardChrome', () => ({
  DashboardChrome: ({ nav }: { nav: ReactNode }) => <div>{nav}</div>,
  sidebarItemClass: () => ''
}));

vi.mock('./McpAgentGuideline', () => ({
  McpAgentGuideline: () => <div>MCP Guideline</div>
}));

vi.mock('../platformMetrics', () => ({
  usePlatformMetrics: () => ({
    mcpTools: 0,
    isLiveMcpCount: false,
    tests: { total: 0, unit: 0, endToEnd: 0, mcp: 0 },
    verifiedOn: '2026-10-07'
  })
}));

vi.mock('../audit/resolveWorkflowInstance', () => ({
  resolveWorkflowInstanceId: () => null
}));

vi.mock('../legalEntity', () => ({
  legalEntity: {
    productName: 'FlowOS',
    supportEmail: 'support@example.test'
  },
  sellerIdentity: 'Example Seller'
}));

vi.mock('./LegalFooterLinks', () => ({
  LegalFooterLinks: () => <div>Legal Links</div>
}));

describe('AdminDashboard tenant selection', () => {
  const session = {
    role: 'Admin' as const,
    tenantId: '22222222-2222-2222-2222-222222222222',
    tenantName: 'Platform Administrator',
    apiKey: 'flowos_prod_secret_key_32_chars_min',
    username: 'superadmin@flowos.internal',
    isSandbox: false,
    isEmailVerified: true
  };

  beforeEach(() => {
    vi.clearAllMocks();
    vi.stubGlobal('alert', vi.fn());
    apiMock.listTenants.mockResolvedValue([
      { tenantId: 'd3e8e2fd-1a8d-416a-987e-0585937090fd', name: 'Prospectbd Software' }
    ]);
    apiMock.list.mockResolvedValue([]);
    apiMock.listInstances.mockResolvedValue([]);
  });

  test('loads admin workflows using the session tenant and exposes a concrete session label', async () => {
    render(
      <AdminDashboard
        session={session}
        onGoHome={vi.fn()}
        onSwitchRole={vi.fn()}
        onRegister={vi.fn()}
        onSignOut={vi.fn()}
        mcpUrl="https://example.test/mcp"
      />
    );

    await waitFor(() => {
      expect(apiMock.list).toHaveBeenCalledWith(
        undefined,
        undefined,
        'Admin',
        '22222222-2222-2222-2222-222222222222'
      );
    });

    fireEvent.click(screen.getByRole('button', { name: /workflows/i }));

    expect(await screen.findByText('Session Tenant: Platform Administrator')).toBeInTheDocument();
  });

  test('reloads admin workflows with the selected tenant filter', async () => {
    render(
      <AdminDashboard
        session={session}
        onGoHome={vi.fn()}
        onSwitchRole={vi.fn()}
        onRegister={vi.fn()}
        onSignOut={vi.fn()}
        mcpUrl="https://example.test/mcp"
      />
    );

    fireEvent.click(screen.getByRole('button', { name: /workflows/i }));

    const select = await screen.findByRole('combobox');
    fireEvent.change(select, { target: { value: 'd3e8e2fd-1a8d-416a-987e-0585937090fd' } });

    await waitFor(() => {
      expect(apiMock.list).toHaveBeenLastCalledWith(
        undefined,
        undefined,
        'Admin',
        'd3e8e2fd-1a8d-416a-987e-0585937090fd'
      );
    });
  });

  const renderDashboard = () => render(
    <AdminDashboard
      session={session}
      onGoHome={vi.fn()}
      onSwitchRole={vi.fn()}
      onRegister={vi.fn()}
      onSignOut={vi.fn()}
      mcpUrl="https://example.test/mcp"
    />
  );

  test('shows a rejected workflow or instance list instead of an empty dashboard', async () => {
    apiMock.list.mockRejectedValue(new Error('Failed to list workflow classes (HTTP 403 Forbidden)'));
    apiMock.listInstances.mockRejectedValue(new Error('Failed to list workflow instances (HTTP 401 )'));

    renderDashboard();

    const alertBox = await screen.findByRole('alert');
    expect(alertBox).toHaveTextContent('Failed to list workflow classes (HTTP 403 Forbidden)');
    expect(alertBox).toHaveTextContent('Failed to list workflow instances (HTTP 401 )');
    expect(alertBox).toHaveTextContent('Sign in again if this keeps happening.');
  });

  test('shows every failed load, not only the first', async () => {
    apiMock.listTenants.mockRejectedValue(new Error('Failed to load tenants (HTTP 500 )'));
    apiMock.list.mockRejectedValue(new Error('Failed to list workflow classes (HTTP 500 )'));

    renderDashboard();

    const alertBox = await screen.findByRole('alert');
    expect(alertBox).toHaveTextContent('Failed to load tenants (HTTP 500 )');
    expect(alertBox).toHaveTextContent('Failed to list workflow classes (HTTP 500 )');
    expect(alertBox).not.toHaveTextContent('Sign in again');
  });

  describe('workflow detail', () => {
    const blueprints = [
      { id: 'bp-a', name: 'Alpha', tenantId: 'tenant-a' },
      { id: 'bp-b', name: 'Beta', tenantId: 'tenant-b' }
    ];

    beforeEach(() => {
      apiMock.list.mockResolvedValue(blueprints);
      apiMock.get.mockImplementation(async (id: string) => blueprints.find(b => b.id === id));
      apiMock.validate.mockResolvedValue({ isValid: true, errors: [] });
    });

    test('does not carry one workflow\'s validation result over to the next one opened', async () => {
      renderDashboard();
      fireEvent.click(screen.getByRole('button', { name: /workflows/i }));

      fireEvent.click(await screen.findByRole('button', { name: 'View Alpha' }));
      fireEvent.click(await screen.findByRole('button', { name: 'Validate' }));
      await waitFor(() => expect(screen.getByTestId('validation')).toHaveTextContent('validated'));

      fireEvent.click(screen.getByRole('button', { name: 'Close' }));
      fireEvent.click(await screen.findByRole('button', { name: 'View Beta' }));

      expect(await screen.findByText('Beta')).toBeInTheDocument();
      expect(screen.getByTestId('validation')).toHaveTextContent('no-validation');
    });

    test('tells the administrator when a workflow cannot be opened', async () => {
      apiMock.get.mockRejectedValue(new Error('Failed to get workflow class (HTTP 404 )'));
      renderDashboard();
      fireEvent.click(screen.getByRole('button', { name: /workflows/i }));

      fireEvent.click(await screen.findByRole('button', { name: 'View Alpha' }));

      await waitFor(() => expect(globalThis.alert).toHaveBeenCalledWith(
        expect.stringContaining('Failed to get workflow class (HTTP 404 )')
      ));
      expect(screen.queryByTestId('detail')).not.toBeInTheDocument();
    });

    test('tells the administrator when validation fails', async () => {
      apiMock.validate.mockRejectedValue(new Error('Failed to validate (HTTP 500 )'));
      renderDashboard();
      fireEvent.click(screen.getByRole('button', { name: /workflows/i }));
      fireEvent.click(await screen.findByRole('button', { name: 'View Alpha' }));

      fireEvent.click(await screen.findByRole('button', { name: 'Validate' }));

      await waitFor(() => expect(globalThis.alert).toHaveBeenCalledWith(
        expect.stringContaining('Failed to validate (HTTP 500 )')
      ));
      expect(screen.getByTestId('validation')).toHaveTextContent('no-validation');
    });
  });
});
