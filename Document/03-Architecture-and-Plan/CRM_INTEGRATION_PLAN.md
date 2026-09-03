# ProMaxx2 QA Hub — Defect ↔ CRM (BlueSea Helpdesk) Integration Plan

> เอกสารวางแผน (planning only — ยังไม่ได้ implement) สำหรับการเชื่อม **Defect Detail ใน QA Hub** เข้ากับ **CRM/Helpdesk (BlueSea, `booklicenceapi`)** สองทิศทาง: (1) กดปุ่มสร้าง Job ใน CRM จาก Defect พร้อมแจ้งเมล์ Dev ที่ Assign และ (2) เมื่อ CRM มีการเปลี่ยนแปลง (status/reassign) ให้ sync กลับมาอัปเดต Defect พร้อมแจ้งเมล์ Assignee ใน QA Hub
> เอกสารนี้เป็นผลสรุปจากการคุยวางแผนหลายรอบ — field mapping และ endpoint ทั้งหมด **ล็อกแล้ว 100%** เหลือแค่ของที่ต้อง provision จริง (ดูหัวข้อ 7)

## สถานะ

| ส่วน | สถานะ |
|---|---|
| Flow/สถาปัตยกรรม | ✅ ล็อกแล้ว |
| Field mapping (Create Job) | ✅ ล็อกแล้ว 100% |
| Endpoint inventory จาก CRM (reverse-engineered) | ✅ ครบตามหน้าที่มี |
| Schema เปลี่ยนแปลงฝั่ง QA Hub | ✅ Implement แล้ว (migration apply กับ dev DB แล้ว) |
| Phase 1 backend/frontend (ปุ่ม "ส่งไป CRM", Setting Center → CRM config) | ✅ Implement แล้ว build+test ผ่าน |
| **Client authentication ของ BlueID token request** | ✅ **แก้แล้ว** — เลิกใช้ OAuth2 password grant ที่ตัน (§4.1) เปลี่ยนไปใช้ Playwright ขับ browser จริงผ่านหน้า Employee login เหมือน `H:\APP\ExportReport\exporter.py` เดิม (ดู `CrmTokenService.cs`) พิสูจน์แล้วว่าสร้าง ticket จริงได้ |
| Mapping table Project → CRM Product/Version | ✅ Implement แล้ว (หน้า Setting Center → CRM Mapping) |
| Comment sync (QA Hub → CRM) | ✅ Implement แล้ว (ดู §5.1) |
| Reassign ticket เดิม (ไม่สร้างซ้ำ) | ✅ Implement แล้ว — ปุ่ม "เปลี่ยนผู้รับผิดชอบ CRM" |
| Phase 2 (background sync poller CRM → QA Hub) | ✅ Implement แล้ว (`CrmSyncWorker`/`CrmSyncService`, poll ทุก 2 นาที) — ดู §5.3 — ยังไม่ได้ verify กับ CRM จริง |
| Phase 3 (email แจ้งเตือน 2 จุด) | ✅ Implement แล้ว (`EmailConfigurationService`/`EmailSenderService` — Gmail SMTP) — ดู §5.3 — รอผู้ใช้กรอก Gmail App Password จริงใน Setting Center แล้ว verify |

---

## 1. บริบทและปัญหาเดิม

Flow เดิมเมื่อเจอ bug (manual, หลายขั้นตอน):

1. คีย์ Job ในระบบ CRM ด้วยมือ
2. Service กลาง (ทีมอื่นดูแล, QA Hub คุมไม่ได้) ดึงข้อมูลจาก CRM ไปเขียนลง Google Sheet ทุก 2 นาที
3. App Script กวาด Sheet แล้วส่งเมล์แจ้งเตือนผู้ถูก Assign
4. Dev เข้า CRM อัปเดตงาน กรณีแก้เสร็จจะกำหนด Assign กลับหาเจ้าของเรื่อง
5–6. Sheet sync + App Script แจ้งเมล์ซ้ำรอบสอง

