# Trusted Agent Worker: Feature-Branch Test and Rollout

Status: **Feature branch only. Do not merge/deploy until the production gates below pass.**

This is the first, FlowOS-operated/private-PostgreSQL slice of
`24-agent-workload-isolation-plan.md`. It does not provide a client worker
protocol or move the final `PublishEventCommand` caller back to the Law host.
The worker is **trusted**, holds provider credentials, and can commit through
FlowOS policy enforcement; it must never run on an untrusted customer machine.

## Host roles

- API keeps its current processor by default. Setting
  `FlowOS__Agents__Worker__Enabled=false` makes it enqueue only.
- MCP keeps inline execution and external autopilot by default, for a
  non-disruptive migration. Set `FlowOS__Agents__Worker__ExecuteInMcpRequests=false`
  to make `run_agent_task` and `suggest_agent_action` enqueue and briefly wait,
  without claiming on the request thread. If still running after the wait,
  they return `pending: true` and a `jobId`; use `get_agent_task_job` to poll
  tenant-scoped status.
- Set `FlowOS__Agents__ExternalAutoPilotEnabled=false` on the production MCP
  host to stop that background processor there. If used, set
  `FLOWOS_AGENT_EXTERNAL_AUTOPILOT=true` on **one** trusted worker machine
  only, after verifying its tool registry, authorization, and plan execution.
- Set `FlowOS__Agents__AllowGatewayPrefetch=false` on the production MCP
  host. `get_agent_context` then defaults to no prefetch, and explicit prefetch
  requests are rejected; existing integrations requesting prefetch must be
  reviewed before this switch. Packet building itself remains on the gateway.
- `MCP_TRANSPORT=worker` starts a host with no HTTP/stdio endpoint, no schema
  initialization/migrations, and no unrelated API/MCP hosted processors.
  It requires PostgreSQL and reuses the existing trusted governance runtime.

## Prepare a separate worker machine

Follow `26-trusted-worker-vps-provisioning.md` before changing the production
network. The current production Compose keeps PostgreSQL inside its Docker
bridge, so a second VPS has no private database route yet. Cross-host
decryption of tenant/provider secrets is also unverified; do not use
encrypted BYO provider bindings for the first canary until it is proven.

Use `docker-compose.agent-worker.yml` **on the separate machine**. Supply
`FLOWOS_AGENT_DATABASE_URL` via an operator secret store, never a checked-in
`.env`. The PostgreSQL role should have only the permissions needed for
agent queue, context reads, execution records, leases, and governed event
writes; restrict the DB port to the worker's private network and require TLS.
Provide provider credentials and the same tenant secret-protection key ring
through the approved secret deployment mechanism. Keep the worker and API
build versions identical. Database migrations remain an API/deployment
operation, not a worker startup task.
The example Compose file disables the hosted LLM until its credentials have
been configured by the operator. Starting a worker without a usable provider
must not be treated as a successful job-processing canary.

The sample worker has 2 CPUs, 2 GiB RAM, batch size 2, and concurrency 2.
Tune these against measured packet size, DB connections, provider throughput,
and queue age. It exposes no public port. A container started on the existing
production host does **not** isolate host resources.

## Canary switch

1. Verify a production-like PostgreSQL environment, including agent queue
   migrations and an encrypted BYO provider binding if applicable. Run the
   targeted unit/MCP tests and a real queued Agent step, then confirm a
   single event and matching execution record.
2. Start one worker off-host **while** the API processor is still active.
   Both can compete safely for pending jobs; monitor claims, completions,
   expired claims, duplicate events, and memory. Test a model call exceeding
   the original two-minute claim TTL, worker termination, and retry.
3. After canaries complete on the worker, set the API worker flag to false.
   Set MCP inline execution false and disable MCP autopilot or move it to
   one worker if that feature is required.
4. Confirm production API/MCP CPU and memory improve and that no provider
   calls occur there for the agent task path. Watch queue oldest age and
   dead-letter count before scaling out.

## Rollback and known gates

If the off-host worker is unavailable, stop its new claims, wait for
outstanding claims to complete or expire, then re-enable the API processor
with low concurrency. Do not delete job rows, leases, or event history.
Turning off the API processor **before** a working remote worker exists
will leave AI work queued.

Claim heartbeat is implemented for the worker, and auto-commit now uses a
queue-owned PostgreSQL transaction that locks the job row, revalidates
claimant/attempt/expiry, runs `PublishEventCommand`, and rolls back if the
claim expires before commit. The PostgreSQL suite now covers competing
claims, heartbeat renewal, expiry, reconciliation, reclaim, stale-owner
rejection, and transactional rollback on commit expiry (9/9 concurrency
tests pass).
Remaining gates still matter: the separate execution lease is not renewed
during long-running provider calls, and the worker path still needs an
actual off-host canary with provider traffic, termination, and recovery.
The worker-only host started and polled successfully against an isolated,
fully migrated local PostgreSQL database. This did not exercise a provider
call, queued job, worker termination/reclaim, or the container image.
The 83 MCP unit tests and Compose syntax validation pass. A Docker Desktop
build with approximately 2 GiB available compiled several projects but
did not finish in 15 minutes, so it was stopped. No image was verified.
Retry the image build on a host with more build memory, then run a separate
off-host canary before switching any production processors.
The full server-owned finalizer and untrusted/client execution belong to
later phases; this branch must not be advertised as implementing them.
