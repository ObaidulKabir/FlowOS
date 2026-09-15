# FlowOS Guide: Pre/Post-Event Reminders, Alerts & Multi-Tier SLA Timers

This document is the authoritative reference for software engineers and autonomous AI agents using the FlowOS Model Context Protocol (MCP) tool suite to design, synthesize, validate, execute, and monitor automated reminders and alerts before or after workflow events.

---

## 1. Executive Summary & Architectural Overview

In event-driven business architectures, generating reminders and alerts around events is essential for operational excellence. Common use cases include:
- **Pre-Event Countdowns (Lead-Time Alerts):** Sending an SMS/Email reminder 24 hours before a scheduled doctor appointment, flight, or court hearing (`eventDate - 24h`).
- **Post-Event Inactivity Follow-Ups:** Sending a customer check-in or cart recovery nudge 3 days after an order or sign-up if no further action occurs (`checkoutDate + 3d`).
- **Multi-Tier Task SLA Warnings:** Alerting assignees 24 hours and 2 hours before a hard 48-hour approval SLA deadline expires, with automatic cancellation once the task is completed.

FlowOS delivers first-class support for these scenarios through two complementary architectural patterns:
1. **Dynamic Relative Timers (Standalone `Timer` steps):** Computes absolute UTC wake-up timestamps dynamically by resolving target dates from the workflow instance payload (`targetTimestampProperty`) combined with positive or negative lead-time offsets (`leadTime` or `offset`).
2. **Multi-Tier Step SLA Intermediate Reminders (`sla.reminders` list):** Attaches intermediate non-interrupting countdown notifications directly to any human or system task, firing notifications at specified offsets and automatically cancelling all remaining timer jobs upon task completion.

All outgoing alerts are dispatched through the **Transactional Outbox**, guaranteeing resilience, retry policies, HMAC-SHA256 cryptographic signatures (for webhooks), and real-time SSE streaming (for in-app notifications).

---

## 2. Architectural Patterns

```
+-----------------------------------------------------------------------------------------+
|                                    FLOWOS WORKFLOW ENGINE                                |
+-----------------------------------------------------------------------------------------+
                                              |
               +------------------------------+------------------------------+
               |                                                             |
               v                                                             v
   [ PATTERN A: RELATIVE TIMER ]                                 [ PATTERN B: STEP SLA REMINDERS ]
  StepType: "Timer"                                             StepType: "HumanTask" / "SystemTask"
  Conditions:                                                   Sla: {
    targetTimestampProperty: "appointmentDate"                    duration: "48h",
    leadTime: "-24h"                                              timeoutEvent: "EVT-ESCALATE",
                                                                  reminders: [
  Engine resolves:                                                  { duration: "-24h", triggerEvent: "EVT-REMIND-1" },
  target = payload["appointmentDate"]                              { duration: "-2h",  triggerEvent: "EVT-REMIND-2" }
  dueTimeUtc = target - 24h                                       ]
  TimerJob stored in DB (DueTimeUtc)                            }
                                                                TimerJobs scheduled in DB:
                                                                - Primary SLA: UtcNow + 48h
                                                                - Reminder 1:  UtcNow + 24h
                                                                - Reminder 2:  UtcNow + 46h
                                                                * On CompleteTask: ALL 3 cancelled atomically!
```

### Pattern A: Dynamic Relative Timers (Pre/Post-Event)

Use a standalone `Timer` step when the workflow needs to pause until a calculated relative moment in time before or after a business event.

- **Pre-Event (Lead-Time):** `leadTime: "-24h"` with `targetTimestampProperty: "appointmentDate"`. The engine pauses execution until exactly 24 hours prior to `appointmentDate`.
- **Post-Event (Elapsed Follow-up):** `leadTime: "+3d"` (or `offset: "3d"`) with `targetTimestampProperty: "orderDate"`. The engine pauses until 3 days after `orderDate`.
- **Past-Due Guard:** If the calculated `dueTimeUtc <= DateTime.UtcNow`, the timer fires immediately (`TimeSpan.Zero`), avoiding deadlocks or missed notifications.

### Pattern B: Multi-Tier Task SLA Reminders

Use `sla.reminders` when a task has a fixed SLA deadline and needs progressive warning alerts sent to the assignee or team before the hard escalation triggers.

- **Negative Offsets (Countdowns before deadline):** `duration: "-2h"` means "fire 2 hours before the SLA deadline expires".
- **Positive Offsets (Elapsed since step entry):** `duration: "30m"` means "fire 30 minutes after the step was entered".
- **Automatic Cancellation:** When the human task is completed via `complete_task` (or the step exits via any transition), `IWorkflowTimerService.CancelTimerAsync(workflowInstanceId, stepId)` cancels the primary SLA timeout job and all associated reminder jobs in a single atomic database query.

