import { toUtcDate, formatThaiDateTime } from "../dateTime";
import { useState, useRef, useMemo, useCallback, useEffect } from "react";
import type { DefectItem, UserLookup } from "../shared/types";
import { apiFetch, apiUrl, authHeaders, getJson, isAbortError } from "../api";
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
import { defectAgeDays, defectClosingStatuses, defectStatusKey, isDefectDone, defectResolutionLabel, parseReproSteps, splitResolutionChoice, type DefectResolutionChoice } from "../shared/defects";
import { DefectResolutionModal } from "../components/DefectResolutionModal";
import { useDebounced } from "../components/useDebounced";
import { ReproSteps } from "../components/ReproSteps";
import { DefectDescription } from "../components/DefectDescription";
import { ModalShell } from "../components/ModalShell";
import { ActionMenu, type ActionMenuItem } from "../components/ActionMenu";
import { DefectBoard, DefectTypeIcon, SeverityMark, StatusLozenge } from "./defectJira";
import { AssigneeCell, Avatar } from "../components/JiraParts";
import { DeletedItemsButton } from "../components/DeletedItemsModal";
import { type ModuleItem, copyText, renderModuleSelectOptions, useMasterOptions } from "../shared/appShared";

type SimilarDefect = { defectId: string; defectCode: string; title: string; status: string; severity: string; resolutionType?: string | null; snippet: string };
// BadRequest ของ API บางจุดส่งข้อความล้วน (ไม่ใช่ ProblemDetails) — อ่านได้ทั้งสองแบบ
const readError = async (response: Response, fallback: string) => { const text = await response.text().catch(() => ""); try { const p = JSON.parse(text); return typeof p === "string" ? p : p?.detail ?? p?.title ?? fallback; } catch { return text.trim() || fallback; } };

