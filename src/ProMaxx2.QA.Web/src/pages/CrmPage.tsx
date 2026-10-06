import { useEffect, useMemo, useRef, useState } from "react";
import { ApiError, apiFetch, apiUrl, getJson, isAbortError } from "../api";
import { Badge } from "../components/Badge";
import { ModalShell } from "../components/ModalShell";
import { ImageLightbox } from "../components/ImageLightbox";
import { confirmDialog, notify } from "../components/dialogStore";
import { fmtDateTimeBE } from "../shared/appShared";
import { bangkokMidnightMs, toUtcDate } from "../dateTime";
import "../Crm.css";

type CrmConnectionStatus = {
  isConfigured: boolean;
  isEnabled: boolean;
  username?: string | null;
  updatedAt?: string | null;
};

type CrmTicket = {
  jobNo: string;
  subject?: string | null;
  status?: string | null;
  serviceType?: string | null;
  product?: string | null;
  assignee?: string | null;
  member?: string | null;
  branch?: string | null;
  owner?: string | null;
  developer?: string | null;
  contactDate?: string | null;
  dueDate?: string | null;
  lastReplyAt?: string | null;
};

type CrmListResult = {
  rows: CrmTicket[];
  total: number;
  page: number;
  pageSize: number;
  summary: { total: number; open: number; inProgress: number; closed: number };
  lastFetchedAt: string;
  scopeCounts?: { mine: number; previous: number };
  // jobNo → QA คนล่าสุดจากประวัติ Flow Tracking (ใช้เมื่อ Ticket ถูกส่งกลับ Support แล้ว Assignto จึงไม่ใช่ QA)
  previousQa?: Record<string, string> | null;
};

// mine = Assignto เป็นผู้ใช้ตอนนี้, previous = งานที่อยู่กับคนอื่นแล้วแต่ผู้ใช้ยังเกี่ยวข้อง (เจ้าของเรื่อง/Dev/เคยถือ) และยังไม่ Close/Finish — ตรงกับ CrmTicketScope ฝั่ง API
type CrmTicketScope = "mine" | "previous";

type CrmTicketAnswer = {
  answerNo: string;
  description: string;
  posted: string;
  answerDate?: string | null;
  answerType: string;
  image?: string | null;
};

type CrmTicketDetail = {
  ticket: CrmTicket;
  description?: string | null;
  answers: CrmTicketAnswer[];
  lastFetchedAt: string;
};

type CrmFlowState = {
  jobNo: string;
  status?: string | null;
  support?: string | null;
  qa?: string | null;
  developer?: string | null;
};

type CrmFlowHistoryItem = {
  timestamp: string;
  action: string;
  before?: CrmFlowState | null;
  after: CrmFlowState;
  actorName?: string | null;
};

type CrmLinkedDefect = {
  defectId: string;
  defectCode: string;
  title: string;
  severity: string;
  status: string;
  projectId: string;
  crmTicketId: string;
  crmSyncStatus: string;
};

type LinkableDefect = Pick<CrmLinkedDefect, "defectId" | "defectCode" | "title" | "severity" | "status" | "projectId">;
type DefectListResult = { rows: LinkableDefect[] };
type CrmTicketMutation = { jobNo: string; status: string; assignee: string; lastSyncedAt?: string | null };
type CrmCreatedDefect = CrmLinkedDefect;

