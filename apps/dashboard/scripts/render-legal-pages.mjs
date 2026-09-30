import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const entity = JSON.parse(readFileSync(join(root, 'src', 'legalEntity.json'), 'utf8'));

const pages = [
  { href: '/terms', file: 'terms.html', label: 'Terms' },
  { href: '/privacy', file: 'privacy.html', label: 'Privacy' },
  { href: '/refunds', file: 'refunds.html', label: 'Refunds' },
  { href: '/acceptable-use', file: 'acceptable-use.html', label: 'Acceptable use' },
  { href: '/contact', file: 'contact.html', label: 'Contact' }
];

const esc = value => String(value)
  .replaceAll('&', '&amp;')
  .replaceAll('<', '&lt;')
  .replaceAll('>', '&gt;')
  .replaceAll('"', '&quot;');

const sellerFacts = [
  `${entity.productName} is operated by ${entity.operatorName}.`,
  entity.companyNumber ? `Company number: ${entity.companyNumber}.` : '',
  entity.registeredOffice ? `Registered office: ${entity.registeredOffice}.` : '',
  `Website: ${entity.siteUrl}`,
  `Support and billing: ${entity.supportEmail}`
].filter(Boolean);

const sellerHtml = sellerFacts.map(line => `<p>${esc(line)}</p>`).join('\n');

const mail = `<a href="mailto:${esc(entity.supportEmail)}">${esc(entity.supportEmail)}</a>`;

const layout = ({ title, description, body }) => `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>${esc(title)} — ${esc(entity.productName)}</title>
  <meta name="description" content="${esc(description)}" />
  <link rel="icon" type="image/png" href="/brand/flowos-icon.png" />
  <style>
    :root { color-scheme: dark; }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      font-family: "Segoe UI", system-ui, sans-serif;
      background: #020617;
      color: #e2e8f0;
      line-height: 1.6;
    }
    a { color: #93c5fd; }
    header, footer, main { max-width: 46rem; margin: 0 auto; padding: 1.25rem 1.25rem; }
    header { display: flex; justify-content: space-between; gap: 1rem; align-items: center; border-bottom: 1px solid #1e293b; }
    header a.brand { color: #fff; font-weight: 800; text-decoration: none; letter-spacing: -0.02em; }
    nav { display: flex; flex-wrap: wrap; gap: 0.75rem; font-size: 0.8rem; }
    h1 { font-size: 1.8rem; line-height: 1.2; margin: 0.4rem 0 0.6rem; }
    h2 { font-size: 1.05rem; margin: 1.6rem 0 0.4rem; }
    .kicker { color: #94a3b8; font-size: 0.8rem; margin: 0; }
    .seller {
      background: #0f172a;
      border: 1px solid #334155;
      border-radius: 0.9rem;
      padding: 0.9rem 1rem;
      margin: 1rem 0 1.4rem;
      font-size: 0.92rem;
    }
    .seller p { margin: 0.15rem 0; }
    ul { padding-left: 1.2rem; }
    li { margin: 0.35rem 0; }
    footer { border-top: 1px solid #1e293b; color: #94a3b8; font-size: 0.8rem; }
  </style>
</head>
<body>
  <header>
    <a class="brand" href="/">${esc(entity.productName)}</a>
    <nav>
      ${pages.map(page => `<a href="${page.href}">${esc(page.label)}</a>`).join('\n      ')}
    </nav>
  </header>
  <main>
    <p class="kicker">Last updated ${esc(entity.lastUpdated)}</p>
    <h1>${esc(title)}</h1>
    <aside class="seller">
      ${sellerHtml}
    </aside>
    ${body}
  </main>
  <footer>
    <p>© 2026 ${esc(entity.operatorName)}. ${esc(entity.productName)} at ${esc(entity.siteUrl)}.</p>
    <nav>
      ${pages.map(page => `<a href="${page.href}">${esc(page.label)}</a>`).join('\n      ')}
    </nav>
  </footer>
</body>
</html>
`;

