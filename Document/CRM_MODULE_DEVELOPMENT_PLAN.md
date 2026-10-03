# ProMaxx2 QA Hub — CRM Module Development Plan

สถานะ: **Phase 5 — Create QA Hub Defect from CRM Ticket implementation in progress; Production CRM write-back และ second-user verification intentionally deferred**  
วันที่จัดทำ: 2026-10-02  
ขอบเขต: เพิ่มเมนู `CRM` สำหรับแสดงข้อมูล Ticket จาก CRM/BlueSea โดยแยกข้อมูลตามผู้ใช้ที่ Login เข้า QA Hub

เอกสารนี้เป็นแผนพัฒนาต่อยอดจาก:

- `Document/03-Architecture-and-Plan/CRM_INTEGRATION_PLAN.md`
- `Document/03-Architecture-and-Plan/CRM_DEFECT_KANBAN_PLAN.md`
- `Document/03-Architecture-and-Plan/DEFECT_KNOWLEDGE_PLAN.md`
- `Document/02-Developer-Blueprint/SYSTEM_OVERVIEW.md`
- `Document/02-Developer-Blueprint/UI_DESIGN_SYSTEM.md`

## 1. เป้าหมาย

เพิ่มเมนู **CRM — My CRM Work Queue** เพื่อให้ผู้ใช้ดู Ticket ที่เกี่ยวข้องกับบัญชี CRM ของตนเองจาก QA Hub ได้ โดยไม่ต้องเปิด CRM แยกในงานประจำวัน

เป้าหมายหลัก:

1. แสดง Ticket จาก CRM ตามผู้ใช้ปัจจุบัน
2. ค้นหาและกรองรายการได้
3. เปิดดูรายละเอียดและคำตอบ/Comment ของ Ticket ได้
4. แสดงสถานะ CRM, ผู้รับผิดชอบ, อายุงาน และเวลาที่อัปเดตล่าสุด
5. เปิด Ticket ต้นทางใน CRM ได้
6. ไม่เปิดเผย Credential หรือ CRM Token ใน Browser
7. ป้องกันไม่ให้ผู้ใช้เห็นข้อมูลของผู้ใช้อื่น

## 2. ข้อค้นพบจากระบบปัจจุบัน

### สิ่งที่มีอยู่แล้ว

- CRM Credential ถูกจัดเก็บแยกต่อ QA Hub User ผ่าน `CrmConfigurations.UserId`
- Endpoint `GET/PUT /api/v1/auth/me/crm` ใช้จัดการบัญชี CRM ของผู้ใช้ปัจจุบัน
- CRM Token Cache แยกตาม User
- `CrmApiClient` เรียก CRM ผ่าน Backend พร้อมใช้ Credential ของ User ที่ระบุ
- `CrmSyncWorker` ใช้ดึง Status/Assignee ของ Defect ที่ Link กับ CRM แล้ว
- Defect มีข้อมูล `CrmTicketId`, `CrmSyncStatus`, `CrmLastSyncedAt`, `CrmLastKnownStatus` และ `CrmLastKnownAssignto`

### Gap ที่ต้องทำเพิ่ม

- ยังไม่มีเมนู `CRM` และ Route ฝั่ง Frontend
- ยังไม่มี Endpoint สำหรับ List/Search Ticket จาก CRM
- `CrmApiClient` ปัจจุบันดึงรายละเอียดได้ทีละ Ticket ผ่าน `JobNo`
- ยังไม่มีการยืนยัน Contract ของ CRM List/Search API, Pagination และชื่อ Field สำหรับกรองผู้รับผิดชอบ
- ยังไม่มี Permission เฉพาะสำหรับการเข้าหน้า CRM
- CRM Status จริงยังไม่ได้รับการยืนยันครบทุกค่าและยังไม่มี Status Mapping กลาง

## 3. ขอบเขต MVP

### รวมใน MVP

- เมนู `CRM` แบบ Read-only
- ขอบเขตข้อมูลเริ่มต้นเป็น Ticket ที่ `Assignto` ตรงกับ CRM Username ของผู้ใช้ปัจจุบัน
- ค้นหาด้วย Job No. และ Subject
- กรอง CRM Status, Project/Mapping และช่วงวันที่ เมื่อ CRM API รองรับ
- Server-side Pagination ค่าเริ่มต้น 25 รายการต่อหน้า
- Summary KPI เช่น Open, In Progress, Ready for QA และ Stale
- ตารางรายการ Ticket บน Desktop
- Card layout บน Mobile
- Read-only Detail Modal ด้วย `ModalShell`
- แสดงคำตอบ/Comment จาก CRM เมื่อเปิดรายละเอียด
- ลิงก์เปิด Ticket ใน CRM แท็บใหม่
- แสดงสถานะ `Unknown` เมื่อพบ CRM Status ที่ยังไม่ถูก Map
- แสดง Loading, Empty, Error, Unauthorized, CRM Not Configured และ Stale state

### ไม่รวมใน MVP

- แก้ไขข้อมูล CRM จาก QA Hub
- Drag & Drop เพื่อเปลี่ยน CRM Status
- การนำ Ticket ทั้ง CRM มาแสดงโดยไม่มีเงื่อนไขผู้ใช้
- การสร้าง Defect หรือ Link Defect อัตโนมัติจากหน้า CRM
- SLA/Aging เชิงประวัติย้อนหลังแบบละเอียด
- Kanban ที่แก้ไข Status ได้

การสร้าง Defect จาก CRM หรือ CRM Inbox แบบสองทางให้พิจารณาเป็น Phase ถัดไปตาม `DEFECT_KNOWLEDGE_PLAN.md`

## 4. กฎการแยกข้อมูลตามผู้ใช้

### หลักการบังคับ

1. Frontend ห้ามส่ง `userId` หรือ `crmUsername` เพื่อใช้กำหนดขอบเขตข้อมูล
2. Backend ต้องอ่าน User ID จาก JWT Claim เท่านั้น
3. Backend โหลด CRM Configuration ด้วย User ID จาก JWT
4. Backend ใช้ CRM Username ของ Configuration เป็นตัวกรองข้อมูล
5. Default Scope คือ `Assignto == current CRM Username`
6. Endpoint รายละเอียดและ Comment ต้องตรวจ Ownership ซ้ำทุกครั้ง
7. ห้ามใช้ข้อมูลของ User A ที่ถูก Cache ไว้ตอบ User B
8. หากต้องการดู Ticket ที่ User เป็นผู้สร้างแต่ไม่ได้เป็นผู้รับผิดชอบ ให้ทำเป็น Filter/Permission แยกภายหลัง ไม่ใช้ OR รวมใน MVP โดยอัตโนมัติ

### กรณีบัญชี CRM ไม่พร้อม

หากผู้ใช้ยังไม่มี CRM Configuration หรือบัญชีถูกปิดใช้งาน หน้า CRM ต้องแสดง Empty State พร้อมปุ่ม **ตั้งค่าบัญชี CRM ของฉัน** ซึ่งเปิด Modal เดิมจาก Topbar และไม่ยิง CRM API ซ้ำ

## 5. Integration Contract ที่ต้องยืนยันก่อนเริ่ม Implement

เป็นจุดตรวจสำคัญของ Phase 0:

1. Endpoint List/Search Ticket ของ CRM ที่หน้า Support ใช้จริง
2. รูปแบบ Pagination เช่น `draw`, `start`, `length` หรือรูปแบบอื่น
3. Response Shape และชื่อ Field ที่แท้จริง
4. Field สำหรับระบุผู้รับผิดชอบ เช่น `Assignto`
5. ความหมายของ `OwnerSubjectId` เทียบกับ `Assignto`
6. รายการ CRM Status ทั้งหมด
7. สถานะใดถือเป็น Closed/Done/Ready for QA
8. Filter ที่ CRM รองรับจริง เช่น Status, Date, Product, Version
9. Rate Limit, Timeout และพฤติกรรมเมื่อ Token หมดอายุ
10. เงื่อนไขการเรียก Detail/Answers ของ Ticket ที่ผู้ใช้ไม่ได้เป็น Assignee

หาก CRM ไม่มี List/Search API ให้เลือกทางใดทางหนึ่งก่อนเริ่ม Phase 1:

- ทางสำรอง A: ให้ผู้ใช้กรอก Job No. เพื่อดึงรายละเอียดทีละ Ticket
- ทางสำรอง B: สร้าง Local CRM Ticket Mirror/Inbox โดย Worker ดึงข้อมูลต่อ User

ไม่ควรสร้างหน้า List ที่ดึง Ticket ทั้งหมดจาก CRM โดยไม่สามารถยืนยัน Scope ได้

### 5.1 ผลการตรวจ Phase 0 เบื้องต้น — 2026-10-02

สถานะ: **Partial — ยืนยัน User Scope และ Flow การค้นหาแล้ว, Raw CRM Contract ยังต้องยืนยันก่อน Production**

#### หลักฐานจากหน้า CRM จริง

