# แผนระบบ AI Chat + คลังความรู้ (Knowledge Hub)

> สถานะ: **Phase 0–1 เสร็จ (2026-10-21)** — ค้นหาความรู้จาก Defect + บทความใช้งานได้; Phase 2–6 ยังไม่เริ่ม (ดู Progress Log ท้ายไฟล์)
> จัดทำ: 21 ตุลาคม 2026
> ต่อยอดจาก `DEFECT_KNOWLEDGE_PLAN.md` — Phase 2–4 ของแผนนั้นย้ายมารวมในคลังความรู้กลางนี้


## Context

ทีม QA ต้องการผู้ช่วย AI แบบแชทที่ตอบจาก "ความรู้ขององค์กร" จริง พร้อมอ้างอิงแหล่งที่มา เช่น "ปัญหาปิดวันใน Module Stock เคยเกิดเพราะอะไร แก้ยังไง" หรือ "ลูกค้าแจ้งเรื่องพิมพ์ใบกำกับภาษีบ่อยแค่ไหน"
แหล่งข้อมูลมี 3 กลุ่ม: **Defect**, **CRM Ticket** และ **เอกสาร/บทความที่ผู้ใช้เพิ่มเองได้**

**สภาพปัจจุบันจากการสำรวจ**
- **AI:** มีอยู่แล้วที่ `SharedAiConfigurationService` (6 provider รวม Local/Ollama) แต่รองรับแค่ `SendStructuredAsync` แบบ JSON schema ที่รอคำตอบทั้งก้อน — ไม่มี chat, streaming, embeddings, vector หรือ full-text
- **Defect:** มีข้อความครบ (Title/Description/Steps/Expected/Actual) + คอมเมนต์ใน `DefectActivities` ("Comment"/"CrmComment") + ลิงก์ Test Case แต่**ไม่มี Root Cause / Resolution**
- **CRM:** `CrmTickets` เก็บแค่ Subject + metadata (ตั้งใจไม่เก็บข้อความ) ข้อความเต็มดึงสดได้จาก `/Support/HelpDesksJob` (รายละเอียด) และ `/Support/HelpDeskAnswerMain` (คำตอบ) และ ticket **ไม่มี ProjectId**
- **SQL Server 2022:** ไม่มีชนิด `VECTOR` แต่**ติดตั้ง Full-Text Search แล้ว และมี Thai word breaker (LCID 1054)** (ตรวจแล้ว)
- มีแผน `DEFECT_KNOWLEDGE_PLAN.md` (ยังไม่เริ่ม) ที่วางทิศทางไว้ แผนนี้ต่อยอดจากแผนนั้นและขยายเป็นคลังความรู้กลาง

**สิ่งที่ผู้ใช้ตัดสินใจแล้ว**
1. ใช้ Provider ตาม Setting Center (Local หรือ Cloud) และมีสวิตช์แยกว่า "อนุญาตส่งข้อมูล CRM ให้ AI ภายนอก" หรือไม่
2. ค้นหาแบบ Hybrid ใน SQL 2022: Full-Text (ไทย) + embedding เก็บเป็น `varbinary` แล้วคำนวณ cosine ในแอป
3. เอกสารรอบแรกรองรับ PDF, DOCX, MD/TXT และบทความที่เขียนในระบบ
4. ดึงรายละเอียดและคำตอบของ CRM มาเก็บ **หลัง mask ข้อมูลอ่อนไหว**

## สถาปัตยกรรม

```
แหล่งข้อมูล                  Ingestion (rule-based)                  คลังความรู้                     ใช้งาน
Defect + Comments ─┐                                          KnowledgeDocuments (1 ต่อ 1 แหล่ง)
CRM Ticket + คำตอบ ─┼─► KnowledgeIndexWorker ─► Mask ─► Chunk ─► KnowledgeChunks ─┬─ Full-Text (Thai)   ─┐
บทความในระบบ       ─┤   (Stale flag + ContentHash)               (+ Embedding)    └─ Cosine (in-app)   ─┼─► RRF merge ─► LLM ─► คำตอบ + อ้างอิง
ไฟล์ PDF/DOCX/MD   ─┘                                                                                  │     (กรองสิทธิ์ก่อนค้น)
                                                                                ChatSessions/Messages ◄─┘
```

