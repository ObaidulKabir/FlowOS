export interface ApiKeyScopeDefinition {
  id: string;
  label: string;
  description: string;
  capabilities: string[];
}

export const API_KEY_SCOPE_DEFINITIONS: ApiKeyScopeDefinition[] = [
  {
    id: '*',
    label: 'Full Admin',
    description: 'All capabilities held by the tenant Admin role.',
    capabilities: ['All Admin role capabilities']
  },
  {
    id: 'workflow:start',
    label: 'Start Workflows',
    description: 'Create live workflow instances.',
    capabilities: ['workflow.start']
  },
  {
    id: 'event:publish',
    label: 'Publish Events',
    description: 'Advance authorized workflow events.',
    capabilities: ['event.publish', 'event.publish.<eventId>']
  },
  {
    id: 'task:complete',
    label: 'Complete Tasks',
    description: 'Complete authorized HumanTask work.',
    capabilities: ['task.complete']
  },
  {
    id: 'workflow:read',
    label: 'Read Telemetry',
    description: 'Read tenant workflow telemetry.',
    capabilities: ['workflow.read']
  },
  {
    id: 'governance:manage',
    label: 'Manage Blueprints',
    description: 'Create and govern workflow definitions within the role ceiling.',
    capabilities: ['workflow.create', 'workflow.approve_public']
  }
];

export const API_KEY_SCOPE_PRESETS = {
  full: ['*'],
  operator: ['workflow:start', 'event:publish', 'task:complete', 'workflow:read'],
  readOnly: ['workflow:read']
} as const;

export const effectiveCapabilitiesForScopes = (scopes: string[]): string[] => {
  if (scopes.includes('*') || scopes.includes('admin:*')) {
    return ['All capabilities held by the tenant Admin role'];
  }

  return Array.from(new Set(
    API_KEY_SCOPE_DEFINITIONS
      .filter(definition => scopes.includes(definition.id))
      .flatMap(definition => definition.capabilities)
  ));
};
