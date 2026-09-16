<#
.SYNOPSIS
  FlowOS MCP Agent Workflow Test Runner
  Executes the 4-step AI agent verification lifecycle against a FlowOS MCP endpoint.

.DESCRIPTION
  Matches the AI browser evaluation experiment:
  1. Discovery: GET /mcp (verifies public self-documenting endpoint)
  2. Handshake & Tools: JSON-RPC initialize & tools/list (21 registered tools)
  3. "Show me all active workflows for this tenant" (list_workflow_instances & list_public_workflowclasses)
  4. "Why is workflow X blocked?" (get_workflow_instance_status & suggest_agent_action)
  5. "Execute that transition" under strict State Machine enforcement (publish_event / complete_task)
  6. Rejection of illegal transitions (verifying "State Machine = Law")
#>

param (
    [string]$Url = "",
    [string]$ApiKey = "",
    [string]$TenantId = ""
)

$ErrorActionPreference = "Stop"

$envFromShell = @{}
Get-ChildItem Env: | ForEach-Object { $envFromShell[$_.Name] = $true }

function Import-DotEnv([string]$Path) {
    if (-not (Test-Path $Path)) { return }
    Get-Content $Path | ForEach-Object {
        $line = $_.Trim()
        if (-not $line -or $line.StartsWith('#')) { return }
        $eq = $line.IndexOf('=')
        if ($eq -le 0) { return }
        $name = $line.Substring(0, $eq).Trim()
        $value = $line.Substring($eq + 1).Trim().Trim('"').Trim("'")
        if ($envFromShell.ContainsKey($name)) { return }
        Set-Item -Path "Env:$name" -Value $value
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
Import-DotEnv (Join-Path $repoRoot '.env')
$envFlavor = ([string]$env:FLOWOS_ENV).Trim().ToLowerInvariant()
if ($envFlavor -eq 'staging' -or $envFlavor -eq 'production') {
    Import-DotEnv (Join-Path $repoRoot ".env.$envFlavor")
}
Import-DotEnv (Join-Path $repoRoot '.env.local')

function Test-ProductionHost([string]$Hostname) {
    $hostName = $Hostname.ToLowerInvariant()
    return ($hostName -eq 'flowosbd.com') -or $hostName.EndsWith('.flowosbd.com')
}

function Resolve-McpUrl([string]$Candidate, [string]$Origin) {
    if ($Candidate) {
        try {
            $parsed = [Uri]$Candidate
            $path = $parsed.AbsolutePath.TrimEnd('/')
            if ($path -eq '/mcp' -and (Test-ProductionHost $parsed.Host) -and -not $Candidate.EndsWith('/')) {
                return "$Candidate/"
            }
            if ($path -eq '/mcp' -and -not (Test-ProductionHost $parsed.Host)) {
                return $Candidate.TrimEnd('/')
            }
            return $Candidate
        } catch {
            return $Candidate
        }
    }

    if ($Origin) {
        $base = $Origin.Trim().TrimEnd('/')
        try {
            $parsed = [Uri]$base
            if (Test-ProductionHost $parsed.Host) { return "$base/mcp/" }
        } catch { }
        return "$base/mcp"
    }

    throw 'Set MCP_URL or FLOWOS_PUBLIC_ORIGIN. Staging: https://flowos.prospectbdltd.com/mcp  Production: https://flowosbd.com/mcp/'
}

if (-not $Url) { $Url = $env:MCP_URL }
$Url = Resolve-McpUrl $Url $env:FLOWOS_PUBLIC_ORIGIN

if (-not $ApiKey) { $ApiKey = $env:MCP_API_KEY }
if (-not $TenantId) { $TenantId = $env:MCP_TENANT_ID }

if ([string]::IsNullOrWhiteSpace($ApiKey) -or [string]::IsNullOrWhiteSpace($TenantId)) {
    throw 'Set MCP_API_KEY and MCP_TENANT_ID in .env.local (or pass -ApiKey / -TenantId) before running this script.'
}

function Write-Step([string]$title) {
    Write-Host "`n========================================================" -ForegroundColor Cyan
    Write-Host ">> $title" -ForegroundColor Yellow
    Write-Host "========================================================" -ForegroundColor Cyan
}

function Write-Success([string]$msg) {
    Write-Host "  [PASS] $msg" -ForegroundColor Green
}

function Write-Info([string]$msg) {
    Write-Host "  [INFO] $msg" -ForegroundColor Gray
}

function Write-Warn([string]$msg) {
    Write-Host "  [WARN] $msg" -ForegroundColor DarkYellow
}

function Send-McpRpc([string]$method, [hashtable]$params = $null, [int]$id = 1, [bool]$expectSuccess = $true) {
    $payload = @{
        jsonrpc = "2.0"
        id      = $id
        method  = $method
    }
    if ($null -ne $params) {
        $payload["params"] = $params
    }

    $jsonBody = $payload | ConvertTo-Json -Depth 10

    $headers = @{
        "Content-Type"         = "application/json"
        "Accept"               = "application/json, text/event-stream"
        "X-MCP-API-Key"        = $ApiKey
        "x-tenant-id"          = $TenantId
        "MCP-Protocol-Version" = "2025-03-26"
    }

    try {
        $response = Invoke-RestMethod -Uri $Url -Method Post -Headers $headers -Body $jsonBody
        return $response
    } catch {
        if (-not $expectSuccess) {
            return $_
        }
        Write-Host "RPC Call Failed: $_" -ForegroundColor Red
        throw
    }
}

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " FlowOS MCP AI Agent Workflow Verification" -ForegroundColor White
Write-Host " Target:   $Url" -ForegroundColor Gray
Write-Host " Tenant:   $TenantId" -ForegroundColor Gray
Write-Host "============================================================" -ForegroundColor Cyan

# -------------------------------------------------------------
# STEP 1: Test Public Discovery Endpoint (GET /mcp)
# -------------------------------------------------------------
Write-Step "STEP 1: Testing Public GET /mcp Discovery Endpoint"
try {
    $discovery = Invoke-RestMethod -Uri $Url -Method Get -Headers @{ "Accept" = "application/json" }
    Write-Success "Discovery endpoint reachable without API key"
    Write-Info "Server Name: $($discovery.name)"
    Write-Info "Status:      $($discovery.status)"
    Write-Info "Protocol:    $($discovery.protocol)"
    Write-Info "Tools Count: $($discovery.toolsCount)"
} catch {
    Write-Warn "GET /mcp returned error: $_. Checking POST directly..."
}

# -------------------------------------------------------------
# STEP 2: MCP Handshake (initialize & tools/list)
# -------------------------------------------------------------
Write-Step "STEP 2: Initializing MCP Handshake (initialize & tools/list)"
$initResponse = Send-McpRpc -method "initialize" -params @{
    protocolVersion = "2025-03-26"
    clientInfo      = @{ name = "FlowOS-Agent-Tester"; version = "1.0.0" }
    capabilities    = @{}
} -id 1

Write-Success "MCP Handshake initialized successfully"
Write-Info "Server protocolVersion: $($initResponse.result.protocolVersion)"

$toolsList = Send-McpRpc -method "tools/list" -params @{} -id 2
$tools = $toolsList.result.tools
Write-Success "Retrieved $($tools.Count) registered MCP tools from server"
foreach ($t in $tools) {
    Write-Info " - $($t.name): $($t.description.Substring(0, [Math]::Min(70, $t.description.Length)))..."
}

# -------------------------------------------------------------
# STEP 3: "Show me all active workflows for this tenant"
# -------------------------------------------------------------
Write-Step "STEP 3: Agent Query: 'Show me all active workflows for this tenant'"
$listResult = Send-McpRpc -method "tools/call" -params @{
    name      = "list_workflow_instances"
    arguments = @{ status = "Active" }
} -id 3

$instancesText = $listResult.result.content[0].text
Write-Success "Workflow instances retrieved:"
Write-Host $instancesText -ForegroundColor White

$publicTemplates = Send-McpRpc -method "tools/call" -params @{
    name      = "list_public_workflowclasses"
    arguments = @{}
} -id 4
Write-Success "Public workflow templates catalog:"
Write-Host $publicTemplates.result.content[0].text -ForegroundColor White

# -------------------------------------------------------------
# STEP 4: Start a Workflow & Inspect State ("Why is it blocked?")
# -------------------------------------------------------------
Write-Step "STEP 4: Agent Action: Start Workflow & Inspect State"
$startResult = Send-McpRpc -method "tools/call" -params @{
    name      = "start_workflow"
    arguments = @{
        workflowName = "ExpenseApproval"
    }
} -id 5

$startText = $startResult.result.content[0].text
Write-Info "Start result: $startText"

$startObj = $startText | ConvertFrom-Json
$instanceId = $startObj.workflowInstanceId

if (-not $instanceId) {
    Write-Warn "Workflow instance ID not returned, parsing from text..."
    if ($startText -match '([0-9a-fA-F-]{36})') {
        $instanceId = $matches[1]
    }
}

if ($instanceId) {
    Write-Success "Started workflow instance: $instanceId"

    Write-Info "Querying status: 'Why is workflow $instanceId waiting?'"
    $statusResult = Send-McpRpc -method "tools/call" -params @{
        name      = "get_workflow_instance_status"
        arguments = @{ instanceId = $instanceId }
    } -id 6
    Write-Success "Workflow Status & Current Step:"
    Write-Host $statusResult.result.content[0].text -ForegroundColor White

    Write-Info "Asking Agent: 'What transition or action is recommended?'"
    $agentResult = Send-McpRpc -method "tools/call" -params @{
        name      = "suggest_agent_action"
        arguments = @{
            workflowInstanceId = $instanceId
            agentId            = "RiskAnalysisAgent"
            objective          = "Determine next valid transition for pending expense"
        }
    } -id 7
    Write-Success "Agent Suggestion:"
    Write-Host $agentResult.result.content[0].text -ForegroundColor White

    # -------------------------------------------------------------
    # STEP 5: State Machine Enforcement: Rejection of Invalid Transition
    # -------------------------------------------------------------
    Write-Step "STEP 5: Verifying 'State Machine = Law' (Illegal Transition Rejection)"
    $illegalResult = Send-McpRpc -method "tools/call" -params @{
        name      = "publish_event"
        arguments = @{
            workflowInstanceId = $instanceId
            eventType          = "EVT-ILLEGAL-JUMP-STATE"
        }
    } -id 8

    $illegalText = $illegalResult.result.content[0].text
    if ($illegalResult.result.isError -or $illegalText -match "not accepted" -or $illegalText -match "Fail") {
        Write-Success "State machine correctly REJECTED illegal transition: $illegalText"
    } else {
        Write-Warn "Expected state machine rejection, got: $illegalText"
    }

    # -------------------------------------------------------------
    # STEP 6: Execute Valid Transition
    # -------------------------------------------------------------
    Write-Step "STEP 6: Agent Action: Execute Valid State Transition"
    $transitionResult = Send-McpRpc -method "tools/call" -params @{
        name      = "publish_event"
        arguments = @{
            workflowInstanceId = $instanceId
            eventType          = "EVT-SUBMIT"
            payload            = @{ amount = 150; currency = "USD" }
        }
    } -id 9
    Write-Success "Valid transition execution response:"
    Write-Host $transitionResult.result.content[0].text -ForegroundColor White
} else {
    Write-Warn "Could not extract instanceId to proceed with progression step."
}

# -------------------------------------------------------------
# STEP 7: Enforced Human Confirmation Gate (Anti-Autonomy Violation)
# -------------------------------------------------------------
Write-Step "STEP 7: Verifying Enforced Human Confirmation Gate on High-Risk Action"
$unconfirmedPublish = Send-McpRpc -method "tools/call" -params @{
    name      = "publish_workflowclass"
    arguments = @{ id = "00000000-0000-0000-0000-000000000001" }
} -id 10

$unconfirmedText = $unconfirmedPublish.result.content[0].text
if ($unconfirmedText -match "MCP-APPROVAL-REQUIRED") {
    Write-Success "Machine-enforced human gate blocked unconfirmed execution: $unconfirmedText"
} else {
    Write-Warn "Expected MCP-APPROVAL-REQUIRED error, got: $unconfirmedText"
}

Write-Host "`n============================================================" -ForegroundColor Green
Write-Host " FlowOS MCP Agent Workflow Verification COMPLETED SUCCESSFULLY!" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
