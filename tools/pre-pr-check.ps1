<#
.SYNOPSIS
  ตรวจงานอัตโนมัติก่อนเปิด Pull Request (Pre-PR Review Gate) — ดูขั้นตอนเต็มใน
  Document/02-Developer-Blueprint/PRE_PR_REVIEW.md

.DESCRIPTION
  เทียบ branch ปัจจุบันกับ -Base (ค่าเริ่มต้น main) รวมไฟล์ที่ยังไม่ได้ commit แล้วรันเฉพาะส่วนที่เกี่ยวกับไฟล์ที่เปลี่ยน:
    1. Git: branch / ไฟล์ค้าง / ไฟล์ต้องห้าม (bin, obj, node_modules, App_Data, .env) / ไฟล์ใหญ่
    2. git diff --check (whitespace) และข้อความไทยเสีย (mojibake)
    3. หาข้อมูลลับในบรรทัดที่เพิ่ม (password, key, token, connection string)
    4. Backend: dotnet build (output แยก ไม่ชน DLL ที่ API รันอยู่) + dotnet test
    5. Frontend: npm build + lint (ห้ามมี warning) + tsc + vitest
    6. Automation Agent: dotnet test agent/ProMaxx2.Automation.slnx
    7. กฎเอกสารจาก AGENTS.md (UI_DESIGN_SYSTEM.md / AUTOMATION_TODO.md) และ migration
  ผลแต่ละข้อเป็น PASS / WARN / FAIL — มี FAIL อย่างน้อย 1 ข้อ = exit code 1 (ห้ามเปิด PR)

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\pre-pr-check.ps1
  powershell -ExecutionPolicy Bypass -File tools\pre-pr-check.ps1 -Base main -SkipTests
#>
[CmdletBinding()]
param(
    [string]$Base = "main",
    [switch]$SkipBuild,   # ข้าม build/test ทั้งหมด (ตรวจเฉพาะ git/เอกสาร/ข้อมูลลับ — ใช้ตอนแก้เอกสารล้วน)
    [switch]$SkipTests    # build แต่ไม่รัน unit test
)

$ErrorActionPreference = "Continue"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $root
$work = Join-Path $env:TEMP "qa-pre-pr"
New-Item -ItemType Directory -Force $work | Out-Null

$results = New-Object System.Collections.Generic.List[object]
function Add-Result([string]$Check, [string]$Status, [string]$Detail) {
    $results.Add([pscustomobject]@{ Check = $Check; Status = $Status; Detail = $Detail })
    $color = "Green"
    if ($Status -eq "WARN") { $color = "Yellow" }
    if ($Status -eq "FAIL") { $color = "Red" }
    if ($Status -eq "SKIP") { $color = "DarkGray" }
    Write-Host ("[{0}] {1} - {2}" -f $Status, $Check, $Detail) -ForegroundColor $color
}
function Invoke-Step([string]$Name, [scriptblock]$Command, [string]$LogName) {
    Write-Host ""
    Write-Host "==> $Name" -ForegroundColor Cyan
    $log = Join-Path $work $LogName
    & $Command *> $log
    $code = $LASTEXITCODE
    Get-Content $log -Tail 4 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
    return @{ Code = $code; Log = $log }
}

# ---------------------------------------------------------------- 1. Git และไฟล์ที่เปลี่ยน
$branch = (git rev-parse --abbrev-ref HEAD).Trim()
git rev-parse --verify --quiet $Base | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "ไม่พบ branch '$Base' — ระบุด้วย -Base" -ForegroundColor Red; exit 2 }
$mergeBase = (git merge-base $Base HEAD).Trim()

if ($branch -eq $Base) { Add-Result "Branch" "FAIL" "อยู่บน $Base — ต้องแยก branch ก่อนเปิด PR" }
else { Add-Result "Branch" "PASS" "$branch (เทียบกับ $Base ที่ $($mergeBase.Substring(0,7)))" }