- หน้า Helpdesk List: `https://bluesea.seniorsoft.com/bluesea/BookLicence/MA/Support/DetailHD`
- หน้า List มีตัวกรอง `JobNo`, `เรื่อง`, `เรื่องที่บริการ`, `เจ้าของเรื่อง`, `ส่งเรื่องให้กับ`, `Product`, `Tester/Development`, `สถานะงาน` และ `สาขา`
- ตารางแสดงคอลัมน์หลัก ได้แก่ Subject, Member, Service Type, วันที่รับเรื่อง, Owner, Assignee, Follow-up, Job No., Product, Developer, Status, อายุงาน และ Email
- สถานะที่พบในหน้า CRM ได้แก่ `Open`, `Continue`, `Approve`, `Develop`, `Planning`, `Test`, `EditErr`, `Finish`, `Close` และตัวเลือกกลุ่ม `Not Close`
- หน้า List รองรับจำนวนรายการ 10/25/50/100 รายการต่อหน้าใน UI
- หน้า Export Support ยืนยันว่ามี Filter `ผู้รับเรื่อง`, `เจ้าของเรื่อง`, `ส่งเรื่องให้กับ`, Date Range และ Status แบบเลือกได้หลายค่า
- หน้า Export Support แสดงตารางผลลัพธ์ที่มี Job No., Service, Subject, Owner, Assignee, Product, Developer และ Status แต่จำนวนรายการเริ่มต้นเป็น 0 จนกว่าจะสั่งค้นหา
- หน้า Detail ยืนยันความแตกต่างของ Scope ได้: Ticket ที่เปิดตรวจมี `Owner` เป็น User หนึ่งคน แต่ `Assignee` เป็น User ปัจจุบัน จึงไม่ควรรวม Owner โดยอัตโนมัติใน MVP

#### ผลการทดสอบ User Scope

ทดสอบด้วยบัญชี CRM ที่แสดงเป็น User `6101`:

1. เปิดหน้า List โดยไม่กรองผู้รับผิดชอบ พบ 8 Ticket
2. เลือกตัวกรอง `ส่งเรื่องให้กับ = เหรียญทอง เจือบุญ (6101)`
3. กดค้นหา พบ 1 Ticket เท่านั้น:
   - Job No.: `BHD691002000004`
   - Assignee: `เหรียญทอง เจือบุญ (6101)`
   - Status: `Continue`

ข้อสรุปเบื้องต้น: Default Scope ของหน้า CRM ควรใช้ **`Assignto == CRM Username ของผู้ใช้ปัจจุบัน`** ตามแผนเดิม ไม่ควรใช้ `OwnerSubjectId` เป็นเงื่อนไข OR โดยอัตโนมัติ

#### ผลทดสอบ Export/Search จริงด้วย Date Range

ทดสอบหน้า Export Support โดยเลือก Assignee เดิมและเลือกวันที่ผ่าน Date Picker ของ CRM ตั้งแต่ `01/09/2569` ถึง `02/10/2569` รวมถึงช่วง Duedate เดียวกัน ผลลัพธ์ที่แสดงบนหน้า CRM คือ:

- จำนวนทั้งหมด `21 รายการ`
- ทุกรายการที่แสดงมี `ส่งเรื่องให้กับ = เหรียญทอง เจือบุญ (6101)`
- รายการถูกจัดกลุ่มตาม Owner ได้ แปลว่า Assignee ปัจจุบันสามารถมี Ticket ที่ Owner เป็น User อื่นได้
- DataTable แสดง `Showing 1 to 21 of 21 entries`
- การเลือกวันที่ผ่าน Date Picker ใช้รูปแบบแสดงผล พ.ศ. (`dd/MM/2569`) ขณะที่ Candidate API ใน Source Code ใช้ ISO Date (`YYYY-MM-DD`) จึงต้องแปลงรูปแบบเฉพาะใน Backend Adapter
- ตารางมี Summary/Grouping Row แทรก เช่น จำนวนรวมตาม Owner และ Service Type ซึ่งไม่ใช่ Ticket จริง ต้องไม่นำไปนับเป็นรายการหรือส่งเป็น `CrmTicketDto`
- Ticket Row ที่ยืนยันจาก UI มี Field หลัก ได้แก่ JobNo, Service Type, Member, Subject, Contact Name, Due Date, Contact Date, Owner, Assignee, Follow-up, Product, Developer, Status และ Last Reply Date

ข้อสรุปเพิ่มเติม: Filter Assignee + Date Range ทำงานผ่านหน้า CRM ได้จริงแล้ว แต่ยังไม่ถือว่าเป็นหลักฐานว่า API รองรับ Server-side Pagination เพราะชุดทดสอบมี 21 รายการและยังไม่ข้ามหน้า

#### Pagination Decision สำหรับ QA Hub

จาก Payload ใน Exporter เดิมไม่พบพารามิเตอร์มาตรฐานของ Server-side DataTable เช่น `draw`, `start`, `length`, `page` หรือ `pageSize` และเครื่องมือเดิมอ่านผลลัพธ์เป็นชุดตาม Date Range ดังนั้นให้ใช้แนวทางต่อไปนี้เป็น **Fallback ที่อนุมัติสำหรับ MVP**:

1. QA Hub รับ `page`/`pageSize` และคืน Pagination Contract ให้ Frontend เสมอ
2. Backend จำกัด Default Date Range ที่ 30 วันล่าสุด และกำหนด Maximum Date Range ตามผลทดสอบจริง
3. ถ้า CRM คืนข้อมูลทั้งช่วงวันที่ ให้ Backend กรอง Scope, ตัด Summary Row, Map DTO และทำ Pagination ฝั่ง QA Hub
4. กำหนดเพดานจำนวนแถวต่อ Request เช่น 5,000 แถว; ถ้าเกินให้คืน Error `CRM_RESULT_TOO_LARGE` พร้อมให้ผู้ใช้ลดช่วงวันที่
5. เมื่อพบว่า CRM รองรับ Pagination จริงภายหลัง ให้เปลี่ยน Adapter ไปใช้ Pagination ต้นทางโดยไม่เปลี่ยน Contract ฝั่ง Frontend

#### Endpoint ที่พบจากเครื่องมือเดิม

พบการใช้งานจาก `H:\APP\ExportReport\exporter.py`:

```text
POST https://bluesea.seniorsoft.com/booklicenceapi/Support/HelpDeskExport
Content-Type: application/json
Authorization: Bearer <per-user-token>
```

Payload ที่พบมี Filter สำคัญดังนี้:

```json
{
  "SJobtype": "0",
  "SService": "",
  "SProduct": "",
  "SStatus": [],
  "SOwnerSubject": null,
  "SAssignTo": null,
  "SContactDateS": "YYYY-MM-DD",
  "SContactDateE": "YYYY-MM-DD",
  "SDueDateS": "",
  "SDueDateE": ""
}
```

Response ถูกอ่านเป็น JSON และรองรับทั้ง Array หรือ Object ที่ห่อด้วย `data`, `result`, `items`, `rows` เป็นต้น โดย Field ที่พบจาก Exporter ได้แก่ `jobNo`, `status`, `jobType`, `sysserViceTypeName`, `productName`, `contactDate`, `duedate`, `member`, `fname`, `lname`, `assignto`, `subject`, `reply`, `service`, `source` และ `posted`

ข้อจำกัดของ Endpoint นี้:

- เป็น Endpoint สำหรับ Export ที่ยืนยันจาก Source Code ของเครื่องมือเดิม แต่ยังไม่ได้ยืนยันว่าเป็น Endpoint เดียวกับ DataTable หน้า Helpdesk
- ต้องส่งช่วงวันที่ จึงต้องกำหนด Default Date Range ของหน้า CRM
- ยังไม่ยืนยันว่า Response มี Server-side Pagination หรือคืนข้อมูลทั้งช่วงวันที่
- ยังไม่ได้ยืนยันชื่อ Field และค่าที่ใช้กรอง `Assignto` ว่ารับเป็น Staff Code โดยตรงหรือรับเป็น Object/Display Value
- UI ของ Export รองรับการกรอง Assignee และ Date Range แต่ยังไม่ถือเป็นหลักฐานว่า API รับค่า `SAssignTo` ในรูปแบบเดียวกันโดยตรง

#### ข้อสรุป Phase 0 ณ ตอนนี้