## โครงสร้างข้อมูล (migration ใหม่ ใน `Infrastructure/Persistence/Migrations/`)

Entity ใหม่อยู่ใน `Domain/Knowledge/` (sealed, private setters ตาม convention) และ config อยู่ใน `Infrastructure/Persistence/KnowledgeConfiguration.cs`

| ตาราง | ฟิลด์หลัก |
|---|---|
| `KnowledgeDocuments` | `KnowledgeDocumentId`, `SourceType` (Defect / CrmTicket / Article / File), `SourceKey` (DefectId / JobNo / ArticleId / FileId — unique คู่กับ SourceType), `ProjectId?` (null = ความรู้กลาง), `Scope` (Project / Crm / Global), `Title`, `SourceUrl` (ลิงก์กลับ เช่น `#/defects?id=`), `ContentHash`, `State` (Stale / Indexed / Failed / Excluded), `ExcludeFromAi`, `ContainsCustomerData`, `IndexedAt`, `Error` |
| `KnowledgeChunks` | `KnowledgeChunkId` (int identity — จำเป็นต่อ full-text key), `KnowledgeDocumentId`, `ChunkNo`, `Text` nvarchar(max) (~800–1200 ตัวอักษร), `Embedding` varbinary(max)?, `EmbeddingModel`, `EmbeddingDim` — **Full-Text Index LANGUAGE 1054** บน `Text` (สร้างด้วย raw SQL `suppressTransaction: true` ใน migration) |
| `KnowledgeArticles` | บทความที่เขียนในระบบ: `Title`, `BodyMarkdown`, `ProjectId?`, `Tags`, `Status` (Draft/Published), audit columns |
| `KnowledgeFiles` | ไฟล์อัปโหลด: ชื่อ, ชนิด, ขนาด, `ProjectId?`, path บนดิสก์ `App_Data/KnowledgeFiles/{id:N}/` (แบบเดียวกับ `DefectImageStorage`), ข้อความที่แยกได้ |
| `CrmTicketContents` | `JobNo` (PK, FK `CrmTickets`), `DescriptionMasked`, `FetchedAt`, `ContentHash` |
| `CrmTicketAnswers` | `JobNo`, `AnswerNo` (unique คู่), `AnswerType`, `TextMasked`, `AnsweredAt` |
| `ChatSessions` / `ChatMessages` | session ต่อผู้ใช้ (`UserId`, `Title`, `ProjectId?`), ข้อความ (`Role`, `Content`, `CitationsJson`, `Model`, `TokenUsage`, `Feedback` 👍/👎, `CreatedAt`) |
| `AiConfiguration` (เพิ่มคอลัมน์) | `EmbeddingProvider` (None / OpenAI / Local), `EmbeddingModel` (เช่น `bge-m3`, `text-embedding-3-small`), `EmbeddingBaseUrl`, `AllowCrmDataToExternalAi` (default false) |

> CRM ต้นฉบับ**ไม่เก็บ** — เก็บเฉพาะข้อความที่ mask แล้ว (ลดความเสี่ยงข้อมูลลูกค้า) และเก็บเฉพาะ ticket ที่ปิดงานแล้ว/มีคำตอบ

## Backend (ไฟล์ใหม่/ที่แก้)

**ใช้ของเดิม**
- `Api/Services/SharedAiConfigurationService.cs` → เพิ่ม `ChatAsync`, `StreamChatAsync` (`IAsyncEnumerable<string>`) และ `EmbedAsync` โดยต่อยอด `GetRuntimeAsync`, `CreateClient` และ provider switch เดิม (OpenAI `/responses` stream, Anthropic `/v1/messages` stream, OpenAI-compatible `/chat/completions` stream, Google `:streamGenerateContent`) ส่วน Embeddings รองรับ OpenAI และ Local (`{base}/embeddings`) เท่านั้น ถ้าไม่ตั้งค่า → ค้นแบบ Full-Text อย่างเดียว
- `ProjectAccessFilter` / `ProjectAccessContext.AllowedProjectIds` → ใช้กรองการค้น
- Masking: สร้าง `Application/Knowledge/SensitiveDataMasker.cs` ตาม §5.4 ของ `DEFECT_KNOWLEDGE_PLAN.md` (อีเมล, เบอร์โทร, เลขบัตร 13 หลัก, บัตรเครดิต, token/password/connection string, IP, `[CUSTOMER]`, `[BRANCH]`, `[STAFF]`)
- `CrmApiClient` (`HelpDesksJob`, `HelpDeskAnswerMain`) + บัญชีกลางของ `CrmMirrorWorker` → ใช้ดึงข้อความ CRM

