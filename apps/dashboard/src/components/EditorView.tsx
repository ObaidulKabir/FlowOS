import React, { useState, useEffect } from 'react';
import { WorkflowClass, CreateDraftRequest, ValidationResult } from '../types';
import { X, Save, AlertTriangle, CheckCircle, Info, Layers } from 'lucide-react';
import { WorkflowGraphVisualizer } from './WorkflowGraphVisualizer';
import { DraftSimulator } from './DraftSimulator';
import { StepActionBuilder, StepAction } from './StepActionBuilder';

interface Props {
  item?: WorkflowClass; // If null, creating new
  validation: ValidationResult | null;
  onClose: () => void;
  onSave: (req: CreateDraftRequest) => Promise<void>;
}

// "Smart" Editor Component
export const EditorView: React.FC<Props> = ({ item, validation, onClose, onSave }) => {
  const [name, setName] = useState(item?.name || 'New Workflow');
  const [version, setVersion] = useState(item?.version || '0.1.0');
  const [rightPanelMode, setRightPanelMode] = useState<'visual' | 'simulate'>('visual');
  
  // Structured State for "Smart" Editing
  const [events, setEvents] = useState<{eventId: string, name: string}[]>(
    item?.definition?.Events || []
  );
  const [states, setStates] = useState<string[]>(
    item?.definition?.StateMachine?.States || ['Draft', 'Active', 'Completed']
  );
  const [initialState, setInitialState] = useState(
    item?.definition?.StateMachine?.InitialState || 'Draft'
  );
  const [transitions, setTransitions] = useState<{from: string, to: string, evt: string}[]>(
    item?.definition?.StateMachine?.Transitions?.map((t: any) => ({
        from: t.FromState, to: t.ToState, evt: t.EventId
    })) || []
  );

  // Steps State
  const [startStepId, setStartStepId] = useState(
    item?.definition?.Workflow?.StartStepId || 'Start'
  );
  const [steps, setSteps] = useState<{
    stepId: string, 
    stepType: string, 
    nextSteps: {outcome: string, target: string}[],
    roles: string,
    slaDuration: string,
    slaTimeoutEvent: string,
    slaEscalationStepId: string,
    onEntry: StepAction[],
    onExit: StepAction[],
    onFailure: StepAction[]
  }[]>([]);

  const [jsonMode, setJsonMode] = useState(false);
  const [rawJson, setRawJson] = useState(JSON.stringify(item?.definition || {}, null, 2));

  // Sync JSON to Form (When rawJson changes in JSON mode, or initially)
  useEffect(() => {
      if (item) {
          setName(item.name);
          setVersion(item.version);
          const def = item.definition || {};
          
          const getProp = (obj: any, key: string) => {
              if (!obj) return undefined;
              return obj[key] || obj[key.toLowerCase()] || obj[key.charAt(0).toLowerCase() + key.slice(1)];
          };

          const eventsList = getProp(def, 'Events') || [];
          const sm = getProp(def, 'StateMachine') || {};
          const statesList = getProp(sm, 'States') || ['Draft'];
          const transitionsList = getProp(sm, 'Transitions') || [];
          
          setEvents(eventsList.map((e: any) => ({
              eventId: getProp(e, 'EventId') || '',
              name: getProp(e, 'Name') || ''
          })));
          
          setStates(statesList);
          setInitialState(getProp(sm, 'InitialState') || 'Draft');
          
          setTransitions(transitionsList.map((t: any) => ({
            from: getProp(t, 'FromState') || '', 
            to: getProp(t, 'ToState') || '', 
            evt: getProp(t, 'EventId') || ''
          })));

          const wf = getProp(def, 'Workflow') || {};
          setStartStepId(getProp(wf, 'StartStepId') || 'Start');
          const stepsList = getProp(wf, 'Steps') || [];
          setSteps(stepsList.map((s: any) => {
              const conditionsDict = getProp(s, 'Conditions') || {};
              const nextStepsDict = getProp(s, 'NextSteps') || {};
              const rawRoutes = (getProp(s, 'StepType') === 'Decision' && Object.keys(conditionsDict).length > 0)
                  ? conditionsDict
                  : nextStepsDict;
              const nextStepsArray = Object.keys(rawRoutes).map(k => ({
                  outcome: k,
                  target: rawRoutes[k]
              }));
              const rolesList = getProp(s, 'RequiredRoles') || [];
              const slaRaw = getProp(s, 'Sla') || {};
              const onEntryRaw = getProp(s, 'OnEntry') || getProp(s, 'onEntry') || [];
              const onExitRaw = getProp(s, 'OnExit') || getProp(s, 'onExit') || [];
              const onFailureRaw = getProp(s, 'OnFailure') || getProp(s, 'onFailure') || [];
              
              return {
                  stepId: getProp(s, 'StepId') || '',
                  stepType: getProp(s, 'StepType') || 'Command',
                  nextSteps: nextStepsArray,
                  roles: rolesList.join(', '),
                  slaDuration: getProp(slaRaw, 'Duration') || '',
                  slaTimeoutEvent: getProp(slaRaw, 'TimeoutEvent') || '',
                  slaEscalationStepId: getProp(slaRaw, 'EscalationStepId') || '',
                  onEntry: Array.isArray(onEntryRaw) ? onEntryRaw : [],
                  onExit: Array.isArray(onExitRaw) ? onExitRaw : [],
                  onFailure: Array.isArray(onFailureRaw) ? onFailureRaw : []
              };
          }));

          setRawJson(JSON.stringify(def, null, 2));
      }
  }, [item]);

  // Sync Form to JSON
  useEffect(() => {
    if (!jsonMode) {
        const def = {
            Events: events.map(e => ({ EventId: e.eventId, Name: e.name })),
            StateMachine: {
                InitialState: initialState,
                States: states,
                Transitions: transitions.map(t => ({ FromState: t.from, ToState: t.to, EventId: t.evt }))
            },
            Workflow: {
                StartStepId: startStepId,
                Steps: steps.map(s => {
                    const nextStepsDict: Record<string, string> = {};
                    s.nextSteps.forEach(ns => {
                        if (ns.outcome) nextStepsDict[ns.outcome] = ns.target;
                    });
                    
                    const stepObj: any = {
                        StepId: s.stepId,
                        StepType: s.stepType,
                        NextSteps: nextStepsDict,
                        RequiredRoles: s.roles ? s.roles.split(',').map(r => r.trim()).filter(r => r) : [],
                        OnEntry: s.onEntry && s.onEntry.length > 0 ? s.onEntry : undefined,
                        OnExit: s.onExit && s.onExit.length > 0 ? s.onExit : undefined,
                        OnFailure: s.onFailure && s.onFailure.length > 0 ? s.onFailure : undefined
                    };
                    
                    if (s.stepType === 'Decision') {
                        stepObj.Conditions = nextStepsDict;
                    }

                    if (s.slaDuration || s.slaTimeoutEvent || s.slaEscalationStepId) {
                        stepObj.Sla = {
                            Duration: s.slaDuration,
                            TimeoutEvent: s.slaTimeoutEvent,
                            EscalationStepId: s.slaEscalationStepId
                        };
                    }
                    
                    return stepObj;
                })
            }
        };
        setRawJson(JSON.stringify(def, null, 2));
    }
  }, [events, states, initialState, transitions, steps, startStepId, jsonMode]);

  const handleSave = () => {
    try {
        const def = JSON.parse(rawJson);
        onSave({
            name,
            version,
            definition: def
        });
    } catch (e) {
        alert("Invalid JSON");
    }
  };

  const loadTemplate = (templateName: string) => {
    const commonEvents = [
        { eventId: 'EVT-SUBMIT', name: 'Submit' },
        { eventId: 'EVT-APPROVE', name: 'Approve' },
        { eventId: 'EVT-REJECT', name: 'Reject' }
    ];
    const commonStates = ['Draft', 'Pending', 'Approved', 'Rejected'];
    const commonTransitions = [
        { from: 'Draft', to: 'Pending', evt: 'EVT-SUBMIT' },
        { from: 'Pending', to: 'Approved', evt: 'EVT-APPROVE' },
        { from: 'Pending', to: 'Rejected', evt: 'EVT-REJECT' }
    ];
    const commonInitialState = 'Draft';

    if (templateName === 'Simple') {
        setName('ExpenseApproval');
        setVersion('1.0.0');
        setEvents(commonEvents);
        setStates(commonStates);
        setInitialState(commonInitialState);
        setTransitions(commonTransitions);
        setStartStepId('Draft');
        setSteps([
            { stepId: 'Draft', stepType: 'Command', nextSteps: [{ outcome: 'EVT-SUBMIT', target: 'Pending' }], roles: 'User', slaDuration: '', slaTimeoutEvent: '', slaEscalationStepId: '', onEntry: [], onExit: [], onFailure: [] },
            { stepId: 'Pending', stepType: 'HumanTask', nextSteps: [{ outcome: 'EVT-APPROVE', target: 'Approved' }, { outcome: 'EVT-REJECT', target: 'Rejected' }], roles: 'Manager', slaDuration: '48h', slaTimeoutEvent: 'EVT-ESCALATE', slaEscalationStepId: 'Rejected', onEntry: [], onExit: [], onFailure: [] },
            { stepId: 'Approved', stepType: 'Command', nextSteps: [{ outcome: 'Default', target: 'END' }], roles: '', slaDuration: '', slaTimeoutEvent: '', slaEscalationStepId: '', onEntry: [], onExit: [], onFailure: [] },
            { stepId: 'Rejected', stepType: 'Command', nextSteps: [{ outcome: 'Default', target: 'END' }], roles: '', slaDuration: '', slaTimeoutEvent: '', slaEscalationStepId: '', onEntry: [], onExit: [], onFailure: [] }
        ]);
    } else if (templateName === 'Complex') {
        setName('ExpenseApprovalV2');
        setVersion('1.0.0');
        setEvents(commonEvents);
        setStates(commonStates);
        setInitialState(commonInitialState);
        setTransitions(commonTransitions);
        
        setStartStepId('ValidateInput'); 
        setSteps([
            { stepId: 'ValidateInput', stepType: 'Command', nextSteps: [{ outcome: 'Default', target: 'Draft' }], roles: 'System', slaDuration: '', slaTimeoutEvent: '', slaEscalationStepId: '', onEntry: [], onExit: [], onFailure: [] },
            { stepId: 'Draft', stepType: 'HumanTask', nextSteps: [{ outcome: 'EVT-SUBMIT', target: 'FraudCheck' }], roles: 'User', slaDuration: '', slaTimeoutEvent: '', slaEscalationStepId: '', onEntry: [], onExit: [], onFailure: [] },
            { stepId: 'FraudCheck', stepType: 'Command', nextSteps: [{ outcome: 'Default', target: 'Pending' }], roles: 'System', slaDuration: '', slaTimeoutEvent: '', slaEscalationStepId: '', onEntry: [], onExit: [], onFailure: [] },
            { stepId: 'Pending', stepType: 'HumanTask', nextSteps: [{ outcome: 'EVT-APPROVE', target: 'NotifyApproval' }, { outcome: 'EVT-REJECT', target: 'NotifyRejection' }], roles: 'Manager', slaDuration: '7d', slaTimeoutEvent: 'EVT-ESCALATE', slaEscalationStepId: 'NotifyRejection', onEntry: [], onExit: [], onFailure: [] },
            { stepId: 'NotifyApproval', stepType: 'Event', nextSteps: [{ outcome: 'Default', target: 'Approved' }], roles: 'System', slaDuration: '', slaTimeoutEvent: '', slaEscalationStepId: '', onEntry: [], onExit: [], onFailure: [] },
            { stepId: 'NotifyRejection', stepType: 'Event', nextSteps: [{ outcome: 'Default', target: 'Rejected' }], roles: 'System', slaDuration: '', slaTimeoutEvent: '', slaEscalationStepId: '', onEntry: [], onExit: [], onFailure: [] },
            { stepId: 'Approved', stepType: 'Command', nextSteps: [{ outcome: 'Default', target: 'END' }], roles: '', slaDuration: '', slaTimeoutEvent: '', slaEscalationStepId: '', onEntry: [], onExit: [], onFailure: [] },
            { stepId: 'Rejected', stepType: 'Command', nextSteps: [{ outcome: 'Default', target: 'END' }], roles: '', slaDuration: '', slaTimeoutEvent: '', slaEscalationStepId: '', onEntry: [], onExit: [] , onFailure: []}
        ]);
    } else if (templateName === 'OrderSaga') {
        setName('OrderSagaFulfillment');
        setVersion('1.0.0');
        setEvents([
            { eventId: 'EVT-VALIDATE', name: 'Validate Order' },
            { eventId: 'EVT-PAY-SUCCESS', name: 'Payment Authorized' },
            { eventId: 'EVT-STOCK-LOCKED', name: 'Inventory Reserved' },
            { eventId: 'EVT-SHIP-FAIL', name: 'Shipping Generation Failed' },
            { eventId: 'EVT-COMPENSATE', name: 'Rollback Completed' }
        ]);
        setStates(['Draft', 'PaymentAuthorized', 'InventoryReserved', 'Compensating', 'RolledBack', 'Completed']);
        setInitialState('Draft');
        setTransitions([
            { from: 'Draft', to: 'PaymentAuthorized', evt: 'EVT-PAY-SUCCESS' },
            { from: 'PaymentAuthorized', to: 'InventoryReserved', evt: 'EVT-STOCK-LOCKED' },
            { from: 'InventoryReserved', to: 'Compensating', evt: 'EVT-SHIP-FAIL' },
            { from: 'Compensating', to: 'RolledBack', evt: 'EVT-COMPENSATE' }
        ]);
        setStartStepId('ValidateOrder');
        setSteps([
            {
                stepId: 'ValidateOrder',
                stepType: 'Command',
                nextSteps: [{ outcome: 'Default', target: 'AuthorizePayment' }],
                roles: 'System',
                slaDuration: '',
                slaTimeoutEvent: '',
                slaEscalationStepId: '',
                onEntry: [{ actionType: 'Notification', target: 'System', template: 'Validating order {{OrderId}} with amount ${{Amount}}' }],
                onExit: [],
                onFailure: []
            },
            {
                stepId: 'AuthorizePayment',
                stepType: 'Command',
                nextSteps: [{ outcome: 'EVT-PAY-SUCCESS', target: 'ReserveInventory' }],
                roles: 'System',
                slaDuration: '',
                slaTimeoutEvent: '',
                slaEscalationStepId: '',
                onEntry: [{
                    actionType: 'Webhook',
                    target: 'https://api.stripe.com/v1/charges/hold',
                    template: 'Holding payment for order {{OrderId}}',
                    payloadMapping: { orderRef: 'OrderId', taxedAmount: 'Amount * 1.05' },
                    signPayload: true
                }],
                onExit: [],
                onFailure: [{
                    actionType: 'Webhook',
                    target: 'https://api.stripe.com/v1/refunds/void-hold',
                    template: 'Voiding payment hold for {{OrderId}} due to downstream failure',
                    payloadMapping: { orderRef: 'OrderId' }
                }]
            },
            {
                stepId: 'ReserveInventory',
                stepType: 'Command',
                nextSteps: [{ outcome: 'EVT-STOCK-LOCKED', target: 'GenerateShippingLabel' }],
                roles: 'Warehouse',
                slaDuration: '',
                slaTimeoutEvent: '',
                slaEscalationStepId: '',
                onEntry: [{
                    actionType: 'Webhook',
                    target: 'https://warehouse.internal/api/lock-sku',
                    payloadMapping: { sku: 'ItemSku', quantity: 'Quantity' }
                }],
                onExit: [],
                onFailure: [
                    {
                        actionType: 'Webhook',
                        target: 'https://warehouse.internal/api/release-sku',
                        payloadMapping: { sku: 'ItemSku', quantity: 'Quantity' }
                    },
                    {
                        actionType: 'Notification',
                        target: 'WarehouseOps',
                        template: 'Saga rollback: released SKU {{ItemSku}} for canceled order {{OrderId}}'
                    }
                ]
            },
            {
                stepId: 'GenerateShippingLabel',
                stepType: 'Command',
                nextSteps: [{ outcome: 'Default', target: 'END' }, { outcome: 'EVT-SHIP-FAIL', target: 'CompensateOrder' }],
                roles: 'Logistics',
                slaDuration: '',
                slaTimeoutEvent: '',
                slaEscalationStepId: '',
                onEntry: [],
                onExit: [],
                onFailure: [{
                    actionType: 'Notification',
                    target: 'CustomerSupport',
                    template: 'Shipping label generation failed for {{OrderId}}. Triggering full Saga rollback.'
                }]
            },
            {
                stepId: 'CompensateOrder',
                stepType: 'Command',
                nextSteps: [{ outcome: 'EVT-COMPENSATE', target: 'END' }],
                roles: 'System',
                slaDuration: '',
                slaTimeoutEvent: '',
                slaEscalationStepId: '',
                onEntry: [{
                    actionType: 'Notification',
                    target: 'Customer',
                    template: 'Order {{OrderId}} could not be fulfilled. All holds and inventory reservations were safely rolled back.'
                }],
                onExit: [],
                onFailure: []
            }
        ]);
    } else if (templateName === 'LoanUnderwriting') {
        setName('LoanUnderwritingFlow');
        setVersion('1.0.0');
        setEvents([
            { eventId: 'EVT-APPLY', name: 'Application Submitted' },
            { eventId: 'EVT-AUTO-APPROVE', name: 'Fast-Track Auto Approved' },
            { eventId: 'EVT-MANUAL-REVIEW', name: 'Requires Underwriter Review' },
            { eventId: 'EVT-FINAL-APPROVE', name: 'Underwriter Approved' },
            { eventId: 'EVT-DECLINE', name: 'Application Declined' }
        ]);
        setStates(['Draft', 'Evaluating', 'UnderwritingReview', 'Approved', 'Declined']);
        setInitialState('Draft');
        setTransitions([
            { from: 'Draft', to: 'Evaluating', evt: 'EVT-APPLY' },
            { from: 'Evaluating', to: 'Approved', evt: 'EVT-AUTO-APPROVE' },
            { from: 'Evaluating', to: 'UnderwritingReview', evt: 'EVT-MANUAL-REVIEW' },
            { from: 'UnderwritingReview', to: 'Approved', evt: 'EVT-FINAL-APPROVE' },
            { from: 'UnderwritingReview', to: 'Declined', evt: 'EVT-DECLINE' },
            { from: 'Evaluating', to: 'Declined', evt: 'EVT-DECLINE' }
        ]);
        setStartStepId('IntakeApplication');
        setSteps([
            {
                stepId: 'IntakeApplication',
                stepType: 'Command',
                nextSteps: [{ outcome: 'EVT-APPLY', target: 'EvaluateRisk' }],
                roles: 'Applicant',
                slaDuration: '',
                slaTimeoutEvent: '',
                slaEscalationStepId: '',
                onEntry: [{
                    actionType: 'Notification',
                    target: 'Applicant',
                    template: 'Loan application received for applicant {{ApplicantName}} (Principal: ${{Amount}})'
                }],
                onExit: [],
                onFailure: []
            },
            {
                stepId: 'EvaluateRisk',
                stepType: 'Decision',
                nextSteps: [
                    { outcome: 'CreditScore >= 720 && DebtToIncome < 0.35', target: 'FastTrackDisbursement' },
                    { outcome: 'CreditScore < 580', target: 'DeclineApplication' },
                    { outcome: 'Default', target: 'UnderwriterReview' }
                ],
                roles: 'System',
                slaDuration: '',
                slaTimeoutEvent: '',
                slaEscalationStepId: '',
                onEntry: [],
                onExit: [],
                onFailure: []
            },
            {
                stepId: 'UnderwriterReview',
                stepType: 'HumanTask',
                roles: 'Manager, Director',
                nextSteps: [
                    { outcome: 'EVT-FINAL-APPROVE', target: 'DisburseFunds' },
                    { outcome: 'EVT-DECLINE', target: 'DeclineApplication' }
                ],
                slaDuration: '48h',
                slaTimeoutEvent: 'EVT-DECLINE',
                slaEscalationStepId: 'DeclineApplication',
                onEntry: [{
                    actionType: 'Notification',
                    target: 'Underwriters',
                    template: 'Manual underwriting required for loan ${{Amount}} (CreditScore: {{CreditScore}})'
                }],
                onExit: [],
                onFailure: []
            },
            {
                stepId: 'FastTrackDisbursement',
                stepType: 'Command',
                nextSteps: [{ outcome: 'EVT-AUTO-APPROVE', target: 'DisburseFunds' }],
                roles: 'System',
                slaDuration: '',
                slaTimeoutEvent: '',
                slaEscalationStepId: '',
                onEntry: [],
                onExit: [],
                onFailure: []
            },
            {
                stepId: 'DisburseFunds',
                stepType: 'Command',
                nextSteps: [{ outcome: 'Default', target: 'END' }],
                roles: 'Finance',
                slaDuration: '',
                slaTimeoutEvent: '',
                slaEscalationStepId: '',
                onEntry: [
                    {
                        actionType: 'Webhook',
                        target: 'https://core-banking.partner.com/api/v2/disbursements',
                        template: 'Disbursing ${{Amount}} to recipient account {{AccountNum}}',
                        payloadMapping: {
                            applicant: 'ApplicantName',
                            principal: 'Amount',
                            riskTier: 'CreditScore >= 720 ? "Prime" : "Standard"'
                        },
                        signPayload: true
                    },
                    {
                        actionType: 'Notification',
                        target: 'Applicant',
                        template: 'Congratulations {{ApplicantName}}! Your loan of ${{Amount}} has been approved and funds are being wired.'
                    }
                ],
                onExit: [],
                onFailure: []
            },
            {
                stepId: 'DeclineApplication',
                stepType: 'Command',
                nextSteps: [{ outcome: 'Default', target: 'END' }],
                roles: 'System',
                slaDuration: '',
                slaTimeoutEvent: '',
                slaEscalationStepId: '',
                onEntry: [{
                    actionType: 'Notification',
                    target: 'Applicant',
                    template: 'We regret to inform you that your application for ${{Amount}} could not be approved at this time.'
                }],
                onExit: [],
                onFailure: []
            }
        ]);
    } else if (templateName === 'SecOpsAccess') {
        setName('SecOpsAccessGovernance');
        setVersion('1.0.0');
        setEvents([
            { eventId: 'EVT-REQUEST-ACCESS', name: 'Request Privileged Access' },
            { eventId: 'EVT-APPROVE', name: 'Manager Approved' },
            { eventId: 'EVT-ESCALATE', name: 'SLA Breached - Auto Escalated' },
            { eventId: 'EVT-DIRECTOR-APPROVE', name: 'SecOps Director Approved' },
            { eventId: 'EVT-REVOKE', name: 'Session Expired / Revoked' }
        ]);
        setStates(['Draft', 'PendingManager', 'PendingDirector', 'AccessActive', 'Revoked']);
        setInitialState('Draft');
        setTransitions([
            { from: 'Draft', to: 'PendingManager', evt: 'EVT-REQUEST-ACCESS' },
            { from: 'PendingManager', to: 'AccessActive', evt: 'EVT-APPROVE' },
            { from: 'PendingManager', to: 'PendingDirector', evt: 'EVT-ESCALATE' },
            { from: 'PendingDirector', to: 'AccessActive', evt: 'EVT-DIRECTOR-APPROVE' },
            { from: 'AccessActive', to: 'Revoked', evt: 'EVT-REVOKE' }
        ]);
        setStartStepId('RequestAccess');
        setSteps([
            {
                stepId: 'RequestAccess',
                stepType: 'Command',
                nextSteps: [{ outcome: 'EVT-REQUEST-ACCESS', target: 'ManagerApproval' }],
                roles: 'Employee',
                slaDuration: '',
                slaTimeoutEvent: '',
                slaEscalationStepId: '',
                onEntry: [{
                    actionType: 'Notification',
                    target: 'SecurityTeam',
                    template: 'Access requested by {{UserEmail}} for environment {{Environment}}'
                }],
                onExit: [],
                onFailure: []
            },
            {
                stepId: 'ManagerApproval',
                stepType: 'HumanTask',
                roles: 'Manager',
                nextSteps: [
                    { outcome: 'EVT-APPROVE', target: 'ProvisionCredentials' },
                    { outcome: 'EVT-ESCALATE', target: 'DirectorEscalation' }
                ],
                slaDuration: '24h',
                slaTimeoutEvent: 'EVT-ESCALATE',
                slaEscalationStepId: 'DirectorEscalation',
                onEntry: [{
                    actionType: 'Notification',
                    target: 'DirectManager',
                    template: 'Action Required: Access approval pending for {{UserEmail}} (24h SLA remaining)'
                }],
                onExit: [],
                onFailure: []
            },
            {
                stepId: 'DirectorEscalation',
                stepType: 'HumanTask',
                roles: 'Director',
                nextSteps: [{ outcome: 'EVT-DIRECTOR-APPROVE', target: 'ProvisionCredentials' }],
                slaDuration: '',
                slaTimeoutEvent: '',
                slaEscalationStepId: '',
                onEntry: [{
                    actionType: 'Notification',
                    target: 'SecOpsDirector',
                    template: 'SLA BREACH ALERT: Manager did not respond in 24h. Access request for {{UserEmail}} escalated to SecOps Director.'
                }],
                onExit: [],
                onFailure: []
            },
            {
                stepId: 'ProvisionCredentials',
                stepType: 'Command',
                nextSteps: [{ outcome: 'Default', target: 'SessionExpirationTimer' }],
                roles: 'SecOps',
                slaDuration: '',
                slaTimeoutEvent: '',
                slaEscalationStepId: '',
                onEntry: [
                    {
                        actionType: 'Webhook',
                        target: 'https://iam.internal/api/v1/temp-creds',
                        template: 'Provisioning temporary 8h IAM credentials for {{UserEmail}}',
                        payloadMapping: {
                            user: 'UserEmail',
                            scope: 'Environment',
                            ttlHours: '8'
                        },
                        signPayload: true
                    },
                    {
                        actionType: 'Notification',
                        target: 'User',
                        template: 'Temporary access to {{Environment}} granted for 8 hours.'
                    }
                ],
                onExit: [],
                onFailure: []
            },
            {
                stepId: 'SessionExpirationTimer',
                stepType: 'Timer',
                nextSteps: [{ outcome: 'EVT-REVOKE', target: 'RevokeAccess' }],
                roles: 'System',
                slaDuration: '8h',
                slaTimeoutEvent: 'EVT-REVOKE',
                slaEscalationStepId: '',
                onEntry: [],
                onExit: [],
                onFailure: []
            },
            {
                stepId: 'RevokeAccess',
                stepType: 'Command',
                nextSteps: [{ outcome: 'Default', target: 'END' }],
                roles: 'SecOps',
                slaDuration: '',
                slaTimeoutEvent: '',
                slaEscalationStepId: '',
                onEntry: [
                    {
                        actionType: 'Webhook',
                        target: 'https://iam.internal/api/v1/revoke-creds',
                        template: 'Revoking credentials for {{UserEmail}} upon 8h timer expiration',
                        payloadMapping: { user: 'UserEmail' }
                    },
                    {
                        actionType: 'Notification',
                        target: 'User',
                        template: 'Your temporary access session for {{Environment}} has expired and credentials have been revoked.'
                    }
                ],
                onExit: [],
                onFailure: []
            }
        ]);
    }
  };

  return (
    <div className="fixed inset-0 bg-black/80 backdrop-blur-sm overflow-hidden h-full w-full flex justify-center items-center z-50 p-4">
      <div className="bg-slate-900 border border-slate-700 text-slate-100 w-[96vw] h-[92vh] rounded-2xl shadow-2xl flex overflow-hidden">
        
        {/* Left: Editor (Scrollable) */}
        <div className="flex-1 flex flex-col border-r border-slate-800 overflow-hidden">
            <div className="p-4 border-b border-slate-800 flex justify-between items-center bg-slate-950/70">
                <h2 className="text-lg font-bold text-white">{item ? `Edit ${item.name}` : 'Create WorkflowClass Draft'}</h2>
                <div className="space-x-2 flex items-center">
                    {!item && (
                        <select onChange={(e) => loadTemplate(e.target.value)} className="text-xs bg-slate-800 border border-slate-700 text-slate-200 p-1.5 rounded-lg mr-2" defaultValue="">
                            <option value="" disabled>Load Blueprint Example...</option>
                            <option value="OrderSaga">Flagship: Order Saga Fulfillment (Distributed Saga & Rollback)</option>
                            <option value="LoanUnderwriting">Flagship: Loan Underwriting (Decision Engine & HMAC)</option>
                            <option value="SecOpsAccess">Flagship: SecOps Access Governance (24h SLA Timers)</option>
                            <option value="Complex">Legacy: Expense Approval V2 (Decoupled)</option>
                            <option value="Simple">Legacy: Expense Approval V1 (Direct Mapping)</option>
                        </select>
                    )}
                    <button onClick={() => setJsonMode(!jsonMode)} className="px-3 py-1.5 bg-slate-800 hover:bg-slate-700 text-slate-200 border border-slate-700 rounded-lg text-xs font-medium transition-colors">
                        {jsonMode ? "Switch to Form" : "Switch to JSON"}
                    </button>
                    <button onClick={onClose} className="text-slate-400 hover:text-white p-1 rounded transition-colors">
                        <X size={20} />
                    </button>
                </div>
            </div>

            <div className="flex-1 overflow-y-auto p-6 bg-slate-900">
                <div className="grid grid-cols-2 gap-4 mb-6">
                    <div>
                        <label className="block text-xs font-medium text-slate-300 mb-1">Blueprint Name</label>
                        <input value={name} onChange={e => setName(e.target.value)} className="w-full bg-slate-800 border border-slate-700 rounded-lg p-2 text-sm text-white focus:outline-none focus:border-blue-500 font-medium" />
                    </div>
                    <div>
                        <label className="block text-xs font-medium text-slate-300 mb-1">Version (SemVer)</label>
                        <input value={version} onChange={e => setVersion(e.target.value)} className="w-full bg-slate-800 border border-slate-700 rounded-lg p-2 text-sm text-white font-mono focus:outline-none focus:border-blue-500" />
                    </div>
                </div>

                {jsonMode ? (
                    <textarea 
                        value={rawJson} 
                        onChange={e => setRawJson(e.target.value)} 
                        className="w-full h-full font-mono text-xs p-4 border border-slate-800 rounded-xl bg-slate-950 text-blue-300/90 min-h-[500px] leading-relaxed focus:outline-none focus:border-blue-500"
                    />
                ) : (
                    <div className="space-y-8">
                        {/* 1. Events */}
                        <section className="border border-indigo-100 rounded-lg overflow-hidden">
                            <div className="bg-indigo-50 px-4 py-2 border-b border-indigo-100">
                                <h3 className="font-semibold text-indigo-900">1. Events (Facts)</h3>
                                <p className="text-xs text-indigo-700">What facts can happen in this domain?</p>
                            </div>
                            <div className="p-4 space-y-2">
                                {events.map((evt, idx) => (
                                    <div key={idx} className="flex gap-2">
                                        <input value={evt.eventId} onChange={e => {
                                            const newEvts = [...events]; newEvts[idx].eventId = e.target.value; setEvents(newEvts);
                                        }} placeholder="Event ID (e.g. EVT-SUBMIT)" className="flex-1 border p-2 rounded text-sm" />
                                        <input value={evt.name} onChange={e => {
                                            const newEvts = [...events]; newEvts[idx].name = e.target.value; setEvents(newEvts);
                                        }} placeholder="Display Name" className="flex-1 border p-2 rounded text-sm" />
                                        <button onClick={() => setEvents(events.filter((_, i) => i !== idx))} className="text-red-500 hover:bg-red-50 p-1 rounded">×</button>
                                    </div>
                                ))}
                                <button onClick={() => setEvents([...events, {eventId: '', name: ''}])} className="text-indigo-600 text-sm font-medium hover:underline">+ Add Event</button>
                            </div>
                        </section>

                        {/* 2. State Machine */}
                        <section className="border border-amber-100 rounded-lg overflow-hidden">
                            <div className="bg-amber-50 px-4 py-2 border-b border-amber-100">
                                <h3 className="font-semibold text-amber-900">2. State Machine (The Law)</h3>
                                <p className="text-xs text-amber-700">Defines legal statuses and transitions. Cannot be bypassed.</p>
                            </div>
                            <div className="p-4 space-y-4">
                                <div>
                                    <label className="block text-xs font-medium text-gray-700 mb-1">States</label>
                                    <div className="flex flex-wrap gap-2 mb-2">
                                        {states.map((st, idx) => (
                                            <span key={idx} className="bg-white border px-2 py-1 rounded flex items-center gap-1 text-sm">
                                                {st}
                                                <button onClick={() => setStates(states.filter(s => s !== st))} className="text-red-500 text-xs ml-1">×</button>
                                            </span>
                                        ))}
                                        <input 
                                            placeholder="+ Add State (Enter)" 
                                            className="border p-1 rounded text-sm min-w-[120px]" 
                                            onKeyDown={e => {
                                                if(e.key === 'Enter') {
                                                    setStates([...states, e.currentTarget.value]);
                                                    e.currentTarget.value = '';
                                                }
                                            }}
                                        />
                                    </div>
                                </div>
                                
                                <div>
                                    <label className="block text-xs font-medium text-gray-700 mb-1">Initial State</label>
                                    <select 
                                        value={initialState} 
                                        onChange={e => setInitialState(e.target.value)}
                                        className="w-full border p-2 rounded text-sm"
                                    >
                                        {states.map(s => <option key={s} value={s}>{s}</option>)}
                                    </select>
                                </div>

                                <div>
                                    <label className="block text-xs font-medium text-gray-700 mb-1">Legal Transitions</label>
                                    <div className="space-y-2">
                                        {transitions.map((t, idx) => (
                                            <div key={idx} className="flex gap-2 items-center bg-gray-50 p-2 rounded">
                                                <select value={t.from} onChange={e => {
                                                    const newTr = [...transitions]; newTr[idx].from = e.target.value; setTransitions(newTr);
                                                }} className="border p-1 rounded text-sm flex-1">
                                                    {states.map(s => <option key={s} value={s}>{s}</option>)}
                                                </select>
                                                <span className="text-gray-400 text-xs">+</span>
                                                <select value={t.evt} onChange={e => {
                                                    const newTr = [...transitions]; newTr[idx].evt = e.target.value; setTransitions(newTr);
                                                }} className="border p-1 rounded text-sm flex-1">
                                                    <option value="">(Event)</option>
                                                    {events.map(e => <option key={e.eventId} value={e.eventId}>{e.name}</option>)}
                                                </select>
                                                <span className="text-gray-400 text-xs">→</span>
                                                <select value={t.to} onChange={e => {
                                                    const newTr = [...transitions]; newTr[idx].to = e.target.value; setTransitions(newTr);
                                                }} className="border p-1 rounded text-sm flex-1">
                                                    {states.map(s => <option key={s} value={s}>{s}</option>)}
                                                </select>
                                                <button onClick={() => setTransitions(transitions.filter((_, i) => i !== idx))} className="text-red-500">×</button>
                                            </div>
                                        ))}
                                        <button onClick={() => setTransitions([...transitions, {from: states[0], to: states[0], evt: ''}])} className="text-amber-600 text-sm font-medium hover:underline">+ Add Transition</button>
                                    </div>
                                </div>
                            </div>
                        </section>

                        {/* 3. Workflow */}
                        <section className="border border-green-100 rounded-lg overflow-hidden">
                            <div className="bg-green-50 px-4 py-2 border-b border-green-100">
                                <h3 className="font-semibold text-green-900">3. Workflow (The Work)</h3>
                                <p className="text-xs text-green-700">Procedural steps. Steps ≠ States.</p>
                            </div>
                            <div className="p-4 space-y-4">
                                <div>
                                    <label className="block text-xs font-medium text-gray-700 mb-1">Start Step ID</label>
                                    <input 
                                        value={startStepId} 
                                        onChange={e => setStartStepId(e.target.value)} 
                                        className="w-full border p-2 rounded text-sm" 
                                        placeholder="e.g. Start"
                                    />
                                </div>

                                <div className="space-y-4">
                                    {steps.map((step, idx) => (
                                        <div key={idx} className="border bg-white p-4 rounded shadow-sm relative group">
                                            <button onClick={() => setSteps(steps.filter((_, i) => i !== idx))} className="absolute top-2 right-2 text-gray-300 hover:text-red-500 opacity-0 group-hover:opacity-100 transition-opacity">
                                                <X size={16} />
                                            </button>
                                            
                                            <div className="grid grid-cols-2 gap-4 mb-3">
                                                <div>
                                                    <label className="block text-xs text-gray-500">Step ID</label>
                                                    <input 
                                                        value={step.stepId} 
                                                        onChange={e => {
                                                            const newSteps = [...steps]; newSteps[idx].stepId = e.target.value; setSteps(newSteps);
                                                        }}
                                                        className="w-full border p-2 rounded text-sm font-medium" 
                                                        placeholder="Step ID"
                                                    />
                                                </div>
                                                <div>
                                                    <label className="block text-xs text-gray-500">Type</label>
                                                    <select 
                                                        value={step.stepType} 
                                                        onChange={e => {
                                                            const newSteps = [...steps]; newSteps[idx].stepType = e.target.value; setSteps(newSteps);
                                                        }}
                                                        className="w-full border p-2 rounded text-sm"
                                                    >
                                                        <option value="Command">Command</option>
                                                        <option value="HumanTask">HumanTask</option>
                                                        <option value="Event">Event</option>
                                                        <option value="Decision">Decision</option>
                                                        <option value="Timer">Timer</option>
                                                    </select>
                                                </div>
                                            </div>

                                            <div className="mb-3">
                                                <label className="block text-xs text-gray-500 mb-1">Next Steps (Routes)</label>
                                                {step.nextSteps.map((ns, nsIdx) => (
                                                    <div key={nsIdx} className="flex gap-2 mb-2 items-center">
                                                        <select 
                                                            value={ns.outcome} 
                                                            onChange={e => {
                                                                const newSteps = [...steps]; 
                                                                newSteps[idx].nextSteps[nsIdx].outcome = e.target.value; 
                                                                setSteps(newSteps);
                                                            }}
                                                            className="flex-1 border p-1 rounded text-xs"
                                                        >
                                                            <option value="">(Select Outcome)</option>
                                                            <option value="Default">Default</option>
                                                            {events.map(e => <option key={e.eventId} value={e.eventId}>{e.name}</option>)}
                                                        </select>
                                                        <span className="text-gray-400 text-xs">→</span>
                                                        <input 
                                                            value={ns.target} 
                                                            onChange={e => {
                                                                const newSteps = [...steps]; 
                                                                newSteps[idx].nextSteps[nsIdx].target = e.target.value; 
                                                                setSteps(newSteps);
                                                            }}
                                                            placeholder="Target Step ID"
                                                            className="flex-1 border p-1 rounded text-xs"
                                                        />
                                                        <button 
                                                            onClick={() => {
                                                                const newSteps = [...steps]; 
                                                                newSteps[idx].nextSteps = newSteps[idx].nextSteps.filter((_, i) => i !== nsIdx);
                                                                setSteps(newSteps);
                                                            }}
                                                            className="text-red-400 hover:text-red-600"
                                                        >×</button>
                                                    </div>
                                                ))}
                                                <button 
                                                    onClick={() => {
                                                        const newSteps = [...steps];
                                                        newSteps[idx].nextSteps.push({ outcome: '', target: '' });
                                                        setSteps(newSteps);
                                                    }}
                                                    className="text-xs text-green-600 hover:underline"
                                                >+ Add Route</button>
                                            </div>

                                            <div>
                                                <label className="block text-xs text-gray-500">Roles</label>
                                                <input 
                                                    value={step.roles}
                                                    onChange={e => {
                                                        const newSteps = [...steps]; newSteps[idx].roles = e.target.value; setSteps(newSteps);
                                                    }}
                                                    className="w-full border p-1 rounded text-sm"
                                                    placeholder="Required Roles (comma separated)"
                                                />
                                            </div>

                                            <div className="mt-3 pt-3 border-t border-gray-100">
                                                <label className="block text-[11px] font-semibold text-indigo-500 uppercase tracking-wider mb-2">SLA / Timeouts</label>
                                                <div className="grid grid-cols-3 gap-2">
                                                    <div>
                                                        <label className="block text-[10px] text-gray-500">Duration (e.g. 24h)</label>
                                                        <input 
                                                            value={step.slaDuration || ''}
                                                            onChange={e => {
                                                                const newSteps = [...steps]; newSteps[idx].slaDuration = e.target.value; setSteps(newSteps);
                                                            }}
                                                            className="w-full border p-1 rounded text-xs"
                                                            placeholder="Leave empty for none"
                                                        />
                                                    </div>
                                                    <div>
                                                        <label className="block text-[10px] text-gray-500">Timeout Event</label>
                                                        <select 
                                                            value={step.slaTimeoutEvent || ''}
                                                            onChange={e => {
                                                                const newSteps = [...steps]; newSteps[idx].slaTimeoutEvent = e.target.value; setSteps(newSteps);
                                                            }}
                                                            className="w-full border p-1 rounded text-xs"
                                                        >
                                                            <option value="">(Select)</option>
                                                            {events.map(e => <option key={e.eventId} value={e.eventId}>{e.name}</option>)}
                                                        </select>
                                                    </div>
                                                    <div>
                                                        <label className="block text-[10px] text-gray-500">Escalation Target</label>
                                                        <input 
                                                            value={step.slaEscalationStepId || ''}
                                                            onChange={e => {
                                                                const newSteps = [...steps]; newSteps[idx].slaEscalationStepId = e.target.value; setSteps(newSteps);
                                                            }}
                                                            className="w-full border p-1 rounded text-xs"
                                                            placeholder="Target Step ID"
                                                        />
                                                    </div>
                                                </div>
                                            </div>

                                            {/* Lifecycle Hooks Builder (OnEntry / OnExit / OnFailure) */}
                                            <StepActionBuilder
                                                stepId={step.stepId}
                                                onEntry={step.onEntry || []}
                                                onExit={step.onExit || []}
                                                onFailure={step.onFailure || []}
                                                availableEvents={events}
                                                onChange={(newOnEntry, newOnExit, newOnFailure) => {
                                                    const newSteps = [...steps];
                                                    newSteps[idx].onEntry = newOnEntry;
                                                    newSteps[idx].onExit = newOnExit;
                                                    newSteps[idx].onFailure = newOnFailure;
                                                    setSteps(newSteps);
                                                }}
                                            />
                                        </div>
                                    ))}
                                    <button 
                                        onClick={() => setSteps([...steps, { stepId: '', stepType: 'Command', nextSteps: [], roles: '', slaDuration: '', slaTimeoutEvent: '', slaEscalationStepId: '', onEntry: [], onExit: [], onFailure: [] }])}
                                        className="w-full py-2 border-2 border-dashed border-gray-300 rounded text-gray-500 hover:border-blue-300 hover:text-blue-500 transition-colors"
                                    >
                                        + Add Workflow Step
                                    </button>
                                </div>
                            </div>
                        </section>
                    </div>
                )}
            </div>

            <div className="p-4 border-t border-slate-800 bg-slate-950/70 flex justify-between items-center">
                <span className="text-xs text-slate-400 italic">Authoritative validation is run automatically on save.</span>
                <button onClick={handleSave} className="px-6 py-2 bg-blue-600 hover:bg-blue-500 text-white rounded-xl flex items-center gap-2 font-semibold text-xs shadow-lg transition-all">
                    <Save size={16} />
                    Save Draft
                </button>
            </div>
        </div>

        {/* Right: Live Preview & Validation */}
        <div className="w-[45vw] bg-slate-950 border-l border-slate-800 flex flex-col overflow-hidden">
            
            {/* Live Graph / Simulator Section */}
            <div className="flex-1 flex flex-col overflow-hidden">
                <div className="p-3 border-b border-slate-800 bg-slate-900/90 flex justify-between items-center">
                    <div className="flex items-center gap-1 bg-slate-950 p-1 rounded-lg border border-slate-800 text-xs">
                        <button
                            onClick={() => setRightPanelMode('visual')}
                            className={`px-2.5 py-1 rounded transition-all font-medium flex items-center gap-1.5 ${
                                rightPanelMode === 'visual' ? 'bg-blue-600 text-white shadow' : 'text-slate-400 hover:text-white'
                            }`}
                        >
                            <Layers size={14} /> Live Blueprint Preview
                        </button>
                        <button
                            onClick={() => setRightPanelMode('simulate')}
                            className={`px-2.5 py-1 rounded transition-all font-medium flex items-center gap-1.5 ${
                                rightPanelMode === 'simulate' ? 'bg-emerald-600 text-white shadow' : 'text-slate-400 hover:text-white'
                            }`}
                        >
                            Sandbox Simulator
                        </button>
                    </div>
                    {rightPanelMode === 'visual' && (
                        <span className="text-[10px] bg-blue-500/20 text-blue-300 border border-blue-500/30 px-2 py-0.5 rounded">Updates as you type</span>
                    )}
                </div>
                <div className="flex-1 overflow-auto bg-slate-950 p-2">
                    {rightPanelMode === 'visual' ? (
                        <WorkflowGraphVisualizer 
                            definition={(() => {
                                try { return JSON.parse(rawJson); } catch (e) { return null; }
                            })()} 
                            initialView="both" 
                        />
                    ) : (
                        <DraftSimulator 
                            definition={(() => {
                                try { return JSON.parse(rawJson); } catch (e) { return null; }
                            })()}
                        />
                    )}
                </div>
            </div>

            {/* Validation Report Section */}
            <div className="h-[30vh] border-t border-slate-800 flex flex-col bg-slate-900/50">
                <div className="p-3 border-b border-slate-800 bg-slate-900/90">
                    <h3 className="font-bold text-white text-sm flex items-center gap-2">
                        {validation ? (
                            validation.isValid ? <CheckCircle className="text-emerald-400" size={18} /> : <AlertTriangle className="text-rose-400" size={18} />
                        ) : (
                            <Info className="text-slate-400" size={18} />
                        )}
                        Validation Report
                    </h3>
                </div>
                <div className="flex-1 overflow-y-auto p-4">
                    {!validation ? (
                        <div className="text-center text-slate-500 mt-4 space-y-2">
                            <p className="text-xs">Save the draft to run full validation.</p>
                            <p className="text-[11px] text-slate-600">Validation verifies Schema, Graph Completeness, and Governance Rules.</p>
                        </div>
                    ) : validation.isValid ? (
                        <div className="text-center text-emerald-400 mt-4 space-y-1">
                            <p className="font-semibold text-sm">No issues found.</p>
                            <p className="text-xs text-slate-400">Blueprint is valid and ready to be published to engine.</p>
                        </div>
                    ) : (
                        <div className="space-y-3">
                            {validation.errors.map((err, idx) => (
                                <div key={idx} className="bg-slate-900 p-3 rounded-xl border border-rose-500/30 shadow-sm border-l-4 border-l-rose-500">
                                    <div className="flex justify-between items-start mb-1">
                                        <span className="text-[10px] font-bold text-rose-400 font-mono tracking-wider">{err.code}</span>
                                        <span className="text-[10px] text-slate-500">{err.category}</span>
                                    </div>
                                    <p className="text-xs font-medium text-slate-200 mb-1">{err.message}</p>
                                    {err.element && (
                                        <div className="text-[10px] text-slate-400 mt-1.5 bg-slate-950 p-1.5 rounded font-mono">
                                            Location: <span className="text-blue-300">{err.element}</span>
                                        </div>
                                    )}
                                </div>
                            ))}
                        </div>
                    )}
                </div>
            </div>
        </div>

      </div>
    </div>
  );
};
