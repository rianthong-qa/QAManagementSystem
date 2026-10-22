# แผนระบบ AI Chat + คลังความรู้ (Knowledge Hub)

> สถานะ: **Phase 0–2 เสร็จในโค้ด (2026-10-21)** — ค้นหาความรู้ + แชทผู้ช่วย AI (RAG) พร้อมอ้างอิง; **ยังไม่ได้ทดสอบกับ LLM จริง เพราะ API key ของ opencode ที่ตั้งค่าไว้หมดอายุ** (ดู Progress Log); Phase 3–6 ยังไม่เริ่ม
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
| 2B | **MCP Server ให้ Claude Code / Claude Desktop ค้นคลังความรู้** — Personal Access Token, endpoint `/mcp`, เครื่องมืออ่านอย่างเดียว 5 ตัว, หน้า "เชื่อมต่อ Claude" (ดูหัวข้อ "Phase 2B" ด้านล่าง) | ทีมถามความรู้ QA ได้จาก Claude ที่ใช้อยู่ |
| 6 (ต่อยอด) | เตือน Defect คล้ายกันตอนสร้าง Defect, เพิ่ม Root Cause/Resolution ตาม Phase 1 ของ `DEFECT_KNOWLEDGE_PLAN.md` (คุณภาพความรู้จาก Defect ขึ้นกับฟิลด์นี้มาก), source Requirement/Test Case | ความรู้แม่นขึ้น |

## Phase 2B — MCP Server สำหรับ Claude (วางแผน + ทำแล้ว 2026-10-21 — ดู Progress Log)

### เป้าหมาย

ให้ทีม QA ถามคลังความรู้จาก **Claude Code / Claude Desktop** ที่ใช้อยู่ได้โดยตรง เช่น "เคยมี Defect เรื่องพิมพ์ใบกำกับภาษีไหม แก้ยังไง" หรือใช้ประกอบงานอื่นใน Claude เช่น เขียน Test Case จาก Defect เดิม — QA Hub เป็น **remote MCP server (Streamable HTTP)** ที่เปิดเครื่องมือแบบอ่านอย่างเดียว ใช้ `KnowledgeSearchService` และกติกาสิทธิ์เดิมทั้งหมด (เห็นเฉพาะ Project ที่เป็นสมาชิก)

### ข้อเท็จจริงจากเอกสารทางการ (ตรวจ 2026-10-21)

- **Claude Code** — `claude mcp add --transport http qa-hub https://api-promaxx2.qahub.store/mcp --header "Authorization: Bearer <token>"` หรือ `.mcp.json` ที่ใช้ `"headers": { "Authorization": "Bearer ${QA_MCP_TOKEN}" }` (รองรับ `${VAR}` / `${VAR:-default}`; ห้ามใช้ชื่อตัวแปร `ANTHROPIC_*`); entry ต้องมี `"type": "http"`; ถ้าตั้ง `Authorization` เองแล้ว server ตอบ 401/403 Claude Code **ไม่**เริ่ม OAuth discovery — https://code.claude.com/docs/en/mcp.md
- **Claude Desktop / claude.ai** — Customize → Connectors → Add custom connector → URL + **Request headers** (Bearer token แบบคงที่ เลือก "No sign in" ได้ ไม่บังคับ OAuth); connector ถูกเรียกจาก cloud ของ Anthropic จึงต้องเป็น URL สาธารณะ (ใช้ `api-promaxx2.qahub.store` ผ่าน Cloudflare ได้); แผน Pro/Max เพิ่มเองรายคน แต่ **Team/Enterprise ให้ Owner เพิ่ม connector และใช้ header ชุดเดียวร่วมกันทั้งองค์กร → token รายคนใช้ไม่ได้** (ต้อง OAuth — ไม่อยู่ในขอบเขตรอบนี้); ตัวเลือก Request headers อาจยังไม่เปิดให้ทุกบัญชี — https://support.claude.com/en/articles/11175166-get-started-with-custom-connectors-using-remote-mcp
- **ขนาดผลลัพธ์** — Claude Code เตือนเมื่อผลเครื่องมือเกิน 10,000 token (ค่าเริ่มต้นสูงสุด 25,000) → ออกแบบผลให้กระชับ ≤ ~8,000 ตัวอักษร; คำอธิบายเครื่องมือถูกตัดที่ 4,096 ตัวอักษร (ใส่คำสำคัญไว้ต้น)
- **SDK** — `ModelContextProtocol.AspNetCore` (ทางการ; NuGet แสดง 2.1.0 — ต้องยืนยันเวอร์ชันและ API `AddMcpServer().WithHttpTransport()` / `MapMcp` ตอนลงมือ และเลือกเวอร์ชันที่ออกมาแล้วอย่างน้อย 2 สัปดาห์)

### การยืนยันตัวตน: Personal Access Token (PAT)

JWT ปัจจุบันอายุ 24 ชม. และเก็บใน browser — ไม่เหมาะให้ Claude ใช้ระยะยาว จึงเพิ่ม token ส่วนตัวที่ผู้ใช้สร้าง/ยกเลิกเองได้