**ปัญหา**: ต้องสลับหน้าจอไปคีย์ Job ใน CRM เอง, รอบ sync ผ่าน Sheet ช้าและพึ่งพาทีมอื่น, ไม่มี traceability ระหว่าง Defect ใน QA Hub กับ Job ใน CRM

## 2. เป้าหมาย

ในหน้า **Defect Detail** ของ QA Hub:

1. ปุ่ม **"ส่งไป CRM"** → สร้าง Job ใหม่ใน CRM ผ่าน API โดยตรง (ไม่ผ่าน Sheet) พร้อมเลือก Dev ที่จะ Assign → ส่งอีเมลแจ้ง Dev คนนั้น
2. เมื่อ CRM มีการเปลี่ยนแปลง (status เปลี่ยน หรือถูกส่งเคสกลับหาเจ้าของเรื่อง) → QA Hub sync กลับมาอัปเดต Defect เอง พร้อมส่งอีเมลแจ้ง Assignee ปัจจุบันของ Defect ใน QA Hub

## 3. Flow สุดท้าย

```
[Defect Detail] --กดปุ่ม "ส่งไป CRM"-->
  Dialog เลือก Dev (Assignto = รหัสพนักงาน 5 หลัก, ดึง live จาก BlueID user directory)
  --> POST {Url}/Support  (Create Job, synchronous)
  --> ได้ CrmTicketId (JobNo) กลับทันที
  --> Defect.CrmTicketId + CrmSyncStatus = Linked, CrmLastSyncedAt = now
  --> DefectActivity "CrmTicketCreated"
  --> อีเมลแจ้ง Dev ที่ถูกเลือก (Assignto)

[QA Hub Background Worker] --ทุก N นาที, เฉพาะ Defect ที่ CrmSyncStatus = Linked-->
  GET {Url}/Support/HelpDesksJob?JobNo=X&JobType=HD
  เทียบ status / assignto กับค่าที่เก็บไว้ล่าสุด (CrmLastKnownStatus / CrmLastKnownAssignto)
  เจอเปลี่ยน  → อัปเดต Defect + DefectActivity "CrmStatusChanged"
  เจอ assignto == ownerSubjectId → DefectActivity "CrmReturnedToOwner"
                                  → อีเมลแจ้ง Assignee ปัจจุบันของ Defect ใน QA Hub
```

**หมายเหตุสำคัญ**:
- ฝั่ง push เป็น **synchronous** (ได้ JobNo กลับทันทีจาก API) — ไม่ต้องมี state "Pending" รอ
- ฝั่ง pull **ไม่ใช้ Google Sheet** — poll CRM API ตรงๆ ทีละ Ticket ที่ผูกไว้แล้วเท่านั้น (เร็วกว่า, ไม่พึ่ง Service กลางที่ QA Hub คุมไม่ได้)
- Trigger "ส่งเคสกลับหาเจ้าของเรื่อง" คือ `assignto == ownerSubjectId` ตรงกับ checkbox "to เจ้าของเรื่อง" ที่ Dev ใช้ในหน้า CRM จริง

---

## 4. CRM API Inventory (reverse-engineered จากหน้า Support/JobDetailsHD)

> ยังไม่มี Swagger/official doc — ทั้งหมดนี้อ่านจาก front-end JS ของ BlueSea Helpdesk (`/bluesea/BookLicence/MA/Support` และ `/bluesea/BookLicence/MA/Support/JobDetailsHD`) Base URL: `https://bluesea.seniorsoft.com/booklicenceapi`

