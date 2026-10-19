# FlowOS Admin Operations and Declarative Capacity Plan

Status: Proposed for approval. Planning only; no deployment authorization.
Date: 2026-10-19

## 1. Outcome and boundaries

Give authorized platform operators a simple way to observe capacity, request
changes, approve their impact, and verify results without editing server
configuration manually. Preserve tenant isolation and the Law/Work architecture.

Review the existing application before expanding its topology. Do not rewrite
the workflow kernel, build a custom cloud orchestrator, or make AI a dependency
of infrastructure recovery.

This plan extends, rather than replaces:

- `24-agent-workload-isolation-plan.md`
- `25-trusted-agent-worker-rollout.md`
- `26-trusted-worker-vps-provisioning.md`

The trusted-worker implementation still permits trusted workers to invoke
governed commits. Process-level server-owned finalization remains a separate
architectural milestone, required before any untrusted worker capability.

## 2. Evidence and unresolved gates

The completed local cluster experiment used current origin/main API binaries
mounted into existing containers, not a newly built and verified API image.

| Check | Observed result |
| --- | --- |
| Healthy routing | 30/30 HTTP 200, distributed 10/10/10 |
| Stopped-node failover, four-second deadline | 7/18 requests timed out |
| Subsequent eight-second-deadline sample | 12/12 HTTP 200; some retries approximately three seconds |
| Restart and rejoin | 30/30 HTTP 200, distributed 9/11/10 |

The deadline samples occurred sequentially as passive failure detection evolved;
they are not a controlled comparison of client timeouts. Re-run from equivalent
starting conditions before attributing differences solely to the deadline.

These checks establish basic routing behavior, not business-command correctness,
production capacity, multi-host readiness, or full availability.
The test containers and network were removed; the test database volume was
retained. The deleted temporary current-binary override must not be restored
implicitly.

Current deployment gates include a verified release image, failover behavior,
cross-host secret protection, private database access, background-service
coordination, and the off-host worker canary.

## 3. Target responsibilities

| Component | Responsibility | Must not do |
| --- | --- | --- |
| Business API / Law | Authentication, policy, state transitions, governed events | Run heavy agent loops when isolation is enabled |
| MCP gateway | Authorized tools, bounded context and asynchronous job status | Bypass approvals or perform infrastructure mutations directly |
| Trusted workers / Work | Claim bounded work and execute within existing governance | Acquire unrestricted infrastructure authority |
| Admin Operations module | Read models, capacity policy, proposals, approvals, audit | Run Terraform or SSH inside HTTP requests |
| External deployment runner | Apply approved changes and report authenticated results | Apply an unapproved or changed plan |
| Terraform and Ansible | Provision resources and configure hosts from versioned definitions | Treat ordinary Compose networking as a cross-host network |
| Monitoring | Supply fresh, attributable health and capacity observations | Treat missing telemetry as proof that a node has stopped |

Start with an Operations module in the existing layered application, with a
separate execution process for infrastructure automation. A separate operations
service is optional later; it is not a prerequisite for the first dashboard.

Use Git as the source of approved infrastructure intent. The database holds
proposal, execution, observation, and audit records, not a competing editable
infrastructure specification. The UI creates a versioned change proposal.
Generate provider outputs and host inventory rather than maintaining duplicate
IP lists manually.

## 4. Phase A: Architecture and scalability review

Deliver a runtime responsibility map, a scalability findings register with
file references, and architecture decisions covering:

1. Every API/MCP provider call, context expansion, and background processor.
2. Outbox, timer, agent-job and execution-lease ownership under multiple replicas.
3. Idempotency and tenant authorization during retry, failover, and delayed work.
4. Shared authentication material, Data Protection, configuration revisions,
   caches, local files, sessions, and MCP/SSE connection behavior.
5. PostgreSQL connection budgets, queue polling load, migration ownership,
   backups, and recovery objectives.
6. Load-balancer readiness, retry eligibility, draining, and failure detection.
   Do not blindly retry non-idempotent commands to reduce timeout counts.
7. Platform authorization boundaries and the current deployment pipeline,
   including unsafe credential defaults, pruning, and builds on production.
8. Actual production topology and failure domains, collected with approved
   read-only access. Repository configuration is not proof of live configuration.

Agree on measured API latency/error objectives, client deadlines, queue-wait
objectives, recovery targets, and cost ceilings. Do not invent universal
production thresholds from the local health-endpoint samples.