| ตาราง `PersonalAccessTokens` | คำอธิบาย |
|---|---|
| `PersonalAccessTokenId`, `UserId`, `Name` (เช่น "Claude Code — โน้ตบุ๊ก") | |
| `TokenPrefix` (8 ตัวแรก แสดงในรายการ), `TokenHash` (SHA-256, unique) | **ไม่เก็บ token จริง** — แสดงครั้งเดียวตอนสร้าง รูปแบบ `qak_<32 ไบต์ base64url>` |
| `Scope` = `knowledge:read` | จำกัดใช้กับ `/mcp` เท่านั้น — ใช้เรียก API อื่นไม่ได้ |
| `CreatedAt`, `ExpiresAt` (ค่าเริ่มต้น 90 วัน, สูงสุด 365), `LastUsedAt`, `LastUsedIp`, `RevokedAt` | |

- Authentication scheme `PersonalAccessToken` ใช้เฉพาะ endpoint `/mcp`: hash แล้วค้น → ตรวจหมดอายุ/ยกเลิก/ผู้ใช้ยัง Active → สร้าง `ClaimsPrincipal` จากสิทธิ์ปัจจุบันในฐานข้อมูล (role/permission เปลี่ยน = มีผลทันที, cache 1 นาที) → ต้องผ่าน `KnowledgeView`
- ไม่มี/ผิด/หมดอายุ → `401` + `WWW-Authenticate: Bearer` แบบไม่มี resource metadata และไม่มี `/.well-known/oauth-protected-resource` (กัน Claude พยายามทำ OAuth)
- อัปเดต `LastUsedAt` ไม่เกินนาทีละครั้ง; rate limit 60 คำขอ/นาที/token; สร้าง token ได้ไม่เกิน 10 ใบที่ใช้งานอยู่ต่อคน

### เครื่องมือ MCP (อ่านอย่างเดียวทั้งหมด)

| Tool | Input | ผลลัพธ์ (ข้อความกระชับ + ลิงก์เปิดในเว็บ) |
|---|---|---|
| `search_knowledge` | `query`, `project_code?`, `sources?` (Defect/Article), `limit` ≤ 10 | รายการ: ประเภท · รหัส/ชื่อ · Project · snippet · URL — ใช้ `KnowledgeSearchService.SearchAsync` |
| `get_defect` | `defect_code` หรือ `defect_id` | รายละเอียดเต็ม (สถานะ, ความรุนแรง, โมดูล, Release/Build, รายละเอียด, ขั้นตอน, ผลที่คาดหวัง/ผลจริง, Test Case ที่ลิงก์, คอมเมนต์ล่าสุด 10) **ผ่าน `SensitiveDataMasker`** — ไม่มีสิทธิ์ Project = แจ้งว่าไม่พบ |
| `get_article` | `article_id` | บทความ (เฉพาะ Published) |
| `find_similar_defects` | `description`, `project_code?`, `limit` ≤ 10 | Defect ที่คล้ายจาก Full-Text (Phase 5 เปลี่ยนเป็น embedding) — ใช้ตรวจ Defect ซ้ำก่อนแจ้ง |
| `list_projects` | — | Project ที่ผู้ใช้เป็นสมาชิก (รหัส + ชื่อ) ให้ Claude ใช้กรอง |

- URL ในผลลัพธ์ใช้ `PublicWebBaseUrl` เช่น `https://promaxx2.qahub.store/#/defects?defectId=…` (หน้า Defect เปิดรายละเอียดจากลิงก์นี้ได้แล้วใน Phase 1)
- **ไม่เปิดข้อมูล CRM ผ่าน MCP** เลยในรอบนี้ (Claude เป็น AI ภายนอกเสมอ) — Phase 4 ค่อยพิจารณาตามสวิตช์ `AllowCrmDataToExternalAi`
- เคารพ `ExcludeFromAi` ของเอกสาร; ผลแต่ละเครื่องมือตัดที่ ~8,000 ตัวอักษร
- `server instructions` สั้น ๆ: "คลังความรู้ QA ของ ProMaxx2 — ค้นด้วย search_knowledge ก่อน แล้วดูรายละเอียดด้วย get_defect/get_article; อ้างอิงรหัส Defect และลิงก์ทุกครั้ง"

### การควบคุม (Governance)

- **สวิตช์กลาง** ใน Setting Center → AI Configuration → คลังความรู้: "เปิดให้เชื่อมต่อจาก Claude (MCP)" — ค่าเริ่มต้น **ปิด**; ปิดแล้ว `/mcp` ตอบ 403 ทุกคำขอและสร้าง token ใหม่ไม่ได้
- Audit Log ทุกการเรียกเครื่องมือ (`KnowledgeMcp` / ชื่อ tool / สรุป input เช่น คำค้น / จำนวนผล / token prefix) + การสร้าง/ยกเลิก token
- SYS_ADMIN ดูรายการ token ทั้งหมดและยกเลิกแทนได้ (กรณีพนักงานลาออก/token หลุด) — ผู้ใช้ที่ถูกปิดบัญชี token ใช้ไม่ได้ทันที
- ต้องมีผู้อนุมัตินโยบายข้อมูล: ข้อมูล Defect (mask แล้ว) จะถูกส่งเข้า Claude ของผู้ใช้แต่ละคน

### หน้าเว็บ: แท็บ "เชื่อมต่อ Claude" ในหน้าคลังความรู้

