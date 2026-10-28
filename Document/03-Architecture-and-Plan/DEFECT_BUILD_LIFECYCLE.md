# วงจร Defect ข้าม Build / Version

> สถานะ: เฟส 1–4 ทำแล้ว (2026-10-28) · เฟส 5 (เคลียร์ backlog) เป็นงานของทีม
> ใช้คู่กับ `Document/01-System-Blueprint/WORKFLOW.md` §7–§9 และ `DEFECT_KNOWLEDGE_PLAN.md` §4.1/§4.3

## 1. ปัญหาที่แก้

ข้อมูลจริง ณ วันที่ 28 ต.ค. 2026 (ProMaxx2QA):

- **Defect ที่ยังไม่ปิดมี 2,164 รายการ:** Open 1,698 / In Progress 147 / Resolved 319
- **2,096 รายการผูกกับ Build เดียว** (`PMX2-REL-003 beta.2`) และ Open 1,635 รายการไม่มีใครแตะมา 6 สัปดาห์
- **Resolved 319 รายการรอ QA ตรวจซ้ำมา 3–5 สัปดาห์** แต่ทั้งระบบเคย Closed แค่ 12 รายการ

ข้อจำกัดของระบบเดิม:

- Defect จำได้แค่ "เจอใน Build ไหน" ไม่รู้ว่าแก้ใน Build ไหนหรือตรวจซ้ำใน Build ไหน
- ไม่มีกฎว่าสถานะไหนเปลี่ยนไปสถานะไหนได้
- ถ้าเคสที่ผูกกับ Defect ที่ Resolved แล้ว Fail ซ้ำ ระบบสร้าง Defect ใหม่แทนการเปิดตัวเดิมกลับมา
- CRM CLOSE ปิด Defect เองโดยที่ QA ไม่ได้ตรวจซ้ำ

## 2. ความหมายของสถานะ (คงไว้ 5 สถานะ)

| สถานะ | ความหมาย | ใครเปลี่ยน |
|---|---|---|
| Open | ยังไม่ได้แก้ (รวมถึงเปิดกลับมาใหม่ = Reopen) | QA / ระบบ |
| In Progress | Dev กำลังแก้ | Dev |
| Resolved | Dev แก้แล้ว **รอ QA ตรวจซ้ำบน Fix Build** | Dev / CRM FINISH |
| Closed | QA ตรวจซ้ำผ่านแล้ว หรือปิดด้วยผลที่ไม่ใช่ "แก้ไขแล้ว" | QA |
| Rejected | ไม่ใช่ Defect | QA |

Defect เก็บ Build ไว้ 3 แบบ:

| ฟิลด์ | ความหมาย | ตั้งเมื่อไร |
|---|---|---|
| `BuildId` | เจอใน Build ไหน | ตอนสร้าง (แก้ไม่ได้) |
| `FixedInBuildId` | แก้ใน Build ไหน | ตอนเป็น Resolved |
| `VerifiedInBuildId` | ตรวจซ้ำผ่านใน Build ไหน | ตอน Resolved → Closed |

นอกจากนี้ยังมี:

- `ReopenCount` = จำนวนครั้งที่แก้แล้วไม่ผ่าน
- `RegressionOfDefectId` = Defect เดิมที่ปิดไปแล้วแต่ปัญหากลับมาอีก

## 3. กฎการเปลี่ยนสถานะ (`Defect.TransitionTo`)

ทุกช่องทางผ่านกฎเดียวกัน ได้แก่ หน้า Defect, ฟอร์มแก้ไข, Bulk, CRM sync และการสร้าง Defect อัตโนมัติจาก Execution

