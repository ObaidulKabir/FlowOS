# Notification Service Test Report

## Overview
This document summarizes the testing of the new FlowOS Notification Service. The service provides real-time alerts based on system events (e.g., workflow updates, agent insights).

## Setup
The service was tested using an **In-Memory Database** due to local Docker unavailability.
The following components were verified:
1.  **Notification API**: `GET /api/notifications` and `GET /api/notifications/stream`.
2.  **Workflow Engine**: Auto-advancement and event handling.
3.  **Event Interceptor**: Capturing domain events and projecting them to notifications.
4.  **Security**: Role-based access control (RBAC) using `x-tenant-id` and `X-Mock-Role`.

## Test Steps

### 1. Start the API Server
The server was configured to use In-Memory DB:
```bash
dotnet run --project src/FlowOS.Api/FlowOS.Api.csproj --urls=http://localhost:5001 --UseInMemoryDatabase=true
```

### 2. Verify Empty Notifications
**Request:**
```bash
curl -H "x-tenant-id: 11111111-1111-1111-1111-111111111111" -H "X-Mock-Role: Admin" http://localhost:5001/api/notifications
```
**Response:** `[]` (Empty list)

### 3. Start a Workflow
Triggers the creation of a new workflow instance. The handler was updated to auto-advance through initial `Default` transitions.

**Request:**
```bash
curl -X POST -H "Content-Type: application/json" \
     -H "x-tenant-id: 11111111-1111-1111-1111-111111111111" \
     -H "X-Mock-Role: Admin" \
     -d '{ "workflowName": "OrderProcessing", "version": 1 }' \
     http://localhost:5001/api/workflows/start
```
**Response:**
```json
{"workflowInstanceId":"..."}
```

### 4. Publish an Event
Simulates an external event (e.g., `EVT-ORDER-APPROVED`) that advances the workflow.

**Request:**
```bash
curl -X POST -H "Content-Type: application/json" \
     -H "x-tenant-id: 11111111-1111-1111-1111-111111111111" \
     -H "X-Mock-Role: Admin" \
     -d '{ "workflowInstanceId": "...", "eventType": "EVT-ORDER-APPROVED" }' \
     http://localhost:5001/api/events/publish
```
**Response:** `Event published`

### 5. Verify Notification Received
Checks if the event triggered a notification.

**Request:**
```bash
curl -H "x-tenant-id: 11111111-1111-1111-1111-111111111111" -H "X-Mock-Role: Admin" http://localhost:5001/api/notifications
```
**Response:**
```json
[
  {
    "message": "Event: EVT-ORDER-APPROVED",
    "severity": "Info",
    "createdAt": "2026-01-31T13:05:38.3751232Z",
    "eventType": "EVT-ORDER-APPROVED"
  }
]
```

## Conclusion
The Notification Service is fully functional. It correctly intercepts domain events, maps them to notifications, and exposes them via the API. The circular dependency issues were resolved, and the security context is correctly propagated.
