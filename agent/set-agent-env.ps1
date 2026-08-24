# set-agent-env.ps1
# ตั้ง User environment variables สำหรับ ProMaxx2 Automation Agent (ค่าอยู่เฉพาะเครื่องนี้)
$ErrorActionPreference = 'Stop'
function Set-UserEnv([string]$name, [string]$value) {
  if ($value) { [Environment]::SetEnvironmentVariable($name, $value, 'User') }
}

$baseUrl   = Read-Host 'QA Hub Base URL (default http://localhost:5038/api/v1)'
Set-UserEnv 'QAHUB_BASE_URL' $(if ($baseUrl) { $baseUrl } else { 'http://localhost:5038/api/v1' })
$username  = Read-Host 'QA Hub Username (ต้องมีสิทธิ์ AUTOMATION.EXECUTE)'
Set-UserEnv 'QAHUB_USERNAME' $username
$password  = Read-Host 'QA Hub Password'
Set-UserEnv 'QAHUB_PASSWORD' $password
Set-UserEnv 'AGENT_CODE' $env:COMPUTERNAME
$autExe    = Read-Host 'Path PromaxxsPos.exe (default H:\APP\QAManagementSystem\ProMaxx2\1.0.0-beta.2\PromaxxsPos.exe)'
Set-UserEnv 'AUT_EXE' $(if ($autExe) { $autExe } else { 'H:\APP\QAManagementSystem\ProMaxx2\1.0.0-beta.2\PromaxxsPos.exe' })
$autUser   = Read-Host 'ProMaxx2 Test Username'
Set-UserEnv 'AUT_USER' $autUser
$autPass   = Read-Host 'ProMaxx2 Test Password'
Set-UserEnv 'AUT_PASSWORD' $autPass

Write-Host ''
Write-Host 'Agent environment set. เปิด terminal ใหม่ แล้วรัน: .\run-agent.ps1' -ForegroundColor Green