---

## 3. Duration Syntax & Relative Time Expressions

All durations in FlowOS blueprints follow strict, ISO-compatible shorthand notation:

| Syntax | Description | Example |
| :--- | :--- | :--- |
| `\d+s` | Seconds | `30s` (30 seconds) |
| `\d+m` | Minutes | `15m`, `45m` (minutes) |
| `\d+h` | Hours | `2h`, `24h`, `48h` (hours) |
| `\d+d` | Days | `1d`, `3d`, `7d` (days) |
| `-\d+(s\|m\|h\|d)` | Negative offset (Lead-time before target or deadline) | `-2h`, `-30m`, `-1d` |
| `+\d+(s\|m\|h\|d)` | Explicit positive offset | `+1d`, `+2h` |

### Target Timestamp Property Resolution
For relative timers, FlowOS checks the step's `conditions` dictionary for:
1. `targetTimestampProperty` (recommended): Specifies the key name in the workflow payload containing an ISO 8601 UTC date string (e.g. `"appointmentDate"`).
2. `targetTimestamp` / `referenceDate` / `scheduledAt`: Alternative condition property keys supported interchangeably.

---

## 4. Alert Notification Channels

When a reminder fires, the step's `OnEntry`, `OnExit`, or transition actions dispatch messages via FlowOS Outbox channels:

### 1. In-App Notifications (`actionType: "Notification"`)
Pushes real-time SSE alerts directly to connected tenant web apps and dashboards.
```json
{
  "actionType": "Notification",
  "target": "PatientUser",
  "template": "Reminder: Your appointment is scheduled for tomorrow at 2:00 PM."
}
```

### 2. Email Notifications (`actionType: "Email"`)
Dispatches outbound email through the configured tenant SMTP service (e.g., Zoho SMTP).
```json
{
  "actionType": "Email",
  "target": "patient@example.com",
  "template": "Appointment Reminder: 24 Hours Remaining",
  "payloadMapping": {
    "to": "patientEmail",
    "subject": "'Appointment Reminder'",
    "body": "'Your consultation is tomorrow at 2:00 PM UTC. Please bring valid photo identification.'"
  }
}
```

### 3. Bank-Grade HMAC-SHA256 Webhooks (`actionType: "Webhook"`)
Dispatches authenticated POST requests to external systems with non-repudiation signature headers (`X-FlowOS-Signature: t={timestamp},v1={hash}`).
```json
{
  "actionType": "Webhook",
  "url": "https://api.clinic.com/webhooks/reminders",
  "signPayload": true,
  "payloadMapping": {
    "workflowInstanceId": "workflowInstanceId",
    "alertType": "'PRE_EVENT_REMINDER'",
    "appointmentDate": "appointmentDate"
  }
}
```

---

## 5. Full Executable Production Blueprints

### Blueprint 1: Doctor Appointment Booking with 24-Hour Pre-Event Lead Time Reminder

```json
{
  "name": "Doctor Appointment Booking",
  "version": "1.0.0",
  "blueprint": {
    "events": [
      { "eventId": "EVT-BOOK", "name": "Appointment Booked", "category": "Human" },
      { "eventId": "EVT-REMIND-24H", "name": "24h Pre-Event Reminder", "category": "System" },
      { "eventId": "EVT-CHECKIN", "name": "Patient Checked In", "category": "Human" },
      { "eventId": "EVT-NOSHOW", "name": "Patient No-Show", "category": "System" }
    ],
    "stateMachine": {
      "initialState": "Booked",
      "states": ["Booked", "ReminderSent", "CheckedIn", "NoShow"],
      "transitions": [
        { "fromState": "Booked", "toState": "ReminderSent", "eventId": "EVT-REMIND-24H" },
        { "fromState": "ReminderSent", "toState": "CheckedIn", "eventId": "EVT-CHECKIN" },
        { "fromState": "ReminderSent", "toState": "NoShow", "eventId": "EVT-NOSHOW" }
      ]
    },
    "workflow": {
      "startStepId": "WaitForLeadTime",
      "steps": [
        {
          "stepId": "WaitForLeadTime",
          "stepType": "Timer",
          "conditions": {
            "targetTimestampProperty": "appointmentDate",
            "leadTime": "-24h"
          },
          "nextSteps": {
            "EVT-REMIND-24H": "SendPreEventAlert"
          }
        },
        {
          "stepId": "SendPreEventAlert",
          "stepType": "Command",
          "onEntry": [
            {
              "actionType": "Notification",
              "target": "Patient",
              "template": "Reminder: Your appointment is scheduled in 24 hours."
            },
            {
              "actionType": "Email",
              "target": "patient@example.com",
              "template": "Upcoming Appointment Alert"
            }
          ],
          "nextSteps": {
            "Default": "AwaitCheckIn"
          }
        },
        {
          "stepId": "AwaitCheckIn",
          "stepType": "HumanTask",
          "sla": {
            "duration": "26h",
            "timeoutEvent": "EVT-NOSHOW",
            "isInterrupting": true
          },
          "nextSteps": {
            "EVT-CHECKIN": "END",
            "EVT-NOSHOW": "END"
          }
        }
      ]
    },
    "roles": [
      { "name": "Patient", "description": "Receives appointment countdown notifications." },
      { "name": "Doctor", "description": "Conducts consultations." }
    ],
    "capabilities": []
  }
}
```

