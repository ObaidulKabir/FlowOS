# Policy Management Guide

This guide explains how to manage **Policies** in FlowOS. Policies provide dynamic, tenant-specific access control rules that go beyond static capabilities.

## 1. Concepts
- **Policy**: A rule definition stored in the database.
- **Provider**: `EfCorePolicyProvider` loads policies for the current tenant.
- **Enforcement**: `PolicyEnforcementBehavior` checks all loaded policies against the current command.

## 2. Managing Policies

### A. Create a Policy
Policies are created with a Name and a JSON condition (currently placeholder).

**Endpoint:** `POST /api/policies`

**Request:**
```bash
curl -X POST "http://localhost:5001/api/policies" \
  -H "Content-Type: application/json" \
  -H "X-Tenant-ID: 3fa85f64-5717-4562-b3fc-2c963f66afa6" \
  -d '{
    "name": "DenyAll",
    "conditionJson": "{ \"action\": \"Deny\" }"
  }'
```

**Response:**
```json
{
  "id": "503a5545-6177-48f6-bb8a-ac35bb23a0f5"
}
```

### B. Policy Enforcement
Once created, policies are automatically active for the tenant.

**Example:**
If a "DenyAll" policy exists (and the evaluator supports it), all secured commands will be rejected.

**Response (403 Forbidden):**
```json
{
  "title": "Policy Violation",
  "status": 403,
  "detail": "Policy 'DenyAll' denied execution: DenyAll policy is active."
}
```

## 3. Future Enhancements
- **Rich Conditions**: Use JSON Logic or a Rules Engine to parse `ConditionJson`.
- **Targeting**: Apply policies to specific Resource Types or Actions.