| รายการ | สถานะ | หลักฐาน/งานต่อ |
|---|---|---|
| CRM Login ต่อ User | ยืนยันแล้ว | CRM User ปัจจุบันแสดงเป็น `6101` |
| Filter ด้วย Assignee | ยืนยันแล้ว | หน้า List เหลือ 1 จาก 8 และหน้า Export คืน 21 รายการ โดยทุกแถว Assignee ตรงกับ User ปัจจุบัน |
| CRM Status เบื้องต้น | ยืนยันบางส่วน | พบ 9 สถานะหลักจากหน้า Detail/List |
| List/Search Endpoint | ยืนยันผ่าน UI, Raw Contract ยังไม่ล็อก | หน้า Export Search คืน 21 รายการจาก Filter เดิม; Endpoint Path มาจาก Exporter |
| Export Filter UI | ยืนยันแล้ว | มี Assignee, Owner, Date Range และ Multi-status Filter |
| Pagination Contract | QA Hub ล็อก Fallback แล้ว | CRM Upstream Pagination ยังไม่ยืนยัน; MVP ใช้ Date Range จำกัด + Paginate ฝั่ง Backend |
| OwnerSubjectId vs Assignto | เบื้องต้นเลือก Assignto | ต้องยืนยันกับผู้ใช้งานว่ารวม Ticket ที่เป็น Owner แต่ไม่ได้ Assign หรือไม่ |
| ทดสอบแยกข้อมูล 2 User | ยังไม่ทำ | ต้องใช้บัญชี CRM อีก User หรือชุดข้อมูลที่ยืนยัน Scope ได้ |
| Date Format | ยืนยันจาก UI แล้ว | CRM Date Picker ใช้วันที่ พ.ศ.; Backend ต้องแปลงเป็น ISO ก่อนเรียก API |
| Grouping Row | ยืนยันจาก UI แล้ว | ต้องกรอง Summary Row ออกจาก Ticket Row ก่อนทำ Mapping/Count/Pagination |

#### งานถัดไปของ Phase 0

1. ยืนยันว่า `HelpDeskExport` รองรับ Filter `SAssignTo` ด้วย Staff Code เช่น `6101`
2. ตรวจ Response จริงของ `HelpDeskExport` โดยไม่บันทึก Credential/Token ลง Repository หรือ Log
3. ตรวจ Request ของ DataTable หน้า Helpdesk เพื่อยืนยันว่าไม่ขัดกับ Fallback Pagination
4. ใช้ Default Date Range หน้า CRM 30 วันล่าสุด แปลงเป็น ISO Date และทดสอบเพดานจำนวนแถว
5. ทดสอบด้วย User ที่สองเพื่อยืนยันว่า Scope ไม่ปะปนกัน
6. เพิ่ม Parser Rule ให้แยก Summary/Grouping Row ออกจาก Ticket Row
7. เมื่อยืนยันครบ ให้ล็อก API Contract ก่อนเริ่ม Phase 1 Backend

## 6. สถาปัตยกรรมที่เสนอ

```text
QA Hub User Login
        |
        v
JWT UserId
        |
        v
CrmController
  - อ่าน CRM config ของ User ปัจจุบัน
  - สร้าง CRM token ต่อ User
  - เรียก CRM List/Search/Detail/Answers
  - ตรวจ Ownership และ Normalize DTO
        |
        v
CRM / BlueSea Helpdesk API
```

ข้อกำหนด:

- Browser เรียกเฉพาะ QA Hub API
- ห้ามฝัง CRM Base URL ที่มี Credential หรือ Bearer Token ใน Frontend
- ใช้ DTO ของ QA Hub แทนการส่ง JSON ดิบจาก CRM กลับไปยัง Browser
- รายการจำนวนมากต้องใช้ Server-side Pagination
- หากมี Cache ต้องผูก Cache Key กับ User Scope และห้ามใช้ Cache กลางข้าม User

## 7. Backend Plan

### 7.0 Phase 0 Draft Contract v0.1

สถานะ Contract ฝั่ง QA Hub: **เริ่ม Implement แบบ bounded fallback แล้ว** โดยมีข้อกำหนดที่ต้องห้ามเปลี่ยนระหว่างการ Implement ดังนี้:

| หัวข้อ | Contract ที่ล็อกไว้ | หมายเหตุ |
|---|---|---|
| Identity | อ่าน QA Hub User ID จาก JWT เท่านั้น | Frontend ห้ามส่ง User Scope |
| CRM Credential | โหลดจาก `CrmConfigurations.UserId` ของผู้ใช้ปัจจุบัน | ห้ามใช้ Credential กลาง |
| Default Scope | `Assignto == CRM Username ของผู้ใช้ปัจจุบัน` | ไม่รวม `OwnerSubjectId` อัตโนมัติ |
| Date Range | Default 30 วันล่าสุด | แปลงวันที่จาก QA Hub เป็น ISO ก่อนเรียก CRM |
| QA Hub Pagination | `page` เริ่มที่ 1, `pageSize` เริ่มต้น 25, จำกัดค่าสูงสุด | ถ้า CRM ไม่รองรับ Pagination ให้ใช้ Bounded Local Pagination หลังกรอง Scope |
| Cache | ถ้ามีต้องผูกกับ User ID + CRM Username + Query | ห้ามแชร์ข้าม User |
| CRM Adapter | Candidate คือ `POST /booklicenceapi/Support/HelpDeskExport` | ใช้ Date Range จำกัดเป็น Fallback จนกว่าจะยืนยัน Upstream Pagination |
| Failure Policy | 401 Retry ได้ 1 ครั้งหลัง Invalidate Token; Timeout/429/5xx แปลงเป็น Error กลาง | ห้าม Log Token/Password/ข้อมูลลูกค้าเต็มชุด |

Contract ฝั่ง CRM ที่ยังต้องยืนยันก่อน Production ใช้งาน:

- Response จริงเป็น Array หรือ Wrapper ใด และชื่อ Field ที่เป็น Canonical
- `SAssignTo` รับ Staff Code โดยตรงหรือรับ Object/Display Value
- CRM มี Pagination จริงหรือคืนข้อมูลทั้ง Date Range
- ค่าขีดจำกัดช่วงวันที่และ Rate Limit ที่เหมาะสม

### 7.0.1 Phase 0 Test Cases

1. User ที่มี CRM Configuration เรียก List แล้วเห็นเฉพาะรายการที่ `Assignto` ตรงกับ Username ของตน
2. Ticket ที่ Owner ต่างจาก Assignee ปัจจุบันยังต้องแสดงได้เมื่อ Assignee ตรง Scope
3. User ที่ไม่มี CRM Configuration ได้สถานะ `CRM_NOT_CONFIGURED` โดยไม่เรียก CRM
4. Frontend ส่ง `userId`, `crmUsername` หรือ Assignee อื่นเข้ามาแล้ว Backend ต้องไม่ใช้ค่าดังกล่าวกำหนด Scope
5. Unknown CRM Status ต้องแสดงเป็น `Unknown` และไม่ทำให้รายการหาย
6. CRM 401 ต้อง Invalidate Token และ Retry ได้ไม่เกิน 1 ครั้ง
7. CRM Timeout/429/5xx ต้องคืน Error Code กลางและไม่เปิดเผย Response ดิบ
8. Cache Key ของ User A ต้องไม่ถูกใช้ตอบ Query ของ User B
9. Pagination ของ QA Hub ต้องคง `total`, `page`, `pageSize` ให้สอดคล้องกับข้อมูลจริง
10. Date Picker แบบ พ.ศ. ที่หน้า CRM ต้องถูกแปลงเป็น ISO Date ก่อนส่งเข้า Adapter
11. Summary/Grouping Row จาก CRM ต้องไม่ถูกนับเป็น Ticket และต้องไม่ทำให้ `total` สูงเกินจริง

### 7.1 Controller และ Endpoint ที่เสนอ

เพิ่ม `CrmController` ที่ Route `api/v1/crm` และมี `[Authorize]` ทุก Endpoint

```text
GET  /api/v1/crm/connection
GET  /api/v1/crm/tickets
GET  /api/v1/crm/tickets/{jobNo}
```

`GET /api/v1/crm/tickets/{jobNo}` คืน Detail และ Answer history ใน response เดียว เพื่อลดการเรียก CRM ซ้ำ;
ยังไม่แยก `/answers` เป็น Endpoint เพิ่มจนกว่าจะมี use case อื่นที่ต้องโหลดเฉพาะ Comment

ตัวอย่าง Query ของรายการ:

```text
/crm/tickets?page=1&pageSize=25
  &search=
  &status=
  &projectId=
  &from=
  &to=
```

ไม่รับ `userId` ใน Query

### 7.2 DTO ที่เสนอ

```text
CrmConnectionDto
  configured
  enabled
  crmUsername
  displayName
  lastCheckedAt

CrmTicketListResultDto
  rows[]
  total
  page
  pageSize
  summary
  lastFetchedAt

CrmTicketDto
  jobNo
  subject
  description
  crmStatus
  serviceType
  assigneeStaffCode
  assigneeName
  ownerSubjectId
  productId
  versionId
  projectId
  createdAt
  updatedAt
  ageInDays
  linkedDefectId
  linkedDefectCode
  syncStatus
  lastSyncedAt
```

ชื่อ Field จริงต้องปรับตาม Response Contract ของ CRM

### 7.2.1 QA Hub API Contract Example

Request:

```http
GET /api/v1/crm/tickets?page=1&pageSize=25&search=barcode&status=Continue&from=2026-09-02&to=2026-10-02
Authorization: Bearer <qa-hub-jwt>
```