| Endpoint | Method | ใช้ทำอะไร |
|---|---|---|
| `/Support` | **POST** | สร้าง Job ใหม่ (multipart/form-data) → response คืนค่า JobNo ตรงๆ |
| `/Support` | **PUT** | อัปเดต Job เดิม (ต้องส่ง field เกือบทั้งหมดซ้ำ ไม่ใช่ partial patch) — ใช้โดย CRM เอง ไม่ใช่จุดที่ QA Hub เรียก |
| `/Support/HelpDesksJob?JobNo=X&JobType=HD` | GET | ดึงรายละเอียด Job ทีละใบแบบเต็ม — **นี่คือ endpoint ที่ QA Hub worker จะ poll** |
| `/Support/HelpDeskAnswerChangeHistory?DetailJobNo=X` | GET | ประวัติการเปลี่ยนแปลงของ Job (สำรอง เผื่อ diff ไม่พอ) |
| `/Support/HelpDeskAnswerMain?DetailJobNo=X` | GET | ข้อความ/ไฟล์แนบในเคส |
| `/Support/ComposeEmail` | POST | CRM มี API ส่งอีเมลของตัวเอง (Subject/To/CC/Body) — ไม่ได้ใช้ในแผนนี้ (QA Hub มี email flow ของตัวเอง) แต่บันทึกไว้เผื่อใช้ในอนาคต |
| `/Support/SysSrviceType` | GET | Lookup ประเภทงาน — ใช้หา code ของ "Bug" |
| `/Support/Products`, `/Support/VersionProduct`, `/Support/OS`, `/Support/Followup`, `/Support/BranchList`, `/Support/SellerBranchs` | GET | Lookup lists ประกอบ payload |
| `https://blueid.seniorsoft.com/blueidapi/UserAccount/DWUserAccountSeniorV2` | GET | รายชื่อ user ภายใน (seniorSoftID + fullName + **email**) — ใช้ทำ dropdown "เลือก Dev" |

**Auth**: Bearer token ออกจาก BlueID SSO (`https://blueid.seniorsoft.com/blueid/connect/token`) — **ปรับให้ง่ายลงตามที่ตกลงกันล่าสุด**: QA Hub เก็บแค่ 3 ค่า **MerchantID, Username (รหัสพนักงาน), Password** เหมือนหน้า Employee login ของ BlueID เอง แล้วขอ token ด้วย OAuth2 `grant_type=password` (Resource Owner Password Credentials) — ไม่ใช่ `client_credentials` แบบเดิม และไม่ใช่การจำลอง OIDC Authorization Code + PKCE แบบ browser login เต็มรูปแบบ (สคริปต์ไม่ได้เพราะต้องมี browser จริง) `client_id` (`"BlueSea"`) และ Token URL/Base URL เป็นค่าคงที่ hardcode ไว้ในโค้ด ไม่ให้ตั้งค่าผ่านหน้าจอ — **ชื่อ form field ที่แท้จริงสำหรับ grant_type/scope/merchant_id ยังเป็นการเดาที่ดีที่สุด ต้องยืนยันกับทีม CRM/BlueID**

**MerchantId**: จากหน้า login ของ BlueID (`/blueid/Account/Login`) พบว่าทุก login/token ถูก scope ด้วย MerchantID 8 หลัก (เห็นเป็น claim `merchantid` ใน JWT ด้วย, ตัวอย่าง `10000001`) — เก็บเป็นส่วนหนึ่งของ 3 ค่าที่ตั้งค่าใน CRM Configuration

**Username แทน RecipientId**: เดิมมี field แยก "RecipientId (Service Account's seniorSoftID)" — ตอนนี้ตัดออกแล้ว เพราะ login ด้วย Username จริง (=รหัสพนักงาน) โดยตรง จึงใช้ Username ตัวเดียวกันเป็นทั้ง `RecipientId` และ `Posted` ใน payload ของ CRM ได้เลย

### 4.1 ⚠️ Blocker ที่ยืนยันแล้วจากการทดสอบจริง (2026-08-31)

ทดสอบยิง `grant_type=password` ไปที่ `https://blueid.seniorsoft.com/blueid/connect/token` ด้วย MerchantID/Username/Password จริง (login เข้า BlueSea ได้ปกติ) — **BlueID ปฏิเสธด้วย 401**:

```json
{ "error": "invalid_client", "error_description": "Client authentication is required for this application." }
```

