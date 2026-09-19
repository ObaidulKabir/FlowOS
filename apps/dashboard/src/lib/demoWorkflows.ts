import { applySimulationGovernance } from './simulationGovernance';

export const DEMO_NAMES = [
  'ExpenseApprovalV2',
  'OrderSagaFulfillment',
  'LoanUnderwritingFlow',
  'SecOpsAccessGovernance',
  'IncidentAlertEscalation',
  'ExpenseApproval'
];

const FALLBACK_EXPENSE = applySimulationGovernance({
  Events: [
    { EventId: 'EVT-SUBMIT', Name: 'Submit Request', AllowedRoles: ['User', 'Employee'] },
    { EventId: 'EVT-APPROVE', Name: 'Approve Request', AllowedRoles: ['Manager'] },
    { EventId: 'EVT-REJECT', Name: 'Reject Request', AllowedRoles: ['Manager'] }
  ],
  StateMachine: {
    InitialState: 'Draft',
    States: ['Draft', 'Pending', 'Approved', 'Rejected'],
    Transitions: [
      { FromState: 'Draft', ToState: 'Pending', EventId: 'EVT-SUBMIT' },
      { FromState: 'Pending', ToState: 'Approved', EventId: 'EVT-APPROVE' },
      { FromState: 'Pending', ToState: 'Rejected', EventId: 'EVT-REJECT' }
    ]
  },
  Workflow: {
    StartStepId: 'Draft',
    Steps: [
      {
        StepId: 'Draft',
        StepType: 'Command',
        NextSteps: { 'EVT-SUBMIT': 'Pending' },
        RequiredRoles: ['User']
      },
      {
        StepId: 'Pending',
        StepType: 'HumanTask',
        NextSteps: { 'EVT-APPROVE': 'Approved', 'EVT-REJECT': 'Rejected' },
        RequiredRoles: ['Manager']
      },
      { StepId: 'Approved', StepType: 'Command', NextSteps: { Default: 'END' } },
      { StepId: 'Rejected', StepType: 'Command', NextSteps: { Default: 'END' } }
    ]
  }
});

const FALLBACK_EXPENSE_V2 = applySimulationGovernance({
  Events: [
    { EventId: 'EVT-SUBMIT', Name: 'Submit Request', AllowedRoles: ['User', 'Employee'] },
    { EventId: 'EVT-APPROVE', Name: 'Approve Request', AllowedRoles: ['Manager'] },
    { EventId: 'EVT-REJECT', Name: 'Reject Request', AllowedRoles: ['Manager'] },
    { EventId: 'EVT-ESCALATE', Name: 'Escalate to Director', AllowedRoles: ['Manager'] },
    { EventId: 'EVT-DIRECTOR-APPROVE', Name: 'Director Approve', AllowedRoles: ['Director'] },
    { EventId: 'EVT-DIRECTOR-REJECT', Name: 'Director Reject', AllowedRoles: ['Director'] }
  ],
  StateMachine: {
    InitialState: 'Draft',
    States: ['Draft', 'PendingManager', 'PendingDirector', 'Approved', 'Rejected'],
    Transitions: [
      { FromState: 'Draft', ToState: 'PendingDirector', EventId: 'EVT-SUBMIT', Condition: 'Amount > 5000' },
      { FromState: 'Draft', ToState: 'PendingManager', EventId: 'EVT-SUBMIT' },
      { FromState: 'PendingManager', ToState: 'Approved', EventId: 'EVT-APPROVE' },
      { FromState: 'PendingManager', ToState: 'PendingDirector', EventId: 'EVT-ESCALATE' },
      { FromState: 'PendingManager', ToState: 'Rejected', EventId: 'EVT-REJECT' },
      { FromState: 'PendingDirector', ToState: 'Approved', EventId: 'EVT-DIRECTOR-APPROVE' },
      { FromState: 'PendingDirector', ToState: 'Rejected', EventId: 'EVT-DIRECTOR-REJECT' }
    ]
  },
  Workflow: {
    StartStepId: 'Draft',
    Steps: [
      {
        StepId: 'Draft',
        StepType: 'Command',
        NextSteps: { 'EVT-SUBMIT': 'CheckAmount' },
        RequiredRoles: ['User']
      },
      {
        StepId: 'CheckAmount',
        StepType: 'Decision',
        Conditions: {
          'Amount > 5000': 'PendingDirector',
          Default: 'PendingManager'
        },
        NextSteps: { Default: 'PendingManager' },
        RequiredRoles: ['System']
      },
      {
        StepId: 'PendingManager',
        StepType: 'HumanTask',
        NextSteps: { 'EVT-APPROVE': 'Approved', 'EVT-ESCALATE': 'PendingDirector', 'EVT-REJECT': 'Rejected' },
        RequiredRoles: ['Manager']
      },
      {
        StepId: 'PendingDirector',
        StepType: 'HumanTask',
        NextSteps: { 'EVT-DIRECTOR-APPROVE': 'Approved', 'EVT-DIRECTOR-REJECT': 'Rejected' },
        RequiredRoles: ['Director']
      },
      { StepId: 'Approved', StepType: 'Command', NextSteps: { Default: 'END' } },
      { StepId: 'Rejected', StepType: 'Command', NextSteps: { Default: 'END' } }
    ]
  }
});

