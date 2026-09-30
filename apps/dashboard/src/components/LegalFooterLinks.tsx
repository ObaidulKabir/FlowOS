import React from 'react';
import { LEGAL_PAGES } from '../legalEntity';

export const LegalFooterLinks: React.FC<{ className?: string }> = ({ className = '' }) => (
  <>
    {LEGAL_PAGES.map(page => (
      <a key={page.href} href={page.href} className={className}>
        {page.label}
      </a>
    ))}
  </>
);
