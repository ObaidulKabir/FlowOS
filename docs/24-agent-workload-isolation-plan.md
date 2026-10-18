# Draft: Isolate AI Workloads from the FlowOS Production Host

Status: **Trusted-worker first slice in `feature/agent-workload-isolation`; not approved for merge or deployment.**

## Goal and boundaries

Keep the production API/MCP hosts responsive when many tenants run AI agents or expand context concurrently. Use a durable, bounded work queue to distribute expensive processing to other machines. The state machine, workflow governance, tenant isolation, and event log remain authoritative in FlowOS.

This plan is about *compute placement*, not migrating tenant data to an in-memory database or exposing PostgreSQL to customer laptops. A second container on the same production machine does not relieve host-level CPU, RAM, or network exhaustion; the first worker must run on a **separate machine**.

## Verified starting point

- `src/FlowOS.Api/Program.cs` unconditionally registers `AgentTaskProcessorService` in the API process. It stages/claims `AgentTaskJobs` from PostgreSQL through `AgentTaskQueue` (`FOR UPDATE SKIP LOCKED`).
- `AgentTaskRunner` builds a `DecisionPacket`, prefetches tools, runs the provider, and can call `PublishEventCommand` from the same worker process. Moving this runner to a trusted worker therefore also moves a **privileged commit caller**. The kernel still validates the transition, but the old claim that only the API process commits is not true today.
- `AgentTools.RunAgentTask` and `SuggestAgentAction` use `EnqueueAndExecuteAsync`, which can claim and execute on the **MCP request thread**, even when the API background processor is disabled. MCP also registers `ExternalAIAgentAutoPilotService`.
- The existing external change feed is an **outbox-change feed**, not a secure remote `AgentTaskJobs` claim/result protocol. It must not be treated as the latter without a new contract and authorization model.
- Job claims and execution leases have finite TTLs; the runner currently uses a two-minute execution lease. Long model/tool calls need renewal or fencing before adding more workers.
- `docker-compose.prod.yml` currently places API, MCP, dashboard, and PostgreSQL on one deployment host; the deploy workflow runs that compose file there.

## Target topology

```text
Users / apps -> API + MCP gateways on production host -> PostgreSQL queue
                   |                                    ^
                   | authoritative commands/events       |
                   +-- validation, auth, governance       |
                                                        private network
                                              trusted AI worker machine(s)
                                              packet build, prefetch, LLM

Untrusted tenant/desktop worker -> authenticated HTTPS task gateway
                                -> bounded context + suggested action
                                -> FlowOS validates and commits/parks
```

**Trusted worker**: FlowOS-operated, private-network access to PostgreSQL and provider secrets, no public HTTP exposure. Initial split can reuse `AgentTaskCoordinator` and its existing governed command path. It reduces host load but does **not** yet isolate commit authority at the process level. Harder separation would require a subsequent result-submission service on the Law host.

**Tenant/client worker**: untrusted relative to the control plane. No database connection, platform provider key, tenant plugin secret, or direct queue table access. It only receives an authorized, bounded context and proposes an action through a server-side validation endpoint. Neither the existing external change feed nor `get_agent_context` alone provides this protocol.

## Work plan and acceptance gates

### 0. Inventory and contain (before moving compute)

1. Enumerate every synchronous/in-process provider execution path: API processor, MCP `run_agent_task` / `suggest_agent_action`, MCP external autopilot, context tools with `prefetch=true`, and any other direct LLM/planner calls. Record which endpoints must become enqueue-and-return versus bounded wait-only.
2. Establish baseline API/MCP p95 latency, memory, CPU, PostgreSQL connection count, job queue depth/oldest age, provider concurrency, payload size, and dead-letter rate. Define operating thresholds from observed production capacity rather than inventing universal numbers.
3. Add bounded limits: per-process and per-tenant in-flight jobs, queue admission/backpressure, packet/history/prefetch size and time budgets, provider/tool deadlines, and rate limits on context endpoints. Apply authorization and redaction before serialization. An overloaded service should reject/defer new AI work, not slow down workflow commands.

**Gate:** a load test shows workflow start/publish/health remain responsive at the agreed AI saturation level, with clear overload responses and no cross-tenant context exposure.

### 1. Trusted off-host queue workers (first deployment)