const FALLBACK_ORDER_SAGA = applySimulationGovernance({
  Events: [
    { EventId: 'EVT-VALIDATE', Name: 'Validate Order' },
    { EventId: 'EVT-PAY-SUCCESS', Name: 'Payment Authorized' },
    { EventId: 'EVT-STOCK-LOCKED', Name: 'Inventory Reserved' },
    { EventId: 'EVT-SHIP-FAIL', Name: 'Shipping Generation Failed' },
    { EventId: 'EVT-COMPENSATE', Name: 'Rollback Completed' }
  ],
  StateMachine: {
    InitialState: 'Draft',
    States: ['Draft', 'PaymentAuthorized', 'InventoryReserved', 'Compensating', 'RolledBack', 'Completed'],
    Transitions: [
      { FromState: 'Draft', ToState: 'PaymentAuthorized', EventId: 'EVT-PAY-SUCCESS' },
      { FromState: 'PaymentAuthorized', ToState: 'InventoryReserved', EventId: 'EVT-STOCK-LOCKED' },
      { FromState: 'InventoryReserved', ToState: 'Compensating', EventId: 'EVT-SHIP-FAIL' },
      { FromState: 'Compensating', ToState: 'RolledBack', EventId: 'EVT-COMPENSATE' }
    ]
  },
  Workflow: {
    StartStepId: 'ValidateOrder',
    Steps: [
      {
        StepId: 'ValidateOrder',
        StepType: 'Command',
        NextSteps: { Default: 'AuthorizePayment' },
        RequiredRoles: ['System']
      },
      {
        StepId: 'AuthorizePayment',
        StepType: 'Command',
        NextSteps: { 'EVT-PAY-SUCCESS': 'ReserveInventory' },
        RequiredRoles: ['System']
      },
      {
        StepId: 'ReserveInventory',
        StepType: 'Command',
        NextSteps: { 'EVT-STOCK-LOCKED': 'GenerateShippingLabel' },
        RequiredRoles: ['Warehouse']
      },
      {
        StepId: 'GenerateShippingLabel',
        StepType: 'Command',
        NextSteps: { Default: 'END', 'EVT-SHIP-FAIL': 'CompensateOrder' },
        RequiredRoles: ['Logistics']
      },
      {
        StepId: 'CompensateOrder',
        StepType: 'Command',
        NextSteps: { 'EVT-COMPENSATE': 'END' },
        RequiredRoles: ['System']
      }
    ]
  }
});