$committed = @(git diff --name-only "$mergeBase" HEAD)
$pending = @(git status --porcelain | ForEach-Object { $_.Substring(3).Trim('"') })
$changed = @($committed + $pending | Where-Object { $_ } | Sort-Object -Unique)
if ($pending.Count -gt 0) { Add-Result "Uncommitted" "WARN" "$($pending.Count) ไฟล์ยังไม่ได้ commit — PR จะไม่มีไฟล์เหล่านี้" }
else { Add-Result "Uncommitted" "PASS" "ไม่มีไฟล์ค้าง" }
if ($changed.Count -eq 0) { Add-Result "Changes" "FAIL" "ไม่มีไฟล์ที่เปลี่ยนเทียบกับ $Base"; }
else { Add-Result "Changes" "PASS" "$($changed.Count) ไฟล์" }

$forbidden = @($changed | Where-Object { $_ -match '(^|/)(bin|obj|node_modules|dist|App_Data|\.vs)/|(^|/)\.env($|\.)|\.(pfx|p12|key)$' })
if ($forbidden.Count -gt 0) { Add-Result "Forbidden files" "FAIL" ($forbidden -join ", ") }
else { Add-Result "Forbidden files" "PASS" "ไม่มี bin/obj/node_modules/dist/App_Data/.env/key" }

$large = @($changed | Where-Object { (Test-Path $_ -PathType Leaf) -and ((Get-Item $_).Length -gt 5MB) })
if ($large.Count -gt 0) { Add-Result "Large files" "WARN" ("ไฟล์เกิน 5 MB: " + ($large -join ", ")) }
else { Add-Result "Large files" "PASS" "ไม่มีไฟล์เกิน 5 MB" }

# ---------------------------------------------------------------- 2. Whitespace / mojibake
$check1 = git diff --check "$mergeBase" HEAD 2>&1
$check2 = git diff --check 2>&1
$untracked = @(git ls-files --others --exclude-standard)
$trailing = @($untracked | Where-Object { (Test-Path $_ -PathType Leaf) -and $_ -match '\.(cs|ts|tsx|css|md|ps1|json|sql)$' -and (Select-String -Path $_ -Pattern '[ \t]+$' -Quiet) })
if ($check1 -or $check2 -or $trailing.Count -gt 0) {
    $sample = (@($check1) + @($check2) + $trailing | Where-Object { $_ } | Select-Object -First 3) -join " | "
    Add-Result "Whitespace" "FAIL" ("git diff --check ไม่ผ่าน: " + $sample)
} else { Add-Result "Whitespace" "PASS" "git diff --check ผ่าน" }

$addedLines = @(git diff -U0 "$mergeBase" 2>$null | Where-Object { $_ -match '^\+' -and $_ -notmatch '^\+\+\+' })
foreach ($file in $untracked) {
    if ((Test-Path $file -PathType Leaf) -and $file -match '\.(cs|ts|tsx|css|md|ps1|json|sql|config|yml|yaml)$') {
        $addedLines += @(Get-Content $file -Encoding UTF8 | ForEach-Object { "+$_" })
    }
}
# UTF-8 ภาษาไทยที่ถูกอ่านเป็น Latin-1 จะขึ้นต้นด้วยอักขระ U+00E0 ตามด้วย U+00B8/U+00B9 — สร้าง pattern จากรหัสอักขระ ไม่ให้สคริปต์นี้จับตัวเอง
$mojibakePattern = ([string][char]0xE0 + [char]0xB8) + "|" + ([string][char]0xE0 + [char]0xB9)
$mojibake = @($addedLines | Where-Object { $_ -match $mojibakePattern })
if ($mojibake.Count -gt 0) { Add-Result "Thai encoding" "FAIL" "พบข้อความไทยเสีย (mojibake) $($mojibake.Count) บรรทัด" }
else { Add-Result "Thai encoding" "PASS" "ไม่พบ mojibake" }

