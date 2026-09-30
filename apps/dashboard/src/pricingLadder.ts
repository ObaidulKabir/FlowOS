import catalog from './pricingCatalog.json';

export interface PricingTier {
  id: string;
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

interface CatalogTier {
  id: string;
  name: string;
  stage: string;
  monthlyUsd: number | null;
  priceNote?: string;
  publications: number | null;
  eventsPerMonth: number | null;
  mcpCallsPerMonth: number | null;
  activeWorkflows: number | null;
  projects: number | null;
  concurrency: number | null;
  retentionDays: number | null;
  members: number | null;
  support: string;
  highlight?: boolean;
  runtime: boolean;
  publicIncludes?: string;
}

const usd = (amount: number) =>
  `$${amount.toLocaleString('en-US')}`;

const compact = (value: number) => {
  if (value >= 1_000_000) {
    const millions = value / 1_000_000;
    return `${Number.isInteger(millions) ? millions : millions}M`;
  }
  if (value >= 1_000) {
    const thousands = value / 1_000;
    return `${Number.isInteger(thousands) ? thousands : thousands}K`;
  }
  return String(value);
};

const allowance = (value: number | null, kind: 'count' | 'volume' | 'days' | 'members') => {
  if (value == null) return 'Custom';
  if (kind === 'days') return `${value} days`;
  if (kind === 'volume') return compact(value);
  if (kind === 'count') return value.toLocaleString('en-US');
  return String(value);
};

const presentTier = (tier: CatalogTier): PricingTier => {
  const paid = tier.monthlyUsd != null && tier.monthlyUsd > 0;
  const annual = paid ? (tier.monthlyUsd as number) * catalog.annualMonthsPaid : 0;
  return {
    id: tier.id,
    name: tier.name,
    stage: tier.stage,
    price: tier.monthlyUsd == null ? 'Custom' : usd(tier.monthlyUsd),
    priceNote: tier.monthlyUsd == null
      ? (tier.priceNote ?? 'Custom')
      : paid
        ? `/ month · ~${usd(annual)} / year`
        : '/ month',
    publications: allowance(tier.publications, 'count'),
    events: allowance(tier.eventsPerMonth, 'volume'),
    mcpCalls: allowance(tier.mcpCallsPerMonth, 'volume'),
    activeWorkflows: allowance(tier.activeWorkflows, 'count'),
    projects: allowance(tier.projects, 'count'),
    concurrency: allowance(tier.concurrency, 'count'),
    retention: allowance(tier.retentionDays, 'days'),
    members: allowance(tier.members, 'members'),
    support: tier.support,
    highlight: tier.highlight
  };
};

export const PRICING_POLICY = {
  annualMonthsPaid: catalog.annualMonthsPaid,
  annualMonthsFree: catalog.annualMonthsFree,
  hardStop: catalog.hardStop,
  automaticOverageBilling: catalog.automaticOverageBilling,
  annual: catalog.copy.annual,
  annualDetail: catalog.copy.annualDetail,
  notCharged: catalog.copy.notCharged,
  overage: catalog.copy.overage,
  softOverage: catalog.copy.softOverage
};

export const PRICING_METERS = catalog.meters;
export const PRICING_TIERS: PricingTier[] = (catalog.tiers as CatalogTier[]).map(presentTier);

export const pricingTier = (id: string) => PRICING_TIERS.find(tier => tier.id === id);
