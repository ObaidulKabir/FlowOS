import { render, screen, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { ContextSimulationStudio } from './ContextSimulationStudio';
import { api } from '../api/client';

// Mock the API client used by the component to avoid real network calls.
vi.mock('../api/client', () => ({
  api: {
    listContextBindings: vi.fn(),
    simulateContextBinding: vi.fn(),
    startWorkflowByContext: vi.fn(),
  },
}));

// Ensure the list call always returns an array (empty by default).
beforeEach(() => {
  api.listContextBindings.mockResolvedValue([]);
});

describe('ContextSimulationStudio – payload JSON validation', () => {
  test('shows an error when the initial payload JSON is malformed', async () => {
    render(<ContextSimulationStudio />);
    const textarea = screen.getByRole('textbox');
    fireEvent.change(textarea, { target: { value: '{ invalid' } });
    const errorMsg = await screen.findByText('Invalid JSON');
    expect(errorMsg).toBeInTheDocument();
    expect(textarea).toHaveClass('border-rose-600');
  });

  test('removes the error when the JSON becomes valid', async () => {
    render(<ContextSimulationStudio />);
    const textarea = screen.getByRole('textbox');
    fireEvent.change(textarea, { target: { value: '{ invalid' } });
    await screen.findByText('Invalid JSON');
    fireEvent.change(textarea, { target: { value: '{"key":"value"}' } });
    expect(screen.queryByText('Invalid JSON')).not.toBeInTheDocument();
    expect(textarea).not.toHaveClass('border-rose-600');
  });
});

// Core functionality tests – each test sets up its own mock bindings where needed.

describe('ContextSimulationStudio – core functionality', () => {
  const mockBindings = [
    {
      id: 'b1',
      name: 'Binding One',
      contextType: 'typeA',
      draftRevision: {
        id: 'd1',
        definition: {
          sourcePayloadSchema: '{}',
          eventAliases: {},
          eventSourcePayloadSchemas: {},
          roleOverrides: {},
          entityType: 'entityA',
          inputMapping: {},
          conditionParameters: {},
        },
        sourceWorkflowClassId: 'wf1234567890',
        sourceWorkflowClassVersion: '1.0',
        revision: 1,
        status: 'draft',
      },
      activeRevision: undefined,
    },
    {
      id: 'b2',
      name: 'Binding Two',
      contextType: 'typeB',
      draftRevision: undefined,
      activeRevision: {
        id: 'a1',
        definition: {
          sourcePayloadSchema: '{}',
          eventAliases: {},
          eventSourcePayloadSchemas: {},
          roleOverrides: {},
          entityType: 'entityB',
          inputMapping: {},
          conditionParameters: {},
        },
        sourceWorkflowClassId: 'wf0987654321',
        sourceWorkflowClassVersion: '2.0',
        revision: 2,
        status: 'active',
      },
    },
  ];

  beforeEach(() => {
    // Use the binding set suitable for the test unless overridden.
    api.listContextBindings.mockResolvedValue(mockBindings);
    api.simulateContextBinding.mockResolvedValue({
      status: 'Allowed',
      revisionKind: 'draft',
      revision: 1,
      currentStepId: 'step1',
      currentState: 'state1',
      isPersistedRuntime: false,
      graph: {},
      trace: [
        {
          index: 0,
          eventType: 'event1',
          isAllowed: true,
          fromStepId: 's0',
          toStepId: 's1',
          fromState: 'init',
          toState: 'mid',
          outcome: 'ok',
          reason: '',
          roles: [],
          canonicalEventType: null,
          contextBefore: {},
          contextAfter: {},
          canonicalDelta: {},
          plannedActions: [],
          pendingWork: [],
        },
      ],
    });
    api.startWorkflowByContext.mockResolvedValue({ workflowInstanceId: 'wf-123' });
    Object.assign(navigator, { clipboard: { writeText: vi.fn().mockResolvedValue(undefined) } });
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    vi.spyOn(window, 'alert').mockImplementation(() => {});
  });

  test('loads bindings and allows selection', async () => {
    render(<ContextSimulationStudio />);
    await screen.findByRole('combobox');
    const select = screen.getByRole('combobox');
    fireEvent.change(select, { target: { value: 'b1' } });
    expect(select).toHaveValue('b1');
  });

  test('adds an event and updates its payload', async () => {
    render(<ContextSimulationStudio />);
    await screen.findByRole('combobox');
    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'b2' } });
    const addBtn = screen.getByRole('button', { name: /Add event/i });
    userEvent.click(addBtn);
    const eventInput = await screen.findByPlaceholderText('Contextual event ID');
    expect(eventInput).toBeInTheDocument();
    fireEvent.change(eventInput, { target: { value: 'customEvent' } });
    const payloadArea = screen.getByDisplayValue('{}');
    expect(payloadArea).toBeInTheDocument();
    fireEvent.change(payloadArea, { target: { value: '{"a":1}' } });
    expect(screen.queryByText('Invalid JSON')).not.toBeInTheDocument();
  });

  test('runs simulation and displays result', async () => {
    render(<ContextSimulationStudio />);
    await screen.findByRole('combobox');
    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'b1' } });
    const runBtn = screen.getByRole('button', { name: /Run simulation/i });
    userEvent.click(runBtn);
    const status = await screen.findByText('Allowed');
    expect(status).toBeInTheDocument();
    const timelineItem = screen.getByText('event1');
    expect(timelineItem).toBeInTheDocument();
  });

  test('copies scenario and shows copied feedback', async () => {
    render(<ContextSimulationStudio />);
    await screen.findByRole('combobox');
    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'b1' } });
    const copyBtn = screen.getByRole('button', { name: /Copy scenario/i });
    userEvent.click(copyBtn);
    expect(navigator.clipboard.writeText).toHaveBeenCalled();
    const feedback = await screen.findByText('Scenario copied.');
    expect(feedback).toBeInTheDocument();
  });

  test('starts real workflow when active revision', async () => {
    // Override bindings for this case.
    api.listContextBindings.mockResolvedValue([
      {
        id: 'b3',
        name: 'Active Binding',
        contextType: 'typeC',
        draftRevision: undefined,
        activeRevision: {
          id: 'a2',
          definition: {
            sourcePayloadSchema: '{}',
            eventAliases: {},
            eventSourcePayloadSchemas: {},
            roleOverrides: {},
            entityType: 'entityC',
            inputMapping: {},
            conditionParameters: {},
          },
          sourceWorkflowClassId: 'wfActive123',
          sourceWorkflowClassVersion: '3.0',
          revision: 3,
          status: 'active',
        },
      },
    ]);
    render(<ContextSimulationStudio />);
    await screen.findByRole('combobox');
    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'b3' } });
    const activeBtn = screen.getByRole('button', { name: 'Pinned active' });
    userEvent.click(activeBtn);
    const runBtn = screen.getByRole('button', { name: /Run simulation/i });
    userEvent.click(runBtn);
    await screen.findByText('Allowed');
    const startBtn = screen.getByRole('button', { name: /Start real workflow/i });
    userEvent.click(startBtn);
    expect(window.confirm).toHaveBeenCalled();
    expect(api.startWorkflowByContext).toHaveBeenCalled();
    expect(window.alert).toHaveBeenCalledWith(expect.stringContaining('Workflow started'));
  });

  test('reset clears fields', async () => {
    render(<ContextSimulationStudio />);
    await screen.findByRole('combobox');
    const textarea = screen.getByRole('textbox');
    fireEvent.change(textarea, { target: { value: '{"x":1}' } });
    const resetBtn = screen.getByRole('button', { name: /Reset scenario/i });
    userEvent.click(resetBtn);
    // After reset the component uses the seed JSON derived from the binding's schema.
    // For our mock bindings the schema is an empty object, which renders as '{\n  \n}'.
    expect(textarea).toHaveValue('{\n  \n}');
  });
});
