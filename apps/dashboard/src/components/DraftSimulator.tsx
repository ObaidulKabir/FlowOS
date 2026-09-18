import React, { useState, useEffect, useMemo } from 'react';
import { 
  Play, RotateCcw, Activity, ArrowRight, Clock, UserCheck, Cpu, 
  Sparkles, Sliders, Database, AlertTriangle, Check,
  Zap, Bell, Send, Radio
} from 'lucide-react';
import { WorkflowGraphVisualizer } from './WorkflowGraphVisualizer';

interface Props {
  definition: any;
}

const PRESET_PAYLOADS: Record<string, { label: string; data: Record<string, any> }> = {
  highExpense: {
    label: 'High Expense ($7,500)',
    data: {
      Amount: 7500,
      Currency: 'USD',
      Category: 'Equipment',
      Department: 'Engineering',
      Urgent: true,
      Requester: 'Alice Smith'
    }
  },
  lowExpense: {
    label: 'Low Expense ($450)',
    data: {
      Amount: 450,
      Currency: 'USD',
      Category: 'OfficeSupplies',
      Department: 'Marketing',
      Urgent: false,
      Requester: 'Bob Jones'
    }
  },
  travelRequest: {
    label: 'Travel Request ($2,200)',
    data: {
      Amount: 2200,
      Currency: 'EUR',
      Category: 'Travel',
      Department: 'Sales',
      Destination: 'London',
      Urgent: true,
      Requester: 'Carol Danvers'
    }
  }
};

/** Prefer business-facing payload keys when summarizing context for the execution log. */
const BUSINESS_CONTEXT_PRIORITY = [
  'OrderId', 'orderId', 'ApplicantName', 'Requester', 'CustomerId', 'Amount', 'Currency',
  'Category', 'Department', 'ItemSku', 'Quantity', 'CreditScore', 'DebtToIncome',
  'Destination', 'Urgent', 'Description', 'ApprovalLimit', 'Principal', 'LoanId'
];

const formatPayloadValue = (value: unknown): string => {
  if (value === null || value === undefined) return 'null';
  if (typeof value === 'string') return value.length > 48 ? `${value.slice(0, 45)}…` : value;
  if (typeof value === 'number' || typeof value === 'boolean') return String(value);
  try {
    const json = JSON.stringify(value);
    return json.length > 48 ? `${json.slice(0, 45)}…` : json;
  } catch {
    return String(value);
  }
};

const buildBusinessContextSnapshot = (
  currentPayload: Record<string, any> | null | undefined,
  options?: {
    maxFields?: number;
    eventId?: string;
    eventLabel?: string;
    entityType?: string;
  }
): string | null => {
  if (!currentPayload || typeof currentPayload !== 'object') return null;

  const maxFields = options?.maxFields ?? 6;
  const used = new Set<string>();
  const fields: string[] = [];

  const pushField = (key: string) => {
    if (used.has(key) || !(key in currentPayload) || fields.length >= maxFields) return;
    used.add(key);
    fields.push(`${key}=${formatPayloadValue(currentPayload[key])}`);
  };

  for (const key of BUSINESS_CONTEXT_PRIORITY) pushField(key);
  for (const key of Object.keys(currentPayload)) pushField(key);

  if (fields.length === 0 && !options?.eventLabel && !options?.entityType) return null;

  const parts: string[] = [];
  if (options?.entityType) parts.push(`entity=${options.entityType}`);
  if (options?.eventLabel && options.eventLabel !== options.eventId) {
    parts.push(`event="${options.eventLabel}"`);
  } else if (options?.eventId && options.eventId.toLowerCase() !== 'default') {
    parts.push(`event=${options.eventId}`);
  }
  if (fields.length > 0) parts.push(fields.join(', '));

  return parts.length > 0 ? `Business context: ${parts.join(' · ')}` : null;
};