ข้อกำหนดสำคัญ:

- ไม่รับ `userId`, `crmUsername`, `assigneeStaffCode` หรือ `ownerSubjectId` จาก Query เพื่อกำหนด Scope
- ถ้าไม่ส่ง `from/to` ให้ใช้ 30 วันล่าสุด
- ถ้า `pageSize` เกิน Maximum ให้ Clamp หรือคืน Validation Error ตามมาตรฐาน API ของระบบ
- `search` ใช้กับ Job No. และ Subject หลังจาก Backend ได้ข้อมูลตาม Scope แล้ว

Response:

```json
{
  "rows": [
    {
      "jobNo": "BHD...",
      "subject": "...",
      "crmStatus": "Continue",
      "serviceType": "Question",
      "assigneeStaffCode": "<current-crm-username>",
      "assigneeName": "...",
      "ownerSubjectId": "...",
      "productId": "...",
      "contactDate": "2026-09-29T00:00:00Z",
      "dueDate": "2026-09-29T00:00:00Z",
      "lastReplyAt": "2026-09-29T08:27:00Z"
    }
  ],
  "total": 1,
  "page": 1,
  "pageSize": 25,
  "summary": {
    "total": 1,
    "open": 0,
    "inProgress": 1,
    "closed": 0,
    "unknownStatus": 0
  },
  "lastFetchedAt": "2026-10-02T...Z"
}
```

`total` และ `summary` ต้องคำนวณจาก Ticket Row หลังกรอง Scope และตัด Summary/Grouping Row แล้ว ไม่ใช่จำนวนแถวทั้งหมดที่ CRM ส่งกลับมา

### 7.2.2 Error Contract

| HTTP | Code | ความหมาย |
|---:|---|---|
| 401 | `CRM_NOT_CONFIGURED` | ผู้ใช้ยังไม่มีบัญชี CRM หรือ Configuration ใช้งานไม่ได้ |
| 401 | `CRM_UNAUTHORIZED` | CRM ปฏิเสธ Credential หลัง Retry แล้ว |
| 408 | `CRM_TIMEOUT` | CRM ตอบกลับไม่ทันตาม Timeout |
| 413 | `CRM_RESULT_TOO_LARGE` | ผลลัพธ์เกินเพดานที่กำหนด ต้องลดช่วงวันที่ |
| 429 | `CRM_RATE_LIMITED` | CRM จำกัดจำนวน Request |
| 502 | `CRM_BAD_RESPONSE` | Response ไม่ตรงรูปแบบที่ Parser รองรับ |
| 503 | `CRM_UNAVAILABLE` | CRM หรือ Network ใช้งานไม่ได้ชั่วคราว |

Error Response ห้ามคืน Password, Bearer Token, Raw CRM Response หรือข้อมูลลูกค้าที่ไม่จำเป็นต่อการแก้ปัญหา

### 7.3 Service/Client

- เพิ่ม `ListJobsAsync` ใน `CrmApiClient`
- แยก Parser ของ CRM Response ไว้จุดเดียว
- รองรับ Response ที่เป็น DataTables หรือ JSON List
- Map Date/Status/Assignee ให้เป็นรูปแบบกลางของ QA Hub
- จัดการ 401 ด้วย Token Invalidate แล้ว Retry ได้อย่างจำกัด 1 ครั้ง
- จัดการ Timeout/429/5xx ด้วย Error ที่สื่อสารกับ UI ได้
- Log เฉพาะ Job No., Status Code และ Correlation ID
- ห้าม Log Password, Bearer Token หรือ Response ที่มีข้อมูลลูกค้าเกินความจำเป็น

### 7.4 Permission

เพิ่ม Permission ใหม่:

```text
CRM.VIEW
```

ผู้ใช้ที่ไม่มี Permission นี้จะไม่เห็นเมนูและไม่สามารถเรียก Endpoint ได้ โดย `SYS_ADMIN` เป็น role override ตามมาตรฐาน Policy ของระบบ

ถ้าจะเพิ่มการแก้ไขข้อมูลในอนาคต ให้แยก Permission เพิ่ม เช่น `CRM.EDIT` และไม่ใช้ `CRM.VIEW` ปะปน

## 8. Frontend/UI Plan

### 8.1 Navigation

เพิ่มกลุ่มใหม่ใน Sidebar:

```text
INTEGRATIONS
 └─ CRM
```

ไอคอนแนะนำ: `support_agent` หรือ `hub`

ไฟล์หลักที่เกี่ยวข้อง:

- `src/pages/CrmPage.tsx`
- `src/Crm.css`
- `src/shared/appShared.tsx`
- `src/App.tsx`

เพิ่ม `crm` ใน `Page`, `nav`, `pageNames`, `pageIds`, `viewPermission` และ Routing แบบ `React.lazy`

### 8.2 Page Header

หัวหน้าแสดง:

- ชื่อ `CRM`
- คำอธิบาย `Ticket ที่เกี่ยวข้องกับบัญชี CRM ของคุณ`
- Badge `CRM: Connected / Not Configured / Error`
- ชื่อ CRM Username ปัจจุบัน
- เวลาที่โหลดข้อมูลล่าสุด
- ปุ่ม `Refresh`
- ปุ่ม `ตั้งค่าบัญชี CRM ของฉัน` เมื่อยังไม่ได้ตั้งค่า

### 8.3 Summary KPI

แสดงเป็นการ์ดที่กดเพื่อกรองรายการได้:

- Open
- In Progress
- Ready for QA
- Closed ล่าสุด
- Stale/Sync Error

จำนวนต้องมาจาก Response Summary เดียวกับรายการ ไม่คำนวณจากข้อมูลที่โหลดมาเพียงบางหน้า

### 8.4 Filter Bar

Desktop:

- Search Job No./Subject
- CRM Status
- Project/Mapping
- วันที่เริ่มต้น/สิ้นสุด
- ปุ่ม Clear Filters
- Toggle List/Board เมื่อ Board พร้อมใช้งาน

Mobile:

- Stack เป็นหนึ่งคอลัมน์
- Search อยู่ด้านบนสุด
- Filter อื่นอยู่ใน Accordion หรือเรียงต่อกัน
- ไม่มี Page-level Horizontal Scroll

ช่อง Search ที่ยิง API ต้อง Debounce ประมาณ 300ms และยกเลิก Request เก่าด้วย `AbortController`

### 8.5 Ticket List

Desktop ใช้ตาราง:

- CRM Ticket
- Subject
- CRM Status
- Assignee
- Product/Project
- Version/Release
- อายุงาน
- Updated
- Action

Mobile ใช้ `table-cards` พร้อม `data-label` ตาม Design System

สถานะต้องแสดงทั้ง Badge และข้อความ ห้ามสื่อด้วยสีอย่างเดียว

### 8.6 Detail Modal

ใช้ `ModalShell` และเป็น Read-only ใน MVP:

- Header: Job No., Subject, CRM Status
- Section ข้อมูล Ticket
- Section Description
- Section Answers/Comments
- Section ความสัมพันธ์กับ QA Hub
- Footer: `ปิด` และ `เปิดใน CRM`

Modal ต้องรองรับ Loading/Error แยกจาก Empty และไม่ปิดด้วย Background Click หากมี Request กำลังทำงาน

### 8.7 Board View ระยะถัดไป

เมื่อยืนยัน Status Mapping แล้วจึงเพิ่ม Board View ตาม [`CRM_DEFECT_KANBAN_PLAN.md`](<H:/APP/QAManagementSystem/Document/03-Architecture-and-Plan/CRM_DEFECT_KANBAN_PLAN.md:102>):

- Board เป็น Read-only ก่อน
- แสดง Unknown Column เสมอเมื่อมี Status ที่ยังไม่ Map
- Horizontal Scroll ต้องอยู่เฉพาะ Board Container
- Card ต้องแสดง Job No., Subject, Status, Assignee, Project, Age และ Last Sync

## 9. Responsive และ Accessibility

ต้องตรวจอย่างน้อยที่:

- 1440px Desktop
- 1024px Tablet
- 768px Mobile/Tablet
- 390px Small Mobile

ข้อกำหนด:

- ใช้ Design Token ใน `UI_DESIGN_SYSTEM.md`
- Font ขั้นต่ำ 11px
- ใช้ Material Symbols สำหรับ Icon มาตรฐาน
- ทุก Icon-only Button ต้องมี `aria-label` และ `title`
- ทุก Input ต้องมี Visible Label หรือ `aria-label`
- รองรับ Keyboard Focus และ Enter เพื่อเปิด Detail
- ชื่อ Ticket/Subject ยาวต้อง Wrap ได้
- ห้ามเกิด Page-level Horizontal Scroll
- ตารางเลื่อนได้เฉพาะ Container หรือแปลงเป็น Card บน Mobile

## 10. Error และสถานะที่ต้องออกแบบ

