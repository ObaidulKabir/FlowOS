export function mcpRpcUrl(origin = typeof window !== 'undefined' ? window.location.origin : ''): string {
  const base = (origin || '').replace(/\/$/, '');
  if (!base)
    return '/mcp';

  try {
    const host = new URL(base).hostname.toLowerCase();
    if (host === 'flowosbd.com' || host.endsWith('.flowosbd.com'))
      return `${base}/mcp/`;
  } catch {
    // ignore invalid origin
  }

  return `${base}/mcp`;
}