Acceptance: every replicated responsibility has a documented ownership model;
blockers have a test and remediation path; unresolved issues explicitly block
the affected rollout. No runtime changes are required to complete this review.

## 5. Phase B: Reliability and deployment prerequisites

Implement narrowly scoped fixes identified in Phase A before broad scale-out.

- Build API/MCP/worker images in CI; identify releases by immutable digest.
  Build frontend assets once per release, without embedded secrets.
- Verify those exact images in staging; do not substitute mounted publish output.
- Separate migrations into a designated deployment operation; validate compatible
  rolling upgrades and avoid concurrent replica startup migrations.
- Remove unsafe credential defaults and unnecessary public ports. Rotate any
  exposed credentials that were used through the approved secret mechanism.
- Establish private/VPN connectivity, verified database TLS, connection limits,
  and cross-host key protection.
- Tune and test readiness, gateway detection, timeouts, and draining against
  agreed client behavior. Increasing client timeouts is not the only remedy.
- Validate worker lease/commit safety and off-host processing using the existing
  rollout checklist. Keep the final functioning processor until canary success.
- Replace broad production cleanup with scoped retention. Preserve database
  volumes, backups, and rollback images.

Test sequentially on the constrained Windows host. Give startup a configurable
readiness budget with progress and diagnostic checks; do not classify slow
startup alone as a bug or wait indefinitely without evidence. Run larger builds
on approved CI/staging capacity if local resources are insufficient.

Acceptance: verified images and relevant functional/concurrency/security suites
pass; failure/recovery tests meet agreed objectives. Tests of API replication
and worker isolation have separate pass/fail decisions.

## 6. Phase C: Read-only Admin Operations MVP

Add an Operations area without infrastructure mutation controls:

- Overview: readiness, p95 latency/error rates, oldest queue age, dead letters,
  database connections, CPU/memory, and active incidents.
- Resource pools: API, MCP, and worker roles; node identity, release digest,
  desired/observed capacity, heartbeat age, and draining state.
- Change history: deployed revision and available external deployment status.
- Clearly label unknown, stale, unavailable, starting, ready, and unhealthy.

Introduce authenticated telemetry collection with bounded polling, caching,
retention, and cardinality. Store operational summaries rather than duplicating
a metrics database. Monitoring load must not degrade business endpoints.

Use explicit platform permissions, beginning with `Operations.View`; a tenant
administrator does not inherit platform infrastructure visibility. Decide which
tenant-specific usage summaries can be exposed separately.

Acceptance: authorization tests reject tenant-only and unauthenticated access;
dashboard tests cover empty/stale/partial-failure states; browser checks verify
the displayed status comes from real observations, not static cluster topology.

## 7. Phase D: Approved declarative capacity changes

Proposed model: ResourcePool, CapacityPolicy, CapacityChangeRequest,
InfrastructurePlan, DeploymentRun, and NodeObservation. Keep domain rules
independent of provider SDKs and test them before implementing adapters.

Admin flow:

1. Select environment and pool; request capacity within a configured limit.
2. Validate permissions, approved instance profiles, quotas, dependency capacity,
   and estimated cost. Show assumptions and uncertainty in cost estimates.
3. Produce a Git change and infrastructure plan with a digest and expiry.
4. An authorized approver reviews the exact plan, impact, and recovery procedure.
5. An external runner applies the approved revision, verifies readiness, and
   admits new capacity only after checks pass.
6. Report observed results and audit evidence. Partial failures require
   reconciliation or an explicit compensating action.

Suggested lifecycle:

`Requested -> Planning -> AwaitingApproval -> Approved -> Applying -> Verifying -> Completed`

Also model Rejected, CancelledBeforeApply, Failed, RecoveryRequired, Recovering,
and Recovered. Cancellation after apply begins is not a guaranteed rollback.
Never assume a schema/data change can be reversed by reverting Git.

Controls:

- Separate propose, approve, and apply permissions; require independent approval
  for production changes and step-up authentication for sensitive actions.
- Bind approval to actor, environment, exact plan digest, and validity window.
- Use short-lived scoped runner credentials, authenticated callbacks, replay
  protection, idempotency keys, and one active apply per environment.
- Protect Terraform state with access control, encryption, versioning, and
  locking. Reject stale plans after drift or intervening applies.
