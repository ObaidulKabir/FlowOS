<#
.SYNOPSIS
  Verifies the FlowOS-hosted agent through a live QuoteAutoReview instance.

.DESCRIPTION
  The API process owns the hosted-provider credential. This script never reads,
  accepts, or prints an API key. It verifies health, durable workflow progress,
  provider-neutral attribution, the committed decision, and execution telemetry.

  Start the local API with FLOWOS_HOSTED_LLM_API_KEY configured, then run:
    pwsh ./scripts/test-live-hosted-agent.ps1
#>

param (
    [string]$BaseUrl = "http://127.0.0.1:5183",
    [Guid]$TenantId = "22222222-2222-2222-2222-222222222222",
    [int]$TimeoutSeconds = 90
)

$ErrorActionPreference = "Stop"
$BaseUrl = $BaseUrl.TrimEnd("/")

if ($TimeoutSeconds -lt 1 -or $TimeoutSeconds -gt 300) {
    throw "TimeoutSeconds must be between 1 and 300."
}

$headers = @{
    "Content-Type" = "application/json"
    "x-tenant-id"  = $TenantId.ToString()
    "X-Mock-Role"  = "Admin"
}

$live = (Invoke-WebRequest -UseBasicParsing -Uri "$BaseUrl/health/live" -TimeoutSec 10).StatusCode
$ready = (Invoke-WebRequest -UseBasicParsing -Uri "$BaseUrl/health/ready" -TimeoutSec 10).StatusCode
if ($live -ne 200 -or $ready -ne 200) {
    throw "FlowOS health checks did not pass."
}

$startBody = @{
    tenantId    = $TenantId
    workflowName = "QuoteAutoReview"
} | ConvertTo-Json -Compress

$started = Invoke-RestMethod `
    -Method Post `
    -Uri "$BaseUrl/api/workflows/start" `
    -Headers $headers `
    -Body $startBody

$instanceId = [Guid]$started.workflowInstanceId
$submitBody = @{
    tenantId          = $TenantId
    workflowInstanceId = $instanceId
    eventType         = "EVT-SUBMIT"
    payload           = @{
        Amount   = 900
        Estimate = 1000
        QuoteId  = "Q-LIVE-HOSTED-SMOKE"
    }
} | ConvertTo-Json -Compress -Depth 5

Invoke-RestMethod `
    -Method Post `
    -Uri "$BaseUrl/api/events/publish" `
    -Headers $headers `
    -Body $submitBody | Out-Null

$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
do {
    $instance = Invoke-RestMethod `
        -Method Get `
        -Uri "$BaseUrl/api/workflows/$instanceId" `
        -Headers $headers

    if ($instance.status -in @("Completed", "Failed", "Cancelled")) {
        break
    }

    Start-Sleep -Milliseconds 500
} while ([DateTime]::UtcNow -lt $deadline)

if ($instance.status -ne "Completed" -or
    $instance.currentStepId -ne "Closed" -or
    $instance.currentState -ne "Accepted") {
    throw "Hosted-agent workflow did not complete as Closed / Accepted."
}

$history = Invoke-RestMethod `
    -Method Get `
    -Uri "$BaseUrl/api/agents/$instanceId/history" `
    -Headers $headers
$execution = @($history.executions) | Select-Object -First 1

if ($null -eq $execution) {
    throw "No agent execution record was persisted."
}
if ($execution.status -ne "Succeeded" -or
    $execution.providerAlias -ne "flowos-hosted" -or
    $execution.providerName -ne "openai" -or
    $execution.suggestedEvent -ne "EVT-ACCEPT" -or
    -not $execution.wasCommitted) {
    throw "Hosted-agent execution did not produce the expected committed decision."
}

[pscustomobject]@{
    Passed         = $true
    InstanceId     = $instanceId
    Status         = $instance.status
    CurrentStep    = $instance.currentStepId
    CurrentState   = $instance.currentState
    Actor           = $execution.actor
    ProviderAlias  = $execution.providerAlias
    ProviderName   = $execution.providerName
    Model           = $execution.model
    SuggestedEvent = $execution.suggestedEvent
    WasCommitted   = $execution.wasCommitted
    InputTokens    = $execution.inputTokens
    OutputTokens   = $execution.outputTokens
} | Format-List
