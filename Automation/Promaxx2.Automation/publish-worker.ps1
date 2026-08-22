[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$Output = ''
)

$ErrorActionPreference = 'Stop'

$automationRoot = $PSScriptRoot
$runnerProject = Join-Path $automationRoot 'src\Promaxx2.Automation.Runner\Promaxx2.Automation.Runner.csproj'
if (-not (Test-Path -LiteralPath $runnerProject)) { throw "Runner project not found: $runnerProject" }

if ([string]::IsNullOrWhiteSpace($Output)) { $Output = Join-Path $automationRoot 'publish' }
New-Item -ItemType Directory -Force -Path $Output | Out-Null

Write-Host "Publishing Promaxx2.Automation.Runner ($Configuration / $Runtime / self-contained single-file)..."
& dotnet publish $runnerProject `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    --output $Output

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

$exe = Join-Path $Output 'Promaxx2.Automation.Runner.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "Publish succeeded but $exe was not found." }

Write-Host ""
Write-Host "Publish complete:" -ForegroundColor Green
Write-Host "  exe: $exe"
Write-Host ""
Write-Host "Next: set environment variables (.\set-runner-env.ps1) then register the worker task"
Write-Host "  .\install-worker-task.ps1 -ProjectId <ProjectGUID> -Targets pos,app"