**ใหม่**
- `Application/Knowledge/`: `KnowledgeContracts.cs`, `TextChunker` (ตัดตามย่อหน้า/หัวข้อ + overlap), `HybridRanker` (Reciprocal Rank Fusion) และ builder ต่อแหล่ง (`DefectKnowledgeSource`, `CrmKnowledgeSource`, `ArticleKnowledgeSource`, `FileKnowledgeSource`) ที่สร้าง canonical text แบบคงที่ เช่น `ปัญหา: … | โมดูล: … | ขั้นตอน: … | ผลจริง: … | คอมเมนต์: …`
- `Api/Services/KnowledgeIndexWorker.cs`: BackgroundService ทุก 5 นาที ทำ batch ของ document ที่ Stale → mask → chunk → embed → upsert ปิดได้ด้วย `Knowledge:IndexerEnabled=false` และ CRM ล่มก็ไม่หยุด worker (pattern เดียวกับ `CrmSyncWorker`)
- **การ mark Stale:** hook ใน `DefectsController` / `DefectActivityService` (สร้าง, แก้, คอมเมนต์, เปลี่ยนสถานะ, ลบ → Excluded) + บทความ/ไฟล์ตอนบันทึก + CRM ตอน mirror เห็น `LastChangedAt` เปลี่ยน
- `Api/Services/CrmKnowledgeFetcher.cs`: ต่อจาก `CrmMirrorSyncService` ดึงรายละเอียด+คำตอบของ ticket ที่ปิด/มีคำตอบใหม่ ด้วย rate จำกัด (เช่น 50 ใบ/รอบ) และ backfill ย้อนหลังทีละน้อย
- `Api/Services/DocumentTextExtractor.cs`: PDF ใช้ **UglyToad.PdfPig**, DOCX ใช้ **DocumentFormat.OpenXml**, MD/TXT อ่านตรง จำกัด 10 ไฟล์/ครั้ง, 20 MB/ไฟล์ และแจ้งชัดเมื่อ PDF เป็นภาพสแกน (ไม่มีข้อความ)
- `Api/Services/KnowledgeRetriever.cs`: (1) กรองสิทธิ์ (2) `FREETEXTTABLE`/`CONTAINSTABLE` top 30 (3) ถ้ามี embedding → cosine กับ chunk ที่ผ่านสิทธิ์ (โหลด vector เข้าหน่วยความจำและ cache ตาม `IndexedAt`; หลักหมื่น chunk ยังเร็ว) (4) RRF → top 8 ไม่เกิน ~6k token
- `Api/Services/KnowledgeChatService.cs`: สร้าง prompt ภาษาไทย ("ตอบจากบริบทเท่านั้น ถ้าไม่พบให้บอกว่าไม่พบ อ้างอิง [1][2]") + ประวัติ 6 ข้อความล่าสุด → stream → บันทึก message + citations
- **Controllers**
  - `KnowledgeChatController` `api/v1/knowledge/chat`: `POST /sessions`, `GET /sessions`, `GET /sessions/{id}`, `POST /sessions/{id}/messages` (SSE `text/event-stream`), `POST /messages/{id}/feedback`, `DELETE /sessions/{id}`
  - `KnowledgeLibraryController` `api/v1/knowledge`: articles CRUD, files upload/list/delete, `GET /documents` (สถานะ index), `POST /reindex` (SysAdmin), `GET /search` (ค้นอย่างเดียวไม่ใช้ AI)
