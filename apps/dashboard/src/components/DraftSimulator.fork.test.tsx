import { fireEvent, render, screen } from '@testing-library/react';
import '@testing-library/jest-dom';
import { describe, expect, test, vi } from 'vitest';
import { DraftSimulator } from './DraftSimulator';

vi.mock('mermaid', () => ({
  default: {
    initialize: vi.fn(),
    render: vi.fn(async () => ({ svg: '<svg></svg>' }))
  }
}));

const human = (stepId: string, eventId: string, target: string, role: string) => ({
  stepId,
  stepType: 'HumanTask',
  requiredRoles: [role],
  nextSteps: { [eventId]: target }
});

const definition = {
  stateMachine: { initialState: 'Contracting', states: ['Contracting'], transitions: [] },
  roles: [
    { name: 'Coordinator', grantedCapabilities: [] },
    { name: 'Liaison', grantedCapabilities: [] },
    { name: 'Owner', grantedCapabilities: [] }
  ],
  events: [
    { eventId: 'GO', allowedRoles: ['Coordinator'] },
    { eventId: 'COLLECT', allowedRoles: ['Liaison'] },
    { eventId: 'PROCESS', allowedRoles: ['Owner'] },
    { eventId: 'SURVEY', allowedRoles: ['Coordinator'] }
  ],
  workflow: {
    startStepId: 'Confirm',
    steps: [
      human('Confirm', 'GO', 'ForkPreparation', 'Coordinator'),
      {
        stepId: 'ForkPreparation',
        stepType: 'Fork',
        nextSteps: {},
        branches: ['Collect', 'Survey'],
        joinPolicy: 'WaitAll'
      },
      human('Collect', 'COLLECT', 'Process', 'Liaison'),
      human('Process', 'PROCESS', 'JoinPreparation', 'Owner'),
      human('Survey', 'SURVEY', 'JoinPreparation', 'Coordinator'),
      {
        stepId: 'JoinPreparation',
        stepType: 'Join',
        nextSteps: { Default: 'Ready' },
        inboundSteps: ['Process', 'Survey'],
        joinPolicy: 'WaitAll'
      },
      {
        stepId: 'Ready',
        stepType: 'HumanTask',
        requiredRoles: ['Coordinator'],
        nextSteps: {}
      }
    ]
  }
};

describe('DraftSimulator fork and join', () => {
  test('opens both branches instead of ending on an empty fork route', () => {
    render(<DraftSimulator definition={definition} />);

    fireEvent.click(screen.getByRole('button', { name: /GO/ }));

    expect(screen.getByText(/Opened parallel branches: Collect, Survey/)).toBeInTheDocument();
    expect(screen.getAllByText('Collect').length).toBeGreaterThan(0);
    expect(screen.queryByText('Simulation Complete')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Auto-Step/ })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /COLLECT/ }));
    fireEvent.click(screen.getByRole('button', { name: /PROCESS/ }));

    expect(screen.getByText(/Continue Survey/)).toBeInTheDocument();
    expect(screen.queryByText('Simulation Complete')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /SURVEY/ }));

    expect(screen.getByText(/All inbound branches arrived. Continue Ready/)).toBeInTheDocument();
    expect(screen.getAllByText('Ready').length).toBeGreaterThan(0);
    expect(screen.queryByText('Simulation Complete')).not.toBeInTheDocument();
  });
});