# ---------------------------------------------------------------- 3. ข้อมูลลับ
$secretPattern = '(?i)(password|passwd|pwd|secret|client_secret|api[_-]?key|access[_-]?token)\s*[:=]\s*["''][^"''\s]{6,}["'']|Jwt__Key\s*=\s*["''][^"'']{16,}|Bearer\s+eyJ[A-Za-z0-9_-]{10,}|-----BEGIN [A-Z ]*PRIVATE KEY-----|(?i)Password=[^;"\s]{4,};|AKIA[0-9A-Z]{16}|ghp_[A-Za-z0-9]{30,}'
$secrets = @($addedLines | Where-Object { $_ -match $secretPattern -and $_ -notmatch '(?i)example|placeholder|\*\*\*|<.*>|test' })
if ($secrets.Count -gt 0) {
    Add-Result "Secrets" "FAIL" ("พบบรรทัดที่อาจเป็นข้อมูลลับ $($secrets.Count) บรรทัด เช่น: " + ($secrets[0].Substring(0, [Math]::Min(80, $secrets[0].Length))))
} else { Add-Result "Secrets" "PASS" "ไม่พบ password/key/token ในบรรทัดที่เพิ่ม" }

# ---------------------------------------------------------------- ขอบเขตของงาน
$backendChanged = @($changed | Where-Object { $_ -match '^src/ProMaxx2\.QA\.(Api|Application|Domain|Infrastructure)/|^tests/' }).Count -gt 0
$frontendChanged = @($changed | Where-Object { $_ -match '^src/ProMaxx2\.QA\.Web/' }).Count -gt 0
$agentChanged = @($changed | Where-Object { $_ -match '^agent/' }).Count -gt 0
$uiChanged = @($changed | Where-Object { $_ -match '^src/ProMaxx2\.QA\.Web/src/.*\.(tsx|css)$' }).Count -gt 0
$automationChanged = @($changed | Where-Object { $_ -match '(?i)^agent/|automation' -and $_ -notmatch '(?i)AUTOMATION_TODO\.md$' }).Count -gt 0
$entityChanged = @($changed | Where-Object { $_ -match '^src/ProMaxx2\.QA\.(Domain|Infrastructure)/.*\.cs$' -and $_ -notmatch '/Migrations/' }).Count -gt 0
$migrationAdded = @($changed | Where-Object { $_ -match '/Migrations/' }).Count -gt 0

# ---------------------------------------------------------------- 4-6. Build / Test
if ($SkipBuild) {
    Add-Result "Build & tests" "SKIP" "-SkipBuild"
} else {
    if ($backendChanged) {
        $r = Invoke-Step "Backend build" { dotnet build src/ProMaxx2.QA.Api --nologo -o (Join-Path $work "api") } "backend-build.log"
        $warnings = @(Select-String -Path $r.Log -Pattern ': warning [A-Z]+\d+' | Where-Object { $_.Line -notmatch 'MSB3026' })
        if ($r.Code -ne 0) { Add-Result "Backend build" "FAIL" "ดู $($r.Log)" }
        elseif ($warnings.Count -gt 0) { Add-Result "Backend build" "WARN" "$($warnings.Count) warning — ดู $($r.Log)" }
        else { Add-Result "Backend build" "PASS" "0 warning / 0 error" }
        if ($SkipTests) { Add-Result "Backend tests" "SKIP" "-SkipTests" }
        else {
            $r = Invoke-Step "Backend unit tests" { dotnet test tests/ProMaxx2.QA.UnitTests --nologo --output (Join-Path $work "tests") } "backend-test.log"
            $summary = (Select-String -Path $r.Log -Pattern '(Passed|Failed)!.*' | Select-Object -Last 1).Line
            if ($r.Code -ne 0) { Add-Result "Backend tests" "FAIL" "$summary — ดู $($r.Log)" } else { Add-Result "Backend tests" "PASS" ("$summary".Trim()) }
        }
    } else { Add-Result "Backend" "SKIP" "ไม่มีไฟล์ backend เปลี่ยน" }

    if ($frontendChanged) {
        Push-Location src/ProMaxx2.QA.Web
        $r = Invoke-Step "Frontend build" { npm.cmd run build } "web-build.log"
        if ($r.Code -ne 0) { Add-Result "Frontend build" "FAIL" "ดู $($r.Log)" } else { Add-Result "Frontend build" "PASS" "tsc -b && vite build" }
        $r = Invoke-Step "Frontend lint" { npm.cmd run lint } "web-lint.log"
        $lintWarn = @(Select-String -Path $r.Log -Pattern '(?i)\b[1-9]\d* warnings?\b|\b[1-9]\d* errors?\b')
        if ($r.Code -ne 0 -or $lintWarn.Count -gt 0) { Add-Result "Frontend lint" "FAIL" "oxlint ต้องไม่มี warning — ดู $($r.Log)" } else { Add-Result "Frontend lint" "PASS" "oxlint ไม่มี warning" }
        $r = Invoke-Step "Type check" { npx.cmd tsc --noEmit -p tsconfig.app.json } "web-tsc.log"
        if ($r.Code -ne 0) { Add-Result "Type check" "FAIL" "ดู $($r.Log)" } else { Add-Result "Type check" "PASS" "tsc -p tsconfig.app.json" }
        if ($SkipTests) { Add-Result "Frontend tests" "SKIP" "-SkipTests" }
        else {
            $r = Invoke-Step "Frontend tests" { npm.cmd run test } "web-test.log"
            $summary = (Select-String -Path $r.Log -Pattern 'Tests\s+\d+ (passed|failed)' -CaseSensitive | Select-Object -Last 1).Line
            if ($r.Code -ne 0) { Add-Result "Frontend tests" "FAIL" "ดู $($r.Log)" } else { Add-Result "Frontend tests" "PASS" ("$summary".Trim()) }
        }
        Pop-Location
    } else { Add-Result "Frontend" "SKIP" "ไม่มีไฟล์ frontend เปลี่ยน" }

    if ($agentChanged -and -not $SkipTests) {
        $r = Invoke-Step "Agent tests" { dotnet test agent/ProMaxx2.Automation.slnx --nologo } "agent-test.log"
        if ($r.Code -ne 0) { Add-Result "Agent tests" "FAIL" "ดู $($r.Log)" } else { Add-Result "Agent tests" "PASS" "agent/ProMaxx2.Automation.slnx" }
    }
}

