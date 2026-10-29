import { AuthSession } from '../types';

/** Same id as TenantIdentityRules.PlatformTenantId. */
export const PLATFORM_TENANT_ID = '11111111-1111-1111-1111-111111111111';

const decodeJwtPayload = (token: string): Record<string, unknown> | null => {
  const parts = token.split('.');
  if (parts.length !== 3) return null;
  try {
    const base64 = parts[1].replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64 + '='.repeat((4 - (base64.length % 4)) % 4);
    const bytes = Uint8Array.from(atob(padded), char => char.charCodeAt(0));
    return JSON.parse(new TextDecoder().decode(bytes));
  } catch {
    return null;
  }
};

const tokenIsUnusable = (token?: string): boolean => {
  if (!token) return true;
  const payload = decodeJwtPayload(token);
  if (!payload) return false;
  return typeof payload.exp === 'number' && payload.exp * 1000 < Date.now();
};

const isPrivilegedRole = (value: unknown) =>
  typeof value === 'string' && ['admin', 'superadmin'].includes(value.trim().toLowerCase());

/** True only for a usable sign-in token of an Admin on the platform tenant. */
export const isPlatformAdminToken = (token?: string): boolean => {
  if (!token || tokenIsUnusable(token)) return false;
  const payload = decodeJwtPayload(token);
  if (!payload) return false;
  const tenant = String(payload.tenant_id ?? '').trim().toLowerCase();
  if (tenant !== PLATFORM_TENANT_ID) return false;
  if (isPrivilegedRole(payload.role)) return true;
  return Array.isArray(payload.roles) && payload.roles.some(isPrivilegedRole);
};

/** Restores the admin panel for the signed-in platform administrator. */
export const platformAdminHomeSession = (session: AuthSession): AuthSession | null => {
  if (!session.token || session.isSandbox === true || !isPlatformAdminToken(session.token)) return null;
  const payload = decodeJwtPayload(session.token);
  const tenantName = typeof payload?.tenant_name === 'string' && payload.tenant_name.trim()
    ? payload.tenant_name.trim()
    : 'Platform Administrator';
  return {
    ...session,
    role: 'Admin',
    tenantId: PLATFORM_TENANT_ID,
    tenantName,
    isSandbox: false,
    isEmailVerified: true
  };
};
