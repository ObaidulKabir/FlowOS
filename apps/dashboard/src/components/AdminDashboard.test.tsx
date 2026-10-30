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
    validate: vi.fn(),
    listPlanRequests: vi.fn(),
    setTenantPlan: vi.fn(),
    rejectPlanRequest: vi.fn()
  }
}));

vi.mock('../api/client', () => ({
  api: apiMock
}));

vi.mock('./TenantManager', () => ({
  TenantManager: () => <div>Tenant Manager</div>
}));

vi.mock('./WorkflowTable', () => ({
  WorkflowTable: () => <div>Workflow Table</div>
}));

vi.mock('./WorkflowInstanceTable', () => ({
  WorkflowInstanceTable: () => <div>Workflow Instances</div>
}));

vi.mock('./EventAuditViewer', () => ({
  EventAuditViewer: () => <div>Event Audit</div>
}));

vi.mock('./DetailView', () => ({
  DetailView: () => null
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
    apiMock.listPlanRequests.mockResolvedValue([]);
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
});

describe('AdminDashboard plan request inbox', () => {
  const session = {
    role: 'Admin' as const,
    tenantId: '11111111-1111-1111-1111-111111111111',
    tenantName: 'Default Tenant',
    apiKey: 'flowos_prod_secret_key_32_chars_min',
    username: 'admin@flowos.internal',
    isSandbox: false,
    isEmailVerified: true
  };

  const pending = (tenantId: string, name: string, plan: string) => ({
    tenantId,
    tenantName: name,
    currentPlan: 'None',
    currentBillingStatus: 'Unpaid',
    canRunRuntime: false,
    planRequest: {
      requestedPlan: plan,
      status: 'Pending',
      paymentReference: 'TRX-9',
      requestedAt: '2026-10-30T10:00:00Z'
    }
  });

  const renderDashboard = () =>
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

  beforeEach(() => {
    vi.clearAllMocks();
    apiMock.listTenants.mockResolvedValue([]);
    apiMock.list.mockResolvedValue([]);
    apiMock.listInstances.mockResolvedValue([]);
  });

  test('shows waiting tenants with a badge and approves in one click', async () => {
    apiMock.listPlanRequests
      .mockResolvedValueOnce([pending('t-1', 'Acme Ltd', 'Team')])
      .mockResolvedValue([]);
    apiMock.setTenantPlan.mockResolvedValue({ canRunRuntime: true, keysExtended: 2 });

    renderDashboard();

    expect(await screen.findByText('Acme Ltd')).toBeInTheDocument();
    expect(screen.getByLabelText('1 plan requests waiting')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /approve team/i }));

    await waitFor(() => expect(apiMock.setTenantPlan).toHaveBeenCalledWith('t-1', 'Team', 'Active'));
    await waitFor(() => expect(screen.queryByText('Acme Ltd')).not.toBeInTheDocument());
    expect(await screen.findByRole('status')).toHaveTextContent('Acme Ltd is active on Team. 2 lapsed API keys were renewed.');
  });

  test('rejects with a reason only after confirmation', async () => {
    apiMock.listPlanRequests
      .mockResolvedValueOnce([pending('t-2', 'Beta Co', 'Starter')])
      .mockResolvedValue([]);
    apiMock.rejectPlanRequest.mockResolvedValue(undefined);

    renderDashboard();
    await screen.findByText('Beta Co');

    fireEvent.click(screen.getByRole('button', { name: /^reject$/i }));
    expect(apiMock.rejectPlanRequest).not.toHaveBeenCalled();

    fireEvent.change(screen.getByLabelText('Rejection reason for Beta Co'), { target: { value: 'ref unclear' } });
    fireEvent.click(screen.getByRole('button', { name: /confirm reject/i }));

    await waitFor(() => expect(apiMock.rejectPlanRequest).toHaveBeenCalledWith('t-2', 'ref unclear'));
  });

  test('keeps the request and shows the failure when approval fails', async () => {
    apiMock.listPlanRequests.mockResolvedValue([pending('t-3', 'Gamma Inc', 'Scale')]);
    apiMock.setTenantPlan.mockRejectedValue(new Error('HTTP 500 boom'));

    renderDashboard();
    await screen.findByText('Gamma Inc');
    fireEvent.click(screen.getByRole('button', { name: /approve scale/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Could not approve Gamma Inc: HTTP 500 boom');
    expect(screen.getByText('Gamma Inc')).toBeInTheDocument();
  });

  test('picks up new requests by polling without a page reload', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      apiMock.listPlanRequests
        .mockResolvedValueOnce([])
        .mockResolvedValue([pending('t-4', 'Delta LLC', 'Growth')]);

      renderDashboard();
      await waitFor(() => expect(apiMock.listPlanRequests).toHaveBeenCalledTimes(1));
      expect(screen.queryByText('Delta LLC')).not.toBeInTheDocument();

      await vi.advanceTimersByTimeAsync(15000);

      expect(await screen.findByText('Delta LLC')).toBeInTheDocument();
    } finally {
      vi.useRealTimers();
    }
  });
});
