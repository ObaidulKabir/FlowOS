I will create an endpoint `GET /api/admin/workflows` to list all running workflows for the current tenant.

### Plan
1.  **Create Query & DTO**:
    *   Create `AdminWorkflowSummaryDto` (lighter version of DetailDto, without Timeline).
    *   Create `GetAdminWorkflowsQuery` (optional filters: status, definitionId).
2.  **Implement Handler**:
    *   Update `AdminQueryHandlers` to handle the new query.
    *   It will query `WorkflowInstances` filtered by TenantId.
3.  **Update Controller**:
    *   Add `[HttpGet("workflows")]` to `AdminController`.

### New Files
*   `src/FlowOS.Application/DTOs/Admin/AdminWorkflowSummaryDto.cs`
*   `src/FlowOS.Application/Queries/Admin/GetAdminWorkflowsQuery.cs`

### Modified Files
*   `src/FlowOS.Application/Handlers/Admin/AdminQueryHandlers.cs`
*   `src/FlowOS.API/Controllers/AdminController.cs`