import { toUtcDate, formatThaiDateTime } from "../dateTime";
import { useState, useRef, useMemo, useCallback, useEffect } from "react";
import type { DefectItem, UserLookup } from "../shared/types";
import { apiUrl, authHeaders, getJson, isAbortError } from "../api";
import { DefectCommentComposer, DefectCommentList } from "../components/DefectComments";
import { type DefectComment, type DefectCommentAttachment, toLightboxImages, useAuthedAttachmentUrls } from "../components/defectCommentUtils";
import { ImageLightbox } from "../components/ImageLightbox";
import { confirmDialog, notify } from "../components/dialogStore";
import { exportDefectModulePdf } from "../DefectModulePdf";
import { SortHeader, type SortState } from "../components/SortHeader";
import "../components/ListPage.css";
import { ListPager } from "../components/ListPager";
import { useUserPageSize } from "../components/usePageSize";
import "./DefectsPage.css";
import { Badge } from "../components/Badge";
import { defectAgeDays, defectClosingStatuses, defectResolutionLabel, parseReproSteps, splitResolutionChoice, type DefectResolutionChoice } from "../shared/defects";
import { DefectResolutionModal } from "../components/DefectResolutionModal";
import { useDebounced } from "../components/useDebounced";
import { ReproSteps } from "../components/ReproSteps";
import { ModalShell } from "../components/ModalShell";
import { type ModuleItem, copyText, defectStatusTones, renderModuleSelectOptions, useMasterOptions } from "../shared/appShared";

type SimilarDefect = { defectId: string; defectCode: string; title: string; status: string; severity: string; resolutionType?: string | null; snippet: string };
// BadRequest ของ API บางจุดส่งข้อความล้วน (ไม่ใช่ ProblemDetails) — อ่านได้ทั้งสองแบบ
const readError = async (response: Response, fallback: string) => { const text = await response.text().catch(() => ""); try { const p = JSON.parse(text); return typeof p === "string" ? p : p?.detail ?? p?.title ?? fallback; } catch { return text.trim() || fallback; } };

type DefectActivityItem = { activityId: string; actionType: string; message: string; actorUserId?: string | null; actorName?: string | null; createdAt: string; performedByUserId?: string | null; performedAt?: string; attachments?: DefectCommentAttachment[] };
type DefectTestCaseItem = { testCaseId: string; testCaseCode: string; title: string; priority?: string; status?: string; linkedAt?: string };
const defectSeverities = ["Critical", "High", "Medium", "Low"];
const defectStatuses = ["Open", "In Progress", "Resolved", "Closed", "Rejected"];
type DefectModuleCount = { moduleId: string | null; moduleCode: string | null; moduleName: string; count: number };
const emptyDefectStats = { total: 0, open: 0, inProgress: 0, resolved: 0, closed: 0, rejected: 0, critical: 0, high: 0, medium: 0, low: 0, oldestOpenAgeDays: 0, unassignedActive: 0, closureRate: 0, modules: [] as DefectModuleCount[] };
const defectPct = (count: number, total: number): number => total > 0 ? Math.round((count / total) * 100) : 0;
// เรียงรายการด้วยหัวคอลัมน์ — ฝั่ง server (sortBy) เพราะแบ่งหน้าที่ server; จำไว้ต่อเครื่อง ค่าเริ่มต้น = สร้างล่าสุดก่อน (ลำดับเดิม)
type DefectSortKey = "code" | "severity" | "status" | "createdAt";
const defectSortStorageKey = "qa.defects.listSort";
const defaultDefectSort: SortState<DefectSortKey> = { key: "createdAt", dir: "desc" };
const readDefectSort = (): SortState<DefectSortKey> => {
  try {
    const saved = JSON.parse(localStorage.getItem(defectSortStorageKey) ?? "null") as SortState<DefectSortKey> | null;
    return saved && ["code", "severity", "status", "createdAt"].includes(saved.key) && (saved.dir === "asc" || saved.dir === "desc") ? saved : defaultDefectSort;
  } catch { return defaultDefectSort; }
};
const defectSortOptions: { value: string; label: string }[] = [
  { value: "createdAt:desc", label: "ใหม่ล่าสุดก่อน" }, { value: "createdAt:asc", label: "ค้างนานที่สุดก่อน" },
  { value: "severity:asc", label: "รุนแรงที่สุดก่อน (Critical)" }, { value: "status:asc", label: "สถานะ (Open → Rejected)" },
  { value: "code:desc", label: "รหัส Defect มาก → น้อย" }, { value: "code:asc", label: "รหัส Defect น้อย → มาก" },
];
const moduleRankingLimit = 6;
const defectSeverityTones: Record<string, string> = { Critical: "red", High: "yellow", Medium: "blue", Low: "green" };
// สถานะการส่งเคสไป CRM (BlueSea Helpdesk) — ใช้แสดงคอลัมน์ CRM ในตาราง Defect list เพื่อให้เห็นได้ทันที
// ว่ารายการไหนส่งไปแล้วบ้าง โดยไม่ต้องเปิด detail ทีละรายการ (เดิมมีแสดงแค่ใน detail modal เท่านั้น)
const defectCrmSyncTones: Record<string, string> = { Linked: "green", Failed: "red", None: "gray" };
const defectCrmSyncLabels: Record<string, string> = { Linked: "ส่งแล้ว", Failed: "ส่งไม่สำเร็จ", None: "ยังไม่ส่ง" };
const testCaseStatusTones: Record<string, string> = { Draft: "gray", Review: "yellow", Ready: "green", Deprecated: "red" };
const defectActionLabels: Record<string, string> = { Created: "สร้าง", Updated: "แก้ไข", StatusChanged: "สถานะ", SeverityChanged: "Severity", Comment: "คอมเมนต์", TestLinked: "เชื่อมโยง Test Case", TestUnlinked: "ยกเลิก Test Case", BulkUpdated: "อัปเดตกลุ่ม", Deleted: "ลบ", CrmSent: "ส่งไป CRM", CrmSyncFailed: "ส่งไป CRM ไม่สำเร็จ", CrmReassigned: "เปลี่ยนผู้รับผิดชอบ CRM", CrmReassignFailed: "เปลี่ยนผู้รับผิดชอบ CRM ไม่สำเร็จ", CrmStatusChanged: "CRM อัปเดตสถานะ/ผู้รับผิดชอบ", CrmReturnedToOwner: "CRM ส่งกลับหาเจ้าของเรื่อง", CrmComment: "คอมเมนต์จาก CRM" };

function fmtAgo(iso?: string | null): string {
  // toUtcDate เติม "Z" ให้ก่อนถ้า backend ส่ง DateTime ที่เป็น UTC มาแบบไม่มี timezone indicator (บั๊ก
  // SQL Server datetime2 ไม่เก็บ Kind — ดู dateTime.ts) ไม่งั้นค่า ms ที่คำนวณจะเพี้ยนไปเท่ากับ timezone
  // offset ของเครื่อง ทำให้ "x ชม./วันที่แล้ว" ผิด
  const parsed = toUtcDate(iso);
  if (!parsed) return "-";
  const ms = Date.now() - parsed.getTime();
  if (ms < 60_000) return "เมื่อสักครู่";
  const m = Math.floor(ms / 60_000);
  if (m < 60) return `${m} นาทีที่แล้ว`;
  const h = Math.floor(m / 60);
  if (h < 24) return `${h} ชม.ที่แล้ว`;
  const d = Math.floor(h / 24);
  if (d < 30) return `${d} วันที่แล้ว`;
  const mo = Math.floor(d / 30);
  if (mo < 12) return `${mo} เดือนที่แล้ว`;
  return `${Math.floor(mo / 12)} ปีที่แล้ว`;
}

