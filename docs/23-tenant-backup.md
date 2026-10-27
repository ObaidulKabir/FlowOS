# 23. Tenant backup and site restore

A tenant can download a **point-in-time backup** of its Designed App and its running instances, then restore that file on another FlowOS site.

A DesignedApp is the WorkflowClass blueprint, its Business Context, and its AI Context. The backup also includes the compiled workflow and state-machine definitions those apps use, every workflow instance, the instance context snapshot, the tenant event log, action history, and timer jobs.

## Download and restore

| Call | Who | Result |
| --- | --- | --- |
| `GET /api/tenants/{tenantId}/backup` | That tenant, or a platform admin, with header `X-FlowOS-Backup-Confirm: download` | `flowos-tenant-backup-{tenantId}-{utc}.json` |
| `POST /api/tenants/{tenantId}/backup/restore` | That tenant, or a platform admin, with header `X-FlowOS-Backup-Confirm: restore` | Loads the JSON body into `{tenantId}` |

A call without the matching header is `BACKUP-CONFIRMATION`. The dashboard **Site backup** control asks for that acknowledgement: a checkbox before download, and a file preview plus the word `RESTORE` before restore.

The file format is `flowos-tenant-backup`, format version 1. It records `flowOsVersion`, `takenAtUtc`, and `sourceTenantId`.

Restore keeps the original record ids and rewrites `tenantId` to the destination. Restore onto a site that already has any of those workflow classes, business contexts, instances, or events is refused (`BACKUP-CONFLICT`). Use another site, or an empty tenant.

## Encryption (CR-15)

A backup holds the tenant's AI provider settings, running-instance data and the event log, so it is encrypted with a passphrase you choose.

* **Download.** Send the passphrase (at least 12 characters) in the `X-FlowOS-Backup-Passphrase` header; the dashboard asks for it twice. The file (`…encrypted.json`, format `flowos-tenant-backup-encrypted`) is AES-256-GCM encrypted with a key derived from the passphrase (PBKDF2-SHA256, 600,000 iterations, random salt and nonce). Only the FlowOS version and the time it was taken stay readable, and they are tamper-proof.
* **Restore.** Send the same passphrase in the same header. A wrong passphrase or a changed file returns `BACKUP-DECRYPT-FAILED`; a missing one returns `BACKUP-PASSPHRASE-REQUIRED`. Older unencrypted backups can still be restored.
* **Required outside Development.** `FlowOS:Backup:RequireEncryption` defaults to `true` outside Development: a download without a passphrase returns `BACKUP-PASSPHRASE-REQUIRED`. Set it to `false` only on a sandbox.
* **Keep the passphrase separately.** FlowOS never stores it. Without it the file cannot be restored.

## FlowOS version

The destination must be the same major release and must not be older than the backup's `flowOsVersion` or any `flowOsVersion` stamped on a blueprint, business-context revision, or AI Context binding. A newer or different-major backup returns `BACKUP-VERSION`. See [Chapter 22](22-designed-app-flowos-version.md).

## What is left out, and why

The file is confidential. It contains the tenant's AI provider configuration, including keys the tenant stored, because the other site cannot run those steps without them. It does **not** contain:

- tenant API keys or user passwords
- the platform hosted-LLM key
- outbox messages

Pending timers are in the file. On restore they are marked processed. The destination has the instance state and history, and it does not fire the same timer or webhook again while the source site might still be running. Re-arming side effects is a later, explicit choice.

This is an application snapshot taken when the download is requested. It is not a database write-ahead-log backup, and it does not roll the tenant back to an earlier minute on the same site.

## MCP

Agents learn this policy from MCP `initialize` instructions. The full steps are prompt `backup_tenant_for_another_site` and resource `flowos://guides/tenant-backup`.

There is no MCP tool that returns or accepts the file. The backup is a download because it contains instances, the event log, and the tenant's AI provider configuration.