**สรุป**: client `"BlueSea"` (hardcode ไว้ใน `CrmTokenService.cs`) ถูกตั้งเป็น **confidential client** ฝั่ง BlueID — ต้องมี client authentication (เช่น `client_secret`) แนบไปกับ token request ด้วย ไม่ใช่แค่ MerchantID/Username/Password เฉยๆ ตามที่ตกลงกันไว้ก่อนหน้า

**ทางเลือกที่ต้องให้ทีม CRM/BlueID ตัดสินใจ** (เลือกอย่างใดอย่างหนึ่ง แล้วแจ้งกลับมาปรับโค้ด):
1. ให้ `client_secret` ของ client `"BlueSea"` มาด้วย (ถ้า client นี้อนุญาตให้ resource owner password grant ได้จริงสำหรับ third-party ใช้)
2. สร้าง client แยกต่างหากสำหรับ QA Hub โดยเฉพาะ (client_id + client_secret ของตัวเอง) — ตรงกับแนวทาง client_credentials ดั้งเดิมที่เคยยืนยันไว้ว่า "มีอยู่แล้ว" ก่อนจะเปลี่ยนมาใช้ password grant แบบง่าย

**ผลกระทบ**: ปุ่ม "ส่งไป CRM" และ "เลือกผู้รับผิดชอบ" ในหน้า Defect Detail ยัง**ใช้งานไม่ได้จริง**จนกว่าจะได้ค่านี้มา แต่โค้ดทั้งหมด (schema, endpoint, UI) พร้อมใช้แล้ว — แค่เพิ่ม field เดียว (`ClientSecret`) เข้า `CrmConfiguration`/หน้า Setting Center ก็จะใช้งานได้ทันที

---

## 5. Field Mapping — Create Job (`POST /Support`)

| CRM Field | ค่าที่ใช้ |
|---|---|
| Subject | `Defect.Title` |
| Member | QA Hub Username ของคนกดปุ่ม (เป็นรหัสพนักงาน 5 หลักอยู่แล้ว เช่น `6101`) |
| FName | QA Hub DisplayName ของคนกดปุ่ม |
| LName | ว่าง |
| Tel | `999` (placeholder คงที่) |
| Email | ว่าง |
| SysCustomerType | `1` (None MA) |
| RecipientId | seniorSoftID ของ **Service Account ของ QA Hub** |
| OwnerSubjectId | QA Hub Username ของคนกดปุ่ม |
| Assignto | รหัสพนักงาน 5 หลักที่ QA เลือกจาก dropdown (ดึงสดจาก `DWUserAccountSeniorV2`) |
| SysDevelop | เท่ากับค่า `Assignto` |
| Status | `Open` |
| Source | `Remote` |
| BranchId | `1` (สำนักงานใหญ่) |
| SysserViceType | resolve จาก `/Support/SysSrviceType` หา entry ที่ `serviceName` = "Bug" (ห้าม hardcode ID ตรงๆ เพราะยังไม่รู้ค่าจริง ต้อง query runtime) |
| SysFollowupId | ว่าง |
| SysProductId | map จาก QA Hub `Project` → CRM Product จริง (ต้องมี mapping table ดูหัวข้อ 7) |
| SysVersionId | map จาก QA Hub Release/Build → CRM Version (ต้องมี mapping table) |
| SysOsId | ว่าง |
| Description | รวม `Description` + `StepsToReproduce` + `ExpectedResult` + `ActualResult` เป็นก้อนเดียว |
| Posted | seniorSoftID เดียวกับ Service Account (RecipientId) |
| JobType | `HD` |
| RefJobNo | ว่าง |
| Duedate | ว่าง |
| BuildDetail | ว่าง (หรือใส่ Build label ของ Defect ถ้ามี) |

---

### 5.1 Comment sync (QA Hub → CRM, added 2026-09-01)

