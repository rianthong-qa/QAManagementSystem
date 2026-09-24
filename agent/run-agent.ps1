# ProMaxx2 Automation Agent - Runner Script
# รัน Runner แบบวนลูปรอ Job จาก QA Hub โดยอ่านค่าจาก User environment variables ที่ตั้งด้วย set-agent-env.ps1
# AUT-AGT-004: ห้ามใส่รหัสผ่านในไฟล์นี้ (ไฟล์อยู่ใน git) — รหัสผ่านถูกเก็บแบบเข้ารหัส DPAPI ในตัวแปร *_DPAPI
# ของ Windows user ที่รัน Agent และถอดรหัสได้เฉพาะ user นั้นบนเครื่องนี้
param(
  [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug'
)
$ErrorActionPreference = 'Stop'

# terminal ที่เปิดไว้ก่อนรัน set-agent-env.ps1 ยังไม่เห็นค่าใหม่ — โหลดจาก User scope ให้ตรงกับที่ตั้งไว้ล่าสุด
$names = 'QAHUB_BASE_URL', 'QAHUB_USERNAME', 'QAHUB_PASSWORD_DPAPI', 'AGENT_CODE', 'AUT_EXE', 'AUT_USER', 'AUT_PASSWORD_DPAPI',
  'AUT_FDB_PATH', 'AUT_DB_TYPE', 'AUT_DB_HOST', 'AUT_DB_PORT', 'AUT_DB_USER', 'AUT_DB_PASSWORD_DPAPI', 'AUT_DB_DATABASE',
  'QAHUB_ALLOW_INSECURE_HTTP', 'AUT_CLOSE_EXISTING'
foreach ($name in $names) {
  $value = [Environment]::GetEnvironmentVariable($name, 'User')
  if ($value) { Set-Item -Path "Env:$name" -Value $value }
}

if (-not $env:QAHUB_BASE_URL -or -not $env:QAHUB_USERNAME -or -not ($env:QAHUB_PASSWORD_DPAPI -or $env:QAHUB_PASSWORD)) {
  Write-Host 'ยังไม่ได้ตั้งค่า Agent — รัน .\set-agent-env.ps1 ก่อน' -ForegroundColor Red
  exit 2
}
if (-not $env:AGENT_CODE) { $env:AGENT_CODE = $env:COMPUTERNAME }

$runner = Join-Path $PSScriptRoot "ProMaxx2.Automation.Runner\bin\$Configuration\net10.0-windows\ProMaxx2.Automation.Runner.exe"
if (-not (Test-Path $runner)) {
  Write-Host "ไม่พบ $runner" -ForegroundColor Red
  Write-Host "build ก่อน: dotnet build `"$PSScriptRoot\ProMaxx2.Automation.slnx`" -c $Configuration" -ForegroundColor Yellow
  exit 2
}

Write-Host '== ProMaxx2 Automation Agent ==' -ForegroundColor Cyan
Write-Host "Hub     : $env:QAHUB_BASE_URL"
Write-Host "Agent   : $env:AGENT_CODE"
Write-Host "AUT     : $env:AUT_EXE"
Write-Host 'กด Ctrl+C เพื่อหยุด agent' -ForegroundColor Yellow
& $runner
exit $LASTEXITCODE
