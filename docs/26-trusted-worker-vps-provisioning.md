# Trusted Worker VPS Provisioning Checklist

Status: **Preparation only. No production network, database, or deployment change
has been made.** See `25-trusted-agent-worker-rollout.md` for canary gates.

## What the repository establishes

- `docker-compose.prod.yml` runs PostgreSQL on the API host in a Docker bridge
  network without publishing port 5432. The `postgres` service name is only
  resolvable inside that Compose network, not from a second VPS.
- The API registers ASP.NET Data Protection, but no shared, persistent key-ring
  configuration for the API and worker is visible in the repository. Do not
  assume a new worker can decrypt existing tenant/provider credentials.
- The worker executes governed event writes as a trusted tenant identity. A
  queue-only database role is insufficient; review its actual table privileges
  and audit access before granting them.
- Production Compose contains fallback database and mail credentials. Do not
  reuse fallback values on the worker. Check the live configuration securely,
  remove unsafe fallbacks in a separately reviewed change, and rotate any
  exposed credentials that were ever used.

## Provider and existing host discovery (read-only first)

1. Identify the current VPS provider/account, region, API host and its private
   interface, PostgreSQL container name, firewall rules, available private
   network/VPC service, backups, and who owns network changes. Do not paste
   credentials, SSH keys, `.env` contents, or connection strings into tickets
   or chat.
2. Confirm whether the provider can connect a **new** VPS to the API host on a
   private network. If not, design a managed WireGuard/VPN link; traffic over
   the public Internet directly to PostgreSQL is not an acceptable fallback.
3. Inventory where the API's Data Protection keys and provider credentials
   actually live. Test decrypting a non-production encrypted provider binding
   on a second process before using BYO bindings on a worker.
4. Confirm production database migrations include `AgentTaskJobs` and related
   agent tables, and capture the current backup/restore procedure. Do not run
   migrations from the worker.

## Provision the new VPS

1. Start with a separate Linux VPS in the provider's same region and private
   network. Allow approximately 4 vCPU / 8 GiB RAM and 40 GiB disk if the
   image is built there; the first worker container itself is capped at 2 CPU
   / 2 GiB. Validate actual capacity against queue, DB, and provider metrics.
2. Install supported Docker Engine and Compose. Restrict administrative SSH
   to approved operators via the provider firewall and keys; keep the host
   patched, enable backups/monitoring, and grant no public application ports.
3. Give the worker a private route to PostgreSQL. On the production host,
   introduce a separately reviewed Compose override or private proxy that
   binds PostgreSQL **only to the private/VPN interface**, with a firewall
   allowlist for the worker's private address. Never publish `5432:5432` on
   all interfaces or place PostgreSQL behind Traefik.
4. Require a verified PostgreSQL TLS connection (CA and hostname checked),
   dedicated credentials from the operator secret store, and an explicit
   connection pool limit. Audit required read/write privileges before
   provisioning the role; never copy the production superuser password.
5. Provision provider credentials and, where needed, a secure shared Data
   Protection key ring. Do not mount or copy the API container's incidental
   local keys without a key-management and rotation review. Until cross-host
   decrypt is proven, limit the first canary to a provider path that does not
   rely on encrypted BYO bindings. The worker Compose example disables the
   hosted LLM by default. Enable `FLOWOS_HOSTED_LLM_ENABLED=true` only after
   supplying `FLOWOS_HOSTED_LLM_API_KEY` from the approved secret store and
   verifying its endpoint, model, and quota settings on the worker. Do not
   claim real jobs before a usable provider has been verified.

## Verify before touching production processor flags

1. Build or load the **same commit** on API/MCP and worker; validate the image
   on a machine with sufficient build memory. Run the worker Compose file with
   no published ports and `RunExternalAutoPilot=false`.
2. From the new VPS, verify private DNS/routing and certificate validation to
   PostgreSQL; confirm that the public IP cannot reach the database port.
3. Observe one worker claim and finish a controlled Agent-step canary with
   one governed event and execution record. Exercise a slow provider call,
   process termination/reclaim, duplicate detection, and tenant isolation.
4. Keep the API processor enabled during the initial canary. Only after these
   checks pass should the API/MCP switches in the rollout document be changed.
   Roll back by stopping new worker claims, letting claims settle, and restoring
   conservative API processing without deleting queue or event history.

**Next operator inputs:** VPS provider/account and private networking
capabilities, approved access to a new VPS, the database/network owner, and
the secret-management method. No new VM can be created from repository access
alone.