เมื่อกด "ส่ง" คอมเมนต์ใน Defect Detail ของ Defect ที่ `CrmSyncStatus == Linked` แล้ว จะ sync ข้อความเดียวกันไปต่อท้าย
CRM ticket ด้วยแบบอัตโนมัติ (ไม่มี checkbox แยก, best-effort — ล้มเหลวได้โดยไม่กระทบการบันทึกคอมเมนต์ใน QA Hub)

CRM **ไม่มี endpoint สำหรับเพิ่มโน้ต/ตอบกลับแยกต่างหาก** — `GET /Support/HelpDeskAnswerMain` เป็น read-only ในหน้า
JobDetailsHD กลไกเดียวที่มีคือปุ่ม "Update" ของ CRM เอง ซึ่ง `PUT /Support` (endpoint เดียวกับตอนสร้าง ticket) เขียน
ทับ job ทั้งใบ ไม่ใช่ partial update ดังนั้น flow คือ:

1. `GET /Support/HelpDesksJob?JobNo=X&JobType=HD` ดึง snapshot ปัจจุบันของ job ทั้งใบ
2. ต่อท้าย `Description` เดิมด้วย `[QA Hub] {ชื่อผู้คอมเมนต์} (วันเวลา): {ข้อความ}` — ถ้ารวมกันเกิน 1000 ตัวอักษร
   (limit เดิมของ CRM) จะตัดข้อความเก่าสุดออกจากหน้า ไม่ตัดคอมเมนต์ใหม่ที่เพิ่งกดส่ง
3. `PUT /Support` ส่งกลับไปทั้งใบ carry-over ทุก field เดิมที่ได้จาก step 1 ไม่แตะ Status/Assignto/Product ฯลฯ —
   ยกเว้น `SysBranchId` ที่ CRM เองก็ hardcode เป็น `"00000"` เสมอ (ไม่ได้อ่านจาก job)
4. **ไม่ส่งอีเมลแจ้งเตือนซ้ำ** (CRM's own UI form แนบ CC/ToAdd/Body/SubjectEmail มาในคำขอ Update เดียวกันเพื่อยิงอีเมล
   แต่ QA Hub เลือกไม่ส่งส่วนนี้ตามที่ยืนยันกับผู้ใช้ไว้)

ดูโค้ดจริงที่ `CrmSendToCrmService.AppendCommentAsync`, `CrmApiClient.GetJobDetailAsync`/`UpdateSupportJobAsync`,
เรียกจาก `DefectsController.AddComment`.

**ข้อควรระวังที่ยังไม่ทดสอบจริง**: ฟิลด์ `Duedate` — ยังไม่เคยเจอ Defect ที่มีการตั้ง Duedate ไว้จริงในทางปฏิบัติ
(create flow ของ Phase 1 ไม่เคยส่งค่านี้) โค้ดปัจจุบัน carry-over ค่าดิบที่ CRM คืนมาตรงๆ โดยไม่ reformat — ยังไม่ได้
ยืนยันว่า CRM backend ยอมรับ format เดิมที่ตัวเองคืนมาตอน round-trip ผ่าน PUT หรือไม่

---

### 5.3 Phase 2 (poller) + Phase 3 (email) — implement แล้ว 2026-09-01, รอ verify กับของจริง

**Phase 2 — `CrmSyncWorker`/`CrmSyncService`**: `BackgroundService` poll ทุก Defect ที่ `CrmSyncStatus == "Linked"`
ทุก 2 นาที (คงคาบเดิมของ Google Sheets export flow) ยิง `GET /Support/HelpDesksJob` เทียบ `status`/`assignto` กับ
`Defect.CrmLastKnownStatus`/`CrmLastKnownAssignto` ที่เก็บไว้ล่าสุด ถ้าเปลี่ยน → อัปเดต snapshot 2 ฟิลด์นั้น + log
`DefectActivity` (`CrmStatusChanged`) — **ตั้งใจไม่แตะ `Defect.Status`/`AssigneeUserId` เลย** (ยืนยันกับผู้ใช้แล้ว:
CRM มี 9 สถานะ ไม่ map ตรงกับ 4 สถานะของ QA Hub ต้องให้ workflow ของ QA Hub เป็นอิสระจาก CRM 100%) ถ้า
`assignto` เปลี่ยนไปเท่ากับ `ownerSubjectId` (แปลว่า CRM ส่งเคสกลับมาหาคนที่สร้าง ticket) → log เพิ่ม
`CrmReturnedToOwner` + ยิงอีเมล (Phase 3 จุดที่ 2)