1. Add a validated execution role/config (API enqueue-only; worker claim/execute). Default existing development behavior deliberately; configure production API explicitly to disable its agent processor. Do not switch off the last processor until the external worker is healthy and claiming.
2. Create a dedicated worker host using shared application/persistence registration without starting public controllers, API/MCP servers, database migrations, seeders, outbox/timer processors, or unrelated background services. Deploy it on another VM/container host with a private PostgreSQL connection, restricted network access, scoped operational credentials, and resource limits. Verify the production host is not running a second copy.
3. Change MCP agent tools so a request **cannot claim or run** agent work locally in production: enqueue and return job ID/status, or wait *only* for a worker-completed result within a short bounded timeout. Update tool response semantics/docs and tests; keep simulation separate. Independently gate/move MCP autopilot and other in-process provider entry points.
4. Add lease renewal/heartbeat and a fencing token or equivalent claim-version check for **both** job ownership and execution ownership. A timed-out worker must not commit after its lease expires; completion, retry, audit, and commit must be idempotent under crash/reclaim. Exercise delayed LLMs longer than the current two-minute TTL.
5. Bound worker concurrency and PostgreSQL connection pool per replica. Scale worker replicas on queue age and measured memory/provider throughput, not web request count; prevent one tenant consuming all slots.

**Gate:** with API and MCP AI execution disabled, a waiting Agent step completes via the remote worker; killing it mid-run results in one governed outcome after recovery. API/MCP CPU and memory stay inside agreed budgets. No production secrets appear in packets or logs.

### 2. Move commit authority to Law host (recommended before BYO workers)

Split the trusted runner into **prepare/execute/suggest** and a server-owned **validate/commit-or-park** path. The Law-side finalizer reloads current instance, pinned definition, tenant authorization, auto-commit policy, quota and job status, then publishes with a stable idempotency key. A stale step or expired/replayed claim yields no commit. The worker never receives a credential that can call arbitrary `publish_event`.

**Gate:** race, retry, duplicate-result, and cross-tenant tests prove a worker suggestion cannot bypass policy or cause two events. Record audit provenance for worker ID, job ID, execution ID, and decision without storing provider secrets.

### 3. Tenant-owned / client machines (opt-in, later)

1. Design a separate remote worker API: tenant-scoped registration, capability-based job eligibility, authenticated claim, short-lived lease plus heartbeat, bounded versioned context, and `submit_agent_suggestion` with job/lease identity and idempotency key. Poll outbound HTTPS only; never expose PostgreSQL. Existing external change feed remains a separate feature.
2. Minimize context: explicit allowlisted fields, capped history, no raw credentials, and per-tool authorization and data budgets. Do not assume redacting `DecisionPacket.Provider` removes secrets from canonical context, event payloads, prompt audits, or tool responses.
3. Route suggestions through the Law-side finalizer from phase 2. On disconnect, expiry, or denial, retry or park for human review according to policy. Add tenant opt-in, revocation, quotas, audit, and residency controls.
4. Desktop/Tauri can run local models; browsers may use an authenticated HTTPS gateway but cannot be assumed reliable background workers. Do not put privileged API keys in browser storage.

**Gate:** malicious/stale/client-disconnect tests, tenant-isolation tests, audit and privacy review, and a small opt-in pilot pass before broad availability.

## Deployment order and rollback

1. Release queue/lease safety and observability first. Keep the existing API processor on with conservative limits while validating.
2. Start **one** off-host trusted worker with low concurrency. Confirm it uses the same PostgreSQL, observes migrations already applied by the designated migration job, and processes canary jobs.
3. Switch production API processor off, then switch MCP synchronous execution/autopilot off or to worker-only behavior. Verify no in-process provider calls remain on production hosts; add worker replicas gradually.
4. Roll back by stopping new worker claims and re-enabling the API processor at conservative concurrency **only after** outstanding claims expire or have been reconciled. Preserve queue rows, execution history, idempotency keys, and event audit. Never roll back by clearing the queue or bypassing Law checks.

Operational alarms: worker heartbeat absent, oldest pending age above SLO, repeated claim expiry, dead letters, DB pool saturation, provider timeouts, API/MCP p95 and RSS above budget, per-tenant starvation, and duplicate-commit attempts.

## Decisions needed before approval

1. Can trusted worker machines access PostgreSQL over a private network initially, or must all remote execution use an HTTP gateway from day one? The latter requires the phase-2 finalizer and a claim/result API before off-host rollout.
2. Are `run_agent_task` and `suggest_agent_action` allowed to become asynchronous (job ID + status polling), or must they retain a short wait-only response for existing MCP clients?
3. Should tenant-owned workers be a later opt-in product feature, with FlowOS-hosted workers as the first production release?
4. What are the measured production CPU/RAM/DB connection budgets and acceptable waiting-job SLO for the first rollout?

The trusted-worker first slice is implemented only on the feature branch.
This draft does not authorize a merge or any production configuration change.
