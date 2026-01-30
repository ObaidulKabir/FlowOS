I will implement the API to retrieve running workflows in the `WorkflowsController`.

### Plan
1.  **Create DTO**: `WorkflowSummaryDto` (Public facing summary).
2.  **Create Query**: `GetWorkflowsQuery` with optional `Status` filter.
3.  **Implement Handler**: Create `WorkflowQueryHandlers.cs` to handle the query using EF Core.
4.  **Update Controller**: Add `GET /api/workflows` endpoint to `WorkflowsController` that invokes the query, filtering for `Running` status by default (or allowing query params).

### New Files
*   `src/FlowOS.Application/DTOs/WorkflowSummaryDto.cs`
*   `src/FlowOS.Application/Queries/GetWorkflowsQuery.cs`
*   `src/FlowOS.Application/Handlers/WorkflowQueryHandlers.cs`

### Modified Files
*   `src/FlowOS.API/Controllers/WorkflowsController.cs`