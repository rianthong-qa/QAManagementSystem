[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][Guid]$ProjectId,
    [ValidateSet('pos', 'app', 'pos,app')][string]$Targets = 'pos,app',
    [string]$TaskName = 'Promaxx2 QA Automation Worker',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$automationRoot = $PSScriptRoot
$runnerProject = Join-Path $automationRoot 'src\Promaxx2.Automation.Runner\Promaxx2.Automation.Runner.csproj'
$requiredVariables = 'QAHUB_USERNAME', 'QAHUB_PASSWORD', 'AUT_POS_EXE', 'AUT_APP_EXE'
$missing = $requiredVariables | Where-Object { [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_, 'User')) }
if ($missing) { throw "Set these user environment variables before installing the task: $($missing -join ', ')" }
if (-not (Test-Path -LiteralPath $runnerProject)) { throw "Runner project not found: $runnerProject" }

$arguments = "run --project `"$runnerProject`" --configuration $Configuration --no-launch-profile -- worker --project $ProjectId --targets $Targets"
$action = New-ScheduledTaskAction -Execute 'dotnet.exe' -Argument $arguments -WorkingDirectory $automationRoot
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Days 0) -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Description 'Interactive Windows UI Automation worker for QA Hub.' -Force | Out-Null
Write-Host "Installed scheduled task '$TaskName'. It starts at user logon in an interactive desktop session."
