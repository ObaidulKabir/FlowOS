# Sample API requests

Example request bodies for the FlowOS API, moved here from the repository root (CR-03).
They illustrate manual testing with `curl` or an HTTP client; the identifiers inside are
examples and do not exist in a fresh database.

| File | Typical use |
|------|-------------|
| `create_workflow.json`, `create_draft.json` | create a workflow class or draft |
| `publish_approval.json` | publish a workflow class |
| `start_workflow.json`, `start_v1.json`, `start_v2.json`, `start_latest.json`, `start_as_manager.json` | start a workflow instance |
| `publish_event.json`, `kickstart.json`, `trigger_start.json` | publish an event to an instance |
| `create_role.json`, `create_manager.json`, `create_policy.json`, `add_capability.json`, `add_start_capability.json` | roles, policies, capabilities |
| `agent_insight.json` | submit an agent insight |
| `test_init.json` | initial test payload |
