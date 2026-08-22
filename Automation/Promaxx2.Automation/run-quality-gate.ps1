param(
    [Parameter(Mandatory = $true)][string]$Build,
    [string]$BaselineBuild = "1.0.0-beta.2",
    [string]$ArtifactsDirectory = "artifacts/quality-gate",
    [string]$QaHubUrl,
    [string]$AccessToken,
    [Guid]$ProjectId,
    [Guid]$ReleaseId,
    [Guid]$BuildId,
    [string]$RunnerName = $env:COMPUTERNAME
)

$ErrorActionPreference = "Stop"
$projectRoot = $PSScriptRoot
$runner = Join-Path $projectRoot "src/Promaxx2.Automation.Runner/Promaxx2.Automation.Runner.csproj"
$policy = Join-Path $projectRoot "quality-gate-policy.json"
$outputRoot = Join-Path $projectRoot $ArtifactsDirectory
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

$targets = @(
    @{ Name = "pos"; Manifest = "examples/scanner.pos.json" },
    @{ Name = "app"; Manifest = "examples/scanner.app.json" }
)

$failed = $false
foreach ($target in $targets) {
    $name = $target.Name
    $manifest = Join-Path $projectRoot $target.Manifest
    $current = Join-Path $outputRoot "$name-report.json"
    $registry = Join-Path $outputRoot "$name-registry.md"
    $baseline = Join-Path $projectRoot "baselines/$BaselineBuild/$name.json"
    $gateJson = Join-Path $outputRoot "$name-gate.json"
    $junit = Join-Path $outputRoot "$name-gate.junit.xml"

    if (-not (Test-Path -LiteralPath $baseline)) {
        throw "Approved baseline not found: $baseline"
    }

    dotnet run --project $runner --no-build -- scan --manifest $manifest --build $Build --out $current --registry $registry
    dotnet run --project $runner --no-build -- gate --baseline $baseline --current $current --policy $policy --out $gateJson --junit $junit
    if ($LASTEXITCODE -ne 0) { $failed = $true }

    if ($QaHubUrl) {
        if (-not $AccessToken -or $ProjectId -eq [Guid]::Empty -or $ReleaseId -eq [Guid]::Empty -or $BuildId -eq [Guid]::Empty) {
            throw "QaHubUrl requires AccessToken, ProjectId, ReleaseId and BuildId."
        }
        $gate = Get-Content -Raw -LiteralPath $gateJson | ConvertFrom-Json
        $payload = @{
            projectId = $ProjectId
            releaseId = $ReleaseId
            buildId = $BuildId
            targetApp = $gate.targetApp
            baselineBuild = $gate.baselineBuild
            currentBuild = $gate.currentBuild
            passed = $gate.passed
            newMissingCount = @($gate.newMissingAutomationIds).Count
            newDuplicateCount = @($gate.newDuplicateAutomationIds).Count
            removedCount = @($gate.removedAutomationIds).Count
            changedCount = @($gate.changedAutomationIds).Count
            messages = @($gate.messages)
            runnerName = $RunnerName
            completedAt = [DateTime]::UtcNow
        } | ConvertTo-Json -Depth 4
        $headers = @{ Authorization = "Bearer $AccessToken" }
        $baseUrl = $QaHubUrl.TrimEnd('/')
        Invoke-RestMethod -Method Post -Uri "$baseUrl/api/v1/automation/quality-gates?projectId=$ProjectId" -Headers $headers -ContentType "application/json" -Body $payload | Out-Null
        Write-Host "Published $name quality gate to QA Hub build $BuildId"
    }
}

if ($failed) {
    Write-Error "AutomationId quality gate failed. Review artifacts in $outputRoot"
    exit 3
}

Write-Host "AutomationId quality gate passed for build $Build"