---

### Blueprint 2: Procurement Approval with Multi-Tier SLA Reminders (-24h, -2h)

```json
{
  "name": "Procurement Purchase Approval",
  "version": "1.0.0",
  "blueprint": {
    "events": [
      { "eventId": "EVT-SUBMIT", "name": "Purchase Order Submitted", "category": "Human" },
      { "eventId": "EVT-WARN-24H", "name": "24h Remaining Warning", "category": "System" },
      { "eventId": "EVT-WARN-2H", "name": "Critical 2h Warning", "category": "System" },
      { "eventId": "EVT-ESCALATE", "name": "SLA Breached Escalate", "category": "System" },
      { "eventId": "EVT-APPROVE", "name": "PO Approved", "category": "Human" }
    ],
    "stateMachine": {
      "initialState": "PendingApproval",
      "states": ["PendingApproval", "Escalated", "Approved"],
      "transitions": [
        { "fromState": "PendingApproval", "toState": "PendingApproval", "eventId": "EVT-WARN-24H" },
        { "fromState": "PendingApproval", "toState": "PendingApproval", "eventId": "EVT-WARN-2H" },
        { "fromState": "PendingApproval", "toState": "Escalated", "eventId": "EVT-ESCALATE" },
        { "fromState": "PendingApproval", "toState": "Approved", "eventId": "EVT-APPROVE" }
      ]
    },
    "workflow": {
      "startStepId": "ManagerReview",
      "steps": [
        {
          "stepId": "ManagerReview",
          "stepType": "HumanTask",
          "requiredRoles": ["FinanceManager"],
          "sla": {
            "duration": "48h",
            "timeoutEvent": "EVT-ESCALATE",
            "escalationStepId": "EscalateToDirector",
            "isInterrupting": true,
            "reminders": [
              { "duration": "-24h", "triggerEvent": "EVT-WARN-24H" },
              { "duration": "-2h",  "triggerEvent": "EVT-WARN-2H" }
            ]
          },
          "nextSteps": {
            "EVT-APPROVE": "END",
            "EVT-WARN-24H": "ManagerReview",
            "EVT-WARN-2H": "ManagerReview",
            "EVT-ESCALATE": "EscalateToDirector"
          }
        },
        {
          "stepId": "EscalateToDirector",
          "stepType": "Command",
          "onEntry": [
            {
              "actionType": "Notification",
              "target": "FinanceDirector",
              "template": "SLA Breached: PO pending review has been escalated to executive authority."
            }
          ],
          "nextSteps": {
            "Default": "END"
          }
        }
      ]
    },
    "roles": [
      { "name": "FinanceManager", "description": "First-line PO approver." },
      { "name": "FinanceDirector", "description": "Escalation authority." }
    ],
    "capabilities": []
  }
}
```

---

### Blueprint 3: Customer Onboarding Inactivity Nudge (Post-Event Follow-Up)

```json
{
  "name": "Customer Onboarding Nudge",
  "version": "1.0.0",
  "blueprint": {
    "events": [
      { "eventId": "EVT-SIGNUP", "name": "Account Created", "category": "System" },
      { "eventId": "EVT-NUDGE", "name": "Send Onboarding Nudge", "category": "System" },
      { "eventId": "EVT-ACTIVATED", "name": "Profile Completed", "category": "Human" }
    ],
    "stateMachine": {
      "initialState": "Registered",
      "states": ["Registered", "NudgeSent", "Active"],
      "transitions": [
        { "fromState": "Registered", "toState": "NudgeSent", "eventId": "EVT-NUDGE" },
        { "fromState": "Registered", "toState": "Active", "eventId": "EVT-ACTIVATED" },
        { "fromState": "NudgeSent", "toState": "Active", "eventId": "EVT-ACTIVATED" }
      ]
    },
    "workflow": {
      "startStepId": "WaitThreeDays",
      "steps": [
        {
          "stepId": "WaitThreeDays",
          "stepType": "Timer",
          "conditions": {
            "targetTimestampProperty": "signupDate",
            "leadTime": "+3d"
          },
          "nextSteps": {
            "EVT-NUDGE": "SendCheckInEmail"
          }
        },
        {
          "stepId": "SendCheckInEmail",
          "stepType": "Command",
          "onEntry": [
            {
              "actionType": "Email",
              "target": "user@example.com",
              "template": "Need help getting started with FlowOS?"
            }
          ],
          "nextSteps": {
            "Default": "END"
          }
        }
      ]
    },
    "roles": [
      { "name": "Customer", "description": "New platform user." }
    ],
    "capabilities": []
  }
}
```