| จาก → ไป | เงื่อนไข / ผล |
|---|---|
| Open ↔ In Progress | ทำได้อิสระ |
| Open / In Progress → Resolved | บันทึก `FixedInBuildId` (ไม่ระบุ = Build ล่าสุดของ Release — Resolve รอบใหม่หลัง Reopen ตั้งใหม่เสมอ ไม่ใช้ Fix Build รอบก่อน) และ `ResolvedAt` |
| Resolved → Closed | บันทึก `VerifiedInBuildId` (ไม่ระบุ = Fix Build), `ClosedAt` และผลตรวจซ้ำ `LastRetestResult = Pass` (Resolved → Open/In Progress = `Fail`) |
| Open / In Progress / Rejected → Closed | ต้องมีผลการปิดที่**ไม่ใช่** "แก้ไขแล้ว" (Duplicate / Cannot Reproduce / By Design / Won't Fix / Deferred) |
| Resolved / Closed → Open / In Progress | **Reopen:** `ReopenCount` +1, ล้าง Verified Build และผลการปิด แต่เก็บ Fix Build ไว้เป็นประวัติ |
| ทุกสถานะ → Rejected | ทำได้ ยกเว้นจาก Closed (ต้อง Reopen ก่อน) |
| Closed → Resolved, Rejected → Resolved | ไม่ได้ ต้องเปิดกลับเป็น Open ก่อน |

ข้อความ error ทุกข้อเป็นภาษาไทยที่บอกว่าต้องทำอะไรต่อ ส่วน Bulk จะข้ามรายการที่ผิดกฎและแจ้งกลับ (`failed`) โดยรายการอื่นยังบันทึก

**CRM:**
- **FINISH** → Resolved และตั้ง Fix Build เป็น Build ล่าสุดของ Release (activity บอกว่าตั้งอัตโนมัติ, QA แก้ได้ตอนตรวจซ้ำ)
- **CLOSE** → ไม่ปิด Defect เอง แค่บันทึก activity `CrmClosed` ว่ารอ QA ตรวจซ้ำ

**เคส Fail ซ้ำใน Execution** (`DefectAutoCreateService`):

| Defect ที่ผูกกับเคสนั้น | ผล |
|---|---|
| Open / In Progress | ใช้ตัวเดิม ไม่สร้างใหม่ |
| Resolved และ Build ที่ Fail อยู่ตรงหรือหลัง Fix Build (เทียบ `BuildDate`) หรือไม่มี Fix Build | **Reopen ตัวเดิม** |
| Resolved แต่ Build ที่ Fail เก่ากว่า Fix Build | ไม่ Reopen เพราะ Build นี้ยังไม่มีการแก้ |
| Closed ที่ปิดเพราะแก้แล้ว (ผล Fixed หรือมี Verified Build) | **สร้าง Defect ใหม่ที่อ้างตัวเดิม** (`RegressionOfDefectId`) ไม่เปิด Defect ที่ปิดในรอบก่อน |
| Closed ด้วยผลอื่น (Won't Fix, By Design ฯลฯ) หรือ Rejected | สร้าง Defect ใหม่ธรรมดา (ไม่นับเป็น regression) |

## 4. แนวปฏิบัติของทีม QA เมื่อได้ Build ใหม่ (SOP)

1. Dev หรือ Release Owner **ลงทะเบียน Build ในหน้า Release / Build ทุกครั้ง** พร้อม Change Notes (ไม่แจ้งแค่ในแชท) เพราะ Fix Build และคิวตรวจซ้ำอ้าง Build ในระบบ
2. QA รัน Smoke แล้วตั้งสถานะ Build เป็น Testing (ผ่าน) หรือ Blocked (ไม่ผ่าน)
3. เปิด **Release / Build → กดเลข Build → ส่วน "Defect ของ Build นี้"** ดูแท็บ **รอตรวจซ้ำ** (Resolved ที่ Fix Build คือ Build นี้หรือก่อนหน้า หรือไม่ระบุ) แล้วกด **"สร้าง Retest Cycle"** (เลือก Environment) — ระบบสร้าง Cycle ประเภท Regression จาก Test Case ที่ผูกกับ Defect ในคิว แล้วพาไป Execution Workspace; Defect ที่ไม่ผูก Test Case ตรวจซ้ำจากหน้า Defect
4. ตรวจซ้ำ Critical / High ก่อน
   - ผ่าน → กด "ตรวจซ้ำผ่าน" แล้วเลือก Build ที่ทดสอบ
   - ไม่ผ่าน → กด "Reopen" แล้วแนบหลักฐานในคอมเมนต์
5. รัน Cycle ตามแผนหรือ Regression ของ Build นี้ (Clone Cycle เดิมไป Build ใหม่) ระบบจะ Reopen หรือสร้าง Defect ให้ตามกฎข้อ 3
6. ทบทวนแท็บ **ค้างจาก Build ก่อน** กับ Dev (หรือหน้า Defect เลือก Build ที่ topbar แล้วกดชิป "ยังไม่ปิด ณ Build นี้ (รวมของค้าง)"): Critical / High ทุกรายการต้องมีผู้รับผิดชอบและ Build เป้าหมาย
7. ก่อน Mark RC / Sign-off ต้องไม่เหลือ Resolved ที่ยังไม่ตรวจซ้ำ และ High ที่เหลือต้องมี Risk Acceptance
   - หน้า **Release Sign-off** มีส่วน "Defect ของ Release" แสดง Critical ค้าง / High ค้าง / Resolved รอตรวจซ้ำ / ยังไม่มี Risk Acceptance
   - Risk Acceptance ต้อง**ผูกกับ Defect** (เลือก Defect ตอนสร้าง) จึงนับว่าครอบคลุม Defect นั้น
8. สร้าง Test Summary — กด "สร้างข้อความสรุปใหม่" แล้ว Known Issues จะดึง Defect ที่ปิดด้วย Deferred / Won't Fix, Defect ที่มี Risk Acceptance และ Known Issues ของ Build ให้อัตโนมัติ (แก้ข้อความต่อได้)

## 4.1 นิยามกลุ่มสถานะและ Release Gate (เฟส 4)

`DefectStatusGroups` (Domain) เป็นนิยามเดียวที่ Dashboard, Test Summary, Regression, สถิติหน้า Defect และ Release Gate ใช้ร่วมกัน:

| กลุ่ม | สถานะ | ใช้ที่ |
|---|---|---|
| Unresolved | Open, In Progress | "ค้าง" ที่ Dev ยังไม่ได้แก้ (อายุ Defect ค้าง, ไม่มีผู้รับผิดชอบ) |
| AwaitingVerification | Resolved | รอ QA ตรวจซ้ำ |
| Done | Closed, Rejected | จบแล้ว — อัตราปิด (Closure) = Done ÷ ทั้งหมด |
| NotDone | Unresolved + AwaitingVerification | "Open Defects" ของ Dashboard / Test Summary / Regression |

ผลที่เปลี่ยนจากเดิม: Dashboard และ Test Summary นับ Resolved เป็น Defect ที่ยังเปิด (เดิมนับเป็นปิดแล้ว) และอัตราปิดในหน้า Defect ไม่นับ Resolved

**Release Gate (`ReleaseGate.Evaluate` + `DefectGateCounts`):**

| เงื่อนไข (Defect ของ Release ที่ยังไม่ลบ) | ผล |
|---|---|
| Critical ที่ยัง Open / In Progress | NO-GO (Risk Acceptance ปลดไม่ได้) |
| High ที่ยัง Open / In Progress หรือ Resolved ที่ยังไม่ตรวจซ้ำ (ทุก Severity) **ที่ไม่มี** Risk Acceptance สถานะ Approved ผูกกับ Defect นั้น | NO-GO |
| ข้างบนทุกรายการมี Risk Acceptance แล้ว | CONDITIONAL GO |

- กฎเดิม (Smoke, P0, P1 Blocker) ยังตัดสินก่อนเหมือนเดิม
- เปิดใช้ด้วย config `Defects:GateEnabled` (ค่าเริ่มต้น `false` ใน `appsettings.json`, อ่านแบบ live ไม่ต้อง restart) — ระหว่างปิด API ยังส่งตัวเลข Defect มาแสดงในหน้า Release Sign-off เพื่อติดตาม แต่ไม่มีผลกับคำแนะนำ
- เปิดเมื่อจบเฟส 5 เท่านั้น ไม่งั้น Release ปัจจุบันจะเป็น NO-GO ทันที

## 5. แผนที่เหลือ

| เฟส | เนื้อหา | สถานะ |
|---|---|---|
| 1 | ฟิลด์ Fix / Verified Build, ResolvedAt / ClosedAt, ReopenCount, RegressionOf, activity from/to/build (migration `AddDefectBuildLifecycle`) | ✅ 2026-10-28 |
| 2 | กฎเปลี่ยนสถานะ, CRM FINISH / CLOSE, Reopen ตอน Fail ซ้ำ, ช่อง Fix / Verified Build ในหน้าต่างผลการปิด, ปุ่ม Reopen / ตรวจซ้ำผ่าน | ✅ 2026-10-28 |
| 3 | `GET /builds/{id}/defect-carryover`, ตัวกรอง `buildMode=openAsOf`, `POST /defects/{id}/retest`, `POST /builds/{id}/retest-cycle`, ส่วน "Defect ของ Build นี้" ในรายละเอียด Build | ✅ 2026-10-28 |
| 4 | `DefectStatusGroups` นิยามเดียวทั้งระบบ, Release Gate นับ Defect (เปิดด้วย `Defects:GateEnabled`), Known Issues จาก Defect | ✅ 2026-10-28 (Gate ยังปิดอยู่) |
| 5 | เคลียร์ backlog: triage Open 1,635 รายการ, ตั้ง Fix Build + ตรวจซ้ำ Resolved 319 รายการ แล้วจึงเปิด Gate | งานของทีม |

### Progress Log

- **2026-10-28 — เฟส 1+2 (branch `feat/defect-build-lifecycle`):**
  - Domain `Defect.TransitionTo` / `TransitionError` / `Retest` / `MarkRegressionOf` และ `DefectTransitionException`
  - `DefectActivity` เพิ่ม `FromValue` / `ToValue` / `BuildId`
  - `DefectBuilds` (Build ล่าสุด, ตรวจ Project, เทียบลำดับ Build)
  - `DefectsController` ให้ Create / Update / ChangeStatus / Bulk ผ่านกฎ, Bulk รับ `resolution` / `fixedInBuildId` และตอบ `failed`
  - `CrmDefectStatusSync` (FINISH → Resolved + Fix Build, CLOSE → activity)
  - `DefectAutoCreateService` (Reopen / regression)
  - หน้าเว็บ: ช่อง Fix / Verified Build ในหน้าต่างผลการปิด, ปุ่ม Reopen / ตรวจซ้ำผ่าน, Build 3 แบบ + Reopen ในรายละเอียด, ข้อความ Reopen ใน Execution Workspace
  - ทดสอบ: `DefectLifecycleTests` (transition table, Fix / Verified Build, Reopen, Retest, CRM, auto-create 4 กรณี)
- **2026-10-28 — แก้ตาม `/code-review` (PR #4):** Resolve รอบใหม่ตั้ง Fix Build ใหม่เสมอ; regression เฉพาะ Defect ที่ปิดเพราะแก้แล้ว; `LastRetestResult` บันทึกจากการออกจาก Resolved; Bulk ส่งผลการปิด/Fix Build โดยไม่มีสถานะ = 400 และบันทึก activity Resolution; หน้าต่างผลการปิดใช้ "ระบบเลือก" เป็นค่าเริ่มต้น (ลำดับ Build ตัดสินฝั่ง server ที่เดียว, ไม่ส่ง Build ที่ปิดใช้งาน); ซ่อนปุ่ม Resolve ของ Rejected; ข้อความ Execution Workspace แยกกรณี Defect รอตรวจซ้ำบน Build ที่ใหม่กว่า; activity CRM บอกตามจริงเมื่อไม่มี Fix Build; รายการ Defect ดึงเลข Build ครั้งเดียวต่อหน้า
- **2026-10-28 — เฟส 3 (branch `feat/defect-build-carryover`):**
  - `BuildDefectsController`: `GET /builds/{buildId}/defect-carryover` (3 กลุ่ม + นับตาม Severity, 100 รายการแรกต่อกลุ่ม เรียงรุนแรงสุด/ค้างนานสุดก่อน) และ `POST /builds/{buildId}/retest-cycle` (สิทธิ์ `EXECUTION.RUN`, Cycle ประเภท Regression รหัส `{Project}-CYC-n` จาก Test Case ที่ผูกกับคิวตรวจซ้ำ)
  - `POST /defects/{id}/retest { buildId, passed, note? }` สำหรับเครื่องมือ/Automation (หน้าเว็บใช้ปุ่ม "ตรวจซ้ำผ่าน" / "Reopen" เดิมซึ่งบันทึกผลตรวจซ้ำแบบเดียวกัน)
  - `GET /defects?buildId=&buildMode=openAsOf` — Defect ที่ยังไม่ปิดซึ่งเจอใน Build นี้หรือก่อนหน้า
  - `DefectBuilds.ReleaseOrderAsync` ลำดับ Build ใน Release (BuildDate แล้ววันที่ลงทะเบียน)
  - หน้าเว็บ: `BuildDefectsPanel` ในหน้าต่างรายละเอียด Build, ชิป "เจอใน Build นี้ / ยังไม่ปิด ณ Build นี้" ในหน้า Defect เมื่อเลือก Build
  - ทดสอบ: `BuildDefectCarryoverTests` (จัดกลุ่มตามลำดับ Build, สิทธิ์ Project, openAsOf, retest ผ่าน/ไม่ผ่าน, Retest Cycle, แปลง SQL)
- **2026-10-28 — เฟส 4 (branch `feat/defect-release-gate`):**
  - Domain `DefectStatusGroups` (Unresolved / AwaitingVerification / Done / NotDone / KnownIssueResolutions) — `Defect.UnresolvedStatuses` ชี้มาที่นี่
  - ใช้ใน `DashboardRepository` (Open Defects, Severity, Module = NotDone), `RegressionImpactAnalyzer`, สถิติ `DefectsController` (Closure = Done) และหน้า Test Summary (`isDefectDone` ใน `shared/defects.ts`)
  - `ReleaseGate`: `DefectGateCounts.From` + `ReleaseGateInput.Defects` (null = ปิด); `ReleaseSignoffRepository` นับ Defect ของ Release + Risk Acceptance ที่ Approved และผูก `DefectId`; `DefectGateOptions` (`Defects:GateEnabled`, `IOptionsMonitor`)
  - `GET /releases/{id}/release-gate` เพิ่ม `defectGateEnabled`, `openCriticalDefects`, `openHighDefects`, `unverifiedResolvedDefects`, `defectBlockersWithoutRisk`, `defectBlockersWithRisk`
  - `GET /releases/{id}/test-summary` เพิ่ม `knownIssues[]` จาก `KnownIssueRepository` (Defect ปิดด้วย Deferred / Won't Fix, Defect ที่ยังไม่จบแต่มี Risk Approved — สูงสุด 200 รายการ เรียง Severity แล้วรหัส — ต่อท้ายด้วย Known Issues ของ Build ที่ใช้งาน)
  - หน้าเว็บ: ส่วน "Defect ของ Release" ใน Release Gate Panel; Test Summary สร้างข้อความ Known Issues จาก `knownIssues` (15 รายการแรก + จำนวนที่เหลือ)
  - ทดสอบ: `DefectReleaseGateTests` (กลุ่มสถานะครบ, จัดประเภท Blocker, ตาราง Gate เปิด/ปิด, กฎเดิมมาก่อน, ตัวเลขจาก repository, Known Issues, แปลง SQL)
- **2026-10-28 — แก้ตาม `/code-review` (เฟส 3–4):**
  - Retest Cycle สร้างเป็น `InProgress` ทันที (Execution Workspace แสดงเฉพาะ Cycle ที่ InProgress)
  - `POST /defects/{id}/retest` รับเฉพาะ Build ใน Release เดียวกับ Defect, Build ที่ใช้งานอยู่ และเป็น Fix Build หรือใหม่กว่า
  - คิวตรวจซ้ำใช้กฎเดียว (`DefectBuilds.RetestQueue`) ทั้งส่วน "Defect ของ Build" และ Retest Cycle; ส่วน "Defect ของ Build" ดึงข้อมูลด้วย query เดียว (`BuildDefectsController.CarryoverQuery`)
  - `GET /defects/stats` รับ `buildMode=openAsOf` เหมือนรายการ ชิปสถานะจึงนับชุดเดียวกับตาราง
  - Release Gate ไม่นับ Risk Acceptance ที่ถูกลบ; Dashboard นับ Critical เฉพาะที่ยังไม่ได้แก้ (ตรงกับ Gate)
  - หน้าเว็บใช้ `isDefectDone` ทุกจุด (Execution Workspace, อายุ Defect ในตาราง); Test Summary ที่บันทึกข้อความไว้แล้วเติม Known Issue ใหม่ต่อท้ายโดยไม่ทับข้อความเดิม; `BuildDefectsPanel` ใช้ `apiFetch` และรับสิทธิ์จาก App; `apiFetch` อ่านข้อความ error แบบ text ธรรมดาได้
  - **ช่องว่างที่พบ:** หน้า Risk Acceptance โหลดตัวเลือก Defect แค่ 100 รายการแรกของ Release — Release ที่มี Defect หลักพันเลือก Defect ส่วนใหญ่ไม่ได้ ต้องเปลี่ยนเป็นช่องค้นหา Defect ก่อนเปิด Gate