**Phase 3 — `EmailConfigurationService`/`EmailSenderService`**: Gmail SMTP (`System.Net.Mail.SmtpClient`, ไม่ใช้
NuGet เพิ่ม) ตั้งค่าที่ Setting Center → Email/SMTP (เก็บ App Password เข้ารหัสแบบเดียวกับ CRM/AI config) มีปุ่ม
"ส่งอีเมลทดสอบ" ในหน้าเดียวกัน ยิง 2 จุด ทั้งคู่ best-effort (ไม่ทำให้ action หลักล้มเหลวถ้าอีเมลส่งไม่ได้):
1. หลัง "ส่งไป CRM" สำเร็จ (`CrmSendToCrmService.SendAsync`) → แจ้ง Dev ที่ถูก assign (อีเมลจาก BlueID directory)
2. หลัง poller เจอ `CrmReturnedToOwner` (ด้านบน) → แจ้ง `Defect.AssigneeUserId`'s QA Hub email

**ยังไม่ได้ทำ**: verify กับ CRM/Gmail จริง (ผู้ใช้ยังไม่ได้กรอก Gmail App Password จริงใน Setting Center) —
เมื่อ verify แล้วให้อัปเดตแถวในตาราง "สถานะ" ด้านบนเป็นยืนยันแล้ว เหมือน Phase 1

---

### 5.4 CRM Login: Service Account กลาง → per-user credentials (2026-09-01)

**เหตุผล**: CRM แยกงานตามคนที่ login จริง (OwnerSubjectId/Assignto/RecipientId ผูกกับ identity จริงใน CRM) — เดิมทุก
action จาก QA Hub login เข้า CRM ด้วย Service Account กลางบัญชีเดียว (`CrmConfigurations` แถวเดียว) ทำให้ทุก ticket
ที่สร้างจาก QA Hub ดูเหมือนมาจากคนเดียวกันหมดใน CRM ไม่ว่าใครจะเป็นคนกดปุ่มจริง — เปลี่ยนเป็นให้ **แต่ละ user กรอก
CRM Login ของตัวเอง** (self-service, ไม่ใช่ Admin ตั้งให้)

- `CrmConfigurations` เปลี่ยนจาก single row เป็น **หนึ่งแถวต่อหนึ่ง QA Hub user** (`UserId` unique index) — จัดการที่
  ปุ่ม "บัญชี CRM ของฉัน" (มุมขวาบน ข้างปุ่ม logout) ผ่าน `GET/PUT /auth/me/crm` ใน `AuthController.cs`
- `CrmTokenService` cache token เป็น **ต่อ user** แล้ว (`ConcurrentDictionary<Guid, CachedToken>` + lock ต่อ user)
  แทนที่จะเป็นตัวแปร shared ตัวเดียว — `BranchId` ก็ผูกกับ user เช่นกัน (คืนมาพร้อมกับ token แทนที่จะเป็น property กลาง)
- `CrmApiClient`/`CrmSendToCrmService` ทุก method รับ `Guid actingUserId` แล้วใช้ credential ของ user นั้นจริงๆ —
  `OwnerSubjectId`/`Member`/`RecipientId`/`Posted` ใช้ `cfg.Username` (รหัสพนักงานจริงของคนที่กดปุ่ม) สม่ำเสมอ
  (แก้จุดที่แต่ก่อนผสม QA Hub Username กับ Service Account Username ปนกัน)
