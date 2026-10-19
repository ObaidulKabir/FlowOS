# ==============================================================================
# FlowOS Multi-VPS Cluster & Load Balancing Test Script
# Tests round-robin distribution, health checks, and individual node endpoints.
# ==============================================================================

param(
    [string]$BaseUrl = "http://localhost:5183",
    [int]$RequestsCount = 9
)

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "       FlowOS Multi-VPS Load Balancing Cluster Test       " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Target Load Balancer URL: $BaseUrl" -ForegroundColor Yellow
Write-Host ""

# 1. Query Cluster Topology Discovery Endpoint
Write-Host "[1/3] Querying Load Balancer Cluster Topology (/health/cluster)..." -ForegroundColor Green
try {
    $clusterInfo = Invoke-RestMethod -Uri "http://localhost/health/cluster" -Method Get -TimeoutSec 5
    $clusterJson = $clusterInfo | ConvertTo-Json -Depth 5
    Write-Host "Cluster Status: OK" -ForegroundColor Green
    Write-Host $clusterJson -ForegroundColor Gray
}
catch {
    Write-Host "Could not query http://localhost/health/cluster. Testing API ports directly..." -ForegroundColor DarkYellow
}

Write-Host ""
# 2. Test Load Balancing Distribution
Write-Host "[2/3] Sending $RequestsCount consecutive requests to Load Balancer to verify traffic distribution..." -ForegroundColor Green
$nodesSeen = @{}

for ($i = 1; $i -le $RequestsCount; $i++) {
    try {
        $response = Invoke-WebRequest -Uri "$BaseUrl/health" -Method Get -TimeoutSec 5 -UseBasicParsing
        $servedBy = $response.Headers["X-Served-By"]
        if (-not $servedBy) {
            $servedBy = "Unknown (Check upstream)"
        }

        if (-not $nodesSeen.ContainsKey($servedBy)) {
            $nodesSeen[$servedBy] = 0
        }
        $nodesSeen[$servedBy]++

        Write-Host "  Request #$i -> Served by VPS Node: $servedBy (Status: $($response.StatusCode))" -ForegroundColor Cyan
    }
    catch {
        Write-Host "  Request #$i -> Error: $_" -ForegroundColor Red
    }
    Start-Sleep -Milliseconds 200
}

Write-Host ""
Write-Host "--- Distribution Summary ---" -ForegroundColor Yellow
foreach ($key in $nodesSeen.Keys) {
    Write-Host "  Node $key handled $($nodesSeen[$key]) request(s)" -ForegroundColor Green
}

Write-Host ""
# 3. Test Direct VPS Node Ports
Write-Host "[3/3] Checking direct access to individual VPS ports..." -ForegroundColor Green
$directNodes = @(
    @{ Name = "VPS 1"; Port = 5181; ExpectedIP = "10.20.0.11" },
    @{ Name = "VPS 2"; Port = 5182; ExpectedIP = "10.20.0.12" },
    @{ Name = "VPS 3"; Port = 5184; ExpectedIP = "10.20.0.13" }
)

foreach ($node in $directNodes) {
    try {
        $directResponse = Invoke-RestMethod -Uri "http://localhost:$($node.Port)/health" -Method Get -TimeoutSec 3
        Write-Host "  [OK] $($node.Name) (Port $($node.Port), IP $($node.ExpectedIP)) responded directly: status=$($directResponse.status)" -ForegroundColor Green
    }
    catch {
        Write-Host "  [WAITING] $($node.Name) (Port $($node.Port)) did not respond (service might still be booting or stopped): $_" -ForegroundColor DarkGray
    }
}

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Cluster verification completed." -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
