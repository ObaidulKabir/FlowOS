import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import '@testing-library/jest-dom';
import { beforeEach, describe, expect, test, vi } from 'vitest';
import { WorkflowInstanceTable } from './WorkflowInstanceTable';

const { apiMock } = vi.hoisted(() => ({
  apiMock: {
    getWorkflowAudit: vi.fn(),
    list: vi.fn()
  }
}));

vi.mock('../api/client', () => ({ api: apiMock }));
vi.mock('./WorkflowGraphVisualizer', () => ({ WorkflowGraphVisualizer: () => <div>Graph</div> }));
vi.mock('./WorkflowActionAuditViewer', () => ({
  WorkflowActionAuditViewer: ({ tenantId }: { tenantId?: string }) => <div>Actions tenant={tenantId}</div>
}));
vi.mock('./TimeTravelPlayer', () => ({
  TimeTravelPlayer: ({ tenantId }: { tenantId?: string }) => <div>TimeTravel tenant={tenantId}</div>
}));

const items = [
  { id: 'aaaaaaaa-1111-0000-0000-000000000001', tenantId: 't-acme', workflowClassName: 'Onboarding', status: 'Running', createdAt: '2026-11-01T01:00:00Z' },
  { id: 'bbbbbbbb-2222-0000-0000-000000000002', tenantId: 't-beta', workflowClassName: 'Refund', status: 'Failed', createdAt: '2026-11-01T02:00:00Z' },
  { id: 'cccccccc-3333-0000-0000-000000000003', tenantId: 't-beta', workflowClassName: 'Refund', status: 'Completed', createdAt: '2026-11-01T03:00:00Z' }
];

describe('WorkflowInstanceTable admin all-tenants view', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    apiMock.getWorkflowAudit.mockResolvedValue({ id: 'x', definitionName: 'Refund', timeline: [], status: 'Failed' });
    apiMock.list.mockResolvedValue([]);
  });

  test('shows a tenant column, status counts, and filters by status', () => {
    render(<WorkflowInstanceTable items={items as any} tenantNames={{ 't-acme': 'Acme Ltd', 't-beta': 'Beta Co' }} />);

    expect(screen.getByRole('columnheader', { name: 'Tenant' })).toBeInTheDocument();
    expect(screen.getByText('Acme Ltd')).toBeInTheDocument();
    expect(screen.getAllByText('Beta Co')).toHaveLength(2);
    expect(screen.getByRole('button', { name: 'All (3)' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Failed (1)' })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Failed (1)' }));

    const rows = screen.getAllByRole('row').slice(1);
    expect(rows).toHaveLength(1);
    expect(within(rows[0]).getByText('Beta Co')).toBeInTheDocument();
  });

  test('has no tenant column outside the all-tenants view', () => {
    render(<WorkflowInstanceTable items={items as any} />);
    expect(screen.queryByRole('columnheader', { name: 'Tenant' })).not.toBeInTheDocument();
  });

  test("opening a row reads that row's own tenant", async () => {
    render(<WorkflowInstanceTable items={items as any} tenantNames={{ 't-acme': 'Acme Ltd', 't-beta': 'Beta Co' }} />);

    const betaRow = screen.getAllByRole('row').find(row => within(row).queryByText('Failed'))!;
    fireEvent.click(within(betaRow).getByRole('button', { name: /audit trail/i }));

    await waitFor(() =>
      expect(apiMock.getWorkflowAudit).toHaveBeenCalledWith('bbbbbbbb-2222-0000-0000-000000000002', undefined, 't-beta')
    );
  });
});