const FALLBACK_LOAN = applySimulationGovernance({
  Events: [
    { EventId: 'EVT-APPLY', Name: 'Application Submitted', AllowedRoles: ['User', 'Employee'] },
    { EventId: 'EVT-AUTO-APPROVE', Name: 'Fast-Track Auto Approved', AllowedRoles: ['System'] },
    { EventId: 'EVT-MANUAL-REVIEW', Name: 'Requires Underwriter Review', AllowedRoles: ['System'] },
    { EventId: 'EVT-FINAL-APPROVE', Name: 'Underwriter Approved', AllowedRoles: ['Manager', 'Director'] },
    { EventId: 'EVT-DECLINE', Name: 'Application Declined', AllowedRoles: ['Manager', 'Director'] }
  ],
  StateMachine: {
    InitialState: 'Draft',
    States: ['Draft', 'Evaluating', 'UnderwritingReview', 'Approved', 'Declined'],
    Transitions: [
      { FromState: 'Draft', ToState: 'Evaluating', EventId: 'EVT-APPLY' },
      { FromState: 'Evaluating', ToState: 'Approved', EventId: 'EVT-AUTO-APPROVE' },
      { FromState: 'Evaluating', ToState: 'UnderwritingReview', EventId: 'EVT-MANUAL-REVIEW' },
      { FromState: 'UnderwritingReview', ToState: 'Approved', EventId: 'EVT-FINAL-APPROVE' },
      { FromState: 'UnderwritingReview', ToState: 'Declined', EventId: 'EVT-DECLINE' },
      { FromState: 'Evaluating', ToState: 'Declined', EventId: 'EVT-DECLINE' }
    ]
  },
  Workflow: {
    StartStepId: 'IntakeApplication',
    Steps: [
      {
        StepId: 'IntakeApplication',
        StepType: 'Command',
        NextSteps: { 'EVT-APPLY': 'EvaluateRisk' },
        RequiredRoles: ['Applicant']
      },
      {
        StepId: 'EvaluateRisk',
        StepType: 'Decision',
        Conditions: {
          'CreditScore >= 720 && DebtToIncome < 0.35': 'FastTrackDisbursement',
          'CreditScore < 580': 'DeclineApplication',
          Default: 'UnderwriterReview'
        },
        RequiredRoles: ['System']
      },
      {
        StepId: 'UnderwriterReview',
        StepType: 'HumanTask',
        RequiredRoles: ['Manager', 'Director'],
        NextSteps: { 'EVT-FINAL-APPROVE': 'DisburseFunds', 'EVT-DECLINE': 'DeclineApplication' },
        Sla: { Duration: '48h', TimeoutEvent: 'EVT-DECLINE', EscalationStepId: 'DeclineApplication' }
      },
      {
        StepId: 'FastTrackDisbursement',
        StepType: 'Command',
        NextSteps: { 'EVT-AUTO-APPROVE': 'DisburseFunds' },
        RequiredRoles: ['System']
      },
      {
        StepId: 'DisburseFunds',
        StepType: 'Command',
        NextSteps: { Default: 'END' },
        RequiredRoles: ['Finance']
      },
      {
        StepId: 'DeclineApplication',
        StepType: 'Command',
        NextSteps: { Default: 'END' },
        RequiredRoles: ['System']
      }
    ]
  }
});

const FALLBACK_INCIDENT_ALERT = applySimulationGovernance({
  Events: [
    { EventId: 'EVT-OPEN', Name: 'Open Incident', AllowedRoles: ['Reporter'] },
    { EventId: 'EVT-SLA-WARN-1H', Name: '1h SLA warning alert' },
    { EventId: 'EVT-SLA-WARN-3H', Name: '3h SLA warning alert' },
    { EventId: 'EVT-ESCALATE', Name: 'Escalate to On-Call', AllowedRoles: ['Support'] },
    { EventId: 'EVT-RESOLVE', Name: 'L1 Resolve', AllowedRoles: ['Support'] },
    { EventId: 'EVT-CLOSE', Name: 'On-Call Close', AllowedRoles: ['OnCall'] }
  ],
  StateMachine: {
    InitialState: 'Draft',
    States: ['Draft', 'L1Queued', 'Escalated', 'Resolved'],
    Transitions: [
      { FromState: 'Draft', ToState: 'Escalated', EventId: 'EVT-OPEN', Condition: 'Severity == "Critical"' },
      { FromState: 'Draft', ToState: 'L1Queued', EventId: 'EVT-OPEN' },
      { FromState: 'L1Queued', ToState: 'Resolved', EventId: 'EVT-RESOLVE' },
      { FromState: 'L1Queued', ToState: 'Escalated', EventId: 'EVT-ESCALATE' },
      { FromState: 'Escalated', ToState: 'Resolved', EventId: 'EVT-CLOSE' }
    ]
  },
  Workflow: {
    StartStepId: 'ReportIncident',
    Steps: [
      {
        StepId: 'ReportIncident',
        StepType: 'Command',
        NextSteps: { 'EVT-OPEN': 'CheckSeverity' },
        RequiredRoles: ['Reporter']
      },
      {
        StepId: 'CheckSeverity',
        StepType: 'Decision',
        Conditions: { 'Severity == "Critical"': 'OnCallEscalation', Default: 'L1Review' },
        NextSteps: { Default: 'L1Review' },
        RequiredRoles: ['System']
      },
      {
        StepId: 'L1Review',
        StepType: 'HumanTask',
        RequiredRoles: ['Support'],
        NextSteps: { 'EVT-RESOLVE': 'Closed', 'EVT-ESCALATE': 'OnCallEscalation' },
        Sla: {
          Duration: '4h',
          TimeoutEvent: 'EVT-ESCALATE',
          EscalationStepId: 'OnCallEscalation',
          EscalationRole: 'OnCall',
          Reminders: [
            { Duration: '1h', TriggerEvent: 'EVT-SLA-WARN-1H' },
            { Duration: '3h', TriggerEvent: 'EVT-SLA-WARN-3H' }
          ]
        }
      },
      {
        StepId: 'OnCallEscalation',
        StepType: 'HumanTask',
        RequiredRoles: ['OnCall'],
        NextSteps: { 'EVT-CLOSE': 'Closed' }
      },
      {
        StepId: 'Closed',
        StepType: 'Command',
        NextSteps: { Default: 'END' }
      }
    ]
  }
});