// วันที่สำหรับ <input type="date"> และตัวกรอง (yyyy-mm-dd) ต้องเป็นวันตามปฏิทินไทยเสมอ ไม่ขึ้นกับ timezone ของเครื่องผู้ใช้
const dateInput = (value: Date) => new Intl.DateTimeFormat("en-CA", { timeZone: "Asia/Bangkok", year: "numeric", month: "2-digit", day: "2-digit" }).format(value);
// ค่า yyyy-mm-dd จาก <input type="date"> แสดงเป็น วัน/เดือน/ปี พ.ศ. ให้ตรงกับวันที่ในรายการ — รูปแบบของ input เองขึ้นกับ locale ของเบราว์เซอร์
const dayLabel = (value: string) => { const [year, month, day] = value.split("-"); return year && month && day ? `${day}/${month}/${Number(year) + 543}` : "-"; };
// 0 = เฉพาะวันนี้ (From = To = วันนี้)
const RANGE_PRESETS = [0, 7, 15, 30] as const;
const DEFAULT_RANGE_DAYS = 0;
const daysAgo = (days: number) => dateInput(new Date(Date.now() - days * 86_400_000));
function CrmDateField({ label, value, min, max, onChange }: { label: string; value: string; min?: string; max?: string; onChange: (value: string) => void }) {
  return <label className="crm-date-field"><span>{label}</span><span className="crm-date-control"><span className="crm-date-text">{value ? dayLabel(value) : "วว/ดด/ปปปป"}</span><span className="material-symbols-outlined" aria-hidden="true">calendar_month</span><input type="date" value={value} min={min} max={max} aria-label={`${label} (วัน/เดือน/ปี)`} onClick={(event) => { try { event.currentTarget.showPicker(); } catch { /* เบราว์เซอร์ที่ไม่รองรับ showPicker ใช้การพิมพ์/ปุ่มปฏิทินเดิม */ } }} onChange={(event) => { if (event.target.value) onChange(event.target.value); }} /></span></label>;
}
const displayDate = (value?: string | null) => value ? fmtDateTimeBE(value) : "-";
const listDate = (value?: string | null) => {
  const text = displayDate(value);
  const [date, time] = text.split(" ");
  return <span className="crm-list-date"><strong>{date}</strong>{time && <small>{time}</small>}</span>;
};
// อายุงาน = จำนวนวันตามปฏิทินไทยนับจากวันที่ติดต่อ (ติดต่อเมื่อวาน 23:00 น. วันนี้ = 1 วัน ไม่ใช่ "วันนี้")
const ageDays = (value?: string | null) => {
  const start = bangkokMidnightMs(value), today = bangkokMidnightMs(new Date());
  if (start === null || today === null) return null;
  return Math.max(0, Math.round((today - start) / 86_400_000));
};
const ageLabel = (value?: string | null) => {
  const days = ageDays(value);
  return days === null ? "-" : days === 0 ? "วันนี้" : `${days} วัน`;
};
// ระดับอายุงานสำหรับสีป้ายในรายการ: ≥7 วันแดง, ≥3 วันเหลือง, น้อยกว่านั้นเขียว
const ageLevel = (days: number | null) => days === null ? "none" : days >= 7 ? "late" : days >= 3 ? "warn" : "fresh";
const initials = (name?: string | null) => (name ?? "").trim().slice(0, 2).toUpperCase() || "-";
const isCrmEditHistory = (value?: string | null) => /^\s*\[QA Hub\]/i.test(value ?? "");
// ไฟล์แนบในประวัติการติดต่อเก็บที่ Azure Blob สาธารณะของ BlueSea (เปิดได้ไม่ต้อง login) เช่น
// https://seniorsoftbluesea.blob.core.windows.net/helpdesk/helpdeskHD/<guid>.jpg — ค่า `image` จาก CRM อาจเป็น URL เต็ม,
// path ใต้ container หรือชื่อไฟล์อย่างเดียว; รับเฉพาะ host นี้ (ไม่แสดง URL ภายนอกอื่น) และอาจมีหลายไฟล์คั่นด้วย , ; |
const CRM_BLOB_BASE = "https://seniorsoftbluesea.blob.core.windows.net/helpdesk/";
const crmAttachmentUrls = (image?: string | null) => (image ?? "").split(/[,;|]/).map((value) => value.trim()).filter(Boolean).flatMap((value) => {
  if (/^https?:\/\//i.test(value)) return value.toLowerCase().startsWith(CRM_BLOB_BASE) ? [value] : [];
  const path = value.replace(/^\/+/, "").replace(/^helpdesk\//i, "");
  return [`${CRM_BLOB_BASE}${(path.includes("/") ? path : `helpdeskHD/${path}`).split("/").map(encodeURIComponent).join("/")}`];
});
const isImageUrl = (url: string) => /\.(jpe?g|png|gif|webp|bmp)(?:$|[?#])/i.test(url);
const fileNameOf = (url: string) => { const name = url.split(/[?#]/)[0].split("/").pop() || "ไฟล์แนบ"; try { return decodeURIComponent(name); } catch { return name; } };

// สถานะที่เลือกได้ในตัวกรองและตอนอัปเดต Ticket — ชุดเดียวกับ CrmController.EditableStatuses ฝั่ง backend
const CRM_STATUSES = ["Open", "Continue", "Approve", "Develop", "Planning", "Test", "EditErr", "Finish", "Close"] as const;
const CRM_REPLY_MAX = 1000;
// ช่วงวันที่ยาวสุดที่ backend ยอมรับ (CrmApiClient.MaxTicketListRangeDays) — HelpDeskExport ส่งทั้งช่วงมาทีเดียว
const CRM_MAX_RANGE_DAYS = 366;
const shiftDate = (value: string, days: number) => { const [y, m, d] = value.split("-").map(Number); return new Date(Date.UTC(y, m - 1, d + days)).toISOString().slice(0, 10); };
const CRM_STALE_AFTER_MS = 15 * 60 * 1000;
type CrmStaff = { staffCode: string; name: string };
/**
 * แสดงรหัสพนักงานพร้อมชื่อ เช่น "6101 เหรียญทอง" — รับได้ทั้งรหัสล้วน ("6101") และรูปแบบ export ของ CRM
 * ("เหรียญทอง เจือบุญ (6101)"); ชื่อมาจาก BlueID directory (`/crm/staff`) ก่อน แล้วค่อยใช้ชื่อในวงเล็บ
 * ค่าที่ไม่ใช่รหัสพนักงาน (เช่น รหัสสมาชิกลูกค้าที่ไม่มีใน directory) แสดงตามเดิม
 */
// คำนำหน้าชื่อใน BlueID directory (เช่น "นาย สมชาย ใจดี") — ตัดออกก่อนเอาคำแรกเป็นชื่อ ไม่งั้นจะแสดง "6907 นาย"
// รองรับทั้งคำนำหน้าที่มีช่องว่างและรูปแบบจาก CRM ที่ติดกับชื่อ เช่น "นายรัช"
const NAME_TITLE = /^(?:(?:นางสาว|นาง|นาย|คุณ|ดร|ด\.ช\.|ด\.ญ\.)\.?\s*|(?:น\.ส\.|Mrs|Miss|Mr|Ms|Dr)\.?\s*)+/i;
const withoutNameTitle = (value?: string | null) => (value ?? "").trim().replace(NAME_TITLE, "").trim();
const staffLabel = (value: string | null | undefined, names: Record<string, string>): string | null => {
  const text = value?.trim();
  if (!text) return null;
  const withName = text.match(/^(.*?)\s*\((\d+)\)$/);
  const code = /^\d+$/.test(text) ? text : withName?.[2];
  if (!code) return withoutNameTitle(text);
  const firstName = withoutNameTitle(names[code] ?? withName?.[1]).split(/\s+/)[0];
  return firstName ? `${code} ${firstName}` : code;
};
// รหัสพนักงานจากค่า "6101" หรือ "ชื่อ นามสกุล (6101)" — ใช้เทียบว่าเป็นคนเดียวกัน
const staffCode = (value?: string | null) => { const text = value?.trim(); return text ? text.match(/\((\d+)\)$/)?.[1] ?? text : null; };
const sameStaff = (left?: string | null, right?: string | null) => { const code = staffCode(left); return Boolean(code) && code === staffCode(right); };
// Flow ใช้ "ช่องของ CRM" ไม่เดาตำแหน่งงาน (ผู้ใช้ยืนยัน 2026-10-06): คนใน Development อาจเป็น QA และเจ้าของเรื่องอาจไม่ใช่ Support
// เจ้าของเรื่อง (OwnerSubjectId) → Assign To (Assignto = คนที่ถืองานตอนนี้) → Development (sysDevelop)
const crmFlowInfo = (item: CrmTicket | CrmFlowState) => {
  const status = item.status?.trim().toLocaleLowerCase();
  const developer = item.developer?.trim() && item.developer.trim() !== "0" ? item.developer.trim() : null;
  const owner = "owner" in item ? item.owner : (item as CrmFlowState).support;
  const assignee = "assignee" in item ? item.assignee : (item as CrmFlowState).qa;
  const closed = status === "finish" || status === "close" || status === "closed";
  return { owner, assignee, developer, closed, waitingTest: status === "test" };
};
// Test/Testing = รอทดสอบ (ม่วง) แยกจาก Continue และสถานะกำลังดำเนินการอื่น (น้ำเงิน)
const statusTone = (status?: string | null) => status === "Open" ? "yellow" : status === "Close" || status === "Finish" ? "green" : /^test/i.test(status ?? "") ? "purple" : status ? "blue" : "gray";
const boardBucket = (status?: string | null) => {
  const normalized = status?.trim().toLocaleLowerCase();
  if (normalized === "open") return "Open";
  if (normalized === "test") return "Test";
  if (normalized === "close" || normalized === "closed" || normalized === "finish") return "Closed";
  return "Continue";
};
const crmTicketUrl = (jobNo: string) => `https://bluesea.seniorsoft.com/bluesea/BookLicence/MA/Support/JobDetailsHD?JobNo=${encodeURIComponent(jobNo)}&JobType=HD`;
const crmErrorMessage = (reason: unknown, fallback: string) => {
  if (!(reason instanceof ApiError)) return reason instanceof Error ? reason.message : fallback;
  if (reason.status === 403 && !reason.code) return "บัญชี QA Hub ของคุณไม่มีสิทธิ์ทำรายการนี้ (ต้องมีสิทธิ์ CRM.EDIT) กรุณาติดต่อผู้ดูแลระบบ";
  return reason.code === "CRM_NOT_CONFIGURED" ? "ยังไม่มีการตั้งค่าบัญชี CRM สำหรับผู้ใช้นี้ กรุณาตั้งค่าที่ปุ่ม บัญชี CRM ของฉัน ด้านขวาบน"
    : reason.code === "CRM_UNAUTHORIZED" ? "บัญชี CRM ไม่ได้รับอนุญาตให้เชื่อมต่อ กรุณาตรวจสอบ Username/Password แล้วลองใหม่"
      : reason.code === "CRM_RATE_LIMITED" ? "CRM จำกัดจำนวนคำขอชั่วคราว กรุณารอสักครู่แล้วลองใหม่"
        : reason.code === "CRM_TIMEOUT" ? "CRM ตอบกลับช้าเกินกำหนด กรุณาลองใหม่อีกครั้ง"
          : reason.code === "CRM_UNAVAILABLE" ? (reason.message.startsWith("CRM / BlueID") ? reason.message : "CRM ไม่พร้อมใช้งานในขณะนี้ กรุณาลองใหม่ภายหลัง")
            : reason.code === "CRM_RESULT_TOO_LARGE" ? "ข้อมูลจาก CRM มีจำนวนมากเกินไป กรุณาจำกัดช่วงวันที่หรือลองใช้ตัวกรองเพิ่มเติม"
              : reason.code === "CRM_BAD_RESPONSE" ? "CRM ส่งข้อมูลไม่ตรงรูปแบบที่ระบบรองรับ กรุณาลองใหม่หรือติดต่อผู้ดูแลระบบ"
                : reason.code === "CRM_TICKET_CLOSED" ? "Ticket นี้ปิดแล้ว ไม่สามารถแก้ไขข้อมูลได้"
                : reason.code === "CRM_TICKET_NOT_FOUND" ? "ไม่พบ Ticket นี้ในขอบเขตงานของบัญชี CRM ปัจจุบัน"
                  : reason.code === "CRM_INVALID_QUERY" ? "ตัวกรองหรือช่วงวันที่ไม่ถูกต้อง กรุณาตรวจสอบข้อมูลแล้วลองใหม่"
                    : reason.message || fallback;
};
const crmErrorTitle = (code?: string) => code === "CRM_NOT_CONFIGURED" ? "ยังไม่ได้ตั้งค่าบัญชี CRM"
  : code === "CRM_UNAUTHORIZED" ? "บัญชี CRM ไม่ได้รับอนุญาต"
    : code === "CRM_RATE_LIMITED" ? "CRM จำกัดจำนวนคำขอชั่วคราว"
      : code === "CRM_TIMEOUT" ? "CRM ตอบกลับไม่ทันเวลา"
        : code === "CRM_UNAVAILABLE" ? "CRM ไม่พร้อมใช้งาน"
          : code === "CRM_RESULT_TOO_LARGE" ? "ข้อมูลจาก CRM มากเกินไป"
            : code === "CRM_BAD_RESPONSE" ? "ข้อมูลจาก CRM ไม่ถูกต้อง"
              : code === "CRM_TICKET_CLOSED" ? "Ticket ปิดแล้ว"
              : code === "CRM_TICKET_NOT_FOUND" ? "ไม่พบ Ticket"
                : code === "CRM_INVALID_QUERY" ? "ข้อมูลค้นหาไม่ถูกต้อง"
                  : "โหลดข้อมูล CRM ไม่สำเร็จ";

/** ไฟล์แนบของ comment: แสดงใต้ comment เดียวกัน รูปคลิกเปิด lightbox และไฟล์อื่นเปิดแท็บใหม่ */
function CrmAnswerAttachments({ image, onOpen }: { image?: string | null; onOpen: (src: string) => void }) {
  const [broken, setBroken] = useState<string[]>([]);
  const attachments = crmAttachmentUrls(image).map((src) => ({ src, name: fileNameOf(src), isImage: isImageUrl(src) }));
  const unresolved = Boolean(image?.trim()) && attachments.length === 0;
  if (attachments.length === 0 && !unresolved) return null;
  return <div className="crm-answer-files crm-answer-files-inline" aria-label="รูปภาพและไฟล์แนบของ comment">
    {attachments.map((attachment, index) => {
      const imageNumber = attachments.slice(0, index).filter((item) => item.isImage).length + 1;
      return attachment.isImage && !broken.includes(attachment.src)
        ? <button key={attachment.src} type="button" className="crm-answer-thumb" onClick={() => onOpen(attachment.src)} aria-label={`ดูรูปภาพ ${imageNumber}: ${attachment.name}`}><img src={attachment.src} alt="" loading="lazy" onError={() => setBroken((current) => [...current, attachment.src])} /></button>
        : <a key={attachment.src} className="crm-answer-attachment" href={attachment.src} target="_blank" rel="noreferrer"><span className="material-symbols-outlined" aria-hidden="true">attach_file</span>{attachment.name}<span className="material-symbols-outlined" aria-hidden="true">open_in_new</span></a>;
    })}
    {unresolved && <small className="crm-answer-attachment crm-ticket-attachments-note"><span className="material-symbols-outlined" aria-hidden="true">attach_file</span>มีไฟล์แนบใน CRM ที่ไม่สามารถแสดงลิงก์ได้</small>}
  </div>;
}

type CrmLookupItem = { id: string; name: string };
type CrmAssignee = { staffCode: string; name: string };
const CRM_SUBJECT_MAX = 200;

// ช่องทางการติดต่อที่ยืนยันแล้ว — ชุดเดียวกับ CrmController.CreateSources
const CRM_SOURCES = ["Call", "Email", "Facebook", "Walk In", "Remote", "Line"] as const;
// ประเภทลูกค้า (SysCustomerType) และกติกาไฟล์แนบ — ชุดเดียวกับฟอร์ม CRM และ CrmAttachmentRules ฝั่ง API
const CRM_CUSTOMER_TYPES = [["1", "None MA"], ["2", "MA"], ["3", "Demo"], ["4", "Dealer"]] as const;
const CRM_FILE_MAX = 10, CRM_FILE_MAX_BYTES = 5 * 1024 * 1024, CRM_FILE_TOTAL_BYTES = 25 * 1024 * 1024;
const CRM_FILE_ACCEPT = ".jpg,.jpeg,.png,.xlsx,.xls,.doc,.docx,.pdf";
type CrmPendingFile = { id: string; file: File; preview?: string };
const splitPersonName = (fullName?: string | null) => { const parts = (fullName ?? "").trim().replace(NAME_TITLE, "").split(/\s+/).filter(Boolean); return { first: parts[0] ?? "", last: parts.slice(1).join(" ") }; };

/**
 * สร้าง Ticket ใหม่ใน CRM — ช่องและลำดับตามฟอร์ม New Job ของ BlueSea (เรื่อง → ข้อมูลลูกค้า → รายละเอียดงาน)
 * ผู้รับเรื่อง/ผู้บันทึกเป็นผู้ใช้เองเสมอ; Member/ชื่อ ตั้งต้นเป็นผู้ใช้เอง; หัวเรื่องให้บริการ/Product ดึงสดจาก CRM
 */
function CrmCreateTicketModal({ username, fullName, staff, onClose, onCreated }: { username?: string | null; fullName?: string; staff: (value?: string | null) => string | null; onClose: () => void; onCreated: (jobNo: string, assignedToSelf: boolean) => void }) {
  const [lookups, setLookups] = useState<{ serviceTypes: CrmLookupItem[]; products: CrmLookupItem[] } | null>(null), [lookupError, setLookupError] = useState(""), [lookupReload, setLookupReload] = useState(0);
  const [assignees, setAssignees] = useState<CrmAssignee[]>([]);
  const [contactAt] = useState(() => fmtDateTimeBE(new Date().toISOString()));
  const today = useMemo(() => dateInput(new Date()), []);
  const [form, setForm] = useState(() => {
    const name = splitPersonName(fullName);
    return { subject: "", member: username ?? "", firstName: name.first, lastName: name.last, tel: "", nickName: "", lineId: "", email: "", customerType: "1", serviceTypeId: "", status: "Open", source: "Call", owner: "", assignTo: "", development: "", refJobNo: "", dueDate: today, productId: "", description: "" };
  });
  const [saving, setSaving] = useState(false), [saveError, setSaveError] = useState("");
  const [files, setFiles] = useState<CrmPendingFile[]>([]), [fileError, setFileError] = useState("");
  const fileInputRef = useRef<HTMLInputElement>(null);
  const filesRef = useRef(files);
  filesRef.current = files;
  // คืนหน่วยความจำของรูปพรีวิวเมื่อปิด modal
  useEffect(() => () => filesRef.current.forEach((item) => { if (item.preview) URL.revokeObjectURL(item.preview); }), []);
  const addFiles = (incoming: File[]) => {
    if (incoming.length === 0) return;
    const allowed = /\.(jpe?g|png|xlsx?|docx?|pdf)$/i;
    const bad = incoming.find((file) => !allowed.test(file.name));
    if (bad) { setFileError(`ไฟล์ ${bad.name} ไม่รองรับ — แนบได้เฉพาะ .jpg, .jpeg, .png, .xlsx, .xls, .doc, .docx, .pdf`); return; }
    const large = incoming.find((file) => file.size > CRM_FILE_MAX_BYTES);
    if (large) { setFileError(`ไฟล์ ${large.name} มีขนาดเกิน 5 MB`); return; }
    if (files.length + incoming.length > CRM_FILE_MAX) { setFileError(`แนบไฟล์ได้สูงสุด ${CRM_FILE_MAX} ไฟล์`); return; }
    if ([...files.map((item) => item.file), ...incoming].reduce((sum, file) => sum + file.size, 0) > CRM_FILE_TOTAL_BYTES) { setFileError("ขนาดไฟล์แนบรวมต้องไม่เกิน 25 MB"); return; }
    setFileError("");
    setFiles((current) => [...current, ...incoming.map((file) => ({ id: `${file.name}-${file.size}-${file.lastModified}-${Math.random().toString(36).slice(2)}`, file, preview: /^image\//.test(file.type) ? URL.createObjectURL(file) : undefined }))]);
  };
  const removeFile = (id: string) => setFiles((current) => current.filter((item) => { if (item.id === id && item.preview) URL.revokeObjectURL(item.preview); return item.id !== id; }));
  type FormKey = keyof typeof form;
  const set = (key: FormKey) => (event: { target: { value: string } }) => setForm((current) => ({ ...current, [key]: event.target.value }));

  useEffect(() => {
    const controller = new AbortController();
    setLookups(null); setLookupError("");
    apiFetch<{ serviceTypes: CrmLookupItem[]; products: CrmLookupItem[] }>("/crm/lookups", { signal: controller.signal })
      .then((value) => setLookups({ serviceTypes: value.serviceTypes ?? [], products: value.products ?? [] }))
      .catch((reason: unknown) => { if (!isAbortError(reason)) setLookupError(crmErrorMessage(reason, "โหลดรายการหัวเรื่องให้บริการ/Product จาก CRM ไม่สำเร็จ")); });
    return () => controller.abort();
  }, [lookupReload]);

  useEffect(() => {
    const controller = new AbortController();
    // ไม่บังคับ — โหลดไม่ได้ยังเลือกตัวเองได้
    apiFetch<CrmAssignee[]>("/crm/assignees", { signal: controller.signal }).then((value) => setAssignees(Array.isArray(value) ? value : [])).catch(() => undefined);
    return () => controller.abort();
  }, []);

  const selfLabel = staff(username) ?? "บัญชี CRM ของคุณ";
  const people = assignees.filter((item) => item.staffCode !== username);
  const personOptions = people.map((item) => <option key={item.staffCode} value={item.staffCode}>{item.staffCode} {withoutNameTitle(item.name)}</option>);
  const emailInvalid = Boolean(form.email.trim()) && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(form.email.trim());
  const required: FormKey[] = ["subject", "member", "firstName", "tel", "serviceTypeId", "productId", "description"];
  const canSave = Boolean(lookups) && required.every((key) => form[key].trim()) && !emailInvalid && !saving;
  const save = async () => {
    if (!canSave) return;
    setSaving(true); setSaveError("");
    try {
      const body = new FormData();
      const fields: Record<string, string> = { subject: form.subject, description: form.description, serviceTypeId: form.serviceTypeId, productId: form.productId, member: form.member, firstName: form.firstName, lastName: form.lastName, tel: form.tel, nickName: form.nickName, lineId: form.lineId, email: form.email, customerType: form.customerType, status: form.status, source: form.source, dueDate: form.dueDate, refJobNo: form.refJobNo, assignToStaffCode: form.assignTo, ownerStaffCode: form.owner, developmentStaffCode: form.development };
      Object.entries(fields).forEach(([key, value]) => body.append(key, value));
      files.forEach((item) => body.append("files", item.file, item.file.name));
      const result = await apiFetch<{ jobNo: string }>("/crm/tickets", { method: "POST", form: body, fallbackMessage: "สร้าง Ticket ใน CRM ไม่สำเร็จ" });
      onCreated(result.jobNo, !form.assignTo || form.assignTo === username);
    } catch (reason) {
      setSaveError(crmErrorMessage(reason, "สร้าง Ticket ใน CRM ไม่สำเร็จ"));
      setSaving(false);
    }
  };
  const loadingLabel = (label: string) => lookups ? label : lookupError ? "โหลดไม่สำเร็จ" : "กำลังโหลด...";
  const req = <span className="crm-required" aria-hidden="true">*</span>;

  return <ModalShell labelledBy="crm-create-ticket-title" className="crm-create-ticket-modal" onDismiss={() => { if (!saving) onClose(); }}>
    <div className="modal-head"><div><h2 id="crm-create-ticket-title">สร้าง Ticket ใหม่ใน CRM</h2><ul className="crm-ct-meta" aria-label="ข้อมูลที่ระบบกำหนด"><li><span className="material-symbols-outlined" aria-hidden="true">schedule</span>วันที่ติดต่อ <strong>{contactAt}</strong></li><li><span className="material-symbols-outlined" aria-hidden="true">person_check</span>ผู้รับเรื่อง <strong>{selfLabel}</strong></li><li><span className="material-symbols-outlined" aria-hidden="true">bolt</span>บันทึกลง CRM ทันทีเมื่อกดสร้าง</li></ul></div><button type="button" aria-label="ปิด" disabled={saving} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
    {lookupError && <div className="crm-inline-alert" role="alert"><span className="material-symbols-outlined" aria-hidden="true">error</span><span>{lookupError}</span><button className="crm-configure-button" type="button" onClick={() => setLookupReload((value) => value + 1)}>ลองใหม่</button></div>}
    <fieldset className="crm-create-ticket-form" disabled={saving}>
      <label className="crm-ct-subject"><span>เรื่อง {req}</span><input maxLength={CRM_SUBJECT_MAX} value={form.subject} onChange={set("subject")} placeholder="หัวข้อเรื่องที่แจ้ง" required /></label>
      <div className="crm-ct-panes">
        <section className="crm-ct-section" aria-labelledby="crm-ct-customer"><h3 id="crm-ct-customer"><span className="material-symbols-outlined" aria-hidden="true">person</span>ข้อมูลลูกค้า</h3><div className="crm-ct-grid crm-ct-grid-2">
          <label><span>Member {req}</span><input maxLength={50} value={form.member} onChange={set("member")} placeholder="รหัสสมาชิก" required /></label>
          <label><span>เบอร์โทรศัพท์ {req}</span><input type="tel" maxLength={50} value={form.tel} onChange={set("tel")} required /></label>
          <label><span>ชื่อ {req}</span><input maxLength={100} value={form.firstName} onChange={set("firstName")} required /></label>
          <label>นามสกุล<input maxLength={100} value={form.lastName} onChange={set("lastName")} /></label>
          <label>ชื่อเล่น<input maxLength={50} value={form.nickName} onChange={set("nickName")} /></label>
          <label>LineID<input maxLength={50} value={form.lineId} onChange={set("lineId")} /></label>
          <label>E-Mail<input type="email" maxLength={100} value={form.email} onChange={set("email")} aria-invalid={emailInvalid || undefined} aria-describedby={emailInvalid ? "crm-ct-email-error" : undefined} />{emailInvalid && <small id="crm-ct-email-error" className="crm-field-error">รูปแบบ E-Mail ไม่ถูกต้อง</small>}</label>
          <label>ประเภทลูกค้า<select value={form.customerType} onChange={set("customerType")}>{CRM_CUSTOMER_TYPES.map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>
        </div></section>
        <section className="crm-ct-section" aria-labelledby="crm-ct-job"><h3 id="crm-ct-job"><span className="material-symbols-outlined" aria-hidden="true">assignment</span>รายละเอียดงาน</h3><div className="crm-ct-grid crm-ct-grid-3">
          <label><span>หัวเรื่องให้บริการ {req}</span><select value={form.serviceTypeId} onChange={set("serviceTypeId")} disabled={!lookups} required><option value="">{loadingLabel("กรุณาเลือกประเภทการติดต่อ")}</option>{lookups?.serviceTypes.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
          <label><span>Product {req}</span><select value={form.productId} onChange={set("productId")} disabled={!lookups} required><option value="">{loadingLabel("กรุณาเลือก Product")}</option>{lookups?.products.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
          <label>สถานะ<select value={form.status} onChange={set("status")}>{CRM_STATUSES.map((value) => <option key={value} value={value}>{value}</option>)}</select></label>
          <label>เจ้าของเรื่อง<select value={form.owner} onChange={set("owner")}><option value="">{selfLabel}</option>{personOptions}</select></label>
          <label>Assign To<select value={form.assignTo} onChange={set("assignTo")}><option value="">{selfLabel}</option>{personOptions}</select></label>
          <label>Development<select value={form.development} onChange={set("development")}><option value="">ไม่ระบุพนักงาน</option>{username && <option value={username}>{selfLabel}</option>}{personOptions}</select></label>
          <label>ช่องทางการติดต่อ<select value={form.source} onChange={set("source")}>{CRM_SOURCES.map((value) => <option key={value} value={value}>{value}</option>)}</select></label>
          <CrmDateField label="Duedate" value={form.dueDate} min={today} onChange={(value) => setForm((current) => ({ ...current, dueDate: value }))} />
          <label>Ref JobNo<input maxLength={30} value={form.refJobNo} onChange={set("refJobNo")} spellCheck={false} /></label>
          <label className="crm-ct-description"><span>รายละเอียด {req}<small className="crm-create-ticket-count">{form.description.length}/{CRM_REPLY_MAX}</small></span><textarea rows={3} maxLength={CRM_REPLY_MAX} value={form.description} onChange={set("description")} onPaste={(event) => { const pasted = Array.from(event.clipboardData.files).filter((file) => /^image\//.test(file.type)); if (pasted.length > 0) { event.preventDefault(); addFiles(pasted.map((file, index) => new File([file], file.name && file.name !== "image.png" ? file.name : `paste-${Date.now()}-${index + 1}.png`, { type: file.type }))); } }} placeholder="อาการ/สิ่งที่ต้องการให้ดำเนินการ — วางรูปจากคลิปบอร์ดได้ (บันทึกเป็นประวัติการติดต่อรายการแรก)" required /></label>
        </div>
        <div className="crm-ct-files">
          <div className="crm-ct-files-row"><button type="button" className="btn crm-ct-attach" onClick={() => fileInputRef.current?.click()} disabled={files.length >= CRM_FILE_MAX} title={`แนบได้สูงสุด ${CRM_FILE_MAX} ไฟล์ ไฟล์ละไม่เกิน 5 MB (.jpg .png .pdf .doc .xls)`} aria-describedby="crm-ct-files-hint"><span className="material-symbols-outlined" aria-hidden="true">attach_file</span>แนบไฟล์ ({files.length}/{CRM_FILE_MAX})</button><input ref={fileInputRef} type="file" accept={CRM_FILE_ACCEPT} multiple hidden onChange={(event) => { addFiles(Array.from(event.target.files ?? [])); event.target.value = ""; }} />
          {files.length > 0 ? <ul className="crm-ct-file-list">{files.map((item) => <li key={item.id}>{item.preview ? <img src={item.preview} alt="" /> : <span className="material-symbols-outlined" aria-hidden="true">description</span>}<span className="crm-ct-file-name" title={item.file.name}>{item.file.name}</span><button type="button" aria-label={`นำไฟล์ ${item.file.name} ออก`} onClick={() => removeFile(item.id)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></li>)}</ul> : <small id="crm-ct-files-hint" className="crm-ct-files-hint">รูป/เอกสาร ≤ {CRM_FILE_MAX} ไฟล์ ไฟล์ละ 5 MB · วางรูปในช่องรายละเอียดได้</small>}</div>
          {fileError && <small className="crm-field-error" role="alert">{fileError}</small>}
        </div></section>
      </div>
    </fieldset>
    {saveError && <div className="crm-inline-alert" role="alert"><span className="material-symbols-outlined" aria-hidden="true">error</span><span>{saveError}</span></div>}
    <div className="modal-actions"><button type="button" className="btn" disabled={saving} onClick={onClose}>ยกเลิก</button><button type="button" className="btn primary" disabled={!canSave} onClick={() => void save()}>{saving ? "กำลังสร้าง..." : "สร้าง Ticket"}</button></div>
  </ModalShell>;
}

export function CrmPage({ search, onConfigure, configVersion = 0, contextProjectId, canLinkDefect = false, canEditCrm = false }: { search: string; onConfigure?: () => void; configVersion?: number; contextProjectId?: string; canLinkDefect?: boolean; canEditCrm?: boolean }) {
  const initialFrom = useMemo(() => daysAgo(DEFAULT_RANGE_DAYS), []);
  const initialTo = useMemo(() => dateInput(new Date()), []);
  const [connection, setConnection] = useState<CrmConnectionStatus | null>(null), [connectionError, setConnectionError] = useState("");
  const [rows, setRows] = useState<CrmTicket[]>([]), [summary, setSummary] = useState({ total: 0, open: 0, inProgress: 0, closed: 0 }), [total, setTotal] = useState(0), [lastFetchedAt, setLastFetchedAt] = useState("");
  const [status, setStatus] = useState(""), [from, setFrom] = useState(initialFrom), [rangePreset, setRangePreset] = useState<number | "custom">(DEFAULT_RANGE_DAYS), [to, setTo] = useState(initialTo), [page, setPage] = useState(1), [pageSize, setPageSize] = useState(25), [loading, setLoading] = useState(true), [error, setError] = useState(""), [errorCode, setErrorCode] = useState<string | undefined>(), [reload, setReload] = useState(0);
  const [selectedTicket, setSelectedTicket] = useState<CrmTicket | null>(null), [jobNoQuery, setJobNoQuery] = useState(""), [jobNoBusy, setJobNoBusy] = useState(false), [jobNoError, setJobNoError] = useState("");
  // รายละเอียดที่โหลดไว้แล้วตอนค้นหา Job No. — effect โหลดรายละเอียดใช้ซ้ำเมื่อเป็น Job และรอบ reload เดียวกัน (ไม่ยิง CRM ซ้ำ)
  const prefetchedDetail = useRef<{ detail: CrmTicketDetail; reload: number } | null>(null);
  const [ticketDetail, setTicketDetail] = useState<CrmTicketDetail | null>(null), [ticketDetailLoading, setTicketDetailLoading] = useState(false), [ticketDetailError, setTicketDetailError] = useState(""), [ticketDetailErrorCode, setTicketDetailErrorCode] = useState<string | undefined>(), [detailReload, setDetailReload] = useState(0);
  const [probeLoading, setProbeLoading] = useState(false), [probeMessage, setProbeMessage] = useState(""), [probeError, setProbeError] = useState(false);
  const [viewMode, setViewMode] = useState<"list" | "board">("list");
  const [ticketScope, setTicketScope] = useState<CrmTicketScope>("mine"), [scopeCounts, setScopeCounts] = useState<{ mine: number; previous: number } | null>(null), [previousQa, setPreviousQa] = useState<Record<string, string> | null>(null);
  const [linkedDefect, setLinkedDefect] = useState<CrmLinkedDefect | null>(null), [linkedDefectLoading, setLinkedDefectLoading] = useState(false), [linkedDefectSaving, setLinkedDefectSaving] = useState(false), [linkedDefectError, setLinkedDefectError] = useState("");
  const [defectLinkDialogOpen, setDefectLinkDialogOpen] = useState(false), [linkableDefects, setLinkableDefects] = useState<LinkableDefect[]>([]), [linkDefectSearch, setLinkDefectSearch] = useState(""), [linkDefectLoading, setLinkDefectLoading] = useState(false), [linkDefectSaving, setLinkDefectSaving] = useState(false), [linkDefectError, setLinkDefectError] = useState("");
  const [createDefectDialogOpen, setCreateDefectDialogOpen] = useState(false), [createDefectTitle, setCreateDefectTitle] = useState(""), [createDefectSeverity, setCreateDefectSeverity] = useState("Medium"), [createDefectDescription, setCreateDefectDescription] = useState(""), [createDefectSaving, setCreateDefectSaving] = useState(false), [createDefectError, setCreateDefectError] = useState("");
  const [replyMessage, setReplyMessage] = useState(""), [replyStatus, setReplyStatus] = useState(""), [replyToOwner, setReplyToOwner] = useState(false), [replySaving, setReplySaving] = useState(false), [replyError, setReplyError] = useState(""), [replyFiles, setReplyFiles] = useState<CrmPendingFile[]>([]), [replyFileError, setReplyFileError] = useState("");
  const [assigneeOptions, setAssigneeOptions] = useState<CrmAssignee[]>([]), [assigneeValue, setAssigneeValue] = useState(""), [assigneeQuery, setAssigneeQuery] = useState(""), [assigneeSearchOpen, setAssigneeSearchOpen] = useState(false), [assigneeLoading, setAssigneeLoading] = useState(false), [assigneeError, setAssigneeError] = useState("");
  const [serviceOptions, setServiceOptions] = useState<CrmLookupItem[]>([]), [serviceValue, setServiceValue] = useState(""), [serviceOriginalValue, setServiceOriginalValue] = useState(""), [serviceQuery, setServiceQuery] = useState(""), [serviceSearchOpen, setServiceSearchOpen] = useState(false), [serviceLoading, setServiceLoading] = useState(false), [serviceError, setServiceError] = useState("");
  const [productOptions, setProductOptions] = useState<CrmLookupItem[]>([]), [productValue, setProductValue] = useState(""), [productOriginalValue, setProductOriginalValue] = useState(""), [productQuery, setProductQuery] = useState(""), [productSearchOpen, setProductSearchOpen] = useState(false), [productLoading, setProductLoading] = useState(false), [productError, setProductError] = useState("");
  const [clock, setClock] = useState(() => Date.now());
  const [createTicketOpen, setCreateTicketOpen] = useState(false);
  const [answerLightbox, setAnswerLightbox] = useState<number | null>(null);
  const [historyTab, setHistoryTab] = useState<"contact" | "edit">("contact");
  const [flowHistory, setFlowHistory] = useState<CrmFlowHistoryItem[]>([]), [flowHistoryLoading, setFlowHistoryLoading] = useState(false), [flowHistoryError, setFlowHistoryError] = useState("");
  const replyFileInputRef = useRef<HTMLInputElement>(null);
  const replyFilesRef = useRef(replyFiles);
  replyFilesRef.current = replyFiles;
  useEffect(() => () => replyFilesRef.current.forEach((item) => { if (item.preview) URL.revokeObjectURL(item.preview); }), []);
  // reload (ปุ่มรีเฟรช/หลังบันทึก) ต้องดึงสดจาก CRM ส่วนเปลี่ยนหน้า/ค้นหาใช้ cache ฝั่ง server 60 วินาทีได้
  const lastListReload = useRef<number | null>(null);
  const [staffNames, setStaffNames] = useState<Record<string, string>>({});
  const selectedJobNo = selectedTicket?.jobNo;
  // เปิดจากช่อง Job No. จะมีแค่ jobNo — เติมข้อมูลที่เหลือจากรายละเอียด Ticket เมื่อโหลดเสร็จ (เฉพาะ Ticket เดียวกัน และไม่ทับค่าที่มีด้วยค่าว่าง)
  const ticketView: CrmTicket | null = selectedTicket && {
    ...selectedTicket,
    ...(ticketDetail?.ticket.jobNo.toLowerCase() === selectedTicket.jobNo.toLowerCase() ? Object.fromEntries(Object.entries(ticketDetail.ticket).filter(([, value]) => value !== null && value !== undefined && value !== "")) : {}),
  };
  const ticketClosed = ["close", "closed"].includes(ticketView?.status?.trim().toLocaleLowerCase() ?? "");
  const canEditSelectedTicket = canEditCrm && !ticketClosed;
  // ค้นหา Job No. ไม่ขึ้นกับช่วงวันที่/ตัวกรอง — เปิดรายละเอียดจาก CRM โดยตรง (ถ้าอยู่ในรายการหน้านี้ใช้ข้อมูลแถวนั้นเป็นค่าตั้งต้น)
  // ตรวจกับ CRM ก่อนเปิด modal — ไม่พบ/โหลดไม่สำเร็จแจ้งใต้ช่องค้นหา ไม่เปิด modal ที่ไม่มีข้อมูล
  const openJobNo = async () => {
    const jobNo = jobNoQuery.trim();
    if (!jobNo || jobNoBusy) return;
    setJobNoBusy(true); setJobNoError("");
    try {
      const detail = await getJson<CrmTicketDetail>(`${apiUrl}/crm/tickets/${encodeURIComponent(jobNo)}`);
      if (!detail?.ticket?.jobNo) throw new ApiError(404, "", "CRM_TICKET_NOT_FOUND");
      prefetchedDetail.current = { detail, reload: detailReload };
      const sameJob = (row: CrmTicket) => row.jobNo.toLowerCase() === detail.ticket.jobNo.toLowerCase();
      // รายละเอียดจาก CRM (HelpDesksJob) มีแค่รหัส Service/Product ไม่มีชื่อ — ชื่ออยู่ในข้อมูลรายการ จึงดึงแถวของ Job นี้
      // จากรายการวันที่ติดต่อวันเดียว (best-effort: ไม่ได้ก็เปิด modal ด้วยข้อมูลจากรายละเอียดตามเดิม)
      let row = rows.find(sameJob);
      const contactDay = toUtcDate(detail.ticket.contactDate);
      if (!row && contactDay) {
        const day = dateInput(contactDay);
        const params = new URLSearchParams({ page: "1", pageSize: "100", from: day, to: day, search: detail.ticket.jobNo });
        row = await getJson<CrmListResult>(`${apiUrl}/crm/tickets?${params.toString()}`).then((value) => value.rows?.find(sameJob)).catch(() => undefined);
      }
      setSelectedTicket(row ?? detail.ticket);
    } catch (reason) {
      setJobNoError(reason instanceof ApiError && (reason.code === "CRM_TICKET_NOT_FOUND" || reason.status === 404) ? `ไม่พบ Job No. "${jobNo}" ในงานของบัญชี CRM นี้ กรุณาตรวจสอบเลขอีกครั้ง`
        : reason instanceof TypeError ? "เชื่อมต่อระบบไม่สำเร็จ กรุณาตรวจสอบเครือข่ายแล้วลองใหม่"
          : crmErrorMessage(reason, "ค้นหา Job No. ไม่สำเร็จ"));
    } finally { setJobNoBusy(false); }
  };

  useEffect(() => {
    const timer = window.setInterval(() => setClock(Date.now()), 60_000);
    return () => window.clearInterval(timer);
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    getJson<CrmConnectionStatus>(`${apiUrl}/crm/connection`, controller.signal)
      .then((value) => { setConnection(value); setConnectionError(""); })
      .catch((reason: unknown) => { if (!isAbortError(reason)) { setConnection({ isConfigured: false, isEnabled: false }); setConnectionError(crmErrorMessage(reason, "ตรวจสอบสถานะ CRM ไม่สำเร็จ")); } });
    return () => controller.abort();
  }, [configVersion, reload]);

  useEffect(() => {
    if (!connection) return;
    if (!connection.isConfigured || !connection.isEnabled) {
      setRows([]); setTotal(0); setSummary({ total: 0, open: 0, inProgress: 0, closed: 0 }); setScopeCounts(null); setLastFetchedAt(""); setError(""); setErrorCode(undefined); setLoading(false);
      return;
    }
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      setLoading(true); setError(""); setErrorCode(undefined);
      const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize), from, to });
      if (lastListReload.current !== null && lastListReload.current !== reload) params.set("refresh", "true");
      lastListReload.current = reload;
      if (search.trim()) params.set("search", search.trim());
      if (status) params.set("status", status);
      if (ticketScope !== "mine") params.set("scope", ticketScope);
      getJson<CrmListResult>(`${apiUrl}/crm/tickets?${params.toString()}`, controller.signal)
        .then((value) => { setRows(Array.isArray(value.rows) ? value.rows : []); setTotal(value.total ?? 0); setSummary(value.summary ?? { total: 0, open: 0, inProgress: 0, closed: 0 }); setScopeCounts(value.scopeCounts ?? null); setPreviousQa(value.previousQa ?? null); setLastFetchedAt(value.lastFetchedAt ?? ""); })
        .catch((reason: unknown) => {
          if (isAbortError(reason)) return;
          const apiError = reason instanceof ApiError ? reason : null;
          setRows([]); setScopeCounts(null);
          setErrorCode(apiError?.code);
          setError(crmErrorMessage(reason, "โหลดรายการ CRM ไม่สำเร็จ"));
        })
        .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    }, 300);
    return () => { window.clearTimeout(timer); controller.abort(); };
  }, [connection, from, page, pageSize, reload, search, status, ticketScope, to]);

  useEffect(() => {
    if (!selectedJobNo) return;
    const cached = prefetchedDetail.current;
    if (cached && cached.reload === detailReload && cached.detail.ticket.jobNo.toLowerCase() === selectedJobNo.toLowerCase()) {
      setTicketDetail(cached.detail); setTicketDetailError(""); setTicketDetailErrorCode(undefined); setTicketDetailLoading(false);
      return;
    }
    const controller = new AbortController();
    setTicketDetail(null); setTicketDetailError(""); setTicketDetailErrorCode(undefined); setTicketDetailLoading(true);
    getJson<CrmTicketDetail>(`${apiUrl}/crm/tickets/${encodeURIComponent(selectedJobNo)}`, controller.signal)
      .then(setTicketDetail)
      .catch((reason: unknown) => { if (!isAbortError(reason)) { setTicketDetailErrorCode(reason instanceof ApiError ? reason.code : undefined); setTicketDetailError(crmErrorMessage(reason, "โหลดรายละเอียด Ticket ไม่สำเร็จ")); } })
      .finally(() => { if (!controller.signal.aborted) setTicketDetailLoading(false); });
    return () => controller.abort();
  }, [detailReload, selectedJobNo]);

  useEffect(() => {
    if (!selectedJobNo) { setLinkedDefect(null); setLinkedDefectError(""); return; }
    const controller = new AbortController();
    setLinkedDefect(null); setLinkedDefectError(""); setLinkedDefectLoading(true);
    apiFetch<CrmLinkedDefect>(`/crm/tickets/${encodeURIComponent(selectedJobNo)}/defect`, { signal: controller.signal })
      .then(setLinkedDefect)
      .catch((reason: unknown) => {
        if (isAbortError(reason)) return;
        if (reason instanceof ApiError && reason.status === 404) return;
        setLinkedDefectError(reason instanceof Error ? reason.message : "โหลด Defect ที่เชื่อมโยงไม่สำเร็จ");
      })
      .finally(() => { if (!controller.signal.aborted) setLinkedDefectLoading(false); });
    return () => controller.abort();
  }, [selectedJobNo, detailReload]);

  useEffect(() => {
    if (!selectedJobNo) { setFlowHistory([]); setFlowHistoryError(""); return; }
    const controller = new AbortController();
    setFlowHistory([]); setFlowHistoryError(""); setFlowHistoryLoading(true);
    apiFetch<CrmFlowHistoryItem[]>(`/crm/tickets/${encodeURIComponent(selectedJobNo)}/flow-history`, { signal: controller.signal })
      .then((value) => setFlowHistory(Array.isArray(value) ? value : []))
      .catch((reason: unknown) => { if (!isAbortError(reason)) setFlowHistoryError(crmErrorMessage(reason, "โหลดประวัติ Flow ไม่สำเร็จ")); })
      .finally(() => { if (!controller.signal.aborted) setFlowHistoryLoading(false); });
    return () => controller.abort();
  }, [selectedJobNo, detailReload]);

  useEffect(() => {
    if (!defectLinkDialogOpen) return;
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      setLinkDefectLoading(true); setLinkDefectError("");
      const params = new URLSearchParams({ page: "1", size: "100" });
      if (contextProjectId) params.set("projectId", contextProjectId);
      if (linkDefectSearch.trim()) params.set("search", linkDefectSearch.trim());
      apiFetch<DefectListResult>(`/defects?${params.toString()}`, { signal: controller.signal })
        .then((value) => setLinkableDefects(Array.isArray(value?.rows) ? value.rows : []))
        .catch((reason: unknown) => { if (!isAbortError(reason)) { setLinkableDefects([]); setLinkDefectError(reason instanceof Error ? reason.message : "โหลด Defect ไม่สำเร็จ"); } })
        .finally(() => { if (!controller.signal.aborted) setLinkDefectLoading(false); });
    }, 250);
    return () => { window.clearTimeout(timer); controller.abort(); };
  }, [contextProjectId, defectLinkDialogOpen, linkDefectSearch]);

  // เปลี่ยน Ticket ที่เปิด = ล้างฟอร์มอัปเดต (ไม่ให้ข้อความที่พิมพ์ค้างไปบันทึกผิด Ticket)
  useEffect(() => {
    setReplyMessage(""); setReplyStatus(""); setReplyToOwner(false); setReplyError(""); setReplyFileError("");
    setReplyFiles((current) => { current.forEach((item) => { if (item.preview) URL.revokeObjectURL(item.preview); }); return []; });
    setHistoryTab("contact"); setAssigneeOptions([]); setAssigneeValue(""); setAssigneeQuery(""); setAssigneeSearchOpen(false); setAssigneeError(""); setServiceValue(""); setServiceOriginalValue(""); setServiceQuery(""); setServiceSearchOpen(false); setServiceError(""); setProductOptions([]); setProductValue(""); setProductOriginalValue(""); setProductQuery(""); setProductSearchOpen(false); setProductError("");
  }, [selectedJobNo]);

  useEffect(() => {
    if (!canEditSelectedTicket || !selectedJobNo) return;
    const controller = new AbortController();
    setAssigneeLoading(true); setAssigneeError("");
    apiFetch<CrmAssignee[]>("/crm/assignees", { signal: controller.signal })
      .then((value) => {
        const options = Array.isArray(value) ? value : [];
        setAssigneeOptions(options);
        setAssigneeValue((previous) => previous || ticketView?.assignee?.trim() || "");
      })
      .catch((reason: unknown) => { if (!isAbortError(reason)) { setAssigneeOptions([]); setAssigneeError(crmErrorMessage(reason, "โหลดรายชื่อผู้รับผิดชอบไม่สำเร็จ")); } })
      .finally(() => { if (!controller.signal.aborted) setAssigneeLoading(false); });
    return () => controller.abort();
  }, [canEditSelectedTicket, selectedJobNo, ticketView?.assignee]);

  useEffect(() => {
    if (!canEditSelectedTicket || !selectedJobNo) return;
    const controller = new AbortController();
    setServiceLoading(true); setServiceError(""); setProductLoading(true); setProductError("");
    apiFetch<{ serviceTypes: CrmLookupItem[]; products: CrmLookupItem[] }>("/crm/lookups", { signal: controller.signal })
      .then((value) => {
        const options = Array.isArray(value?.serviceTypes) ? value.serviceTypes : [];
        const products = Array.isArray(value?.products) ? value.products : [];
        const currentName = ticketView?.serviceType?.trim().toLocaleLowerCase();
        const current = currentName ? options.find((item) => item.name.trim().toLocaleLowerCase() === currentName) : undefined;
        const currentProductName = ticketView?.product?.trim().toLocaleLowerCase();
        const currentProduct = currentProductName ? products.find((item) => item.name.trim().toLocaleLowerCase() === currentProductName) : undefined;
        setServiceOptions(options);
        setServiceValue((previous) => previous || current?.id || "");
        setServiceOriginalValue((previous) => previous || current?.id || "");
        setProductOptions(products);
        setProductValue((previous) => previous || currentProduct?.id || "");
        setProductOriginalValue((previous) => previous || currentProduct?.id || "");
      })
      .catch((reason: unknown) => { if (!isAbortError(reason)) { setServiceOptions([]); setServiceError(crmErrorMessage(reason, "โหลดรายการ Service ไม่สำเร็จ")); setProductOptions([]); setProductError(crmErrorMessage(reason, "โหลดรายการ Product ไม่สำเร็จ")); } })
      .finally(() => { if (!controller.signal.aborted) { setServiceLoading(false); setProductLoading(false); } });
    return () => controller.abort();
  }, [canEditSelectedTicket, selectedJobNo, ticketView?.serviceType, ticketView?.product]);

  const crmReady = Boolean(connection?.isConfigured && connection.isEnabled);
  useEffect(() => {
    if (!crmReady || Object.keys(staffNames).length > 0) return;
    const controller = new AbortController();
    // ไม่บล็อกหน้า: โหลดชื่อไม่สำเร็จก็ยังแสดงรหัสตามเดิม
    apiFetch<CrmStaff[]>("/crm/staff", { signal: controller.signal })
      .then((rows) => setStaffNames(Object.fromEntries((Array.isArray(rows) ? rows : []).map((row) => [row.staffCode, row.name]))))
      .catch(() => undefined);
    return () => controller.abort();
  }, [crmReady, staffNames]);
  const staff = (value?: string | null) => staffLabel(value, staffNames);
  const assigneeOptionLabel = (item: CrmAssignee) => {
    const label = staff(item.staffCode);
    if (label && label !== item.staffCode) return label;
    const name = withoutNameTitle(item.name);
    return name ? `${item.staffCode} ${name}` : item.staffCode;
  };
  const assigneeChoices = ticketView?.assignee && !assigneeOptions.some((item) => item.staffCode === ticketView.assignee)
    ? [{ staffCode: ticketView.assignee, name: "" }, ...assigneeOptions]
    : assigneeOptions;
  const assigneeQueryText = assigneeQuery.trim().toLocaleLowerCase();
  const filteredAssigneeChoices = assigneeChoices.filter((item) => {
    if (!assigneeQueryText) return true;
    return item.staffCode.toLocaleLowerCase().includes(assigneeQueryText)
      || withoutNameTitle(item.name).toLocaleLowerCase().includes(assigneeQueryText)
      || assigneeOptionLabel(item).toLocaleLowerCase().includes(assigneeQueryText);
  });
  const pendingAssignee = assigneeValue.trim();
  const displayedAssignee = pendingAssignee || ticketView?.assignee || "";
  const displayedAssigneeOption = assigneeChoices.find((item) => item.staffCode === displayedAssignee);
  const displayedAssigneeLabel = displayedAssigneeOption ? assigneeOptionLabel(displayedAssigneeOption) : staff(displayedAssignee);
  const pendingAssigneeChanged = Boolean(pendingAssignee && pendingAssignee !== (ticketView?.assignee ?? "").trim());
  const displayedServiceOption = serviceOptions.find((item) => item.id === serviceValue.trim());
  const displayedServiceLabel = displayedServiceOption?.name || ticketView?.serviceType;
  const serviceQueryText = serviceQuery.trim().toLocaleLowerCase();
  const filteredServiceChoices = serviceOptions.filter((item) => {
    if (!serviceQueryText) return true;
    return item.id.toLocaleLowerCase().includes(serviceQueryText) || item.name.toLocaleLowerCase().includes(serviceQueryText);
  });
  const pendingServiceChanged = Boolean(serviceValue.trim() && serviceValue.trim() !== serviceOriginalValue.trim());
  const displayedProductOption = productOptions.find((item) => item.id === productValue.trim());
  const displayedProductLabel = displayedProductOption?.name || ticketView?.product;
  const productQueryText = productQuery.trim().toLocaleLowerCase();
  const filteredProductChoices = productOptions.filter((item) => {
    if (!productQueryText) return true;
    return item.id.toLocaleLowerCase().includes(productQueryText) || item.name.toLocaleLowerCase().includes(productQueryText);
  });
  const pendingProductChanged = Boolean(productValue.trim() && productValue.trim() !== productOriginalValue.trim());
  const pageCount = Math.max(1, Math.ceil(total / pageSize));
  const configured = connection?.isConfigured && connection.isEnabled;
  const statusMessage = connectionError || (connection && !configured ? "บัญชี CRM ยังไม่พร้อมใช้งาน" : "");
  const connectionBadge = connectionError ? "ตรวจสอบไม่ได้" : configured ? "ตั้งค่าแล้ว" : "ต้องตั้งค่า";
  const stale = Boolean(lastFetchedAt && !Number.isNaN(Date.parse(lastFetchedAt)) && clock - Date.parse(lastFetchedAt) >= CRM_STALE_AFTER_MS);
  // List และ Board เรียงตามอายุงาน (Contact Date) มากไปน้อย — Ticket ที่ไม่มีวันที่ติดต่อไว้ท้ายสุด
  // เรียงเฉพาะข้อมูลในหน้าปัจจุบันที่ API ส่งมา
  // ประวัติการติดต่อเรียงล่าสุดไว้บนสุดเสมอ (CRM ส่งลำดับมาไม่แน่นอน) — รายการที่ไม่มี/อ่านวันที่ไม่ได้ไว้ท้ายสุด
  const sortedAnswers = useMemo(() => {
    const answerTime = (value?: string | null) => toUtcDate(value)?.getTime() ?? Number.NEGATIVE_INFINITY;
    return [...(ticketDetail?.answers ?? [])].sort((a, b) => { const left = answerTime(a.answerDate), right = answerTime(b.answerDate); return left === right ? 0 : left > right ? -1 : 1; });
  }, [ticketDetail]);
  const visibleAnswers = useMemo(() => {
    const result: CrmTicketAnswer[] = [];
    for (const answer of sortedAnswers) {
      if (answer.description?.trim()) {
        result.push(answer);
      } else if (answer.image?.trim() && result.length > 0) {
        const previous = result[result.length - 1];
        result[result.length - 1] = { ...previous, image: [previous.image, answer.image].filter(Boolean).join(",") };
      }
    }
    return result;
  }, [sortedAnswers]);
  const contactAnswers = useMemo(() => visibleAnswers.filter((answer) => !isCrmEditHistory(answer.description)), [visibleAnswers]);
  const editHistoryAnswers = useMemo(() => visibleAnswers.filter((answer) => isCrmEditHistory(answer.description)), [visibleAnswers]);
  const historyAnswers = historyTab === "edit" ? editHistoryAnswers : contactAnswers;
  // CRM บันทึก Description ตอนเปิดเรื่องเป็นแถวแรกของประวัติการติดต่อ (HelpDesksJob ไม่มีช่อง description) —
  // ถ้า Job ไม่มี description ในตัว ใช้ข้อความของรายการเก่าสุดที่มีวันที่ (ไม่นับไฟล์แนบ "P") ให้ตรงกับหน้า BlueSea
  const ticketDescription = useMemo(() => {
    if (ticketDetail?.description?.trim()) return ticketDetail.description;
    const textAnswers = sortedAnswers.filter((answer) => toUtcDate(answer.answerDate) && answer.description?.trim() && !/^p$/i.test(answer.answerType ?? ""));
    return textAnswers.at(-1)?.description ?? "";
  }, [sortedAnswers, ticketDetail]);
  // ไฟล์แนบทั้งหมดของ Ticket (ตามลำดับประวัติที่แสดง) — แสดงรวมไว้ใต้รายละเอียด
  const answerAttachments = useMemo(() => {
    const seen = new Set<string>();
    return sortedAnswers.flatMap((answer) => crmAttachmentUrls(answer.image).map((src) => ({ src, name: fileNameOf(src), isImage: isImageUrl(src) }))).filter((attachment) => {
      if (seen.has(attachment.src)) return false;
      seen.add(attachment.src);
      return true;
    });
  }, [sortedAnswers]);
  const answerImages = useMemo(() => answerAttachments.filter((attachment) => attachment.isImage).map(({ src, name }) => ({ src, name })), [answerAttachments]);
  const openAnswerImage = (src: string) => {
    const index = answerImages.findIndex((item) => item.src === src);
    if (index >= 0) setAnswerLightbox(index);
  };
  const flowPerson = (value?: string | null, empty = "ยังไม่ระบุ") => staff(value) ?? value ?? empty;
  const renderFlowTracking = (item: CrmTicket | CrmFlowState, compact = false) => {
    const { owner, assignee, developer, closed, waitingTest } = crmFlowInfo(item);
    // คนที่ถืองานก่อนหน้า (จากประวัติ Flow Tracking) — แสดงในบรรทัดสรุปเมื่อไม่ใช่คนที่ถืออยู่ตอนนี้
    const recorded = previousQa?.[item.jobNo.toUpperCase()] ?? previousQa?.[item.jobNo] ?? null;
    const previousHolder = recorded && !sameStaff(recorded, assignee) ? recorded : null;
    const step = (key: "owner" | "assignee" | "developer", label: string, value: string | null | undefined, empty: string) => {
      const isCurrent = key === "assignee" && !closed && Boolean(value);
      const state = isCurrent ? "current" : value ? "done" : "waiting";
      return <div className={`crm-flow-step ${state}`} title={`${label}: ${flowPerson(value, empty)}`} aria-current={isCurrent ? "step" : undefined}><small>{label}</small><strong>{flowPerson(value, empty)}</strong></div>;
    };
    // บรรทัดสรุปตอบ "ตอนนี้อยู่กับใคร" ก่อน แล้วบอกว่าคนนั้นอยู่ในช่องไหนของ Ticket / สิ่งที่รออยู่
    const notes = [
      sameStaff(assignee, owner) ? "เจ้าของเรื่อง" : sameStaff(assignee, developer) ? "Development" : "",
      waitingTest ? "รอทดสอบ" : "",
      previousHolder ? `ก่อนหน้า ${flowPerson(previousHolder)}` : "",
    ].filter(Boolean);
    return <div className={`crm-flow-tracking${compact ? " compact" : ""}`} aria-label={`Flow Tracking ${item.jobNo}: เจ้าของเรื่อง ${flowPerson(owner)} · Assign To ${flowPerson(assignee)} · Development ${flowPerson(developer, "ไม่ระบุ")}${closed ? " · ปิดงานแล้ว" : ` · ตอนนี้อยู่กับ ${flowPerson(assignee)}`}`}>
      <div className="crm-flow-steps">
        {step("owner", "เจ้าของเรื่อง", owner, "ไม่ระบุ")}
        <span className="crm-flow-arrow" aria-hidden="true">→</span>
        {step("assignee", "Assign To", assignee, "ไม่ระบุ")}
        <span className="crm-flow-arrow" aria-hidden="true">→</span>
        {step("developer", "Development", developer, "ไม่ระบุ")}
      </div>
      {!compact && <small className="crm-flow-current"><span className="material-symbols-outlined" aria-hidden="true">{closed ? "task_alt" : waitingTest ? "hourglass_top" : "person_pin_circle"}</span>{closed ? "ปิดงานแล้ว" : <span>ตอนนี้อยู่กับ <strong>{flowPerson(assignee)}</strong>{notes.length > 0 && <> · {notes.join(" · ")}</>}</span>}</small>}
    </div>;
  };
  const sortedRows = useMemo(() => {
    const contactTime = (value?: string | null) => toUtcDate(value)?.getTime() ?? Number.POSITIVE_INFINITY;
    return [...rows].sort((a, b) => { const left = contactTime(a.contactDate), right = contactTime(b.contactDate); return left === right ? 0 : left < right ? -1 : 1; });
  }, [rows]);
  // Finish/Close อยู่คอลัมน์เดียวกันบน Board; List ยังคงแยกส่วนปิดงานไว้ด้านล่าง
  const activeRows = useMemo(() => sortedRows.filter((item) => boardBucket(item.status) !== "Closed"), [sortedRows]);
  const closedRows = useMemo(() => sortedRows.filter((item) => boardBucket(item.status) === "Closed"), [sortedRows]);
  const boardColumns = useMemo(() => [
    { key: "Open", label: "Open", items: sortedRows.filter((item) => boardBucket(item.status) === "Open") },
    { key: "Continue", label: "Continue", items: sortedRows.filter((item) => boardBucket(item.status) === "Continue") },
    { key: "Test", label: "Test", items: sortedRows.filter((item) => boardBucket(item.status) === "Test") },
    { key: "Closed", label: "Finish / Close", items: sortedRows.filter((item) => boardBucket(item.status) === "Closed") },
  ], [sortedRows]);
  const ticketTableHead = <thead><tr><th>Job No.</th><th>เรื่อง</th><th>สถานะ</th><th>Service / Product</th><th>ผู้แจ้ง / สาขา</th><th>Flow Tracking</th><th>วันที่ติดต่อ</th><th>อายุงาน</th></tr></thead>;
  const renderTicketRow = (item: CrmTicket) => { const days = ageDays(item.contactDate); return <tr key={item.jobNo} className={`crm-row crm-row-${boardBucket(item.status) === "Open" ? "open" : boardBucket(item.status) === "Closed" ? "closed" : "progress"}`}><td data-label="Job No."><button className="crm-job-link" type="button" onClick={() => setSelectedTicket(item)} aria-label={`เปิดรายละเอียด ${item.jobNo}`}><strong className="crm-job-no">{item.jobNo}</strong><span className="material-symbols-outlined" aria-hidden="true">open_in_new</span></button></td><td data-label="เรื่อง"><div className="crm-subject"><strong title={item.subject || undefined}>{item.subject || "ไม่มี Subject"}</strong></div></td><td data-label="สถานะ"><Badge tone={statusTone(item.status)}>{item.status || "Unknown"}</Badge></td><td data-label="Service / Product"><div className="crm-service"><span>{item.serviceType || "-"}</span><small className="crm-product-chip">{item.product || "ไม่ระบุ Product"}</small></div></td><td data-label="ผู้แจ้ง / สาขา"><span className="crm-reporter"><strong title={staff(item.member) ?? undefined}><span className="material-symbols-outlined" aria-hidden="true">person</span><span>{staff(item.member) ?? "-"}</span></strong><small title={item.branch || undefined}><span className="material-symbols-outlined" aria-hidden="true">storefront</span><span>{item.branch || "ไม่ระบุสาขา"}</span></small></span></td><td data-label="Flow Tracking">{renderFlowTracking(item)}</td><td data-label="วันที่ติดต่อ">{listDate(item.contactDate)}</td><td data-label="อายุงาน"><span className="crm-age"><span className={`crm-age-pill crm-age-${ageLevel(days)}`}>{days === null ? "-" : days === 0 ? "วันนี้" : <><strong>{days}</strong> วัน</>}</span><small>ตอบล่าสุด {displayDate(item.lastReplyAt)}</small></span></td></tr>; };
  const openDefectLinkDialog = () => { setLinkDefectSearch(""); setLinkDefectError(""); setDefectLinkDialogOpen(true); };
  const unlinkLinkedDefect = async () => {
    if (!selectedJobNo || !linkedDefect || linkedDefectSaving) return;
    if (!(await confirmDialog({ title: "ยกเลิกการเชื่อมโยง Defect", message: `ยกเลิกการเชื่อมโยง ${linkedDefect.defectCode} ออกจาก Ticket ${selectedJobNo} หรือไม่`, confirmLabel: "ยกเลิกการเชื่อมโยง" }))) return;
    setLinkedDefectSaving(true); setLinkedDefectError("");
    try {
      await apiFetch(`/crm/tickets/${encodeURIComponent(selectedJobNo)}/defect`, { method: "DELETE" });
      setLinkedDefect(null);
      notify(`ยกเลิกการเชื่อมโยง Defect ${linkedDefect.defectCode} แล้ว`, "success");
    } catch (reason) {
      setLinkedDefectError(reason instanceof Error ? reason.message : "ยกเลิกการเชื่อมโยง Defect ไม่สำเร็จ");
    } finally { setLinkedDefectSaving(false); }
  };
  const openCreateDefectDialog = () => {
    if (!selectedTicket || !contextProjectId) return;
    setCreateDefectTitle(ticketDetail?.ticket.subject || selectedTicket.subject || `CRM Ticket ${selectedTicket.jobNo}`);
    setCreateDefectSeverity("Medium");
    setCreateDefectDescription(ticketDescription);
    setCreateDefectError("");
    setCreateDefectDialogOpen(true);
  };
  const createDefectFromCrm = async () => {
    if (!selectedJobNo || !contextProjectId || createDefectSaving || !createDefectTitle.trim()) return;
    setCreateDefectSaving(true); setCreateDefectError("");
    try {
      const created = await apiFetch<CrmCreatedDefect>(`/crm/tickets/${encodeURIComponent(selectedJobNo)}/create-defect`, {
        method: "POST",
        json: { projectId: contextProjectId, title: createDefectTitle.trim(), severity: createDefectSeverity, description: createDefectDescription.trim() || null },
      });
      setLinkedDefect(created);
      setCreateDefectDialogOpen(false);
    } catch (reason) {
      setCreateDefectError(reason instanceof ApiError && reason.code === "CRM_TICKET_ALREADY_LINKED"
        ? "Ticket นี้ถูกเชื่อมกับ Defect แล้ว กรุณาโหลดรายละเอียดใหม่"
        : reason instanceof Error ? reason.message : "สร้าง Defect จาก CRM Ticket ไม่สำเร็จ");
    } finally { setCreateDefectSaving(false); }
  };
  const addReplyFiles = (incoming: File[]) => {
    if (incoming.length === 0) return;
    const allowed = /\.(jpe?g|png|xlsx?|docx?|pdf)$/i;
    const bad = incoming.find((file) => !allowed.test(file.name));
    if (bad) { setReplyFileError(`ไฟล์ ${bad.name} ไม่รองรับ — แนบได้เฉพาะ .jpg, .jpeg, .png, .xlsx, .xls, .doc, .docx, .pdf`); return; }
    const large = incoming.find((file) => file.size > CRM_FILE_MAX_BYTES);
    if (large) { setReplyFileError(`ไฟล์ ${large.name} มีขนาดเกิน 5 MB`); return; }
    if (replyFiles.length + incoming.length > CRM_FILE_MAX) { setReplyFileError(`แนบไฟล์ได้สูงสุด ${CRM_FILE_MAX} ไฟล์`); return; }
    if ([...replyFiles.map((item) => item.file), ...incoming].reduce((sum, file) => sum + file.size, 0) > CRM_FILE_TOTAL_BYTES) { setReplyFileError("ขนาดไฟล์แนบรวมต้องไม่เกิน 25 MB"); return; }
    setReplyFileError("");
    setReplyFiles((current) => [...current, ...incoming.map((file) => ({ id: `${file.name}-${file.size}-${file.lastModified}-${Math.random().toString(36).slice(2)}`, file, preview: /^image\//.test(file.type) ? URL.createObjectURL(file) : undefined }))]);
  };
  const removeReplyFile = (id: string) => setReplyFiles((current) => current.filter((item) => { if (item.id === id && item.preview) URL.revokeObjectURL(item.preview); return item.id !== id; }));
  const saveTicketUpdate = async () => {
    const current = ticketDetail?.ticket ?? selectedTicket;
    const message = replyMessage.trim();
    const draftAssignee = assigneeValue.trim();
    const draftServiceType = serviceValue.trim();
    const draftProductId = productValue.trim();
    const assigneeChanged = Boolean(draftAssignee && draftAssignee !== (current?.assignee ?? "").trim());
    const serviceChanged = Boolean(draftServiceType && draftServiceType !== serviceOriginalValue.trim());
    const productChanged = Boolean(draftProductId && draftProductId !== productOriginalValue.trim());
    if (!current || ticketClosed || !selectedJobNo || replySaving || (!message && !replyStatus && !replyToOwner && !assigneeChanged && !serviceChanged && !productChanged && replyFiles.length === 0)) return;
    if (replyToOwner && !(await confirmDialog({ title: "ส่งกลับเจ้าของเรื่อง", message: `เปลี่ยนผู้รับผิดชอบ ${selectedJobNo} เป็นเจ้าของเรื่อง${current.owner ? ` (${staff(current.owner)})` : ""} — Ticket จะย้ายไปแท็บ "ส่งต่อแล้ว" จนกว่าจะปิดงาน`, confirmLabel: "ส่งกลับเจ้าของเรื่อง" }))) return;
    setReplySaving(true); setReplyError("");
    try {
      const body = new FormData();
      if (message) body.append("message", message);
      if (replyStatus) body.append("status", replyStatus);
      if (replyToOwner) body.append("assignToOwner", "true");
      if (!replyToOwner && assigneeChanged) body.append("assignToStaffCode", draftAssignee);
      if (serviceChanged) body.append("serviceTypeId", draftServiceType);
      if (productChanged) body.append("productId", draftProductId);
      if (current.status) body.append("expectedStatus", current.status);
      if (current.assignee) body.append("expectedAssignee", current.assignee);
      replyFiles.forEach((item) => body.append("files", item.file, item.file.name));
      const result = await apiFetch<CrmTicketMutation>(`/crm/tickets/${encodeURIComponent(selectedJobNo)}`, {
        method: "PATCH",
        form: body,
      });
      const updatedAssignee = result.assignee || (assigneeChanged ? draftAssignee : current.assignee || "");
      const updatedServiceType = serviceOptions.find((item) => item.id === draftServiceType)?.name || current.serviceType || "";
      const updatedProduct = productOptions.find((item) => item.id === draftProductId)?.name || current.product || "";
      setReplyMessage(""); setReplyStatus(""); setReplyToOwner(false); setReplyFileError(""); setReplyFiles((current) => { current.forEach((item) => { if (item.preview) URL.revokeObjectURL(item.preview); }); return []; }); setAssigneeValue(updatedAssignee); setAssigneeQuery(""); setAssigneeSearchOpen(false); setServiceValue(serviceChanged ? draftServiceType : serviceOriginalValue); setServiceOriginalValue(serviceChanged ? draftServiceType : serviceOriginalValue); setServiceQuery(""); setServiceSearchOpen(false); setProductValue(productChanged ? draftProductId : productOriginalValue); setProductOriginalValue(productChanged ? draftProductId : productOriginalValue); setProductQuery(""); setProductSearchOpen(false);
      setReload((value) => value + 1);
      if (replyToOwner) {
        // Ticket ไม่ได้อยู่ในขอบเขตงานของผู้ใช้แล้ว (Assignto เปลี่ยน) — ปิดรายละเอียดแทนการโหลดซ้ำซึ่งจะได้ 404
        notify(`ส่ง ${selectedJobNo} กลับเจ้าของเรื่องแล้ว`, "success");
        setSelectedTicket(null);
        return;
      }
      notify(`บันทึก ${selectedJobNo} ไป CRM แล้ว`, "success");
      setSelectedTicket((previous) => previous ? { ...previous, status: result.status, assignee: updatedAssignee, serviceType: updatedServiceType, product: updatedProduct } : previous);
      setTicketDetail((previous) => previous ? { ...previous, ticket: { ...previous.ticket, status: result.status, assignee: updatedAssignee, serviceType: updatedServiceType, product: updatedProduct } } : previous);
      if (!assigneeChanged && !serviceChanged && !productChanged) setDetailReload((value) => value + 1);
    } catch (reason: unknown) {
      setReplyError(crmErrorMessage(reason, "บันทึกไป CRM ไม่สำเร็จ กรุณาลองใหม่"));
      if (reason instanceof ApiError && reason.code === "CRM_CONFLICT") setDetailReload((value) => value + 1);
    } finally { setReplySaving(false); }
  };
  const linkSelectedDefect = async (defectId: string) => {
    if (!selectedJobNo || linkDefectSaving) return;
    setLinkDefectSaving(true); setLinkDefectError("");
    try {
      const linked = await apiFetch<CrmLinkedDefect>(`/crm/tickets/${encodeURIComponent(selectedJobNo)}/link-defect`, { method: "POST", json: { defectId } });
      setLinkedDefect(linked); setDefectLinkDialogOpen(false);
    } catch (reason) {
      setLinkDefectError(reason instanceof Error ? reason.message : "เชื่อม CRM Ticket กับ Defect ไม่สำเร็จ");
    } finally { setLinkDefectSaving(false); }
  };
  const probeConnection = async () => {
    if (!configured || probeLoading) return;
    setProbeLoading(true); setProbeMessage(""); setProbeError(false);
    try {
      await apiFetch("/crm/connection/test", { method: "POST" });
      setProbeMessage("CRM ตอบกลับปกติ");
    } catch (reason) {
      setProbeError(true);
      setProbeMessage(crmErrorMessage(reason, "ทดสอบการเชื่อมต่อ CRM ไม่สำเร็จ"));
    } finally { setProbeLoading(false); }
  };

  return (
    <section className="crm-page" aria-label="CRM work queue">
      <div className="crm-connection-card card">
        <div className="crm-hero-main"><span className="crm-hero-icon material-symbols-outlined" aria-hidden="true">support_agent</span><div className="crm-connection-copy"><div className="crm-connection-title"><h2>งาน CRM ของฉัน</h2><Badge tone={connectionError ? "red" : configured ? "green" : "yellow"}>{connectionBadge}</Badge></div><p>{connectionError ? "ยังตรวจสอบสถานะ CRM ไม่ได้ กรุณาลองตรวจสอบอีกครั้ง" : configured ? `แสดงเฉพาะ Ticket ที่มอบหมายให้ ${staff(connection?.username) ?? "บัญชี CRM ของคุณ"}` : "ข้อมูลจะแยกตามบัญชี CRM ของผู้ Login — ตั้งค่าได้จากปุ่ม บัญชี CRM ของฉัน ด้านขวาบน"}</p></div></div>
        <div className="crm-connection-meta"><div className="crm-account"><span className="crm-account-avatar" aria-hidden="true">{initials(connection?.username)}</span><div><span>CRM Username</span><strong>{staff(connection?.username) ?? "ยังไม่ได้ตั้งค่า"}</strong>{connection?.updatedAt && <small>อัปเดตบัญชีล่าสุด {displayDate(connection.updatedAt)}</small>}</div></div>{configured && <div className="crm-connection-actions"><button className="btn" type="button" onClick={() => void probeConnection()} disabled={probeLoading}>{probeLoading ? "กำลังทดสอบ..." : "ทดสอบการเชื่อมต่อ"}</button>{probeMessage && <small className={probeError ? "crm-probe-error" : "crm-probe-success"} role="status">{probeMessage}</small>}</div>}</div>
      </div>
      {statusMessage && <div className="crm-inline-alert" role="status"><span className="material-symbols-outlined" aria-hidden="true">info</span><span>{statusMessage}</span>{connectionError ? <button className="crm-configure-button" type="button" onClick={() => setReload((value) => value + 1)}>ลองตรวจสอบอีกครั้ง</button> : connection && !configured && onConfigure && <button className="crm-configure-button" type="button" onClick={onConfigure}>ตั้งค่าบัญชี CRM ของฉัน</button>}</div>}
      <div className="crm-kpi-grid" aria-label="สรุปงาน CRM">
        {[
          { key: "total", label: "Ticket ทั้งหมด", value: summary.total, hint: "ตามตัวกรองปัจจุบัน", icon: "confirmation_number" },
          { key: "open", label: "Open", value: summary.open, hint: "รอเริ่มดำเนินการ", icon: "inbox" },
          { key: "progress", label: "กำลังดำเนินการ", value: summary.inProgress, hint: "Continue ถึง Test", icon: "autorenew" },
          { key: "closed", label: "ปิดงานแล้ว", value: summary.closed, hint: "Finish หรือ Close", icon: "task_alt" },
        ].map((kpi) => { const share = kpi.key === "total" || summary.total === 0 ? null : Math.round((kpi.value / summary.total) * 100); return <article className={`crm-kpi crm-kpi-${kpi.key} card`} key={kpi.key}><div className="crm-kpi-head"><span className="crm-kpi-label">{kpi.label}</span><span className="crm-kpi-icon material-symbols-outlined" aria-hidden="true">{kpi.icon}</span></div><strong>{kpi.value.toLocaleString()}</strong><small>{kpi.hint}{share !== null && <> · {share}%</>}</small>{share !== null && <span className="crm-kpi-bar" aria-hidden="true"><span style={{ width: `${share}%` }} /></span>}</article>; })}
      </div>
      <div className="crm-filter-card card">
        <div className="crm-filter-heading"><div className="crm-filter-title"><span className="crm-section-icon material-symbols-outlined" aria-hidden="true">tune</span><div><h3>ค้นหาและกรองรายการ</h3><p>ค้นหาจาก Job No., Subject, ผู้แจ้ง, Service หรือ Product</p></div></div><button className="btn" type="button" onClick={() => setReload((value) => value + 1)} disabled={loading}><span className="material-symbols-outlined" aria-hidden="true">refresh</span> รีเฟรช</button></div>
        <div className="crm-filter-row">
          <div className="crm-range-group" role="group" aria-labelledby="crm-range-label"><span id="crm-range-label">ช่วงวันที่ติดต่อ</span><div className="crm-range-presets">{RANGE_PRESETS.map((days) => <button key={days} type="button" className={rangePreset === days ? "active" : ""} aria-pressed={rangePreset === days} onClick={() => { setRangePreset(days); setFrom(daysAgo(days)); setTo(dateInput(new Date())); setPage(1); }}>{days === 0 ? "วันนี้" : `${days} วัน`}</button>)}<button type="button" className={rangePreset === "custom" ? "active" : ""} aria-pressed={rangePreset === "custom"} aria-expanded={rangePreset === "custom"} aria-controls="crm-custom-range" onClick={() => setRangePreset("custom")}><span className="material-symbols-outlined" aria-hidden="true">date_range</span>กำหนดเอง</button></div><small>{dayLabel(from)} – {dayLabel(to)}</small></div>
          <form className="crm-jobno-search" role="search" onSubmit={(event) => { event.preventDefault(); void openJobNo(); }}><label>ค้นหา Job No.<span className="crm-jobno-control"><input type="search" value={jobNoQuery} onChange={(event) => { setJobNoQuery(event.target.value); setJobNoError(""); }} placeholder="เช่น BHD-10" autoComplete="off" spellCheck={false} aria-invalid={jobNoError ? true : undefined} aria-describedby="crm-jobno-hint" /><button type="submit" aria-label={jobNoBusy ? "กำลังค้นหา Job No." : "เปิดรายละเอียด Ticket ตาม Job No."} disabled={!jobNoQuery.trim() || jobNoBusy}><span className={`material-symbols-outlined${jobNoBusy ? " crm-jobno-spin" : ""}`} aria-hidden="true">{jobNoBusy ? "progress_activity" : "search"}</span></button></span></label>{jobNoError ? <small id="crm-jobno-hint" className="crm-jobno-error" role="alert"><span className="material-symbols-outlined" aria-hidden="true">error</span>{jobNoError}</small> : <small id="crm-jobno-hint">ค้นได้ทุกช่วงวันที่ · กด Enter</small>}</form>
          <label>สถานะ<select value={status} onChange={(event) => { setStatus(event.target.value); setPage(1); }}><option value="">ทุกสถานะ</option>{CRM_STATUSES.map((value) => <option key={value} value={value}>{value}</option>)}</select></label>
          <label>แสดงต่อหน้า<select value={pageSize} onChange={(event) => { setPageSize(Number(event.target.value)); setPage(1); }}><option value={25}>25</option><option value={50}>50</option><option value={100}>100</option></select></label>
        </div>
        {rangePreset === "custom" && <div className="crm-custom-range" id="crm-custom-range"><div className="crm-custom-range-head"><span className="material-symbols-outlined" aria-hidden="true">event</span><div><strong>ระบุช่วงวันที่เอง</strong><small>แสดงเป็น วัน/เดือน/ปี (พ.ศ.) · ไม่เกิน {CRM_MAX_RANGE_DAYS} วัน</small></div></div><div className="crm-custom-range-fields"><CrmDateField label="วันที่เริ่มต้น" value={from} min={shiftDate(to, -CRM_MAX_RANGE_DAYS)} max={to} onChange={(value) => { setFrom(value); setPage(1); }} /><span className="crm-custom-range-sep" aria-hidden="true">–</span><CrmDateField label="วันที่สิ้นสุด" value={to} min={from} max={[shiftDate(from, CRM_MAX_RANGE_DAYS), dateInput(new Date())].sort()[0]} onChange={(value) => { setTo(value); setPage(1); }} /></div></div>}
      </div>
      {stale && <div className="crm-stale-alert" role="status"><span className="material-symbols-outlined" aria-hidden="true">schedule</span><span><strong>ข้อมูลอาจเก่า</strong> โหลดล่าสุด {displayDate(lastFetchedAt)} แล้ว</span><button className="btn" type="button" onClick={() => setReload((value) => value + 1)} disabled={loading}>รีเฟรชข้อมูล</button></div>}
      <div className="crm-list-card card">
        <div className="crm-list-heading"><div><h3>รายการ Ticket</h3><p>{loading ? "กำลังโหลดข้อมูลจาก CRM..." : `แสดง ${rows.length.toLocaleString()} จาก ${total.toLocaleString()} รายการ`}</p></div><div className="crm-list-heading-actions">{lastFetchedAt && <small>ดึงข้อมูลล่าสุด {displayDate(lastFetchedAt)}</small>}{canEditCrm && configured && <button type="button" className="btn primary crm-create-ticket-button" onClick={() => setCreateTicketOpen(true)}><span className="material-symbols-outlined" aria-hidden="true">add</span>สร้าง Ticket ใหม่</button>}<div className="crm-view-switcher" role="group" aria-label="รูปแบบการแสดงผล"><button type="button" className={`btn ${viewMode === "list" ? "active" : ""}`} aria-pressed={viewMode === "list"} onClick={() => setViewMode("list")}>List</button><button type="button" className={`btn ${viewMode === "board" ? "active" : ""}`} aria-pressed={viewMode === "board"} onClick={() => setViewMode("board")}>Board</button></div></div></div>
        <div className="crm-scope-tabs" role="group" aria-label="กลุ่ม Ticket">
          {([
            { key: "mine", label: "ส่งมาหาฉัน", hint: "Assign ให้ฉันอยู่ตอนนี้", icon: "assignment_ind" },
            { key: "previous", label: "ส่งต่อแล้ว", hint: "อยู่กับคนอื่นแล้ว · ยังไม่ปิดงาน", icon: "forward_to_inbox" },
          ] as const).map((tab) => <button key={tab.key} type="button" aria-pressed={ticketScope === tab.key} className={`crm-scope-tab${ticketScope === tab.key ? " active" : ""}`} onClick={() => { if (ticketScope !== tab.key) { setTicketScope(tab.key); setPage(1); } }}><span className="material-symbols-outlined" aria-hidden="true">{tab.icon}</span><span className="crm-scope-tab-text"><strong>{tab.label}</strong><small>{tab.hint}</small></span><span className="crm-scope-count" aria-label={`${scopeCounts?.[tab.key] ?? 0} รายการ`}>{scopeCounts ? scopeCounts[tab.key].toLocaleString() : "-"}</span></button>)}
        </div>
        {error ? <div className="crm-state crm-state-error" role="alert"><span className="material-symbols-outlined" aria-hidden="true">error</span><div><strong>{crmErrorTitle(errorCode)}</strong><p>{error}</p><button className="btn" type="button" onClick={() => setReload((value) => value + 1)}>ลองอีกครั้ง</button></div></div>
          : loading ? <div className="crm-state" role="status"><span className="spinner" aria-hidden="true" /><p>กำลังโหลดรายการ Ticket...</p></div>
            : rows.length === 0 ? <div className="crm-state"><span className="material-symbols-outlined" aria-hidden="true">{ticketScope === "previous" ? "forward_to_inbox" : "inbox"}</span><p>{ticketScope === "previous" ? "ไม่มี Ticket ที่ส่งต่อแล้วและยังไม่ปิดงาน" : "ไม่พบ Ticket ตามตัวกรองนี้"}</p><small>{ticketScope === "previous" ? "งานที่คุณเกี่ยวข้องแต่อยู่กับคนอื่นแล้วจะแสดงที่นี่จนกว่าจะ Finish / Close" : "ลองเปลี่ยนช่วงวันที่ สถานะ หรือคำค้นหา"}</small></div>
              : <><div className={`table-wrap crm-table-wrap ${viewMode === "board" ? "crm-board-hidden" : ""}`} aria-hidden={viewMode === "board"}>{activeRows.length === 0 ? <div className="crm-active-empty"><span className="material-symbols-outlined" aria-hidden="true">celebration</span>ไม่มีงานที่ต้องดำเนินการในหน้านี้</div> : <table className="table-cards crm-table">{ticketTableHead}<tbody>{activeRows.map(renderTicketRow)}</tbody></table>}</div><div className={`crm-board ${viewMode === "list" ? "crm-board-hidden" : ""}`} aria-hidden={viewMode === "list"}>{boardColumns.map((column) => <section className="crm-board-column" data-bucket={column.key} key={column.key} aria-label={`${column.label} ${column.items.length} รายการ`}><div className="crm-board-column-head"><strong>{column.label}</strong><span>{column.items.length}</span></div><div className="crm-board-column-body">{column.items.length === 0 ? <small className="crm-board-empty">ไม่มี Ticket</small> : column.items.map((item) => <button type="button" className="crm-board-ticket" data-tone={statusTone(item.status)} key={item.jobNo} onClick={() => setSelectedTicket(item)} aria-label={`เปิดรายละเอียด ${item.jobNo} ${item.subject || ""}`} title={item.subject || undefined}><span className="crm-board-ticket-top"><strong>{item.jobNo}</strong><Badge tone={statusTone(item.status)}>{item.status || "Unknown"}</Badge></span><span className="crm-board-ticket-subject">{item.subject || "ไม่มี Subject"}</span>{renderFlowTracking(item, true)}<span className="crm-board-ticket-meta"><span><span className="material-symbols-outlined" aria-hidden="true">category</span><span>{item.serviceType || "-"}</span></span><span><span className="material-symbols-outlined" aria-hidden="true">person</span><span>{staff(item.member) ?? "-"}</span></span><span className="crm-board-ticket-age"><span className="material-symbols-outlined" aria-hidden="true">schedule</span><span>{ageLabel(item.contactDate)}</span></span></span></button>)}</div></section>)}</div>{closedRows.length > 0 && <details className={`crm-closed-section ${viewMode === "board" ? "crm-board-hidden" : ""}`}><summary><span className="crm-closed-icon material-symbols-outlined" aria-hidden="true">task_alt</span><span className="crm-closed-title"><strong>ปิดงานแล้ว</strong><small>สถานะ Finish / Close · แยกจากงานที่ต้องดำเนินการ</small></span><span className="crm-closed-count">{closedRows.length.toLocaleString()} รายการ</span><span className="crm-closed-chevron material-symbols-outlined" aria-hidden="true">expand_more</span></summary><div className="table-wrap crm-table-wrap crm-closed-table"><table className="table-cards crm-table">{ticketTableHead}<tbody>{closedRows.map(renderTicketRow)}</tbody></table></div></details>}<div className="crm-pagination"><small>หน้า {page} จาก {pageCount}</small><div><button className="btn" type="button" disabled={page <= 1 || loading} onClick={() => setPage((value) => Math.max(1, value - 1))}>ก่อนหน้า</button><button className="btn" type="button" disabled={page >= pageCount || loading} onClick={() => setPage((value) => Math.min(pageCount, value + 1))}>ถัดไป</button></div></div></>}
      </div>
      {ticketView && <ModalShell labelledBy="crm-ticket-detail-title" boxClassName="modal-box crm-ticket-modal" onDismiss={() => setSelectedTicket(null)}>
        <div className="modal-head crm-ticket-head"><div className="crm-ticket-head-main"><span className="crm-eyebrow">รายละเอียด Ticket จาก CRM</span><div className="crm-ticket-head-title"><h2 id="crm-ticket-detail-title">{ticketView.jobNo}</h2><Badge tone={statusTone(ticketView.status)}>{ticketView.status || "Unknown"}</Badge>{(() => { const days = ageDays(ticketView.contactDate); return <span className={`crm-age-pill crm-age-${ageLevel(days)}`} title="อายุงานนับจากวันที่ติดต่อ"><span className="material-symbols-outlined" aria-hidden="true">schedule</span>{days === null ? "-" : days === 0 ? "วันนี้" : <><strong>{days}</strong> วัน</>}</span>; })()}</div><p>{ticketView.subject || "ไม่มี Subject"}</p></div><button type="button" aria-label="ปิดรายละเอียด Ticket" onClick={() => setSelectedTicket(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
        <div className="crm-ticket-body">
          <div className="crm-ticket-main">
            {canEditSelectedTicket && <section className="crm-reply" aria-labelledby="crm-reply-title">
              <div className="crm-reply-head"><h3 id="crm-reply-title"><span className="material-symbols-outlined" aria-hidden="true">edit_note</span>อัปเดต Ticket</h3><small>บันทึกไป CRM ทันที · ข้อความจะเป็นแถวใหม่ในประวัติการติดต่อ</small></div>
              <label className="crm-reply-text"><textarea aria-label="เพิ่มประวัติการติดต่อ" rows={3} maxLength={CRM_REPLY_MAX} value={replyMessage} disabled={replySaving} placeholder="เพิ่มประวัติการติดต่อ เช่น สิ่งที่ตรวจสอบ/แก้ไข หรือสิ่งที่ต้องให้เจ้าของเรื่องทำต่อ" onChange={(event) => setReplyMessage(event.target.value)} onPaste={(event) => { const images = Array.from(event.clipboardData.files).filter((file) => /^image\//.test(file.type)); if (images.length > 0) { event.preventDefault(); addReplyFiles(images); } }} onKeyDown={(event) => { if ((event.ctrlKey || event.metaKey) && event.key === "Enter") { event.preventDefault(); void saveTicketUpdate(); } }} /></label>
              <div className="crm-ct-files crm-reply-files">
                <div className="crm-ct-files-row"><button type="button" className="btn crm-ct-attach" onClick={() => replyFileInputRef.current?.click()} disabled={replySaving || replyFiles.length >= CRM_FILE_MAX} title={`แนบได้สูงสุด ${CRM_FILE_MAX} ไฟล์ ไฟล์ละไม่เกิน 5 MB`} aria-describedby="crm-reply-files-hint"><span className="material-symbols-outlined" aria-hidden="true">attach_file</span>แนบไฟล์ ({replyFiles.length}/{CRM_FILE_MAX})</button><input ref={replyFileInputRef} type="file" accept={CRM_FILE_ACCEPT} multiple hidden onChange={(event) => { addReplyFiles(Array.from(event.target.files ?? [])); event.target.value = ""; }} />
                  {replyFiles.length > 0 ? <ul className="crm-ct-file-list">{replyFiles.map((item) => <li key={item.id}>{item.preview ? <img src={item.preview} alt="" /> : <span className="material-symbols-outlined" aria-hidden="true">description</span>}<span className="crm-ct-file-name" title={item.file.name}>{item.file.name}</span><button type="button" aria-label={`นำไฟล์ ${item.file.name} ออก`} onClick={() => removeReplyFile(item.id)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></li>)}</ul> : <small id="crm-reply-files-hint" className="crm-ct-files-hint">รูป/เอกสาร ≤ {CRM_FILE_MAX} ไฟล์ ไฟล์ละ 5 MB · วางรูปในช่องข้อความได้</small>}
                </div>
              </div>
              {replyFileError && <small className="crm-reply-file-error" role="alert">{replyFileError}</small>}
              <div className="crm-reply-controls">
                <label className="crm-reply-status">สถานะ<select value={replyStatus} disabled={replySaving} onChange={(event) => setReplyStatus(event.target.value)}><option value="">คงเดิม ({ticketView.status || "-"})</option>{CRM_STATUSES.filter((value) => value !== ticketView.status).map((value) => <option key={value} value={value}>{value}</option>)}</select></label>
                <label className="crm-reply-owner"><input type="checkbox" checked={replyToOwner} disabled={replySaving || !ticketView.owner} onChange={(event) => setReplyToOwner(event.target.checked)} /><span>ส่งกลับเจ้าของเรื่อง{ticketView.owner ? <small>Assign เป็น {staff(ticketView.owner)}</small> : <small>ไม่มีข้อมูลเจ้าของเรื่อง</small>}</span></label>
                 <div className="crm-reply-submit"><small aria-live="polite">{replyMessage.length}/{CRM_REPLY_MAX}</small><button type="button" className="btn primary" disabled={replySaving || (!replyMessage.trim() && !replyStatus && !replyToOwner && !pendingAssigneeChanged && !pendingServiceChanged && !pendingProductChanged && replyFiles.length === 0)} onClick={() => void saveTicketUpdate()}>{replySaving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">send</span> บันทึกไป CRM</>}</button></div>
              </div>
              {replyError && <div className="crm-inline-alert" role="alert"><span className="material-symbols-outlined" aria-hidden="true">error</span><span>{replyError}</span></div>}
            </section>}
            {canEditCrm && ticketClosed && <div className="crm-detail-note" role="status"><span className="material-symbols-outlined" aria-hidden="true">lock</span><div><strong>Ticket ปิดแล้ว</strong><p>สถานะ Close ไม่สามารถแก้ไขข้อมูลหรือเพิ่มประวัติการติดต่อได้</p></div></div>}
            {ticketDetailLoading ? <div className="crm-detail-loading" role="status"><span className="spinner" aria-hidden="true" /> กำลังโหลดรายละเอียดและประวัติจาก CRM...</div>
              : ticketDetailError ? <div className="crm-detail-note crm-detail-note-error" role="alert"><span className="material-symbols-outlined" aria-hidden="true">error</span><div><strong>{crmErrorTitle(ticketDetailErrorCode)}</strong><p>{ticketDetailError}</p><button className="btn" type="button" onClick={() => setDetailReload((value) => value + 1)}>ลองโหลดรายละเอียดอีกครั้ง</button></div></div>
                : ticketDetail && <><section className="crm-flow-history" aria-label="ประวัติ Flow Tracking">
  <div className="crm-flow-history-head"><h4>ประวัติ Flow Tracking</h4><small>{flowHistory.length.toLocaleString()} เหตุการณ์</small></div>
  {flowHistoryLoading ? <p className="crm-flow-history-empty">กำลังโหลดประวัติ Flow...</p> : flowHistoryError ? <p className="crm-flow-history-empty">{flowHistoryError}</p> : flowHistory.length === 0 ? <p className="crm-flow-history-empty">ยังไม่มีประวัติการติดตามในฐานข้อมูล</p> : <ol className="crm-flow-history-list">{flowHistory.map((event, index) => <li key={`${event.timestamp}-${event.action}-${index}`}><div><strong>{event.action === "FlowStarted" ? "เริ่มติดตาม Flow" : "เปลี่ยนเส้นทางงาน"}</strong><time>{displayDate(event.timestamp)}</time></div><span className="crm-flow-history-route">เจ้าของเรื่อง {flowPerson(event.after.support)} → Assign To {flowPerson(event.after.qa)} → Development {flowPerson(event.after.developer, "ไม่ระบุ")}</span><small>{event.actorName ? `บันทึกโดย ${event.actorName}` : "บันทึกโดยระบบ"} · สถานะ {event.after.status || "ไม่ระบุ"}</small></li>)}</ol>}
</section><section className="crm-detail-section"><h3><span className="material-symbols-outlined" aria-hidden="true">description</span>รายละเอียด</h3><p className="crm-detail-description">{ticketDescription || "ไม่มี Description"}</p></section><section className="crm-detail-section"><div className="crm-detail-section-heading"><div><h3><span className="material-symbols-outlined" aria-hidden="true">forum</span>{historyTab === "edit" ? "ประวัติการแก้ไข" : "ประวัติการติดต่อ"}</h3><small>{historyAnswers.length.toLocaleString()} รายการ · ล่าสุดอยู่บน</small></div><div className="crm-history-tabs" role="tablist" aria-label="ประเภทประวัติ"><button type="button" role="tab" aria-selected={historyTab === "contact"} className={historyTab === "contact" ? "active" : undefined} onClick={() => setHistoryTab("contact")}>ประวัติการติดต่อ <span>{contactAnswers.length.toLocaleString()}</span></button><button type="button" role="tab" aria-selected={historyTab === "edit"} className={historyTab === "edit" ? "active" : undefined} onClick={() => setHistoryTab("edit")}>ประวัติการแก้ไข <span>{editHistoryAnswers.length.toLocaleString()}</span></button></div></div>{historyAnswers.length === 0 ? <div className="crm-detail-empty">{historyTab === "edit" ? "ยังไม่มีประวัติการแก้ไขจาก CRM" : "ยังไม่มีข้อความจาก CRM"}</div> : <div className="crm-answer-list">{historyAnswers.map((answer) => <article className="crm-answer" key={answer.answerNo}><span className="crm-answer-avatar" aria-hidden="true">{initials(staff(answer.posted)?.replace(/^\d+\s*/, "") || answer.posted)}</span><div><div className="crm-answer-meta"><strong>{staff(answer.posted) ?? "CRM User"}</strong><small>{displayDate(answer.answerDate)}</small></div><p>{answer.description}</p><CrmAnswerAttachments image={answer.image} onOpen={openAnswerImage} /></div></article>)}</div>}</section></>}
          </div>
          <aside className="crm-ticket-aside" aria-label="ข้อมูล Ticket">
            <dl className="crm-ticket-facts">
              {[
                { icon: "person", label: "ผู้แจ้ง", value: staff(ticketView.member) },
                { icon: "storefront", label: "สาขา", value: ticketView.branch },
                { icon: "manage_accounts", label: "เจ้าของเรื่อง", value: staff(ticketView.owner) },
                { icon: "support_agent", label: "Assignee", value: displayedAssigneeLabel },
                { icon: "category", label: "Service", value: displayedServiceLabel },
                { icon: "inventory_2", label: "Product", value: ticketView.product },
                { icon: "call", label: "วันที่ติดต่อ", value: ticketView.contactDate ? displayDate(ticketView.contactDate) : null },
                { icon: "event", label: "กำหนดส่ง", value: ticketView.dueDate ? displayDate(ticketView.dueDate) : null },
                { icon: "reply", label: "ตอบล่าสุด", value: ticketView.lastReplyAt ? displayDate(ticketView.lastReplyAt) : null },
              ].map((fact) => <div key={fact.label} className={(fact.label === "Assignee" || fact.label === "Service" || fact.label === "Product") && canEditSelectedTicket ? "crm-fact-editable" : undefined}><dt><span className="material-symbols-outlined" aria-hidden="true">{fact.icon}</span>{fact.label}</dt><dd className="crm-fact-value" title={fact.value || undefined}>{fact.label === "Service" && canEditSelectedTicket && serviceOptions.length > 0 ? <div className="crm-fact-combobox"><input className="crm-fact-search" type="search" value={serviceQuery} placeholder={displayedServiceLabel || "ค้นหา Service"} title={serviceError || undefined} disabled={serviceLoading || replySaving} aria-label="ค้นหา Service ด้วยรหัสหรือชื่อ" aria-controls="crm-service-search-results" aria-expanded={serviceSearchOpen} onFocus={() => setServiceSearchOpen(true)} onBlur={() => { window.setTimeout(() => { setServiceQuery(""); setServiceSearchOpen(false); }, 120); }} onChange={(event) => { setServiceQuery(event.target.value); setServiceSearchOpen(true); }} />{serviceSearchOpen && <div id="crm-service-search-results" className="crm-fact-search-results" role="listbox" aria-label="ผลการค้นหา Service">{filteredServiceChoices.length > 0 ? filteredServiceChoices.map((item) => <button key={item.id} type="button" className={item.id === serviceValue ? "crm-fact-search-option selected" : "crm-fact-search-option"} role="option" aria-selected={item.id === serviceValue} onPointerDown={(event) => { event.preventDefault(); setServiceValue(item.id); setServiceQuery(""); setServiceSearchOpen(false); }}>{item.name}</button>) : <p className="crm-fact-search-empty">ไม่พบ Service ที่ตรงกับคำค้น</p>}</div>}</div> : fact.label === "Assignee" && canEditSelectedTicket && assigneeChoices.length > 0 ? <div className="crm-fact-combobox"><input className="crm-fact-search" type="search" value={assigneeQuery} placeholder={displayedAssigneeLabel || "ค้นหารหัสหรือชื่อ"} title={assigneeError || undefined} disabled={assigneeLoading || replySaving} aria-label="ค้นหา Assignee ด้วยรหัสหรือชื่อ" aria-controls="crm-assignee-search-results" aria-expanded={assigneeSearchOpen} onFocus={() => setAssigneeSearchOpen(true)} onBlur={() => { window.setTimeout(() => { setAssigneeQuery(""); setAssigneeSearchOpen(false); }, 120); }} onChange={(event) => { setAssigneeQuery(event.target.value); setAssigneeSearchOpen(true); }} />{assigneeSearchOpen && <div id="crm-assignee-search-results" className="crm-fact-search-results" role="listbox" aria-label="ผลการค้นหา Assignee">{filteredAssigneeChoices.length > 0 ? filteredAssigneeChoices.map((item) => <button key={item.staffCode} type="button" className={item.staffCode === displayedAssignee ? "crm-fact-search-option selected" : "crm-fact-search-option"} role="option" aria-selected={item.staffCode === displayedAssignee} onPointerDown={(event) => { event.preventDefault(); setAssigneeValue(item.staffCode); setAssigneeQuery(""); setAssigneeSearchOpen(false); }}>{assigneeOptionLabel(item)}</button>) : <p className="crm-fact-search-empty">ไม่พบ Assignee ที่ตรงกับคำค้น</p>}</div>}</div> : fact.label === "Product" && canEditSelectedTicket && productOptions.length > 0 ? <div className="crm-fact-combobox"><input className="crm-fact-search" type="search" value={productQuery} placeholder={displayedProductLabel || "ค้นหา Product"} title={productError || undefined} disabled={productLoading || replySaving} aria-label="ค้นหา Product ด้วยรหัสหรือชื่อ" aria-controls="crm-product-search-results" aria-expanded={productSearchOpen} onFocus={() => setProductSearchOpen(true)} onBlur={() => { window.setTimeout(() => { setProductQuery(""); setProductSearchOpen(false); }, 120); }} onChange={(event) => { setProductQuery(event.target.value); setProductSearchOpen(true); }} />{productSearchOpen && <div id="crm-product-search-results" className="crm-fact-search-results" role="listbox" aria-label="ผลการค้นหา Product">{filteredProductChoices.length > 0 ? filteredProductChoices.map((item) => <button key={item.id} type="button" className={item.id === productValue ? "crm-fact-search-option selected" : "crm-fact-search-option"} role="option" aria-selected={item.id === productValue} onPointerDown={(event) => { event.preventDefault(); setProductValue(item.id); setProductQuery(""); setProductSearchOpen(false); }}>{item.name}</button>) : <p className="crm-fact-search-empty">ไม่พบ Product ที่ตรงกับคำค้น</p>}</div>}</div> : <span className="crm-fact-value-text">{fact.value || "-"}</span>}</dd></div>)}
            </dl>
            <div className="crm-link-panel"><div><small>QA Hub Defect</small>{linkedDefectLoading ? <strong>กำลังตรวจสอบการเชื่อมโยง...</strong> : linkedDefect ? <strong>{linkedDefect.defectCode} · {linkedDefect.title}</strong> : <strong>ยังไม่มี Defect ที่เชื่อมโยง</strong>}{linkedDefectError && <span className="crm-link-error">{linkedDefectError}</span>}</div>{canLinkDefect && (linkedDefect ? <div className="crm-link-actions"><button type="button" className="btn crm-link-unlink" onClick={() => void unlinkLinkedDefect()} disabled={linkedDefectSaving}>ยกเลิกการเชื่อมโยง</button></div> : <div className="crm-link-actions"><button type="button" className="btn primary" onClick={openDefectLinkDialog}>เชื่อมกับ Defect</button><button type="button" className="btn" onClick={openCreateDefectDialog} disabled={!contextProjectId}>สร้าง Defect จาก Ticket</button></div>)}</div>
          </aside>
        </div>
        <div className="modal-actions"><button className="btn" type="button" onClick={() => setSelectedTicket(null)}>ปิดหน้าต่าง</button><a className="btn primary crm-source-link" href={crmTicketUrl(ticketView.jobNo)} target="_blank" rel="noreferrer"><span className="material-symbols-outlined" aria-hidden="true">open_in_new</span> เปิดใน CRM (แท็บใหม่)</a></div>
      </ModalShell>}
      {answerLightbox !== null && answerImages[answerLightbox] && <ImageLightbox images={answerImages} index={answerLightbox} onIndexChange={setAnswerLightbox} onClose={() => setAnswerLightbox(null)} />}
      {createTicketOpen && <CrmCreateTicketModal username={connection?.username} fullName={connection?.username ? staffNames[connection.username] : undefined} staff={staff} onClose={() => setCreateTicketOpen(false)} onCreated={(jobNo, assignedToSelf) => {
        setCreateTicketOpen(false);
        notify(`สร้าง Ticket ${jobNo} ใน CRM แล้ว${assignedToSelf ? "" : " (มอบหมายให้ผู้อื่น จึงไม่แสดงในรายการของคุณ)"}`, "success");
        // Ticket ใหม่ติดต่อวันนี้ — กลับไปช่วง "วันนี้" แล้วดึงสดจาก CRM ให้เห็นรายการที่เพิ่งสร้าง
        setRangePreset(0); setFrom(daysAgo(0)); setTo(dateInput(new Date())); setPage(1); setReload((value) => value + 1);
      }} />}
      {createDefectDialogOpen && selectedTicket && <ModalShell labelledBy="crm-create-defect-title" className="crm-create-defect-modal" onDismiss={() => { if (!createDefectSaving) setCreateDefectDialogOpen(false); }}><div className="modal-head"><div><h2 id="crm-create-defect-title">สร้าง Defect จาก CRM Ticket</h2><small>{selectedTicket.jobNo} · บันทึกลง QA Hub และผูก Ticket อัตโนมัติ</small></div><button type="button" aria-label="ปิด" disabled={createDefectSaving} onClick={() => setCreateDefectDialogOpen(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div><div className="crm-create-defect-note"><span className="material-symbols-outlined" aria-hidden="true">info</span><span>ข้อมูลจาก CRM เป็นค่าเริ่มต้น กรุณาตรวจสอบก่อนสร้าง Defect โดยเลือก Project จาก Topbar</span></div><div className="crm-create-defect-form"><label>Title<input maxLength={300} value={createDefectTitle} onChange={(event) => setCreateDefectTitle(event.target.value)} disabled={createDefectSaving} /></label><label>Severity<select value={createDefectSeverity} onChange={(event) => setCreateDefectSeverity(event.target.value)} disabled={createDefectSaving}>{['Critical', 'High', 'Medium', 'Low'].map((value) => <option key={value} value={value}>{value}</option>)}</select></label><label className="full">Description<textarea rows={7} value={createDefectDescription} onChange={(event) => setCreateDefectDescription(event.target.value)} disabled={createDefectSaving} placeholder="รายละเอียดจาก CRM หรือข้อมูลเพิ่มเติม" /></label></div>{createDefectError && <div className="crm-inline-alert" role="alert"><span className="material-symbols-outlined" aria-hidden="true">error</span><span>{createDefectError}</span></div>}<div className="modal-actions"><button type="button" className="btn" disabled={createDefectSaving} onClick={() => setCreateDefectDialogOpen(false)}>ยกเลิก</button><button type="button" className="btn primary" disabled={createDefectSaving || !createDefectTitle.trim() || !contextProjectId} onClick={() => void createDefectFromCrm()}>{createDefectSaving ? "กำลังสร้าง..." : "สร้าง Defect"}</button></div></ModalShell>}
      {defectLinkDialogOpen && selectedTicket && <ModalShell labelledBy="crm-link-defect-title" className="crm-link-modal" onDismiss={() => { if (!linkDefectSaving) setDefectLinkDialogOpen(false); }}><div className="modal-head"><div><h2 id="crm-link-defect-title">เชื่อม CRM Ticket กับ Defect</h2><small>{selectedTicket.jobNo} · เลือก Defect ที่มีอยู่ใน QA Hub</small></div><button type="button" aria-label="ปิด" disabled={linkDefectSaving} onClick={() => setDefectLinkDialogOpen(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div><label className="crm-link-search">ค้นหา Defect<input autoFocus value={linkDefectSearch} onChange={(event) => setLinkDefectSearch(event.target.value)} placeholder="ค้นหาด้วย Defect Code หรือชื่อเรื่อง" /></label>{linkDefectError && <div className="crm-inline-alert" role="alert"><span className="material-symbols-outlined" aria-hidden="true">error</span><span>{linkDefectError}</span></div>}{linkDefectLoading ? <div className="crm-detail-loading" role="status"><span className="spinner" aria-hidden="true" /> กำลังโหลด Defect...</div> : linkableDefects.length === 0 ? <div className="crm-detail-empty">ไม่พบ Defect ที่เข้าถึงได้</div> : <div className="crm-defect-options">{linkableDefects.map((defect) => <button type="button" className="crm-defect-option" key={defect.defectId} disabled={linkDefectSaving} onClick={() => void linkSelectedDefect(defect.defectId)}><span><strong>{defect.defectCode}</strong><small>{defect.title}</small></span><span><Badge tone={defect.status === "Closed" ? "green" : defect.severity === "Critical" || defect.severity === "High" ? "red" : "blue"}>{defect.status}</Badge><small>{defect.severity}</small></span></button>)}</div>}<div className="modal-actions"><button type="button" className="btn" disabled={linkDefectSaving} onClick={() => setDefectLinkDialogOpen(false)}>ยกเลิก</button></div></ModalShell>}
    </section>
  );
}

export default CrmPage;
