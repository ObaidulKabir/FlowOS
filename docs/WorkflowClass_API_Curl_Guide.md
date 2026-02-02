# WorkflowClass API Guide

Manage the lifecycle of `WorkflowClass` definitions.

## 1. Create Draft
**POST** `/api/workflow-classes`

Creates a new Draft.

**Payload:**
```json
{
  "name": "ExpenseApproval",
  "version": "1.0.0",
  "definition": {
    "events": [ ... ],
    "stateMachine": { ... },
    "workflow": { ... },
    "roles": [ ... ]
  }
}
```

## 2. Get Definition
**GET** `/api/workflow-classes/{id}`

Returns the full definition.
*   **Private**: Only visible to owner.
*   **Public/Shared**: Visible to all.

## 3. Lifecycle Actions

### Publish (Private)
**POST** `/api/workflow-classes/{id}/publish`
Validates integrity and freezes the version. Ready for local compilation.

### Submit for Review (Shared)
**POST** `/api/workflow-classes/{id}/submit`
Moves to Shared scope for Admin review.

### Approve (Public)
**POST** `/api/workflow-classes/{id}/approve`
(Admin Only) Makes the class globally available as a template.

## 4. Adoption
### Copy to Tenant
**POST** `/api/workflow-classes/{id}/copy`

Copies a **Public** template to your tenant as a new **Draft** (v1.0.0).

**Payload:**
```json
{
  "newTenantId": "your-tenant-guid"
}
```