export function DefectsPage({ projectId, releaseId, buildId, projectName, releaseLabel, buildLabel, search, onSearchChange, onClearSearch, canEdit, canExport, onOpenTestCase }: { projectId?: string; releaseId?: string; buildId?: string; projectName?: string; releaseLabel?: string; buildLabel?: string; search: string; onSearchChange?: (value: string) => void; onClearSearch?: () => void; canEdit?: boolean; canExport?: boolean; onOpenTestCase?: (testCaseId: string) => void }) {
  const [items, setItems] = useState<DefectItem[]>([]);
  const [listSort, setListSortState] = useState<SortState<DefectSortKey>>(readDefectSort);
  const setListSort = (next: SortState<DefectSortKey>) => { setListSortState(next); setPage(1); try { localStorage.setItem(defectSortStorageKey, JSON.stringify(next)); } catch { /* ไม่จำก็ได้ */ } };
  const listSortBy = `${listSort.key}_${listSort.dir}`;
  const [loading, setLoading] = useState(true);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useUserPageSize("qa.defects.pageSize");
  // Module ที่หน้าอื่นส่งมา (qa.targetModuleId เช่นจาก Dashboard) ใช้เป็นตัวกรองเริ่มต้นครั้งเดียวแล้วลบทิ้ง
  const [moduleFilter, setModuleFilter] = useState(() => {
    try { const target = localStorage.getItem("qa.targetModuleId") ?? ""; localStorage.removeItem("qa.targetModuleId"); return target; } catch { return ""; }
  });
  const [jumpToList, setJumpToList] = useState(false);
  const [severityFilter, setSeverityFilter] = useState("");
  const [priorityFilter, setPriorityFilter] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [assigneeFilter, setAssigneeFilter] = useState("");
  const [modules, setModules] = useState<ModuleItem[]>([]);
  const [users, setUsers] = useState<UserLookup[]>([]);
  const [selectedIds, setSelectedIds] = useState<string[]>([]);  const [activities, setActivities] = useState<DefectActivityItem[]>([]);
  const [detail, setDetail] = useState<DefectItem | null>(null);
  const [linkedCases, setLinkedCases] = useState<DefectTestCaseItem[]>([]);
  const [_detailLoading, setDetailLoading] = useState(false);
  const [detailError, setDetailError] = useState("");
  const [comments, setComments] = useState<DefectComment[]>([]);
  const commentImageUrls = useAuthedAttachmentUrls(apiUrl, detail?.defectId, comments.flatMap((c) => c.attachments.map((a) => a.attachmentId)));
  // รูปของ activity "แนบรูปภาพ n ไฟล์" (API จับคู่ให้) — คลิกรูปย่อเปิด ImageLightbox ของชุดรูปในรายการนั้น
  const activityImageUrls = useAuthedAttachmentUrls(apiUrl, detail?.defectId, activities.flatMap((a) => a.attachments?.map((x) => x.attachmentId) ?? []));
  const [activityViewer, setActivityViewer] = useState<{ attachments: DefectCommentAttachment[]; index: number } | null>(null);
  // แผงขวาของรายละเอียด: คอมเมนต์ (ค่าเริ่มต้น) หรือ Activities — คอมเมนต์เรียงเก่า → ใหม่ จึงเลื่อนไปล่างสุดให้เห็นล่าสุดก่อน
  const [detailTab, setDetailTab] = useState<"comments" | "activities">("comments");
  const commentScrollRef = useRef<HTMLDivElement>(null);
  useEffect(() => { const el = commentScrollRef.current; if (el) el.scrollTop = el.scrollHeight; }, [comments, detailTab]);
  const [codeCopied, setCodeCopied] = useState(false);
  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<DefectItem | null>(null);
  const [formTitle, setFormTitle] = useState("");
  const [formSeverity, setFormSeverity] = useState("Medium");
  const [formStatus, setFormStatus] = useState("Open");
  // ผลการปิด (Phase 6): mode quick = ปุ่มเปลี่ยนสถานะ, form = บันทึกฟอร์มที่เลือกสถานะปิด, status null = แก้ผลอย่างเดียว
  const [resolving, setResolving] = useState<{ item: DefectItem | null; status: string | null; mode: "quick" | "form" | "edit" } | null>(null);
  const masterOptions = useMasterOptions();
  const rootCauses = masterOptions("DefectRootCause");
  const rootCauseName = (value: string) => rootCauses.find((x) => x.value === value)?.displayName ?? value;
  const [formModuleId, setFormModuleId] = useState("");
  const [formDescription, setFormDescription] = useState("");
  const [formStepsToReproduce, setFormStepsToReproduce] = useState("");
  const [formExpectedResult, setFormExpectedResult] = useState("");
  const [formActualResult, setFormActualResult] = useState("");
  const [formAssigneeUserId, setFormAssigneeUserId] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [reload, setReload] = useState(0);
  const [summaryStats, setSummaryStats] = useState(emptyDefectStats);
  const [statsLoaded, setStatsLoaded] = useState(false);
  const [statsError, setStatsError] = useState("");
  const [statsReload, setStatsReload] = useState(0);
  const [exportingModulePdf, setExportingModulePdf] = useState(false);
  const [showAllModules, setShowAllModules] = useState(false);
  const [crmDialogOpen, setCrmDialogOpen] = useState(false);
  // Defect ที่ dialog นี้กำลังทำงานด้วย — แยกจาก `detail` (Defect ที่เปิด detail modal อยู่) เพราะตอนนี้
  // เปิด dialog นี้ได้ 2 ทาง: จากปุ่มใน detail modal (item = detail อยู่แล้ว) หรือจากคอลัมน์ CRM ในตาราง
  // list โดยตรง (ไม่ได้เปิด detail modal เลย) — ต้องรู้ว่ากำลังส่งให้ Defect ตัวไหนโดยไม่พึ่ง `detail`
  const [crmTargetItem, setCrmTargetItem] = useState<DefectItem | null>(null);
  const [crmDevUsers, setCrmDevUsers] = useState<{ staffCode: string; name: string; email?: string | null }[]>([]);
  const [crmDevUsersLoading, setCrmDevUsersLoading] = useState(false);
  const [crmDevUsersError, setCrmDevUsersError] = useState("");
  const [crmAssignTo, setCrmAssignTo] = useState("");
  const [crmSending, setCrmSending] = useState(false);
  const defectRequestVersion = useRef(0);
  // "send" = ยังไม่เคยผูก CRM มาก่อน (สร้าง ticket ใหม่); "reassign" = ผูกแล้ว แค่เปลี่ยนผู้รับผิดชอบบน ticket เดิม
  const [crmMode, setCrmMode] = useState<"send" | "reassign">("send");
  const headers = useMemo(() => ({ "Content-Type": "application/json", Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` }), []);
  const load = useCallback(() => {
    void reload; // refresh the list after create, edit, delete, or bulk updates
    const requestVersion = ++defectRequestVersion.current;
    if (!projectId) { setItems([]); setTotalCount(0); setLoading(false); return; }
    setLoading(true); setError("");
    const q = new URLSearchParams({ projectId, ...(releaseId && { releaseId }), ...(buildId && { buildId }), ...(search && { search }), ...(moduleFilter && moduleFilter !== "unassigned" && { moduleId: moduleFilter }), ...(moduleFilter === "unassigned" && { unassignedModule: "true" }), ...(severityFilter && { severity: severityFilter }), ...(priorityFilter && { priority: priorityFilter }), ...(statusFilter && { status: statusFilter }), ...(assigneeFilter && { assigneeUserId: assigneeFilter }), page: String(page), size: String(pageSize) });
    q.set("sortBy", listSortBy);
    fetch(`${apiUrl}/defects?${q}`, { headers }).then(async r => {
      if (!r.ok) throw new Error("โหลด Defect ไม่สำเร็จ");
      const json = await r.json();
      const response = json && typeof json === "object" ? json as Record<string, unknown> : null;
      const nestedItems = response?.items && typeof response.items === "object" ? response.items as Record<string, unknown> : null;
      const rows = Array.isArray(json) ? json
        : Array.isArray(response?.rows) ? response.rows
        : Array.isArray(response?.items) ? response.items
        : Array.isArray(nestedItems?.rows) ? nestedItems.rows
        : [];
      const total = Number(response?.total ?? response?.totalCount ?? nestedItems?.total ?? nestedItems?.totalCount ?? rows.length);
      if (requestVersion !== defectRequestVersion.current) return;
      setItems(rows as DefectItem[]);
      setTotalCount(Number.isFinite(total) ? total : rows.length);
    }).catch(e => { if (requestVersion === defectRequestVersion.current) setError(e instanceof Error ? e.message : "โหลด Defect ไม่สำเร็จ"); }).finally(() => { if (requestVersion === defectRequestVersion.current) setLoading(false); });
  }, [projectId, releaseId, buildId, search, moduleFilter, severityFilter, priorityFilter, statusFilter, assigneeFilter, listSortBy, page, pageSize, headers, reload]);
  useEffect(load, [load]);
  // การ์ดสรุปด้านบน — ขอบเขตตาม Project/Release/Build ที่เลือก (ไม่ผูกกับตัวกรองของตารางด้านล่าง) เพื่อให้เห็น
  // ภาพรวมทั้งหมดของบริบทนั้นเสมอ ไม่ว่าจะกรอง/ค้นหาตารางด้วยอะไรอยู่
  useEffect(() => {
    if (!projectId) { setSummaryStats(emptyDefectStats); setStatsLoaded(false); setStatsError(""); return; }
    setStatsLoaded(false);
    setStatsError("");
    const controller = new AbortController();
    const q = new URLSearchParams({ projectId, ...(releaseId && { releaseId }), ...(buildId && { buildId }) });
    fetch(`${apiUrl}/defects/stats?${q}`, { headers, signal: controller.signal }).then(r => { if (!r.ok) throw new Error(`โหลดสรุป Defect ไม่สำเร็จ (${r.status})`); return r.json(); }).then(d => {
      if (controller.signal.aborted) return;
      if (d && typeof d === "object") setSummaryStats({
        total: d.total ?? 0, open: d.open ?? 0, inProgress: d.inProgress ?? 0, resolved: d.resolved ?? 0, closed: d.closed ?? 0, rejected: d.rejected ?? 0,
        critical: d.critical ?? 0, high: d.high ?? 0, medium: d.medium ?? 0, low: d.low ?? 0,
        oldestOpenAgeDays: d.oldestOpenAgeDays ?? 0, unassignedActive: d.unassignedActive ?? 0, closureRate: d.closureRate ?? 0,
        modules: Array.isArray(d.modules) ? d.modules as DefectModuleCount[] : [],
      });
      if (d && typeof d === "object") setStatsLoaded(true);
    }).catch((e) => { if (!controller.signal.aborted) setStatsError(e instanceof Error ? e.message : "โหลดสรุป Defect ไม่สำเร็จ"); });
    return () => controller.abort();
  }, [projectId, releaseId, buildId, headers, reload, statsReload]);
  useEffect(() => {
    if (!projectId) return;
    Promise.all([
      getJson<ModuleItem[]>(`${apiUrl}/projects/${projectId}/modules`),
      getJson<UserLookup[]>(`${apiUrl}/lookups/users`),
    ]).then(([m, u]) => { setModules(m.filter(x => x.isActive)); setUsers(u); })
      .catch(() => setError("โหลดรายการ Module / ผู้ใช้สำหรับตัวกรองและฟอร์มไม่สำเร็จ — ตัวเลือกอาจว่าง"));
  }, [projectId]);
  useEffect(() => { setPage(1); }, [search, moduleFilter, severityFilter, priorityFilter, statusFilter, assigneeFilter]);
  useEffect(() => {
    if (!jumpToList || loading) return;
    const target = document.getElementById("defect-list-results");
    target?.focus({ preventScroll: true });
    target?.scrollIntoView({ behavior: "smooth", block: "start" });
    setJumpToList(false);
  }, [jumpToList, loading, items]);
  const openModuleDefects = (moduleId: string | null) => {
    onClearSearch?.();
    setModuleFilter(moduleId ?? "unassigned");
    setSeverityFilter("");
    setPriorityFilter("");
    setStatusFilter("");
    setAssigneeFilter("");
    setPage(1);
    setJumpToList(true);
  };
  const openForm = (item?: DefectItem) => {
    setEditing(item ?? null);
    setFormTitle(item?.title ?? "");
    setFormSeverity(item?.severity ?? "Medium");
    setFormStatus(item?.status ?? "Open");
    setFormModuleId(item?.moduleId ?? "");
    setFormDescription(item?.description ?? "");
    setFormStepsToReproduce(item?.stepsToReproduce ?? "");
    setFormExpectedResult(item?.expectedResult ?? "");
    setFormActualResult(item?.actualResult ?? "");
    setFormAssigneeUserId(item?.assigneeUserId ?? "");
    setFormOpen(true);
  };
  const saveForm = async (choice?: DefectResolutionChoice) => {
    // เปลี่ยนเป็นสถานะปิดจากฟอร์ม = ต้องบันทึกผลการปิดก่อน (แก้ Defect ที่ปิดอยู่แล้วไม่ถามซ้ำ)
    if (!choice && defectClosingStatuses.includes(formStatus) && (!editing || !defectClosingStatuses.includes(editing.status))) { setResolving({ item: editing, status: formStatus, mode: "form" }); return; }
    const picked = choice ? splitResolutionChoice(choice) : null;
    setSaving(true); setError("");
    try {
      const body = { moduleId: formModuleId || null, title: formTitle, severity: formSeverity, status: formStatus, description: formDescription || null, stepsToReproduce: formStepsToReproduce || null, expectedResult: formExpectedResult || null, actualResult: formActualResult || null, assigneeUserId: formAssigneeUserId || null, releaseId: releaseId || null, buildId: buildId || null, resolution: picked?.resolution ?? null, fixedInBuildId: picked?.fixedInBuildId ?? null, verifiedInBuildId: picked?.verifiedInBuildId ?? null };
      const response = await fetch(editing ? `${apiUrl}/defects/${editing.defectId}` : `${apiUrl}/defects`, { method: editing ? "PUT" : "POST", headers, body: JSON.stringify(body) });
      if (!response.ok) throw new Error(await readError(response, "บันทึก Defect ไม่สำเร็จ"));
      setFormOpen(false);
      setNotice(editing ? "แก้ไข Defect แล้ว" : "สร้าง Defect แล้ว");
      setReload(x => x + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "บันทึกไม่สำเร็จ"); } finally { setSaving(false); }
  };
  const removeDefect = async (item: DefectItem) => {
    if (!await confirmDialog(`ลบ ${item.defectCode} ใช่หรือไม่?`)) return;
    const response = await fetch(`${apiUrl}/defects/${item.defectId}`, { method: "DELETE", headers });
    if (response.ok) { setNotice(`ลบ ${item.defectCode} แล้ว`); setReload(x => x + 1); }
  };
  const quickStatus = async (item: DefectItem, status: string) => {
    // Resolved / Closed / Rejected ต้องระบุผลการปิด (ใช้หน้าต่างผลการปิดแทนกล่องยืนยันเดิม)
    if (defectClosingStatuses.includes(status)) { setResolving({ item, status, mode: "quick" }); return; }
    // กลับเป็น Open/In Progress จาก Resolved/Closed = Reopen (นับจำนวนครั้ง ล้างผลการปิดเดิม)
    const reopen = item.status === "Resolved" || item.status === "Closed";
    if (reopen && !await confirmDialog({ title: `Reopen ${item.defectCode}`, message: `ปัญหายังอยู่หลังแก้ใช่หรือไม่? Defect จะกลับเป็น ${status}, นับเป็น Reopen ครั้งที่ ${(item.reopenCount ?? 0) + 1} และล้างผลการปิดเดิม (ประวัติยังอยู่ใน Activity)`, confirmLabel: "Reopen" })) return;
    const response = await fetch(`${apiUrl}/defects/${item.defectId}/status`, { method: "PATCH", headers, body: JSON.stringify({ status }) });
    if (response.ok) { setNotice(reopen ? `Reopen ${item.defectCode} แล้ว` : `เปลี่ยนสถานะ ${item.defectCode} เป็น ${status}`); setReload(x => x + 1); }
    else notify(await readError(response, `เปลี่ยนสถานะ ${item.defectCode} ไม่สำเร็จ`), "error");
  };
  const confirmResolution = async (value: DefectResolutionChoice) => {
    if (!resolving) return;
    const { item, status, mode } = resolving;
    if (mode === "form") { setResolving(null); await saveForm(value); return; }
    if (!item) return;
    const { resolution, fixedInBuildId, verifiedInBuildId } = splitResolutionChoice(value);
    const response = status
      ? await fetch(`${apiUrl}/defects/${item.defectId}/status`, { method: "PATCH", headers, body: JSON.stringify({ status, resolution, fixedInBuildId, verifiedInBuildId }) })
      : await fetch(`${apiUrl}/defects/${item.defectId}/resolution`, { method: "PUT", headers, body: JSON.stringify(resolution) });
    if (!response.ok) throw new Error(await readError(response, "บันทึกผลการปิดไม่สำเร็จ"));
    const updated = await response.json() as DefectItem;
    setResolving(null);
    setNotice(status ? `เปลี่ยนสถานะ ${item.defectCode} เป็น ${status} และบันทึกผลการปิดแล้ว` : `บันทึกผลการปิด ${item.defectCode} แล้ว`);
    setReload(x => x + 1);
    if (mode === "edit") setDetail({ ...item, ...updated });
  };
  // เตือน Defect ที่อาจซ้ำตอนสร้าง (ค้นคลังความรู้ของ Project นี้) — ไม่บล็อกการบันทึก
  const [similar, setSimilar] = useState<SimilarDefect[]>([]);
  const similarText = useDebounced(formOpen && !editing ? `${formTitle} ${formDescription}`.trim() : "", 600);
  useEffect(() => {
    if (!projectId || similarText.length < 6) { setSimilar([]); return; }
    const controller = new AbortController();
    getJson<SimilarDefect[]>(`${apiUrl}/defects/similar?${new URLSearchParams({ projectId, text: similarText.slice(0, 300) })}`, controller.signal)
      .then((rows) => setSimilar(Array.isArray(rows) ? rows : []))
      .catch((e) => { if (!isAbortError(e)) setSimilar([]); });
    return () => controller.abort();
  }, [similarText, projectId]);
  const openCrmDialog = async (mode: "send" | "reassign", item: DefectItem) => {
    setCrmMode(mode); setCrmTargetItem(item);
    setCrmAssignTo(""); setCrmDialogOpen(true); setCrmDevUsersLoading(true); setCrmDevUsersError("");
    try {
      const response = await fetch(`${apiUrl}/defects/crm/dev-users`, { headers });
      if (!response.ok) { const p = await response.json().catch(() => null); throw new Error(p?.detail ?? "โหลดรายชื่อผู้รับผิดชอบไม่สำเร็จ"); }
      setCrmDevUsers(await response.json());
    } catch (e) { setCrmDevUsersError(e instanceof Error ? e.message : "โหลดรายชื่อผู้รับผิดชอบไม่สำเร็จ"); }
    finally { setCrmDevUsersLoading(false); }
  };
  // ป้องกันกดซ้ำระหว่างรอ response — backend เองก็กัน re-send ซ้ำอีกชั้นด้วย CrmSyncStatus=="Linked" (409 ตอน send,
  // เช็ค Linked แล้วเท่านั้นตอน reassign) — mode "reassign" ยิงคนละ endpoint แต่ใช้ dialog/ผู้รับผิดชอบชุดเดียวกัน
  const sendToCrm = async () => {
    const item = crmTargetItem;
    if (!item || crmSending) return;
    setCrmSending(true);
    try {
      const url = crmMode === "reassign" ? `${apiUrl}/defects/${item.defectId}/crm-reassign` : `${apiUrl}/defects/${item.defectId}/send-to-crm`;
      const response = await fetch(url, { method: "POST", headers, body: JSON.stringify({ assignToStaffCode: crmAssignTo }) });
      const body = await response.json().catch(() => null);
      if (!response.ok) throw new Error(body?.detail ?? (crmMode === "reassign" ? "เปลี่ยนผู้รับผิดชอบใน CRM ไม่สำเร็จ" : "ส่งไป CRM ไม่สำเร็จ"));
      setNotice(crmMode === "reassign" ? `เปลี่ยนผู้รับผิดชอบใน CRM Ticket #${item.crmTicketId} สำเร็จ` : `ส่งไป CRM สำเร็จ Ticket #${body.crmTicketId}`);
      setCrmDialogOpen(false);
      setReload(x => x + 1);
      // เปิด/รีเฟรช detail modal ต่อให้เห็น badge/activity อัปเดตทันที เฉพาะกรณีเปิด dialog นี้มาจาก detail
      // modal ของ Defect ตัวเดียวกันอยู่แล้ว — ถ้าส่งจากคอลัมน์ CRM ในตาราง list โดยตรง (ไม่ได้เปิด detail
      // ไว้) ก็ไม่ต้องเด้ง detail มาให้ ปล่อยให้ตาราง reload แล้วเห็น badge เปลี่ยนในแถวเดิมพอ
      if (detail && detail.defectId === item.defectId) openDetail(item);
    } catch (e) { notify(e instanceof Error ? e.message : "ดำเนินการไม่สำเร็จ", "error"); }
    finally { setCrmSending(false); }
  };
  const openDetail = async (item: DefectItem) => {
    setDetail(item); setActivities([]); setActivityViewer(null); setDetailTab("comments"); setLinkedCases([]); setComments([]); setCodeCopied(false); setDetailLoading(true); setDetailError("");
    try {
      // โหลดไม่สำเร็จต้องบอก — เดิมแสดงเป็น "ยังไม่มีกิจกรรม / ยังไม่มี Test Case ที่เชื่อมโยง"
      const failed: string[] = [];
      const [actRes, tcRes, commentRes] = await Promise.all([
        getJson<any[]>(`${apiUrl}/defects/${item.defectId}/activities`).catch((): any[] => { failed.push("ประวัติกิจกรรม"); return []; }),
        getJson<DefectTestCaseItem[]>(`${apiUrl}/defects/${item.defectId}/test-cases`).catch((): DefectTestCaseItem[] => { failed.push("Test Case ที่เชื่อมโยง"); return []; }),
        getJson<DefectComment[]>(`${apiUrl}/defects/${item.defectId}/comments`).catch((): DefectComment[] => { failed.push("คอมเมนต์"); return []; }),
      ]);
      setComments(Array.isArray(commentRes) ? commentRes : []);
      if (failed.length) setDetailError(`โหลด${failed.join(" และ ")}ไม่สำเร็จ — ปิดแล้วเปิดใหม่เพื่อลองอีกครั้ง`);
      setActivities(Array.isArray(actRes) ? actRes.map((a: any) => ({ activityId: a.activityId ?? a.defectActivityId ?? "", actionType: a.actionType ?? a.activityType ?? "", message: a.message ?? a.description ?? "", actorUserId: a.actorUserId ?? a.performedByUserId ?? null, actorName: a.actorName ?? null, createdAt: a.createdAt ?? a.performedAt ?? "", performedAt: a.performedAt ?? a.createdAt ?? "", attachments: Array.isArray(a.attachments) ? a.attachments : [] })) : []);
      setLinkedCases(Array.isArray(tcRes) ? tcRes : []);
    } catch {} finally { setDetailLoading(false); }
  };
  // Defect ที่หน้าอื่นส่งมาให้เปิด (qa.targetDefectId หรือ #/defects?defectId= เช่น จากผลค้นหาคลังความรู้) — เปิดรายละเอียดครั้งเดียวแล้วลบทิ้ง
  // ดึงด้วย id โดยตรงเพราะ Defect อาจไม่อยู่ในหน้ารายการ/ตัวกรองปัจจุบัน; API ตรวจสิทธิ์ Project ให้
  const targetDefectHandled = useRef(false);
  useEffect(() => {
    if (targetDefectHandled.current) return;
    targetDefectHandled.current = true;
    let targetId = new URLSearchParams(window.location.hash.split("?")[1] ?? "").get("defectId") ?? "";
    try { targetId = localStorage.getItem("qa.targetDefectId") || targetId; localStorage.removeItem("qa.targetDefectId"); } catch { /* storage ใช้ไม่ได้ — ใช้ค่าจาก hash */ }
    if (!targetId) return;
    getJson<DefectItem>(`${apiUrl}/defects/${encodeURIComponent(targetId)}`)
      .then((item) => { void openDetail(item); })
      .catch(() => notify("เปิด Defect ไม่ได้ — อาจถูกลบแล้วหรือคุณไม่มีสิทธิ์ใน Project นั้น", "error"));
  });
  // ข้อความ + รูป (สูงสุด 5 รูป) ส่งเป็น multipart — endpoint นี้ยังรอ sync ไป CRM ด้วย (best-effort, ดู
  // CrmSendToCrmService.AppendCommentAsync) ถ้า Defect ผูก CRM แล้ว จึงอาจใช้เวลานานกว่าปกติเล็กน้อย; composer กันกดซ้ำให้เอง
  const submitComment = async (body: string, files: File[]): Promise<boolean> => {
    if (!detail) return false;
    const form = new FormData();
    form.append("body", body);
    files.forEach((file) => form.append("files", file));
    const response = await fetch(`${apiUrl}/defects/${detail.defectId}/comments`, { method: "POST", headers: authHeaders(), body: form });
    if (!response.ok) { const problem = await response.json().catch(() => null) as { detail?: string } | null; throw new Error(problem?.detail ?? "ส่งคอมเมนต์ไม่สำเร็จ"); }
    await openDetail(detail);
    return true;
  };
  const bulkStatus = async (status: string) => {
    if (canEdit === false || !selectedIds.length) return;
    if (!await confirmDialog({ title: "เปลี่ยนสถานะหลายรายการ", message: `เปลี่ยนสถานะ Defect ${selectedIds.length} รายการเป็น ${status} ใช่หรือไม่?`, confirmLabel: `เปลี่ยนเป็น ${status}` })) return;
    const response = await fetch(`${apiUrl}/defects/bulk`, { method: "POST", headers, body: JSON.stringify({ ids: selectedIds, status }) });
    if (!response.ok) { notify(await readError(response, `เปลี่ยนสถานะ ${selectedIds.length} รายการไม่สำเร็จ`), "error"); return; }
    // รายการที่ผิดกฎวงจร Defect (เช่น ปิดตรงจาก Open โดยไม่มีผลการปิด) ข้ามไป — รายการอื่นบันทึกแล้ว
    const result = await response.json() as { updated: number; failed?: { defectCode: string; error: string }[] };
    const failed = result.failed ?? [];
    setNotice(`เปลี่ยนสถานะเป็น ${status} แล้ว ${result.updated} รายการ`);
    if (failed.length) notify(`ข้าม ${failed.length} รายการ: ${failed[0].defectCode} — ${failed[0].error}${failed.length > 1 ? ` (และอีก ${failed.length - 1} รายการ)` : ""}`, "error");
    setSelectedIds([]); setReload(x => x + 1);
  };
  const exportCsv = () => {
    const rows = [["Defect ID", "Title", "Severity", "Status", "CRM", "Module", "Created", "Assignee"], ...items.map(x => [x.defectCode, x.title, x.severity, x.status, defectCrmSyncLabels[x.crmSyncStatus ?? "None"] ?? "ยังไม่ส่ง", modules.find(m => m.moduleId === x.moduleId)?.moduleName ?? "", formatThaiDateTime(x.createdAt, { day: "2-digit", month: "2-digit", year: "numeric" }), x.assigneeName ?? ""])];
    const csv = "\ufeff" + rows.map(row => row.map(v => `"${String(v).replaceAll('"', '""')}"`).join(",")).join("\r\n");
    const url = URL.createObjectURL(new Blob([csv], { type: "text/csv;charset=utf-8" }));
    const a = document.createElement("a"); a.href = url; a.download = "defects.csv"; a.click(); URL.revokeObjectURL(url);
  };
  const downloadModulePdf = async () => {
    if (!statsLoaded || exportingModulePdf) return;
    setExportingModulePdf(true);
    setError("");
    try {
      await exportDefectModulePdf(summaryStats, { project: projectName || "ไม่ระบุ", release: releaseLabel || "ทุก Release", build: buildLabel || "ทุก Build" });
    } catch {
      setError("สร้างไฟล์ PDF ไม่สำเร็จ กรุณาลองใหม่อีกครั้ง");
    } finally {
      setExportingModulePdf(false);
    }
  };
  const toggleSelectAll = () => { setSelectedIds(selectedIds.length === items.length ? [] : items.map(x => x.defectId)); };
  const pageCount = Math.max(1, Math.ceil(totalCount / pageSize));
  const activeDefectFilterCount = [search, moduleFilter, severityFilter, priorityFilter, statusFilter, assigneeFilter].filter(Boolean).length;
  const clearDefectFilters = () => {
    onClearSearch?.();
    setModuleFilter("");
    setSeverityFilter("");
    setPriorityFilter("");
    setStatusFilter("");
    setAssigneeFilter("");
    setPage(1);
  };
  const visibleModuleRanking = showAllModules ? summaryStats.modules : summaryStats.modules.slice(0, moduleRankingLimit);
  // สถานะของภาพรวม + ชิปกรองรายการ (จำนวนเป็นของทั้งขอบเขต Project/Release/Build ไม่ขึ้นกับตัวกรองอื่น)
  const defectStatusParts = [
    { key: "open", value: "Open", label: "Open", count: summaryStats.open },
    { key: "inprogress", value: "In Progress", label: "In Progress", count: summaryStats.inProgress },
    { key: "resolved", value: "Resolved", label: "Resolved", count: summaryStats.resolved },
    { key: "closed", value: "Closed", label: "Closed", count: summaryStats.closed },
    { key: "rejected", value: "Rejected", label: "Rejected", count: summaryStats.rejected },
  ];
  const defectContextLabel = [projectName, releaseLabel, buildLabel].filter(Boolean).join(" · ") || "ยังไม่ได้เลือก Project / Release / Build";
  if (loading && !items.length) return <article className="card empty"><div className="spinner" /><p>กำลังโหลด Defect...</p></article>;
  return <div className="defect-page df-page">
    {error && <div className="inline-alert error"><span>{error}</span><button type="button" aria-label="ปิดข้อความ" onClick={() => setError("")}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>}
    {notice && <div className="inline-alert success"><span>{notice}</span><button type="button" aria-label="ปิดข้อความ" onClick={() => setNotice("")}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>}
    {/* ภาพรวมการ์ดเดียว (แทน intro + KPI 6 ใบ + กราฟความรุนแรง + ข้อมูลประกอบ + อันดับโมดูล) — ขอบเขต Project/Release/Build เท่านั้น ไม่ผูกตัวกรองตาราง */}
    <section className="card df-overview" aria-label="ภาพรวม Defect">
      <div className="df-ov-status">
        <div className="df-ov-head">
          <div className="df-ov-total"><strong>{summaryStats.total.toLocaleString()}</strong><span>Defect ทั้งหมด</span></div>
          <small className="df-ov-scope" title={defectContextLabel}><span className="material-symbols-outlined" aria-hidden="true">filter_alt</span>{defectContextLabel}</small>
        </div>
        <div className="df-ov-bar" role="img" aria-label={defectStatusParts.map((p) => `${p.label} ${p.count}`).join(", ")}>
          {summaryStats.total > 0 ? defectStatusParts.filter((p) => p.count).map((p) => <span key={p.key} className={`is-${p.key}`} style={{ flexGrow: p.count }} title={`${p.label} ${p.count.toLocaleString()} (${defectPct(p.count, summaryStats.total)}%)`} />) : null}
        </div>
        <ul className="df-ov-legend">
          {defectStatusParts.map((p) => <li key={p.key}><span className={`df-dot is-${p.key}`} aria-hidden="true" /><span>{p.label}</span><b>{p.count.toLocaleString()}</b><small>{defectPct(p.count, summaryStats.total)}%</small></li>)}
        </ul>
        <dl className="df-ov-facts">
          <div><dt>อัตราปิด (Closure)</dt><dd>{summaryStats.closureRate}%</dd></div>
          <div title="อายุของ Open/In Progress ที่นานที่สุด"><dt>ค้างนานที่สุด</dt><dd className={summaryStats.oldestOpenAgeDays > 30 ? "is-warning" : undefined}>{summaryStats.oldestOpenAgeDays.toLocaleString()} วัน</dd></div>
          <div title="Open/In Progress ที่ยังไม่มีผู้รับผิดชอบ"><dt>ยังไม่มอบหมาย</dt><dd className={summaryStats.unassignedActive > 0 ? "is-warning" : undefined}>{summaryStats.unassignedActive.toLocaleString()}</dd></div>
        </dl>
      </div>
      <div className="df-ov-severity">
        <h3>ความรุนแรง (Severity)</h3>
        {([["Critical", summaryStats.critical, "critical"], ["High", summaryStats.high, "high"], ["Medium", summaryStats.medium, "medium"], ["Low", summaryStats.low, "low"]] as [string, number, string][]).map(([label, count, key]) => <div key={key} className="df-sev-row">
          <span className="df-sev-label">{label}</span>
          <span className="df-sev-track" aria-hidden="true"><span className={`is-${key}`} style={{ width: `${Math.max(defectPct(count, summaryStats.total), count > 0 ? 3 : 0)}%` }} /></span>
          <b>{count.toLocaleString()}</b>
          <small>{defectPct(count, summaryStats.total)}%</small>
        </div>)}
      </div>
      <div className="df-ov-modules">
        <div className="df-ov-modules-head">
          <h3>Defect ตามโมดูล <small>{summaryStats.modules.length.toLocaleString()} กลุ่ม</small></h3>
          {canExport !== false && <button type="button" className="df-ov-link" disabled={!statsLoaded || exportingModulePdf} onClick={downloadModulePdf}>{exportingModulePdf ? <><span className="spinner inline" aria-hidden="true" />กำลังสร้าง...</> : <><span className="material-symbols-outlined" aria-hidden="true">picture_as_pdf</span>PDF</>}</button>}
        </div>
        {statsError ? <div className="inline-alert error" role="alert"><span>{statsError}</span><button type="button" onClick={() => setStatsReload(value => value + 1)}><span className="material-symbols-outlined" aria-hidden="true">refresh</span> ลองใหม่</button></div> : summaryStats.modules.length > 0 ? <>
          <ol className="df-ov-module-list">
            {visibleModuleRanking.map((module, index) => {
              const value = module.moduleId ?? "unassigned";
              return <li key={value}>
                <button type="button" className={moduleFilter === value ? "is-selected" : undefined} aria-pressed={moduleFilter === value} title="กดเพื่อกรองรายการด้านล่างตามโมดูลนี้" aria-label={`ดู Defect ของ ${module.moduleCode ? `${module.moduleCode} ` : ""}${module.moduleName} ${module.count} รายการ`} onClick={() => openModuleDefects(module.moduleId)}>
                  <span className="df-rank">{index + 1}</span>
                  <span className="df-module-name"><b>{module.moduleName}</b>{module.moduleCode && <small>{module.moduleCode}</small>}</span>
                  <span className="df-module-count">{module.count.toLocaleString()}</span>
                  <span className="df-module-track" aria-hidden="true"><span style={{ width: `${(module.count / Math.max(1, summaryStats.modules[0].count)) * 100}%` }} /></span>
                </button>
              </li>;
            })}
          </ol>
          {summaryStats.modules.length > moduleRankingLimit && <button type="button" className="df-ov-link df-ov-toggle" onClick={() => setShowAllModules(value => !value)}>
            <span className="material-symbols-outlined" aria-hidden="true">{showAllModules ? "expand_less" : "expand_more"}</span>
            {showAllModules ? "แสดงเฉพาะอันดับต้น ๆ" : `แสดงทั้งหมด ${summaryStats.modules.length} กลุ่ม`}
          </button>}
        </> : <p className="df-ov-empty">ยังไม่มี Defect ในขอบเขตที่เลือก</p>}
      </div>
    </section>
    <article className="card lp-card" id="defect-list-results" tabIndex={-1} aria-label="รายการ Defect">
      <div className="lp-head">
        <div className="lp-title"><div><strong>{totalCount.toLocaleString()}</strong><span>Defect{activeDefectFilterCount ? ` · กรอง ${activeDefectFilterCount} เงื่อนไข` : ""}</span></div></div>
        <div className="lp-actions">
          <button className="btn" type="button" disabled={!items.length} onClick={exportCsv}><span className="material-symbols-outlined" aria-hidden="true">download</span>Export</button>
          {canEdit !== false && <button className="btn primary" type="button" disabled={!projectId} onClick={() => openForm()}><span className="material-symbols-outlined" aria-hidden="true">add</span>Defect</button>}
        </div>
      </div>
      <label className="lp-sort-mobile">เรียงตาม<select value={`${listSort.key}:${listSort.dir}`} onChange={(e) => { const [key, dir] = e.target.value.split(":"); setListSort({ key: key as DefectSortKey, dir: dir as "asc" | "desc" }); }}>
        {defectSortOptions.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
      </select></label>
      <div className="lp-chips" role="group" aria-label="กรองตามสถานะ">
        <button type="button" aria-pressed={statusFilter === ""} onClick={() => setStatusFilter("")}>ทุกสถานะ <b>{summaryStats.total.toLocaleString()}</b></button>
        {defectStatusParts.map((p) => <button key={p.key} type="button" aria-pressed={statusFilter === p.value} onClick={() => setStatusFilter((current) => current === p.value ? "" : p.value)}><span className={`df-dot is-${p.key}`} aria-hidden="true" />{p.value} <b>{p.count.toLocaleString()}</b></button>)}
      </div>
      <div className="lp-filters" role="group" aria-label="ตัวกรอง Defect">
        {onSearchChange && <label className="lp-search"><span className="material-symbols-outlined" aria-hidden="true">search</span><input type="search" value={search} onChange={(e) => onSearchChange(e.target.value)} placeholder="ค้นหารหัส / ชื่อ Defect" aria-label="ค้นหา Defect" /></label>}
        <select aria-label="Module" className="testcase-module-filter" value={moduleFilter} onChange={e => setModuleFilter(e.target.value)}><option value="">ทุก Module</option>{renderModuleSelectOptions(modules)}{summaryStats.modules.some(x => x.moduleId === null) && <option value="unassigned">ไม่ระบุโมดูล</option>}</select>
        <select aria-label="Severity" value={severityFilter} onChange={e => setSeverityFilter(e.target.value)}><option value="">ทุก Severity</option>{defectSeverities.map(s => <option key={s}>{s}</option>)}</select>
        <select aria-label="Priority ของ Test Case ที่เชื่อมโยง" value={priorityFilter} onChange={e => setPriorityFilter(e.target.value)}><option value="">ทุก Priority</option>{["P0", "P1", "P2", "P3"].map(priority => <option key={priority}>{priority}</option>)}</select>
        <select aria-label="ผู้รับผิดชอบ" value={assigneeFilter} onChange={e => setAssigneeFilter(e.target.value)}><option value="">ทุกผู้รับผิดชอบ</option>{users.map(u => <option key={u.userId} value={u.userId}>{u.displayName}</option>)}</select>
        {activeDefectFilterCount > 0 && <button type="button" className="btn lp-clear" onClick={clearDefectFilters}><span className="material-symbols-outlined" aria-hidden="true">filter_alt_off</span>ล้างตัวกรอง</button>}
      </div>
      {canEdit !== false && selectedIds.length > 0 && (
        <div className="lp-bulk" role="region" aria-label="Defect ที่เลือก">
          <span className="lp-bulk-count"><b>{selectedIds.length.toLocaleString()}</b> เลือกแล้ว</span>
          <span className="lp-bulk-group">
            <button type="button" className="btn" onClick={() => bulkStatus("Resolved")}><span className="material-symbols-outlined" aria-hidden="true">task_alt</span>Resolve</button>
            <button type="button" className="btn" onClick={() => bulkStatus("Closed")}><span className="material-symbols-outlined" aria-hidden="true">lock</span>Close</button>
          </span>
          <span className="lp-bulk-end">
            <button type="button" className="lp-bulk-clear" onClick={() => setSelectedIds([])} aria-label="ยกเลิกการเลือกทั้งหมด" title="ยกเลิกการเลือก"><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
          </span>
        </div>
      )}
      <ListPager position="top" page={page} pageCount={pageCount} totalCount={totalCount} pageSize={pageSize} disabled={loading} onPage={setPage} onPageSize={size => { setPageSize(size); setPage(1); }} />
      <div className="lp-table-wrap">
        <table className={`lp-table df-table${canEdit !== false ? " is-selectable" : ""}`}>
          <thead><tr>
            {canEdit !== false && <th className="lp-col-select"><input type="checkbox" aria-label="เลือกทั้งหน้านี้" checked={selectedIds.length === items.length && items.length > 0} onChange={toggleSelectAll} /></th>}
            <SortHeader label="Defect" sortKey="code" sort={listSort} onSort={setListSort} className="lp-col-item" />
            <th className="lp-col-module">Module</th>
            <SortHeader label="Severity" sortKey="severity" sort={listSort} onSort={setListSort} className="lp-col-badge" />
            <SortHeader label="สถานะ" sortKey="status" sort={listSort} onSort={setListSort} className="lp-col-status" />
            <th className="lp-col-crm lp-hide-md">CRM</th>
            <SortHeader label="อายุ" sortKey="createdAt" sort={listSort} onSort={setListSort} className="lp-col-date" defaultDir="asc" />
            <th className="lp-col-actions lp-actions-4"><span className="sr-only">จัดการ</span></th>
          </tr></thead>
          <tbody>
            {items.map(x => {
              const module = modules.find(m => m.moduleId === x.moduleId);
              const crmStatus = x.crmSyncStatus ?? "None";
              return <tr key={x.defectId} className={selectedIds.includes(x.defectId) ? "is-selected" : undefined}>
                {canEdit !== false && <td className="lp-col-select"><input type="checkbox" aria-label={`เลือก ${x.defectCode}`} checked={selectedIds.includes(x.defectId)} onChange={() => setSelectedIds(prev => prev.includes(x.defectId) ? prev.filter(id => id !== x.defectId) : [...prev, x.defectId])} /></td>}
                <td className="lp-col-item">
                  <button type="button" className="lp-item" onClick={() => openDetail(x)} aria-label={`เปิด ${x.defectCode} ${x.title}`}>
                    <span className="lp-code">{x.defectCode}{(x.releaseCode || x.buildNumber || x.assigneeName) && <span className="lp-meta">{[x.releaseCode, x.buildNumber && `Build ${x.buildNumber}`, x.assigneeName && `ผู้รับผิดชอบ ${x.assigneeName}`].filter(Boolean).join(" · ")}</span>}</span>
                    <span className="lp-name">{x.title}</span>
                  </button>
                </td>
                <td className="lp-col-module">{module ? <span className="lp-module" title={`${module.moduleCode} · ${module.moduleName}`}><b>{module.moduleName}</b><small>{module.moduleCode}</small></span> : <span className="lp-muted">ไม่ระบุโมดูล</span>}</td>
                <td className="lp-col-badge"><Badge tone={defectSeverityTones[x.severity] ?? "blue"}>{x.severity}</Badge></td>
                <td className="lp-col-status"><Badge tone={defectStatusTones[x.status] ?? "gray"}>{x.status}</Badge></td>
                <td className="lp-col-crm lp-hide-md">
                  {canEdit !== false && crmStatus === "None"
                    ? <button type="button" className="df-crm-send" title={`ส่ง ${x.defectCode} ไป CRM`} aria-label={`ส่ง ${x.defectCode} ไป CRM`} onClick={() => openCrmDialog("send", x)}><span className="material-symbols-outlined" aria-hidden="true">upload</span>ส่งไป CRM</button>
                    : <Badge tone={defectCrmSyncTones[crmStatus] ?? "gray"}>{defectCrmSyncLabels[crmStatus] ?? "ยังไม่ส่ง"}</Badge>}
                </td>
                <td className="lp-col-date" title={`สร้าง ${fmtAgo(x.createdAt)}`}><span className="lp-date"><span className={defectAgeDays(x.createdAt) > 30 && !["Resolved", "Closed", "Rejected"].includes(x.status) ? "df-age-old" : undefined}>{defectAgeDays(x.createdAt)} วัน</span><small>{fmtAgo(x.createdAt)}</small></span></td>
                <td className="lp-col-actions lp-actions-4"><div className="lp-row-actions">
                  {canEdit !== false && x.status === "Open" && <button type="button" title="เริ่มดำเนินการ" aria-label={`เริ่มดำเนินการ ${x.defectCode}`} onClick={() => quickStatus(x, "In Progress")}><span className="material-symbols-outlined" aria-hidden="true">play_arrow</span></button>}
                  {canEdit !== false && x.status === "In Progress" && <button type="button" title="Resolve" aria-label={`Resolve ${x.defectCode}`} onClick={() => quickStatus(x, "Resolved")}><span className="material-symbols-outlined" aria-hidden="true">task_alt</span></button>}
                  {canEdit !== false
                    ? <button type="button" title="แก้ไข" aria-label={`แก้ไข ${x.defectCode}`} onClick={() => openForm(x)}><span className="material-symbols-outlined" aria-hidden="true">edit</span></button>
                    : <button type="button" title="ดูรายละเอียด" aria-label={`ดูรายละเอียด ${x.defectCode}`} onClick={() => openDetail(x)}><span className="material-symbols-outlined" aria-hidden="true">info</span></button>}
                  {canEdit !== false && <button type="button" className="danger" title="ลบ" aria-label={`ลบ ${x.defectCode}`} onClick={() => removeDefect(x)}><span className="material-symbols-outlined" aria-hidden="true">delete</span></button>}
                </div></td>
              </tr>;
            })}
            {!loading && !items.length && <tr className="lp-empty-row"><td colSpan={canEdit !== false ? 8 : 7}><div className="lp-empty"><span className="material-symbols-outlined" aria-hidden="true">search_off</span><b>{activeDefectFilterCount ? "ไม่พบ Defect ตามตัวกรองที่เลือก" : "ยังไม่มี Defect ในขอบเขตที่เลือก"}</b>{activeDefectFilterCount > 0 && <button type="button" className="btn" onClick={clearDefectFilters}><span className="material-symbols-outlined" aria-hidden="true">filter_alt_off</span> ล้างตัวกรอง</button>}</div></td></tr>}
          </tbody>
        </table>
      </div>
      <ListPager position="bottom" page={page} pageCount={pageCount} totalCount={totalCount} pageSize={pageSize} disabled={loading} onPage={setPage} onPageSize={size => { setPageSize(size); setPage(1); }} />
    </article>
    {/* หน้าสร้าง/แก้ไข Defect ปรับให้ใช้ภาษาภาพเดียวกับหน้ารายละเอียด Defect (defect-detail ด้านล่าง) —
        eyebrow เหนือหัวข้อ, ส่วนต่างๆ ใช้ .cycle-detail-section (ไอคอน+h3) แทน label เดี่ยวๆ, และ
        Description/Steps to Reproduce กับ Expected/Actual Result จัดเป็น 2 คอลัมน์ (.defect-detail-split)
        เหมือนที่หน้ารายละเอียดจัดไว้เป๊ะๆ ให้ตอนแก้ไขรู้สึกเหมือนกำลังดู/แก้ข้อมูลชุดเดียวกันต่อเนื่องกัน */}
    {formOpen && <ModalShell label={editing ? "แก้ไข Defect" : "สร้าง Defect"} className="defect-form-modal" onDismiss={() => setFormOpen(false)}>
        <div className="modal-head">
          <div className="modal-head-title-group">
            <div>
              <span className="cycle-detail-eyebrow">DEFECT</span>
              <h2>{editing ? "แก้ไข" : "สร้าง"} Defect</h2>
            </div>
          </div>
          <button aria-label="ปิดหน้าต่าง" onClick={() => setFormOpen(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
        </div>
        <section className="cycle-detail-section">
          <h3><span aria-hidden="true">▢</span> ข้อมูลทั่วไป</h3>
          <div className="form-grid">
            <label className="full">Title<input value={formTitle} onChange={e => setFormTitle(e.target.value)} placeholder="ระบุชื่อ Defect" /></label>
            <div className="form-row">
              <label>Module<select value={formModuleId} onChange={e => setFormModuleId(e.target.value)}><option value="">เลือก Module</option>{renderModuleSelectOptions(modules)}</select></label>
              <label>Severity<select value={formSeverity} onChange={e => setFormSeverity(e.target.value)}>{defectSeverities.map(s => <option key={s}>{s}</option>)}</select></label>
            </div>
            <div className="form-row">
              <label>Status<select value={formStatus} onChange={e => setFormStatus(e.target.value)}>{defectStatuses.map(s => <option key={s}>{s}</option>)}</select></label>
              <label>Assignee<select value={formAssigneeUserId} onChange={e => setFormAssigneeUserId(e.target.value)}><option value="">ไม่ระบุ</option>{users.map(u => <option key={u.userId} value={u.userId}>{u.displayName}</option>)}</select></label>
            </div>
          </div>
        </section>
        {!editing && similar.length > 0 && <section className="defect-similar" aria-label="Defect ที่อาจซ้ำ">
          <h3><span className="material-symbols-outlined" aria-hidden="true">content_copy</span> อาจซ้ำกับ Defect ที่มีอยู่ ({similar.length})</h3>
          <p>ตรวจก่อนสร้าง — ถ้าเป็นปัญหาเดียวกัน ให้คอมเมนต์ใน Defect เดิมแทนการสร้างใหม่</p>
          <ul>{similar.map(s => <li key={s.defectId}>
            <a href={`#/defects?defectId=${s.defectId}`} target="_blank" rel="noreferrer" title={`${s.defectCode} ${s.title} — เปิดในแท็บใหม่`}><b>{s.defectCode}</b><span>{s.title}</span></a>
            <Badge tone={defectStatusTones[s.status] ?? "gray"}>{s.status}</Badge>
            {s.resolutionType && <small>{defectResolutionLabel(s.resolutionType)}</small>}
          </li>)}</ul>
        </section>}
        <div className="defect-detail-split">
          <section className="cycle-detail-section">
            <h3><span className="material-symbols-outlined" aria-hidden="true">description</span> Description</h3>
            <textarea className="defect-form-field" rows={4} value={formDescription} onChange={e => setFormDescription(e.target.value)} placeholder="รายละเอียด Defect" aria-label="Description" />
          </section>
          <section className="cycle-detail-section">
            <h3><span className="material-symbols-outlined" aria-hidden="true">description</span> Steps to Reproduce</h3>
            <textarea className="defect-form-field" rows={4} value={formStepsToReproduce} onChange={e => setFormStepsToReproduce(e.target.value)} placeholder="ขั้นตอนการทำซ้ำ" aria-label="Steps to Reproduce" />
          </section>
        </div>
        <div className="defect-detail-split">
          <section className="cycle-detail-section">
            <h3>Expected Result</h3>
            <input className="defect-form-field" value={formExpectedResult} onChange={e => setFormExpectedResult(e.target.value)} aria-label="Expected Result" />
          </section>
          <section className="cycle-detail-section">
            <h3>Actual Result</h3>
            <input className="defect-form-field" value={formActualResult} onChange={e => setFormActualResult(e.target.value)} aria-label="Actual Result" />
          </section>
        </div>
        {error && <div className="inline-alert error" role="alert"><span>{error}</span></div>}
        <div className="modal-actions">
          <button className="btn" onClick={() => setFormOpen(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิก</button>
          <button className="btn primary" disabled={saving || !formTitle.trim()} onClick={() => void saveForm()}>{saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">check</span> บันทึก</>}</button>
        </div>
      </ModalShell>}
    {resolving && projectId && <DefectResolutionModal
      defect={{ defectId: resolving.item?.defectId, defectCode: resolving.item?.defectCode, title: resolving.item?.title ?? formTitle, projectId: resolving.item?.projectId ?? projectId }}
      targetStatus={resolving.status} currentStatus={resolving.item?.status ?? "Open"} releaseId={resolving.item?.releaseId ?? releaseId ?? null} fixedInBuildId={resolving.item?.fixedInBuildId ?? null}
      rootCauses={rootCauses} initial={resolving.item}
      onCancel={() => setResolving(null)} onConfirm={confirmResolution} />}
    {detail && (() => {
      const steps = detail.stepsToReproduce ? parseReproSteps(detail.stepsToReproduce) : null;
      const moduleName = modules.find(m => m.moduleId === detail.moduleId)?.moduleName ?? "-";
      return (
        <ModalShell labelledBy="defect-detail-title" className="defect-detail" onDismiss={() => setDetail(null)}>
            <div className="modal-head">
              <div className="modal-head-title-group">
                <button className="modal-back-btn" aria-label="ปิดรายละเอียด Defect" onClick={() => setDetail(null)}>←</button>
                <div>
                  <span className="cycle-detail-eyebrow">DEFECT</span>
                  <h2 id="defect-detail-title">
                    {detail.defectCode}
                    <button type="button" className="defect-copy-btn" title="คัดลอกรหัส Defect" aria-label="คัดลอกรหัส Defect" onClick={async () => { const ok = await copyText(detail.defectCode); setCodeCopied(ok); setTimeout(() => setCodeCopied(false), 1500); }}>
                      <span aria-hidden="true">{codeCopied ? "✓" : "⧉"}</span>
                    </button>
                  </h2>
                  <small>{detail.title}</small>
                </div>
              </div>
              <div className="defect-detail-head-badges">
                <Badge tone={defectSeverityTones[detail.severity] ?? "blue"}>{detail.severity}</Badge>
                <Badge tone={defectStatusTones[detail.status] ?? "gray"}>{detail.status}</Badge>
              </div>
              <button aria-label="ปิดรายละเอียด Defect" onClick={() => setDetail(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
            </div>
            {/* ปุ่มดำเนินการอยู่ใต้หัวตลอด (เดิมอยู่กลางหน้า ต้องเลื่อนหา) — คำอธิบายย้ายจากไอคอน ⓘ ข้างปุ่มไปเป็น tooltip ของปุ่มเอง */}
            {canEdit !== false && (
              <div className="defect-detail-toolbar" role="toolbar" aria-label="การดำเนินการกับ Defect">
                {(detail.status === "Open" || detail.status === "Rejected") && (
                  <button className="btn quick-action-blue" title="เปลี่ยนสถานะ Defect นี้เป็น In Progress" onClick={() => { quickStatus(detail, "In Progress"); setDetail(null); }}><span className="material-symbols-outlined" aria-hidden="true">arrow_forward</span> In Progress</button>
                )}
                {(detail.status === "Resolved" || detail.status === "Closed") && (
                  <button className="btn quick-action-red" title="ปัญหายังอยู่หลังแก้ — เปิด Defect นี้กลับเป็น Open (นับจำนวนครั้ง Reopen)" onClick={() => { quickStatus(detail, "Open"); setDetail(null); }}><span className="material-symbols-outlined" aria-hidden="true">replay</span> Reopen</button>
                )}
                {(detail.status === "Open" || detail.status === "In Progress") && (
                  <button className="btn quick-action-green" title="เปลี่ยนสถานะ Defect นี้เป็น Resolved" onClick={() => { quickStatus(detail, "Resolved"); setDetail(null); }}><span className="material-symbols-outlined" aria-hidden="true">check</span> Resolve</button>
                )}
                {detail.status !== "Closed" && (
                  <button className="btn quick-action-purple" title={detail.status === "Resolved" ? "QA ตรวจซ้ำบน Build แล้วผ่าน — ปิด Defect" : "ปิด Defect ที่ไม่ต้องแก้ (ซ้ำ, ทำซ้ำไม่ได้, ตามที่ออกแบบ, ไม่แก้, เลื่อน)"} onClick={() => { quickStatus(detail, "Closed"); setDetail(null); }}><span aria-hidden="true">⏹</span> {detail.status === "Resolved" ? "ตรวจซ้ำผ่าน" : "Closed"}</button>
                )}
                <button className="btn" title="เปิดฟอร์มแก้ไขรายละเอียด Defect นี้ (หัวข้อ, Severity, คำอธิบาย, ผู้รับผิดชอบ ฯลฯ)" onClick={() => { openForm(detail); setDetail(null); }}><span className="material-symbols-outlined" aria-hidden="true">edit</span> แก้ไข</button>
                {detail.crmSyncStatus !== "Linked" && (
                  <button className="btn quick-action-blue" title="สร้าง Ticket ใหม่ใน CRM (BlueSea Helpdesk) จากข้อมูล Defect นี้ ให้เลือกผู้รับผิดชอบฝั่ง Dev แล้วผูก Ticket ID ไว้กับ Defect นี้" onClick={() => openCrmDialog("send", detail)}><span aria-hidden="true">⇪</span> ส่งไป CRM</button>
                )}
                {detail.crmSyncStatus === "Linked" && (
                  <button className="btn quick-action-blue" title={`เปลี่ยนผู้รับผิดชอบ (Assignto) บน CRM Ticket #${detail.crmTicketId} เดิมที่ผูกไว้แล้ว ไม่สร้าง Ticket ใหม่`} onClick={() => openCrmDialog("reassign", detail)}><span aria-hidden="true">↺</span> เปลี่ยนผู้รับผิดชอบ CRM</button>
                )}
                <button className="btn quick-action-red defect-detail-delete" title="ลบ Defect นี้ออกจากระบบ (soft delete — ยังกู้คืนได้จากฐานข้อมูล ไม่แสดงในรายการอีก)" onClick={() => { const item = detail; setDetail(null); removeDefect(item); }}><span aria-hidden="true">🗑</span> ลบ</button>
              </div>
            )}
            {detailError && <div className="inline-alert error defect-detail-alert" role="alert"><span>{detailError}</span></div>}
            <div className="defect-detail-body">
              <div className="defect-detail-main">
                <dl className="defect-detail-meta">
                  <div><dt>Module</dt><dd>{moduleName}</dd></div>
                  <div><dt>Assignee</dt><dd>{detail.assigneeName ?? "ไม่ระบุ"}</dd></div>
                  <div><dt>Age</dt><dd>{defectAgeDays(detail.createdAt)} วัน</dd></div>
                  <div><dt>เจอใน Build</dt><dd>{detail.buildNumber ?? "ไม่ระบุ"}</dd></div>
                  {(detail.fixedInBuildId || detail.status === "Resolved" || detail.status === "Closed") && <div><dt>Fix Build</dt><dd>{detail.fixedInBuildNumber ?? "ไม่ระบุ Build"}</dd></div>}
                  {detail.verifiedInBuildNumber && <div><dt>ตรวจซ้ำผ่านใน</dt><dd>{detail.verifiedInBuildNumber}</dd></div>}
                  {(detail.reopenCount ?? 0) > 0 && <div><dt>Reopen</dt><dd><Badge tone="red">{detail.reopenCount} ครั้ง</Badge></dd></div>}
                  {detail.regressionOfDefectCode && <div><dt>ปัญหาเดิมของ</dt><dd>{detail.regressionOfDefectCode}</dd></div>}
                  <div><dt>Created</dt><dd>{fmtAgo(detail.createdAt)} <small>{formatThaiDateTime(detail.createdAt, { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" })}</small></dd></div>
                  {detail.crmSyncStatus === "Linked" && detail.crmTicketId && <div><dt>CRM Ticket</dt><dd><a href={`https://bluesea.seniorsoft.com/bluesea/BookLicence/MA/Support/JobDetailsHD?JobNo=${encodeURIComponent(detail.crmTicketId)}&JobType=HD`} target="_blank" rel="noreferrer" title={`เปิด Ticket #${detail.crmTicketId} ใน CRM`}>#{detail.crmTicketId}</a></dd></div>}
                  {detail.crmSyncStatus === "Failed" && <div><dt>CRM</dt><dd><Badge tone="red">ส่งไม่สำเร็จ</Badge></dd></div>}
                </dl>
                <div className="defect-detail-split">
                  <section className="cycle-detail-section">
                    <h3><span className="material-symbols-outlined" aria-hidden="true">description</span> Description</h3>
                    <div className="defect-detail-text defect-description-text">
                      {(detail.description || "ไม่มีคำอธิบาย").split(/\r?\n/).map((line, index) => {
                        const labeledLine = line.match(/^(.{2,40}):\s*(.*)$/);
                        return <span key={`${index}-${line}`}>{labeledLine ? <><b>{labeledLine[1]}:</b>{labeledLine[2] && <span className="defect-description-value"> {labeledLine[2]}</span>}</> : (line || " ")}</span>;
                      })}
                    </div>
                  </section>
                  <section className="cycle-detail-section">
                    <h3><span className="material-symbols-outlined" aria-hidden="true">format_list_numbered</span> Steps to Reproduce</h3>
                    {detail.stepsToReproduce ? (steps ? <ReproSteps steps={steps} /> :<p className="defect-detail-text">{detail.stepsToReproduce}</p>) : <p className="defect-detail-text muted-text">ไม่มีขั้นตอนการทำซ้ำ</p>}
                  </section>
                </div>
                {(detail.expectedResult || detail.actualResult) && (
                  <div className="defect-detail-split">
                    {detail.expectedResult && <section className="cycle-detail-section"><h3>Expected Result</h3><p className="defect-detail-text">{detail.expectedResult}</p></section>}
                    {detail.actualResult && <section className="cycle-detail-section"><h3>Actual Result</h3><p className="defect-detail-text">{detail.actualResult}</p></section>}
                  </div>
                )}
                {(detail.resolutionType || defectClosingStatuses.includes(detail.status)) && <section className="cycle-detail-section defect-resolution-section">
                  <h3><span className="material-symbols-outlined" aria-hidden="true">task_alt</span> ผลการปิด</h3>
                  {detail.resolutionType ? <dl className="defect-resolution-view">
                    <div><dt>ผล</dt><dd>{defectResolutionLabel(detail.resolutionType)}{detail.duplicateOfDefectCode ? ` · ซ้ำกับ ${detail.duplicateOfDefectCode}` : ""}</dd></div>
                    {detail.rootCauseCategory && <div><dt>หมวดสาเหตุ</dt><dd>{rootCauseName(detail.rootCauseCategory)}</dd></div>}
                    {detail.rootCauseNote && <div className="full"><dt>สาเหตุ</dt><dd>{detail.rootCauseNote}</dd></div>}
                    {detail.resolutionNote && <div className="full"><dt>วิธีแก้</dt><dd>{detail.resolutionNote}</dd></div>}
                  </dl> : <p className="muted-text">ยังไม่ระบุสาเหตุและวิธีแก้ (ปิดจากช่องทางอื่น เช่น เปลี่ยนสถานะหลายรายการ) — บันทึกไว้เพื่อให้คลังความรู้และการเตือน Defect ซ้ำแม่นขึ้น</p>}
                  {canEdit !== false && <button type="button" className="btn defect-resolution-edit" onClick={() => { const item = detail; setDetail(null); setResolving({ item, status: null, mode: "edit" }); }}><span className="material-symbols-outlined" aria-hidden="true">edit_note</span>{detail.resolutionType ? "แก้ไขผลการปิด" : "บันทึกผลการปิด"}</button>}
                </section>}
                <section className="cycle-detail-section">
                  <h3>Linked Test Cases ({linkedCases.length})</h3>
                  <div className="defect-linked-cases">
                    {linkedCases.length ? linkedCases.map(tc => (
                      <div key={tc.testCaseId} className="defect-linked-case">
                        <div><b>{tc.testCaseCode}</b><small>{tc.title}</small></div>
                        <Badge tone={tc.status ? (testCaseStatusTones[tc.status] ?? "gray") : "gray"}>{tc.status ?? "-"}</Badge>
                        {onOpenTestCase && <button className="btn" onClick={() => { const id = tc.testCaseId; setDetail(null); onOpenTestCase(id); }}><span aria-hidden="true">⤢</span> ดูรายละเอียด</button>}
                      </div>
                    )) : <p className="muted-text">{detailError ? "โหลด Test Case ที่เชื่อมโยงไม่สำเร็จ" : "ยังไม่มี Test Case ที่เชื่อมโยง"}</p>}
                  </div>
                </section>
              </div>
              {(() => { const history = activities.filter(a => a.actionType !== "Comment" && a.actionType !== "CrmComment"); return (
              <aside className="defect-detail-side" aria-label="คอมเมนต์และประวัติ">
                <div className="defect-detail-tabs" role="tablist" aria-label="คอมเมนต์และประวัติ">
                  <button type="button" role="tab" id="defect-tab-comments" aria-controls="defect-panel-comments" aria-selected={detailTab === "comments"} tabIndex={detailTab === "comments" ? 0 : -1}
                    onClick={() => setDetailTab("comments")} onKeyDown={(e) => { if (e.key === "ArrowRight" || e.key === "ArrowLeft") { setDetailTab("activities"); document.getElementById("defect-tab-activities")?.focus(); } }}>
                    <span className="material-symbols-outlined" aria-hidden="true">forum</span> Comments <span className="defect-detail-tab-count">{comments.length}</span>
                  </button>
                  <button type="button" role="tab" id="defect-tab-activities" aria-controls="defect-panel-activities" aria-selected={detailTab === "activities"} tabIndex={detailTab === "activities" ? 0 : -1}
                    onClick={() => setDetailTab("activities")} onKeyDown={(e) => { if (e.key === "ArrowRight" || e.key === "ArrowLeft") { setDetailTab("comments"); document.getElementById("defect-tab-comments")?.focus(); } }}>
                    <span className="material-symbols-outlined" aria-hidden="true">history</span> Activities <span className="defect-detail-tab-count">{history.length}</span>
                  </button>
                </div>
                {detailTab === "comments" ? (
                  <>
                    <div className="defect-detail-side-scroll" role="tabpanel" id="defect-panel-comments" aria-labelledby="defect-tab-comments" ref={commentScrollRef}>
                      <DefectCommentList comments={comments} imageSrc={(id) => commentImageUrls[id]} emptyText={detailError.includes("คอมเมนต์") ? "โหลดคอมเมนต์ไม่สำเร็จ" : "ยังไม่มีคอมเมนต์"} />
                    </div>
                    {canEdit !== false && <div className="defect-detail-composer"><DefectCommentComposer onSubmit={submitComment} /></div>}
                  </>
                ) : (
                  <div className="defect-detail-side-scroll" role="tabpanel" id="defect-panel-activities" aria-labelledby="defect-tab-activities">
                    <div className="defect-activity-list">
                      {history.length ? history.map(a => (
                        <div key={a.activityId} className="defect-activity-row">
                          <Badge tone="blue">{defectActionLabels[a.actionType] ?? a.actionType}</Badge>
                          <div>
                            <p>{a.message ?? a.actionType}</p><small>{a.actorName ?? "System"} · {fmtAgo(a.performedAt ?? a.createdAt)}</small>
                            {a.attachments && a.attachments.length > 0 && (
                              <div className="defect-comment-thumbs defect-activity-thumbs">
                                {a.attachments.map((x, i) => {
                                  const src = activityImageUrls[x.attachmentId];
                                  return (
                                    <button type="button" key={x.attachmentId} aria-label={`ดูรูป ${x.fileName} ขนาดใหญ่`} title={x.fileName}
                                      onClick={() => setActivityViewer({ attachments: a.attachments!, index: i })}>
                                      {src ? <img src={src} alt={x.fileName} loading="lazy" /> : <span className="material-symbols-outlined" aria-hidden="true">image</span>}
                                    </button>
                                  );
                                })}
                              </div>
                            )}
                          </div>
                        </div>
                      )) : <p className="muted-text">ยังไม่มี Activity</p>}
                    </div>
                  </div>
                )}
              </aside>
              ); })()}
            </div>
            {activityViewer && <ImageLightbox images={toLightboxImages(activityViewer.attachments, (id) => activityImageUrls[id])} index={activityViewer.index} onIndexChange={(index) => setActivityViewer({ ...activityViewer, index })} onClose={() => setActivityViewer(null)} />}
          </ModalShell>
      );
    })()}
    {/* Dialog เลือกผู้รับผิดชอบฝั่ง Dev แล้วส่ง/เปลี่ยนผู้รับผิดชอบใน CRM — อยู่นอก {detail && ...} ตั้งใจ
        เพราะตอนนี้เปิดได้ทั้งจาก detail modal (ปุ่ม Quick Action) และจากคอลัมน์ CRM ในตาราง list โดยตรง
        (ไม่เปิด detail modal เลย) ใช้ crmTargetItem แทน detail เป็นตัวอ้างอิงว่ากำลังทำงานกับ Defect ตัวไหน */}
    {crmDialogOpen && (
      <ModalShell labelledBy="crm-send-title" onDismiss={() => { if (!crmSending) setCrmDialogOpen(false); }}>
          <div className="modal-head">
            <h2 id="crm-send-title">{crmMode === "reassign" ? "เปลี่ยนผู้รับผิดชอบ CRM" : "ส่งไป CRM"}</h2>
            <button aria-label="ปิด" disabled={crmSending} onClick={() => setCrmDialogOpen(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
          </div>
          <p><b>{crmTargetItem?.defectCode}</b> — {crmMode === "reassign" ? <>เลือกผู้รับผิดชอบฝั่ง Dev คนใหม่สำหรับ Ticket #{crmTargetItem?.crmTicketId} เดิม (ไม่สร้าง Ticket ใหม่)</> : "เลือกผู้รับผิดชอบฝั่ง Dev สำหรับ Ticket ใน CRM"}</p>
          {crmDevUsersLoading ? <span className="spinner inline" aria-hidden="true" /> : crmDevUsersError ? (
            <div className="inline-alert error"><span>{crmDevUsersError}</span></div>
          ) : (
            <label>ผู้รับผิดชอบ (Dev)
              <select value={crmAssignTo} onChange={e => setCrmAssignTo(e.target.value)}>
                <option value="">-- เลือก --</option>
                {[...crmDevUsers].sort((a, b) => a.staffCode.localeCompare(b.staffCode, undefined, { numeric: true })).map(u => <option key={u.staffCode} value={u.staffCode}>{u.staffCode} {u.name}</option>)}
              </select>
            </label>
          )}
          <div className="modal-actions">
            <button className="btn" disabled={crmSending} onClick={() => setCrmDialogOpen(false)}>ยกเลิก</button>
            <button className="btn primary" disabled={!crmAssignTo || crmSending} onClick={sendToCrm}>
              {crmSending ? <><span className="spinner inline" aria-hidden="true" /> กำลังส่ง...</> : crmMode === "reassign" ? "ยืนยันเปลี่ยนผู้รับผิดชอบ" : "ยืนยันส่งไป CRM"}
            </button>
          </div>
        </ModalShell>
    )}
  </div>;
}