- Keep provider secrets out of Git, browser state, prompts, plans exposed to
  users, and logs. Restrict registration to approved networks and providers.
- Drain HTTP traffic and active worker claims before scale-in. Enforce minimum
  capacity, cooldowns, and maximum change size; treat DB changes separately.
- Provide an audited external recovery procedure if FlowOS or its DB is down.
  Reconcile emergency changes back into Git before normal automation resumes.

Prefer a provider-managed load balancer when suitable. Otherwise, explicitly
design gateway redundancy and ingress failover; a second gateway alone does
not make ingress highly available. Leave database HA as a separate reviewed
design rather than silently including it in API scaling.

Acceptance: duplicate requests/callbacks, stale approvals, unauthorized changes,
partial provisioning, runner crashes, drift, draining, and recovery are tested.
The existing production deployment job must not race the new runner.

## 8. Phase E: Guarded scaling, then optional intelligence

Start with recommendations only. Scale worker pools from queue age and measured
throughput, subject to provider limits, tenant fairness, memory, and DB budgets.
Scale API pools from latency, concurrency, errors, and available dependency
capacity. High CPU alone is not sufficient evidence.

After an agreed observation period, allow deterministic automatic changes within
pre-approved policy envelopes. Enforce min/max capacity, budget caps, cooldowns,
bounded steps, stabilization windows, readiness gates, and a manual pause.
Suspend automation on stale telemetry or unresolved deployment failures.

AI is optional and advisory: summarize incidents, explain recommendations, and
draft proposals from redacted evidence. It holds no infrastructure credentials
and cannot override policy. Do not send tenant content to an operational advisor.
Evaluate recommendation quality, cost, and false actions before expanding use.

Acceptance: replay/simulation tests cover overload, recovery, oscillation,
missing metrics, dependency saturation, budget exhaustion, and scale-in safety.
Do not begin this phase before manual approved changes work reliably.

## 9. Test and release gates

| Gate | Required evidence |
| --- | --- |
| Architecture | Ownership map, risk decisions, agreed objectives |
| Release artifacts | Exact image digests built and tested |
| Replicated business behavior | Authentication/tenant isolation, idempotent writes, shared config, timers/outbox, MCP/SSE recovery |
| Worker isolation | Off-host provider task, slow execution, kill/reclaim, single governed outcome |
| Network and persistence | Private DB, secret decryption, connection budgets, backup restore |
| Admin security | Platform permissions, approval integrity, no secret exposure |
| Infrastructure execution | Plan validation, drift/concurrency protection, partial failure recovery |
| Production canary | Small reversible change, metrics, operator approval, recovery rehearsal |

Routing health checks remain smoke tests. Include authenticated business
requests and workloads under AI saturation. Run fault tests from equivalent
initial states, recording timings and actual serving nodes.

## 10. Work separation and rollout

- Preserve unrelated local changes and the existing worker feature branch.
- Implement architecture-approved work in separate branches/worktrees based on
  current main. Suggested slices: reliability fixes, operations visibility,
  declarative deployment, approved admin changes, and guarded scaling.
- Bring in worker changes explicitly through reviewed dependencies; do not
  silently merge all worker changes into the operations work.
- Add relevant tests with each behavior change. Update MCP definitions, tool
  descriptions, async result semantics, permissions, and tests in the same
  change whenever policy or agent-facing behavior changes.
- First deploy only the required reliability/visibility changes. Validate one
  off-host worker and one additional API node as separate canaries, never as a
  single unobservable cutover.
- Do not migrate PostgreSQL or replace the working ingress in the first capacity
  canary. Plan those changes separately when required.
- Each production apply needs explicit approval. This document authorizes no
  VM creation, spending, network change, merge, push, or deployment.

## 11. Inputs and immediate next action

Phase A can begin from repository evidence without cloud credentials.
Before provider-specific implementation, confirm:

1. VPS provider/region and private-network options.
2. Approved staging and monthly infrastructure budgets.
3. Registry, secret store, Terraform state backend, and operator ownership.
4. Production approvers, maintenance windows, and recovery objectives.
5. Whether to use a managed load balancer or operate redundant gateways.

Do not request secrets in chat.

Immediate next action after plan approval: perform Phase A and return the
architecture findings, topology/responsibility map, and prioritized test-backed
remediation list. Select the smallest next implementation slice from that
evidence; do not start by building an autoscaler or provisioning VPS nodes.
