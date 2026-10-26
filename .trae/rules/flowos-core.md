
Read and follow `AGENTS.md` at the repository root before every task. Key points:

- One change request or issue per branch and PR; never push to `main` (it deploys to production).
- No secrets or production-capable default credentials anywhere, not even as fallbacks.
- Do not change behaviour the task did not ask for; report unrelated bugs instead.
- Tests first; never weaken or delete tests. Model changes need an EF Core migration.
- No personal data or raw tenant IDs in logs or telemetry.
- Documentation and MCP guidance must match the code, in the same PR.
- In C# raw string literals (`"""`), use real line breaks, not `\n`.
