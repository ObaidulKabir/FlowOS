# FlowOS MCP Server Usage Guide

This document demonstrates how an AI model interacts with the FlowOS MCP Server to design and govern workflows.

## 1. Discovery

The AI connects to the MCP server and lists available tools.

**Request:**

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/list",
  "params": {}
}
```

**Response:**

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "tools": [
      {
        "name": "describe_workflowclass_schema",
        "description": "Get the JSON schema for WorkflowClassBlueprint"
      },
      {
        "name": "create_draft_workflowclass",
        "description": "Create a new Draft WorkflowClass"
      },
      {
        "name": "validate_draft_workflowclass",
        "description": "Validate a Draft WorkflowClass"
      },
      ...
    ]
  }
}
```

## 2. Design Loop Example: Leave Approval

### Step 1: AI Proposes a Draft

The AI constructs a `WorkflowClassBlueprint` based on the user's requirements ("Leave Approval").

**Request:**

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "create_draft_workflowclass",
    "arguments": {
      "name": "LeaveApproval",
      "version": "1.0.0",
      "blueprint": {
        "Events": [
          { "EventType": "EVT-SUBMIT", "Category": "User" },
          { "EventType": "EVT-APPROVE", "Category": "User" }
        ],
        "StateMachine": {
          "InitialState": "Draft",
          "States": ["Draft", "Submitted", "Approved"],
          "Transitions": [
            { "FromState": "Draft", "EventType": "EVT-SUBMIT", "ToState": "Submitted" },
            { "FromState": "Submitted", "EventType": "EVT-APPROVE", "ToState": "Approved" }
          ]
        },
        "Workflow": {
          "StartStepId": "SubmitStep",
          "Steps": [
            {
              "StepId": "SubmitStep",
              "StepType": "HumanTask",
              "Label": "Submit Leave Request",
              "NextSteps": { "EVT-SUBMIT": "ApprovalStep" }
            },
            {
              "StepId": "ApprovalStep",
              "StepType": "HumanTask",
              "Label": "Manager Approval",
              "NextSteps": { "EVT-APPROVE": "End" }
            }
          ]
        }
      }
    }
  }
}
```

**Response:**

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "result": {
    "content": [
      {
        "type": "json",
        "text": "{ \"id\": \"<GUID>\", \"status\": \"Draft\", \"message\": \"Draft created successfully\" }"
      }
    ],
    "isError": false
  }
}
```

### Step 2: Validation

The AI checks if the draft is valid according to FlowOS laws.

**Request:**

```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "validate_draft_workflowclass",
    "arguments": {
      "id": "<GUID>"
    }
  }
}
```

**Response (Validation Failure):**

```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "result": {
    "content": [
      {
        "type": "json",
        "text": "{ \"isValid\": false, \"errors\": [ { \"code\": \"WF-VAL-005\", \"message\": \"Step 'End' is referenced but not defined.\" } ] }"
      }
    ],
    "isError": false
  }
}
```

### Step 3: Refinement

The AI interprets the error and updates the draft to include the missing step.

**Request:**

```json
{
  "jsonrpc": "2.0",
  "id": 4,
  "method": "tools/call",
  "params": {
    "name": "update_draft_workflowclass",
    "arguments": {
      "id": "<GUID>",
      "blueprint": {
        ... // Previous blueprint
        "Workflow": {
          ...
          "Steps": [
            ... // Previous steps
            {
              "StepId": "End",
              "StepType": "SystemTask",
              "Label": "End Process",
              "NextSteps": {}
            }
          ]
        }
      }
    }
  }
}
```

## 3. Forking a Public Template

**Request:**

```json
{
  "jsonrpc": "2.0",
  "id": 5,
  "method": "tools/call",
  "params": {
    "name": "fork_public_workflowclass",
    "arguments": {
      "publicId": "<Public_GUID>",
      "tenantId": "<My_Tenant_ID>"
    }
  }
}
```

**Response:**

```json
{
  "result": {
    "content": [
      { "type": "json", "text": "{ \"id\": \"<New_Draft_GUID>\", \"status\": \"Draft\", \"message\": \"Forked from StandardApproval\" }" }
    ]
  }
}
```
