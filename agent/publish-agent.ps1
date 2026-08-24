# publish-agent.ps1
# Build deployable Agent (self-contained win-x64) into agent\publish
# Output: publish\ProMaxx2.Automation.AgentGui.exe + ProMaxx2.Automation.Runner.exe + dependencies
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'publish'
$dotnet = 'dotnet'

Write-Host "Publishing Agent -> $out" -ForegroundColor Cyan
& $dotnet publish (Join-Path $PSScriptRoot 'ProMaxx2.Automation.Runner') -c Release -r win-x64 --self-contained true -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnet publish (Join-Path $PSScriptRoot 'ProMaxx2.Automation.AgentGui') -c Release -r win-x64 --self-contained true -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ''
Write-Host "Done. Copy '$out' to the test machine and run ProMaxx2.Automation.AgentGui.exe" -ForegroundColor Green
Write-Host 'Note: settings are saved as agent-config.json next to the exe (passwords encrypted with DPAPI).'