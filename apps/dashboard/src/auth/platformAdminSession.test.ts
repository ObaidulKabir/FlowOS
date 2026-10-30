import { describe, expect, test } from 'vitest';
import {
  PLATFORM_TENANT_ID,
  isExpiredPlatformAdminToken,
  isPlatformAdminToken,
  isPrivilegedRole
} from './platformAdminSession';

const jwt = (payload: Record<string, unknown>) =>
  `header.${btoa(JSON.stringify(payload))}.signature`;

const otherTenant = 'd3e8e2fd-1a8d-416a-987e-0585937090fd';

describe('isExpiredPlatformAdminToken', () => {
  test('is true for an expired administrator token on the platform tenant', () => {
    expect(isExpiredPlatformAdminToken(jwt({ exp: 1, tenant_id: PLATFORM_TENANT_ID, role: 'Admin' }))).toBe(true);
  });

  test('is false while the token is still valid', () => {
    const token = jwt({ exp: 4_000_000_000, tenant_id: PLATFORM_TENANT_ID, role: 'Admin' });
    expect(isExpiredPlatformAdminToken(token)).toBe(false);
    expect(isPlatformAdminToken(token)).toBe(true);
  });

  test('is false for a tenant administrator, a non-admin and a missing token', () => {
    expect(isExpiredPlatformAdminToken(jwt({ exp: 1, tenant_id: otherTenant, role: 'Admin' }))).toBe(false);
    expect(isExpiredPlatformAdminToken(jwt({ exp: 1, tenant_id: PLATFORM_TENANT_ID, role: 'Member' }))).toBe(false);
    expect(isExpiredPlatformAdminToken(undefined)).toBe(false);
    expect(isExpiredPlatformAdminToken('not-a-jwt')).toBe(false);
  });

  test('recognises SuperAdmin in the roles list', () => {
    expect(isExpiredPlatformAdminToken(jwt({ exp: 1, tenant_id: PLATFORM_TENANT_ID, roles: ['SuperAdmin'] }))).toBe(true);
  });
});

describe('isPrivilegedRole', () => {
  test('accepts Admin and SuperAdmin in any case and nothing else', () => {
    expect(isPrivilegedRole('Admin')).toBe(true);
    expect(isPrivilegedRole(' superadmin ')).toBe(true);
    expect(isPrivilegedRole('Member')).toBe(false);
    expect(isPrivilegedRole(undefined)).toBe(false);
  });
});