- **สิทธิ์ใหม่** (seed migration แบบ `SeedCrmPermissions` + `DatabaseInitializer.PermissionCodes`): `KNOWLEDGE.VIEW` (ใช้ผู้ช่วย AI/ค้นหา), `KNOWLEDGE.EDIT` (เพิ่ม/แก้เอกสาร/บทความ) — ใช้รูปแบบ `<AREA>.VIEW/EDIT` เพื่อให้อยู่ใน permission matrix หน้า User / Role และ policy ใน `Program.cs`
- **กติกาสิทธิ์ตอนค้น (สำคัญ)**
  - ProjectId ต้องอยู่ใน `AllowedProjectIds` ห้ามใช้ช่องโหว่ "allowed ว่าง = เห็นหมด" แบบ `DefectsController.CanAccess`
  - Scope Crm → ผู้ใช้ต้องมี `CRM.VIEW`
  - Scope Global → ทุกคนที่มี `KNOWLEDGE.VIEW`
  - ถ้า provider ไม่ใช่ Local และ `AllowCrmDataToExternalAi=false` → ตัด chunk CRM ออกก่อนส่ง LLM (และแจ้งในคำตอบ)
- **Audit:** บันทึกการถาม (ไม่ใช่ทั้งข้อความ — เก็บใน ChatMessages อยู่แล้ว), การอัปโหลด/ลบเอกสาร, reindex
- **SSE ผ่าน Cloudflare:** ตั้ง `Cache-Control: no-cache`, `X-Accel-Buffering: no`, flush ทุก chunk และ heartbeat ทุก 15 วินาที ถ้า stream ล้มเหลว frontend จะ fallback ไปเรียกแบบไม่ stream (`?stream=false`)

## Frontend (`src/ProMaxx2.QA.Web/src/`)

- หน้าใหม่ `pages/KnowledgePage.tsx` (+ `Knowledge.css`) route `#/knowledge` ชื่อ "ผู้ช่วย AI / คลังความรู้" อยู่ในกลุ่มภาพรวม
- ลงทะเบียนตาม 4 จุดใน `App.tsx`: `Page` union ใน `shared/appShared.tsx`, `nav`, `viewPermission: "KNOWLEDGE.VIEW"` และ lazy import + branch ของหน้า
- **แท็บ "แชท"**
  - รายการ session ซ้าย (Mobile เป็น drawer) และกล่องแชท
  - แสดงคำตอบแบบ stream ด้วย `fetch` + `ReadableStream` (EventSource ส่ง header Authorization ไม่ได้)
  - render markdown แบบปลอดภัย (ไม่ใช้ `dangerouslySetInnerHTML` กับ HTML ดิบ)
  - citation chip [1] คลิกไปยัง Defect / CRM ticket / บทความ / ไฟล์
  - ตัวกรองแหล่ง (Defect / CRM / เอกสาร) และ Project, ปุ่ม 👍/👎, ปุ่มหยุด (AbortController)
- **แท็บ "คลังเอกสาร"**: รายการบทความ+ไฟล์, สร้าง/แก้บทความ (`ModalShell`), อัปโหลดไฟล์, สถานะ index ต่อเอกสาร, สวิตช์ "ไม่ส่งให้ AI"
- **แท็บ "สถานะ Index"** (SysAdmin): จำนวน document/chunk ต่อแหล่ง, Stale/Failed, ปุ่ม reindex
- `MasterSettingsPage.tsx` ส่วน AI Configuration: เพิ่มช่อง Embedding provider/model/base URL และสวิตช์ `AllowCrmDataToExternalAi` พร้อมแก้ `aiReady` ให้ Local ที่ไม่มี key แสดง "พร้อม"
- ทุก UI ต้องอ่านและทำตาม `UI_DESIGN_SYSTEM.md` ทั้งไฟล์ก่อนเริ่ม (tokens, `ModalShell`, `dialogStore`, `table-cards`, ห้าม horizontal scroll, ตัวอักษร ≥ 11px) และคำขอข้อมูลให้ขอครั้งเดียว/ขนาน (latency Cloudflare)

## ลำดับการทำ (แต่ละ Phase ส่งมอบใช้งานได้จริง)