export const DraftSimulator: React.FC<Props> = ({ definition }) => {
  const [currentStepId, setCurrentStepId] = useState<string>('');
  const [currentState, setCurrentState] = useState<string>('');
  const [history, setHistory] = useState<string[]>([]);
  const [activeSideTab, setActiveSideTab] = useState<'controls' | 'context'>('controls');

  // Runtime Payload & Context State
  const [payload, setPayload] = useState<Record<string, any>>(PRESET_PAYLOADS.highExpense.data);
  const [payloadText, setPayloadText] = useState<string>(JSON.stringify(PRESET_PAYLOADS.highExpense.data, null, 2));
  const [payloadError, setPayloadError] = useState<string | null>(null);
  const [simulatedRole, setSimulatedRole] = useState<string>('Manager');
  /** current = show this step's events + required/active role; asRole = filter actions to the selected simulated role */
  const [eventViewMode, setEventViewMode] = useState<'current' | 'asRole'>('current');

  // Safe case-insensitive helper
  const getProp = (obj: any, ...keys: string[]) => {
    if (!obj) return undefined;
    for (const key of keys) {
      if (obj[key] !== undefined) return obj[key];
      const lower = key.toLowerCase();
      if (obj[lower] !== undefined) return obj[lower];
      const camel = key.charAt(0).toLowerCase() + key.slice(1);
      if (obj[camel] !== undefined) return obj[camel];
    }
    return undefined;
  };

  /** Normalize NextSteps dict or [{outcome,target}] arrays into stable routes. */
  const getNextStepRoutes = (step: any): { outcome: string; target: string }[] => {
    const raw = getProp(step, 'nextSteps', 'NextSteps');
    if (!raw) return [];
    if (Array.isArray(raw)) {
      return raw
        .map((item: any) => ({
          outcome: String(getProp(item, 'outcome', 'Outcome', 'eventId', 'EventId') || 'Default'),
          target: String(getProp(item, 'target', 'Target', 'stepId', 'StepId') || 'END')
        }))
        .filter(route => route.outcome);
    }
    if (typeof raw === 'object') {
      return Object.entries(raw).map(([outcome, target]) => ({
        outcome: String(outcome),
        target: String(target ?? 'END')
      }));
    }
    return [];
  };

  const pickAutoStepRoute = (routes: { outcome: string; target: string }[]) => {
    if (routes.length === 0) return { outcome: 'Default', target: 'END' };
    const defaultRoute = routes.find(route => route.outcome.trim().toLowerCase() === 'default');
    return defaultRoute || routes[0];
  };

  const wfObj = getProp(definition, 'workflow', 'Workflow') || definition;
  const rawSteps: any[] = getProp(wfObj, 'steps', 'Steps') || [];
  const startStepId = getProp(wfObj, 'startStepId', 'StartStepId') || 'Start';
  
  const smObj = getProp(definition, 'stateMachine', 'StateMachine') || {};
  const initialState = getProp(smObj, 'initialState', 'InitialState') || '';
  const transitions: any[] = getProp(smObj, 'transitions', 'Transitions') || [];
  const entityType =
    getProp(smObj, 'entityType', 'EntityType') ||
    getProp(definition, 'entityType', 'EntityType') ||
    '';
  const catalogEvents: any[] = getProp(definition, 'events', 'Events') || [];

  const resolveEventLabel = (eventId: string): string | undefined => {
    if (!eventId) return undefined;
    const match = catalogEvents.find((event: any) => {
      const id = String(getProp(event, 'eventId', 'EventId') || '');
      return id.trim().toLowerCase() === eventId.trim().toLowerCase();
    });
    const name = match ? getProp(match, 'name', 'Name') : undefined;
    return name ? String(name) : undefined;
  };

  const appendBusinessContext = (
    logs: string[],
    currentPayload: Record<string, any>,
    options?: { eventId?: string; maxFields?: number }
  ) => {
    const snapshot = buildBusinessContextSnapshot(currentPayload, {
      maxFields: options?.maxFields,
      eventId: options?.eventId,
      eventLabel: options?.eventId ? resolveEventLabel(options.eventId) : undefined,
      entityType: entityType || undefined
    });
    if (snapshot) logs.push(snapshot);
  };

  const getStepRoles = (step: any): string[] => {
    if (!step) return [];
    const raw = getProp(step, 'allowedRoles', 'AllowedRoles', 'requiredRoles', 'RequiredRoles', 'roles', 'Roles');
    if (!raw) {
      const type = (getProp(step, 'stepType', 'StepType') || '').toString().toLowerCase();
      if (type.includes('command') || type.includes('event') || type.includes('timer')) return ['System'];
      return ['Unassigned'];
    }
    if (Array.isArray(raw)) return raw.map((r: any) => r.toString().trim()).filter(Boolean);
    if (typeof raw === 'string') return raw.split(',').map((r: string) => r.trim()).filter(Boolean);
    return [raw.toString()];
  };

  // Collect all known roles from the blueprint
  const allKnownRoles = useMemo(() => {
    const roleSet = new Set<string>(['User', 'Manager', 'Director', 'Admin', 'System']);
    rawSteps.forEach(s => {
      getStepRoles(s).forEach(r => {
        if (r && r !== 'Anyone' && r !== 'Unassigned') roleSet.add(r);
      });
    });
    catalogEvents.forEach((e: any) => {
      const allowed = getProp(e, 'allowedRoles', 'AllowedRoles') || [];
      if (Array.isArray(allowed)) {
        allowed.forEach((r: any) => {
          if (r && r !== 'Anyone' && r !== 'Unassigned') roleSet.add(String(r));
        });
      }
    });
    return Array.from(roleSet);
  }, [rawSteps, catalogEvents]);

  const catalogRoles: any[] = getProp(definition, 'roles', 'Roles') || [];

  const getRoleGrantedCapabilities = (role: string): string[] => {
    const roleKey = (role || '').trim().toLowerCase();
    if (!roleKey) return [];
    const match = catalogRoles.find((item: any) => {
      const name = String(getProp(item, 'name', 'Name') || '').trim().toLowerCase();
      return name === roleKey;
    });
    const granted = getProp(match, 'grantedCapabilities', 'GrantedCapabilities') || [];
    return Array.isArray(granted) ? granted.map((cap: any) => String(cap)).filter(Boolean) : [];
  };

  const getRequiredCapabilities = (source: any): string[] => {
    const raw = getProp(source, 'requiredCapabilities', 'RequiredCapabilities');
    if (!raw) return [];
    if (Array.isArray(raw)) return raw.map((cap: any) => String(cap).trim()).filter(Boolean);
    return [String(raw)];
  };

  const getEventRequiredCapabilities = (eventId: string): string[] => {
    if (!eventId) return [];
    const eventIdKey = eventId.trim().toLowerCase();
    const found = catalogEvents.find((e: any) => {
      const id = (getProp(e, 'eventId', 'EventId') || getProp(e, 'name', 'Name') || '').toString().toLowerCase();
      return id === eventIdKey;
    });
    return found ? getRequiredCapabilities(found) : [];
  };

  const capabilitiesAllow = (role: string, required: string[]): boolean => {
    const roleKey = (role || '').trim().toLowerCase();
    if (!roleKey) return false;
    if (roleKey === 'admin') return true;
    if (required.length === 0) return false;
    const granted = getRoleGrantedCapabilities(role).map(cap => cap.toLowerCase());
    return required.some(cap => {
      const key = cap.toLowerCase();
      if (granted.includes(key)) return true;
      return granted.includes('event.publish') && key.startsWith('event.publish.');
    });
  };

  const getEventAllowedRoles = (eventId: string): string[] => {
    if (!eventId) return [];
    const eventIdKey = eventId.trim().toLowerCase();
    const found = catalogEvents.find((e: any) => {
      const id = (getProp(e, 'eventId', 'EventId') || getProp(e, 'name', 'Name') || '').toString().toLowerCase();
      return id === eventIdKey;
    });
    if (!found) return [];
    const roles = getProp(found, 'allowedRoles', 'AllowedRoles') || [];
    return Array.isArray(roles) ? roles.map((r: any) => String(r)) : [];
  };

  const roleCanActOnStep = (step: any, role: string): boolean => {
    if (!step) return false;
    const roleKey = (role || '').trim().toLowerCase();
    if (!roleKey) return false;
    if (roleKey === 'admin') return true;

    const required = getRequiredCapabilities(step);
    if (required.length > 0) {
      return capabilitiesAllow(role, required);
    }

    const roles = getStepRoles(step);
    if (roles.length === 0 || roles.some(r => ['anyone', 'unassigned'].includes(r.toLowerCase()))) {
      return true;
    }
    if (roles.map(r => r.toLowerCase()).includes(roleKey)) return true;

    const type = (getProp(step, 'stepType', 'StepType') || '').toString().toLowerCase();
    const isSystemStep =
      type.includes('command') ||
      type.includes('event') ||
      type.includes('timer') ||
      type.includes('decision') ||
      type.includes('choice') ||
      type.includes('fork') ||
      type.includes('join');
    // System automations are visible under System/Admin when the step lists System (or only system-like roles).
    if (isSystemStep && (roleKey === 'system' || roleKey === 'admin')) {
      return roles.some(r => ['system', 'admin'].includes(r.toLowerCase())) || roles.length === 0;
    }
    return false;
  };

  const isEventAuthorized = (eventId: string, step: any, role: string): boolean => {
    const roleKey = (role || '').trim().toLowerCase();
    if (!roleKey) return false;
    if (roleKey === 'admin') return true;

    const eventCaps = getEventRequiredCapabilities(eventId);
    const stepCaps = getRequiredCapabilities(step);
    const required = eventCaps.length > 0 ? eventCaps : stepCaps;
    if (required.length > 0) {
      return capabilitiesAllow(role, required);
    }

    const eventRoles = getEventAllowedRoles(eventId);
    if (eventRoles.length > 0) {
      if (eventRoles.some(r => ['anyone', 'unassigned'].includes(r.toLowerCase()))) return true;
      if (eventRoles.map(r => r.toLowerCase()).includes(roleKey)) return true;
      if ((roleKey === 'system' || roleKey === 'admin') && eventRoles.some(r => ['system', 'admin'].includes(r.toLowerCase()))) return true;
      return false;
    }
    // Fall back to step-level roles when the pack has not declared capabilities yet
    return roleCanActOnStep(step, role);
  };

  const roleEventCatalog = useMemo(() => {
    return rawSteps
      .map((step: any) => {
        const stepId = String(getProp(step, 'stepId', 'StepId') || '');
        const roles = getStepRoles(step);
        const routes = getNextStepRoutes(step);
        const conditions = getProp(step, 'conditions', 'Conditions') || {};
        const conditionTargets = Object.entries(conditions).map(([expression, target]) => ({
          outcome: String(expression),
          target: String(target)
        }));
        const events = routes.length > 0 ? routes : conditionTargets;
        return {
          stepId,
          roles,
          events,
          canAct: roleCanActOnStep(step, simulatedRole)
        };
      })
      .filter(item => item.canAct && item.events.length > 0 && item.stepId);
  }, [rawSteps, simulatedRole]);

  // Evaluates a condition expression string against the payload dictionary
  const evaluateExpression = (expression: string, currentPayload: Record<string, any>): { result: boolean; error?: string } => {
    if (!expression || expression.trim() === '') return { result: true };
    const trimmed = expression.trim();
    if (trimmed.toLowerCase() === 'default') return { result: true };
    if (trimmed.toLowerCase() === 'true') return { result: true };
    if (trimmed.toLowerCase() === 'false') return { result: false };

    try {
      let expr = trimmed;
      // Replace "Payload.Key" or "payload.Key" with "Key"
      expr = expr.replace(/\b[pP]ayload\./g, '');
      // Normalize single '=' to '==' safely (excluding !=, <=, >=, ==)
      expr = expr.replace(/([^!<>=])=([^=])/g, '$1==$2');

      const keys = Object.keys(currentPayload);
      const values = Object.values(currentPayload);

      const fn = new Function(...keys, `try { return Boolean(${expr}); } catch (e) { return false; }`);
      const res = fn(...values);
      return { result: Boolean(res) };
    } catch (err: any) {
      return { result: false, error: err.message };
    }
  };

  const getStepActions = (step: any, hook: 'onEntry' | 'onExit'): any[] => {
    if (!step) return [];
    const raw = getProp(step, hook, hook === 'onEntry' ? 'OnEntry' : 'OnExit');
    if (Array.isArray(raw)) return raw;
    return [];
  };

  // Interpolates {{Expression}} placeholders with payload variables or expressions
  const interpolateTemplate = (template: string, currentPayload: Record<string, any>): string => {
    if (!template || !template.includes('{{')) return template || '';
    return template.replace(/\{\{\s*(.+?)\s*\}\}/g, (_, expr) => {
      const trimmed = expr.trim();
      const sanitized = trimmed.replace(/\b[pP]ayload\./g, '');
      if (currentPayload[sanitized] !== undefined) {
        return String(currentPayload[sanitized]);
      }
      try {
        const keys = Object.keys(currentPayload);
        const values = Object.values(currentPayload);
        const fn = new Function(...keys, `try { return ${sanitized}; } catch(e) { return ""; }`);
        const res = fn(...values);
        return res !== undefined && res !== null ? String(res) : '';
      } catch {
        return '';
      }
    });
  };

  // Transforms payload mapping expressions into evaluated key-values
  const transformPayload = (mapping: Record<string, string>, currentPayload: Record<string, any>): Record<string, any> => {
    if (!mapping || Object.keys(mapping).length === 0) return {};
    const result: Record<string, any> = {};
    for (const [key, expr] of Object.entries(mapping)) {
      const trimmed = (expr || '').trim();
      const sanitized = trimmed.replace(/\b[pP]ayload\./g, '');
      if (currentPayload[sanitized] !== undefined) {
        result[key] = currentPayload[sanitized];
        continue;
      }
      try {
        const keys = Object.keys(currentPayload);
        const values = Object.values(currentPayload);
        const fn = new Function(...keys, `try { return ${sanitized}; } catch(e) { return ${JSON.stringify(trimmed)}; }`);
        const res = fn(...values);
        result[key] = res !== undefined ? res : trimmed;
      } catch {
        result[key] = trimmed;
      }
    }
    return result;
  };

  const evaluateStepHooks = (actions: any[], hookType: 'OnEntry' | 'OnExit', stepId: string, currentPayload: Record<string, any>): string[] => {
    const logs: string[] = [];
    actions.forEach((act: any) => {
      const type = getProp(act, 'actionType', 'ActionType') || 'Action';
      const rawTarget = getProp(act, 'target', 'Target') || '';
      const target = interpolateTemplate(rawTarget, currentPayload);
      const cond = getProp(act, 'condition', 'Condition');
      const template = getProp(act, 'template', 'Template');
      const mapping = getProp(act, 'payloadMapping', 'PayloadMapping');

      let actionDetails = '';
      if (template) {
        const interpolated = interpolateTemplate(template, currentPayload);
        if (interpolated) actionDetails += ` | Msg: "${interpolated}"`;
      }
      if (mapping && Object.keys(mapping).length > 0) {
        const transformed = transformPayload(mapping, currentPayload);
        const preview = Object.entries(transformed).slice(0, 3).map(([k, v]) => `${k}=${v}`).join(', ');
        actionDetails += ` | Payload: {${preview}}`;
      }

      if (cond && cond.trim() !== '') {
        const evalRes = evaluateExpression(cond, currentPayload);
        if (evalRes.result) {
          logs.push(`[${stepId} | Hook: ${hookType}] ${type} -> "${target}" (Condition "${cond}" matched)${actionDetails}`);
        } else {
          logs.push(`[${stepId} | Hook: ${hookType} SKIPPED] ${type} -> "${target}" (Condition "${cond}" evaluated to FALSE)`);
        }
      } else {
        logs.push(`[${stepId} | Hook: ${hookType}] ${type} -> "${target}"${actionDetails}`);
      }
    });
    return logs;
  };

  // Initialize
  useEffect(() => {
    resetSimulation();
  }, [definition]);

  // Auto-select required role when entering a new step
  useEffect(() => {
    if (currentStepRoles.length > 0) {
      const lowerRoles = currentStepRoles.map(r => r.toLowerCase());
      if (
        !lowerRoles.includes(simulatedRole.toLowerCase()) && 
        !lowerRoles.includes('anyone') && 
        !lowerRoles.includes('unassigned')
      ) {
        setSimulatedRole(currentStepRoles[0]);
      }
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [currentStepId]);

  const resetSimulation = () => {
    setCurrentStepId(startStepId);
    setCurrentState(initialState);
    const startStep = rawSteps.find((s: any) => 
      (getProp(s, 'stepId', 'StepId') || '').toLowerCase() === (startStepId || '').toLowerCase()
    );
    const initialLogs: string[] = [];
    appendBusinessContext(initialLogs, payload, { maxFields: 8 });
    if (startStep) {
      const entryActions = getStepActions(startStep, 'onEntry');
      if (entryActions.length > 0) {
        initialLogs.push(...evaluateStepHooks(entryActions, 'OnEntry', startStepId, payload));
      }
    }
    setHistory(initialLogs);
  };

  const currentStep = rawSteps.find((s: any) => 
    (getProp(s, 'stepId', 'StepId') || '').toLowerCase() === (currentStepId || '').toLowerCase()
  );

  const normalizedStepId = (currentStepId || '').trim().toLowerCase();
  const isTerminalStepId = normalizedStepId === 'end' || normalizedStepId === 'none';
  const hasSimulationProgress =
    history.length > 0 ||
    (Boolean(normalizedStepId) && normalizedStepId !== (startStepId || '').trim().toLowerCase()) ||
    (Boolean(currentState) && (currentState || '') !== (initialState || ''));
  const isSimulationComplete =
    hasSimulationProgress && (isTerminalStepId || !currentStep);

  const currentStepRoles = getStepRoles(currentStep);
  const stepType = (getProp(currentStep, 'stepType', 'StepType') || 'Command').toString();
  const stepTypeLower = stepType.toLowerCase();
  const isHumanTask = stepTypeLower.includes('human');
  const isDecisionStep = stepTypeLower.includes('decision') || stepTypeLower.includes('choice');
  const activeRoleDisplay = currentStepRoles.length > 0 ? currentStepRoles.join(', ') : (isHumanTask ? 'Unassigned' : 'System');
  const currentOnEntry = getStepActions(currentStep, 'onEntry');
  const currentOnExit = getStepActions(currentStep, 'onExit');

  // Payload text editing handler
  const handlePayloadChange = (text: string) => {
    setPayloadText(text);
    try {
      const parsed = JSON.parse(text);
      if (typeof parsed === 'object' && parsed !== null && !Array.isArray(parsed)) {
        setPayload(parsed);
        setPayloadError(null);
      } else {
        setPayloadError('Payload must be a valid JSON object.');
      }
    } catch (err: any) {
      setPayloadError(err.message);
    }
  };

  const loadPresetPayload = (key: string) => {
    const preset = PRESET_PAYLOADS[key];
    if (preset) {
      setPayload(preset.data);
      setPayloadText(JSON.stringify(preset.data, null, 2));
      setPayloadError(null);
    }
  };

  // Evaluate conditions on the current step
  const rawConditions: Record<string, string> = getProp(currentStep, 'conditions', 'Conditions') || {};
  const evaluatedConditions = useMemo(() => {
    if (!currentStep) return { evaluated: [], winningTarget: null, winningExpr: null };

    let winnerTarget: string | null = null;
    let winnerExpr: string | null = null;

    const items = Object.entries(rawConditions).map(([expression, target]) => {
      const isDefault = expression.trim().toLowerCase() === 'default';
      const evalRes = evaluateExpression(expression, payload);
      const matched = evalRes.result;

      if (!winnerTarget && !isDefault && matched) {
        winnerTarget = target;
        winnerExpr = expression;
      }

      return {
        expression,
        target,
        matched,
        isDefault,
        error: evalRes.error
      };
    });

    // If no explicit condition matched, look for default
    if (!winnerTarget) {
      const defaultEntry = items.find(i => i.isDefault);
      if (defaultEntry) {
        winnerTarget = defaultEntry.target;
        winnerExpr = 'Default';
      }
    }

    return { evaluated: items, winningTarget: winnerTarget, winningExpr: winnerExpr };
  }, [currentStep, rawConditions, payload]);

  // Execute a decision route
  const handleDecisionAdvance = (targetStepId: string, winningExpr: string) => {
    const logMsg = `[Decision: ${currentStepId}] Condition "${winningExpr}" -> TRUE => Advanced to [${targetStepId}]`;
    const newLogs: string[] = [];

    // OnExit hooks for departed step
    if (currentStep) {
      const exitActions = getStepActions(currentStep, 'onExit');
      if (exitActions.length > 0) {
        newLogs.push(...evaluateStepHooks(exitActions, 'OnExit', currentStepId, payload));
      }
    }

    newLogs.push(logMsg);
    appendBusinessContext(newLogs, payload, { maxFields: 8 });

    // OnEntry hooks for entered step
    const targetStep = rawSteps.find((s: any) => 
      (getProp(s, 'stepId', 'StepId') || '').toLowerCase() === (targetStepId || '').toLowerCase()
    );
    if (targetStep) {
      const entryActions = getStepActions(targetStep, 'onEntry');
      if (entryActions.length > 0) {
        newLogs.push(...evaluateStepHooks(entryActions, 'OnEntry', targetStepId, payload));
      }
    }

    setHistory(prev => [...prev, ...newLogs]);
    setCurrentStepId(targetStepId);
  };

  const fireEvent = (eventId: string, targetStepId: string) => {
    // 1. Check State Machine Transition (case-insensitive; Default/true auto-routes do not consume Law)
    let nextState = currentState;
    let guardBlocked = false;
    let guardReason = '';
    let matchedTransition = false;

    const eventKey = (eventId || '').trim().toLowerCase();
    const isNonConsumingAutoRoute = eventKey === 'default' || eventKey === 'true' || eventKey === '';

    const matching = transitions.filter(t => {
      const from = String(getProp(t, 'fromState', 'FromState') ?? '');
      const evt = String(getProp(t, 'eventId', 'EventId', 'eventName', 'EventName') ?? '');
      const fromOk =
        from === '*' ||
        from.trim().toLowerCase() === String(currentState || '').trim().toLowerCase();
      const evtOk = evt.trim().toLowerCase() === eventKey;
      return fromOk && evtOk;
    });

    if (matching.length > 0) {
      matchedTransition = true;
      const transition = matching[0];
      const targetState = getProp(transition, 'toState', 'ToState');
      
      // Check transition guard condition against payload if configured
      const constraint = getProp(transition, 'condition', 'Condition', 'constraint', 'Constraint');
      if (constraint && typeof constraint === 'string') {
        const evalRes = evaluateExpression(constraint, payload);
        if (!evalRes.result) {
          guardBlocked = true;
          guardReason = `State Machine Guard Failed: Expression "${constraint}" evaluated to FALSE against current payload.`;
        }
      }

      if (!guardBlocked && targetState) {
        nextState = targetState;
      }
    }

    if (guardBlocked) {
      const blockedLogs = [`[GUARD BLOCKED] ${guardReason} (State: ${currentState})`];
      appendBusinessContext(blockedLogs, payload, { eventId, maxFields: 8 });
      setHistory(prev => [...prev, ...blockedLogs]);
      alert(`⚠️ State Machine Guard Violation:\n\n${guardReason}\n\nState remains [${currentState}]. Update payload in "Context & Payload" tab to satisfy the guard.`);
      return;
    }

    // 2. Advance
    const actingRole = simulatedRole || activeRoleDisplay;
    const newLogs: string[] = [];
    const eventLabel = resolveEventLabel(eventId);
    const eventDisplay = eventLabel && eventLabel !== eventId ? `${eventId} (${eventLabel})` : eventId;

    // OnExit hooks for departed step
    if (currentStep) {
      const exitActions = getStepActions(currentStep, 'onExit');
      if (exitActions.length > 0) {
        newLogs.push(...evaluateStepHooks(exitActions, 'OnExit', currentStepId, payload));
      }
    }

    if (!isNonConsumingAutoRoute && !matchedTransition) {
      newLogs.push(
        `[SM WARN] No state-machine transition for event "${eventId}" from state "${currentState || 'None'}". Workflow step advances; Law state unchanged.`
      );
    }

    newLogs.push(
      `[Role: ${actingRole}] Fired "${eventDisplay}" -> Step: ${targetStepId} (State: ${currentState || 'None'} → ${nextState || 'None'})`
    );
    appendBusinessContext(newLogs, payload, { eventId, maxFields: 8 });

    // OnEntry hooks for entered step
    const targetStep = rawSteps.find((s: any) => 
      (getProp(s, 'stepId', 'StepId') || '').toLowerCase() === (targetStepId || '').toLowerCase()
    );
    if (targetStep) {
      const entryActions = getStepActions(targetStep, 'onEntry');
      if (entryActions.length > 0) {
        newLogs.push(...evaluateStepHooks(entryActions, 'OnEntry', targetStepId, payload));
      }
    }

    setHistory(prev => [...prev, ...newLogs]);
    setCurrentStepId(targetStepId);
    setCurrentState(nextState);
  };

  // Check role authorization for human tasks
  const isRoleAuthorized = useMemo(() => {
    return roleCanActOnStep(currentStep, simulatedRole);
  }, [currentStep, simulatedRole]);

  const guardUnauthorizedAction = (actionLabel: string, eventId?: string): boolean => {
    if (!currentStep) return false;
    const isAuthorized = eventId 
      ? isEventAuthorized(eventId, currentStep, simulatedRole) 
      : isRoleAuthorized;
    if (!isAuthorized) {
      const eventRoles = eventId ? getEventAllowedRoles(eventId) : [];
      const reqRolesDisplay = eventRoles.length > 0 ? eventRoles.join(', ') : activeRoleDisplay;
      alert(
        `Your current role "${simulatedRole}" cannot perform "${actionLabel}" at step [${currentStepId}].\n\nRequired role: ${reqRolesDisplay}.\n\nSwitch your role in the right panel to match the required role.`
      );
      return true;
    }
    return false;
  };

  const fireEventGuarded = (eventId: string, targetStepId: string) => {
    if (guardUnauthorizedAction(eventId, eventId)) return;
    fireEvent(eventId, targetStepId);
  };

  const handleDecisionAdvanceGuarded = (targetStepId: string, winningExpr: string) => {
    if (guardUnauthorizedAction(winningExpr || 'Decision')) return;
    handleDecisionAdvance(targetStepId, winningExpr);
  };

  // Render controls based on active step
  const renderControls = () => {
    if (isSimulationComplete || !currentStep) return (
      <div className="text-slate-500 text-xs py-6 text-center space-y-3">
        <div className="w-8 h-8 rounded-full bg-emerald-500/20 text-emerald-300 border border-emerald-500/40 flex items-center justify-center mx-auto">
          <Check size={16} />
        </div>
        <div>
          <p className="font-semibold text-emerald-300">Simulation Complete</p>
          <p className="text-[11px] text-slate-500 mt-1">Workflow reached end or terminal step.</p>
        </div>
        <button
          type="button"
          onClick={resetSimulation}
          className="mx-auto px-4 py-2 bg-emerald-600 hover:bg-emerald-500 text-white text-xs font-bold rounded-lg shadow-lg shadow-emerald-500/25 transition-all flex items-center gap-1.5"
        >
          <RotateCcw size={14} /> Restart Simulation
        </button>
      </div>
    );

    const nextStepRoutes = getNextStepRoutes(currentStep);
    const nextSteps = Object.fromEntries(nextStepRoutes.map(route => [route.outcome, route.target]));
    const sla = getProp(currentStep, 'sla', 'Sla', 'SLA');
    const hasConditions = Object.keys(rawConditions).length > 0;
    const autoRoute = pickAutoStepRoute(nextStepRoutes);
    const alternateRoutes = nextStepRoutes.filter(
      route => route.outcome.trim().toLowerCase() !== autoRoute.outcome.trim().toLowerCase()
    );
    const canActAsSelectedRole = roleCanActOnStep(currentStep, simulatedRole);

    if (eventViewMode === 'asRole' && !canActAsSelectedRole) {
      const ownedElsewhere = roleEventCatalog.filter(item => item.stepId !== currentStepId);
      return (
        <div className="space-y-3">
          <div className="bg-indigo-500/10 border border-indigo-500/30 p-3.5 rounded-xl space-y-2">
            <div className="flex items-center justify-between gap-2">
              <p className="text-xs text-indigo-200 font-bold flex items-center gap-1.5">
                <UserCheck size={14} /> Filtered for role: {simulatedRole}
              </p>
              <span className="text-[10px] px-2 py-0.5 rounded-full bg-slate-900 border border-slate-700 text-slate-400">
                Active role here: {activeRoleDisplay}
              </span>
            </div>
            <p className="text-[11px] text-slate-400">
              No events at step <strong className="text-amber-300 font-mono">{currentStepId}</strong> for this role.
              Switch to <strong className="text-slate-200">Current step</strong> mode to see this step&apos;s events, or pick a role that matches the active role.
            </p>
            <button
              type="button"
              onClick={() => setEventViewMode('current')}
              className="text-[11px] font-semibold text-indigo-300 hover:text-white underline"
            >
              Show current step events
            </button>
          </div>
          {ownedElsewhere.length > 0 && (
            <div className="rounded-xl border border-slate-800 bg-slate-950/70 p-3 space-y-2">
              <p className="text-[10px] uppercase tracking-wider text-slate-500 font-bold">
                Events {simulatedRole} can fire elsewhere in this workflow
              </p>
              <div className="space-y-1.5 max-h-40 overflow-y-auto">
                {ownedElsewhere.map(item => (
                  <div key={item.stepId} className="text-[10px] font-mono text-slate-400 flex flex-wrap gap-x-2 gap-y-1">
                    <span className="text-indigo-300 font-bold">{item.stepId}</span>
                    <span className="text-slate-600">·</span>
                    {item.events.slice(0, 4).map(ev => (
                      <span key={`${item.stepId}-${ev.outcome}`} className="text-amber-300/90">{ev.outcome}</span>
                    ))}
                    {item.events.length > 4 && <span className="text-slate-600">+{item.events.length - 4}</span>}
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>
      );
    }

    return (
      <div className="space-y-4">
        {eventViewMode === 'asRole' && (
          <div className="text-[10px] text-indigo-300 bg-indigo-500/10 border border-indigo-500/25 rounded-lg px-2.5 py-1.5 flex items-center justify-between gap-2">
            <span>
              Showing only actions <strong>{simulatedRole}</strong> may run at this step.
            </span>
            <span className="text-slate-500">Active role: {activeRoleDisplay}</span>
          </div>
        )}
        {/* Role Authorization Status Banner */}
        {!isRoleAuthorized && (
          <div className="p-2.5 bg-rose-500/10 border border-rose-500/30 rounded-xl flex items-center justify-between gap-2">
            <div className="flex items-center gap-2 text-[11px] text-rose-300">
              <span className="w-5 h-5 rounded-full bg-rose-500/20 border border-rose-500/40 flex items-center justify-center text-[10px] font-bold shrink-0">✕</span>
              <span>Events <strong>disabled</strong> — your role <strong className="text-white">{simulatedRole}</strong> does not match required role <strong className="text-amber-300">{activeRoleDisplay}</strong></span>
            </div>
            <button 
              onClick={() => {
                const firstRole = currentStepRoles.find(r => !['unassigned', 'anyone'].includes(r.toLowerCase()));
                if (firstRole) setSimulatedRole(firstRole);
              }}
              className="shrink-0 px-2.5 py-1 bg-rose-500/20 hover:bg-rose-500/30 border border-rose-500/40 text-rose-200 text-[10px] font-bold rounded-lg transition-colors"
            >
              Switch to {currentStepRoles.find(r => !['unassigned', 'anyone'].includes(r.toLowerCase())) || 'required role'}
            </button>
          </div>
        )}
        {isRoleAuthorized && (
          <div className="p-2 bg-emerald-500/10 border border-emerald-500/30 rounded-xl flex items-center gap-2 text-[11px] text-emerald-300">
            <span className="w-5 h-5 rounded-full bg-emerald-500/20 border border-emerald-500/40 flex items-center justify-center text-[10px] font-bold shrink-0">✓</span>
            <span>Role <strong className="text-white">{simulatedRole}</strong> authorized — events <strong>enabled</strong></span>
          </div>
        )}
        {/* CASE 1: Decision Step / Step with Evaluated Conditions */}
        {(isDecisionStep || hasConditions) ? (
          <div className="bg-purple-500/10 border border-purple-500/30 p-3.5 rounded-xl space-y-3">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-1.5">
                <Sparkles size={15} className="text-purple-400" />
                <p className="text-xs text-purple-300 font-bold">Decision Step / Dynamic Conditions</p>
              </div>
              <span className="text-[10px] bg-purple-500/20 text-purple-300 px-2 py-0.5 rounded border border-purple-500/30 font-mono">
                {evaluatedConditions.evaluated.length} Rules
              </span>
            </div>

            <p className="text-[11px] text-slate-300">
              Evaluates condition expressions against the active context payload:
            </p>

            {/* Condition Rules List with Live Evaluation Results */}
            <div className="space-y-1.5">
              {evaluatedConditions.evaluated.map((c, idx) => {
                const isWinner = evaluatedConditions.winningTarget === c.target && (evaluatedConditions.winningExpr === c.expression);
                return (
                  <div 
                    key={idx} 
                    className={`p-2 rounded-lg border text-xs flex items-center justify-between transition-all ${
                      isWinner 
                        ? 'bg-purple-950/80 border-emerald-500/80 text-white shadow-md ring-1 ring-emerald-500/40' 
                        : 'bg-slate-950/70 border-slate-800 text-slate-400 opacity-70'
                    }`}
                  >
                    <div className="flex items-center gap-2">
                      <span className={`w-4 h-4 rounded-full flex items-center justify-center text-[10px] font-bold ${
                        c.matched ? 'bg-emerald-500/20 text-emerald-400' : 'bg-slate-800 text-slate-500'
                      }`}>
                        {c.matched ? '✓' : '×'}
                      </span>
                      <div>
                        <span className="font-mono text-[11px] font-semibold text-slate-200">{c.expression}</span>
                        <span className="text-[10px] text-slate-500 block">Target ➔ <strong className="text-blue-300">{c.target}</strong></span>
                      </div>
                    </div>
                    {isWinner && (
                      <span className="px-1.5 py-0.5 rounded bg-emerald-500/20 text-emerald-300 border border-emerald-500/40 text-[9px] font-bold uppercase tracking-wider">
                        Matches Payload
                      </span>
                    )}
                  </div>
                );
              })}
            </div>

            {evaluatedConditions.winningTarget ? (
              <button 
                onClick={() => handleDecisionAdvanceGuarded(evaluatedConditions.winningTarget!, evaluatedConditions.winningExpr!)}
                disabled={!isRoleAuthorized}
                className="w-full py-2 bg-gradient-to-r from-purple-600 to-indigo-600 hover:from-purple-500 hover:to-indigo-500 text-white text-xs font-bold rounded-lg shadow-lg shadow-purple-500/20 transition-all flex items-center justify-center gap-1.5 disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:from-purple-600 disabled:hover:to-indigo-600"
              >
                <span>Take Branch: <strong>{evaluatedConditions.winningTarget}</strong></span>
                <ArrowRight size={14} />
              </button>
            ) : (
              <div className="p-2 bg-rose-500/10 border border-rose-500/30 rounded text-rose-300 text-[11px] text-center">
                No conditions evaluated to true and no Default branch specified.
              </div>
            )}
          </div>
        ) : stepTypeLower.includes('command') || stepTypeLower.includes('event') ? (
          /* CASE 2: Command / Automated Action Step */
          <div className="bg-blue-500/10 border border-blue-500/30 p-3.5 rounded-xl space-y-2.5">
            <div className="flex items-center justify-between gap-2">
              <div>
                <p className="text-xs text-blue-300 font-semibold mb-0.5 flex items-center gap-1.5">
                  <Cpu size={14} className="text-blue-400" /> System Action ({stepType || 'Command'})
                </p>
                <div className="flex items-center gap-1.5 text-[11px]">
                  <span className="text-slate-400">Executing Role:</span>
                  <span className="text-blue-300 font-mono font-bold">{activeRoleDisplay}</span>
                </div>
              </div>
              <button 
                onClick={() => fireEventGuarded(autoRoute.outcome, autoRoute.target)}
                disabled={!isRoleAuthorized}
                className="px-3.5 py-1.5 bg-blue-600 hover:bg-blue-500 text-white text-xs font-bold rounded-lg shadow-md transition-all flex items-center gap-1.5 shrink-0 disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-blue-600"
                title={`Fire "${autoRoute.outcome}" → ${autoRoute.target}`}
              >
                Auto-Step <span className="font-mono opacity-90">{autoRoute.outcome}</span> <ArrowRight size={14} />
              </button>
            </div>
            <p className="text-[10px] text-slate-500 italic">
              Fires the step outcome as the dual-kernel event (not a hard-coded Default), so Law state advances when a matching transition exists.
            </p>
            {alternateRoutes.length > 0 && (
              <div className="pt-1 border-t border-blue-500/20 space-y-1.5">
                <p className="text-[10px] text-slate-400 font-semibold">Alternate outcomes:</p>
                <div className="flex flex-wrap gap-2">
                  {alternateRoutes.map(route => (
                    <button
                      key={`${route.outcome}:${route.target}`}
                      onClick={() => fireEventGuarded(route.outcome, route.target)}
                      disabled={!isRoleAuthorized}
                      className="px-3 py-1.5 bg-slate-800 hover:bg-slate-700 border border-slate-600 text-white text-xs font-semibold rounded-lg transition-all flex items-center gap-1 disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-slate-800"
                    >
                      Fire: <span className="text-amber-300 font-mono font-bold">{route.outcome}</span>
                      <span className="text-slate-500">→ {route.target}</span>
                    </button>
                  ))}
                </div>
              </div>
            )}
          </div>
        ) : stepTypeLower.includes('timer') ? (
          /* CASE 3: Timer Step (Relative Pre/Post-Event or Static Duration) */
          <div className="bg-cyan-500/10 border border-cyan-500/30 p-3.5 rounded-xl space-y-3">
            <div className="flex items-center justify-between mb-1">
              <p className="text-xs text-cyan-300 font-semibold flex items-center gap-1.5">
                <Clock size={14} className="text-cyan-400" /> Timer Delay / Pre-Event Countdown
              </p>
              <span className="px-2 py-0.5 rounded text-[10px] font-bold bg-cyan-500/20 text-cyan-200 border border-cyan-500/40">
                System Timer
              </span>
            </div>

            {/* Relative Timer vs Static Details */}
            {(() => {
              const targetProp = getProp(rawConditions, 'targetTimestampProperty', 'targetTimestamp', 'referenceDate', 'targetDate', 'eventDate', 'property');
              const offsetStr = getProp(rawConditions, 'leadTime', 'offset', 'delay');
              const staticDur = getProp(rawConditions, 'duration') || getProp(sla, 'duration', 'Duration');
              const rawTargetVal = targetProp ? payload[targetProp] : undefined;

              let calculatedDue: string | null = null;
              if (rawTargetVal) {
                try {
                  const baseDate = new Date(rawTargetVal);
                  if (!isNaN(baseDate.getTime()) && offsetStr) {
                    const trimmed = offsetStr.trim();
                    const isNeg = trimmed.startsWith('-');
                    const numPart = parseFloat(trimmed.replace(/[+\-smhd]/gi, ''));
                    const unit = trimmed.slice(-1).toLowerCase();
                    let ms = 0;
                    if (unit === 's') ms = numPart * 1000;
                    else if (unit === 'm') ms = numPart * 60 * 1000;
                    else if (unit === 'h') ms = numPart * 3600 * 1000;
                    else if (unit === 'd') ms = numPart * 86400 * 1000;
                    const dueTime = new Date(baseDate.getTime() + (isNeg ? -ms : ms));
                    calculatedDue = dueTime.toISOString();
                  }
                } catch {}
              }

              return (
                <div className="bg-slate-950/70 border border-slate-800 rounded-lg p-2.5 space-y-1.5 text-xs">
                  {targetProp ? (
                    <>
                      <div className="flex items-center justify-between text-[11px]">
                        <span className="text-slate-400">Target Property:</span>
                        <span className="text-cyan-300 font-mono font-semibold">{targetProp}</span>
                      </div>
                      <div className="flex items-center justify-between text-[11px]">
                        <span className="text-slate-400">Payload Value:</span>
                        <span className="text-slate-200 font-mono">{rawTargetVal ? String(rawTargetVal) : <span className="text-rose-400 italic">Not found in payload</span>}</span>
                      </div>
                      {offsetStr && (
                        <div className="flex items-center justify-between text-[11px]">
                          <span className="text-slate-400">Offset / Lead Time:</span>
                          <span className="text-amber-300 font-mono font-bold">{offsetStr}</span>
                        </div>
                      )}
                      {calculatedDue && (
                        <div className="flex items-center justify-between text-[11px] pt-1 border-t border-slate-800/80">
                          <span className="text-emerald-400 font-semibold">Scheduled Due:</span>
                          <span className="text-emerald-300 font-mono text-[10px]">{calculatedDue}</span>
                        </div>
                      )}
                    </>
                  ) : (
                    <div className="flex items-center justify-between text-[11px]">
                      <span className="text-slate-400">Duration:</span>
                      <span className="text-cyan-300 font-mono font-semibold">{staticDur || 'Configured'}</span>
                    </div>
                  )}
                </div>
              );
            })()}

            <p className="text-[11px] text-slate-300">
              Advance simulation by triggering timer elapsed event:
            </p>

            <div className="flex flex-wrap gap-2 pt-1">
              {Object.entries(nextSteps).map(([outcome, target]) => (
                <button
                  key={outcome}
                  onClick={() => fireEventGuarded(outcome, target as string)}
                  disabled={!isRoleAuthorized}
                  className="px-3.5 py-1.5 bg-cyan-600 hover:bg-cyan-500 text-white text-xs font-bold rounded-lg shadow-md shadow-cyan-600/20 transition-all flex items-center gap-1.5 disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-cyan-600"
                >
                  <span>Elapse Timer: <strong className="font-mono">{outcome}</strong></span>
                  <ArrowRight size={14} />
                </button>
              ))}
            </div>
          </div>
        ) : (
          /* CASE 4: Human Task Step */
          <div className="bg-amber-500/10 border border-amber-500/30 p-3.5 rounded-xl space-y-3">
            <div>
              <div className="flex items-center justify-between mb-1">
                <p className="text-xs text-amber-300 font-semibold flex items-center gap-1.5">
                  <UserCheck size={14} className="text-amber-400" /> Human Task Sign-off
                </p>
                <span className="px-2 py-0.5 rounded text-[10px] font-bold bg-amber-500/20 text-amber-200 border border-amber-500/40">
                  Active role: {activeRoleDisplay}
                </span>
              </div>

              {!isRoleAuthorized && (
                <div className="mb-2 p-2 bg-rose-500/15 border border-rose-500/30 rounded-lg text-rose-300 text-[11px] flex items-center justify-between gap-2">
                  <span>Your role <strong>{simulatedRole}</strong> cannot handle this step. Required: <strong>{activeRoleDisplay}</strong></span>
                  <button 
                    onClick={() => setSimulatedRole(currentStepRoles[0] || 'Manager')} 
                    className="underline text-[10px] font-bold text-rose-200 hover:text-white shrink-0"
                  >
                    Switch Role
                  </button>
                </div>
              )}

              <p className="text-[11px] text-slate-300">
                Select an outcome below. Each event displays its required role and enables/disables based on your selected role:
              </p>
            </div>
            <div className="flex flex-col gap-2 pt-1">
              {Object.entries(nextSteps).map(([outcome, target]) => {
                const eventAuth = isEventAuthorized(outcome, currentStep, simulatedRole);
                const eventReqRoles = getEventAllowedRoles(outcome);
                const reqRolesDisplay = eventReqRoles.length > 0 ? eventReqRoles.join(', ') : activeRoleDisplay;

                return (
                  <div key={outcome} className="flex flex-wrap items-center gap-2">
                    <button 
                      onClick={() => fireEventGuarded(outcome, target as string)}
                      disabled={!eventAuth}
                      className={`px-3 py-1.5 border text-xs font-semibold rounded-lg transition-all flex items-center gap-1.5 shadow-sm ${
                        eventAuth
                          ? 'bg-slate-800 hover:bg-slate-700 border-amber-500/50 text-white'
                          : 'bg-slate-900 border-slate-800 text-slate-500 opacity-40 cursor-not-allowed'
                      }`}
                    >
                      Fire: <span className="text-amber-400 font-mono font-bold">{outcome}</span>
                    </button>
                    <span className={`text-[10px] px-2 py-0.5 rounded-full border font-mono ${
                      eventAuth
                        ? 'bg-emerald-500/10 border-emerald-500/30 text-emerald-300'
                        : 'bg-amber-500/10 border-amber-500/30 text-amber-300'
                    }`}>
                      Role: <strong>{reqRolesDisplay}</strong>
                    </span>
                    {!eventAuth && eventReqRoles.length > 0 && (
                      <button
                        onClick={() => setSimulatedRole(eventReqRoles[0])}
                        className="text-[10px] text-amber-300 hover:text-white underline font-semibold shrink-0"
                      >
                        Switch to {eventReqRoles[0]}
                      </button>
                    )}
                  </div>
                );
              })}
            </div>
          </div>
        )}

        {/* SLA Escalation & Multi-Tier Reminders */}
        {sla && (
           <div className="bg-rose-500/10 border border-rose-500/30 p-3 rounded-xl space-y-2.5">
             <div className="flex items-center justify-between">
               <div>
                 <p className="text-xs text-rose-300 font-semibold mb-0.5 flex items-center gap-1">
                   <Clock size={14}/> SLA Timeout: {getProp(sla, 'duration', 'Duration')}
                 </p>
                 <p className="text-[10px] text-slate-400">
                   Escalates to: {getProp(sla, 'escalationStepId', 'EscalationStepId') || 'END'}
                 </p>
               </div>
               <button 
                 onClick={() => {
                   const evt = getProp(sla, 'timeoutEvent', 'TimeoutEvent') || 'TIMEOUT';
                   const tgt = getProp(sla, 'escalationStepId', 'EscalationStepId') || 'END';
                   fireEventGuarded(evt, tgt);
                 }}
                 disabled={!isRoleAuthorized}
                 className="px-3 py-1.5 bg-rose-900/50 hover:bg-rose-800/80 text-rose-200 border border-rose-700 text-xs font-bold rounded shadow transition-colors flex items-center gap-1 shrink-0 disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-rose-900/50"
               >
                 Force Timeout
               </button>
             </div>

             {/* Intermediate SLA Reminders */}
             {(() => {
               const rawReminders = getProp(sla, 'reminders', 'Reminders');
               if (!Array.isArray(rawReminders) || rawReminders.length === 0) return null;

               return (
                 <div className="pt-2 border-t border-rose-500/20 space-y-1.5">
                   <p className="text-[10px] text-amber-300 font-semibold flex items-center gap-1">
                     <Bell size={12} className="text-amber-400" /> Intermediate SLA Warnings & Reminders:
                   </p>
                   <div className="flex flex-wrap gap-1.5">
                     {rawReminders.map((rem: any, rIdx: number) => {
                       const dur = getProp(rem, 'duration', 'Duration') || '';
                       const evt = getProp(rem, 'triggerEvent', 'TriggerEvent') || '';
                       const target = nextSteps[evt] || currentStepId;

                       return (
                         <button
                           key={rIdx}
                           onClick={() => fireEventGuarded(evt, target as string)}
                           disabled={!isRoleAuthorized}
                           className="px-2.5 py-1 bg-amber-500/20 hover:bg-amber-500/30 border border-amber-500/40 text-amber-200 text-[10px] font-semibold rounded flex items-center gap-1 transition-colors disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-amber-500/20"
                           title={`Trigger reminder event '${evt}' (${dur})`}
                         >
                           <Bell size={10} />
                           <span>Fire Reminder: <strong>{evt}</strong> ({dur})</span>
                         </button>
                       );
                     })}
                   </div>
                 </div>
               );
             })()}
           </div>
        )}
      </div>
    );
  };

  return (
    <div className="bg-slate-900 border border-slate-800 rounded-xl overflow-hidden flex flex-col min-h-[600px] h-full">
      {/* Top Header */}
      <div className="p-3 bg-slate-950 border-b border-slate-800 flex justify-between items-center">
        <div className="flex items-center gap-3">
          <h3 className="text-sm font-bold text-white flex items-center gap-2">
            <Play size={16} className="text-emerald-400" /> Template Draft Simulator
          </h3>
          {currentStep && (
            <div className="hidden sm:flex items-center gap-2 text-[11px]">
              <span className={`px-2 py-0.5 rounded-full border flex items-center gap-1 font-semibold ${
                isRoleAuthorized 
                  ? 'bg-emerald-500/15 border-emerald-500/30 text-emerald-300' 
                  : 'bg-rose-500/15 border-rose-500/30 text-rose-300'
              }`}>
                {isRoleAuthorized ? '✓' : '✕'} {simulatedRole} → {activeRoleDisplay}
              </span>
            </div>
          )}
        </div>
        <button
          type="button"
          onClick={resetSimulation}
          disabled={!hasSimulationProgress && !isSimulationComplete}
          className={`text-xs flex items-center gap-1 px-2.5 py-1 rounded transition-all border ${
            isSimulationComplete
              ? 'bg-emerald-600 hover:bg-emerald-500 text-white border-emerald-400/40 font-bold shadow-md shadow-emerald-500/30'
              : hasSimulationProgress
              ? 'bg-slate-800 hover:bg-slate-700 text-slate-200 border-slate-700'
              : 'bg-slate-900 text-slate-600 border-slate-800 cursor-not-allowed opacity-50'
          }`}
          title={
            isSimulationComplete
              ? 'Simulation finished — restart from the beginning'
              : hasSimulationProgress
              ? 'Reset simulation to the start step'
              : 'Restart unlocks after you begin or complete a simulation'
          }
        >
          <RotateCcw size={14} /> Restart Simulation
        </button>
      </div>

      <div className="flex flex-1 overflow-hidden">
        {/* Left: Graph */}
        <div className="flex-1 bg-slate-950/50 overflow-auto border-r border-slate-800 p-2 relative h-full">
           <WorkflowGraphVisualizer 
              definition={definition} 
              currentStepId={currentStepId} 
              currentState={currentState} 
              initialView="both" 
           />
        </div>

        {/* Right: Controls, Context & Logs */}
        <div className="w-[380px] bg-slate-900 flex flex-col h-full overflow-hidden">
          
          {/* Side Panel Tab Selector */}
          <div className="flex bg-slate-950 border-b border-slate-800 p-1">
            <button
              onClick={() => setActiveSideTab('controls')}
              className={`flex-1 py-1.5 text-xs font-semibold rounded-lg flex items-center justify-center gap-1.5 transition-all ${
                activeSideTab === 'controls' ? 'bg-slate-800 text-white shadow-sm' : 'text-slate-400 hover:text-slate-200'
              }`}
            >
              <Sliders size={13} /> Actions & Execution
            </button>
            <button
              onClick={() => setActiveSideTab('context')}
              className={`flex-1 py-1.5 text-xs font-semibold rounded-lg flex items-center justify-center gap-1.5 transition-all ${
                activeSideTab === 'context' ? 'bg-slate-800 text-white shadow-sm' : 'text-slate-400 hover:text-slate-200'
              }`}
            >
              <Database size={13} /> Context & Payload ({Object.keys(payload).length})
            </button>
          </div>

          {activeSideTab === 'controls' ? (
            <>
              {/* Active Context Card */}
              <div className="p-4 border-b border-slate-800 shrink-0 bg-slate-950/40 space-y-2.5">
                <div className="flex items-center justify-between">
                  <h4 className="text-xs font-bold text-slate-300 uppercase tracking-wider">Active Context</h4>
                  <span className={`px-2 py-0.5 rounded-full text-[10px] font-bold border flex items-center gap-1 ${
                    isDecisionStep || Object.keys(rawConditions).length > 0
                      ? 'bg-purple-500/20 text-purple-300 border-purple-500/30'
                      : isHumanTask 
                      ? 'bg-amber-500/20 text-amber-300 border-amber-500/30' 
                      : 'bg-blue-500/20 text-blue-300 border-blue-500/30'
                  }`}>
                    {isDecisionStep || Object.keys(rawConditions).length > 0 ? <Sparkles size={11} /> : isHumanTask ? <UserCheck size={11} /> : <Cpu size={11} />}
                    {isDecisionStep || Object.keys(rawConditions).length > 0 ? 'Decision Step' : isHumanTask ? 'Human Task' : 'Automated Action'}
                  </span>
                </div>

                <div className="bg-slate-900/90 p-3 rounded-xl border border-slate-800 text-xs space-y-2">
                  <div className="flex items-center justify-between">
                    <span className="text-slate-400 text-[11px]">Current Step:</span>
                    <span className="font-mono text-amber-300 font-bold px-2 py-0.5 rounded bg-amber-500/15 border border-amber-500/30">
                      {currentStepId || 'END'}
                    </span>
                  </div>
                  <div className="flex items-center justify-between">
                    <span className="text-slate-400 text-[11px]">Required Role:</span>
                    <span className="font-mono text-amber-300 font-bold px-2 py-0.5 rounded bg-amber-500/15 border border-amber-500/30">
                      {activeRoleDisplay}
                    </span>
                  </div>
                  <div className="flex items-center justify-between">
                    <span className="text-slate-400 text-[11px]">Your Role:</span>
                    <div className="flex items-center gap-1.5">
                      <select 
                        value={simulatedRole} 
                        onChange={e => setSimulatedRole(e.target.value)}
                        className={`bg-slate-950 border font-bold text-[11px] rounded px-2 py-0.5 focus:outline-none ${
                          isRoleAuthorized 
                            ? 'border-emerald-500/40 text-emerald-300 focus:border-emerald-400'
                            : 'border-rose-500/40 text-rose-300 focus:border-rose-400'
                        }`}
                      >
                        {allKnownRoles.map(r => (
                          <option key={r} value={r}>{r}</option>
                        ))}
                      </select>
                      <span className={`w-5 h-5 rounded-full flex items-center justify-center text-[9px] font-bold border ${
                        isRoleAuthorized
                          ? 'bg-emerald-500/20 border-emerald-500/40 text-emerald-300'
                          : 'bg-rose-500/20 border-rose-500/40 text-rose-300'
                      }`}>
                        {isRoleAuthorized ? '✓' : '✕'}
                      </span>
                    </div>
                  </div>
                  <div className="flex items-center justify-between">
                    <span className="text-slate-400 text-[11px]">Legal State:</span>
                    <span className="font-mono text-emerald-400 font-bold px-2 py-0.5 rounded bg-emerald-500/15 border border-emerald-500/30">
                      {currentState || 'None'}
                    </span>
                  </div>

                  {/* Quick Payload Variables Peek */}
                  <div className="pt-2 border-t border-slate-800/80 flex items-center justify-between">
                    <span className="text-slate-500 text-[10px]">Payload:</span>
                    <div className="flex items-center gap-1.5">
                      <span className="font-mono text-[10px] text-slate-300 bg-slate-950 px-1.5 py-0.5 rounded truncate max-w-[170px]">
                        {Object.entries(payload).slice(0, 2).map(([k, v]) => `${k}:${v}`).join(', ')}
                      </span>
                      <button 
                        onClick={() => setActiveSideTab('context')} 
                        className="text-[10px] text-blue-400 hover:text-blue-300 underline font-medium"
                      >
                        Edit
                      </button>
                    </div>
                  </div>

                  {/* Lifecycle Hooks Display */}
                  {(currentOnEntry.length > 0 || currentOnExit.length > 0) && (
                    <div className="pt-2 border-t border-slate-800/80 space-y-1.5">
                      <div className="flex items-center justify-between text-[11px]">
                        <span className="text-amber-400 font-bold flex items-center gap-1">
                          <Zap size={12} /> Lifecycle Hooks:
                        </span>
                        <span className="text-[10px] text-slate-400 font-mono">
                          {currentOnEntry.length} onEntry · {currentOnExit.length} onExit
                        </span>
                      </div>
                      <div className="space-y-1">
                        {currentOnEntry.map((act: any, idx: number) => {
                          const type = getProp(act, 'actionType', 'ActionType') || 'Action';
                          const target = getProp(act, 'target', 'Target') || '';
                          const cond = getProp(act, 'condition', 'Condition');
                          const template = getProp(act, 'template', 'Template');
                          const mapping = getProp(act, 'payloadMapping', 'PayloadMapping');
                          const resolvedTarget = interpolateTemplate(target, payload);
                          const interpolatedMsg = template ? interpolateTemplate(template, payload) : null;
                          const transformedPayload = mapping && Object.keys(mapping).length > 0 ? transformPayload(mapping, payload) : null;
                          const evalRes = cond ? evaluateExpression(cond, payload) : null;
                          return (
                            <div key={`entry-${idx}`} className="p-1.5 rounded bg-slate-950 border border-slate-800 text-[10px] space-y-1">
                              <div className="flex items-center justify-between">
                                <div className="flex items-center gap-1.5 truncate">
                                  <span className="px-1 py-0.2 rounded bg-emerald-500/20 text-emerald-400 text-[9px] font-bold">OnEntry</span>
                                  {type === 'Webhook' ? <Send size={10} className="text-cyan-400 shrink-0" /> : type === 'Notification' ? <Bell size={10} className="text-amber-400 shrink-0" /> : <Radio size={10} className="text-indigo-400 shrink-0" />}
                                  <span className="text-slate-200 font-medium">{type}</span>
                                  <span className="text-slate-400 truncate max-w-[110px] font-mono">{resolvedTarget || target}</span>
                                </div>
                                {cond && (
                                  <span className={`text-[8px] px-1 py-0.2 rounded font-bold uppercase ${evalRes?.result ? 'bg-emerald-500/20 text-emerald-300' : 'bg-rose-500/20 text-rose-300'}`}>
                                    {evalRes?.result ? 'True' : 'False'}
                                  </span>
                                )}
                              </div>
                              {interpolatedMsg && (
                                <div className="text-[9px] text-slate-300 pl-1 font-mono truncate bg-slate-900/60 rounded px-1 py-0.5">
                                  <span className="text-slate-500">Msg:</span> "{interpolatedMsg}"
                                </div>
                              )}
                              {transformedPayload && Object.keys(transformedPayload).length > 0 && (
                                <div className="text-[9px] text-cyan-300/80 pl-1 font-mono truncate bg-slate-900/60 rounded px-1 py-0.5">
                                  <span className="text-slate-500">Payload:</span> {JSON.stringify(transformedPayload)}
                                </div>
                              )}
                            </div>
                          );
                        })}
                        {currentOnExit.map((act: any, idx: number) => {
                          const type = getProp(act, 'actionType', 'ActionType') || 'Action';
                          const target = getProp(act, 'target', 'Target') || '';
                          const cond = getProp(act, 'condition', 'Condition');
                          const template = getProp(act, 'template', 'Template');
                          const mapping = getProp(act, 'payloadMapping', 'PayloadMapping');
                          const resolvedTarget = interpolateTemplate(target, payload);
                          const interpolatedMsg = template ? interpolateTemplate(template, payload) : null;
                          const transformedPayload = mapping && Object.keys(mapping).length > 0 ? transformPayload(mapping, payload) : null;
                          const evalRes = cond ? evaluateExpression(cond, payload) : null;
                          return (
                            <div key={`exit-${idx}`} className="p-1.5 rounded bg-slate-950 border border-slate-800 text-[10px] space-y-1">
                              <div className="flex items-center justify-between">
                                <div className="flex items-center gap-1.5 truncate">
                                  <span className="px-1 py-0.2 rounded bg-amber-500/20 text-amber-400 text-[9px] font-bold">OnExit</span>
                                  {type === 'Webhook' ? <Send size={10} className="text-cyan-400 shrink-0" /> : type === 'Notification' ? <Bell size={10} className="text-amber-400 shrink-0" /> : <Radio size={10} className="text-indigo-400 shrink-0" />}
                                  <span className="text-slate-200 font-medium">{type}</span>
                                  <span className="text-slate-400 truncate max-w-[110px] font-mono">{resolvedTarget || target}</span>
                                </div>
                                {cond && (
                                  <span className={`text-[8px] px-1 py-0.2 rounded font-bold uppercase ${evalRes?.result ? 'bg-emerald-500/20 text-emerald-300' : 'bg-rose-500/20 text-rose-300'}`}>
                                    {evalRes?.result ? 'True' : 'False'}
                                  </span>
                                )}
                              </div>
                              {interpolatedMsg && (
                                <div className="text-[9px] text-slate-300 pl-1 font-mono truncate bg-slate-900/60 rounded px-1 py-0.5">
                                  <span className="text-slate-500">Msg:</span> "{interpolatedMsg}"
                                </div>
                              )}
                              {transformedPayload && Object.keys(transformedPayload).length > 0 && (
                                <div className="text-[9px] text-cyan-300/80 pl-1 font-mono truncate bg-slate-900/60 rounded px-1 py-0.5">
                                  <span className="text-slate-500">Payload:</span> {JSON.stringify(transformedPayload)}
                                </div>
                              )}
                            </div>
                          );
                        })}
                      </div>
                    </div>
                  )}
                </div>
              </div>

              {/* Action Controls */}
              <div className="p-4 border-b border-slate-800 shrink-0">
                <h4 className="text-xs font-bold text-slate-300 uppercase tracking-wider mb-3">Simulation Controls</h4>
                {renderControls()}
              </div>
            </>
          ) : (
            /* Context & Payload Editor Tab */
            <div className="p-4 flex-1 flex flex-col overflow-hidden space-y-4">
              <div>
                <div className="flex items-center justify-between mb-1.5">
                  <h4 className="text-xs font-bold text-white flex items-center gap-1.5">
                    <Database size={14} className="text-blue-400" /> Context Payload (JSON)
                  </h4>
                  <span className="text-[10px] text-slate-500 font-mono">Dynamic Dictionary</span>
                </div>
                <p className="text-[11px] text-slate-400">
                  Data evaluated by <strong>Decision steps</strong> and <strong>State Machine guards</strong>.
                </p>
              </div>

              {/* Preset Templates */}
              <div>
                <span className="text-[10px] text-slate-500 uppercase font-bold block mb-1">Load Preset Payload:</span>
                <div className="flex flex-wrap gap-1.5">
                  {Object.entries(PRESET_PAYLOADS).map(([k, v]) => (
                    <button
                      key={k}
                      onClick={() => loadPresetPayload(k)}
                      className="px-2 py-1 bg-slate-800 hover:bg-slate-750 text-slate-300 hover:text-white rounded text-[10px] font-medium transition-colors border border-slate-700"
                    >
                      {v.label}
                    </button>
                  ))}
                </div>
              </div>

              {/* Role Setting */}
              <div>
                <label className="block text-[10px] text-slate-400 uppercase font-bold mb-1">Your Role</label>
                <div className="flex gap-2 items-center">
                  <select 
                    value={simulatedRole} 
                    onChange={e => setSimulatedRole(e.target.value)}
                    className={`w-full bg-slate-950 border rounded-lg p-2 text-xs text-white focus:outline-none font-medium ${
                      isRoleAuthorized ? 'border-emerald-500/40 focus:border-emerald-500' : 'border-rose-500/40 focus:border-rose-500'
                    }`}
                  >
                    {allKnownRoles.map(r => (
                      <option key={r} value={r}>{r}</option>
                    ))}
                  </select>
                  <span className={`shrink-0 w-6 h-6 rounded-full flex items-center justify-center text-[10px] font-bold border ${
                    isRoleAuthorized
                      ? 'bg-emerald-500/20 border-emerald-500/40 text-emerald-300'
                      : 'bg-rose-500/20 border-rose-500/40 text-rose-300'
                  }`}>
                    {isRoleAuthorized ? '✓' : '✕'}
                  </span>
                </div>
                <p className="text-[10px] text-slate-500 mt-1">
                  Step requires: <strong className="text-amber-300">{activeRoleDisplay}</strong>
                </p>
              </div>

              {/* Live JSON Editor */}
              <div className="flex-1 flex flex-col overflow-hidden">
                <label className="block text-[10px] text-slate-400 uppercase font-bold mb-1">Payload Variables</label>
                <textarea
                  value={payloadText}
                  onChange={e => handlePayloadChange(e.target.value)}
                  className={`w-full flex-1 font-mono text-xs p-3 rounded-xl bg-slate-950 text-blue-300 focus:outline-none border ${
                    payloadError ? 'border-rose-500/70 text-rose-300' : 'border-slate-800 focus:border-blue-500'
                  }`}
                  placeholder="Enter JSON payload..."
                />
                {payloadError && (
                  <p className="text-[10px] text-rose-400 mt-1 flex items-center gap-1">
                    <AlertTriangle size={11} /> {payloadError}
                  </p>
                )}
              </div>

              <div className="pt-2 border-t border-slate-800 flex justify-end">
                <button
                  onClick={() => setActiveSideTab('controls')}
                  className="px-4 py-1.5 bg-blue-600 hover:bg-blue-500 text-white rounded-lg text-xs font-semibold shadow transition-colors flex items-center gap-1"
                >
                  Apply & Run Controls <ArrowRight size={13} />
                </button>
              </div>
            </div>
          )}
          
          {/* Execution Log */}
          <div className="flex-1 p-4 overflow-y-auto bg-slate-950/30">
            <h4 className="text-xs font-bold text-slate-500 uppercase tracking-wider mb-3 flex items-center gap-1">
              <Activity size={14} /> Execution Log
            </h4>
            <div className="space-y-2">
              <div className="text-[10px] text-emerald-400 font-mono pb-2 border-b border-slate-800/50">
                [SYSTEM] Simulator started. Initial Step: {startStepId} (State: {initialState || 'None'})
                {entityType ? ` · Entity: ${entityType}` : ''}
              </div>
              {history.map((log, idx) => {
                const isBusinessContext = log.startsWith('Business context:');
                return (
                  <div
                    key={idx}
                    className={`text-[10px] font-mono pb-2 border-b border-slate-800/50 last:border-0 flex items-start gap-1.5 ${
                      isBusinessContext ? 'text-amber-200/90' : 'text-slate-300'
                    }`}
                  >
                    <span className={isBusinessContext ? 'text-amber-500' : 'text-slate-600'}>
                      {isBusinessContext ? '◎' : '❯'}
                    </span>
                    <span className={isBusinessContext ? 'leading-relaxed' : undefined}>{log}</span>
                  </div>
                );
              })}
              {isSimulationComplete && (
                <div className="text-[10px] text-blue-400 font-mono pt-1 flex items-center justify-between gap-2">
                  <span>[SYSTEM] Reached End of Workflow.</span>
                  <button
                    type="button"
                    onClick={resetSimulation}
                    className="shrink-0 px-2 py-0.5 rounded bg-emerald-600 hover:bg-emerald-500 text-white font-bold flex items-center gap-1"
                  >
                    <RotateCcw size={11} /> Restart
                  </button>
                </div>
              )}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};
