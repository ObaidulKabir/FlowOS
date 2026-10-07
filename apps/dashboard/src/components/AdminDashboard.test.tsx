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