- รายการ token ของฉัน (ชื่อ, prefix, สร้างเมื่อ, หมดอายุ, ใช้ล่าสุด, ปุ่มยกเลิก) + ปุ่ม "สร้าง Token"
- สร้างแล้วแสดง token **ครั้งเดียว** (`ModalShell` `backdropDismiss={false}`, `CopyButton`) พร้อมวิธีตั้งค่าแบบคัดลอกได้: คำสั่ง `claude mcp add …`, ตัวอย่าง `.mcp.json` ที่ใช้ `${QA_MCP_TOKEN}` และขั้นตอน Claude Desktop (Add custom connector → URL → Request headers)
- คำเตือน: อย่าใส่ token ในไฟล์ที่ commit, แผน Team/Enterprise ของ claude.ai ใช้ token รายคนไม่ได้
- ต้องอ่านและทำตาม `UI_DESIGN_SYSTEM.md` + อัปเดต Change Log

### Verification

- Unit tests: สร้าง/hash/ตรวจ token (หมดอายุ, ยกเลิก, ผู้ใช้ไม่ Active, scope), auth handler ตอบ 401 ไม่มี OAuth metadata, สวิตช์ปิด = 403, แต่ละ tool เคารพสิทธิ์ Project / ExcludeFromAi / ไม่คืน CRM, masking ใน `get_defect`, ขนาดผลไม่เกินกำหนด, Audit Log
- ทดสอบโปรโตคอลด้วย MCP Inspector (`tools/list`, `tools/call`) กับ API local
- ทดสอบจริง: `claude mcp add` ชี้ `https://api-promaxx2.qahub.store/mcp` ผ่าน Cloudflare → `/mcp` แสดงเครื่องมือ 5 ตัว → ถามเรื่อง Defect ที่รู้คำตอบ ได้รหัส + ลิงก์ถูกต้อง; ผู้ใช้ที่ไม่อยู่ Project ไม่เห็นข้อมูล; ยกเลิก token แล้วเรียกไม่ได้ทันที
- Claude Desktop: Add custom connector + Request headers (ถ้าบัญชีมีตัวเลือกนี้)

### ขนาดงาน

≈ 2–3 วัน: PAT (ตาราง + migration + auth handler + API + tests) ~1 วัน, MCP endpoint + 5 tools + tests ~1 วัน, หน้า "เชื่อมต่อ Claude" + ตรวจ Desktop/Mobile + เอกสาร ~0.5–1 วัน

### เรื่องที่ต้องตัดสินใจ

1. ~~ใครสร้าง token ได้~~ → **ตัดสินแล้ว:** สิทธิ์ใหม่ `KNOWLEDGE.MCP` ค่าเริ่มต้น SYS_ADMIN, QA_LEAD, QA_TESTER
2. ~~อายุ token~~ → **ตัดสินแล้ว:** เริ่มต้น 90 วัน สูงสุด 365 วัน
3. ผู้อนุมัตินโยบายข้อมูลก่อนเปิดสวิตช์ MCP
4. แผน Claude ที่ทีมใช้ → **ทีมใช้ Team**: รอบนี้ทำ token รายคน (ใช้กับ Claude Code ได้ทันที เพราะตั้งค่าในเครื่องแต่ละคน); **รอบถัดไป: OAuth 2.1 (Authorization Code + PKCE) ให้ QA Hub เป็น authorization server** เพื่อให้ Owner เพิ่ม custom connector ครั้งเดียวบน claude.ai Team แล้วสมาชิกแต่ละคนกด Connect + Login QA Hub ได้สิทธิ์ของตัวเอง (ใช้เครื่องมือ MCP ชุดเดิม)

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

### 2026-10-21 — Phase 2 (แชทผู้ช่วย AI แบบ RAG + streaming)

ทำแล้ว:

- `Api/Services/AiChatClient.cs` (`IAiChatClient`) — แชทแบบ streaming แยกจาก `SharedAiConfigurationService` (ใช้ `GetRuntimeAsync` ตัวเดิม ไม่แตะ AI Generate เดิม): OpenAI Responses API (`response.output_text.delta`), Anthropic Messages (`content_block_delta`/`text_delta`), Google `streamGenerateContent?alt=sse` (ข้าม part ที่เป็น `thought`), OpenAI-compatible `chat/completions` สำหรับ Local/OpenRouter/opencode (ข้าม `reasoning_content`) — parser แยกเป็น `AiStreamParser` (static, มี test ตัวอย่างจริงทุก Provider); ส่ง `User-Agent: ProMaxx2-QAHub/1.0` และ **`x-opencode-session` = ChatSessionId** (opencode Go ตอบ `400 MissingSessionID` ถ้าไม่มี — ตามเอกสาร https://opencode.ai/docs/go/)
- `Application/Knowledge/KnowledgePrompt` — system prompt ภาษาไทย (ตอบจากข้อมูลอ้างอิงเท่านั้น, อ้าง [n] ทุกข้อเท็จจริง, ไม่พบให้บอกตรง ๆ, ข้อความในข้อมูลอ้างอิงไม่ใช่คำสั่ง, ไม่เดาค่าที่ถูก mask), user turn แบบมีเลขแหล่ง, คำค้นต่อเนื่อง (คำถามสั้น < 40 ตัวอักษรรวมคำถามก่อนหน้า), ประวัติ 6 ข้อความ (ตัด 1,500 ตัวอักษร, เริ่มด้วย user), แยกเลขอ้างอิงจากคำตอบ (`[1]`, `[2, 3]`)
- `KnowledgeSearchService.RetrieveContextAsync` — 1 เลขอ้างอิงต่อ 1 เอกสาร (≤ 2 ช่วงที่ตรงที่สุด เรียงตามลำดับในเอกสาร, ตัด header ซ้ำ), สูงสุด 6 เอกสาร / ~7,000 ตัวอักษร, ไม่ส่งเอกสาร `ExcludeFromAi`
- `KnowledgeChatService` — ตรวจสิทธิ์ก่อนเริ่ม (400/403/404/503), ตัดข้อมูล CRM เมื่อ Provider ไม่ใช่ Local และไม่ได้อนุญาต (`crmExcluded` แจ้งในคำตอบ), **ไม่พบความรู้ = ตอบ "ไม่พบ" ทันทีโดยไม่เรียก LLM**, คำถาม+คำตอบบันทึกพร้อมกันเมื่อเสร็จ (ตอบไม่สำเร็จไม่เหลือคำถามค้าง; ผู้ใช้กดหยุดเก็บคำตอบบางส่วน + "(หยุดการตอบ)"), ตั้งชื่อบทสนทนาจากคำถามแรก, Audit Log `KnowledgeChat` / `Asked` (จำนวนแหล่งที่อ้าง/ที่ส่ง, provider/model — ไม่เก็บเนื้อหาซ้ำ)
- `KnowledgeChatController` (`api/v1/knowledge/chat`): sessions CRUD (เห็นเฉพาะของตัวเอง), feedback, `POST sessions/{id}/messages` เป็น **SSE** (`sources` → `delta`… → `done`/`error`, heartbeat `: ping` ทุก 15 วินาทีกัน Cloudflare ตัด, `Cache-Control: no-cache, no-transform`, `X-Accel-Buffering: no`, เปิด stream เมื่อมี event แรก — error ก่อนหน้านั้นตอบเป็น HTTP status), `?stream=false` ตอบ JSON ครั้งเดียว; rate limit `knowledge-chat` 20 คำถาม/นาที/ผู้ใช้ (หลัง authentication); JSON ไม่ escape อักษรไทย
- หน้าเว็บ: แท็บแรก "ถามผู้ช่วย AI" (`pages/KnowledgeChat.tsx` + `KnowledgeChat.css`) — รายการบทสนทนา (จำบทสนทนาล่าสุดใน `qa.knowledge.chatSession`), คำตอบแสดงทีละส่วนผ่าน `fetch` + `ReadableStream`, Markdown แบบย่อที่ไม่ใช้ innerHTML, เลขอ้างอิงกดได้, รายการแหล่งอ้างอิง/แหล่งที่ค้นพบแต่ไม่ได้อ้าง, หยุด/ลองใหม่/คัดลอก/👍👎, ตัวกรองแหล่งและ Project; stream ล้มก่อนได้ข้อมูล → ขอแบบ `?stream=false` อัตโนมัติ

ผลตรวจ:

- Unit tests `KnowledgeChatTests` 19 ข้อ (parser 4 Provider, prompt/อ้างอิง/ประวัติ, ดึงบริบท, ถาม-ตอบครบ, ไม่พบไม่เรียก AI, Provider ล้มไม่บันทึก, หยุดเก็บบางส่วน, ตัด CRM, ตรวจก่อน stream, เจ้าของบทสนทนา/feedback) — ทั้งชุด 690 ผ่าน
- `AiChatClient` ผ่าน HTTP จริงกับ server จำลองแบบ OpenAI-compatible (HttpListener, SSE ส่งช้า + แบ่งบรรทัด/ตัวอักษรไทย UTF-8 ข้าม packet + keep-alive + reasoning + ข้อมูลหลัง `[DONE]`): ได้ข้อความทีละส่วนจริง (415 → 881 ms), ต่อตัวอักษรไทยถูก, ส่ง `x-opencode-session` และ User-Agent ถูก
- เรียก Provider จริงที่ตั้งค่า (opencode `mimo-v2.5`) ด้วยคำถามกลางที่ไม่มีข้อมูลองค์กร: ครั้งแรกได้ `400 MissingSessionID` → แก้โดยส่ง `x-opencode-session` แล้ว → ได้ **`401 api_key_expired`** — คีย์ใน Setting Center หมดอายุ (กระทบ AI Generate ทุกหน้าด้วย)
- UI ด้วย headless Chromium (API จำลอง SSE) ที่ 1440 / 1024 / 768 / 390px: หน้าต้อนรับ, ถาม → คำตอบ (เลขอ้างอิง ≥ 4 ปุ่ม, แหล่งที่อ้าง 2, Markdown list/ตัวหนา/หัวข้อ/code, หมายเหตุ CRM), เปิดบทสนทนาเดิม (feedback ค้างสถานะ), แผงบทสนทนาบน ≤ 900px — ไม่มี horizontal scroll, ตัวอักษร ≥ 11px, ปุ่มบน Mobile ≥ 40px; แก้ระหว่างตรวจ: ไอคอนคัดลอกซ้ำ, แท็บ 4 อันบน 390px ทับกัน (→ 2×2), รายการบทสนทนาซ้ำเมื่อโหลดรายการเสร็จหลังสร้างบทสนทนาใหม่

ยังไม่ได้ทดสอบ / ต้องทำ:

- [ ] **ต่ออายุ/เปลี่ยน API key ใน Setting Center** แล้วทดสอบถาม-ตอบจริงผ่านหน้าเว็บ (คุณภาพคำตอบภาษาไทย, การอ้างอิง [n] ถูกแหล่ง)
- [ ] SSE ผ่าน Cloudflare Tunnel จริง (ทดสอบไม่ได้โดยไม่มีบัญชีผู้ใช้ — มี heartbeat และ fallback `?stream=false` รองรับแล้ว)
- [ ] ทบทวนการใช้ opencode Go กับแชทนี้: เอกสาร opencode ระบุว่า Go ออกแบบสำหรับ coding agent ("Send typical coding agent traffic") — แชทความรู้ QA ไม่ใช่ traffic แบบนั้น ควรพิจารณา Provider อื่น (AI Local / OpenAI / Anthropic) สำหรับคลังความรู้

### 2026-10-21 — Phase 2B (MCP Server ให้ Claude Code / Claude Desktop)

ทำแล้ว:

- **Personal Access Token** — `Domain/Identity/PersonalAccessToken` (`qak_` + 256 บิตสุ่ม, เก็บ SHA-256 + prefix 12 ตัว, scope `knowledge:read`, อายุ 1–365 วัน ค่าเริ่มต้น 90, ใช้งานพร้อมกันได้ 10 ใบ/คน, บันทึกการใช้ไม่เกินนาทีละครั้ง), ตาราง `PersonalAccessTokens` (migration `20261021161641_AddPersonalAccessTokens` + คอลัมน์ `AiConfigurations.McpEnabled` + seed สิทธิ์ `KNOWLEDGE.MCP` → SYS_ADMIN, QA_LEAD, QA_TESTER)
- `PersonalAccessTokenService` (สร้าง/ยกเลิก/ตรวจ — สิทธิ์ล่าสุดจากฐานข้อมูล cache 1 นาที, ยกเลิก = ล้าง cache ทันที, ผู้ใช้ถูกปิดบัญชี = ใช้ไม่ได้, Audit Log `PersonalAccessToken`) + authentication scheme `PersonalAccessToken`: JwtBearer `ForwardDefaultSelector` ส่งเฉพาะ path `/mcp` ไปที่ scheme นี้ (token `qak_` ใช้เรียก API อื่นไม่ได้ และ JWT ใช้กับ `/mcp` ไม่ได้), 401 = `WWW-Authenticate: Bearer realm="ProMaxx2 QA Hub"` ไม่มี resource metadata และไม่มี `/.well-known/oauth-protected-resource`
- **MCP server** — `ModelContextProtocol.AspNetCore` **2.2.0** (ออก 2026-08-13; 2.3.0 ใหม่เกินไป), Streamable HTTP แบบ stateless ที่ `/mcp` (`https://api-promaxx2.qahub.store/mcp`), policy `KnowledgeMcp` (PAT scope `knowledge:read` + SYS_ADMIN หรือ `KNOWLEDGE.VIEW` + `KNOWLEDGE.MCP`), rate limit `knowledge-mcp` 60/นาที/token, สวิตช์ `McpEnabled` ปิด = 403 ทุกคำขอ (middleware ก่อน endpoint)
- `KnowledgeMcpTools` 5 เครื่องมือ อ่านอย่างเดียว (`readOnlyHint=true`, `destructiveHint=false`): `list_projects`, `search_knowledge`, `get_defect`, `get_article`, `find_similar_defects` — สิทธิ์ตามสมาชิก Project, `IncludeCrm=false` เสมอ, ข้าม `ExcludeFromAi`, ข้อความผ่าน `SensitiveDataMasker`, ตัดผล ~8,000 ตัวอักษร, ลิงก์เว็บจาก `PublicWebBaseUrl`, Audit Log `KnowledgeMcp` ทุกการเรียก; `KnowledgeIndexService.BuildDefectTextsByIdAsync` แยกให้ใช้ร่วมกับ index
- API จัดการ token `api/v1/knowledge/mcp` (`info`, `tokens` GET/POST/DELETE ของตัวเอง, `tokens/all` SYS_ADMIN)
- หน้าเว็บ: แท็บ "เชื่อมต่อ Claude" (`pages/KnowledgeMcp.tsx` + `KnowledgeMcp.css`) — Token ของฉัน, ขั้นตอน Claude Code (`claude mcp add …` / `.mcp.json` ที่ใช้ `${QA_MCP_TOKEN}`) และ Claude Desktop (custom connector + Request header), ตัวอย่างคำถาม, Token ทั้งระบบ (SYS_ADMIN); หน้าต่างสร้าง Token แสดงครั้งเดียวพร้อมคำสั่งที่เติม Token แล้ว; Setting Center มีสวิตช์ "เปิดให้เชื่อมต่อจาก Claude (MCP)"
- **ช่องโหว่ dependency (พบระหว่างทำ):** `System.Security.Cryptography.Pkcs 9.0.4` ที่มากับ `Microsoft.Data.SqlClient 6.1.1` ติด CVE-2026-84008 / CVE-2026-70162 (ประกาศ 2026-10-13) ทำให้ `dotnet restore` ล้มทั้ง repo (TreatWarningsAsErrors) — อ้างอิง `System.Security.Cryptography.Pkcs 10.0.13` โดยตรงใน Infrastructure

ผลตรวจ:

- Unit tests `KnowledgeMcpTests` 14 ข้อ — ทั้งชุด 704 ผ่าน
- **ทดสอบโปรโตคอล MCP จริง** (server ด้วยคลาสจริง + ฐานข้อมูลในหน่วยความจำ + MCP client ของ SDK ผ่าน HTTP): ไม่มี token / token ผิด → 401 ไม่มี OAuth metadata; ไม่มี `KNOWLEDGE.MCP` → 403; `initialize` ได้ชื่อ server + instructions; `tools/list` 5 เครื่องมือ readOnly; `search_knowledge` ได้รหัส/สถานะ/ลิงก์ + mask; `get_defect` mask เบอร์โทร; Defect ที่ไม่มี → `isError`; ปิดสวิตช์ → 403 ทันที; Audit Log ครบ
- UI ด้วย headless Chromium ที่ 1440 / 1024 / 768 / 390px: แท็บเชื่อมต่อ Claude, หน้าต่างสร้าง/แสดง Token, สวิตช์ใน Setting Center — แก้ระหว่างตรวจ: code block ยาวถูกตัด (→ wrap), วันที่ในตาราง Token ถูกบีบ, หน้าเลื่อนแนวนอนที่ 768px จาก `sr-only` ในหัวตารางและสถานะของ `CopyButton` ที่หลุดขอบ (แก้ที่ `.copy-button` กลางใน `styles.css`)

ยังไม่ได้ทดสอบ / ต้องทำ:

- [ ] ทดสอบจริงด้วย Claude Code ผ่าน Cloudflare: เปิดสวิตช์ใน Setting Center → สร้าง Token → `claude mcp add …` → `/mcp` เห็น 5 เครื่องมือ → ถามเรื่อง Defect
- [ ] Claude Desktop (ถ้าบัญชีมีตัวเลือก Request headers)
- [x] **OAuth สำหรับ claude.ai Team** — ทำแล้ว 2026-10-22 (ดูหัวข้อถัดไป)
- [ ] ขออนุมัตินโยบายข้อมูลก่อนเปิดสวิตช์ (ข้อมูล Defect ที่ mask แล้วถูกส่งเข้า Claude ของผู้ใช้)

ข้อสังเกต: SDK บันทึก `McpException` (เช่น "ไม่พบ Defect") เป็น log ระดับ error — เป็นผลปกติของการค้นไม่เจอ ไม่ใช่ความผิดพลาดของระบบ

### 2026-10-22 — OAuth 2.1 ให้ claude.ai Team / Claude Desktop / Claude Code เชื่อมต่อแบบ Login รายคน

อ้างอิง: https://claude.com/docs/connectors/building/authentication, https://claude.com/docs/connectors/custom/add-unlisted, https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization

ทำแล้ว:

- **API เป็น Authorization Server ของตัวเอง** (`OAuthService` + `OAuthController`, issuer = `Mcp:PublicBaseUrl` ค่าเริ่มต้น `https://api-promaxx2.qahub.store`)
  - `GET /.well-known/oauth-protected-resource` และ `/.well-known/oauth-protected-resource/mcp` (RFC 9728: `resource = https://api-promaxx2.qahub.store/mcp`, `authorization_servers = [issuer]`)
  - `GET /.well-known/oauth-authorization-server` (RFC 8414: S256, `token_endpoint_auth_methods_supported = ["none"]`, `registration_endpoint`, `authorization_response_iss_parameter_supported`) — **ไม่ประกาศ CIMD** (ต้องให้ server ไป fetch URL ภายนอก = เสี่ยง SSRF) Claude จึงใช้ DCR
  - `POST /oauth/register` (DCR, RFC 7591) — **redirect_uri ต้องเป็น `https://claude.ai/api/mcp/auth_callback`** (config `Mcp:OAuth:AllowedRedirectUris`) หรือ loopback `http://localhost|127.0.0.1:<พอร์ตใดก็ได้>` (Claude Code, ปิดได้ `Mcp:OAuth:AllowLoopbackRedirects=false`); ทุก client เป็น public client — ขอวิธียืนยันแบบอื่นจะได้ `none` กลับไป (RFC 7591 §3.2.1)
  - `GET /oauth/authorize` — บังคับ PKCE S256, `resource` ต้องเป็น URL ของ MCP (ไม่ส่ง = ใช้ค่านี้), scope `knowledge:read` (+ `offline_access` ยอมรับ); client/redirect_uri ไม่ถูกต้อง = หน้า error **ไม่ redirect**; ผ่าน = 302 ไปหน้าเว็บ `https://promaxx2.qahub.store/?oauth=req_…`
  - หน้ายืนยันในเว็บ (`pages/OAuthConsentPage.tsx`) — ยังไม่ Login = หน้า Login ก่อน; แสดงชื่อ client, บัญชีที่ Login (+ เปลี่ยนบัญชี), สิ่งที่ Claude ทำได้/ไม่ได้, ปลายทาง; อนุญาตได้เฉพาะเมื่อเปิดสวิตช์ MCP และมี `KNOWLEDGE.VIEW` + `KNOWLEDGE.MCP` → `POST /api/v1/oauth/requests/{id}/approve|deny` (JWT) → กลับไปที่ Claude พร้อม `code`, `state`, `iss`
  - `POST /oauth/token` (form) — `authorization_code` (code ใช้ครั้งเดียวภายใน 2 นาที, ผูก client/redirect_uri/PKCE, verifier ผิด = code ถูกเผาทิ้ง) และ `refresh_token` (**rotation**: refresh/access เดิมใช้ไม่ได้ทันที); `Cache-Control: no-store`; ข้อผิดพลาดตาม RFC 6749 (`invalid_grant`, `invalid_client`, `unsupported_grant_type`, `invalid_target`)
- **Token** เก็บในตาราง `PersonalAccessTokens` (`Kind = OAuth`, `ClientId`, `Resource` = audience, `RefreshTokenHash`, `RefreshExpiresAt`) — access `qao_…` 1 ชม., refresh `qrt_…` 30 วัน; 1 การเชื่อมต่อต่อผู้ใช้ต่อ client (เชื่อมต่อใหม่ = ยกเลิกของเดิม); auth handler ตรวจ audience; ยกเลิกได้จากแท็บ "เชื่อมต่อ Claude" (แสดงเป็น "Login ผ่าน OAuth")
- 401 ของ `/mcp` มี `WWW-Authenticate: Bearer realm="ProMaxx2 QA Hub", resource_metadata="…/.well-known/oauth-protected-resource/mcp", scope="knowledge:read"` — Claude Code ที่ตั้ง header Token เองจะไม่เริ่ม OAuth (ตามเอกสาร) จึงใช้ร่วมกันได้
- Migration `20261021165311_AddMcpOAuth` (`OAuthClients`, `OAuthAuthorizations`, คอลัมน์ใหม่ใน `PersonalAccessTokens`); rate limit `oauth` 120/นาที/IP (ผู้ใช้ claude.ai ทุกคนมาจาก `160.79.104.0/21`); Audit Log `OAuth` (ลงทะเบียน client, อนุญาต/ไม่อนุญาต) + `PersonalAccessToken` (เชื่อมต่อ)
- แท็บ "เชื่อมต่อ Claude": ขั้นตอน Owner (Organization settings → Connectors → Add → Custom → Web → OAuth → Register automatically) และสมาชิก (Customize → Connectors → Connect → Login QA Hub → อนุญาต), Claude Code แบบ Login (`claude mcp add … qa-hub <url>` แล้ว `/mcp` → Authenticate) หรือแบบ Token

ผลตรวจ:

- Unit tests `KnowledgeOAuthTests` 18 ข้อ (PKCE, allowlist/loopback, metadata, DCR, flow ครบ authorize→consent→token→ใช้→refresh→ยกเลิก, code ซ้ำ/verifier ผิด/client ผิด, error ไม่ redirect, ปิดสวิตช์/ไม่มีสิทธิ์/ไม่อนุญาต, เชื่อมต่อใหม่ยกเลิกของเดิม, audience) — ทั้งชุด 722 ผ่าน
- **ทดสอบผ่าน HTTP จริงด้วย OAuth client มาตรฐานของ MCP C# SDK** (DCR + PKCE, server ด้วยคลาสจริง + ฐานข้อมูลในหน่วยความจำ): 401 + `resource_metadata` → PRM → AS metadata → DCR → authorize 302 ไปหน้ายืนยัน (client ส่ง S256 + `resource`) → อนุญาต → code → token → `tools/list` 5 เครื่องมือ + `get_defect` (mask) สำเร็จ; refresh rotation, code ซ้ำ = `invalid_grant`, access เดิมหลัง refresh = 401, token endpoint ต้องเป็น form, client ไม่รู้จัก = หน้า error ไม่ redirect — **พบและแก้:** SDK ลงทะเบียนด้วย auth method อื่นที่ไม่ใช่ `none` (เดิมปฏิเสธ → ตอนนี้ตอบ `none`)
- UI ด้วย headless Chromium ที่ 1440 / 1024 / 768 / 390px: หน้ายืนยัน OAuth + แท็บเชื่อมต่อ Claude ที่มีการเชื่อมต่อแบบ OAuth — แก้ลิงก์ "เปลี่ยนบัญชี" บน Mobile ให้กดง่าย (40px)

ยังไม่ได้ทดสอบ / ต้องทำ:

- [ ] **ทดสอบกับ claude.ai Team จริง**: Owner เพิ่ม custom connector (OAuth → Register automatically) → สมาชิกกด Connect → Login → อนุญาต → ถามเรื่อง Defect
- [ ] Claude Code แบบ Login (`/mcp` → Authenticate) — Claude Code มี client metadata แบบ CIMD (`https://claude.ai/oauth/claude-code-client-metadata`) แต่เราไม่ประกาศ CIMD จึงคาดว่าจะใช้ DCR + loopback (ยังไม่ยืนยัน)
- [ ] ตรวจว่า Cloudflare (WAF/Bot Fight) ไม่บล็อกคำขอจาก `160.79.104.0/21` ไปที่ `/.well-known/*`, `/oauth/*`, `/mcp`
- [x] ลบแถว `OAuthAuthorizations` ที่หมดอายุเกิน 1 วัน — `KnowledgeIndexWorker` ทุกรอบ (2026-10-22); client ที่ไม่เคยถูกใช้ยังไม่ล้าง

### 2026-10-22 — Phase 3 (คลังเอกสาร PDF / DOCX / MD / TXT + นำเข้าเอกสารระบบ)

ทำแล้ว:

- `KnowledgeFile` เพิ่ม `ContentHash` (SHA-256 ของไฟล์), `Origin` (Upload/Import), `ImportPath`, `UpdatedAt` — migration `AddKnowledgeFileContent`
- `DocumentTextExtractor` — ตรวจชนิดจากเนื้อหา (magic bytes: `%PDF-`, zip `PK\x03\x04`, text ไม่มี NUL); **PDF = PdfPig 0.1.16** (Apache-2.0, `ContentOrderTextExtractor`; มีรหัสผ่าน/สแกนเป็นภาพ = แจ้งเหตุผล, ยังไม่รองรับ OCR) + **ซ่อมข้อความไทยจาก PDF** (สระอำที่ถูกสกัดเป็น "พยัญชนะ ␠ า" → "ำ", อักขระ PUA U+F700–F71A → อักษรไทยมาตรฐาน); **DOCX** อ่าน `word/document.xml` เอง (ย่อหน้า/แท็บ/ขึ้นบรรทัด/ตาราง, ปิด DTD) ไม่เพิ่ม dependency; **MD/TXT** UTF-8 (BOM) / UTF-16 LE / TIS-620 (Windows-874)
- `KnowledgeFileService` — อัปโหลดครั้งละ ≤ 10 ไฟล์, ไฟล์ละ ≤ 20 MB, รวม ≤ 50 MB (Cloudflare แผนฟรี ≤ 100 MB/คำขอ); กันไฟล์ซ้ำใน Project เดียวกันด้วย hash; ไฟล์บนดิสก์ `App_Data/KnowledgeFiles/{id}/{สุ่ม}.ext` (ชื่อแสดงคงตัวอักษรไทยรวมสระ/วรรณยุกต์, ตัด path); สกัดข้อความไม่ได้ก็ยังเก็บไฟล์และแจ้งเหตุผล (ไม่เข้าคลัง); ลบ = soft delete + ลบโฟลเดอร์ไฟล์; ดาวน์โหลดตรวจสิทธิ์ Project; Audit Log `KnowledgeFile`
- **นำเข้าเอกสารระบบ** (SYS_ADMIN, `POST /knowledge/files/import-system`): อ่าน `.md`/`.txt` จากโฟลเดอร์ใน config `Knowledge:SystemDocumentFolders` (ค่าเริ่มต้น `Document/ProMaxx2_MD` สัมพัทธ์กับ ContentRoot ของ API) เป็นความรู้กลาง — ผู้ใช้ส่ง path เองไม่ได้; ซ้ำได้: ไม่เปลี่ยน = ข้าม, เนื้อหาเปลี่ยน = อัปเดตตาม `ImportPath`
- index: แหล่ง `File` (`[เอกสาร] ชื่อไฟล์` + Project + รายละเอียด + ข้อความ), `#/knowledge?fileId=`; ค้นหา/แชทมีตัวกรอง "เอกสาร"; MCP: `search_knowledge(source: document)` + เครื่องมือใหม่ **`get_document(documentId, part)`** แบ่งอ่านทีละ ~7,600 ตัวอักษร (mask แล้ว)
- หน้าเว็บแท็บ "เอกสาร" (`pages/KnowledgeDocuments.tsx` + `.css`): ตาราง (ไอคอนชนิด, ชื่อ = ดาวน์โหลด, ชนิด·ขนาด·จำนวนตัวอักษร·เอกสารระบบ, เหตุผลที่สกัดไม่ได้สีแดง, Project, สถานะ, อัปเดต, ดาวน์โหลด/ลบ), หน้าต่างอัปโหลดแบบลากวาง + รายการไฟล์ + ตรวจขีดจำกัดก่อนส่ง + ผลรายไฟล์, ปุ่มนำเข้าเอกสารระบบ (SYS_ADMIN) + สรุปผล

ผลตรวจ:

- Unit tests `KnowledgeFileTests` 14 ข้อ (สกัด TXT/UTF-8 BOM/TIS-620, DOCX + ตาราง + ไฟล์เสีย, PDF จริงที่สร้างด้วย PdfPig + PDF ว่าง, ซ่อมสระอำ/PUA, ชื่อไฟล์, อัปโหลด/ซ้ำ/ไฟล์ปลอม/เกินจำนวน, PDF สแกนไม่เข้าคลัง, นำเข้าซ้ำ/อัปเดต, สิทธิ์ดาวน์โหลด/ลบ, index/ลบแล้ว Excluded, MCP `get_document` แบ่งส่วน/mask/สิทธิ์) — ทั้งชุด 736 ผ่าน; โปรโตคอล MCP ด้วย SDK client: 6 เครื่องมือ
- สกัดไฟล์จริงในเครื่อง (อ่านอย่างเดียว): Markdown 21 ไฟล์ใน `Document/ProMaxx2_MD` ได้ครบ (~190,000 ตัวอักษร); DOCX รายงานภาษาไทยถูกต้องทั้งตาราง; PDF 6 หน้า 260 KB ได้ข้อความครบใน 1.5 วินาที — **พบสระอำแตก 29 จุด ("น าเสนอ") → เพิ่มตัวซ่อม ค้น "นำเสนอ" เจอแล้ว**
- UI ด้วย headless Chromium ที่ 1440 / 1024 / 768 / 390px: แท็บเอกสาร + หน้าต่างอัปโหลด — แก้เนื้อหาหน้าต่างอัปโหลดล้นจอบนมือถือ (grid track ต้อง `minmax(0, 1fr)` เมื่อมีข้อความ nowrap) และปุ่มนำเข้าเต็มแถว

ยังไม่ได้ทดสอบ / ต้องทำ:

- [ ] SYS_ADMIN กด "นำเข้าเอกสารระบบ" ในแท็บเอกสาร (21 ไฟล์) แล้วลองค้น/ถามเรื่องในสเปกโมดูล
- [ ] อัปโหลดผ่าน Cloudflare จริง (ไฟล์ใหญ่ใกล้ 20 MB)
- [ ] OCR สำหรับ PDF สแกน (ยังไม่รองรับ — แจ้งผู้ใช้แทน)