| สถานะ | UI ที่ต้องแสดง |
|---|---|
| ยังไม่ตั้งค่าบัญชี CRM | Empty State + ปุ่มตั้งค่าบัญชี |
| บัญชีถูกปิดใช้งาน | Warning + ลิงก์ไปเปิดใช้งาน |
| CRM Token หมดอายุ | แจ้งให้ลองใหม่ โดยไม่แสดง Token |
| CRM Timeout/5xx | Inline Error + Retry |
| CRM Rate Limit | Warning พร้อมเวลารอถ้ามี |
| ไม่มี Ticket | Empty State ไม่ใช่ Error |
| Status ไม่รู้จัก | Badge `Unknown` และยังแสดงรายการ |
| ข้อมูลเก่า | Badge `Stale` พร้อม Last Synced |
| Request ถูกยกเลิก | ไม่แสดง Error หลอกผู้ใช้ |

## 11. Data Model และ Cache Decision

### ทางเลือก A — Proxy แบบสด

ใช้เมื่อ CRM มี List/Search API ที่มี Pagination พร้อมใช้งาน:

- ไม่เพิ่มตาราง Ticket ใน MVP
- ทุกครั้งที่เปิด/Refresh เรียก CRM ผ่าน Backend
- อาจใช้ Cache สั้น 30–60 วินาทีแบบผูก User Scope

### ทางเลือก B — Local CRM Ticket Mirror

ใช้เมื่อ CRM API ช้า, Rate Limit ต่ำ, ต้องการ History หรือ API List ไม่เสถียร:

- เพิ่มตาราง `CrmTicketSnapshots` หรือขยาย `CrmInboundTickets`
- เก็บ `JobNo` เป็น Unique Key
- เก็บ Owner/Assignee และ User Scope ที่ใช้ดึงข้อมูล
- Worker ดึงข้อมูลแยกตาม User ที่มี CRM Credential
- หน้า CRM อ่านจาก Local Read Model และแสดง Last Synced/Stale

คำแนะนำ: เริ่มจากทางเลือก A หลังยืนยัน List/Search API แล้วประเมินทางเลือก B เมื่อข้อมูลหรือ Rate Limit โตขึ้น

## 12. แผนแบ่ง Phase

### Phase 0 — Contract และ Security Design

- ยืนยัน List/Search API และ Response จริง
- ยืนยัน Field Scope ของ User
- ยืนยัน Status และ Closed Mapping
- ยืนยัน Rate Limit/Timeout
- เขียน API Contract และ Test Cases

ผลลัพธ์: สามารถเริ่ม Backend ได้โดยไม่เดา Contract

### Phase 1 — Backend Read-only API

- เพิ่ม `CrmController`
- เพิ่ม `ListJobsAsync`
- เพิ่ม DTO/Parser
- เพิ่ม `CRM.VIEW`
- เพิ่ม Ownership Guard
- เพิ่ม Error Mapping และ Logging
- เพิ่ม Unit/Integration Tests

ผลลัพธ์: API คืนเฉพาะ Ticket ของ User ปัจจุบัน

### Phase 2 — CRM Frontend Page

- เพิ่มเมนูและ Lazy Route
- เพิ่ม Page Header, Connection State และ KPI
- เพิ่ม Search/Filter/Pagination
- เพิ่ม List และ Mobile Cards
- เพิ่ม Read-only Detail Modal
- เพิ่ม Retry/Empty/Error states

ผลลัพธ์: ผู้ใช้ดูงาน CRM ของตนเองจาก QA Hub ได้

### Phase 3 — Board และ QA Hub Linking

- เพิ่ม Status Mapping Configuration
- เพิ่ม Board View แบบ Read-only
- เพิ่ม Link ไป Defect เดิม
- พิจารณา Create Defect จาก CRM Ticket
- เพิ่ม Audit/History ตามความจำเป็น

### Phase 4 — Controlled CRM Update (Optional)

- แก้ไข Status/Assignee จาก QA Hub
- ตรวจ Conflict และ Last Known Version
- เพิ่ม Permission `CRM.EDIT`
- บันทึก Audit ค่าเดิม/ค่าใหม่/ผลตอบกลับจาก CRM

### Phase 5 — Create QA Hub Defect from CRM Ticket

- เพิ่มการสร้าง Defect ใน QA Hub จาก Ticket ที่ผู้ใช้เปิดดูและผ่าน CRM ownership check แล้ว
- บังคับ Project Access และ `DEFECT.EDIT`; ใช้ Project จาก Context ที่ผู้ใช้เลือก
- ป้องกันการสร้างซ้ำเมื่อ Ticket เดิมถูกผูกกับ Defect อยู่แล้ว
- เก็บ `CrmTicketId`, CRM status/assignee snapshot และ Activity `CreatedFromCrm`
- ให้ผู้ใช้ตรวจสอบ/แก้ไข Title, Severity และ Description ก่อนบันทึก
- ไม่ส่งข้อมูลเขียนกลับไปยัง CRM ใน Phase นี้

## 13. Acceptance Criteria

### Security/Data Isolation

- User A เห็นเฉพาะ Ticket ตาม CRM Scope ของ User A
- User A ไม่สามารถดู Ticket ของ User B ด้วยการแก้ Query หรือ `jobNo`
- Backend ไม่รับ User Scope จาก Frontend
- ไม่พบ CRM Password/Token ใน Browser หรือ Log
- Cache ถ้ามีต้องไม่แชร์ข้าม User

### Backend

- List รองรับ Pagination และ Search
- Detail/Answers ตรวจ Ownership ซ้ำ
- CRM 401, Timeout, 429 และ 5xx ถูกแปลงเป็น Error ที่ UI เข้าใจได้
- Status ใหม่ที่ไม่รู้จักไม่ทำให้รายการหาย
- ไม่มี CRM Configuration ต้องตอบสถานะที่แยกจาก Empty Ticket

### UI

- เมนูแสดงเฉพาะผู้มี `CRM.VIEW` หรือ role `SYS_ADMIN`
- แสดง Connection State และ CRM Username ปัจจุบัน
- KPI สอดคล้องกับรายการทั้งหมด ไม่ใช่เฉพาะหน้าปัจจุบัน
- Search มี Debounce และยกเลิก Request เก่า
- Detail เปิดด้วย Keyboard ได้
- Desktop และ Mobile ไม่มี Page-level Horizontal Scroll
- Loading, Empty และ Error แสดงแยกกันชัดเจน

### Verification

- ทดสอบด้วย User อย่างน้อย 2 คนที่มี CRM Scope ต่างกัน
- ทดสอบ Token หมดอายุและการ Retry
- ทดสอบ CRM Response ผิดรูปแบบ
- ทดสอบข้อมูลจำนวนมากและ Pagination
- รันคำสั่งต่อไปนี้หลังแก้ Frontend:

```powershell
cd src/ProMaxx2.QA.Web
npm.cmd run build
npm.cmd run lint
git diff --check
```

## 14. ไฟล์ที่คาดว่าจะเปลี่ยน

### Backend

- `src/ProMaxx2.QA.Api/Controllers/CrmController.cs`
- `src/ProMaxx2.QA.Api/Services/CrmApiClient.cs`
- `src/ProMaxx2.QA.Api/Services/CrmTicketDetailService.cs`
- `src/ProMaxx2.QA.Api/Services/CrmConfigurationService.cs` หากต้องเพิ่ม Connection Check
- Permission/Policy และ Seed Role
- Tests ใน `tests/ProMaxx2.QA.UnitTests`

### Frontend

- `src/ProMaxx2.QA.Web/src/pages/CrmPage.tsx`
- `src/ProMaxx2.QA.Web/src/Crm.css`
- `src/ProMaxx2.QA.Web/src/shared/appShared.tsx`
- `src/ProMaxx2.QA.Web/src/App.tsx`
- `src/ProMaxx2.QA.Web/src/shared/types.ts` หากใช้ Shared CRM DTO Types

### Documentation หลัง Implement

- อัปเดต `Document/02-Developer-Blueprint/UI_DESIGN_SYSTEM.md` และ Change Log
- อัปเดต `Document/02-Developer-Blueprint/API_SPECIFICATION.md`
- อัปเดต `Document/02-Developer-Blueprint/SCREEN_SPECIFICATION.md`
- อัปเดตสถานะใน `CRM_INTEGRATION_PLAN.md`
- อัปเดต Scope/Phase ใน `CRM_DEFECT_KANBAN_PLAN.md` และ `DEFECT_KNOWLEDGE_PLAN.md` หากมีการเปลี่ยนจาก Linked Defect เป็น Full CRM Ticket View

## 15. จุดที่ต้องยืนยันก่อนเริ่มพัฒนา

