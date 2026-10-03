import { useEffect, useMemo, useRef, useState } from "react";
import { ApiError, apiFetch, apiUrl, getJson, isAbortError } from "../api";
import { Badge } from "../components/Badge";
import { ModalShell } from "../components/ModalShell";
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
};

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
const RANGE_PRESETS = [7, 15, 30] as const;
const DEFAULT_RANGE_DAYS = 30;
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
const staffLabel = (value: string | null | undefined, names: Record<string, string>): string | null => {
  const text = value?.trim();
  if (!text) return null;
  const withName = text.match(/^(.*?)\s*\((\d+)\)$/);
  const code = /^\d+$/.test(text) ? text : withName?.[2];
  if (!code) return text;
  const firstName = (names[code] ?? withName?.[1] ?? "").trim().split(/\s+/)[0];
  return firstName ? `${code} ${firstName}` : code;
};
// Test/Testing = รอทดสอบ (ม่วง) แยกจาก Continue และสถานะกำลังดำเนินการอื่น (น้ำเงิน)
const statusTone = (status?: string | null) => status === "Open" ? "yellow" : status === "Close" || status === "Finish" ? "green" : /^test/i.test(status ?? "") ? "purple" : status ? "blue" : "gray";
const boardBucket = (status?: string | null) => status === "Open" ? "Open" : status === "Close" || status === "Finish" ? "Closed" : "In Progress";
const crmTicketUrl = (jobNo: string) => `https://bluesea.seniorsoft.com/bluesea/BookLicence/MA/Support/JobDetailsHD?JobNo=${encodeURIComponent(jobNo)}&JobType=HD`;
const crmErrorMessage = (reason: unknown, fallback: string) => {
  if (!(reason instanceof ApiError)) return reason instanceof Error ? reason.message : fallback;
  return reason.code === "CRM_NOT_CONFIGURED" ? "ยังไม่มีการตั้งค่าบัญชี CRM สำหรับผู้ใช้นี้ กรุณาตั้งค่าที่ปุ่ม บัญชี CRM ของฉัน ด้านขวาบน"
    : reason.code === "CRM_UNAUTHORIZED" ? "บัญชี CRM ไม่ได้รับอนุญาตให้เชื่อมต่อ กรุณาตรวจสอบ Username/Password แล้วลองใหม่"
      : reason.code === "CRM_RATE_LIMITED" ? "CRM จำกัดจำนวนคำขอชั่วคราว กรุณารอสักครู่แล้วลองใหม่"
        : reason.code === "CRM_TIMEOUT" ? "CRM ตอบกลับช้าเกินกำหนด กรุณาลองใหม่อีกครั้ง"
          : reason.code === "CRM_UNAVAILABLE" ? (reason.message.startsWith("CRM / BlueID") ? reason.message : "CRM ไม่พร้อมใช้งานในขณะนี้ กรุณาลองใหม่ภายหลัง")
            : reason.code === "CRM_RESULT_TOO_LARGE" ? "ข้อมูลจาก CRM มีจำนวนมากเกินไป กรุณาจำกัดช่วงวันที่หรือลองใช้ตัวกรองเพิ่มเติม"
              : reason.code === "CRM_BAD_RESPONSE" ? "CRM ส่งข้อมูลไม่ตรงรูปแบบที่ระบบรองรับ กรุณาลองใหม่หรือติดต่อผู้ดูแลระบบ"
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
              : code === "CRM_TICKET_NOT_FOUND" ? "ไม่พบ Ticket"
                : code === "CRM_INVALID_QUERY" ? "ข้อมูลค้นหาไม่ถูกต้อง"
                  : "โหลดข้อมูล CRM ไม่สำเร็จ";

export function CrmPage({ search, onConfigure, configVersion = 0, contextProjectId, canLinkDefect = false, canEditCrm = false }: { search: string; onConfigure?: () => void; configVersion?: number; contextProjectId?: string; canLinkDefect?: boolean; canEditCrm?: boolean }) {
  const initialFrom = useMemo(() => daysAgo(DEFAULT_RANGE_DAYS), []);
  const initialTo = useMemo(() => dateInput(new Date()), []);
  const [connection, setConnection] = useState<CrmConnectionStatus | null>(null), [connectionError, setConnectionError] = useState("");
  const [rows, setRows] = useState<CrmTicket[]>([]), [summary, setSummary] = useState({ total: 0, open: 0, inProgress: 0, closed: 0 }), [total, setTotal] = useState(0), [lastFetchedAt, setLastFetchedAt] = useState("");
  const [status, setStatus] = useState(""), [from, setFrom] = useState(initialFrom), [rangePreset, setRangePreset] = useState<number | "custom">(DEFAULT_RANGE_DAYS), [to, setTo] = useState(initialTo), [page, setPage] = useState(1), [pageSize, setPageSize] = useState(25), [loading, setLoading] = useState(true), [error, setError] = useState(""), [errorCode, setErrorCode] = useState<string | undefined>(), [reload, setReload] = useState(0);
  const [selectedTicket, setSelectedTicket] = useState<CrmTicket | null>(null);
  const [ticketDetail, setTicketDetail] = useState<CrmTicketDetail | null>(null), [ticketDetailLoading, setTicketDetailLoading] = useState(false), [ticketDetailError, setTicketDetailError] = useState(""), [ticketDetailErrorCode, setTicketDetailErrorCode] = useState<string | undefined>(), [detailReload, setDetailReload] = useState(0);
  const [probeLoading, setProbeLoading] = useState(false), [probeMessage, setProbeMessage] = useState(""), [probeError, setProbeError] = useState(false);
  const [viewMode, setViewMode] = useState<"list" | "board">("list");
  const [linkedDefect, setLinkedDefect] = useState<CrmLinkedDefect | null>(null), [linkedDefectLoading, setLinkedDefectLoading] = useState(false), [linkedDefectError, setLinkedDefectError] = useState("");
  const [defectLinkDialogOpen, setDefectLinkDialogOpen] = useState(false), [linkableDefects, setLinkableDefects] = useState<LinkableDefect[]>([]), [linkDefectSearch, setLinkDefectSearch] = useState(""), [linkDefectLoading, setLinkDefectLoading] = useState(false), [linkDefectSaving, setLinkDefectSaving] = useState(false), [linkDefectError, setLinkDefectError] = useState("");
  const [createDefectDialogOpen, setCreateDefectDialogOpen] = useState(false), [createDefectTitle, setCreateDefectTitle] = useState(""), [createDefectSeverity, setCreateDefectSeverity] = useState("Medium"), [createDefectDescription, setCreateDefectDescription] = useState(""), [createDefectSaving, setCreateDefectSaving] = useState(false), [createDefectError, setCreateDefectError] = useState("");
  const [replyMessage, setReplyMessage] = useState(""), [replyStatus, setReplyStatus] = useState(""), [replyToOwner, setReplyToOwner] = useState(false), [replySaving, setReplySaving] = useState(false), [replyError, setReplyError] = useState("");
  const [clock, setClock] = useState(() => Date.now());
  // reload (ปุ่มรีเฟรช/หลังบันทึก) ต้องดึงสดจาก CRM ส่วนเปลี่ยนหน้า/ค้นหาใช้ cache ฝั่ง server 60 วินาทีได้
  const lastListReload = useRef<number | null>(null);
  const [staffNames, setStaffNames] = useState<Record<string, string>>({});
  const selectedJobNo = selectedTicket?.jobNo;

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
      setRows([]); setTotal(0); setSummary({ total: 0, open: 0, inProgress: 0, closed: 0 }); setLastFetchedAt(""); setError(""); setErrorCode(undefined); setLoading(false);
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
      getJson<CrmListResult>(`${apiUrl}/crm/tickets?${params.toString()}`, controller.signal)
        .then((value) => { setRows(Array.isArray(value.rows) ? value.rows : []); setTotal(value.total ?? 0); setSummary(value.summary ?? { total: 0, open: 0, inProgress: 0, closed: 0 }); setLastFetchedAt(value.lastFetchedAt ?? ""); })
        .catch((reason: unknown) => {
          if (isAbortError(reason)) return;
          const apiError = reason instanceof ApiError ? reason : null;
          setRows([]);
          setErrorCode(apiError?.code);
          setError(crmErrorMessage(reason, "โหลดรายการ CRM ไม่สำเร็จ"));
        })
        .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    }, 300);
    return () => { window.clearTimeout(timer); controller.abort(); };
  }, [connection, from, page, pageSize, reload, search, status, to]);

  useEffect(() => {
    if (!selectedJobNo) return;
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
  useEffect(() => { setReplyMessage(""); setReplyStatus(""); setReplyToOwner(false); setReplyError(""); }, [selectedJobNo]);

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
  const sortedRows = useMemo(() => {
    const contactTime = (value?: string | null) => toUtcDate(value)?.getTime() ?? Number.POSITIVE_INFINITY;
    return [...rows].sort((a, b) => { const left = contactTime(a.contactDate), right = contactTime(b.contactDate); return left === right ? 0 : left < right ? -1 : 1; });
  }, [rows]);
  // Finish/Close = จบงานแล้ว แยกไปส่วน "ปิดงานแล้ว" ด้านล่าง ไม่ปนกับงานที่ยังต้องดำเนินการ (ทั้ง List และ Board)
  const activeRows = useMemo(() => sortedRows.filter((item) => boardBucket(item.status) !== "Closed"), [sortedRows]);
  const closedRows = useMemo(() => sortedRows.filter((item) => boardBucket(item.status) === "Closed"), [sortedRows]);
  const boardColumns = useMemo(() => [
    { key: "Open", label: "Open", items: activeRows.filter((item) => boardBucket(item.status) === "Open") },
    { key: "In Progress", label: "In Progress", items: activeRows.filter((item) => boardBucket(item.status) === "In Progress") },
  ], [activeRows]);
  const ticketTableHead = <thead><tr><th>Job No.</th><th>เรื่อง</th><th>สถานะ</th><th>Service / Product</th><th>ผู้แจ้ง / สาขา</th><th>วันที่ติดต่อ</th><th>อายุงาน</th></tr></thead>;
  const renderTicketRow = (item: CrmTicket) => { const days = ageDays(item.contactDate); return <tr key={item.jobNo} className={`crm-row crm-row-${boardBucket(item.status) === "Open" ? "open" : boardBucket(item.status) === "Closed" ? "closed" : "progress"}`}><td data-label="Job No."><button className="crm-job-link" type="button" onClick={() => setSelectedTicket(item)} aria-label={`เปิดรายละเอียด ${item.jobNo}`}><strong className="crm-job-no">{item.jobNo}</strong><span className="material-symbols-outlined" aria-hidden="true">open_in_new</span></button></td><td data-label="เรื่อง"><div className="crm-subject"><strong title={item.subject || undefined}>{item.subject || "ไม่มี Subject"}</strong></div></td><td data-label="สถานะ"><Badge tone={statusTone(item.status)}>{item.status || "Unknown"}</Badge></td><td data-label="Service / Product"><div className="crm-service"><span>{item.serviceType || "-"}</span><small className="crm-product-chip">{item.product || "ไม่ระบุ Product"}</small></div></td><td data-label="ผู้แจ้ง / สาขา"><span className="crm-reporter"><strong title={staff(item.member) ?? undefined}><span className="material-symbols-outlined" aria-hidden="true">person</span><span>{staff(item.member) ?? "-"}</span></strong><small title={item.branch || undefined}><span className="material-symbols-outlined" aria-hidden="true">storefront</span><span>{item.branch || "ไม่ระบุสาขา"}</span></small></span></td><td data-label="วันที่ติดต่อ">{listDate(item.contactDate)}</td><td data-label="อายุงาน"><span className="crm-age"><span className={`crm-age-pill crm-age-${ageLevel(days)}`}>{days === null ? "-" : days === 0 ? "วันนี้" : <><strong>{days}</strong> วัน</>}</span><small>ตอบล่าสุด {displayDate(item.lastReplyAt)}</small></span></td></tr>; };
  const openDefectLinkDialog = () => { setLinkDefectSearch(""); setLinkDefectError(""); setDefectLinkDialogOpen(true); };
  const openCreateDefectDialog = () => {
    if (!selectedTicket || !contextProjectId) return;
    setCreateDefectTitle(ticketDetail?.ticket.subject || selectedTicket.subject || `CRM Ticket ${selectedTicket.jobNo}`);
    setCreateDefectSeverity("Medium");
    setCreateDefectDescription(ticketDetail?.description || "");
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
  const saveTicketUpdate = async () => {
    const current = ticketDetail?.ticket ?? selectedTicket;
    const message = replyMessage.trim();
    if (!current || !selectedJobNo || replySaving || (!message && !replyStatus && !replyToOwner)) return;
    if (replyToOwner && !(await confirmDialog({ title: "ส่งกลับเจ้าของเรื่อง", message: `เปลี่ยนผู้รับผิดชอบ ${selectedJobNo} เป็นเจ้าของเรื่อง${current.owner ? ` (${staff(current.owner)})` : ""} — Ticket จะออกจากรายการงานของคุณ`, confirmLabel: "ส่งกลับเจ้าของเรื่อง" }))) return;
    setReplySaving(true); setReplyError("");
    try {
      const result = await apiFetch<CrmTicketMutation>(`/crm/tickets/${encodeURIComponent(selectedJobNo)}`, {
        method: "PATCH",
        json: { message: message || null, status: replyStatus || null, assignToOwner: replyToOwner, expectedStatus: current.status || null, expectedAssignee: current.assignee || null },
      });
      setReplyMessage(""); setReplyStatus(""); setReplyToOwner(false);
      setReload((value) => value + 1);
      if (replyToOwner) {
        // Ticket ไม่ได้อยู่ในขอบเขตงานของผู้ใช้แล้ว (Assignto เปลี่ยน) — ปิดรายละเอียดแทนการโหลดซ้ำซึ่งจะได้ 404
        notify(`ส่ง ${selectedJobNo} กลับเจ้าของเรื่องแล้ว`, "success");
        setSelectedTicket(null);
        return;
      }
      notify(`บันทึก ${selectedJobNo} ไป CRM แล้ว`, "success");
      setSelectedTicket((previous) => previous ? { ...previous, status: result.status, assignee: result.assignee } : previous);
      setDetailReload((value) => value + 1);
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
          <div className="crm-range-group" role="group" aria-labelledby="crm-range-label"><span id="crm-range-label">ช่วงวันที่ติดต่อ</span><div className="crm-range-presets">{RANGE_PRESETS.map((days) => <button key={days} type="button" className={rangePreset === days ? "active" : ""} aria-pressed={rangePreset === days} onClick={() => { setRangePreset(days); setFrom(daysAgo(days)); setTo(dateInput(new Date())); setPage(1); }}>{days} วัน</button>)}<button type="button" className={rangePreset === "custom" ? "active" : ""} aria-pressed={rangePreset === "custom"} aria-expanded={rangePreset === "custom"} aria-controls="crm-custom-range" onClick={() => setRangePreset("custom")}><span className="material-symbols-outlined" aria-hidden="true">date_range</span>กำหนดเอง</button></div><small>{dayLabel(from)} – {dayLabel(to)}</small></div>
          <label>สถานะ<select value={status} onChange={(event) => { setStatus(event.target.value); setPage(1); }}><option value="">ทุกสถานะ</option>{CRM_STATUSES.map((value) => <option key={value} value={value}>{value}</option>)}</select></label>
          <label>แสดงต่อหน้า<select value={pageSize} onChange={(event) => { setPageSize(Number(event.target.value)); setPage(1); }}><option value={25}>25</option><option value={50}>50</option><option value={100}>100</option></select></label>
        </div>
        {rangePreset === "custom" && <div className="crm-custom-range" id="crm-custom-range"><div className="crm-custom-range-head"><span className="material-symbols-outlined" aria-hidden="true">event</span><div><strong>ระบุช่วงวันที่เอง</strong><small>แสดงเป็น วัน/เดือน/ปี (พ.ศ.) · ไม่เกิน {CRM_MAX_RANGE_DAYS} วัน</small></div></div><div className="crm-custom-range-fields"><CrmDateField label="วันที่เริ่มต้น" value={from} min={shiftDate(to, -CRM_MAX_RANGE_DAYS)} max={to} onChange={(value) => { setFrom(value); setPage(1); }} /><span className="crm-custom-range-sep" aria-hidden="true">–</span><CrmDateField label="วันที่สิ้นสุด" value={to} min={from} max={[shiftDate(from, CRM_MAX_RANGE_DAYS), dateInput(new Date())].sort()[0]} onChange={(value) => { setTo(value); setPage(1); }} /></div></div>}
      </div>
      {stale && <div className="crm-stale-alert" role="status"><span className="material-symbols-outlined" aria-hidden="true">schedule</span><span><strong>ข้อมูลอาจเก่า</strong> โหลดล่าสุด {displayDate(lastFetchedAt)} แล้ว</span><button className="btn" type="button" onClick={() => setReload((value) => value + 1)} disabled={loading}>รีเฟรชข้อมูล</button></div>}
      <div className="crm-list-card card">
        <div className="crm-list-heading"><div><h3>รายการ Ticket</h3><p>{loading ? "กำลังโหลดข้อมูลจาก CRM..." : `แสดง ${rows.length.toLocaleString()} จาก ${total.toLocaleString()} รายการ`}</p></div><div className="crm-list-heading-actions">{lastFetchedAt && <small>ดึงข้อมูลล่าสุด {displayDate(lastFetchedAt)}</small>}<div className="crm-view-switcher" role="group" aria-label="รูปแบบการแสดงผล"><button type="button" className={`btn ${viewMode === "list" ? "active" : ""}`} aria-pressed={viewMode === "list"} onClick={() => setViewMode("list")}>List</button><button type="button" className={`btn ${viewMode === "board" ? "active" : ""}`} aria-pressed={viewMode === "board"} onClick={() => setViewMode("board")}>Board</button></div></div></div>
        {error ? <div className="crm-state crm-state-error" role="alert"><span className="material-symbols-outlined" aria-hidden="true">error</span><div><strong>{crmErrorTitle(errorCode)}</strong><p>{error}</p><button className="btn" type="button" onClick={() => setReload((value) => value + 1)}>ลองอีกครั้ง</button></div></div>
          : loading ? <div className="crm-state" role="status"><span className="spinner" aria-hidden="true" /><p>กำลังโหลดรายการ Ticket...</p></div>
            : rows.length === 0 ? <div className="crm-state"><span className="material-symbols-outlined" aria-hidden="true">inbox</span><p>ไม่พบ Ticket ตามตัวกรองนี้</p><small>ลองเปลี่ยนช่วงวันที่ สถานะ หรือคำค้นหา</small></div>
              : <><div className={`table-wrap crm-table-wrap ${viewMode === "board" ? "crm-board-hidden" : ""}`} aria-hidden={viewMode === "board"}>{activeRows.length === 0 ? <div className="crm-active-empty"><span className="material-symbols-outlined" aria-hidden="true">celebration</span>ไม่มีงานที่ต้องดำเนินการในหน้านี้</div> : <table className="table-cards crm-table">{ticketTableHead}<tbody>{activeRows.map(renderTicketRow)}</tbody></table>}</div><div className={`crm-board ${viewMode === "list" ? "crm-board-hidden" : ""}`} aria-hidden={viewMode === "list"}>{boardColumns.map((column) => <section className="crm-board-column" data-bucket={column.key} key={column.key} aria-label={`${column.label} ${column.items.length} รายการ`}><div className="crm-board-column-head"><strong>{column.label}</strong><span>{column.items.length}</span></div><div className="crm-board-column-body">{column.items.length === 0 ? <small className="crm-board-empty">ไม่มี Ticket</small> : column.items.map((item) => <button type="button" className="crm-board-ticket" data-tone={statusTone(item.status)} key={item.jobNo} onClick={() => setSelectedTicket(item)} aria-label={`เปิดรายละเอียด ${item.jobNo} ${item.subject || ""}`} title={item.subject || undefined}><span className="crm-board-ticket-top"><strong>{item.jobNo}</strong><Badge tone={statusTone(item.status)}>{item.status || "Unknown"}</Badge></span><span className="crm-board-ticket-subject">{item.subject || "ไม่มี Subject"}</span><span className="crm-board-ticket-meta"><span><span className="material-symbols-outlined" aria-hidden="true">category</span><span>{item.serviceType || "-"}</span></span><span><span className="material-symbols-outlined" aria-hidden="true">person</span><span>{staff(item.member) ?? "-"}</span></span><span className="crm-board-ticket-age"><span className="material-symbols-outlined" aria-hidden="true">schedule</span><span>{ageLabel(item.contactDate)}</span></span></span></button>)}</div></section>)}</div>{closedRows.length > 0 && <details className="crm-closed-section"><summary><span className="crm-closed-icon material-symbols-outlined" aria-hidden="true">task_alt</span><span className="crm-closed-title"><strong>ปิดงานแล้ว</strong><small>สถานะ Finish / Close · แยกจากงานที่ต้องดำเนินการ</small></span><span className="crm-closed-count">{closedRows.length.toLocaleString()} รายการ</span><span className="crm-closed-chevron material-symbols-outlined" aria-hidden="true">expand_more</span></summary><div className="table-wrap crm-table-wrap crm-closed-table"><table className="table-cards crm-table">{ticketTableHead}<tbody>{closedRows.map(renderTicketRow)}</tbody></table></div></details>}<div className="crm-pagination"><small>หน้า {page} จาก {pageCount}</small><div><button className="btn" type="button" disabled={page <= 1 || loading} onClick={() => setPage((value) => Math.max(1, value - 1))}>ก่อนหน้า</button><button className="btn" type="button" disabled={page >= pageCount || loading} onClick={() => setPage((value) => Math.min(pageCount, value + 1))}>ถัดไป</button></div></div></>}
      </div>
      {selectedTicket && <ModalShell labelledBy="crm-ticket-detail-title" boxClassName="modal-box crm-ticket-modal" onDismiss={() => setSelectedTicket(null)}>
        <div className="modal-head crm-ticket-head"><div className="crm-ticket-head-main"><span className="crm-eyebrow">รายละเอียด Ticket จาก CRM</span><div className="crm-ticket-head-title"><h2 id="crm-ticket-detail-title">{selectedTicket.jobNo}</h2><Badge tone={statusTone(selectedTicket.status)}>{selectedTicket.status || "Unknown"}</Badge>{(() => { const days = ageDays(selectedTicket.contactDate); return <span className={`crm-age-pill crm-age-${ageLevel(days)}`} title="อายุงานนับจากวันที่ติดต่อ"><span className="material-symbols-outlined" aria-hidden="true">schedule</span>{days === null ? "-" : days === 0 ? "วันนี้" : <><strong>{days}</strong> วัน</>}</span>; })()}</div><p>{selectedTicket.subject || "ไม่มี Subject"}</p></div><button type="button" aria-label="ปิดรายละเอียด Ticket" onClick={() => setSelectedTicket(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
        <div className="crm-ticket-body">
          <div className="crm-ticket-main">
            {canEditCrm && <section className="crm-reply" aria-labelledby="crm-reply-title">
              <div className="crm-reply-head"><h3 id="crm-reply-title"><span className="material-symbols-outlined" aria-hidden="true">edit_note</span>อัปเดต Ticket</h3><small>บันทึกไป CRM ทันที · ข้อความจะเป็นแถวใหม่ในประวัติการติดต่อ</small></div>
              <label className="crm-reply-text"><textarea aria-label="เพิ่มประวัติการติดต่อ" rows={3} maxLength={CRM_REPLY_MAX} value={replyMessage} disabled={replySaving} placeholder="เพิ่มประวัติการติดต่อ เช่น สิ่งที่ตรวจสอบ/แก้ไข หรือสิ่งที่ต้องให้เจ้าของเรื่องทำต่อ" onChange={(event) => setReplyMessage(event.target.value)} onKeyDown={(event) => { if ((event.ctrlKey || event.metaKey) && event.key === "Enter") { event.preventDefault(); void saveTicketUpdate(); } }} /></label>
              <div className="crm-reply-controls">
                <label className="crm-reply-status">สถานะ<select value={replyStatus} disabled={replySaving} onChange={(event) => setReplyStatus(event.target.value)}><option value="">คงเดิม ({(ticketDetail?.ticket ?? selectedTicket).status || "-"})</option>{CRM_STATUSES.filter((value) => value !== (ticketDetail?.ticket ?? selectedTicket).status).map((value) => <option key={value} value={value}>{value}</option>)}</select></label>
                <label className="crm-reply-owner"><input type="checkbox" checked={replyToOwner} disabled={replySaving || !(ticketDetail?.ticket ?? selectedTicket).owner} onChange={(event) => setReplyToOwner(event.target.checked)} /><span>ส่งกลับเจ้าของเรื่อง{(ticketDetail?.ticket ?? selectedTicket).owner ? <small>Assign เป็น {staff((ticketDetail?.ticket ?? selectedTicket).owner)}</small> : <small>ไม่มีข้อมูลเจ้าของเรื่อง</small>}</span></label>
                <div className="crm-reply-submit"><small aria-live="polite">{replyMessage.length}/{CRM_REPLY_MAX}</small><button type="button" className="btn primary" disabled={replySaving || (!replyMessage.trim() && !replyStatus && !replyToOwner)} onClick={() => void saveTicketUpdate()}>{replySaving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">send</span> บันทึกไป CRM</>}</button></div>
              </div>
              {replyError && <div className="crm-inline-alert" role="alert"><span className="material-symbols-outlined" aria-hidden="true">error</span><span>{replyError}</span></div>}
            </section>}
            {ticketDetailLoading ? <div className="crm-detail-loading" role="status"><span className="spinner" aria-hidden="true" /> กำลังโหลดรายละเอียดและประวัติจาก CRM...</div>
              : ticketDetailError ? <div className="crm-detail-note crm-detail-note-error" role="alert"><span className="material-symbols-outlined" aria-hidden="true">error</span><div><strong>{crmErrorTitle(ticketDetailErrorCode)}</strong><p>{ticketDetailError}</p><button className="btn" type="button" onClick={() => setDetailReload((value) => value + 1)}>ลองโหลดรายละเอียดอีกครั้ง</button></div></div>
                : ticketDetail && <><section className="crm-detail-section"><h3><span className="material-symbols-outlined" aria-hidden="true">description</span>รายละเอียด</h3><p className="crm-detail-description">{ticketDetail.description || "ไม่มี Description"}</p></section><section className="crm-detail-section"><div className="crm-detail-section-heading"><h3><span className="material-symbols-outlined" aria-hidden="true">forum</span>ประวัติการติดต่อ</h3><small>{ticketDetail.answers.length.toLocaleString()} รายการ · ล่าสุดอยู่บน</small></div>{ticketDetail.answers.length === 0 ? <div className="crm-detail-empty">ยังไม่มี Comment จาก CRM</div> : <div className="crm-answer-list">{sortedAnswers.map((answer) => <article className="crm-answer" key={answer.answerNo}><span className="crm-answer-avatar" aria-hidden="true">{initials(staff(answer.posted)?.replace(/^\d+\s*/, "") || answer.posted)}</span><div><div className="crm-answer-meta"><strong>{staff(answer.posted) ?? "CRM User"}</strong><small>{displayDate(answer.answerDate)}</small></div><p>{answer.description || "(ไม่มีข้อความ)"}</p>{answer.image && <small className="crm-answer-attachment"><span className="material-symbols-outlined" aria-hidden="true">attach_file</span>มีไฟล์แนบใน CRM</small>}</div></article>)}</div>}</section></>}
          </div>
          <aside className="crm-ticket-aside" aria-label="ข้อมูล Ticket">
            <dl className="crm-ticket-facts">
              {[
                { icon: "person", label: "ผู้แจ้ง", value: staff(selectedTicket.member) },
                { icon: "storefront", label: "สาขา", value: selectedTicket.branch },
                { icon: "manage_accounts", label: "เจ้าของเรื่อง", value: staff((ticketDetail?.ticket ?? selectedTicket).owner) },
                { icon: "support_agent", label: "Assignee", value: staff((ticketDetail?.ticket ?? selectedTicket).assignee) },
                { icon: "category", label: "Service", value: selectedTicket.serviceType },
                { icon: "inventory_2", label: "Product", value: selectedTicket.product },
                { icon: "call", label: "วันที่ติดต่อ", value: selectedTicket.contactDate ? displayDate(selectedTicket.contactDate) : null },
                { icon: "event", label: "กำหนดส่ง", value: selectedTicket.dueDate ? displayDate(selectedTicket.dueDate) : null },
                { icon: "reply", label: "ตอบล่าสุด", value: selectedTicket.lastReplyAt ? displayDate(selectedTicket.lastReplyAt) : null },
              ].map((fact) => <div key={fact.label}><dt><span className="material-symbols-outlined" aria-hidden="true">{fact.icon}</span>{fact.label}</dt><dd title={fact.value || undefined}>{fact.value || "-"}</dd></div>)}
            </dl>
            <div className="crm-link-panel"><div><small>QA Hub Defect</small>{linkedDefectLoading ? <strong>กำลังตรวจสอบการเชื่อมโยง...</strong> : linkedDefect ? <strong>{linkedDefect.defectCode} · {linkedDefect.title}</strong> : <strong>ยังไม่มี Defect ที่เชื่อมโยง</strong>}{linkedDefectError && <span className="crm-link-error">{linkedDefectError}</span>}</div>{canLinkDefect && !linkedDefect && <div className="crm-link-actions"><button type="button" className="btn primary" onClick={openDefectLinkDialog}>เชื่อมกับ Defect</button><button type="button" className="btn" onClick={openCreateDefectDialog} disabled={!contextProjectId}>สร้าง Defect จาก Ticket</button></div>}</div>
          </aside>
        </div>
        <div className="modal-actions"><button className="btn" type="button" onClick={() => setSelectedTicket(null)}>ปิดหน้าต่าง</button><a className="btn primary crm-source-link" href={crmTicketUrl(selectedTicket.jobNo)} target="_blank" rel="noreferrer"><span className="material-symbols-outlined" aria-hidden="true">open_in_new</span> เปิดใน CRM (แท็บใหม่)</a></div>
      </ModalShell>}
      {createDefectDialogOpen && selectedTicket && <ModalShell labelledBy="crm-create-defect-title" className="crm-create-defect-modal" onDismiss={() => { if (!createDefectSaving) setCreateDefectDialogOpen(false); }}><div className="modal-head"><div><h2 id="crm-create-defect-title">สร้าง Defect จาก CRM Ticket</h2><small>{selectedTicket.jobNo} · บันทึกลง QA Hub และผูก Ticket อัตโนมัติ</small></div><button type="button" aria-label="ปิด" disabled={createDefectSaving} onClick={() => setCreateDefectDialogOpen(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div><div className="crm-create-defect-note"><span className="material-symbols-outlined" aria-hidden="true">info</span><span>ข้อมูลจาก CRM เป็นค่าเริ่มต้น กรุณาตรวจสอบก่อนสร้าง Defect โดยเลือก Project จาก Topbar</span></div><div className="crm-create-defect-form"><label>Title<input maxLength={300} value={createDefectTitle} onChange={(event) => setCreateDefectTitle(event.target.value)} disabled={createDefectSaving} /></label><label>Severity<select value={createDefectSeverity} onChange={(event) => setCreateDefectSeverity(event.target.value)} disabled={createDefectSaving}>{['Critical', 'High', 'Medium', 'Low'].map((value) => <option key={value} value={value}>{value}</option>)}</select></label><label className="full">Description<textarea rows={7} value={createDefectDescription} onChange={(event) => setCreateDefectDescription(event.target.value)} disabled={createDefectSaving} placeholder="รายละเอียดจาก CRM หรือข้อมูลเพิ่มเติม" /></label></div>{createDefectError && <div className="crm-inline-alert" role="alert"><span className="material-symbols-outlined" aria-hidden="true">error</span><span>{createDefectError}</span></div>}<div className="modal-actions"><button type="button" className="btn" disabled={createDefectSaving} onClick={() => setCreateDefectDialogOpen(false)}>ยกเลิก</button><button type="button" className="btn primary" disabled={createDefectSaving || !createDefectTitle.trim() || !contextProjectId} onClick={() => void createDefectFromCrm()}>{createDefectSaving ? "กำลังสร้าง..." : "สร้าง Defect"}</button></div></ModalShell>}
      {defectLinkDialogOpen && selectedTicket && <ModalShell labelledBy="crm-link-defect-title" className="crm-link-modal" onDismiss={() => { if (!linkDefectSaving) setDefectLinkDialogOpen(false); }}><div className="modal-head"><div><h2 id="crm-link-defect-title">เชื่อม CRM Ticket กับ Defect</h2><small>{selectedTicket.jobNo} · เลือก Defect ที่มีอยู่ใน QA Hub</small></div><button type="button" aria-label="ปิด" disabled={linkDefectSaving} onClick={() => setDefectLinkDialogOpen(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div><label className="crm-link-search">ค้นหา Defect<input autoFocus value={linkDefectSearch} onChange={(event) => setLinkDefectSearch(event.target.value)} placeholder="ค้นหาด้วย Defect Code หรือชื่อเรื่อง" /></label>{linkDefectError && <div className="crm-inline-alert" role="alert"><span className="material-symbols-outlined" aria-hidden="true">error</span><span>{linkDefectError}</span></div>}{linkDefectLoading ? <div className="crm-detail-loading" role="status"><span className="spinner" aria-hidden="true" /> กำลังโหลด Defect...</div> : linkableDefects.length === 0 ? <div className="crm-detail-empty">ไม่พบ Defect ที่เข้าถึงได้</div> : <div className="crm-defect-options">{linkableDefects.map((defect) => <button type="button" className="crm-defect-option" key={defect.defectId} disabled={linkDefectSaving} onClick={() => void linkSelectedDefect(defect.defectId)}><span><strong>{defect.defectCode}</strong><small>{defect.title}</small></span><span><Badge tone={defect.status === "Closed" ? "green" : defect.severity === "Critical" || defect.severity === "High" ? "red" : "blue"}>{defect.status}</Badge><small>{defect.severity}</small></span></button>)}</div>}<div className="modal-actions"><button type="button" className="btn" disabled={linkDefectSaving} onClick={() => setDefectLinkDialogOpen(false)}>ยกเลิก</button></div></ModalShell>}
    </section>
  );
}

export default CrmPage;
