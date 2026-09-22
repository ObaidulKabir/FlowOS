export const MCP_JSONRPC_RULE =
  'POST JSON-RPC to the advertised URL exactly, including a trailing slash when present. Do not normalize /mcp/ down to /mcp. Do not follow 301/302 redirects for POST. Accept must include application/json and text/event-stream. Production (flowosbd.com) is /mcp/; staging (flowos.prospectbdltd.com) is /mcp. Tenant keys are host-scoped.';

function isProductionHost(originOrUrl: string): boolean {
  try {
    const host = new URL(originOrUrl).hostname.toLowerCase();
    return host === 'flowosbd.com' || host.endsWith('.flowosbd.com');
  } catch {
    return originOrUrl.toLowerCase().includes('flowosbd.com');
  }
}

export function mcpRpcPath(origin = typeof window !== 'undefined' ? window.location.origin : ''): string {
  return isProductionHost(origin) ? '/mcp/' : '/mcp';
}

export function mcpRpcUrl(origin = typeof window !== 'undefined' ? window.location.origin : ''): string {
  const configuredUrl = import.meta.env.VITE_MCP_URL?.trim();
  if (configuredUrl)
    return configuredUrl;

  try {
    const local = new URL(origin);
    const isLocalHost = local.hostname === 'localhost' || local.hostname === '127.0.0.1';
    const isDashboardPort = ['3000', '4173', '4174', '5173'].includes(local.port);
    if (isLocalHost && isDashboardPort)
      return `${local.protocol}//${local.hostname}:8081/mcp`;
  } catch {
    // Fall through to same-origin resolution for relative or malformed input.
  }

  const base = (origin || '').replace(/\/$/, '');
  if (!base)
    return mcpRpcPath(origin);

  return `${base}${mcpRpcPath(base)}`;
}
