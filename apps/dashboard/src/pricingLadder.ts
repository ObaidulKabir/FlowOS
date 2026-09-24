export type PricingTierId =
  | 'free'
  | 'starter'
  | 'builder'
  | 'team'
  | 'growth'
  | 'scale'
  | 'enterprise';

export interface PricingTier {
  id: PricingTierId;
  name: string;
  stage: string;
  price: string;
  priceNote: string;
  publications: string;
  events: string;
  mcpCalls: string;
  activeWorkflows: string;
  projects: string;
  concurrency: string;
  retention: string;
  members: string;
  support: string;
  highlight?: boolean;
}

export const PRICING_METERS = [
  {
    title: 'Workflow publications',
    weight: 'High value, scarce',
    body: 'A new WorkflowClass, a new version, or a material republish. Active published workflows are shown separately so republishing the same graph does not look like 30 products.'
  },
  {
    title: 'Events published',
    weight: 'Main scale meter',
    body: 'Business activity flowing through FlowOS: OrderCreated, PaymentCompleted, InventoryReserved. This is what should grow as a customer succeeds.'
  },
  {
    title: 'MCP tool calls',
    weight: 'Generous, inexpensive',
    body: 'How often an agent talks to the control plane. Included allowances stay large so a developer is not afraid to ask “why did this fail?”'
  }
] as const;

export const PRICING_TIERS: PricingTier[] = [
  {
    id: 'free',
    name: 'Free',
    stage: 'Explore',
    price: '$0',
    priceNote: '/ month',
    publications: '2',
    events: '2.5K',
    mcpCalls: '2.5K',
    activeWorkflows: '2',
    projects: '1',
    concurrency: '2',
    retention: '7 days',
    members: '1',
    support: 'Community'
  },
  {
    id: 'starter',
    name: 'Starter',
    stage: 'Build',
    price: '$9',
    priceNote: '/ month · ~$90 / year',
    publications: '10',
    events: '15K',
    mcpCalls: '15K',
    activeWorkflows: '5',
    projects: '1',
    concurrency: '5',
    retention: '14 days',
    members: '1',
    support: 'Community'
  },
  {
    id: 'builder',
    name: 'Builder',
    stage: 'Ship',
    price: '$29',
    priceNote: '/ month · ~$290 / year',
    publications: '30',
    events: '75K',
    mcpCalls: '75K',
    activeWorkflows: '15',
    projects: '3',
    concurrency: '10',
    retention: '30 days',
    members: '3',
    support: 'Standard',
    highlight: true
  },
  {
    id: 'team',
    name: 'Team',
    stage: 'Operate together',
    price: '$79',
    priceNote: '/ month · ~$790 / year',
    publications: '100',
    events: '300K',
    mcpCalls: '300K',
    activeWorkflows: '50',
    projects: '10',
    concurrency: '25',
    retention: '90 days',
    members: '10',
    support: 'Priority'
  },
  {
    id: 'growth',
    name: 'Growth',
    stage: 'Production',
    price: '$199',
    priceNote: '/ month · ~$1,990 / year',
    publications: '300',
    events: '1.5M',
    mcpCalls: '1.5M',
    activeWorkflows: '150',
    projects: '25',
    concurrency: '75',
    retention: '180 days',
    members: '25',
    support: 'Priority'
  },
  {
    id: 'scale',
    name: 'Scale',
    stage: 'Infrastructure',
    price: '$499',
    priceNote: '/ month · ~$4,990 / year',
    publications: '1,000',
    events: '10M',
    mcpCalls: '10M',
    activeWorkflows: '500',
    projects: '100',
    concurrency: '250',
    retention: '365 days',
    members: '50',
    support: 'Dedicated'
  },
  {
    id: 'enterprise',
    name: 'Enterprise',
    stage: 'Strategic',
    price: 'Custom',
    priceNote: 'typically $1,500–$5,000+ / month',
    publications: 'Custom',
    events: 'Custom',
    mcpCalls: 'Custom',
    activeWorkflows: 'Custom',
    projects: 'Custom',
    concurrency: 'Custom',
    retention: 'Custom',
    members: 'Custom',
    support: 'Dedicated + SLA'
  }
];
