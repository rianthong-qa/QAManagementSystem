<#
PowerShell helper to install and configure Cloudflare Tunnel (cloudflared) on Windows.

Usage (run as Administrator when installing service):
  powershell -ExecutionPolicy Bypass -File .\scripts\cloudflare\setup-tunnel.ps1

This script performs these steps interactively:
  - Checks for `cloudflared` and attempts to install via `winget` if missing
  - Runs `cloudflared tunnel login` (opens browser for authorisation)
  - Creates a tunnel with the provided name
  - Writes a simple `config.yml` to %USERPROFILE%\.cloudflared\config.yml
  - Optionally creates a DNS route and installs the tunnel as a service

Note: Some commands (install/service install) require elevated privileges.
#>

param(
    [string]$TunnelName = "QASNS",
    [string]$Hostname = "api.example.com",
    [string]$LocalService = "http://localhost:5000",
    [switch]$InstallAsService
)

function Ensure-Cloudflared {
    $exe = Get-Command cloudflared -ErrorAction SilentlyContinue
    if ($null -ne $exe) {
        Write-Host "cloudflared found at: $($exe.Path)"
        return $true
    }

    Write-Host "cloudflared not found. Attempting to install via winget..."
    try {
        winget install --id Cloudflare.Cloudflared -e --accept-package-agreements --accept-source-agreements -h
    } catch {
        Write-Warning "winget install failed. Please install cloudflared manually from https://developers.cloudflare.com/cloudflare-one/connections/connect-apps/install-and-setup/installation"
        return $false
    }

    # re-check
    $exe = Get-Command cloudflared -ErrorAction SilentlyContinue
    if ($null -ne $exe) {
        Write-Host "cloudflared installed: $($exe.Path)"
        return $true
    }
    return $false
}

if (-not (Ensure-Cloudflared)) {
    Write-Host "Cannot continue without cloudflared. Exiting."
    exit 1
}

Write-Host "Starting tunnel setup..."
Write-Host "(This will open a browser window for Cloudflare login if not already authenticated)"

cloudflared tunnel login

Write-Host "Using parameters: TunnelName=$TunnelName, Hostname=$Hostname, LocalService=$LocalService"

Write-Host "Creating tunnel named: $TunnelName"
$createOutput = cloudflared tunnel create $TunnelName 2>&1
Write-Host $createOutput

# Try to extract tunnel ID
$tunnelId = $null
foreach ($line in $createOutput) {
    if ($line -match "(Tunnel ID:|Created tunnel|ID:|Tunnel created with id)\s*([0-9a-fA-F\-]{10,})") {
        $tunnelId = $Matches[2]
        break
    }
}

if (-not $tunnelId) {
    # attempt a different parse: cloudflared prints the file path
    $credFile = Get-ChildItem "$env:USERPROFILE\.cloudflared" -Filter "*.json" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($credFile) {
        # filename is usually <tunnel-id>.json
        $tunnelId = [IO.Path]::GetFileNameWithoutExtension($credFile.Name)
    }
}

if (-not $tunnelId) {
    Write-Warning "Could not determine tunnel ID automatically. You may need to edit the config manually."
} else {
    Write-Host "Detected tunnel ID: $tunnelId"
}

if ($PSBoundParameters.ContainsKey('Hostname')) {
    $hostname = $Hostname
} else {
    $hostname = Read-Host "Enter hostname to expose (example: api.example.com)"
}

if ([string]::IsNullOrWhiteSpace($hostname)) { $hostname = "api.example.com" }

if ($PSBoundParameters.ContainsKey('LocalService')) {
    $localService = $LocalService
} else {
    $localService = Read-Host "Enter local service URL (example: http://localhost:5000)"
}
if ([string]::IsNullOrWhiteSpace($localService)) { $localService = "http://localhost:5000" }

$userCloudflaredDir = Join-Path $env:USERPROFILE ".cloudflared"
if (-not (Test-Path $userCloudflaredDir)) { New-Item -ItemType Directory -Path $userCloudflaredDir | Out-Null }

$configPath = Join-Path $userCloudflaredDir "config.yml"

if ($tunnelId) {
    $credentialsPath = Join-Path $userCloudflaredDir ($tunnelId + ".json")
} else {
    $credentialsPath = "C:\Users\<YOU>\\.cloudflared\\<TUNNEL_ID>.json"
}

$config = @"
tunnel: $($tunnelId -or 'TUNNEL_ID')
credentials-file: $credentialsPath

ingress:
  - hostname: $hostname
    service: $localService
  - service: http_status:404
"@

Set-Content -Path $configPath -Value $config -Encoding UTF8
Write-Host "Wrote config to: $configPath"

Write-Host "Attempting to create DNS route for $hostname (requires Cloudflare account permissions)..."
try {
    cloudflared tunnel route dns $TunnelName $hostname
} catch {
    Write-Warning "Unable to create DNS route automatically. You can create the DNS CNAME (or let cloudflared create it) in the Cloudflare dashboard under Zero Trust -> Tunnels -> Routes."
}

if ($InstallAsService.IsPresent) {
    Write-Host "Installing tunnel as service (requires elevation)..."
    try {
        cloudflared service install $TunnelName
        Start-Service cloudflared -ErrorAction SilentlyContinue
        Write-Host "Installed and started `cloudflared` service."
    } catch {
        Write-Warning "Service install failed. Try running this script as Administrator, or run:\n  cloudflared service install $TunnelName"
    }
} else {
    Write-Host "Skipping service install. You can run tunnel with: cloudflared tunnel run $TunnelName"
}

Write-Host "Setup complete - check Cloudflare dashboard for Tunnel status and DNS records."
