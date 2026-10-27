import { act, fireEvent, render, screen, within } from '@testing-library/react';
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

  test('zooms in with the plus control, out with the minus control, and stays between 50% and 300%', async () => {
    render(<WorkflowGraphVisualizer definition={definition} />);
    fireEvent.click(screen.getByRole('button', { name: 'View workflow diagram large' }));
    await screen.findByTestId('diagram-canvas');

    const dialog = screen.getByRole('dialog', { name: 'Workflow Orchestration Steps' });
    expect(within(dialog).getAllByRole('button').slice(0, 3).map(button => button.getAttribute('aria-label')))
      .toEqual(['Zoom in', 'Reset zoom', 'Zoom out']);

    const canvas = screen.getByTestId('diagram-canvas');
    const zoomIn = screen.getByRole('button', { name: 'Zoom in' });
    const zoomOut = screen.getByRole('button', { name: 'Zoom out' });
    const reset = screen.getByRole('button', { name: 'Reset zoom' });

    expect(reset).toHaveTextContent('100%');
    fireEvent.click(zoomIn);
    expect(reset).toHaveTextContent('125%');
    expect(canvas.style.transform).toContain('scale(1.25)');

    fireEvent.click(zoomOut);
    fireEvent.click(zoomOut);
    expect(reset).toHaveTextContent('75%');
    fireEvent.click(zoomOut);
    expect(reset).toHaveTextContent('50%');
    expect(zoomOut).toBeDisabled();
    fireEvent.click(zoomOut);
    expect(reset).toHaveTextContent('50%');

    fireEvent.click(reset);
    expect(reset).toHaveTextContent('100%');
    for (let step = 0; step < 8; step += 1) fireEvent.click(zoomIn);
    expect(reset).toHaveTextContent('300%');
    expect(zoomIn).toBeDisabled();
    fireEvent.click(zoomIn);
    expect(reset).toHaveTextContent('300%');
    expect(canvas.style.transform).toContain('scale(3)');
  });

  test('scroll zooms and dragging pans the large diagram', async () => {
    render(<WorkflowGraphVisualizer definition={definition} />);
    fireEvent.click(screen.getByRole('button', { name: 'View state diagram large' }));
    await screen.findByTestId('diagram-canvas');

    const viewport = screen.getByTestId('diagram-viewport');
    const canvas = screen.getByTestId('diagram-canvas');
    await act(async () => {
      await Promise.resolve();
    });
    await act(async () => {
      viewport.dispatchEvent(new WheelEvent('wheel', { deltaY: -120, clientX: 40, clientY: 30, bubbles: true, cancelable: true }));
    });
    expect(canvas.style.transform).toContain('scale(1.1)');
    expect(screen.getByRole('button', { name: 'Reset zoom' })).toHaveTextContent('110%');

    const pointer = (type: string, clientX: number, clientY: number, button = 0) => {
      const event = new MouseEvent(type, { clientX, clientY, button, bubbles: true, cancelable: true });
      Object.defineProperty(event, 'pointerId', { value: 1 });
      return event;
    };
    await act(async () => {
      viewport.dispatchEvent(pointer('pointerdown', 10, 12));
      viewport.dispatchEvent(pointer('pointermove', 50, 37));
      viewport.dispatchEvent(pointer('pointerup', 50, 37));
    });
    expect(canvas.style.transform).toBe('translate(36px, 22px) scale(1.1)');
  });

  test('opens the state diagram on its own', async () => {
    render(<WorkflowGraphVisualizer definition={definition} />);

    fireEvent.click(screen.getByRole('button', { name: 'View state diagram large' }));

    expect(screen.getByRole('dialog', { name: 'State Machine Lifecycle' })).toBeInTheDocument();
    await screen.findByTestId('diagram-canvas');
    expect(screen.queryByRole('dialog', { name: 'Workflow Orchestration Steps' })).not.toBeInTheDocument();
  });
});
