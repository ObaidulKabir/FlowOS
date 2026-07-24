# 8. Security: Roles, Capabilities & Policies

Governance is a first-class citizen in FlowOS: a Policy can block an action even when the workflow logic and the caller's role would otherwise allow it.

## Concepts

* **Role** — a named collection of capabilities, scoped to a tenant (e.g. `"Manager"`).
* **Capability** — a granular permission string (e.g. `task.approve`, `workflow.start`).
* **Policy** — dynamic, tenant-specific access control logic layered on top of capability checks.

## Managing roles

### Create a role

**Endpoint:** `POST /api/roles`

```bash
curl -X POST "http://localhost:5183/api/roles" \
  -H "Content-Type: application/json" \
  -H "X-Tenant-ID: 11111111-1111-1111-1111-111111111111" \
  -d '{ "roleName": "Manager" }'
```

```json
{ "id": "4791ff7e-3b57-4f8c-a0cb-0adf74753966" }
```

### Add a capability to a role

**Endpoint:** `POST /api/roles/{roleId}/capabilities`

```bash
curl -X POST "http://localhost:5183/api/roles/<roleId>/capabilities" \
  -H "Content-Type: application/json" \
  -H "X-Tenant-ID: 11111111-1111-1111-1111-111111111111" \
  -d '{ "capabilityCode": "task.approve" }'
```

### Get role details

`GET /api/roles/{roleId}` → `404 Not Found` if the role doesn't exist for the current tenant.

### Recommended capability codes

* `workflow.start` — required to start a new workflow instance.
* `event.publish` — required to publish events (e.g. approvals).
* `role.create` — required to create new roles (admin only, by convention — not currently enforced by an attribute on `RolesController`).
* `task.approve` — domain-specific capability example.

## Capability enforcement: `[RequiresCapability]`

FlowOS enforces capabilities declaratively, via `PolicyEnforcementBehavior<TRequest, TResponse>` — a MediatR pipeline behavior that runs before every command marked `IPolicySecuredCommand`:

```csharp
[RequiresCapability("workflow.start")]
public record StartWorkflowCommand(...) : IRequest<Guid>, IPolicySecuredCommand;
```

If the current user (resolved via `ICurrentUser`/role claims) lacks the required capability, the pipeline throws a `PolicyViolationException`, which `ApiExceptionFilterAttribute` maps to:

**403 Forbidden**

```json
{
  "title": "Policy Violation",
  "status": 403,
  "detail": "Policy 'CapabilityCheck' denied execution: Missing required capability: workflow.start"
}
```

### Simulating roles in development

`MockAuthMiddleware` reads the `X-Mock-Role` header and injects it as a `ClaimTypes.Role` claim:

```bash
curl -X POST "http://localhost:5183/api/workflows/start" \
  -H "X-Mock-Role: Manager" \
  ...
```

## Managing dynamic policies

Policies add a second, dynamic enforcement layer on top of capability checks, evaluated by `IPolicyEvaluator` (`DefaultPolicyEvaluator`) against policies loaded per-tenant by `IPolicyProvider` (`EfCorePolicyProvider`).

### Create a policy

**Endpoint:** `POST /api/policies`

```bash
curl -X POST "http://localhost:5183/api/policies" \
  -H "Content-Type: application/json" \
  -H "X-Tenant-ID: 11111111-1111-1111-1111-111111111111" \
  -d '{ "name": "DenyAll", "conditionJson": "{ \"action\": \"Deny\" }" }'
```

```json
{ "id": "503a5545-6177-48f6-bb8a-ac35bb23a0f5" }
```

`POST /api/policies` returns `409 Conflict` if a policy with the same name already exists for the tenant. `GET /api/policies/{id}` returns `404 Not Found` for a missing or cross-tenant policy.

### Policy enforcement

Once created, a policy is active immediately for its tenant. Example — a `"DenyAll"` policy blocks every secured command:

**403 Forbidden**

```json
{
  "title": "Policy Violation",
  "status": 403,
  "detail": "Policy 'DenyAll' denied execution: DenyAll policy is active."
}
```

*Derived from: `tests/FlowOS.EndToEndTests/DesignConsultancy/DesignConsultancy_PolicyBlock.cs`* — even an admin is subject to policies once one is configured; no state changes occur and the API returns a clear reason.

## Authority ordering

When an actor attempts an action (e.g. `StartWorkflowCommand`), FlowOS evaluates authority strictly top-down. A denial at any layer stops the request; authority never flows upward:

1. **Authentication** — who are you? (`MockAuthMiddleware` / real auth in production)
2. **Capability check** — do you have the key? (`Role`/claims vs. `[RequiresCapability]`)
3. **Policy evaluation** — is it allowed right now? (`IPolicyEvaluator`)
4. **Workflow/State Machine legality** — is this action legal at all? ([Chapter 3](03-state-machines.md))

## What the current `Policy` model actually supports today — read before relying on `ConditionJson`

The `CreatePolicyRequest.ConditionJson` field exists on the wire, and the guide above documents it faithfully — but see [Chapter 15 — Known Limitations](15-known-limitations-and-gaps.md#policy-conditionjson-is-ignored) for the verified, test-proven gap: **`DefaultPolicyEvaluator` currently only checks whether a policy's `Name` is literally `"DenyAll"`.** Any other `Name`, or any content inside `ConditionJson`, is silently ignored by the evaluator today. Don't design a production authorization scheme around arbitrary `ConditionJson` rules yet.

## Where to go next

* [Chapter 9 — WorkflowClass Governance](09-workflow-class-governance.md) for how Roles/Capabilities are declared inside a `WorkflowClassBlueprint`.
* [Chapter 15 — Known Limitations](15-known-limitations-and-gaps.md) for the full, regression-tested list of enforcement gaps.
