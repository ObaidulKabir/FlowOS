# 8. Security: Roles, Capabilities & Policies

Governance is a first-class citizen in FlowOS: a Policy can block an action even when the workflow logic and the caller's role would otherwise allow it.

## Concepts

* **Capability** — the **execution gate**. A granular permission string (e.g. `expense.approve`, `event.publish.EVT-APPROVE`, `workflow.start`). If the caller holds the remapped required capability, they may perform the activity even when their role name is not listed on the step.
* **Role** — a tenant-scoped **bag of capabilities** plus an **inbox / assignment label** (e.g. `"Manager"`). Tenant roles grant capabilities and route waiting HumanTasks.
* **Business context** — remaps names only (`roleOverrides`, `capabilityOverrides`). It never creates tenant roles or grants permissions.
* **Policy** — dynamic, tenant-specific access control logic layered on top of capability checks.

Workflow activities declare `requiredCapabilities` on the HumanTask / human event in the workflow definition. Inbox still uses remapped `requiredRoles`. Tenant `POST /api/roles` + capabilities remains the live grant store.

Runtime `publish_event` and `complete_task` share one `AuthorizeActivity` gate: after context remaps, the caller may act if their tenant-role permissions intersect the compiled required capabilities. Empty compiled capabilities fail closed for HumanTask / human events once the pack declared them; System/Default auto-routes stay internal. Admin still bypasses. `event.publish` remains a wildcard for `event.publish.*`. Policies run after this gate.

> **This chapter is about FlowOS's own tenant/IAM roles** — who can log into a tenant and call FlowOS
> APIs (`RolesController`, JWT `role`/`roles` claims, `[Authorize(Roles = ...)]`). A `WorkflowClassBlueprint`
> also declares `roles[]`/`capabilities[]`, but those describe the *application the workflow was designed
> to model* (e.g. "Approver" in an order-approval process) — a completely separate, business-context-scoped
> vocabulary that is never written into this chapter's Role/TenantUserRole tables. See [Chapter 17](17-workflow-context-bindings.md#business-context-roles) for how those are declared and resolved per instance.

> **Capability means permission, not integration.** An outbound HTTP worker (e.g. `payment.refund.v1`) is a
> **connector**, registered with `register_connector` and invoked by an `InvokeConnector` step action. It has
> nothing to do with role permissions. The older `capability binding` / `InvokeCapability` wording still works —
> see [Chapter 13](13-mcp-and-ai-agent-integration.md).

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

### List roles

`GET /api/roles` returns every role in the current tenant with its capability codes:

```json
[{ "id": "4791ff7e-...", "name": "Manager", "capabilities": ["event.publish.EVT-APPROVE", "workflow.read"] }]
```

### Assign roles to users

A user's `role` column is their **primary** role. Additional roles come from assignments:

* `POST /api/roles/{roleId}/users/{userId}` — assign (idempotent).
* `DELETE /api/roles/{roleId}/users/{userId}` — revoke, `404` when no assignment exists.
* `GET /api/roles/users/{userId}` — list assigned role names.

On login the JWT carries the primary role in `role` and the full set in `roles`. `ValidateToken` emits one
`ClaimTypes.Role` claim per entry (deduplicated), so `ICurrentUser.Roles` and `[Authorize(Roles = ...)]` see all
of them. Capability resolution unions the permissions of every role the caller holds.

### Roles declared by a WorkflowClass are never written here

A `WorkflowClassBlueprint` declares `roles[]` and `capabilities[]`, but activating a context binding
(`POST /api/context-bindings/{id}/activate`) does **not** create or modify anything in this chapter's
Role/TenantUserRole tables. Those declarations describe the modeled application's own roles, not FlowOS
tenant roles, and they are compiled onto the runtime `WorkflowDefinition` as inert declarative metadata
instead. See [Chapter 17](17-workflow-context-bindings.md#business-context-roles) for how their membership
is resolved per running workflow instance.

### Recommended capability codes

* `workflow.start` — required to start a new workflow instance.
* `event.publish` — required to publish events (e.g. approvals).
* `iam.read` / `iam.manage` — inspect and mutate tenant IAM roles.
* `task.approve` — domain-specific capability example.

API-key scopes use colon notation (`workflow:start`) and never expand reserved role grants. A scoped SalesFlow key can start workflows only when the tenant `ApiKey` role already holds `workflow.start` and the key includes `workflow:start` or `*`.

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

## Tenant Registration, Login & Email Verification

FlowOS provides multi-tenant onboarding with mandatory email verification and JWT token issuance.

### 1. Register a Tenant Organization

**Endpoint:** `POST /api/auth/register-tenant`

```bash
curl -X POST "http://localhost:5183/api/auth/register-tenant" \
  -H "Content-Type: application/json" \
  -d '{
    "tenantName": "Acme Corp",
    "email": "owner@acmecorp.com",
    "password": "SecurePassword123!",
    "fullName": "Jane Acme"
  }'
```

Returns `201 Created`. The tenant is placed in `PendingVerification` status, and an official verification email is automatically dispatched from **`admin@flowosbd.com`** (FlowOS Admin) containing a 24-hour verification token.

### 2. Verify Email Address

**Endpoint:** `POST /api/auth/verify-email` (or `GET /api/auth/verify-email?token=...&email=...` via browser)

```bash
curl -X POST "http://localhost:5183/api/auth/verify-email" \
  -H "Content-Type: application/json" \
  -d '{
    "email": "owner@acmecorp.com",
    "token": "<verification_token>"
  }'
```

Upon successful verification, the tenant status is activated (`Active`), enabling tenant members to log in and access protected workflows.

### 3. Login & Obtain JWT Token

**Endpoint:** `POST /api/auth/login`

```bash
curl -X POST "http://localhost:5183/api/auth/login" \
  -H "Content-Type: application/json" \
  -d '{
    "email": "owner@acmecorp.com",
    "password": "SecurePassword123!"
  }'
```

Response:
```json
{
  "ok": true,
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "tokenType": "Bearer",
  "expiresIn": 86400,
  "user": {
    "id": "c1f7a07c-9b8d-4e2b-9e4a-1a2b3c4d5e6f",
    "email": "owner@acmecorp.com",
    "fullName": "Jane Acme",
    "role": "Admin",
    "tenantId": "e2f8b18d-0c9e-4f3c-8a5b-2b3c4d5e6f7a",
    "tenantName": "Acme Corp",
    "isEmailVerified": true
  }
}
```

Use the returned token in subsequent API requests via the header `Authorization: Bearer <token>`. FlowOS's middleware automatically validates the signature, extracts the user's role and tenant ID, and authorizes requests.

### 4. Resend Verification Email

If the verification token expires or is lost:

**Endpoint:** `POST /api/auth/resend-verification`

```bash
curl -X POST "http://localhost:5183/api/auth/resend-verification" \
  -H "Content-Type: application/json" \
  -d '{ "email": "owner@acmecorp.com" }'
```

### 5. Inspect Authenticated User Profile

**Endpoint:** `GET /api/auth/me`

```bash
curl -X GET "http://localhost:5183/api/auth/me" \
  -H "Authorization: Bearer <token>"
```

---

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