1. CRM มี API List/Search Ticket สำหรับหน้า Support แล้วหรือไม่
2. ต้องการกรองเฉพาะ Ticket ที่ `Assignto` เป็นผู้ใช้ปัจจุบัน หรือรวม Ticket ที่ `OwnerSubjectId` เป็นผู้ใช้ปัจจุบันด้วย
3. ผู้ใช้ต้องเห็นเฉพาะงานของตัวเอง หรือมี Role สำหรับดูงานทั้งทีม
4. Status ใดถือเป็น Ready for QA และ Closed
5. MVP ต้องการ List อย่างเดียว หรือรวม Board View ตั้งแต่รุ่นแรก
6. หากไม่มี List API ยอมรับการกรอก Job No. หรือไม่

## 16. Progress Log

### 2026-10-03 - Final CRM response-shape verification

- Production HelpDeskExport returned an array of 20 records shaped as `{ fd, answers }`; ticket fields are nested under `fd`.
- Updated the adapter to unwrap `fd` before mapping JobNo, Assignee, and dates. Unit tests now pass `459/459`; API build has `0 warnings / 0 errors`.
- Authenticated production UI verification after API restart: default range loaded 20 tickets and displayed `BHD690917000005` with status `Continue` and assignee `6101`; timezone normalization now preserves the CRM source date `17/09/2569 17:09`.
- Exact date-range behavior is covered by parser tests for `2026-09-01` through `2026-10-01`; direct UI date manipulation through the browser date control was not used as evidence because the control did not emit a reliable React change event in this test harness.

### 2026-10-03 - Production List/Detail verification completed

- Authenticated production CRM page loaded 20 tickets for CRM username `6101`; KPI summary showed Open `2`, In Progress `15`, and Closed `3`.
- Confirmed `BHD690917000005` appears in the list with status `Continue`, Service `Question`, Product `iConnect2`, Assignee `6101`, and contact date `17/09/2569 17:09:00`.
- Opened the ticket detail read-only modal successfully. It showed the same ticket metadata, Description fallback, and `1` CRM comment dated `17/09/2569 17:11:46`, with the CRM deep link available.
- Phase 2 List/Detail integration gate is complete for the verified authenticated user. Remaining gate: repeat data-isolation verification with a second CRM user/account and a ticket known to belong to that account.

### 2026-10-03 - Automated two-user scope isolation coverage

- Added a parser test using one CRM response containing tickets assigned to `6101` and `6202`.
- Verified that parsing with `6101` returns only the two `6101` tickets, while parsing with `6202` returns only the `6202` ticket; display-name-plus-code assignment is included in the same scenario.
- Backend Unit Tests now pass `460/460`; `git diff --check` passes. Production verification with a second authenticated account is still pending because only the `6101` session is available.

### 2026-10-03 - CRM configuration inventory check

- Read-only database inventory found four active QA Hub users with per-user CRM configuration: `6101`, `6619`, `6914`, and `6915`.
- Verified from the implementation that `CrmController` obtains the user ID from the authenticated `sub`/`NameIdentifier` claim and passes it to `CrmConfigurationService`; runtime credentials and CRM username are therefore resolved from that user’s own `CrmConfigurations` row.
- No credentials were read, decrypted, created, or changed. Production two-user verification remains pending until a second authenticated browser session is provided.

### 2026-10-03 - Phase 3 started: Board and QA Hub Defect linking

- Added a read-only Board view to the CRM page with Open, In Progress, and Closed columns using the current list/filter result.
- Added `GET /api/v1/crm/tickets/{jobNo}/defect` to show the linked QA Hub Defect after re-checking the CRM ticket scope for the current user.
- Added `POST /api/v1/crm/tickets/{jobNo}/link-defect` with `DefectEdit` authorization, Project Access validation, CRM ownership re-check, duplicate-link protection, CRM snapshot capture, and `CrmTicketLinked` activity logging.
- Added a `ModalShell` flow to search and select an accessible Defect from the CRM Ticket detail modal; no Create Defect or CRM write-back flow is included in this increment.
- Frontend build/lint passed; API build passed with `0 warnings / 0 errors` using `UseAppHost=false`; Backend Unit Tests passed `460/460`; `git diff --check` passed.
- Final Phase 3 verification: API Production profile started from the API project directory, `/health` returned `200 Healthy`, anonymous CRM defect-link access returned `401`, backend tests remained `460/460`, and the second-user production verification was intentionally skipped per current instruction.

### 2026-10-03 — ตรวจสอบกรณี Ticket จริง BHD690917000005

- ตรวจสอบจาก CRM Helpdesk โดยตรงแล้วพบ `BHD690917000005` จริง: วันที่ติดต่อ `17/09/2569 17:09`, สถานะ `Continue`, Assign To แสดงเป็น `เหรียญทอง เจือบุญ (6101)`
- พบสาเหตุที่ QA Hub แสดง 0 รายการ 2 จุด: Parser เดิมเทียบ Assignee แบบตรงตัวกับ `6101` จึงไม่รับค่าที่เป็นชื่อพร้อมรหัส และตัวแปลงวันที่เดิมไม่รองรับวันที่พุทธศักราชที่มีเวลา เช่น `17/09/2569 17:09` ทำให้ถูกตัดออกจากช่วงวันที่
- แก้ Parser ให้รองรับ Assignee แบบ display name + code, field alias ของ CRM และวันที่พุทธศักราชพร้อมเวลา โดยยังคงกรองเป็น token ของรหัสเพื่อไม่ให้ข้อมูล User อื่นปะปน
- เพิ่ม Unit Test สำหรับ Ticket ตัวอย่างและกรณีป้องกัน partial code; ผลล่าสุด Backend Unit Tests `458 passed`, API Build `0 warning / 0 error`, `git diff --check` ผ่าน
- รอ Restart API และทดสอบหน้า Production ซ้ำด้วยช่วง `2026-09-01` ถึง `2026-10-01` เพื่อปิด Integration Gate

### 2026-10-02 — เริ่ม Phase 0: Contract และ Security Design

- ตรวจสอบโครงสร้างระบบเดิม, CRM Credential/Token flow และ `CrmApiClient` แล้ว
- ตรวจสอบหน้า CRM Helpdesk จริงและยืนยันว่า Filter `Assignto` ใช้จำกัดรายการตาม CRM User ได้ในเบื้องต้น
- ตรวจสอบหน้า Export Support เพิ่มเติม ยืนยันชุด Filter และคอลัมน์ผลลัพธ์ แต่ยังไม่พบหลักฐาน Pagination Contract จาก UI เพียงอย่างเดียว
- พบ Candidate List API จาก Exporter เดิมคือ `POST /booklicenceapi/Support/HelpDeskExport` พร้อม Filter `SAssignTo` และ Date Range
- ทดสอบ Search จริงด้วย Date Picker และ Assignee ของ User ปัจจุบัน ได้ผล 21 รายการ และทุกแถวมี Assignee ตรงกับ Scope ที่เลือก
- ตรวจพบว่า CRM แทรก Summary/Grouping Row ตาม Owner และ Service Type จึงเพิ่ม Parser Rule ให้แยกออกจาก Ticket Row
- จัดทำ Draft API Contract v0.1 และ Test Cases สำหรับเริ่มออกแบบ Backend โดยยังติดป้าย Assumption ในจุดที่ CRM Raw Contract ไม่ยืนยัน
- ล็อก Pagination Fallback สำหรับ MVP: Date Range จำกัด, กรอง/Map/ตัด Summary Row ที่ Backend และ Paginate ฝั่ง QA Hub พร้อมเพดานจำนวนแถว
- เพิ่มตัวอย่าง QA Hub Request/Response และ Error Contract เพื่อแยก API ภายในออกจาก CRM Raw Response
- ยังไม่ปิด Phase 0 เนื่องจากต้องยืนยัน Response Contract, Pagination, ค่า `SAssignTo` ที่ API รับจริง และทดสอบ Data Isolation ด้วย User อย่างน้อย 2 คน

### 2026-10-02 — เริ่ม Phase 1: Backend Read-only API

- เพิ่ม `CrmController` ที่ `GET /api/v1/crm/connection` และ `GET /api/v1/crm/tickets`
- เพิ่ม `CrmTicketListQuery`, Ticket DTO, Summary DTO และ Parser ที่รองรับ Array/Wrapper response
- บังคับ Scope จาก JWT User ID → CRM Configuration → `Assignto == CRM Username` และไม่รับ Scope จาก Query ของ Frontend
- เรียก Candidate Adapter `POST /Support/HelpDeskExport` ด้วย Date Range ค่าเริ่มต้น 30 วัน และทำ Filter/Date Range/Pagination ฝั่ง QA Hub
- ตัด Summary/Grouping Row โดยรับเฉพาะ record ที่มี `JobNo`; จำกัดผลลัพธ์ที่ Ticket 5,000 รายการ
- เพิ่ม Error Mapping สำหรับ `CRM_NOT_CONFIGURED`, `CRM_UNAUTHORIZED`, `CRM_TIMEOUT`, `CRM_RESULT_TOO_LARGE`, `CRM_RATE_LIMITED`, `CRM_BAD_RESPONSE` และ `CRM_UNAVAILABLE`
- เพิ่ม Policy/Seed `CRM.VIEW` ให้ role มาตรฐานที่มีสิทธิ์อ่านระบบ
- เพิ่ม Unit Tests สำหรับ Scope Isolation, Summary Row, Status/Search และ Pagination; ผลล่าสุด `446 passed`
- สถานะ Phase 1: Backend MVP พร้อมทดสอบภายในแล้ว แต่ยังต้อง Integration Test กับ Response จริงของ CRM และ User Scope อย่างน้อย 2 คนก่อนปิด Phase