type DefectActivityItem = { activityId: string; actionType: string; message: string; actorUserId?: string | null; actorName?: string | null; createdAt: string; performedByUserId?: string | null; performedAt?: string; attachments?: DefectCommentAttachment[] };
type DefectTestCaseItem = { testCaseId: string; testCaseCode: string; title: string; priority?: string; status?: string; linkedAt?: string };
const defectSeverities = ["Critical", "High", "Medium", "Low"];
// คำอธิบายระดับเดียวกับ ExecutionDefectEditor — ใช้กับ Severity แบบ segmented ในฟอร์มสร้าง/แก้ไข
const defectSeverityOptions = [
  { value: "Critical", tone: "red", hint: "ระบบใช้งานไม่ได้ ข้อมูลเสียหาย หรือไม่มีทางเลี่ยง" },
  { value: "High", tone: "yellow", hint: "ฟังก์ชันหลักทำงานผิด กระทบงานของผู้ใช้มาก" },
  { value: "Medium", tone: "blue", hint: "ทำงานผิดแต่มีทางเลี่ยง หรือกระทบบางกรณี" },
  { value: "Low", tone: "green", hint: "เล็กน้อย เช่น ข้อความ การจัดวาง หรือความสวยงาม" },
];
const defectStatuses = ["Open", "In Progress", "Resolved", "Closed", "Rejected"];
// ฟอร์มสร้าง Defect สร้าง Test Case (Draft) ให้ด้วย — ขั้นตอนการทำซ้ำจึงกรอกเป็น Step แบบ Test Case และ Priority ตาม Severity (ต้องตรงกับ DefectsController)
type DefectFormStep = { action: string; testData: string; expectedResult: string };
const emptyDefectStep = (): DefectFormStep => ({ action: "", testData: "", expectedResult: "" });
const defectStepStarted = (step: DefectFormStep) => !!(step.action.trim() || step.testData.trim() || step.expectedResult.trim());
const severityTestCasePriority: Record<string, string> = { Critical: "P0", High: "P1", Medium: "P2", Low: "P3" };
const stepsToReproduceMax = 4000;
// ข้อความขั้นตอนการทำซ้ำของ Defect ใช้รูปแบบเดียวกับ Defect จาก Execution ("1. การกระทำ | ข้อมูล: … | คาดหวัง: …") — parseReproSteps แสดงเป็นการ์ดได้
// Release แสดงเป็น "รหัส · เวอร์ชัน" เช่น PMX2-REL-008 · 1.5.0
const releaseText = (x: { releaseCode?: string | null; releaseVersion?: string | null }) => [x.releaseCode, x.releaseVersion].filter(Boolean).join(" · ") || null;
const flattenStepText = (text: string) => text.trim().replace(/\s*\r?\n\s*/g, " ").replace(/\|/g, "/");
const composeReproSteps = (steps: DefectFormStep[]) => steps.map((step, index) => [
  `${index + 1}. ${flattenStepText(step.action)}`,
  ...(step.testData.trim() ? [`ข้อมูล: ${flattenStepText(step.testData)}`] : []),
  `คาดหวัง: ${flattenStepText(step.expectedResult)}`,
].join(" | ")).join("\n");
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
// มุมมองรายการ / Board และการแสดงสรุป (Insights) — จำต่อเครื่อง; Board โหลดสูงสุด 500 รายการตามตัวกรอง (ไม่แบ่งหน้า)
type DefectView = "list" | "board";
const boardLimit = 500;
const readStored = (key: string) => { try { return localStorage.getItem(key); } catch { return null; } };
const writeStored = (key: string, value: string) => { try { localStorage.setItem(key, value); } catch { /* ไม่จำก็ได้ */ } };
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
  const [view, setViewState] = useState<DefectView>(() => readStored("qa.defects.view") === "board" ? "board" : "list");
  const setView = (next: DefectView) => { setViewState(next); setSelectedIds([]); writeStored("qa.defects.view", next); };
  // ยังไม่เคยเลือก: Desktop แสดงสรุป, มือถือซ่อน (สรุปสูงเกือบเต็มจอ ทำให้รายการอยู่ไกล)
  const [showInsights, setShowInsightsState] = useState(() => { const stored = readStored("qa.defects.insights"); return stored ? stored !== "hidden" : !window.matchMedia("(max-width: 760px)").matches; });
  const setShowInsights = (next: boolean) => { setShowInsightsState(next); writeStored("qa.defects.insights", next ? "shown" : "hidden"); };
  const [createAnother, setCreateAnother] = useState(false);
  const [severityFilter, setSeverityFilter] = useState("");
  const [priorityFilter, setPriorityFilter] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  // เลือก Build ที่ topbar: foundIn = Defect ที่เจอใน Build นี้, openAsOf = ยังไม่ปิด ณ Build นี้ (รวมที่ค้างจาก Build ก่อนของ Release เดียวกัน)
  const [buildMode, setBuildMode] = useState<"foundIn" | "openAsOf">("foundIn");
  const [ownerFilter, setOwnerFilter] = useState(""); // เจ้าของเรื่อง = ผู้สร้าง Defect
  const [modules, setModules] = useState<ModuleItem[]>([]);
  const [users, setUsers] = useState<UserLookup[]>([]);
  const userName = (userId?: string | null) => (userId && users.find(u => u.userId === userId)?.displayName) || null;
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
  const [formSteps, setFormSteps] = useState<DefectFormStep[]>([emptyDefectStep()]);
  const [formPreconditions, setFormPreconditions] = useState("");
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
  // Dev ที่รับงานใน CRM (CrmLastKnownAssignto — ตั้งตอนส่ง/เปลี่ยนผู้รับผิดชอบ และ poller อัปเดตตาม CRM) — Dev ส่วนใหญ่ไม่มีบัญชี QA Hub
  // จึงแสดงคู่กับช่องผู้รับผิดชอบ ไม่บันทึกลงช่องนั้น; ชื่อจาก BlueID directory (/crm/staff) ผู้ใช้ที่ไม่มีบัญชี CRM เห็นเป็นรหัส
  const [crmStaffNames, setCrmStaffNames] = useState<Record<string, string>>({});
  const needCrmNames = items.some(x => x.crmSyncStatus === "Linked" && x.crmAssigneeCode) || !!(detail?.crmSyncStatus === "Linked" && detail.crmAssigneeCode);
  const crmStaffLoaded = useRef(false);
  useEffect(() => {
    if (!needCrmNames || crmStaffLoaded.current) return;
    crmStaffLoaded.current = true;
    apiFetch<{ staffCode: string; name: string }[]>("/crm/staff")
      .then(rows => setCrmStaffNames(Object.fromEntries((Array.isArray(rows) ? rows : []).map(r => [r.staffCode, r.name]))))
      .catch(() => undefined); // โหลดชื่อไม่ได้ (ไม่มีบัญชี CRM) = แสดงรหัสแทน
  }, [needCrmNames]);
  const crmDevName = (x?: Pick<DefectItem, "crmSyncStatus" | "crmAssigneeCode"> | null) => {
    const code = x?.crmSyncStatus === "Linked" ? x.crmAssigneeCode?.trim() : "";
    if (!code) return null;
    const name = crmStaffNames[code] ?? crmDevUsers.find(u => u.staffCode === code)?.name;
    return name ? `${name} (${code})` : code;
  };
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
    const q = new URLSearchParams({ projectId, ...(releaseId && { releaseId }), ...(buildId && { buildId }), ...(buildId && buildMode === "openAsOf" && { buildMode }), ...(search && { search }), ...(moduleFilter && moduleFilter !== "unassigned" && { moduleId: moduleFilter }), ...(moduleFilter === "unassigned" && { unassignedModule: "true" }), ...(severityFilter && { severity: severityFilter }), ...(priorityFilter && { priority: priorityFilter }), ...(statusFilter && view === "list" && { status: statusFilter }), ...(ownerFilter && { createdByUserId: ownerFilter }), page: String(view === "board" ? 1 : page), size: String(view === "board" ? boardLimit : pageSize) });
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
  }, [projectId, releaseId, buildId, buildMode, search, moduleFilter, severityFilter, priorityFilter, statusFilter, ownerFilter, listSortBy, page, pageSize, view, headers, reload]);
  useEffect(load, [load]);
  // การ์ดสรุปด้านบน — ขอบเขตตาม Project/Release/Build ที่เลือก (ไม่ผูกกับตัวกรองของตารางด้านล่าง) เพื่อให้เห็น
  // ภาพรวมทั้งหมดของบริบทนั้นเสมอ ไม่ว่าจะกรอง/ค้นหาตารางด้วยอะไรอยู่
  useEffect(() => {
    if (!projectId) { setSummaryStats(emptyDefectStats); setStatsLoaded(false); setStatsError(""); return; }
    setStatsLoaded(false);
    setStatsError("");
    const controller = new AbortController();
    const q = new URLSearchParams({ projectId, ...(releaseId && { releaseId }), ...(buildId && { buildId }), ...(buildId && buildMode === "openAsOf" && { buildMode }) });
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
  }, [projectId, releaseId, buildId, buildMode, headers, reload, statsReload]);
  useEffect(() => {
    if (!projectId) return;
    Promise.all([
      getJson<ModuleItem[]>(`${apiUrl}/projects/${projectId}/modules`),
      getJson<UserLookup[]>(`${apiUrl}/lookups/users`),
    ]).then(([m, u]) => { setModules(m.filter(x => x.isActive)); setUsers(u); })
      .catch(() => setError("โหลดรายการ Module / ผู้ใช้สำหรับตัวกรองและฟอร์มไม่สำเร็จ — ตัวเลือกอาจว่าง"));
  }, [projectId]);
  useEffect(() => { setPage(1); }, [search, moduleFilter, severityFilter, priorityFilter, statusFilter, ownerFilter]);
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
    setOwnerFilter("");
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
    setFormSteps([emptyDefectStep()]);
    setFormPreconditions("");
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
      // สร้างใหม่: ขั้นตอนมาจากตาราง Step และส่ง testCase ให้ API สร้าง Test Case พร้อมกัน; แก้ไข: ข้อความเดิม (Test Case สร้างไปแล้วตอนสร้าง)
      const filledSteps = formSteps.filter(defectStepStarted);
      const testCase = editing ? null : { preconditions: formPreconditions.trim() || null, steps: filledSteps.map(x => ({ action: x.action.trim(), testData: x.testData.trim() || null, expectedResult: x.expectedResult.trim() })) };
      const stepsToReproduce = editing ? formStepsToReproduce : composeReproSteps(filledSteps);
      const body = { projectId: editing?.projectId ?? projectId ?? null, moduleId: formModuleId || null, title: formTitle, severity: formSeverity, status: formStatus, description: formDescription || null, stepsToReproduce: stepsToReproduce || null, testCase, expectedResult: formExpectedResult || null, actualResult: formActualResult || null, assigneeUserId: formAssigneeUserId || null, releaseId: releaseId || null, buildId: buildId || null, resolution: picked?.resolution ?? null, fixedInBuildId: picked?.fixedInBuildId ?? null, verifiedInBuildId: picked?.verifiedInBuildId ?? null };
      const response = await fetch(editing ? `${apiUrl}/defects/${editing.defectId}` : `${apiUrl}/defects`, { method: editing ? "PUT" : "POST", headers, body: JSON.stringify(body) });
      if (!response.ok) throw new Error(await readError(response, "บันทึก Defect ไม่สำเร็จ"));
      // "สร้างต่ออีกรายการ" (แบบ Jira): คง Module / Severity / ผู้รับผิดชอบ ล้างเฉพาะเนื้อหา แล้วเปิดฟอร์มค้างไว้
      if (!editing && createAnother) {
        const created = await response.json().catch(() => null) as DefectItem | null;
        setFormTitle(""); setFormDescription(""); setFormStepsToReproduce(""); setFormSteps([emptyDefectStep()]); setFormPreconditions(""); setFormExpectedResult(""); setFormActualResult(""); setFormStatus("Open");
        notify(created?.defectCode ? `สร้าง ${created.defectCode} พร้อม Test Case แล้ว — กรอก Defect ถัดไปได้เลย` : "สร้าง Defect พร้อม Test Case แล้ว — กรอก Defect ถัดไปได้เลย", "success");
        setReload(x => x + 1);
        return;
      }
      if (editing && detail?.defectId === editing.defectId) {
        const saved = await response.json().catch(() => null) as DefectItem | null;
        void openDetail({ ...detail, ...saved, assigneeName: users.find(u => u.userId === formAssigneeUserId)?.displayName ?? null, ...(saved?.assigneeName !== undefined && { assigneeName: saved.assigneeName }) }, true);
      }
      setFormOpen(false);
      setNotice(editing ? "แก้ไข Defect แล้ว" : "สร้าง Defect และ Test Case (Draft) แล้ว");
      setReload(x => x + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "บันทึกไม่สำเร็จ"); } finally { setSaving(false); }
  };
  // แก้ช่องในแผงรายละเอียดทันที (แบบ Jira) — API แก้ไขรับข้อมูลทั้งชุด จึงส่งค่าปัจจุบันของ Defect ไปพร้อมช่องที่เปลี่ยน
  // (Module / Release / Build ตั้งได้ตอนสร้างเท่านั้น — API ไม่บันทึกตอนแก้ไข)
  const [detailSaving, setDetailSaving] = useState(false);
  const updateDetailField = async (patch: Partial<Pick<DefectItem, "severity" | "assigneeUserId">>) => {
    if (!detail || detailSaving) return;
    const next = { ...detail, ...patch };
    setDetailSaving(true);
    try {
      const body = { projectId: next.projectId ?? projectId, releaseId: next.releaseId ?? null, buildId: next.buildId ?? null, moduleId: next.moduleId ?? null, title: next.title, severity: next.severity, status: next.status, description: next.description ?? null, stepsToReproduce: next.stepsToReproduce ?? null, expectedResult: next.expectedResult ?? null, actualResult: next.actualResult ?? null, assigneeUserId: next.assigneeUserId ?? null };
      const response = await fetch(`${apiUrl}/defects/${detail.defectId}`, { method: "PUT", headers, body: JSON.stringify(body) });
      if (!response.ok) { notify(await readError(response, `บันทึก ${detail.defectCode} ไม่สำเร็จ`), "error"); return; }
      const updated = await response.json().catch(() => null) as DefectItem | null;
      setReload(x => x + 1);
      await openDetail({ ...next, assigneeName: users.find(u => u.userId === next.assigneeUserId)?.displayName ?? null, ...updated }, true);
    } finally { setDetailSaving(false); }
  };
  const removeDefect = async (item: DefectItem): Promise<boolean> => {
    if (!await confirmDialog({ title: `ลบ ${item.defectCode}`, message: `ลบ ${item.defectCode} ใช่หรือไม่? กู้คืนได้จากปุ่ม "ลบแล้ว"`, confirmLabel: "ลบ", tone: "danger" })) return false;
    const response = await fetch(`${apiUrl}/defects/${item.defectId}`, { method: "DELETE", headers });
    if (!response.ok) { notify(await readError(response, `ลบ ${item.defectCode} ไม่สำเร็จ`), "error"); return false; }
    setNotice(`ลบ ${item.defectCode} แล้ว`); setReload(x => x + 1);
    return true;
  };
  // คืน Defect ที่อัปเดตแล้ว (null = ยกเลิก/ไม่สำเร็จ หรือรอหน้าต่างผลการปิด) ให้หน้าต่างรายละเอียดแสดงค่าใหม่ต่อได้
  const quickStatus = async (item: DefectItem, status: string): Promise<DefectItem | null> => {
    // Resolved / Closed / Rejected ต้องระบุผลการปิด (ใช้หน้าต่างผลการปิดแทนกล่องยืนยันเดิม)
    if (defectClosingStatuses.includes(status)) { setResolving({ item, status, mode: "quick" }); return null; }
    // กลับเป็น Open/In Progress จาก Resolved/Closed = Reopen (นับจำนวนครั้ง ล้างผลการปิดเดิม)
    const reopen = item.status === "Resolved" || item.status === "Closed";
    if (reopen && !await confirmDialog({ title: `Reopen ${item.defectCode}`, message: `ปัญหายังอยู่หลังแก้ใช่หรือไม่? Defect จะกลับเป็น ${status}, นับเป็น Reopen ครั้งที่ ${(item.reopenCount ?? 0) + 1} และล้างผลการปิดเดิม (ประวัติยังอยู่ใน Activity)`, confirmLabel: "Reopen" })) return null;
    const response = await fetch(`${apiUrl}/defects/${item.defectId}/status`, { method: "PATCH", headers, body: JSON.stringify({ status }) });
    if (!response.ok) { notify(await readError(response, `เปลี่ยนสถานะ ${item.defectCode} ไม่สำเร็จ`), "error"); return null; }
    setNotice(reopen ? `Reopen ${item.defectCode} แล้ว` : `เปลี่ยนสถานะ ${item.defectCode} เป็น ${status}`); setReload(x => x + 1);
    const updated = await response.json().catch(() => null) as DefectItem | null;
    return updated ? { ...item, ...updated } : { ...item, status };
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
    // หน้าต่างรายละเอียดของ Defect นี้เปิดอยู่ (หน้าต่างผลการปิดซ้อนอยู่บน) — แสดงค่าใหม่ + ประวัติล่าสุดต่อ
    if (mode === "edit" || detail?.defectId === item.defectId) void openDetail({ ...item, ...updated }, true);
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
      setNotice(crmMode === "reassign" ? `เปลี่ยนผู้รับผิดชอบใน CRM Ticket #${item.crmTicketId} สำเร็จ` : `ส่งไป CRM สำเร็จ Ticket #${body.crmTicketId}${body.imageCount ? ` · แนบรูป ${body.imageCount} รูป` : ""}${body.skippedImages ? ` · รูปที่ CRM ไม่รับ ${body.skippedImages} รูป ดูผ่านลิงก์ใน Ticket` : ""}`);
      setCrmDialogOpen(false);
      setReload(x => x + 1);
      // เปิด/รีเฟรช detail modal ต่อให้เห็น badge/activity อัปเดตทันที เฉพาะกรณีเปิด dialog นี้มาจาก detail
      // modal ของ Defect ตัวเดียวกันอยู่แล้ว — ถ้าส่งจากคอลัมน์ CRM ในตาราง list โดยตรง (ไม่ได้เปิด detail
      // ไว้) ก็ไม่ต้องเด้ง detail มาให้ ปล่อยให้ตาราง reload แล้วเห็น badge เปลี่ยนในแถวเดิมพอ
      if (detail && detail.defectId === item.defectId) openDetail(item);
    } catch (e) { notify(e instanceof Error ? e.message : "ดำเนินการไม่สำเร็จ", "error"); }
    finally { setCrmSending(false); }
  };
  // keep = โหลดใหม่ของ Defect ที่เปิดอยู่ (หลังเปลี่ยนสถานะ/แก้ช่อง/คอมเมนต์) — ไม่ล้างหน้าจอและคงแท็บเดิม
  const openDetail = async (item: DefectItem, keep = false) => {
    setDetail(item); setDetailLoading(true); setDetailError("");
    if (!keep) { setActivities([]); setActivityViewer(null); setDetailTab("comments"); setLinkedCases([]); setComments([]); setCodeCopied(false); }
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
    await openDetail(detail, true);
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
  const activeDefectFilterCount = [search, moduleFilter, severityFilter, priorityFilter, statusFilter, ownerFilter].filter(Boolean).length;
  const clearDefectFilters = () => {
    onClearSearch?.();
    setModuleFilter("");
    setSeverityFilter("");
    setPriorityFilter("");
    setStatusFilter("");
    setOwnerFilter("");
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
  const canEditDefects = canEdit !== false;
  const moduleOf = (item: DefectItem) => modules.find(m => m.moduleId === item.moduleId);
  const rowMenuItems = (x: DefectItem): ActionMenuItem[] => [
    ...(x.status === "Open" ? [{ key: "start", icon: "play_arrow", label: "เริ่มดำเนินการ", hint: "เปลี่ยนเป็น In Progress", onSelect: () => void quickStatus(x, "In Progress") }] : []),
    ...(x.status === "Open" || x.status === "In Progress" ? [{ key: "resolve", icon: "task_alt", label: "Resolve", hint: "Dev แก้แล้ว รอ QA ตรวจซ้ำ", onSelect: () => void quickStatus(x, "Resolved") }] : []),
    { key: "edit", icon: "edit", label: "แก้ไข", onSelect: () => openForm(x) },
    ...((x.crmSyncStatus ?? "None") === "None" ? [{ key: "crm", icon: "upload", label: "ส่งไป CRM", hint: "สร้าง Ticket ใหม่ใน CRM", onSelect: () => void openCrmDialog("send", x) }] : []),
    { key: "delete", icon: "delete", label: "ลบ", danger: true, onSelect: () => void removeDefect(x) },
  ];
  return <div className="defect-page df-page dj-page">
    {error && <div className="inline-alert error"><span>{error}</span><button type="button" aria-label="ปิดข้อความ" onClick={() => setError("")}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>}
    {notice && <div className="inline-alert success"><span>{notice}</span><button type="button" aria-label="ปิดข้อความ" onClick={() => setNotice("")}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>}
    {/* ส่วนหัวแบบ Jira: จำนวน + ขอบเขต | ปุ่มสร้าง → แท็บมุมมอง รายการ / Board | สรุป · Export · ลบแล้ว */}
    {/* ส่วนหัวแบบ Jira แถวเดียว: แท็บมุมมอง + จำนวน/ขอบเขต | สรุป · Export · ลบแล้ว · สร้าง Defect (ชื่อหน้าอยู่ที่หัวหน้าของ App) */}
    <header className="dj-header">
      <div className="dj-header-start">
        <div className="dj-views" role="group" aria-label="มุมมอง">
          <button type="button" aria-pressed={view === "list"} onClick={() => setView("list")}><span className="material-symbols-outlined" aria-hidden="true">view_list</span>รายการ</button>
          <button type="button" aria-pressed={view === "board"} onClick={() => setView("board")}><span className="material-symbols-outlined" aria-hidden="true">view_kanban</span>Board</button>
        </div>
        <p className="dj-title"><b>{totalCount.toLocaleString()} รายการ</b>{activeDefectFilterCount ? <span> · กรอง {activeDefectFilterCount} เงื่อนไข</span> : null}<small title={defectContextLabel}>{defectContextLabel}</small></p>
      </div>
      <div className="dj-header-tools">
        <button className="btn" type="button" aria-pressed={showInsights} aria-controls="defect-insights" onClick={() => setShowInsights(!showInsights)} title={showInsights ? "ซ่อนสรุปภาพรวม" : "แสดงสรุปภาพรวม"}><span className="material-symbols-outlined" aria-hidden="true">insights</span>สรุป</button>
        <button className="btn" type="button" disabled={!items.length} onClick={exportCsv}><span className="material-symbols-outlined" aria-hidden="true">download</span>Export</button>
        <DeletedItemsButton type="defects" label="Defect" onRestored={() => setReload(x => x + 1)} />
        {canEditDefects && <button className="btn primary" type="button" disabled={!projectId} onClick={() => openForm()}><span className="material-symbols-outlined" aria-hidden="true">add</span>สร้าง Defect</button>}
      </div>
    </header>
    {/* ภาพรวมการ์ดเดียว (แทน intro + KPI 6 ใบ + กราฟความรุนแรง + ข้อมูลประกอบ + อันดับโมดูล) — ขอบเขต Project/Release/Build เท่านั้น ไม่ผูกตัวกรองตาราง */}
    {showInsights && <section className="card df-overview" id="defect-insights" aria-label="ภาพรวม Defect">
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
    </section>}
    <div className="dj-filters" role="group" aria-label="ตัวกรอง Defect">
      {onSearchChange && <label className="dj-search"><span className="material-symbols-outlined" aria-hidden="true">search</span><input type="search" value={search} onChange={(e) => onSearchChange(e.target.value)} placeholder="ค้นหารหัส / ชื่อ Defect" aria-label="ค้นหา Defect" /></label>}
      <select aria-label="Module" value={moduleFilter} onChange={e => setModuleFilter(e.target.value)}><option value="">ทุก Module</option>{renderModuleSelectOptions(modules)}{summaryStats.modules.some(x => x.moduleId === null) && <option value="unassigned">ไม่ระบุโมดูล</option>}</select>
      <select aria-label="Severity" value={severityFilter} onChange={e => setSeverityFilter(e.target.value)}><option value="">ทุก Severity</option>{defectSeverities.map(s => <option key={s}>{s}</option>)}</select>
      <select aria-label="Priority ของ Test Case ที่เชื่อมโยง" value={priorityFilter} onChange={e => setPriorityFilter(e.target.value)}><option value="">ทุก Priority</option>{["P0", "P1", "P2", "P3"].map(priority => <option key={priority}>{priority}</option>)}</select>
      <select aria-label="เจ้าของเรื่อง" value={ownerFilter} onChange={e => setOwnerFilter(e.target.value)}><option value="">ทุกเจ้าของเรื่อง</option>{users.map(u => <option key={u.userId} value={u.userId}>{u.displayName}</option>)}</select>
      {buildId && <select aria-label="Defect ของ Build ที่เลือก" title="เจอใน Build นี้ = แจ้งบน Build นี้ทุกสถานะ · ยังไม่ปิด = Open / In Progress / Resolved ที่เจอใน Build นี้หรือ Build ก่อนหน้าของ Release เดียวกัน" value={buildMode} onChange={(e) => { setBuildMode(e.target.value as "foundIn" | "openAsOf"); setPage(1); }}>
        <option value="foundIn">เจอใน Build นี้</option>
        <option value="openAsOf">ยังไม่ปิด ณ Build นี้ (รวมของค้าง)</option>
      </select>}
      {view === "list" && <select className="dj-sort-mobile" aria-label="เรียงตาม" value={`${listSort.key}:${listSort.dir}`} onChange={(e) => { const [key, dir] = e.target.value.split(":"); setListSort({ key: key as DefectSortKey, dir: dir as "asc" | "desc" }); }}>
        {defectSortOptions.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
      </select>}
      {activeDefectFilterCount > 0 && <button type="button" className="btn dj-clear" onClick={clearDefectFilters}><span className="material-symbols-outlined" aria-hidden="true">filter_alt_off</span>ล้างตัวกรอง</button>}
    </div>
    {view === "board" ? <section className="dj-board-wrap" id="defect-list-results" tabIndex={-1} aria-label="Board Defect ตามสถานะ" aria-busy={loading}>
      {totalCount > items.length && <p className="dj-board-note" role="status"><span className="material-symbols-outlined" aria-hidden="true">info</span>แสดง {items.length.toLocaleString()} จาก {totalCount.toLocaleString()} รายการ — ใช้ตัวกรองให้แคบลง หรือดูทั้งหมดในมุมมองรายการ</p>}
      {canEditDefects && items.length > 0 && <p className="dj-board-hint">ลากการ์ดไปคอลัมน์อื่นเพื่อเปลี่ยนสถานะ หรือใช้เมนู ⋯ บนการ์ด</p>}
      <DefectBoard items={items} crmDevName={crmDevName} statuses={defectStatuses} canEdit={canEditDefects} onOpen={(x) => void openDetail(x)} onMove={(x, status) => void quickStatus(x, status)}
        moduleLabel={(x) => { const m = moduleOf(x); return m ? `${m.moduleCode} · ${m.moduleName}` : null; }} />
    </section> : <article className="card lp-card dj-list" id="defect-list-results" tabIndex={-1} aria-label="รายการ Defect">
      {canEditDefects && selectedIds.length > 0 && (
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
      {/* แถวเดียว: ชิปสถานะ (ตัวกรองด่วน) | แบ่งหน้า (บน) */}
      <div className="dj-listbar">
        <div className="dj-quick" role="group" aria-label="กรองตามสถานะ">
          <button type="button" aria-pressed={statusFilter === ""} onClick={() => setStatusFilter("")}>ทั้งหมด <b>{summaryStats.total.toLocaleString()}</b></button>
          {defectStatusParts.map((p) => <button key={p.key} type="button" aria-pressed={statusFilter === p.value} onClick={() => setStatusFilter((current) => current === p.value ? "" : p.value)}><span className={`df-dot is-${p.key}`} aria-hidden="true" />{p.value} <b>{p.count.toLocaleString()}</b></button>)}
        </div>
        <ListPager position="top" page={page} pageCount={pageCount} totalCount={totalCount} pageSize={pageSize} disabled={loading} onPage={setPage} onPageSize={size => { setPageSize(size); setPage(1); }} />
      </div>
      <table className={`dj-table${canEditDefects ? " is-selectable" : ""}`}>
        <thead><tr>
          {canEditDefects && <th className="dj-col-select"><input type="checkbox" aria-label="เลือกทั้งหน้านี้" checked={selectedIds.length === items.length && items.length > 0} onChange={toggleSelectAll} /></th>}
          <SortHeader label="Key" sortKey="code" sort={listSort} onSort={setListSort} className="dj-col-key" />
          <th className="dj-col-summary">ชื่อ Defect</th>
          <SortHeader label="สถานะ" sortKey="status" sort={listSort} onSort={setListSort} className="dj-col-status" />
          <SortHeader label="Severity" sortKey="severity" sort={listSort} onSort={setListSort} className="dj-col-sev" />
          <th className="dj-col-module">Module</th>
          <th className="dj-col-assignee">เจ้าของเรื่อง</th>
          <SortHeader label="สร้างเมื่อ" sortKey="createdAt" sort={listSort} onSort={setListSort} className="dj-col-created" defaultDir="asc" />
          <th className="dj-col-crm">CRM</th>
          <th className="dj-col-menu"><span className="sr-only">จัดการ</span></th>
        </tr></thead>
        <tbody>
          {items.map(x => {
            const module = moduleOf(x);
            const crmStatus = x.crmSyncStatus ?? "None";
            const age = defectAgeDays(x.createdAt);
            const selected = selectedIds.includes(x.defectId);
            return <tr key={x.defectId} className={selected ? "is-selected" : undefined}>
              {canEditDefects && <td className="dj-col-select"><input type="checkbox" aria-label={`เลือก ${x.defectCode}`} checked={selected} onChange={() => setSelectedIds(prev => prev.includes(x.defectId) ? prev.filter(id => id !== x.defectId) : [...prev, x.defectId])} /></td>}
              <td className="dj-col-key"><span className="dj-key"><DefectTypeIcon /><button type="button" className={x.status === "Closed" ? "is-done" : undefined} onClick={() => void openDetail(x)} aria-label={`เปิด ${x.defectCode}`}>{x.defectCode}</button></span></td>
              <td className="dj-col-summary"><button type="button" className="dj-summary" onClick={() => void openDetail(x)} title={x.title}>{x.title}</button>
                {(x.releaseCode || x.buildNumber) && <small className="dj-row-meta">{[releaseText(x), x.buildNumber && `Build ${x.buildNumber}`].filter(Boolean).join(" · ")}</small>}</td>
              <td className="dj-col-status" data-label="สถานะ"><StatusLozenge status={x.status} /></td>
              <td className="dj-col-sev" data-label="Severity"><SeverityMark severity={x.severity} /></td>
              <td className="dj-col-module" data-label="Module">{module ? <span className="dj-module" title={`${module.moduleCode} · ${module.moduleName}`}>{module.moduleName}</span> : <span className="dj-muted">ไม่ระบุ</span>}</td>
              {/* เจ้าของเรื่อง = ผู้สร้าง Defect (API ส่งแค่ createdByUserId — ชื่อจากรายชื่อผู้ใช้ที่โหลดไว้แล้ว) */}
              <td className="dj-col-assignee" data-label="เจ้าของเรื่อง"><AssigneeCell name={userName(x.createdByUserId)} empty="ไม่ทราบผู้สร้าง" /></td>
              <td className="dj-col-created" data-label="สร้างเมื่อ" title={formatThaiDateTime(x.createdAt, { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" })}><span className={age > 30 && !isDefectDone(x.status) ? "df-age-old" : undefined}>{fmtAgo(x.createdAt)}</span></td>
              <td className="dj-col-crm" data-label="CRM">
                {canEditDefects && crmStatus === "None"
                  ? <button type="button" className="df-crm-send" title={`ส่ง ${x.defectCode} ไป CRM`} aria-label={`ส่ง ${x.defectCode} ไป CRM`} onClick={() => openCrmDialog("send", x)}><span className="material-symbols-outlined" aria-hidden="true">upload</span>ส่งไป CRM</button>
                  : <Badge tone={defectCrmSyncTones[crmStatus] ?? "gray"}>{defectCrmSyncLabels[crmStatus] ?? "ยังไม่ส่ง"}</Badge>}
                {crmDevName(x) && <small className="dj-row-meta" title={`Dev ที่รับงานใน CRM: ${crmDevName(x)}`}>Dev {crmDevName(x)}</small>}
              </td>
              <td className="dj-col-menu">{canEditDefects
                ? <ActionMenu className="dj-row-menu" label={`จัดการ ${x.defectCode}`} hideLabel disabled={false} items={rowMenuItems(x)} />
                : <button type="button" className="dj-icon-btn" title="ดูรายละเอียด" aria-label={`ดูรายละเอียด ${x.defectCode}`} onClick={() => void openDetail(x)}><span className="material-symbols-outlined" aria-hidden="true">open_in_new</span></button>}</td>
            </tr>;
          })}
          {!loading && !items.length && <tr className="dj-empty-row"><td colSpan={canEditDefects ? 10 : 9}><div className="lp-empty"><span className="material-symbols-outlined" aria-hidden="true">search_off</span><b>{activeDefectFilterCount ? "ไม่พบ Defect ตามตัวกรองที่เลือก" : "ยังไม่มี Defect ในขอบเขตที่เลือก"}</b>{activeDefectFilterCount > 0 && <button type="button" className="btn" onClick={clearDefectFilters}><span className="material-symbols-outlined" aria-hidden="true">filter_alt_off</span> ล้างตัวกรอง</button>}</div></td></tr>}
        </tbody>
      </table>
      <ListPager position="bottom" page={page} pageCount={pageCount} totalCount={totalCount} pageSize={pageSize} disabled={loading} onPage={setPage} onPageSize={size => { setPageSize(size); setPage(1); }} />
    </article>}
    {/* หน้าต่างรายละเอียดแบบ Jira issue view: ซ้าย = ชื่อ / เนื้อหา / Test Case / Activity, ขวา = ปุ่มสถานะ + แผงรายละเอียดที่แก้ได้ทันที
        ลำดับ JSX: รายละเอียด → ฟอร์ม → ผลการปิด → CRM เพื่อให้หน้าต่างที่เปิดจากรายละเอียดซ้อนอยู่บน (ModalShell ปิดเฉพาะบนสุด) */}
    {detail && (() => {
      const steps = detail.stepsToReproduce ? parseReproSteps(detail.stepsToReproduce) : null;
      const module = moduleOf(detail);
      const history = activities.filter(a => a.actionType !== "Comment" && a.actionType !== "CrmComment");
      const moveDetail = async (status: string) => { const updated = await quickStatus(detail, status); if (updated) await openDetail(updated, true); };
      const transitions: ActionMenuItem[] = [
        ...(detail.status === "Open" || detail.status === "Rejected" ? [{ key: "progress", icon: "play_arrow", label: "In Progress", hint: "เริ่มแก้ไข", onSelect: () => void moveDetail("In Progress") }] : []),
        ...(detail.status === "Open" || detail.status === "In Progress" ? [{ key: "resolve", icon: "task_alt", label: "Resolved", hint: "Dev แก้แล้ว รอ QA ตรวจซ้ำบน Fix Build", onSelect: () => void moveDetail("Resolved") }] : []),
        ...(detail.status !== "Closed" ? [{ key: "close", icon: "lock", label: detail.status === "Resolved" ? "Closed — ตรวจซ้ำผ่าน" : "Closed", hint: detail.status === "Resolved" ? "QA ตรวจซ้ำบน Build แล้วผ่าน" : "ปิดโดยไม่ต้องแก้ (ซ้ำ, ทำซ้ำไม่ได้, ตามที่ออกแบบ, เลื่อน)", onSelect: () => void moveDetail("Closed") }] : []),
        ...(detail.status === "Open" || detail.status === "In Progress" ? [{ key: "reject", icon: "block", label: "Rejected", hint: "ไม่ใช่ Defect", onSelect: () => void moveDetail("Rejected") }] : []),
        ...(detail.status === "Resolved" || detail.status === "Closed" ? [{ key: "reopen", icon: "replay", label: "Reopen", hint: "ปัญหายังอยู่หลังแก้ — กลับเป็น Open และนับจำนวนครั้ง", danger: true, onSelect: () => void moveDetail("Open") }] : []),
      ];
      const moreActions: ActionMenuItem[] = [
        { key: "edit", icon: "edit", label: "แก้ไขทั้งหมด", hint: "ชื่อ, เนื้อหา, ขั้นตอนการทำซ้ำ ฯลฯ", onSelect: () => openForm(detail) },
        detail.crmSyncStatus === "Linked"
          ? { key: "crm", icon: "sync_alt", label: "เปลี่ยนผู้รับผิดชอบ CRM", hint: `Ticket #${detail.crmTicketId} เดิม ไม่สร้างใหม่`, onSelect: () => void openCrmDialog("reassign", detail) }
          : { key: "crm", icon: "upload", label: "ส่งไป CRM", hint: "สร้าง Ticket ใหม่และผูกกับ Defect นี้", onSelect: () => void openCrmDialog("send", detail) },
        { key: "delete", icon: "delete", label: "ลบ Defect", hint: "กู้คืนได้จากปุ่ม \"ลบแล้ว\"", danger: true, onSelect: () => void removeDefect(detail).then((removed) => { if (removed) setDetail(null); }) },
      ];
      const dateText = (iso?: string | null) => iso ? formatThaiDateTime(iso, { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" }) : "-";
      return (
        <ModalShell labelledBy="dj-detail-title" className="dj-detail" onDismiss={() => setDetail(null)}>
          <div className="dj-detail-top">
            <nav className="dj-crumbs" aria-label="ตำแหน่ง">
              <span className="dj-crumb-project">{projectName || "Project"}</span>
              <span aria-hidden="true">/</span>
              <span className="dj-key"><DefectTypeIcon /><b>{detail.defectCode}</b></span>
              <button type="button" className="dj-icon-btn" title="คัดลอกรหัส Defect" aria-label={codeCopied ? "คัดลอกรหัสแล้ว" : "คัดลอกรหัส Defect"} onClick={async () => { const ok = await copyText(detail.defectCode); setCodeCopied(ok); setTimeout(() => setCodeCopied(false), 1500); }}>
                <span className="material-symbols-outlined" aria-hidden="true">{codeCopied ? "check" : "content_copy"}</span>
              </button>
            </nav>
            <div className="dj-detail-top-actions">
              {canEditDefects && <ActionMenu className="dj-more-menu" label="การดำเนินการเพิ่มเติม" hideLabel disabled={false} items={moreActions} />}
              <button type="button" className="dj-icon-btn" aria-label="ปิดรายละเอียด Defect" onClick={() => setDetail(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
            </div>
          </div>
          {detailError && <div className="inline-alert error dj-detail-alert" role="alert"><span>{detailError}</span></div>}
          <div className="dj-detail-body">
            <div className="dj-detail-headline">
              <h1 id="dj-detail-title" tabIndex={-1} data-autofocus>{detail.title}</h1>
              {canEditDefects && <div className="dj-detail-actions">
                <button type="button" className="btn" onClick={() => openForm(detail)}><span className="material-symbols-outlined" aria-hidden="true">edit</span>แก้ไข</button>
                {detail.crmSyncStatus !== "Linked" && <button type="button" className="btn" onClick={() => void openCrmDialog("send", detail)}><span className="material-symbols-outlined" aria-hidden="true">upload</span>ส่งไป CRM</button>}
              </div>}
            </div>
            <aside className="dj-detail-side" aria-label="สถานะและรายละเอียด">
              {canEditDefects && transitions.length
                ? <ActionMenu className={`dj-status-menu is-${defectStatusKey(detail.status)}`} label={detail.status} icon="expand_more" disabled={detailSaving} items={transitions} />
                : <StatusLozenge status={detail.status} />}
              <section className="dj-fields" aria-labelledby="dj-fields-title">
                <h2 id="dj-fields-title">รายละเอียด {detailSaving && <span className="spinner inline" aria-label="กำลังบันทึก" />}</h2>
                <dl>
                  <div><dt>ผู้รับผิดชอบ</dt><dd>{(() => {
                    const crmDev = crmDevName(detail);
                    // ยังไม่มอบหมายใน QA Hub แต่ CRM มี Dev รับงานแล้ว = แสดง Dev จาก CRM แทน "ยังไม่มอบหมาย"; มอบหมายทั้งสองฝั่ง = Dev ใน CRM เป็นบรรทัดรอง
                    const crmLine = crmDev && <span className="dj-crm-dev" title="Dev ที่รับงานใน CRM Ticket — เปลี่ยนได้จากเมนู ⋯ › เปลี่ยนผู้รับผิดชอบ CRM"><AssigneeCell name={crmDev} /><span className="dj-crm-tag">CRM</span></span>;
                    if (!canEditDefects) return detail.assigneeName || !crmLine ? <><AssigneeCell name={detail.assigneeName} />{crmLine}</> : crmLine;
                    const select = <label className="dj-inline-field"><Avatar name={detail.assigneeName} /><select aria-label="ผู้รับผิดชอบใน QA Hub" value={detail.assigneeUserId ?? ""} disabled={detailSaving} onChange={(e) => void updateDetailField({ assigneeUserId: e.target.value || null })}><option value="">{crmDev ? "ไม่ระบุใน QA Hub" : "ยังไม่มอบหมาย"}</option>{users.map(u => <option key={u.userId} value={u.userId}>{u.displayName}</option>)}</select></label>;
                    return detail.assigneeUserId || !crmLine ? <>{select}{crmLine}</> : <>{crmLine}{select}</>;
                  })()}</dd></div>
                  <div><dt>ผู้แจ้ง</dt><dd><AssigneeCell name={detail.createdByName ?? userName(detail.createdByUserId)} empty="ไม่ทราบผู้สร้าง" /></dd></div>
                  <div><dt>Severity</dt><dd>{canEditDefects
                    ? <label className="dj-inline-field"><SeverityMark severity={detail.severity} showLabel={false} /><select aria-label="Severity" value={detail.severity} disabled={detailSaving} onChange={(e) => void updateDetailField({ severity: e.target.value })}>{defectSeverities.map(s => <option key={s}>{s}</option>)}</select></label>
                    : <SeverityMark severity={detail.severity} />}</dd></div>
                  <div><dt>Module</dt><dd>{module ? <span title={`${module.moduleCode} · ${module.moduleName}`}>{module.moduleName}<small>{module.moduleCode}</small></span> : <span className="dj-muted">ไม่ระบุ</span>}</dd></div>
                  <div><dt>Release</dt><dd>{releaseText(detail) ?? <span className="dj-muted">ไม่ระบุ</span>}</dd></div>
                  <div><dt>เจอใน Build</dt><dd>{detail.buildNumber ?? <span className="dj-muted">ไม่ระบุ</span>}</dd></div>
                  {(detail.fixedInBuildId || detail.status === "Resolved" || detail.status === "Closed") && <div><dt>Fix Build</dt><dd>{detail.fixedInBuildNumber ?? <span className="dj-muted">ไม่ระบุ Build</span>}</dd></div>}
                  {detail.verifiedInBuildNumber && <div><dt>ตรวจซ้ำผ่านใน</dt><dd>{detail.verifiedInBuildNumber}</dd></div>}
                  {(detail.reopenCount ?? 0) > 0 && <div><dt>Reopen</dt><dd><Badge tone="red">{detail.reopenCount} ครั้ง</Badge></dd></div>}
                  {detail.regressionOfDefectCode && <div><dt>ปัญหาเดิมของ</dt><dd>{detail.regressionOfDefectCode}</dd></div>}
                  <div><dt>อายุ</dt><dd><span className={defectAgeDays(detail.createdAt) > 30 && !isDefectDone(detail.status) ? "df-age-old" : undefined}>{defectAgeDays(detail.createdAt)} วัน</span></dd></div>
                  <div><dt>CRM</dt><dd>{detail.crmSyncStatus === "Linked" && detail.crmTicketId
                    ? <a href={`https://bluesea.seniorsoft.com/bluesea/BookLicence/MA/Support/JobDetailsHD?JobNo=${encodeURIComponent(detail.crmTicketId)}&JobType=HD`} target="_blank" rel="noreferrer" title={`เปิด Ticket #${detail.crmTicketId} ใน CRM`}>#{detail.crmTicketId}</a>
                    : <Badge tone={defectCrmSyncTones[detail.crmSyncStatus ?? "None"] ?? "gray"}>{defectCrmSyncLabels[detail.crmSyncStatus ?? "None"] ?? "ยังไม่ส่ง"}</Badge>}</dd></div>
                </dl>
              </section>
              <p className="dj-dates">
                <span>สร้าง {dateText(detail.createdAt)}</span>
                {detail.updatedAt && <span>อัปเดต {dateText(detail.updatedAt)}{detail.updatedByName ? ` · ${detail.updatedByName}` : ""}</span>}
              </p>
            </aside>
            <div className="dj-detail-main">
              <section className="dj-section">
                <h3>รายละเอียด</h3>
                {detail.description
                  ? <DefectDescription text={detail.description} hide={[detail.actualResult ?? "", detail.expectedResult ?? ""]} />
                  : <p className="dj-text dj-muted">ไม่มีรายละเอียด</p>}
              </section>
              {(detail.actualResult || detail.expectedResult) && <div className="dj-result-pair">
                <section className="dj-section dj-actual"><h3><span className="material-symbols-outlined" aria-hidden="true">close</span>ผลที่เกิดขึ้นจริง</h3><p className="dj-text">{detail.actualResult || <span className="dj-muted">ไม่ระบุ</span>}</p></section>
                <section className="dj-section dj-expected"><h3><span className="material-symbols-outlined" aria-hidden="true">check</span>ผลที่คาดหวัง</h3><p className="dj-text">{detail.expectedResult || <span className="dj-muted">ไม่ระบุ</span>}</p></section>
              </div>}
              <section className="dj-section">
                <h3>ขั้นตอนการทำซ้ำ</h3>
                {detail.stepsToReproduce ? (steps ? <ReproSteps steps={steps} /> : <p className="dj-text">{detail.stepsToReproduce}</p>) : <p className="dj-text dj-muted">ไม่มีขั้นตอนการทำซ้ำ</p>}
              </section>
              {(detail.resolutionType || defectClosingStatuses.includes(detail.status)) && <section className="dj-section dj-resolution">
                <h3>ผลการปิด</h3>
                {detail.resolutionType ? <dl className="defect-resolution-view">
                  <div><dt>ผล</dt><dd>{defectResolutionLabel(detail.resolutionType)}{detail.duplicateOfDefectCode ? ` · ซ้ำกับ ${detail.duplicateOfDefectCode}` : ""}</dd></div>
                  {detail.rootCauseCategory && <div><dt>หมวดสาเหตุ</dt><dd>{rootCauseName(detail.rootCauseCategory)}</dd></div>}
                  {detail.rootCauseNote && <div className="full"><dt>สาเหตุ</dt><dd>{detail.rootCauseNote}</dd></div>}
                  {detail.resolutionNote && <div className="full"><dt>วิธีแก้</dt><dd>{detail.resolutionNote}</dd></div>}
                </dl> : <p className="dj-muted">ยังไม่ระบุสาเหตุและวิธีแก้ (ปิดจากช่องทางอื่น เช่น เปลี่ยนสถานะหลายรายการ) — บันทึกไว้เพื่อให้คลังความรู้และการเตือน Defect ซ้ำแม่นขึ้น</p>}
                {canEditDefects && <button type="button" className="btn defect-resolution-edit" onClick={() => setResolving({ item: detail, status: null, mode: "edit" })}><span className="material-symbols-outlined" aria-hidden="true">edit_note</span>{detail.resolutionType ? "แก้ไขผลการปิด" : "บันทึกผลการปิด"}</button>}
              </section>}
              <section className="dj-section">
                <h3>Test Case ที่เชื่อมโยง <span className="dj-count">{linkedCases.length}</span></h3>
                {linkedCases.length ? <ul className="dj-links">
                  {linkedCases.map(tc => <li key={tc.testCaseId}>
                    <span className="material-symbols-outlined dj-link-icon" aria-hidden="true">fact_check</span>
                    <b>{tc.testCaseCode}</b>
                    <span className="dj-link-title" title={tc.title}>{tc.title}</span>
                    <Badge tone={tc.status ? (testCaseStatusTones[tc.status] ?? "gray") : "gray"}>{tc.status ?? "-"}</Badge>
                    {onOpenTestCase && <button type="button" className="dj-icon-btn" title={`เปิด ${tc.testCaseCode}`} aria-label={`เปิด ${tc.testCaseCode}`} onClick={() => { const id = tc.testCaseId; setDetail(null); onOpenTestCase(id); }}><span className="material-symbols-outlined" aria-hidden="true">open_in_new</span></button>}
                  </li>)}
                </ul> : <p className="dj-muted">{detailError.includes("Test Case") ? "โหลด Test Case ที่เชื่อมโยงไม่สำเร็จ" : "ยังไม่มี Test Case ที่เชื่อมโยง"}</p>}
              </section>
              <section className="dj-section dj-activity" aria-label="Activity">
                <h3>Activity</h3>
                <div className="dj-tabs" role="tablist" aria-label="Activity">
                  <button type="button" role="tab" id="dj-tab-comments" aria-controls="dj-panel-comments" aria-selected={detailTab === "comments"} tabIndex={detailTab === "comments" ? 0 : -1}
                    onClick={() => setDetailTab("comments")} onKeyDown={(e) => { if (e.key === "ArrowRight" || e.key === "ArrowLeft") { setDetailTab("activities"); document.getElementById("dj-tab-activities")?.focus(); } }}>ความคิดเห็น <span className="dj-count">{comments.length}</span></button>
                  <button type="button" role="tab" id="dj-tab-activities" aria-controls="dj-panel-activities" aria-selected={detailTab === "activities"} tabIndex={detailTab === "activities" ? 0 : -1}
                    onClick={() => setDetailTab("activities")} onKeyDown={(e) => { if (e.key === "ArrowRight" || e.key === "ArrowLeft") { setDetailTab("comments"); document.getElementById("dj-tab-comments")?.focus(); } }}>ประวัติ <span className="dj-count">{history.length}</span></button>
                </div>
                {detailTab === "comments" ? <div role="tabpanel" id="dj-panel-comments" aria-labelledby="dj-tab-comments" className="dj-panel">
                  {canEditDefects && <div className="dj-composer"><DefectCommentComposer onSubmit={submitComment} /></div>}
                  <DefectCommentList comments={[...comments].reverse()} imageSrc={(id) => commentImageUrls[id]} emptyText={detailError.includes("คอมเมนต์") ? "โหลดคอมเมนต์ไม่สำเร็จ" : "ยังไม่มีคอมเมนต์"} />
                </div> : <div role="tabpanel" id="dj-panel-activities" aria-labelledby="dj-tab-activities" className="dj-panel">
                  {history.length ? <ol className="dj-history">
                    {history.map(a => <li key={a.activityId}>
                      <Avatar name={a.actorName ?? "System"} size={28} />
                      <div>
                        <p><b>{a.actorName ?? "System"}</b> <span className="dj-history-action">{defectActionLabels[a.actionType] ?? a.actionType}</span> <small title={dateText(a.performedAt ?? a.createdAt)}>{fmtAgo(a.performedAt ?? a.createdAt)}</small></p>
                        {a.message && <p className="dj-history-message">{a.message}</p>}
                        {a.attachments && a.attachments.length > 0 && <div className="defect-comment-thumbs dj-history-thumbs">
                          {a.attachments.map((x, i) => {
                            const src = activityImageUrls[x.attachmentId];
                            return <button type="button" key={x.attachmentId} aria-label={`ดูรูป ${x.fileName} ขนาดใหญ่`} title={x.fileName} onClick={() => setActivityViewer({ attachments: a.attachments!, index: i })}>
                              {src ? <img src={src} alt={x.fileName} loading="lazy" /> : <span className="material-symbols-outlined" aria-hidden="true">image</span>}
                            </button>;
                          })}
                        </div>}
                      </div>
                    </li>)}
                  </ol> : <p className="dj-muted">ยังไม่มีประวัติ</p>}
                </div>}
              </section>
            </div>
          </div>
          {activityViewer && <ImageLightbox images={toLightboxImages(activityViewer.attachments, (id) => activityImageUrls[id])} index={activityViewer.index} onIndexChange={(index) => setActivityViewer({ ...activityViewer, index })} onClose={() => setActivityViewer(null)} />}
        </ModalShell>
      );
    })()}
    {/* ฟอร์มสร้าง/แก้ไขใช้โครงเดียวกับหน้าต่างรายละเอียด (issue view): แถบบน Project / รหัส | ซ้าย = ชื่อ + เนื้อหา | ขวา = แผงรายละเอียด
        ปุ่มบันทึกติดล่าง; ตอนสร้างไม่มีช่องสถานะ (เริ่มที่ Open เสมอ); Module ตั้งได้ตอนสร้างเท่านั้น — API แก้ไขไม่บันทึก Module */}
    {formOpen && (() => {
      const severityHint = defectSeverityOptions.find(x => x.value === formSeverity)?.hint;
      const assigneeName = users.find(u => u.userId === formAssigneeUserId)?.displayName ?? null;
      const filledSteps = formSteps.filter(defectStepStarted);
      const incompleteSteps = filledSteps.filter(x => !x.action.trim() || !x.expectedResult.trim()).length;
      const stepsTooLong = !editing && composeReproSteps(filledSteps).length > stepsToReproduceMax;
      const missing = [
        !formTitle.trim() && "ชื่อ Defect",
        !editing && !formModuleId && "Module",
        !editing && filledSteps.length === 0 && "ขั้นตอนการทำซ้ำอย่างน้อย 1 ขั้น",
        !editing && incompleteSteps > 0 && `การกระทำ/ผลที่คาดหวังของ ${incompleteSteps} ขั้น`,
      ].filter(Boolean) as string[];
      const formStatusText = missing.length ? `ยังขาด: ${missing.join(", ")}` : stepsTooLong ? `ขั้นตอนรวมยาวเกิน ${stepsToReproduceMax.toLocaleString()} ตัวอักษร` : "";
      const selectedModule = modules.find(m => m.moduleId === formModuleId);
      const updateStep = (index: number, field: keyof DefectFormStep, value: string) => setFormSteps(current => current.map((x, i) => i === index ? { ...x, [field]: value } : x));
      const moveStep = (index: number, delta: number) => setFormSteps(current => { const next = [...current]; [next[index], next[index + delta]] = [next[index + delta], next[index]]; return next; });
      return <ModalShell labelledBy="dj-form-title" className={`dj-detail dj-editor${editing ? "" : " is-create"}`} onDismiss={() => setFormOpen(false)}>
        <div className="dj-detail-top">
          <nav className="dj-crumbs" aria-label="ตำแหน่ง">
            <span className="dj-crumb-project">{projectName || "Project"}</span>
            <span aria-hidden="true">/</span>
            <span className="dj-key"><DefectTypeIcon /><b id="dj-form-title">{editing ? `แก้ไข ${editing.defectCode}` : "สร้าง Defect ใหม่"}</b></span>
          </nav>
          <div className="dj-detail-top-actions">
            <button type="button" className="dj-icon-btn" aria-label="ปิดหน้าต่าง" onClick={() => setFormOpen(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
          </div>
        </div>
        <div className="dj-detail-body">
          <div className="dj-detail-headline">
            <label className="dj-editor-title"><span>ชื่อ Defect <span className="required" aria-hidden="true">*</span></span>
              <input value={formTitle} onChange={e => setFormTitle(e.target.value)} placeholder="สรุปปัญหาสั้น ๆ เช่น บันทึกใบสั่งซื้อแล้วยอดรวมไม่ถูกต้อง" aria-required="true" />
            </label>
            {/* ตอนสร้าง: Module (บังคับ — Test Case อยู่ใน Module นี้) + เงื่อนไขก่อนทดสอบ อยู่แถวเดียวใต้ชื่อ */}
            {!editing && <div className="dj-create-row">
              <label className="dj-editor-field"><span className="dj-editor-label">Module <span className="required" aria-hidden="true">*</span></span>
                <select className="dj-editor-select" aria-required="true" value={formModuleId} onChange={e => setFormModuleId(e.target.value)}><option value="">เลือก Module — Test Case จะอยู่ใน Module นี้</option>{renderModuleSelectOptions(modules)}</select>
              </label>
              <label className="dj-editor-field"><span className="dj-editor-label">เงื่อนไขก่อนทดสอบ</span>
                <textarea rows={1} value={formPreconditions} onChange={e => setFormPreconditions(e.target.value)} placeholder="เช่น Login ด้วยสิทธิ์พนักงานขาย, มีสินค้าในสต็อก" />
              </label>
            </div>}
            {/* พับไว้เป็นแถวเดียวในฟอร์มสร้าง (กดเปิดดูรายการ) — กล่องเต็มดันฟอร์มลงจนต้องเลื่อน */}
            {!editing && similar.length > 0 && <details className="defect-similar dj-similar">
              <summary><span className="material-symbols-outlined" aria-hidden="true">content_copy</span> อาจซ้ำกับ Defect ที่มีอยู่ ({similar.length}) <small>ตรวจก่อนสร้าง — ถ้าเป็นปัญหาเดียวกัน ให้คอมเมนต์ใน Defect เดิม</small></summary>
              <ul>{similar.map(s => <li key={s.defectId}>
                <a href={`#/defects?defectId=${s.defectId}`} target="_blank" rel="noreferrer" title={`${s.defectCode} ${s.title} — เปิดในแท็บใหม่`}><b>{s.defectCode}</b><span>{s.title}</span></a>
                <StatusLozenge status={s.status} />
                {s.resolutionType && <small>{defectResolutionLabel(s.resolutionType)}</small>}
              </li>)}</ul>
            </details>}
          </div>
          <aside className="dj-detail-side" aria-label="รายละเอียด">
            <section className="dj-fields" aria-labelledby="dj-editor-fields-title">
              <h2 id="dj-editor-fields-title">รายละเอียด</h2>
              <dl>
                {editing && <div><dt>สถานะ</dt><dd><label className="dj-inline-field"><select aria-label="สถานะ" value={formStatus} onChange={e => setFormStatus(e.target.value)}>{defectStatuses.map(s => <option key={s}>{s}</option>)}</select></label></dd></div>}
                <div><dt>ผู้รับผิดชอบ</dt><dd><label className="dj-inline-field"><Avatar name={assigneeName} /><select aria-label="ผู้รับผิดชอบ" value={formAssigneeUserId} onChange={e => setFormAssigneeUserId(e.target.value)}><option value="">ยังไม่มอบหมาย</option>{users.map(u => <option key={u.userId} value={u.userId}>{u.displayName}</option>)}</select></label></dd></div>
                <div className="dj-field-top"><dt>Severity</dt><dd><label className="dj-inline-field"><SeverityMark severity={formSeverity} showLabel={false} /><select aria-label="Severity" aria-describedby="dj-editor-sev-hint" value={formSeverity} onChange={e => setFormSeverity(e.target.value)}>{defectSeverities.map(s => <option key={s}>{s}</option>)}</select></label><small id="dj-editor-sev-hint" className="dj-field-hint">{severityHint}</small></dd></div>
                {editing && <div className="dj-field-top"><dt>Module</dt><dd><select className="dj-editor-select" aria-label="Module" value={formModuleId} disabled onChange={e => setFormModuleId(e.target.value)}><option value="">ไม่ระบุ Module</option>{renderModuleSelectOptions(modules)}</select><small className="dj-field-hint">เปลี่ยน Module ได้ตอนสร้างเท่านั้น</small></dd></div>}
                <div><dt>Release</dt><dd>{(editing ? releaseText(editing) : releaseLabel) || <span className="dj-muted">ไม่ระบุ</span>}</dd></div>
                <div><dt>เจอใน Build</dt><dd>{(editing ? editing.buildNumber : buildLabel) || <span className="dj-muted">ไม่ระบุ</span>}</dd></div>
              </dl>
            </section>
            {!editing && <section className="dj-fields dj-tc-preview" aria-labelledby="dj-tc-preview-title">
              <h2 id="dj-tc-preview-title"><span className="material-symbols-outlined" aria-hidden="true">fact_check</span>Test Case ที่จะสร้างให้</h2>
              <dl>
                <div><dt>ชื่อ</dt><dd>{formTitle.trim() || <span className="dj-muted">ตามชื่อ Defect</span>}</dd></div>
                <div><dt>Module</dt><dd>{selectedModule ? `${selectedModule.moduleCode} · ${selectedModule.moduleName}` : <span className="dj-muted">ยังไม่เลือก</span>}</dd></div>
                <div><dt>Priority</dt><dd>{severityTestCasePriority[formSeverity] ?? "P2"} <span className="dj-muted">(ตาม Severity)</span></dd></div>
                <div><dt>Step</dt><dd>{filledSteps.length} ขั้น</dd></div>
                <div><dt>สถานะ</dt><dd>Draft</dd></div>
              </dl>
              <p className="dj-field-hint">เชื่อมโยงกับ Defect นี้ และนำไปใช้ทดสอบซ้ำ / ใส่ Test Suite ได้ภายหลัง</p>
            </section>}
            {!editing && <p className="dj-dates">Release / Build มาจากตัวเลือกด้านบนของหน้า</p>}
          </aside>
          <div className="dj-detail-main">
            <div className="dj-section dj-editor-field"><label htmlFor="dj-f-description" className="dj-editor-label">รายละเอียด</label><textarea id="dj-f-description" rows={editing ? 4 : 2} value={formDescription} onChange={e => setFormDescription(e.target.value)} placeholder="ข้อมูลทดสอบ, Environment, ความถี่ที่เกิด ฯลฯ" /></div>
            <div className="dj-result-pair">
              <div className="dj-section dj-editor-field dj-actual"><label htmlFor="dj-f-actual" className="dj-editor-label"><span className="material-symbols-outlined" aria-hidden="true">close</span>ผลที่เกิดขึ้นจริง</label><textarea id="dj-f-actual" rows={editing ? 3 : 2} value={formActualResult} onChange={e => setFormActualResult(e.target.value)} placeholder="ระบบแสดง / ทำอะไร" /></div>
              <div className="dj-section dj-editor-field dj-expected"><label htmlFor="dj-f-expected" className="dj-editor-label"><span className="material-symbols-outlined" aria-hidden="true">check</span>ผลที่คาดหวัง</label><textarea id="dj-f-expected" rows={editing ? 3 : 2} value={formExpectedResult} onChange={e => setFormExpectedResult(e.target.value)} placeholder="ควรแสดง / ทำอะไร" /></div>
            </div>
            {editing
              ? <div className="dj-section dj-editor-field"><label htmlFor="dj-f-steps" className="dj-editor-label">ขั้นตอนการทำซ้ำ</label><textarea id="dj-f-steps" rows={6} value={formStepsToReproduce} onChange={e => setFormStepsToReproduce(e.target.value)} placeholder={"1. เปิดหน้า …\n2. กรอก …\n3. กดบันทึก"} /></div>
              : <>
                <section className="dj-section dj-steps" aria-labelledby="dj-steps-title">
                  <div className="dj-steps-head">
                    <h3 id="dj-steps-title" className="dj-editor-label">ขั้นตอนการทำซ้ำ <span className="required" aria-hidden="true">*</span> <span className="tce-count">{filledSteps.length}</span></h3>
                    {incompleteSteps > 0 && <small className="tce-steps-warning"><span className="material-symbols-outlined" aria-hidden="true">error</span> ยังกรอกไม่ครบ {incompleteSteps} ขั้น</small>}
                  </div>
                  <div className="tce-step-header" aria-hidden="true"><span>#</span><span>การกระทำ <b className="required-mark">*</b></span><span>ข้อมูลที่ใช้</span><span>ผลที่คาดหวัง <b className="required-mark">*</b></span><span /></div>
                  <ol className="tce-step-list">
                    {formSteps.map((step, index) => (
                      <li className="tce-step" key={index}>
                        <span className="tce-step-no">{index + 1}</span>
                        <label><small>การกระทำ</small><textarea rows={1} placeholder="เช่น เปิดหน้าขาย แล้วสแกนสินค้า" value={step.action} aria-label={`การกระทำของขั้นที่ ${index + 1}`} aria-invalid={defectStepStarted(step) && !step.action.trim()} onChange={e => updateStep(index, "action", e.target.value)} /></label>
                        <label><small>ข้อมูลที่ใช้</small><textarea rows={1} placeholder="ถ้ามี เช่น รหัสสินค้า" value={step.testData} aria-label={`ข้อมูลที่ใช้ของขั้นที่ ${index + 1}`} onChange={e => updateStep(index, "testData", e.target.value)} /></label>
                        <label><small>ผลที่คาดหวัง</small><textarea rows={1} placeholder="ระบบควรแสดง / ทำอะไร" value={step.expectedResult} aria-label={`ผลที่คาดหวังของขั้นที่ ${index + 1}`} aria-invalid={defectStepStarted(step) && !step.expectedResult.trim()} onChange={e => updateStep(index, "expectedResult", e.target.value)} /></label>
                        <div className="tce-step-actions">
                          <button type="button" title="เลื่อนขึ้น" aria-label={`เลื่อนขั้นที่ ${index + 1} ขึ้น`} disabled={index === 0} onClick={() => moveStep(index, -1)}><span className="material-symbols-outlined" aria-hidden="true">arrow_upward</span></button>
                          <button type="button" title="เลื่อนลง" aria-label={`เลื่อนขั้นที่ ${index + 1} ลง`} disabled={index === formSteps.length - 1} onClick={() => moveStep(index, 1)}><span className="material-symbols-outlined" aria-hidden="true">arrow_downward</span></button>
                          <button type="button" className="tce-step-remove" title="ลบขั้น" aria-label={`ลบขั้นที่ ${index + 1}`} disabled={formSteps.length <= 1} onClick={() => setFormSteps(current => current.filter((_, i) => i !== index))}><span className="material-symbols-outlined" aria-hidden="true">delete</span></button>
                        </div>
                      </li>
                    ))}
                  </ol>
                  <button type="button" className="dj-add-step" onClick={() => setFormSteps(current => [...current, emptyDefectStep()])}><span className="material-symbols-outlined" aria-hidden="true">add</span> เพิ่มขั้นตอน</button>
                </section>
              </>}
            {error && <div className="inline-alert error dj-section" role="alert"><span>{error}</span></div>}
          </div>
        </div>
        <div className="dj-editor-actions">
          {!editing && <label className="dj-create-another"><input type="checkbox" checked={createAnother} onChange={e => setCreateAnother(e.target.checked)} />สร้างต่ออีกรายการ</label>}
          <span className={`dj-form-status${formStatusText ? " is-missing" : ""}`} role="status">{formStatusText}</span>
          <button className="btn" onClick={() => setFormOpen(false)}>ยกเลิก</button>
          <button className="btn primary" disabled={saving || !!formStatusText} onClick={() => void saveForm()}>{saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : editing ? "บันทึก" : "สร้าง Defect + Test Case"}</button>
        </div>
      </ModalShell>;
    })()}
    {resolving && projectId && <DefectResolutionModal
      defect={{ defectId: resolving.item?.defectId, defectCode: resolving.item?.defectCode, title: resolving.item?.title ?? formTitle, projectId: resolving.item?.projectId ?? projectId }}
      targetStatus={resolving.status} currentStatus={resolving.item?.status ?? "Open"} releaseId={resolving.item?.releaseId ?? releaseId ?? null} fixedInBuildId={resolving.item?.fixedInBuildId ?? null}
      rootCauses={rootCauses} initial={resolving.item}
      onCancel={() => setResolving(null)} onConfirm={confirmResolution} />}
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