- **Phase 2 poller** (`CrmSyncService`) เปลี่ยนไป poll ด้วย credential ของ **Assignee ของ Defect ใน QA Hub**
  (`Defect.AssigneeUserId`) ไม่ใช่คนที่กด "ส่งไป CRM" ตอนแรก — ถ้า Assignee ไม่มี/ยังไม่ได้ตั้งค่าบัญชี CRM ก็แค่
  ข้าม Defect นั้นไปในรอบนั้น (ไม่ error ทั้ง tick เหมือนตอนใช้ Service Account กลาง)
- รอบ Poll (`PollIntervalMinutes`) แยกออกมาเป็น entity ใหม่ `CrmSyncSettings` (single row, admin-only ผ่าน
  Setting Center → CRM Sync) เพราะเป็นค่ากลางของทั้งระบบ ไม่ใช่ credential ส่วนตัว
- **Migration**: ลบ `CrmConfigurations` แถวเก่าทิ้งทั้งหมด (ตามที่ยืนยันกับผู้ใช้) — ทุกคนต้องกรอกบัญชี CRM ของ
  ตัวเองใหม่หมดถึงจะใช้ "ส่งไป CRM"/"เปลี่ยนผู้รับผิดชอบ CRM"/comment sync ได้ต่อ (ยังไม่ได้ verify กับ CRM จริง)

### 5.5 Comment sync ทิศทาง CRM → QA Hub (added 2026-09-01)

Phase 2 poller เดิม (§5.3) เช็คแค่ Status/Assignto — เพิ่มการเช็ค**คอมเมนต์ใหม่ที่ CRM staff พิมพ์ในตั๋ว**แล้ว
sync กลับมาเป็น DefectActivity ใน QA Hub ด้วย (ปิดช่องว่างที่ §5.1 มีแค่ QA Hub → CRM ทิศทางเดียว):

- ดึงจาก `GET /Support/HelpDeskAnswerMain?DetailJobNo=X` (endpoint เดียวกับที่ตาราง "chat" ในหน้า JobDetailsHD
  ใช้แสดงประวัติ — เป็น DataTables serverSide ajax, ยังไม่ได้ยืนยัน parameter ที่ backend บังคับจริง ใส่แค่
  `draw`/`start`/`length` มาตรฐานไปด้วยเผื่อไว้ — ดู `CrmApiClient.GetHelpDeskAnswersAsync`)
- เทียบ `answerNo` ล่าสุดที่เคยเห็น (`Defect.CrmLastSeenAnswerNo`) กับที่ CRM มีตอนนี้ ถ้ามีรายการใหม่กว่า log เป็น
  `DefectActivity` (ActionType `CrmComment`) ทีละรายการ เรียงเก่า→ใหม่ — เหมือน pattern `isFirstPoll` ของ
  Status/Assignto: poll ครั้งแรกของ ticket ที่เพิ่งผูกจะไม่ log คอมเมนต์เก่าทั้งหมดย้อนหลัง แค่ตั้ง baseline เงียบๆ
- ยังไม่มีอีเมลแจ้งเตือนสำหรับจุดนี้ (ต่างจาก Phase 3 เดิมที่มี 2 จุด) — ต้องการเพิ่มก็ทำได้ภายหลัง

---

## 6. องค์ประกอบทางเทคนิคที่ต้องสร้าง

