import { fireEvent, render, screen } from '@testing-library/react';
import '@testing-library/jest-dom';
import { describe, expect, test, vi } from 'vitest';
import { WorkflowGraphVisualizer } from './WorkflowGraphVisualizer';

vi.mock('mermaid', () => ({
  default: {
    initialize: vi.fn(),
    render: vi.fn(async () => ({ svg: '<svg id="diagram"><text>Step</text></svg>' }))
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
});
