# ProMaxx2 Automation Agent - Runner Script
# ตั้งค่า User environment variables สำหรับ Agent แล้วรัน Runner แบบวนลูปรอ Job จาก QA Hub
$ErrorActionPreference = 'Stop'

# ---- ตั้งค่าครั้งเดียว (ใส่ค่าจริง) ----
$env:QAHUB_BASE_URL   = 'http://localhost:5038/api/v1'
$env:QAHUB_USERNAME   = 'qa_lead'
$env:QAHUB_PASSWORD   = '<รหัสผ่าน QA Hub>'
$env:AGENT_CODE       = $env:COMPUTERNAME
$env:AUT_EXE          = 'H:\APP\QAManagementSystem\ProMaxx2\1.0.0-beta.2\PromaxxsPos.exe'
$env:AUT_USER         = 'admin'
$env:AUT_PASSWORD     = '<รหัสผ่าน ProMaxx2>'
$env:AUT_FDB_PATH     = 'H:\APP\QAManagementSystem\ProMaxx2\1.0.0-beta.2\DB\FBMAXX2.FDB'
# --------------------------

Write-Host "== ProMaxx2 Automation Agent ==" -ForegroundColor Cyan
Write-Host "Hub     : $env:QAHUB_BASE_URL"
Write-Host "Agent   : $env:AGENT_CODE"
Write-Host "AUT     : $env:AUT_EXE"
Write-Host "กด Ctrl+C เพื่อหยุด agent" -ForegroundColor Yellow
& "$PSScriptRoot\..\ProMaxx2.Automation.Runner\bin\Debug\net10.0-windows\ProMaxx2.Automation.Runner.exe"