| งาน | Pattern ต้นแบบในระบบ | รายละเอียด |
|---|---|---|
| Schema `Defect` | `src/ProMaxx2.QA.Domain/Defects/Defect.cs` | เพิ่ม `CrmTicketId`, `CrmSyncStatus` (None/Linked/Failed), `CrmLastSyncedAt`, `CrmLastKnownStatus`, `CrmLastKnownAssignto` + migration |
| `CrmApiClient` | `src/ProMaxx2.QA.Api/Services/SharedAiConfigurationService.cs` | เรียก CRM API ผ่าน `IHttpClientFactory`, จัดการ OAuth2 client_credentials token + cache/refresh (token อายุ ~24 ชม.) |
| ปุ่ม + Dialog "ส่งไป CRM" | Endpoint pattern เดิมใน `src/ProMaxx2.QA.Api/Controllers/DefectsController.cs` | Endpoint ใหม่ `POST /defects/{id}/send-to-crm` (body: seniorSoftID ของ Dev ที่เลือก) |
| `CrmSyncWorker` | `src/ProMaxx2.QA.Api/Services/AutomationScheduleWorker.cs` (BackgroundService + PeriodicTimer) | poll เฉพาะ Defect ที่ `CrmSyncStatus = Linked` |
| `IEmailSender` + Gmail SMTP | ไม่มีของเดิมในระบบ ต้องสร้างใหม่ทั้งหมด | ส่ง 2 จุด: (1) หลัง push สำเร็จ → หา Dev, (2) หลัง poll เจอ `assignto == ownerSubjectId` → หา Defect.AssigneeUserId |
| `DefectActivity` | `src/ProMaxx2.QA.Domain/Defects/DefectActivity.cs` + `DefectActivityService.cs` — ใช้ได้ทันที ไม่ต้องแก้ schema | ActionType ใหม่: `CrmTicketCreated`, `CrmSyncFailed`, `CrmStatusChanged`, `CrmReturnedToOwner` |
| Project → CRM Product mapping | ไม่มีของเดิม | หน้า settings ใหม่ให้ admin ผูก QA Hub Project ↔ CRM `SysProductId` (+ Release/Build ↔ `SysVersionId`) |

---

## 7. สิ่งที่ต้องเตรียมก่อน implement ได้จริง (operational, ไม่ใช่ design gap แล้ว)

1. **บัญชี Employee ของ CRM สำหรับ QA Hub ใช้ Login** — MerchantID (8 หลัก) + Username (รหัสพนักงาน) + Password — ขอจากทีมดูแล BlueID/CRM (รวมถึงยืนยันชื่อ form field ที่แท้จริงของ token request แบบ `grant_type=password`)
2. **Mapping table**: QA Hub Project → CRM `SysProductId`, และ Release/Build → `SysVersionId`
3. **ยืนยันกับทีม CRM**:
   - scope `booklicence.fullaccess` ใช้กับ Service Account (ไม่ใช่ user token) ได้จริงไหม
   - `Source = "Remote"` ใช้แทน "ระบบส่งอัตโนมัติจาก QA Hub" ได้โดยไม่กระทบรายงานฝั่งเขา
   - response ที่แท้จริงของ `POST /Support` (JSON หรือ plain text JobNo) และ error response shape
4. **Gmail องค์กร** สำหรับส่งอีเมลแจ้งเตือน — บัญชีส่ง + วิธี auth (App Password/OAuth2)

## 8. Security Notes

- Service Account credential เก็บที่ backend เท่านั้น เข้ารหัสด้วย ASP.NET Core Data Protection (pattern เดียวกับ `SharedAiConfigurationService`) — **ห้าม**ฝัง JWT/credential ไว้ใน client-side JS แบบที่หน้า BlueSea ทำ
- ห้ามใช้ token จาก browser session ของ user คนใดคนหนึ่งเรียก API แบบ server-to-server

---

## 9. แผนแบ่ง Phase (แนะนำ)

- **Phase 1**: Schema (`CrmTicketId` ฯลฯ) + ปุ่ม "ส่งไป CRM" + `CrmApiClient` (create) + `DefectActivity` log — ต้องมี Service Account (ข้อ 7.1) และ Product/Version mapping (ข้อ 7.2) พร้อมก่อน
- **Phase 2**: `CrmSyncWorker` poll กลับมาอัปเดต Defect (status/assignto/link) + `DefectActivity` log
- **Phase 3**: `IEmailSender` + Gmail SMTP + trigger ทั้ง 2 จุด — ต้องมีบัญชี Gmail ส่ง (ข้อ 7.4)