### 2026-10-02 — เริ่ม Phase 2: Frontend CRM Page

- เพิ่มเมนูและ route `CRM` แบบ lazy-loaded พร้อมตรวจสิทธิ์ `CRM.VIEW`
- เพิ่มหน้า `CrmPage` สำหรับ Connection State, CRM Username, KPI, Date/Status/Search Filter และ Pagination
- ใช้ Search Debounce ร่วมกับ `AbortController` เพื่อยกเลิก Request เก่า และแยก Loading, Empty, Error, Not Configured ให้ชัดเจน
- ใช้ responsive `table-cards` สำหรับ Ticket List และออกแบบให้ไม่เกิด Page-level Horizontal Scroll บน Mobile
- เพิ่ม Read-only Ticket Detail Modal ผ่าน QA Hub Detail API พร้อม Keyboard/Focus behavior ผ่าน `ModalShell`
- เชื่อม Detail API กับ CRM `HelpDesksJob` และ `HelpDeskAnswerMain` เพื่อแสดง Description และ Comment history
- เพิ่มลิงก์เปิด Ticket ต้นทางใน CRM จาก Detail Modal ด้วย `target="_blank"` และ `rel="noreferrer"`
- เพิ่มการแสดงสถานะด้วย Badge พร้อมข้อความ และจำกัดการทำงานเป็น Read-only ตามขอบเขต MVP
- ตรวจแล้ว `npm.cmd run build`, `npm.cmd run lint` และ `git diff --check` ผ่าน; backend unit tests ล่าสุด `448 passed`
- ยังไม่ปิด Phase 2: ต้องตรวจหน้าใน authenticated session บน Desktop/Mobile และทดสอบ List/Detail/Answer API กับ CRM Response จริง

### 2026-10-03 — Phase 2 Verification

- รัน Backend Unit Tests ซ้ำหลังเพิ่ม Token Retry และ Detail Parser แล้ว: `448 passed`, `0 failed`
- รัน Frontend Build และ Lint ซ้ำ: ผ่านทั้งสองคำสั่ง และสร้าง lazy chunk ของ `CrmPage` สำเร็จ
- รัน `git diff --check`: ผ่าน ไม่มี whitespace error
- เปิด local web build ใน Chrome เพื่อตรวจเส้นทางและหน้า Login แล้ว แต่ยังไม่มี authenticated QA Hub session จึงยังไม่ยืนยัน visual QA ของ CRM บนข้อมูลจริง
- เพิ่ม Detail API และเชื่อม Comment/ประวัติการติดต่อจาก Contract ที่มีอยู่ใน `CrmApiClient` เดิม; ยังต้องทดสอบกับ CRM จริงก่อนปิด Integration Gate
- เพิ่มลิงก์ `เปิดใน CRM` ใน Detail Modal และตรวจซ้ำด้วย Frontend Build/Lint หลังแก้ไข: ผ่าน
- รวม Token Retry สูงสุด 1 ครั้งหลัง 401 ให้ CRM GET ของ Detail, Answer, Directory และ Lookup; Backend Build ผ่าน 0 warning/0 error และ Unit Tests ล่าสุด `448 passed`
- ตรวจหน้า Helpdesk CRM จริงแบบ Read-only แล้วยืนยันว่าหน้า Detail มีฟิลด์ Subject, Contact Date, Status, Service, Owner/Assignee, Product, Description และ Answer History ตามขอบเขต Adapter; ยังไม่บันทึกข้อมูลลูกค้าหรือ Ticket จริงลงเอกสาร
- ปรับข้อความ `CRM_NOT_CONFIGURED` ให้ชี้ไปยังปุ่ม `บัญชี CRM ของฉัน` สำหรับการตั้งค่าแบบ self-service ต่อ User ให้ตรงกับ App shell เดิม
- เพิ่ม CTA `ตั้งค่าบัญชี CRM ของฉัน` บน Inline Alert ของหน้า CRM และเชื่อมเข้ากับ Modal ตั้งค่าบัญชีเดิมโดยตรง
- ตรวจ Frontend Build/Lint หลังเพิ่ม CTA แล้วผ่านทั้งสองคำสั่ง และ `git diff --check` ผ่าน
- ปรับลำดับการโหลดให้ตรวจ Connection ก่อน List; User ที่ยังไม่ตั้งค่าหรือ Connection Error จะไม่ยิง `/crm/tickets` ซ้ำ
- หลังบันทึกบัญชี CRM สำเร็จ หน้า CRM จะ re-check Connection และโหลดรายการใหม่อัตโนมัติผ่าน `configVersion`
- ตรวจ Frontend Build/Lint หลังเพิ่ม auto-refresh flow แล้วผ่านทั้งสองคำสั่ง และ `git diff --check` ผ่าน
- เพิ่มการแสดง `อายุงาน` และ `ตอบล่าสุด` ใน Ticket row/Detail จาก `ContactDate` และ `LastReplyAt` โดยมี fallback เป็น `-`
- เพิ่ม field alias ใน Parser สำหรับ Service Type, Product, Owner, Member และ Last Reply ที่ CRM อาจส่งต่างชื่อระหว่าง List/Detail พร้อม Unit Test
- Verification ล่าสุดหลังเพิ่ม alias: Backend Unit Tests `449 passed`, API Build `0 warning / 0 error`
- เพิ่ม Stale Alert เมื่อข้อมูลถูกโหลดเกิน 15 นาที พร้อม Last Fetched และปุ่ม Refresh โดยไม่บล็อกการอ่านรายการเดิม
- ตรวจ Frontend Build/Lint หลังเพิ่ม Stale State แล้วผ่านทั้งสองคำสั่ง และ `git diff --check` ผ่าน
- แยก UX ระหว่าง Connection Error กับ Not Configured: กรณีตรวจสอบสถานะไม่สำเร็จจะแสดงปุ่มลองตรวจสอบอีกครั้ง และไม่แสดง CTA ตั้งค่าบัญชีโดยอัตโนมัติ
- Hardening Answer Adapter: แยก `ParseHelpDeskAnswers` และรองรับ `ansDate`/`image` ที่ CRM ส่งเป็น string, number หรือ null โดยไม่ทำให้ Detail API ล้ม พร้อม Unit Test
- Verification ล่าสุดหลัง hardening Answer Adapter: Backend Unit Tests `450 passed`, API Build `0 warning / 0 error`
- ปรับ CRM Error State ให้แยกหัวข้อ/คำแนะนำตาม stable code ของ Backend สำหรับ Not Configured, Unauthorized, Rate Limited, Timeout และ Unavailable ทั้ง List และ Detail Modal
- ตรวจ Frontend Build/Lint หลังเพิ่ม CRM Error State แล้วผ่านทั้งสองคำสั่ง และ `git diff --check` ผ่าน
- เพิ่ม Error mapping สำหรับ `CRM_RESULT_TOO_LARGE`, `CRM_BAD_RESPONSE`, `CRM_TICKET_NOT_FOUND` และ `CRM_INVALID_QUERY` ให้สอดคล้องกับ Error Contract ของ API
- เพิ่ม Retry action ใน Detail Modal เมื่อโหลด Detail/Answer ไม่สำเร็จ โดยคง Ticket context เดิมไว้และไม่ต้องปิด Modal
- ปรับ Connection Badge ให้ไม่สื่อว่า CRM เชื่อมต่อสำเร็จจากการมี Configuration เพียงอย่างเดียว; แยกเป็น `ตั้งค่าแล้ว`, `ต้องตั้งค่า` และ `ตรวจสอบไม่ได้`
- Hardening Detail Adapter: รองรับ Response ของ `HelpDesksJob` แบบ Array และ wrapper ที่มี `data`, `result`, `job`, `helpDeskJob` หรือ `jobs`; รูปแบบที่ไม่รองรับถูกแปลงเป็น `CrmIntegrationException` พร้อม Unit Tests
- Verification ล่าสุดหลัง hardening Detail Adapter: Backend Unit Tests `452 passed`, API Build `0 warning / 0 error` และ `git diff --check` ผ่าน
- Hardening Answer Adapter เพิ่มเติม: รองรับ wrapper `helpDeskAnswers`, `answers`, `data` และ `result` รวมถึง field แบบ PascalCase; เพิ่ม Unit Test สำหรับ wrapper ซ้อนและรูปแบบ field ต่างชื่อ
- Verification ล่าสุดหลัง hardening Answer Adapter: Backend Unit Tests `453 passed`, API Build `0 warning / 0 error` และ `git diff --check` ผ่าน
- เพิ่ม `POST /api/v1/crm/connection/test` สำหรับ Probe แบบ read-only ด้วย Credential ของ User ปัจจุบัน และเพิ่มปุ่มทดสอบการเชื่อมต่อบนหน้า CRM โดยไม่ส่ง Password/Token ไป Browser
- ตรวจสอบหลังเพิ่ม Connection Probe: Backend Unit Tests `453 passed`, API Build `0 warning / 0 error`, Frontend Build/Lint ผ่าน และ `git diff --check` ผ่าน
- ตรวจทาน API Specification ให้ระบุ `CRM_INVALID_QUERY` และ Error mapping ของ Connection Probe ครบถ้วน
- ปรับ Answer history ให้ดึงแบบ DataTables pagination สูงสุด 1,000 รายการ พร้อม de-duplicate ด้วย `answerNo` และหยุดเมื่อ CRM ส่งข้อมูลซ้ำ/หมดหน้า
- Verification หลังปรับ Answer pagination: Backend Unit Tests `453 passed`, API Build `0 warning / 0 error` และ `git diff --check` ผ่าน
- รัน Frontend test suite เพิ่มเติม: Vitest `5 files passed`, `29 tests passed`
- Visual QA รอบล่าสุด: เปิด Vite local ที่ `http://localhost:5173/` และตรวจหน้า Login บน Desktop แล้ว; layout แสดงผลปกติและไม่มี error ที่หน้า Login แต่ยังไม่ปิด Visual/Integration Gate ของ CRM เพราะไม่มี authenticated QA Hub session สำหรับตรวจ List/Detail บนข้อมูลจริง
- Verification เพิ่มเติมก่อน Authenticated Gate: local API `/health` ตอบ 200, anonymous request ไป `/api/v1/crm/connection` และ `/api/v1/crm/tickets` ได้ 401, local web ตอบ 200, Backend Unit Tests `453 passed`, Frontend Vitest `29 passed`, Frontend Build/Lint และ `git diff --check` ผ่าน
- Authenticated Visual/Integration QA: เปิดหน้า `https://promaxx2.qahub.store/#/crm` ได้และตรวจ Desktop layout, KPI, Filter, Empty state และเมนู CRM แล้ว แต่ Production API ตอบ `403` ที่ `/crm/connection`; ยังไม่แก้ข้อมูล Production และต้อง deploy API/Policy `CRM.VIEW` ชุดล่าสุดก่อนทดสอบ List/Detail/Connection Probe กับ CRM จริง
- ตรวจสอบสาเหตุ 403 เพิ่มเติม: `DatabaseInitializer` เติม `CRM.VIEW` ให้ role เดิมที่มีอยู่ และ `TokenService` สร้าง permission claims ตอน login; จึงต้อง deploy API/seed ชุดล่าสุดและให้ผู้ใช้ login ใหม่ก่อนยืนยัน Production gate โดยยังไม่แตะข้อมูล Production และไม่ข้ามสิทธิ์ `CRM.VIEW`
- ปรับ `CrmView` ให้สอดคล้องกับ Frontend: `SYS_ADMIN` หรือ `CRM.VIEW` และเพิ่ม authorization handler ที่ตรวจ Role/Permission ปัจจุบันจากฐานข้อมูลด้วย user id ใน JWT เพื่อไม่ให้ token เก่าค้างสิทธิ์ CRM หลัง seed permission
- Restart API Production profile ที่ port `5038` และตรวจหน้า `https://promaxx2.qahub.store/#/crm` อีกครั้ง: 403 หาย, Connection เป็น `ตั้งค่าแล้ว`, CRM Username เป็น `6101`, List response สำเร็จและแสดงเวลา fetch ล่าสุด
- ทดสอบ Connection Probe แบบ read-only จากหน้า Production: CRM `SysSrviceType` ตอบ `200`; ไม่ส่ง Password/CRM Token ไป Browser และยังไม่แก้ข้อมูล CRM/QA Hub
- สถานะ Phase 2: Implementation และ Production access gate ผ่านแล้ว; เหลือการเปิด Detail จาก Ticket จริงและยืนยัน User Scope อย่างน้อย 2 บัญชีเมื่อมี sample ticket ที่อยู่ในขอบเขตของแต่ละ User
### 2026-10-03 - Phase 4 started: Controlled CRM Update

