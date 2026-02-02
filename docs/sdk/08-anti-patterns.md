# 08 - Anti-Patterns

How **NOT** to use FlowOS.

## ❌ "God Mode" Client
**Don't do this:**
```javascript
if (userApproved) {
  flow.transitionTo("EndStep"); // ERROR: Client deciding state
}
```

**Do this:**
```javascript
if (userApproved) {
  flow.publishEvent("EVT-DESIGN-APPROVED"); // OK: Client expressing intent
}
```

## ❌ Hardcoded Logic
**Don't do this:**
```javascript
// UI Logic
if (currentStep === "DesignTask") {
  showDesignForm();
} else if (currentStep === "Review") {
  showReviewForm();
}
```
*Why?* If the workflow definition changes (e.g., "Review" is renamed or split), your UI breaks.

**Do this:**
Query the task metadata or use generic task handlers where possible, or map Step IDs to UI Components dynamically.

## ❌ Ignoring Policies
**Don't do this:**
Assuming that because a button is visible, the action will succeed.

**Do this:**
Handle 403 Forbidden / Policy Violations gracefully in your UI. "Action blocked by policy: Weekend Freeze".
