# Run this script once from Windows PowerShell opened with "Run as administrator".
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$serviceName = 'cloudflared'
$currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($currentIdentity)
$administratorRole = [Security.Principal.WindowsBuiltInRole]::Administrator

if (-not $principal.IsInRole($administratorRole)) {
    throw 'Open PowerShell with Run as administrator, then run this script again.'
}

$service = Get-Service -Name $serviceName -ErrorAction Stop
$userSid = $currentIdentity.User.Value
$currentSddl = (& sc.exe sdshow $serviceName | Where-Object { $_ -match '^D:' } | Select-Object -First 1).Trim()
if (-not $currentSddl) { throw 'Unable to read the service security descriptor.' }

$accessEntry = "(A;;LCRPWP;;;$userSid)"
if ($currentSddl.Contains($accessEntry)) {
    Write-Host "Account $($currentIdentity.Name) can already control $serviceName."
    exit 0
}

$newSddl = $currentSddl + $accessEntry
& sc.exe sdset $serviceName $newSddl
if ($LASTEXITCODE -ne 0) { throw 'Unable to update the service permissions.' }

Write-Host "Granted Start/Stop permission for $($service.DisplayName) to $($currentIdentity.Name)."
Write-Host 'Return to System Monitor and use Restart. The API does not need to run as administrator.'
