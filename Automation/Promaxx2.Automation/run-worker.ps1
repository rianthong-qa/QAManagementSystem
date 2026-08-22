[CmdletBinding()]
param(
    [Guid]$ProjectId,
    [string]$ProjectCode = '',
    [ValidateSet('pos', 'app', 'pos,app')][string]$Targets = 'pos,app',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$PublishDir = '',
    [string]$WorkDir = 'queue-work',
    [string]$LogFile = ''
)

$ErrorActionPreference = 'Stop'

if ($ProjectId -eq [Guid]::Empty -and [string]::IsNullOrWhiteSpace($ProjectCode)) {
    throw "Provide either -ProjectId <GUID> or -ProjectCode <Code>."
}

$automationRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($PublishDir)) { $PublishDir = Join-Path $automationRoot 'publish' }

$exe = Join-Path $PublishDir 'Promaxx2.Automation.Runner.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    Write-Host "Runner exe not found ($exe). Publishing now..."
    & (Join-Path $automationRoot 'publish-worker.ps1') -Configuration $Configuration -Output $PublishDir
    if ($LASTEXITCODE -ne 0) { throw 'publish-worker.ps1 failed.' }
    if (-not (Test-Path -LiteralPath $exe)) { throw "Still no exe at $exe" }
}

if ([string]::IsNullOrWhiteSpace($LogFile)) { $LogFile = Join-Path (Join-Path $automationRoot $WorkDir) 'worker.log' }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $LogFile) | Out-Null

# Resolve Project GUID from code (via logged-in QA Hub account)
if ($ProjectId -eq [Guid]::Empty -and -not [string]::IsNullOrWhiteSpace($ProjectCode)) {
    Write-Host "Resolving Project GUID for code '$ProjectCode' from QA Hub (using QAHUB_USERNAME/PASSWORD)..."
    $resolved = (& $exe projects --code $ProjectCode 2>$null | Select-Object -Last 1).Trim()
    if ($resolved -notmatch '^[0-9a-fA-F-]{36}$') { throw "Cannot resolve project '$ProjectCode' to a GUID. Output: $resolved" }
    $ProjectId = [Guid]$resolved
    Write-Host "  resolved ProjectId = $ProjectId"
}

# Build arguments
$arguments = "worker --project $ProjectId --targets $Targets --work-dir `"$WorkDir`" --log `"$LogFile`""

Write-Host "Starting runner in a minimized hidden window:" -ForegroundColor Cyan
Write-Host "  exe:  $exe"
Write-Host "  args: $arguments"
Write-Host "  log:  $LogFile"
Write-Host ""

Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $automationRoot -WindowStyle Minimized
Write-Host "Worker launched. It runs as a separate program on this machine (minimized)." -ForegroundColor Green
Write-Host "Verify in QA Hub: Automation -> Windows Runner Agents shows this machine as Online."