const FALLBACK_SECOPS = applySimulationGovernance({
  Events: [
    { EventId: 'EVT-REQUEST-ACCESS', Name: 'Request Privileged Access', AllowedRoles: ['User', 'Employee'] },
    { EventId: 'EVT-APPROVE', Name: 'Manager Approved', AllowedRoles: ['Manager'] },
    { EventId: 'EVT-ESCALATE', Name: 'SLA Breached - Auto Escalated' },
    { EventId: 'EVT-DIRECTOR-APPROVE', Name: 'SecOps Director Approved', AllowedRoles: ['Director'] },
    { EventId: 'EVT-REVOKE', Name: 'Session Expired / Revoked', AllowedRoles: ['System'] }
  ],
  StateMachine: {
    InitialState: 'Draft',
    States: ['Draft', 'PendingManager', 'PendingDirector', 'AccessActive', 'Revoked'],
    Transitions: [
      { FromState: 'Draft', ToState: 'PendingManager', EventId: 'EVT-REQUEST-ACCESS' },
      { FromState: 'PendingManager', ToState: 'AccessActive', EventId: 'EVT-APPROVE' },
      { FromState: 'PendingManager', ToState: 'PendingDirector', EventId: 'EVT-ESCALATE' },
      { FromState: 'PendingDirector', ToState: 'AccessActive', EventId: 'EVT-DIRECTOR-APPROVE' },
      { FromState: 'AccessActive', ToState: 'Revoked', EventId: 'EVT-REVOKE' }
    ]
  },
  Workflow: {
    StartStepId: 'RequestAccess',
    Steps: [
      {
        StepId: 'RequestAccess',
        StepType: 'Command',
        NextSteps: { 'EVT-REQUEST-ACCESS': 'ManagerApproval' },
        RequiredRoles: ['Employee']
      },
      {
        StepId: 'ManagerApproval',
        StepType: 'HumanTask',
        RequiredRoles: ['Manager'],
        NextSteps: { 'EVT-APPROVE': 'ProvisionCredentials', 'EVT-ESCALATE': 'DirectorEscalation' },
        Sla: { Duration: '24h', TimeoutEvent: 'EVT-ESCALATE', EscalationStepId: 'DirectorEscalation', EscalationRole: 'Director' }
      },
      {
        StepId: 'DirectorEscalation',
        StepType: 'HumanTask',
        RequiredRoles: ['Director'],
        NextSteps: { 'EVT-DIRECTOR-APPROVE': 'ProvisionCredentials' }
      },
      {
        StepId: 'ProvisionCredentials',
        StepType: 'Command',
        NextSteps: { Default: 'SessionExpirationTimer' },
        RequiredRoles: ['SecOps']
      },
      {
        StepId: 'SessionExpirationTimer',
        StepType: 'Timer',
        NextSteps: { 'EVT-REVOKE': 'RevokeAccess' },
        RequiredRoles: ['System'],
        Sla: { Duration: '8h', TimeoutEvent: 'EVT-REVOKE' }
      },
      {
        StepId: 'RevokeAccess',
        StepType: 'Command',
        NextSteps: { Default: 'END' },
        RequiredRoles: ['SecOps']
      }
    ]
  }
});

