import entity from './legalEntity.json';

export const legalEntity = entity;

export const sellerIdentity = [
  entity.operatorName,
  entity.companyNumber ? `company no. ${entity.companyNumber}` : '',
  entity.registeredOffice ? entity.registeredOffice : ''
].filter(Boolean).join(' · ');

export const LEGAL_PAGES = [
  { href: '/terms', label: 'Terms' },
  { href: '/privacy', label: 'Privacy' },
  { href: '/refunds', label: 'Refunds' },
  { href: '/acceptable-use', label: 'Acceptable use' },
  { href: '/contact', label: 'Contact' }
] as const;