| Phase | งาน | ผลที่ได้ |
|---|---|---|
| 0 | Entities + migration + Full-Text Index ไทย, `SensitiveDataMasker` + tests, `TextChunker`, สิทธิ์ใหม่, ช่อง Embedding ใน Setting Center | ฐานพร้อม |
| 1 | Source: Defect (+คอมเมนต์ + Test Case ที่ลิงก์) และบทความในระบบ, `KnowledgeIndexWorker`, backfill Defect เดิม, `GET /search` | ค้นหาความรู้ได้ (ยังไม่ใช้ AI) |
| 2 | `ChatAsync`/`StreamChatAsync`, `KnowledgeRetriever` (full-text), `KnowledgeChatService`, หน้าแชท | **MVP: แชทถาม-ตอบ Defect + บทความ พร้อมอ้างอิง** |
| 3 | อัปโหลดไฟล์ PDF/DOCX/MD/TXT + extractor + นำเข้าเอกสาร `Document/ProMaxx2_MD` เป็นความรู้กลาง | คลังเอกสารครบ |
| 4 | `CrmTicketContents/Answers`, `CrmKnowledgeFetcher` + mask, source CRM, scope + สวิตช์ external AI | ความรู้จาก CRM |
| 5 | `EmbedAsync` + เก็บ vector + cosine + RRF (hybrid) | ค้นตามความหมายได้ |
| 6 (ต่อยอด) | เตือน Defect คล้ายกันตอนสร้าง Defect, เพิ่ม Root Cause/Resolution ตาม Phase 1 ของ `DEFECT_KNOWLEDGE_PLAN.md` (คุณภาพความรู้จาก Defect ขึ้นกับฟิลด์นี้มาก), source Requirement/Test Case | ความรู้แม่นขึ้น |

## เอกสารที่ต้องอัปเดตในงานเดียวกัน

- สร้าง `Document/03-Architecture-and-Plan/AI_KNOWLEDGE_CHAT_PLAN.md` (เนื้อหาแผนนี้ + สถานะต่อ phase)
- `DEFECT_KNOWLEDGE_PLAN.md`: ระบุว่า Phase 2–4 ย้ายมารวมในคลังความรู้กลาง
- `SYSTEM_OVERVIEW.md`: หน้าใหม่, worker ใหม่ (`KnowledgeIndexWorker`), ตาราง/สิทธิ์ใหม่, แผนที่เอกสาร
- `UI_DESIGN_SYSTEM.md` + Change Log: pattern ใหม่ (chat bubble, streaming, citation chip)
- `API_SPECIFICATION.md`, `SQL_SERVER_SCHEMA.md`

## Verification

- **Unit tests** (`tests/ProMaxx2.QA.UnitTests`)
  - masker (ชุดตัวอย่างภาษาไทย/CRM)
  - chunker
  - RRF ranker
  - สิทธิ์การค้น: ข้าม Project ไม่หลุด, CRM ต้องมี `CRM.VIEW`, external provider ตัด CRM
  - ContentHash ไม่ reindex ซ้ำ
  - extractor PDF/DOCX
  - SSE parser ของแต่ละ provider (ใช้ fixture)
  - ส่วน full-text แยกไว้หลัง interface เพราะ EF InMemory รัน FREETEXT ไม่ได้ → ใช้ `ToQueryString` test ตาม pattern เดิม
- **Build/Check:** `dotnet build src/ProMaxx2.QA.Api`, `dotnet test tests/ProMaxx2.QA.UnitTests`, `npm.cmd run build`, `npm.cmd run lint`, `npx.cmd tsc --noEmit -p tsconfig.app.json`, `git diff --check`
- **Migration:** apply ด้วยมือ (`ApplyMigrations=false`) แล้วตรวจ `sys.fulltext_indexes` และสถานะ population
- **End-to-end:** restart API ด้วย Service Manager → เปิด `#/knowledge` ผ่าน `promaxx2.qahub.store` แล้วทดสอบ
  - ถามเรื่อง Defect ที่รู้คำตอบ → ได้คำตอบที่มี citation ถูกต้อง
  - ผู้ใช้ที่ไม่อยู่ Project → ไม่เห็นข้อมูล Project นั้น
  - ถามเรื่องที่ไม่มีในคลัง → ตอบว่าไม่พบ
  - streaming ผ่าน Cloudflare ไม่ค้าง
  - อัปโหลด PDF/DOCX แล้วถามเนื้อหาได้
  - Desktop + Mobile ไม่มี horizontal scroll
- **ก่อน PR:** `tools\pre-pr-check.ps1` + `/code-review` ตาม `PRE_PR_REVIEW.md`

## ความเสี่ยง / ต้องยืนยันระหว่างทำ