/** Align ExpenseApprovalV2 so inbox role follows Amount (high → Director, else Manager). */
export function ensureAmountRoutedExpenseApproval(definition: any): any {
  if (!definition) return definition;
  const workflow = definition.Workflow || definition.workflow;
  const steps: any[] = workflow?.Steps || workflow?.steps;
  if (!Array.isArray(steps)) return definition;

  const stepId = (step: any) => String(step?.StepId || step?.stepId || '');
  const hasDirector = steps.some(step => /pendingdirector|directorapproval/i.test(stepId(step)));
  const hasManager = steps.some(step => /pendingmanager|managerapproval/i.test(stepId(step)));
  if (!hasDirector || !hasManager) return definition;

  const draft = steps.find(step => stepId(step).toLowerCase() === 'draft');
  let check = steps.find(step => stepId(step).toLowerCase() === 'checkamount');
  if (!check) {
    check = {
      StepId: 'CheckAmount',
      StepType: 'Decision',
      Conditions: { 'Amount > 5000': 'PendingDirector', Default: 'PendingManager' },
      NextSteps: { Default: 'PendingManager' },
      RequiredRoles: ['System']
    };
    const insertAt = draft ? steps.indexOf(draft) + 1 : 0;
    steps.splice(insertAt, 0, check);
  } else {
    check.StepType = 'Decision';
    check.stepType = 'Decision';
    check.Conditions = { 'Amount > 5000': 'PendingDirector', Default: 'PendingManager' };
    check.conditions = check.Conditions;
    check.NextSteps = { Default: 'PendingManager' };
    check.nextSteps = check.NextSteps;
    check.RequiredRoles = ['System'];
  }

  if (draft) {
    const next = draft.NextSteps || draft.nextSteps || {};
    next['EVT-SUBMIT'] = 'CheckAmount';
    draft.NextSteps = next;
    draft.nextSteps = next;
  }

  const sm = definition.StateMachine || definition.stateMachine;
  const transitions: any[] = sm?.Transitions || sm?.transitions;
  if (Array.isArray(transitions) && sm) {
    const remaining = transitions.filter(item => {
      const from = String(item.FromState || item.fromState || '');
      const eventId = String(item.EventId || item.eventId || '');
      return !(from === 'Draft' && eventId === 'EVT-SUBMIT');
    });
    remaining.unshift(
      { FromState: 'Draft', ToState: 'PendingDirector', EventId: 'EVT-SUBMIT', Condition: 'Amount > 5000' },
      { FromState: 'Draft', ToState: 'PendingManager', EventId: 'EVT-SUBMIT' }
    );
    sm.Transitions = remaining;
    sm.transitions = remaining;
  }

  return definition;
}

export const DEMO_FALLBACKS: Record<string, { id: string; name: string; version: string; definition: any }> = {
  ExpenseApprovalV2: {
    id: 'fallback-expense-v2',
    name: 'ExpenseApprovalV2',
    version: 'demo',
    definition: FALLBACK_EXPENSE_V2
  },
  ExpenseApproval: {
    id: 'fallback-expense',
    name: 'ExpenseApproval',
    version: 'demo',
    definition: FALLBACK_EXPENSE
  },
  OrderSagaFulfillment: {
    id: 'fallback-order-saga',
    name: 'OrderSagaFulfillment',
    version: 'demo',
    definition: FALLBACK_ORDER_SAGA
  },
  LoanUnderwritingFlow: {
    id: 'fallback-loan',
    name: 'LoanUnderwritingFlow',
    version: 'demo',
    definition: FALLBACK_LOAN
  },
  SecOpsAccessGovernance: {
    id: 'fallback-secops',
    name: 'SecOpsAccessGovernance',
    version: 'demo',
    definition: FALLBACK_SECOPS
  },
  IncidentAlertEscalation: {
    id: 'fallback-incident-alert',
    name: 'IncidentAlertEscalation',
    version: 'demo',
    definition: FALLBACK_INCIDENT_ALERT
  }
};