- Added `CRM.EDIT` permission and `CrmEdit` policy; seeded it for `SYS_ADMIN`, `QA_LEAD`, `QA_TESTER`, and `DEVELOPER` without changing existing CRM credentials.
- Added controlled `GET /api/v1/crm/assignees` and `PATCH /api/v1/crm/tickets/{jobNo}` endpoints. Updates require an existing accessible Defect link, CRM ownership re-check, allowed status values, and optimistic expected Status/Assignee values.
- CRM full-form write-back is performed only after validation; successful updates refresh `CrmLastKnownStatus`, `CrmLastKnownAssignto`, `CrmLastSyncedAt`, and `CrmTicketUpdated` activity.
- Added a responsive CRM edit modal with loading/error states and conflict-safe save behavior. Frontend build/lint passed; backend build passed with `0 warnings / 0 errors`; unit tests passed `460/460`; `git diff --check` passed.
- Production write-back verification is intentionally not executed in this increment; no CRM Ticket was changed by the local implementation work.
### 2026-10-03 - Phase 4 hardening

- Added backend allow-list validation for `assignToStaffCode` against the CRM/BlueID directory; direct API callers can no longer bypass the UI's Assignee options.
- Re-ran API Production startup and anonymous endpoint checks after the hardening change: `/health` returned `200`, `GET /crm/assignees` returned `401`, and `PATCH /crm/tickets/{jobNo}` returned `401` without authentication.
- Backend build remains `0 warnings / 0 errors`; Unit Tests remain `461/461`; `git diff --check` passes.
### 2026-10-03 - Phase 4 UI guard refinement

- The CRM edit action is now rendered only after Ticket detail and an actual QA Hub Defect link are both confirmed; unlinked Tickets expose only the Link Defect action.
- Frontend build/lint and `git diff --check` passed after the refinement.
### 2026-10-03 - Phase 4 update-field edge case

- Changed the CRM edit modal to start both fields as `ไม่เปลี่ยนค่า`; the client now sends only the Status or Assignee that the user explicitly selects.
- This prevents an unsupported existing CRM Status/Assignee from blocking an otherwise valid single-field update. Frontend build/lint and `git diff --check` passed.
### 2026-10-03 - Phase 4 conflict UX refinement

- When the controlled CRM update returns `CRM_CONFLICT`, the UI now refreshes the Ticket Detail automatically without retrying the write-back; the user must explicitly choose new values before saving again.
- Frontend build/lint and `git diff --check` passed.

### 2026-10-03 - Phase 5 started: Create QA Hub Defect from CRM Ticket

- Added `POST /api/v1/crm/tickets/{jobNo}/create-defect` with CRM ownership re-check, `DEFECT.EDIT`, Project Access, active Project validation, duplicate Ticket-link protection, CRM snapshot capture, and `CreatedFromCrm` activity logging.
- Added a responsive CRM modal that pre-fills Title and Description from CRM and requires the user to confirm the Project context, Severity, and final content before creating a QA Hub Defect.
- This flow writes only to QA Hub and does not create or update a CRM Ticket.

### 2026-10-03 - CRM visual refresh

### 2026-10-03 - CRM ticket table readability patch

- Desktop table columns were rebalanced so Subject, Service/Product, Assignee, contact date, and due date remain inside the table card.
- List dates now use separate date/time lines; long Subject and Service/Product content is bounded with wrapping or ellipsis rules.
- Mobile keeps the existing `table-cards` card layout and the change does not add page-level horizontal scrolling.

### 2026-10-03 - CRM Board card wrapping patch

- Board cards now keep Job No., Subject, Service/Product, and Assignee content inside each column with `min-width: 0`, wrapping, and bounded line rules.
- The change preserves the existing responsive one-column Board layout on Mobile.

### 2026-10-03 - CRM Board card hierarchy refinement

- Board cards now use a stable top row for Job No. and Status, followed by Subject and Service/Assignee metadata.
- Status badges remain readable and Job No. uses the available width before falling back to ellipsis.

- ปรับหน้าหลัก CRM ให้มี visual hierarchy ใหม่: Hero connection card, KPI accent ตามสถานะ, Filter/List surface, Board column accent และ Detail Modal ที่อ่านง่ายขึ้น
- เพิ่ม hover/focus treatment สำหรับ Ticket table และ Board card พร้อม responsive overrides สำหรับ Mobile โดยไม่เปลี่ยน API หรือสิทธิ์เดิม
- Frontend Build, Lint, Vitest `29/29` และ `git diff --check` ผ่าน; ตรวจภาพ Desktop จาก local/connected browser แล้ว ไม่พบ page-level horizontal scroll ใน viewport ที่ตรวจได้