- คุณภาพภาษาไทยของโมเดล Local (แนะนำ `qwen3` สำหรับ chat และ `bge-m3` สำหรับ embedding) — ถ้า GPU ไม่พอ ให้ใช้ Cloud กับข้อมูลที่ไม่ใช่ CRM
- Anthropic และ Google ไม่มี embedding ในตัวระบบนี้ → ต้องตั้ง Embedding provider แยก (OpenAI หรือ Local)
- Rate/ภาระต่อ CRM API ตอนดึงข้อความย้อนหลัง → จำกัดต่อรอบและ backfill ทีละน้อย
- Data Protection key ring ไม่ persist (`AddDataProtection()` ไม่มี `PersistKeysTo*`) — ไม่กระทบแผนนี้โดยตรงแต่ควรบันทึกเป็น technical debt

## Progress Log

### 2026-10-21 — Phase 0 (ฐานข้อมูลและส่วนกลาง)

ทำแล้ว (branch `feat/ai-knowledge-chat`):

- Entities `Domain/Knowledge/`: `KnowledgeDocument`, `KnowledgeChunk` (embedding เก็บ float32 เป็น `varbinary`), `KnowledgeArticle`, `KnowledgeFile`, `CrmTicketContent`, `CrmTicketAnswer`, `ChatSession`, `ChatMessage` + config `Infrastructure/Persistence/KnowledgeConfiguration.cs`
- `AiConfiguration` เพิ่ม `EmbeddingProvider` (None/OpenAI/Local), `EmbeddingModel`, `EmbeddingBaseUrl`, `EncryptedEmbeddingApiKey` (Data Protection purpose แยกจากคีย์ Chat), `EmbeddingApiKeyHint`, `AllowCrmDataToExternalAi` (ค่าเริ่มต้นปิด)
- Migration `20261021140034_AddKnowledgeHub`: 8 ตาราง + คอลัมน์ใหม่ + seed สิทธิ์ `KNOWLEDGE.VIEW` (ทุก Role) / `KNOWLEDGE.EDIT` (SYS_ADMIN, QA_LEAD) + **Full-Text Catalog `KnowledgeCatalog` และ Full-Text Index `KnowledgeChunks([Text] LANGUAGE 1054)`** (`suppressTransaction`)
- `Application/Knowledge/SensitiveDataMasker` (secret/Bearer/JWT, อีเมล, บัตรประชาชน 13 หลัก, บัตรเครดิต, เบอร์โทรไทย, IP ภายใน, คำเฉพาะแหล่ง เช่น `[CUSTOMER]`/`[BRANCH]`/`[STAFF]`), `TextChunker` (ย่อหน้า/หัวข้อ Markdown, ตัดที่ช่องว่าง, overlap), `KnowledgeHash` (SHA-256)
- Policy `KnowledgeView` / `KnowledgeEdit` (SYS_ADMIN ผ่านเสมอ) และ `DatabaseInitializer` default role mapping
- API `PUT /api/v1/master-settings/ai/knowledge` (`AdminUser`) + `AiConfigurationView` มีฟิลด์คลังความรู้
- Setting Center → AI Configuration: บล็อก "คลังความรู้ (AI Chat)" และแก้ป้าย "พร้อม" ของ AI Local / opencode ที่ไม่มีคีย์; หน้า User / Role มีแถว "ผู้ช่วย AI / คลังความรู้" ใน permission matrix
- Unit tests `KnowledgeFoundationTests` 36 ข้อ — ทั้งชุด 651 ผ่าน; `npm.cmd run build` / `lint` / `tsc -p tsconfig.app.json` ผ่าน

ยังไม่ได้ทำ / ต้องทำต่อ:

- [x] Apply migration กับฐานข้อมูลจริงด้วย idempotent script (`dotnet ef migrations script --idempotent`) — `__EFMigrationsHistory` = `AddKnowledgeHub`; Full-Text Index `KnowledgeChunks` อยู่ใน `KnowledgeCatalog`, enabled, CHANGE_TRACKING AUTO; สิทธิ์ `KNOWLEDGE.VIEW` 7 Role / `KNOWLEDGE.EDIT` 2 Role; `sys.dm_fts_parser(..., 1054)` ตัดคำไทยได้ถูกต้อง ("พิมพ์ใบกำกับภาษีไม่ได้หลังปิดวัน" → 7 คำ)
- [x] Restart API (profile production) — `/health` 200 ทั้ง local และ `api-promaxx2.qahub.store`; `PUT /master-settings/ai/knowledge` ไม่มี token ได้ 401 (route มีจริง)
- [x] ตรวจหน้าตั้งค่าด้วย headless Chromium ที่ 1440 / 1024 / 768 / 390px (API จำลองด้วย route interception): ไม่มี horizontal scroll, ตัวอักษร ≥ 11px, OpenAI ไม่มีคีย์ = ปุ่มบันทึก disabled + "ยังขาด", None บันทึกได้, ป้าย AI Local "พร้อม" — พบและแก้: คำแนะนำใต้ช่อง Model เล็ก 9px (→ 12px) และช่องในแถวไม่ตรงกันเมื่อมีคำแนะนำ (→ `align-items: start`)
- [ ] ยังไม่ได้ทดสอบบันทึกจริงผ่าน API ด้วยบัญชีผู้ใช้ (ครอบคลุมด้วย unit test ของ `SaveKnowledgeAsync`) — ให้ผู้ดูแลลองบันทึกที่ Setting Center หนึ่งครั้ง

Technical debt ที่พบ:

- `AddDataProtection()` ไม่ persist key ring (`PersistKeysTo*`) — ถ้า key ring หาย คีย์ AI/Embedding/CRM/Email ที่เข้ารหัสไว้ต้องกรอกใหม่ทั้งหมด
- `SaveAsync` ของ AI หลักบังคับ API key สำหรับ opencode แต่ `GetRuntimeAsync` อนุญาตไม่มีคีย์ (ไม่สอดคล้อง)

### 2026-10-21 — Phase 1 (Defect + บทความเข้าคลัง, ค้นหาแบบไม่ใช้ AI)

ทำแล้ว:

- `Application/Knowledge/KnowledgeSourceText` — ข้อความรูปแบบคงที่ต่อแหล่ง: Defect = `[Defect] รหัส: ชื่อ` + Project / โมดูล (path เต็ม) / Release / Build / ความรุนแรง·สถานะ·วันที่ / CRM Ticket / รายละเอียด / ขั้นตอนการทำซ้ำ / ผลที่คาดหวัง / ผลจริง / Test Case ที่ลิงก์ / คอมเมนต์ล่าสุด 20 รายการ (ActionType `Comment`, `CrmComment` — activity อื่นไม่เข้าคลัง); บทความ = `[บทความ] ชื่อ` + Project/Tag + เนื้อหา; วันที่ใช้ ค.ศ. (InvariantCulture) เพื่อให้ hash ไม่ขึ้นกับ culture ของเครื่อง
- `KnowledgeChunkPlanner` — mask → แบ่ง chunk → ติดบรรทัด header ให้ทุก chunk
- `KnowledgeIndexService` + `KnowledgeIndexWorker` (ทุก 5 นาที, เริ่มหลังเปิดระบบ 1 นาที, ปลุกทันทีเมื่อบันทึกบทความ/สั่ง reindex ผ่าน `KnowledgeIndexTrigger`; config `Knowledge:IndexerEnabled`, `Knowledge:IndexIntervalMinutes`, `Knowledge:IndexBatchSize`)
  - **ตรวจการเปลี่ยนแปลงจากเวลา** (`Defect.UpdatedAt` และ `DefectActivities.CreatedAt` ล่าสุด เทียบ `IndexedAt`) แทนการ hook ทุกจุดที่แก้ Defect; `IndexedAt` = เวลาที่อ่านข้อมูลต้นทาง จึงไม่พลาดการแก้ไขระหว่าง index
  - ContentHash (รวม `IndexVersion = "k1"`) เดิม = ไม่เขียน chunk ใหม่; Defect ถูกลบ/บทความเป็นฉบับร่างหรือถูกลบ → `Excluded` และลบ chunk
