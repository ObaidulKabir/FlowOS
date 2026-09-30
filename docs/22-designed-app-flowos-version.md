# 22. DesignedApp and the FlowOS release

A **DesignedApp** is three artifacts authored against one FlowOS release:

1. **WorkflowClass blueprint** — the process template.
2. **Business Context** — the context-binding revision (entity, events, roles, payload mapping).
3. **AI Context** — prompt, provider, and tool bindings.

Those three use whatever MCP tools, schema fields, and runtime behavior the host exposes at the moment they are saved. A DesignedApp is therefore **FlowOS-version dependent**. The blueprint's own `version` (`1.0.0`, `1.1.0`, …) is a different number: it versions the process, not the platform.

## The stamp

`FlowOsRelease.Version` in `src/FlowOS.Domain/FlowOsRelease.cs` is the only platform release. The scheme is **Major.Minor.Build**. The current release is **1.2.2**. The landing page, dashboard rail, `/health`, and MCP discovery all show this value.

Each artifact stores `flowOsVersion` when it is created and again when a draft or binding is edited:

| Artifact | Where `flowOsVersion` is stored |
| --- | --- |
| WorkflowClass blueprint | `WorkflowClasses.FlowOsVersion` |
| Business Context | `WorkflowContextBindingRevisions.FlowOsVersion` |
| AI Context | `PluginBindings.FlowOsVersion` |

MCP `initialize` reports the same value as `serverInfo.version`. Discovery and `/health` report `flowOsVersion`. Rows that existed before this stamp are backfilled to **1.1.0**, the last MCP server release before DesignedApps recorded a dependency.

## Moving a site

Before copying a DesignedApp from one deployment to another, download the tenant backup in [Chapter 23](23-tenant-backup.md). Agents load prompt `backup_tenant_for_another_site` or resource `flowos://guides/tenant-backup`. Then read `flowOsVersion` on the blueprint, the business-context revision, and the AI-context bindings. The destination host must be:

- the **same major** version, and
- **not older** than the newest stamp on those three artifacts.

| Artifact stamp vs destination | Meaning |
| --- | --- |
| `Current` | Authored on this exact release. |
| `OlderCompatible` | Authored on an older build or minor of this major. This host can run it. |
| `NewerThanHost` | Authored on a newer FlowOS. Do not move it here. |
| `IncompatibleMajor` | Different major. Do not move it here. |

Workflow class responses include `flowOsVersion` and `flowOsCompatibility` for the blueprint. Compare the business-context and AI-context stamps the same way; a DesignedApp is only as portable as its newest stamp.

## When to bump

Bump the **build** of `FlowOsRelease.Version` for every contract change, including the smallest one:

- an MCP tool is added, removed, or its arguments or errors change
- the blueprint schema changes
- business-context mapping changes
- AI Context prompt, provider, or tool binding changes

Bump **minor** when the change is additive and older DesignedApps on the same major still run. Bump **major** when older DesignedApps cannot be loaded safely.

After a bump, update this chapter so it still names `FlowOsRelease.Version`, and update the static `flowOsVersion` fields in the well-known MCP manifests. New and edited DesignedApps then record the new release. Already saved artifacts keep the release they were authored against.
