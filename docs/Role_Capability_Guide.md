# Role and Capability Management Guide

This guide explains how to manage **Roles** and **Capabilities** (permissions) in FlowOS using the API.

## 1. Concepts

- **Role**: A named collection of capabilities (e.g., "Manager").
- **Capability**: A granular permission string (e.g., `task.approve`).
- **Policy**: Access control logic that checks capabilities.

## 2. Managing Roles

### A. Create a New Role

Creates a role within the current tenant.

**Endpoint:** `POST /api/roles`

**Request:**

```bash
curl -X POST "http://localhost:5001/api/roles" \
  -H "Content-Type: application/json" \
  -H "X-Tenant-ID: 3fa85f64-5717-4562-b3fc-2c963f66afa6" \
  -d '{
    "roleName": "Manager"
  }'
```

**Response:**

```json
{
  "id": "4791ff7e-3b57-4f8c-a0cb-0adf74753966"
}
```

### B. Add Capability to Role

Assigns a specific permission (Capability) to a Role.

**Endpoint:** `POST /api/roles/{roleId}/capabilities`

**Request:**

```bash
curl -X POST "http://localhost:5001/api/roles/{roleId}/capabilities" \
  -H "Content-Type: application/json" \
  -H "X-Tenant-ID: 3fa85f64-5717-4562-b3fc-2c963f66afa6" \
  -d '{
    "capabilityCode": "task.approve"
  }'
```

### C. Get Role Details

**Endpoint:** `GET /api/roles/{roleId}`

## 3. Recommended Capabilities

Use these standard capability codes for consistency:

- `workflow.start`: Required to start a new workflow instance.
- `event.publish`: Required to publish events (e.g., approvals).
- `role.create`: Required to create new roles (Admin only).
- `task.approve`: Domain-specific capability for approvals.

## 4. Policy Enforcement

FlowOS enforces capabilities automatically using the `[RequiresCapability]` attribute on commands.

### Example: StartWorkflowCommand

This command requires `workflow.start`. If a user (e.g., "Manager") attempts to call it without this capability, the API returns:

**403 Forbidden**

```json
{
  "title": "Policy Violation",
  "status": 403,
  "detail": "Policy 'CapabilityCheck' denied execution: Missing required capability: workflow.start"
}
```

### Testing (Dev Only)

You can simulate roles using the `X-Mock-Role` header:

```bash
curl -X POST ... -H "X-Mock-Role: Manager"
```