- `SqlKnowledgeTextSearch` — `FREETEXTTABLE(..., LANGUAGE 1054)` + กรองสิทธิ์ใน SQL เดียวกัน (Project ผ่าน `OPENJSON`, Global, Crm เมื่อผ่าน policy `CrmView`) + ตัวกรอง Project/แหล่ง; Full-Text ไม่เจอ → ค้นแบบ `LIKE` สำรอง (escape `[ % _`)
- `KnowledgeSearchService` — รวมผลต่อเอกสาร (rank สูงสุด + จำนวนช่วงที่ตรง) + snippet รอบคำค้น; **สิทธิ์ตามสมาชิก Project เท่านั้น** (ไม่มีกรณี "allowed ว่าง = เห็นหมด")
- API `KnowledgeController` (`api/v1/knowledge`, policy `KnowledgeView`): `GET search`, `GET/POST/PUT/DELETE articles` (เขียน = `KnowledgeEdit`, ฉบับร่างเห็นเฉพาะผู้แก้ไขได้, ตรวจสิทธิ์ Project ของบทความ, Audit Log `KnowledgeArticle`), `GET status` / `POST reindex` (SYS_ADMIN, Audit Log `KnowledgeIndex`)
- หน้า `#/knowledge` "ผู้ช่วย AI / คลังความรู้" (`pages/KnowledgePage.tsx` + `Knowledge.css`): แท็บ ค้นหาความรู้ / บทความ / สถานะคลัง (SYS_ADMIN); ผลค้นหา Defect เปิดรายละเอียดในหน้า Defect (`qa.targetDefectId` หรือ `#/defects?defectId=` — `DefectsPage` ดึงด้วย id จึงเปิดได้แม้ไม่อยู่ในตัวกรอง), บทความเปิดเป็นหน้าต่างอ่าน (`#/knowledge?articleId=`)

ผลตรวจ:

- Unit tests `KnowledgeIndexTests` 22 ข้อ (ข้อความ/chunk/snippet, sync-index-hash-stale-exclude, บทความ Draft/Published/scope, สถานะ, รวมผลค้นหา, scope สิทธิ์) — ทั้งชุด 671 ผ่าน
- ฐานข้อมูลจริงหลัง restart API: worker index Defect **2,173 รายการครบในรอบแรก** (5,910 chunk, เฉลี่ย 754 / สูงสุด 999 ตัวอักษร, Failed 0); Full-Text populate ครบ 5,910
- เรียก `KnowledgeSearchService` + `SqlKnowledgeTextSearch` ตัวจริงกับฐานข้อมูลจริง (อ่านอย่างเดียว): "พิมพ์ใบกำกับภาษี" / "บันทึกไม่ได้" ได้ผลตรงเรื่อง 11–16 ms; ไม่มีสิทธิ์ Project = 0 ผล; กรอง PMX2 = ได้ผล, กรอง/เป็นสมาชิกเฉพาะ Project อื่น = 0 ผล; ค้นรหัส Defect ได้ตัวตรงเป็นอันดับแรก; อักขระ `% [ _` ไม่ทำให้ SQL ผิด
- UI ด้วย headless Chromium (API จำลอง) ที่ 1440 / 1024 / 768 / 390px ทุกแท็บ + หน้าต่างอ่าน/เขียนบทความ: ไม่มี horizontal scroll, ตัวอักษร ≥ 11px, ไม่มีข้อความถูกตัด, ปุ่ม/ช่องบน Mobile ≥ 40px (ยกเว้นลิงก์ชื่อเรื่องในข้อความ ซึ่งมีปุ่ม "เปิด"/"ดู" 44/40px คู่กัน)
- ยังไม่ได้ทดสอบด้วยบัญชีผู้ใช้จริงผ่านหน้าเว็บ (สร้างบทความ → ค้นเจอ, กดผลค้นหา → เปิด Defect) — ให้ผู้ใช้ลองหนึ่งรอบ

Technical debt / ข้อสังเกต:

- คำค้นที่ Full-Text ไม่เจอจะสำรองด้วย `LIKE '%…%'` ทั้งตาราง (~0.4 วินาทีที่ 5,910 chunk) — โตตามจำนวน chunk; ถ้าเกิน ~50,000 chunk ให้จำกัด fallback เฉพาะคำค้นสั้น/รหัส หรือใช้ embedding (Phase 5) แทน
- การตรวจการเปลี่ยนแปลงของ Defect โหลดรายการ Defect + เวลา activity ล่าสุดทั้งหมดทุก 5 นาที (2,183 แถว — เบา) ถ้าโตหลักแสนให้เปลี่ยนเป็นตรวจเฉพาะที่เปลี่ยนหลังรอบก่อน
- ชื่อ/รหัส Test Case ที่เปลี่ยนภายหลังไม่ทำให้ Defect ที่ลิงก์ถูก index ใหม่ (จนกว่า Defect จะเปลี่ยนหรือสั่ง reindex)
