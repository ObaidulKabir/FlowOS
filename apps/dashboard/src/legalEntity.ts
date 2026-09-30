import entity from './legalEntity.json';

export const legalEntity = entity;

export const sellerIdentity = [
  entity.operatorName,
  entity.registrationJurisdiction ? `registered in ${entity.registrationJurisdiction}` : '',
  entity.companyNumber ? `company no. ${entity.companyNumber}` : '',
  entity.registeredOffice ? entity.registeredOffice : ''
].filter(Boolean).join(' · ');

export const LEGAL_PAGES = [
  { href: '/product', label: 'Product' },
  { href: '/pricing', label: 'Pricing' },
  { href: '/security', label: 'Security' },
  { href: '/ai-agents', label: 'AI agents' },
  { href: '/company', label: 'Company' },
  { href: '/contact', label: 'Contact' },
  { href: '/terms', label: 'Terms' },
  { href: '/privacy', label: 'Privacy' },
  { href: '/refunds', label: 'Refunds' },
  { href: '/cancellation', label: 'Cancellation' },
  { href: '/acceptable-use', label: 'Acceptable use' }
] as const;
