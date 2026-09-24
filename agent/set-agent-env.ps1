# set-agent-env.ps1
# ตั้ง User environment variables สำหรับ ProMaxx2 Automation Agent (ค่าอยู่เฉพาะเครื่องนี้)
# AUT-AGT-004: รหัสผ่านทุกตัวอ่านแบบซ่อนตัวอักษร แล้วเก็บเป็น NAME_DPAPI (ConvertFrom-SecureString = Windows DPAPI
# ของ user ปัจจุบัน) — ถอดรหัสได้เฉพาะ Windows user นี้บนเครื่องนี้ จึงต้องรันสคริปต์นี้ด้วย user เดียวกับที่รัน Agent
$ErrorActionPreference = 'Stop'

function Set-UserEnv([string]$name, [string]$value) {
  [Environment]::SetEnvironmentVariable($name, $value, 'User')
}

function Read-Default([string]$prompt, [string]$default) {
  $value = Read-Host "$prompt (default $default)"
  if ($value) { $value } else { $default }
}

function Set-UserSecret([string]$name, [string]$prompt) {
  $secure = Read-Host $prompt -AsSecureString
  if ($secure.Length -eq 0) {
    Write-Host "  ข้าม $name (คงค่าเดิมไว้)" -ForegroundColor DarkGray
    return
  }
  Set-UserEnv "${name}_DPAPI" (ConvertFrom-SecureString $secure)
  Set-UserEnv $name $null # ลบค่า plaintext จากสคริปต์เวอร์ชันเก่า (ถ้ามี)
}

$baseUrl = Read-Default 'QA Hub Base URL' 'https://api-promaxx2.qahub.store/api/v1'
if ($baseUrl -like 'http://*' -and $baseUrl -notmatch '^http://(localhost|127\.0\.0\.1|\[::1\])([:/]|$)') {
  Write-Host 'คำเตือน: http ไปยังเครื่องอื่นจะส่งรหัสผ่านแบบไม่เข้ารหัส Agent จะไม่ยอมเชื่อมต่อจนกว่าจะเปลี่ยนเป็น https' -ForegroundColor Yellow
  Write-Host '         (ถ้าจำเป็นจริง ๆ ในเครือข่ายภายใน ให้ตั้ง QAHUB_ALLOW_INSECURE_HTTP=true เอง)' -ForegroundColor Yellow
}
Set-UserEnv 'QAHUB_BASE_URL' $baseUrl.TrimEnd('/')
Set-UserEnv 'QAHUB_USERNAME' (Read-Host 'QA Hub Username (ต้องมีสิทธิ์ AUTOMATION.EXECUTE)')
Set-UserSecret 'QAHUB_PASSWORD' 'QA Hub Password'
Set-UserEnv 'AGENT_CODE' (Read-Default 'Agent Code' $env:COMPUTERNAME)

Set-UserEnv 'AUT_EXE' (Read-Default 'Path PromaxxsPos.exe' 'H:\APP\QAManagementSystem\ProMaxx2\1.0.0-beta.2\PromaxxsPos.exe')
Set-UserEnv 'AUT_USER' (Read-Host 'ProMaxx2 Test Username')
Set-UserSecret 'AUT_PASSWORD' 'ProMaxx2 Test Password'
Set-UserSecret 'AUT_DB_PASSWORD' 'Database Password สำหรับ DB assertion/snapshot (Enter เพื่อข้าม)'

Write-Host ''
Write-Host 'ตั้งค่า Agent แล้ว — รหัสผ่านถูกเก็บแบบเข้ารหัส (*_DPAPI) รัน: .\run-agent.ps1' -ForegroundColor Green
