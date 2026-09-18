type AnyRecord = Record<string, any>;

const isSystemRole = (role?: string) => {
  const key = (role || '').trim().toLowerCase();
  return !key || key === 'system' || key === 'anyone' || key === 'unassigned';
};

const read = (source: any, ...keys: string[]) => {
  if (!source) return undefined;
  for (const key of keys) {
    if (source[key] !== undefined) return source[key];
    const lower = key.charAt(0).toLowerCase() + key.slice(1);
    if (source[lower] !== undefined) return source[lower];
  }
  return undefined;
};

const asArray = (value: any): string[] => {
  if (!value) return [];
  if (Array.isArray(value)) return value.map(item => String(item).trim()).filter(Boolean);
  return [String(value).trim()].filter(Boolean);
};

const unique = (values: string[]) => {
  const seen = new Set<string>();
  const result: string[] = [];
  values.forEach(value => {
    const key = value.toLowerCase();
    if (seen.has(key)) return;
    seen.add(key);
    result.push(value);
  });
  return result;
};

const humanExitEvents = (step: any): string[] => {
  const next = read(step, 'NextSteps', 'nextSteps') || {};
  return Object.keys(next).filter(key => {
    const trimmed = key.trim();
    return trimmed &&
      !['default', 'true', 'subworkflowcompleted'].includes(trimmed.toLowerCase());
  });
};

const inboxRoles = (step: any): string[] => {
  return unique([
    ...asArray(read(step, 'RequiredRoles', 'requiredRoles')),
    ...asArray(read(step, 'AllowedRoles', 'allowedRoles'))
  ].filter(role => !isSystemRole(role)));
};

const isHumanTask = (step: any) =>
  String(read(step, 'StepType', 'stepType') || '').toLowerCase() === 'humantask';

/**
 * Stamps a visual-sandbox / editor pack so DraftSimulator "As role"
 * uses capability intersection: requiredCapabilities + catalog grants.
 */
export function applySimulationGovernance(definition: AnyRecord): AnyRecord {
  if (!definition) return definition;

  const events: any[] = read(definition, 'Events', 'events') || [];
  const workflow = read(definition, 'Workflow', 'workflow') || {};
  const steps: any[] = read(workflow, 'Steps', 'steps') || [];

  const humanEventIds = new Set<string>();
  steps.forEach(step => {
    if (!isHumanTask(step)) return;
    humanExitEvents(step).forEach(eventId => humanEventIds.add(eventId.toLowerCase()));
  });
  events.forEach(evt => {
    const category = String(read(evt, 'Category', 'category') || '');
    const allowed = asArray(read(evt, 'AllowedRoles', 'allowedRoles'));
    if (category.toLowerCase() === 'human' || allowed.some(role => !isSystemRole(role))) {
      const id = String(read(evt, 'EventId', 'eventId') || '').trim();
      if (id) humanEventIds.add(id.toLowerCase());
    }
  });

  const catalog = new Set<string>();
  (read(definition, 'Capabilities', 'capabilities') || []).forEach((item: any) => {
    const code = String(read(item, 'Code', 'code') || '').trim();
    if (code) catalog.add(code);
  });

  events.forEach(evt => {
    const eventId = String(read(evt, 'EventId', 'eventId') || '').trim();
    let caps = unique(asArray(read(evt, 'RequiredCapabilities', 'requiredCapabilities')));
    const isHuman = eventId && humanEventIds.has(eventId.toLowerCase());
    if (isHuman && caps.length === 0) caps = [`event.publish.${eventId}`];
    caps.forEach(cap => catalog.add(cap));
    evt.Category = isHuman ? 'Human' : (read(evt, 'Category', 'category') ?? evt.Category);
    evt.RequiredCapabilities = caps;
    evt.requiredCapabilities = caps;
  });

  const grants = new Map<string, Set<string>>();
  (read(definition, 'Roles', 'roles') || []).forEach((role: any) => {
    const name = String(read(role, 'Name', 'name') || '').trim();
    if (!name) return;
    grants.set(name, new Set(asArray(read(role, 'GrantedCapabilities', 'grantedCapabilities'))));
  });

  steps.forEach(step => {
    if (!isHumanTask(step)) return;
    let caps = unique(asArray(read(step, 'RequiredCapabilities', 'requiredCapabilities')));
    if (caps.length === 0) {
      caps = humanExitEvents(step).map(eventId => `event.publish.${eventId}`);
    }
    caps.forEach(cap => catalog.add(cap));
    step.RequiredCapabilities = caps;
    step.requiredCapabilities = caps;

    inboxRoles(step).forEach(role => {
      if (!grants.has(role)) grants.set(role, new Set());
      caps.forEach(cap => grants.get(role)!.add(cap));
    });
  });

  events.forEach(evt => {
    const caps = unique(asArray(read(evt, 'RequiredCapabilities', 'requiredCapabilities')));
    asArray(read(evt, 'AllowedRoles', 'allowedRoles'))
      .filter(role => !isSystemRole(role))
      .forEach(role => {
        if (!grants.has(role)) grants.set(role, new Set());
        caps.forEach(cap => grants.get(role)!.add(cap));
      });
  });

  const managerCaps = [...(grants.get('Manager') || [])]
    .filter(cap => cap.toLowerCase().startsWith('event.publish.'));
  if (managerCaps.length > 0) {
    if (!grants.has('Director')) grants.set('Director', new Set());
    managerCaps.forEach(cap => grants.get('Director')!.add(cap));
  }

  const roles = [...grants.entries()]
    .sort((a, b) => a[0].localeCompare(b[0]))
    .map(([name, caps]) => ({
      Name: name,
      name,
      Description: `${name} inbox and capability grants`,
      description: `${name} inbox and capability grants`,
      GrantedCapabilities: [...caps].sort((a, b) => a.localeCompare(b)),
      grantedCapabilities: [...caps].sort((a, b) => a.localeCompare(b))
    }));

  const existingCaps = new Map<string, any>();
  (read(definition, 'Capabilities', 'capabilities') || []).forEach((item: any) => {
    const code = String(read(item, 'Code', 'code') || '').trim();
    if (code) existingCaps.set(code.toLowerCase(), item);
  });

  const capabilities = [...catalog]
    .sort((a, b) => a.localeCompare(b))
    .map(code => {
      const existing = existingCaps.get(code.toLowerCase());
      if (existing) return existing;
      return {
        Code: code,
        code,
        Description: `Publish or complete activity gated by ${code}`,
        description: `Publish or complete activity gated by ${code}`
      };
    });

  definition.Events = events;
  definition.events = events;
  definition.Workflow = workflow;
  definition.workflow = workflow;
  definition.Roles = roles;
  definition.roles = roles;
  definition.Capabilities = capabilities;
  definition.capabilities = capabilities;
  return definition;
}