---

## 6. MCP Agent Tool Reference & JSON-RPC Payload Examples

Autonomous AI agents (such as Claude Desktop, Cursor, or custom MCP clients) interact with reminders and alerts using standard JSON-RPC tool calls.

### 1. Generating Blueprints with Reminders from Natural Language
Tool: `generate_workflow_blueprint_from_nl`
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "generate_workflow_blueprint_from_nl",
    "arguments": {
      "prompt": "Create an appointment booking workflow with a reminder alert 24 hours before appointmentDate, and an approval task with 48h SLA and a reminder 2 hours before timeout."
    }
  }
}
```

### 2. Validating Blueprints with Reminders
Tool: `validate_draft_workflowclass`
```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "validate_draft_workflowclass",
    "arguments": {
      "id": "33333333-3333-3333-3333-333333333333",
      "tenantId": "11111111-1111-1111-1111-111111111111"
    }
  }
}
```

#### Validation Error Codes Reference

| Error Code | Category | Cause & Design Correction Hint |
| :--- | :--- | :--- |
| `WF-SLA-001` | WorkflowSla | Step defines an SLA without a `duration`. Provide e.g. `"48h"`. |
| `WF-SLA-002` | WorkflowSla | Step defines an SLA without a `timeoutEvent`. Provide e.g. `"EVT-ESCALATE"`. |
| `WF-SLA-003` | WorkflowSla | Invalid reminder duration format. Duration must match `^([+-])?\d+(s\|m\|h\|d)$` (e.g. `"-2h"`, `"30m"`). |
| `WF-SLA-004` | WorkflowSla | Reminder references an undeclared trigger event or missing `triggerEvent`. Add the event to blueprint `events`. |
| `WF-TMR-001` | WorkflowTimer | Timer step specifies `leadTime` or `offset` but lacks `targetTimestampProperty`. Specify the payload property name. |
| `CON-005` | Consistency | Step SLA references undeclared timeout event. Declare it in `events`. |

### 3. Starting a Workflow with Event Dates in Payload
Tool: `start_workflow`
```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "start_workflow",
    "arguments": {
      "workflowClassId": "44444444-4444-4444-4444-444444444444",
      "tenantId": "11111111-1111-1111-1111-111111111111",
      "payload": {
        "appointmentDate": "2026-10-15T14:30:00Z",
        "patientEmail": "patient@example.com",
        "doctorName": "Dr. Sarah Connor"
      }
    }
  }
}
```
*FlowOS automatically extracts `payload["appointmentDate"]`, applies `-24h`, and schedules a `WorkflowTimerJob` with `DueTimeUtc = 2026-10-14T14:30:00Z`.*

### 4. Completing a Task (Auto-Cancelling Reminders)
Tool: `complete_task`
```json
{
  "jsonrpc": "2.0",
  "id": 4,
  "method": "tools/call",
  "params": {
    "name": "complete_task",
    "arguments": {
      "workflowInstanceId": "55555555-5555-5555-5555-555555555555",
      "taskId": "66666666-6666-6666-6666-666666666666",
      "tenantId": "11111111-1111-1111-1111-111111111111"
    }
  }
}
```
*All active timers and reminders associated with the task are cancelled immediately, guaranteeing zero stale notifications.*

---

## 7. Reliability, Idempotency & Auditability Guarantees

1. **Transactional Outbox Delivery:** Every reminder and alert is recorded within the same ACID database transaction as the workflow state transition. If a downstream network timeout occurs, exponential backoff retries preserve delivery without message loss.
2. **Dead-Letter Queue Visibility:** Use `list_dead_letters` and `retry_dead_letter` MCP tools to observe and replay any failed notification dispatches.
3. **Forensic Action History:** Call `get_instance_action_history` to inspect the exact millisecond timestamps, HTTP status codes, and payload snippets of every notification sent.
4. **Time-Travel Debugging:** Reconstruct the complete event history with `replay_workflow_history` to verify the timeline of timers scheduled, fired, or cancelled.
