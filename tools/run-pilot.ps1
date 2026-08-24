# ProMaxx2 Automation Agent - Pilot Runner (บนเครื่องทดสอบ)
# ตัวอย่างการตั้งค่า env สำหรับรัน pilot กับ PromaxxsPos.exe
# ระวัง: อย่า commit ค่าจริงลง repository — ตั้งค่าผ่านตัวแปร env หรือ secure store

$env:QAHUB_BASE_URL = 'http://localhost:5038/api/v1'
$env:QAHUB_USERNAME = $env:QAHUB_USERNAME   # เช่น 'admin'
$env:QAHUB_PASSWORD = $env:QAHUB_PASSWORD
$env:AGENT_CODE = 'QA-POS-PILOT'
$env:AUT_EXE = 'H:\APP\QAManagementSystem\ProMaxx2\1.0.0-beta.2\PromaxxsPos.exe'
$env:AUT_USER = $env:AUT_USER              # เช่น 'supervisor'
$env:AUT_PASSWORD = $env:AUT_PASSWORD
$env:HEARTBEAT_SECONDS = '5'
$env:ACTION_TIMEOUT_SECONDS = '25'
$env:AUT_DB_TYPE = 'Firebird'
$env:AUT_DB_HOST = '127.0.0.1'
$env:AUT_DB_PORT = '3050'
$env:AUT_DB_USER = 'SYSDBA'
$env:AUT_DB_PASSWORD = $env:AUT_DB_PASSWORD   # เช่น SYSDBA password ของ Firebird
$env:AUT_DB_DATABASE = 'C:\SeniorSoft ProMaxx\FBMAXX.FDB'

Write-Host 'starting agent (pilot)...' -ForegroundColor Cyan
& 'H:\APP\QAManagementSystem\agent\ProMaxx2.Automation.Runner\bin\Debug\net10.0-windows\ProMaxx2.Automation.Runner.exe' 2>&1