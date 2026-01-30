I will update the API documentation to reflect the recent changes:
1.  **New Endpoint**: `GET /api/workflows` (List running workflows).
2.  **New Endpoint**: `GET /api/admin/workflows` (Admin list view).
3.  **Header Requirement**: Document the `x-tenant-id` header requirement for Admin endpoints.
4.  **Database**: Note that the system now uses PostgreSQL by default.

### Plan
1.  **Edit API_DOCUMENTATION.md**:
    *   Add `GET /api/workflows` under the Workflows section.
    *   Add `GET /api/admin/workflows` under the Admin section.
    *   Add a "Headers" section to the Authentication/Overview area specifying `x-tenant-id`.

### Modified Files
*   `API_DOCUMENTATION.md`