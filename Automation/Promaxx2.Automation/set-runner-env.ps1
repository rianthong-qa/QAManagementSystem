[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [Parameter(Mandatory = $true)][string]$Username,
    [Parameter(Mandatory = $true)][string]$Password,
    [Parameter(Mandatory = $true)][string]$PosExe,
    [Parameter(Mandatory = $true)][string]$AppExe,
    [string]$FdbPath = '',
    [string]$PosLoginUser = '',
    [string]$PosLoginPassword = '',
    [string]$AppLoginUser = '',
    [string]$AppLoginPassword = ''
)

$ErrorActionPreference = 'Stop'

# Normalize base URL to end with '/'
$BaseUrl = $BaseUrl.Trim()
if (-not $BaseUrl.EndsWith('/')) { $BaseUrl += '/' }

$values = [ordered]@{
    'QAHUB_BASE_URL' = $BaseUrl
    'QAHUB_USERNAME' = $Username
    'QAHUB_PASSWORD' = $Password
    'AUT_POS_EXE'     = $PosExe
    'AUT_APP_EXE'     = $AppExe
    'AUT_FDB_PATH'    = $FdbPath
    'AUT_POS_USERNAME'= $PosLoginUser
    'AUT_POS_PASSWORD'= $PosLoginPassword
    'AUT_APP_USERNAME'= $AppLoginUser
    'AUT_APP_PASSWORD'= $AppLoginPassword
}

$missing = @()
foreach ($entry in $values.GetEnumerator()) {
    if ($entry.Key -in @('QAHUB_BASE_URL','QAHUB_USERNAME','QAHUB_PASSWORD','AUT_POS_EXE','AUT_APP_EXE') -and [string]::IsNullOrWhiteSpace($entry.Value)) {
        $missing += $entry.Key
    }
}
if ($missing.Count) { throw "Required variables missing: $($missing -join ', ')" }

foreach ($entry in $values.GetEnumerator()) {
    [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'User')
}

Write-Host 'Set the following User environment variables:' -ForegroundColor Green
foreach ($entry in $values.GetEnumerator()) {
    $display = if ($entry.Key -match 'PASSWORD') { if ([string]::IsNullOrWhiteSpace($entry.Value)) { '(empty)' } else { '(set)' } } else { $entry.Value }
    Write-Host ("  {0,-18}= {1}" -f $entry.Key, $display)
}

Write-Host ""
Write-Host "Restart the QA Hub or the .NET runner session to pick up the new values." -ForegroundColor Yellow
Write-Host "Then register the worker task:" -ForegroundColor Yellow
Write-Host '  .\install-worker-task.ps1 -ProjectId <ProjectGUID> -Targets pos,app'
