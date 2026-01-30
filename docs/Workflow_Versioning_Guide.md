# Workflow Versioning Guide

This guide explains how to manage, deploy, and execute multiple versions of workflows in FlowOS.

## 1. Versioning Strategy
FlowOS supports **Semantic Versioning** for workflows. Each version is a distinct, immutable definition.
- **v1**: Legacy or stable process.
- **v2**: New process with added steps (e.g., Risk Check).

## 2. Deploying a New Version
To deploy a new version, simply create a new JSON configuration file with an incremented `version` field.

**Example: OrderProcessing v2**
```json
{
  "name": "OrderProcessing",
  "version": 2,
  "steps": [ ... ]
}
```

Publish it using the Admin API:
```bash
curl -X POST "http://localhost:5001/api/admin/config/publish" \
  -H "X-Tenant-ID: <your-tenant-id>"
```

## 3. Executing Workflows

### A. Start Specific Version
You can target a specific version by including the `version` field in your request.

**Request:**
```json
{
  "workflowName": "OrderProcessing",
  "version": 1
}
```

### B. Start Latest Version (Default)
If you omit the `version` field, FlowOS automatically resolves and starts the **highest available version** number.

**Request:**
```json
{
  "workflowName": "OrderProcessing"
}
```

## 4. Verification
You can verify which version is running by checking the Admin API.

**Endpoint:** `GET /api/admin/workflows`

**Response:**
```json
[
  {
    "id": "...",
    "definitionName": "OrderProcessing",
    "version": 2,  // <--- Shows actual running version
    "status": "Running"
  }
]
```
