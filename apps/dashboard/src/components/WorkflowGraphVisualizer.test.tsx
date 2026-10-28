import { fireEvent, render, screen, within } from '@testing-library/react';
import '@testing-library/jest-dom';
import { describe, expect, test, vi } from 'vitest';
import { WorkflowGraphVisualizer } from './WorkflowGraphVisualizer';

const charts = vi.hoisted(() => ({ items: [] as string[] }));

vi.mock('mermaid', () => ({
  default: {
    initialize: vi.fn(),
    render: vi.fn(async (_id: string, chart: string) => {
      charts.items.push(chart);
      return { svg: '<svg id="diagram"><text>Step</text></svg>' };
    })
  }
}));

const definition = {
  stateMachine: {
    entityType: 'Order',
    initialState: 'Draft',
    states: ['Draft', 'Paid'],
    transitions: [{ fromState: 'Draft', toState: 'Paid', triggerEvent: 'PAY' }]
  },
  workflow: {
    startStepId: 'Start',
    steps: [
      {
        stepId: 'Start',
        stepType: 'Command',
        nextSteps: { PAY: 'END' }
      }
    ]
  }
};

describe('WorkflowGraphVisualizer large view', () => {
  test('opens one diagram in a large window and closes it', () => {
    render(<WorkflowGraphVisualizer definition={definition} />);

    fireEvent.click(screen.getByRole('button', { name: 'View workflow diagram large' }));

    const dialog = screen.getByRole('dialog', { name: 'Workflow Orchestration Steps' });
    expect(dialog).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Close large diagram' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Zoom in' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Own window' })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Close large diagram' }));
    expect(screen.queryByRole('dialog', { name: 'Workflow Orchestration Steps' })).not.toBeInTheDocument();
  });

  test('opens the state diagram on its own', () => {
    render(<WorkflowGraphVisualizer definition={definition} />);

    fireEvent.click(screen.getByRole('button', { name: 'View state diagram large' }));

    expect(screen.getByRole('dialog', { name: 'State Machine Lifecycle' })).toBeInTheDocument();
    expect(screen.queryByRole('dialog', { name: 'Workflow Orchestration Steps' })).not.toBeInTheDocument();
  });

  test('colors each event by the role allowed to fire it', async () => {
    const roleDefinition = {
      events: [
        { eventId: 'PAY', allowedRoles: ['Payer'] },
        { eventId: 'REVIEW', allowedRoles: ['Reviewer'] }
      ],
      stateMachine: {
        entityType: 'Order',
        initialState: 'Draft',
        states: ['Draft', 'Review', 'Paid'],
        transitions: [
          { fromState: 'Draft', toState: 'Review', eventId: 'PAY' },
          { fromState: 'Review', toState: 'Paid', eventId: 'REVIEW' }
        ]
      },
      workflow: {
        startStepId: 'Start',
        steps: [
          { stepId: 'Start', stepType: 'HumanTask', requiredRoles: ['Payer'], nextSteps: { PAY: 'Review' } },
          { stepId: 'Review', stepType: 'HumanTask', requiredRoles: ['Reviewer'], nextSteps: { REVIEW: 'END' } }
        ]
      }
    };
    const start = charts.items.length;
    render(<WorkflowGraphVisualizer definition={roleDefinition} />);
    await screen.findAllByText('Step');

    const drawn = charts.items.slice(start);
    const workflow = drawn.find(chart => chart.startsWith('flowchart TD'));
    const state = drawn.find(chart => chart.startsWith('flowchart LR'));
    expect(workflow).toContain('PAY · Payer');
    expect(workflow).toContain('REVIEW · Reviewer');
    expect(workflow).toContain('linkStyle 0 stroke:#38bdf8,stroke-width:2px,color:#38bdf8');
    expect(workflow).toContain('linkStyle 1 stroke:#f472b6,stroke-width:2px,color:#f472b6');
    expect(state).toContain('PAY · Payer');
    expect(state).toContain('REVIEW · Reviewer');
    expect(state).toContain('linkStyle 1 stroke:#38bdf8,stroke-width:2px,color:#38bdf8');
    expect(state).toContain('linkStyle 2 stroke:#f472b6,stroke-width:2px,color:#f472b6');
    expect(screen.getAllByText('Payer').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Reviewer').length).toBeGreaterThan(0);

    fireEvent.click(screen.getByRole('button', { name: 'View workflow diagram large' }));
    const dialog = screen.getByRole('dialog', { name: 'Workflow Orchestration Steps' });
    expect(within(dialog).getByText('Payer')).toBeInTheDocument();
    expect(within(dialog).getByText('Reviewer')).toBeInTheDocument();
  });
});