const documents = {
  'terms.html': layout({
    title: 'Terms of Service',
    description: 'The contract for the hosted FlowOS workflow control plane.',
    body: `
    <p>These terms are the contract for the hosted ${esc(entity.productName)} service between you and ${esc(entity.operatorName)} (the operator). They cover the website, the dashboard, the API, and the Model Context Protocol (MCP) control plane at ${esc(entity.siteUrl)}.</p>
    <h2>The service</h2>
    <p>${esc(entity.productName)} is a hosted workflow control plane. You use it to design, validate, simulate, run, recover, and govern workflows. A paid subscription is a right to use that hosted service for the term you pay for. It is not a sale of the software.</p>
    <h2>Who may buy</h2>
    <p>Paid plans are sold to businesses. The sandbox is a demonstration workspace and is not a paid contract. If you are a consumer, nothing here removes a right the law does not let us waive, including any cooling-off right that applies to a distance contract.</p>
    <h2>Accounts</h2>
    <p>You register a tenant, verify the account email, and keep API keys and passwords confidential. You are responsible for activity under your tenant, including activity by an AI agent that uses your key.</p>
    <h2>Plans and price</h2>
    <p>Free registration is design-time: you can draft, lint, validate, and simulate. Starter, Builder, Team, Growth, and Scale are paid subscriptions that unlock runtime. Enterprise and any student or local-developer arrangement are agreed in writing and are not a self-serve card price.</p>
    <p>List prices on the website are in ${esc(entity.currencyName)} (${esc(entity.currencyCode)}) and exclude VAT. Where the operator must charge VAT, it is added on the invoice or at card checkout. An annual subscription is the price of ten months (two months free) and is paid in advance.</p>
    <p>What a package includes — publications, events, MCP calls, retention, and the other limits on the pricing page — is part of this contract. Soft overage, when billed, is described on that page. We do not charge per retry, per simulation, or per transition.</p>
    <h2>Payment</h2>
    <p>When card checkout is enabled, card payments are taken by Stripe. ${esc(entity.productName)} does not store your card number. Until checkout is enabled, a paid plan is activated after you request it from ${mail} and the operator confirms payment. A failed or reversed payment returns the tenant to design-time.</p>
    <h2>Cancellation</h2>
    <p>You may cancel a subscription as described on the <a href="/refunds">refunds and cancellation</a> page. Access for a cancelled paid plan continues until the end of the period already paid.</p>
    <h2>Your content</h2>
    <p>You keep ownership of the workflows, business context, and other tenant data you submit. You give the operator permission to host, process, and back up that data only to provide, secure, and support the service.</p>
    <h2>Acceptable use</h2>
    <p>You will follow the <a href="/acceptable-use">acceptable use</a> rules. We may suspend a tenant that breaks them, or that does not pay, and we will tell you when we do unless the law or a security incident requires otherwise.</p>
    <h2>Availability</h2>
    <p>We run the service with reasonable care. A service-level agreement applies only when it is written into an Enterprise agreement. Maintenance and incidents can interrupt access.</p>
    <h2>Liability</h2>
    <p>Nothing in these terms limits liability for fraud, or for death or personal injury caused by negligence, or any other liability that cannot legally be limited. Otherwise, each party's total liability arising out of the service in a 12-month period is limited to the fees you paid for the service in that period. We are not liable for lost profits, lost data, or indirect loss, to the extent the law allows that exclusion.</p>
    <h2>Law</h2>
    <p>These terms are governed by ${esc(entity.governingLaw)}. The courts of that place decide disputes, except where a consumer has a legal right to use a different court.</p>
    <h2>Changes</h2>
    <p>We may update these terms. The date at the top of this page is the current version. If a change materially reduces a paid plan you are already on, we will email the account address before it applies to you.</p>
    `
  }),
  'privacy.html': layout({
    title: 'Privacy notice',
    description: 'How FlowOS uses account, tenant, and billing information.',
    body: `
    <p>${esc(entity.operatorName)} is the controller for personal information processed to provide ${esc(entity.productName)}.</p>
    <h2>What we use</h2>
    <ul>
      <li>Account details: name, email address, and email-verification status.</li>
      <li>Tenant details: tenant name, identifiers, plan, and billing status.</li>
      <li>Product data you submit: workflow definitions, business context, events, and files you choose to back up.</li>
      <li>Credentials: API keys are stored so the service can authenticate requests. A full key is shown to you once when it is created.</li>
      <li>Support messages you send to ${mail}.</li>
      <li>Technical logs needed to keep the service secure and to diagnose faults.</li>
    </ul>
    <h2>Why</h2>
    <p>We use this information to provide the service, authenticate you, take or record payment, secure the platform, answer support requests, and meet legal duties. The lawful bases are contract, legitimate interests in running a secure multi-tenant service, and legal obligation where one applies.</p>
    <h2>Payment data</h2>
    <p>When card checkout is enabled, Stripe Payments Europe or its affiliates collect and process the card payment. We receive the payment status, customer and subscription identifiers, and the plan you bought. We do not receive or store the full card number.</p>
    <h2>Where it is processed</h2>
    <p>The service at ${esc(entity.siteUrl)} may be hosted outside your country. We use processors who host the application, send email, and, when checkout is on, take payment. An Enterprise agreement can set a specific hosting region.</p>
    <h2>How long</h2>
    <p>Account and tenant records are kept while the tenant is open and for a short period afterwards so we can resolve billing and security questions. Event retention follows the package on the pricing page. You can ask us to delete a tenant; we will delete or anonymise personal information unless we must keep it for tax, fraud, or legal claims.</p>
    <h2>Your rights</h2>
    <p>You can ask for access, correction, deletion, restriction, or a copy of your personal information, and you can object to processing based on legitimate interests. Write to ${mail}. If you are in the UK, you can also complain to the Information Commissioner's Office. If you are elsewhere, you can complain to your local data-protection authority.</p>
    `
  }),
  'refunds.html': layout({
    title: 'Refunds and cancellation',
    description: 'How to cancel a FlowOS subscription and when fees are refunded.',
    body: `
    <p>This page is the cancellation and refund policy for hosted ${esc(entity.productName)}.</p>
    <h2>Free and sandbox</h2>
    <p>The sandbox and the Free plan have no subscription fee, so there is nothing to refund. Closing a free tenant does not create a charge.</p>
    <h2>Monthly plans</h2>
    <p>Starter, Builder, Team, Growth, and Scale renew each month until you cancel. You may cancel at any time. The tenant keeps the paid runtime until the end of the month already paid. We do not refund the current month.</p>
    <h2>Annual plans</h2>
    <p>An annual plan is paid in advance at the price of ten months. You may cancel before it renews. We do not refund the current annual term, except where the law requires a refund.</p>
    <h2>How to cancel</h2>
    <p>Email ${mail} from the account address. Include the tenant name and whether you want cancellation at the end of the current period. When the billing portal is available, you can cancel there instead. Cancellation stops the next renewal. It does not delete your workflows by itself; ask us if you also want the tenant deleted.</p>
    <h2>Failed payment</h2>
    <p>If a renewal payment fails, the tenant is marked past due and then returns to design-time. Runtime stays off until the invoice is paid. We do not charge a separate failed-payment fee.</p>
    <h2>Enterprise and other written plans</h2>
    <p>Enterprise, and any student or local-developer price, follow the written agreement or the invoice. They are not cancelled or refunded through the self-serve card rules above.</p>
    <h2>If something is wrong</h2>
    <p>If you were charged in error, or charged twice, email ${mail} and we will correct it. Please contact us before raising a card dispute.</p>
    `
  }),
  'acceptable-use.html': layout({
    title: 'Acceptable use',
    description: 'Rules for using the FlowOS sandbox, dashboard, API, and MCP control plane.',
    body: `
    <p>These rules apply to the website, sandbox, dashboard, API, and MCP endpoint.</p>
    <ul>
      <li>Do not break the law, or use the service to help someone else break the law.</li>
      <li>Do not probe, scan, or overload the platform except through a test we have agreed in writing.</li>
      <li>Do not attempt to access another tenant's data, keys, or workflows.</li>
      <li>Do not share API keys, or use the sandbox as a production system for real customers.</li>
      <li>Do not send spam or unlawful messages through connectors, webhooks, or email actions.</li>
      <li>Do not upload malware, or submit another person's secrets except where you have the right to process them for your own workflow.</li>
      <li>Do not resell the hosted service as if it were your own platform, except under a written Enterprise agreement.</li>
    </ul>
    <p>We may suspend or close a tenant that breaks these rules. Where we can do so safely, we will email the account address and tell you what to fix.</p>
    `
  }),
  'contact.html': layout({
    title: 'Contact',
    description: 'How to reach FlowOS for support, billing, privacy, and security.',
    body: `
    <p>One inbox covers support, billing, privacy, and security: ${mail}.</p>
    <h2>What to include</h2>
    <ul>
      <li>Support: tenant name, what you were doing, and the time it failed.</li>
      <li>Billing: tenant name, the plan you want or the invoice you are asking about, and whether you want to cancel.</li>
      <li>Privacy: the email on the account, and whether you want access, correction, or deletion.</li>
      <li>Security: a description of the issue. Do not send a live API key or password in the email.</li>
    </ul>
    <p>We aim to reply within two business days. Enterprise response times apply only when a written support agreement says so.</p>
    <p>The public product and prices are at <a href="/">${esc(entity.siteUrl)}</a>.</p>
    `
  })
};

for (const page of pages) {
  writeFileSync(join(root, 'public', page.file), documents[page.file]);
}

console.log(`Wrote ${pages.length} legal pages for ${entity.operatorName}.`);
