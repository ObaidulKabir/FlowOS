I will provide a complete set of `curl` commands to test the **OrderApprovalWorkflow** end-to-end.

### Plan
1.  **Environment Setup**: Define variables for `BASE_URL` and `TENANT_ID`.
2.  **Start Workflow**: Use `POST /api/workflows/start` with `workflowName: "OrderApprovalWorkflow"`.
3.  **List Workflows**: Use `GET /api/workflows` to find the new Instance ID.
4.  **Check Status**: Use `GET /api/workflows/{id}` to verify it is at the `ReviewStep`.
5.  **Approve Order**: Use `POST /api/events/publish` to send `EVT-ORDER-APPROVED`.
    *   *Note*: The configured workflow expects this specific event to move from `ReviewStep` to `FinanceStep`.
6.  **Verify Completion**: Check status again to see it moved to `FinanceStep` (or End).

I will output these commands in a format you can copy-paste directly into your terminal (Git Bash / PowerShell).