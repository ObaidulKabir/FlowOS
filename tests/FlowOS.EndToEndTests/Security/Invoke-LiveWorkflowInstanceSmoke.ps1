# Read-only production check. Supply one credential through environment variables,
# never through command-line arguments or committed files.
$ErrorActionPreference = 'Stop'

function Fail([string]$message, [int]$code = 1) {
    [Console]::Error.WriteLine($message)
    exit $code
}

$tenantText = [Environment]::GetEnvironmentVariable('FLOWOS_LIVE_TENANT_ID')
$apiKey = [Environment]::GetEnvironmentVariable('FLOWOS_LIVE_API_KEY')
$jwt = [Environment]::GetEnvironmentVariable('FLOWOS_LIVE_JWT')

[guid]$tenantId = [guid]::Empty
if (-not [guid]::TryParse($tenantText, [ref]$tenantId) -or $tenantId -eq [guid]::Empty) {
    Fail 'NOT RUN: Set FLOWOS_LIVE_TENANT_ID to the affected tenant UUID.' 2
}
if ([bool]$apiKey -eq [bool]$jwt) {
    Fail 'NOT RUN: Set exactly one of FLOWOS_LIVE_API_KEY or FLOWOS_LIVE_JWT in the local environment.' 2
}

$headers = @{ 'x-tenant-id' = $tenantId.ToString('D') }
if ($apiKey) {
    $headers['X-API-Key'] = $apiKey
} else {
    # Match the dashboard's JWT request headers without printing the token.
    $headers['X-API-Key'] = $jwt
    $headers['Authorization'] = "Bearer $jwt"
}

$url = "https://flowosbd.com/api/workflows?tenantId=$($tenantId.ToString('D'))"
try {
    $response = Invoke-WebRequest -Uri $url -Method Get -Headers $headers `
        -MaximumRedirection 0 -SkipHttpErrorCheck -TimeoutSec 20
} catch {
    Fail 'FAIL: Production workflow instance request did not complete. Check network connectivity and server logs.'
}

if ([int]$response.StatusCode -ne 200) {
    Fail "FAIL: Production workflow instance request returned HTTP $([int]$response.StatusCode)."
}

try {
    $body = [System.Text.Json.JsonDocument]::Parse([string]$response.Content)
    try {
        if ($body.RootElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Array) {
            Fail 'FAIL: Workflow instance response is not a JSON array.'
        }
        [Console]::WriteLine("PASS: Production workflow instance list returned HTTP 200 with $($body.RootElement.GetArrayLength()) entries.")
    } finally {
        $body.Dispose()
    }
} catch {
    Fail 'FAIL: Workflow instance response is not valid JSON.'
}