# ---------------------------------------------------------------- 7. กฎเอกสาร (AGENTS.md) และ migration
if ($uiChanged) {
    if ($changed -contains "Document/02-Developer-Blueprint/UI_DESIGN_SYSTEM.md") { Add-Result "UI docs" "PASS" "อัปเดต UI_DESIGN_SYSTEM.md แล้ว" }
    else { Add-Result "UI docs" "WARN" "แก้ UI แต่ไม่ได้อัปเดต UI_DESIGN_SYSTEM.md — ถ้ามี pattern/กฎใหม่ต้องเพิ่ม Change Log" }
}
if ($automationChanged) {
    if ($changed -contains "Document/03-Architecture-and-Plan/AUTOMATION_TODO.md") { Add-Result "Automation docs" "PASS" "อัปเดต AUTOMATION_TODO.md แล้ว" }
    else { Add-Result "Automation docs" "FAIL" "งาน Automation ต้องอัปเดต AUTOMATION_TODO.md (สถานะ + Progress Log) ตาม AGENTS.md" }
}
if ($entityChanged -and -not $migrationAdded) {
    Add-Result "Migration" "WARN" "แก้ Domain/Infrastructure แต่ไม่มีไฟล์ใน Migrations — ถ้าเปลี่ยน schema ต้องเพิ่ม migration (ApplyMigrations=false: apply ด้วยมือ)"
}

# ---------------------------------------------------------------- สรุป
Write-Host ""
Write-Host "================ สรุป Pre-PR Check ================" -ForegroundColor Cyan
$results | Format-Table -AutoSize -Wrap | Out-String -Width 200 | Write-Host
$fail = @($results | Where-Object Status -eq "FAIL").Count
$warn = @($results | Where-Object Status -eq "WARN").Count
Write-Host "Log อยู่ที่ $work"
Write-Host "ขั้นถัดไป: ทำ checklist ใน Document/02-Developer-Blueprint/PRE_PR_REVIEW.md (ทดสอบจริง Desktop/Mobile + review diff + /code-review)"
if ($fail -gt 0) {
    Write-Host "ผล: ไม่ผ่าน ($fail FAIL, $warn WARN) — แก้ก่อนเปิด PR" -ForegroundColor Red
    exit 1
}
Write-Host "ผล: ผ่าน ($warn WARN) — ตรวจ WARN แล้วอธิบายใน PR ถ้าไม่แก้" -ForegroundColor Green
exit 0
