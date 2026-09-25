import { toUtcDate, formatThaiDateTime } from "../dateTime";
import { useState, useRef, useMemo, useCallback, useEffect } from "react";
import type { DefectItem, UserLookup } from "../shared/types";
import { apiUrl, getJson } from "../api";
import { confirmDialog, notify } from "../components/dialogStore";
import { exportDefectModulePdf } from "../DefectModulePdf";
import { Badge } from "../components/Badge";
import { defectAgeDays } from "../shared/defects";
import { ModalShell } from "../components/ModalShell";
import { type ModuleItem, copyText, defectStatusTones, renderModuleSelectOptions } from "../shared/appShared";

type DefectActivityItem = { activityId: string; actionType: string; message: string; actorUserId?: string | null; actorName?: string | null; createdAt: string; performedByUserId?: string | null; performedAt?: string };
type DefectTestCaseItem = { testCaseId: string; testCaseCode: string; title: string; priority?: string; status?: string; linkedAt?: string };
const defectSeverities = ["Critical", "High", "Medium", "Low"];
const defectStatuses = ["Open", "In Progress", "Resolved", "Closed", "Rejected"];
type DefectModuleCount = { moduleId: string | null; moduleCode: string | null; moduleName: string; count: number };
const emptyDefectStats = { total: 0, open: 0, inProgress: 0, resolved: 0, closed: 0, rejected: 0, critical: 0, high: 0, medium: 0, low: 0, oldestOpenAgeDays: 0, unassignedActive: 0, closureRate: 0, modules: [] as DefectModuleCount[] };
const defectPct = (count: number, total: number): number => total > 0 ? Math.round((count / total) * 100) : 0;
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
type DefectReproStep = { stepNo: number; action: string; status?: "Pass" | "Fail"; detail: string };
// "Steps to Reproduce" เป็น freeform text — ถ้าเขียนตามรูปแบบ "1. Action (Pass/Fail) | รายละเอียด" จะแปลงเป็น
// การ์ดลำดับขั้นตอนพร้อม Badge ผลลัพธ์ให้ ถ้าไม่ตรงรูปแบบ (ไม่ได้ขึ้นต้นด้วยเลขข้อทุกบรรทัด) จะคืน null ให้แสดง
// เป็นข้อความธรรมดาแทน ไม่พังแม้ข้อมูลจะเป็น text อิสระที่ไม่ได้ตามรูปแบบนี้
function parseReproSteps(text: string): DefectReproStep[] | null {
  const lines = text.split(/\r?\n/).map((l) => l.trim()).filter(Boolean);
  if (!lines.length) return null;
  const steps: DefectReproStep[] = [];
  for (const line of lines) {
    const m = line.match(/^(\d+)[.)]\s*(.+)$/);
    if (!m) return null;
    const stepNo = Number(m[1]);
    const rest = m[2];
    const statusMatch = rest.match(/^(.*?)\s*\((Pass|Fail)\)\s*(?:\|\s*(.*))?$/);
    if (statusMatch) {
      const [, action, status, detailPart] = statusMatch;
      steps.push({ stepNo, action: action.trim(), status: status as "Pass" | "Fail", detail: (detailPart ?? "").trim() });
    } else {
      const parts = rest.split("|");
      steps.push({ stepNo, action: parts[0].trim(), detail: parts.slice(1).join("|").trim() });
    }
  }
  return steps;
}

export function DefectsPage({ projectId, releaseId, buildId, projectName, releaseLabel, buildLabel, search, onClearSearch, canEdit, canExport, onOpenTestCase }: { projectId?: string; releaseId?: string; buildId?: string; projectName?: string; releaseLabel?: string; buildLabel?: string; search: string; onClearSearch?: () => void; canEdit?: boolean; canExport?: boolean; onOpenTestCase?: (testCaseId: string) => void }) {
  const [items, setItems] = useState<DefectItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(30);
  const [moduleFilter, setModuleFilter] = useState("");
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
  const [commentText, setCommentText] = useState("");
  const [commentSending, setCommentSending] = useState(false);
  const [codeCopied, setCodeCopied] = useState(false);
  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<DefectItem | null>(null);
  const [formTitle, setFormTitle] = useState("");
  const [formSeverity, setFormSeverity] = useState("Medium");
  const [formStatus, setFormStatus] = useState("Open");
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
  }, [projectId, releaseId, buildId, search, moduleFilter, severityFilter, priorityFilter, statusFilter, assigneeFilter, page, pageSize, headers, reload]);
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
  const saveForm = async () => {
    setSaving(true); setError("");
    try {
      const body = { moduleId: formModuleId || null, title: formTitle, severity: formSeverity, status: formStatus, description: formDescription || null, stepsToReproduce: formStepsToReproduce || null, expectedResult: formExpectedResult || null, actualResult: formActualResult || null, assigneeUserId: formAssigneeUserId || null, releaseId: releaseId || null, buildId: buildId || null };
      const response = await fetch(editing ? `${apiUrl}/defects/${editing.defectId}` : `${apiUrl}/defects`, { method: editing ? "PUT" : "POST", headers, body: JSON.stringify(body) });
      if (!response.ok) { const p = await response.json().catch(() => null); throw new Error(p?.detail ?? "บันทึก Defect ไม่สำเร็จ"); }
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
    if (status === "Closed" && !await confirmDialog({ title: "ปิด Defect", message: `ปิด ${item.defectCode} ใช่หรือไม่?\nDefect ที่ปิดแล้วจะไม่อยู่ในรายการที่ต้องติดตาม`, confirmLabel: "ปิด Defect" })) return;
    const response = await fetch(`${apiUrl}/defects/${item.defectId}/status`, { method: "PATCH", headers, body: JSON.stringify({ status }) });
    if (response.ok) { setNotice(`เปลี่ยนสถานะ ${item.defectCode} เป็น ${status}`); setReload(x => x + 1); }
    else notify(`เปลี่ยนสถานะ ${item.defectCode} ไม่สำเร็จ`, "error");
  };
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
    setDetail(item); setActivities([]); setLinkedCases([]); setCommentText(""); setCodeCopied(false); setDetailLoading(true); setDetailError("");
    try {
      // โหลดไม่สำเร็จต้องบอก — เดิมแสดงเป็น "ยังไม่มีกิจกรรม / ยังไม่มี Test Case ที่เชื่อมโยง"
      const failed: string[] = [];
      const [actRes, tcRes] = await Promise.all([
        getJson<any[]>(`${apiUrl}/defects/${item.defectId}/activities`).catch((): any[] => { failed.push("ประวัติกิจกรรม"); return []; }),
        getJson<DefectTestCaseItem[]>(`${apiUrl}/defects/${item.defectId}/test-cases`).catch((): DefectTestCaseItem[] => { failed.push("Test Case ที่เชื่อมโยง"); return []; }),
      ]);
      if (failed.length) setDetailError(`โหลด${failed.join(" และ ")}ไม่สำเร็จ — ปิดแล้วเปิดใหม่เพื่อลองอีกครั้ง`);
      setActivities(Array.isArray(actRes) ? actRes.map((a: any) => ({ activityId: a.activityId ?? a.defectActivityId ?? "", actionType: a.actionType ?? a.activityType ?? "", message: a.message ?? a.description ?? "", actorUserId: a.actorUserId ?? a.performedByUserId ?? null, actorName: a.actorName ?? null, createdAt: a.createdAt ?? a.performedAt ?? "", performedAt: a.performedAt ?? a.createdAt ?? "" })) : []);
      setLinkedCases(Array.isArray(tcRes) ? tcRes : []);
    } catch {} finally { setDetailLoading(false); }
  };
  const postComment = async () => {
    // ตอนนี้ endpoint นี้ยังรอ sync ไป CRM ด้วย (best-effort, ดู CrmSendToCrmService.AppendCommentAsync) ถ้า Defect
    // ผูก CRM แล้ว เลยอาจใช้เวลานานกว่าคอมเมนต์ปกติเล็กน้อย — ต้องกันกดซ้ำ + โชว์สถานะกำลังส่งให้ชัดเจน
    if (!detail || !commentText.trim() || commentSending) return;
    setCommentSending(true);
    try {
      const response = await fetch(`${apiUrl}/defects/${detail.defectId}/comments`, { method: "POST", headers, body: JSON.stringify({ body: commentText.trim() }) });
      if (response.ok) { setCommentText(""); await openDetail(detail); }
    } finally { setCommentSending(false); }
  };
  const bulkStatus = async (status: string) => {
    if (canEdit === false || !selectedIds.length) return;
    if (!await confirmDialog({ title: "เปลี่ยนสถานะหลายรายการ", message: `เปลี่ยนสถานะ Defect ${selectedIds.length} รายการเป็น ${status} ใช่หรือไม่?`, confirmLabel: `เปลี่ยนเป็น ${status}` })) return;
    const response = await fetch(`${apiUrl}/defects/bulk`, { method: "POST", headers, body: JSON.stringify({ ids: selectedIds, status }) });
    if (!response.ok) notify(`เปลี่ยนสถานะ ${selectedIds.length} รายการไม่สำเร็จ`, "error");
    if (response.ok) { setNotice(`เปลี่ยนสถานะ ${selectedIds.length} รายการ`); setSelectedIds([]); setReload(x => x + 1); }
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
  const visibleModuleRanking = showAllModules ? summaryStats.modules : summaryStats.modules.slice(0, 8);
  const defectContextLabel = [projectName, releaseLabel, buildLabel].filter(Boolean).join(" · ") || "ยังไม่ได้เลือก Project / Release / Build";
  if (loading && !items.length) return <article className="card empty"><div className="spinner" /><p>กำลังโหลด Defect...</p></article>;
  return <div className="defect-page defect-page-modern">
    {error && <div className="inline-alert error"><span>{error}</span><button type="button" aria-label="ปิดข้อความ" onClick={() => setError("")}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>}
    {notice && <div className="inline-alert success"><span>{notice}</span><button type="button" aria-label="ปิดข้อความ" onClick={() => setNotice("")}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>}
    <section className="defect-page-intro" aria-labelledby="defect-workspace-title">
      <div className="defect-page-intro-copy">
        <span className="defect-page-eyebrow">DEFECT WORKSPACE</span>
        <h2 id="defect-workspace-title">ภาพรวมและรายการ Defect</h2>
        <p>เริ่มจากดูสถานะรวม แล้วเลือกโมดูลเพื่อเปิดรายการที่ต้องติดตามได้ทันที</p>
      </div>
      <div className="defect-page-context" aria-label="ขอบเขตข้อมูลที่กำลังดู">
        <span className="material-symbols-outlined" aria-hidden="true">filter_alt</span>
        <span>{defectContextLabel}</span>
      </div>
    </section>
    <div className="kpi-grid defect-summary-grid">
      <article className="card kpi defect-kpi defect-kpi-total"><span>Total</span><strong>{summaryStats.total}</strong><small>Defects ทั้งหมด</small></article>
      <article className="card kpi defect-kpi"><span>Open</span><strong>{summaryStats.open}</strong><small className="yellow">ต้องแก้ไข</small><small className="defect-kpi-pct">{defectPct(summaryStats.open, summaryStats.total)}% ของทั้งหมด</small></article>
      <article className="card kpi defect-kpi"><span>In Progress</span><strong>{summaryStats.inProgress}</strong><small className="blue">กำลังแก้ไข</small><small className="defect-kpi-pct">{defectPct(summaryStats.inProgress, summaryStats.total)}% ของทั้งหมด</small></article>
      <article className="card kpi defect-kpi"><span>Resolved</span><strong>{summaryStats.resolved}</strong><small className="green">แก้ไขแล้ว</small><small className="defect-kpi-pct">{defectPct(summaryStats.resolved, summaryStats.total)}% ของทั้งหมด</small></article>
      <article className="card kpi defect-kpi"><span>Closed</span><strong>{summaryStats.closed}</strong><small className="green">ปิดงานแล้ว</small><small className="defect-kpi-pct">{defectPct(summaryStats.closed, summaryStats.total)}% ของทั้งหมด</small></article>
      <article className="card kpi defect-kpi"><span>Rejected</span><strong>{summaryStats.rejected}</strong><small className="gray">ปฏิเสธ</small><small className="defect-kpi-pct">{defectPct(summaryStats.rejected, summaryStats.total)}% ของทั้งหมด</small></article>
    </div>
    <div className="defect-summary-insights">
      <article className="card chart-card defect-summary-severity">
        <div className="chart-card-head">
          <h3>สัดส่วนตามความรุนแรง (Severity)</h3>
          <span>ทั้งหมด {summaryStats.total.toLocaleString()} รายการ</span>
        </div>
        <div className="chart-bars">
          {([["Critical", summaryStats.critical, "#dc2626"], ["High", summaryStats.high, "#f59e0b"], ["Medium", summaryStats.medium, "#2563eb"], ["Low", summaryStats.low, "#94a3b8"]] as [string, number, string][]).map(([label, count, color]) => <div key={label} className="bar-row">
            <div className="bar-label">{label}</div>
            <div className="bar-track"><div className="bar-fill" style={{ width: `${Math.max(defectPct(count, summaryStats.total), count > 0 ? 8 : 0)}%`, background: `linear-gradient(90deg, ${color}cc, ${color})` }} /></div>
            <strong className="severity-count">{count.toLocaleString()}</strong>
            <span className="severity-pct">{defectPct(count, summaryStats.total)}%</span>
          </div>)}
        </div>
        {summaryStats.total === 0 && <p className="chart-empty">ยังไม่มีข้อมูล Defect</p>}
        <div className="defect-closure-note"><span>Defect Closure Rate</span><b>{summaryStats.closureRate}%</b><small>{(summaryStats.resolved + summaryStats.closed).toLocaleString()} Resolved / Closed จากทั้งหมด {summaryStats.total.toLocaleString()} รายการ</small></div>
      </article>
      <div className="defect-detail-stats defect-summary-facts">
        <div className="defect-detail-stat"><span className="defect-detail-stat-icon purple" aria-hidden="true">◔</span><div><small>ค้างนานที่สุด</small><b>{summaryStats.oldestOpenAgeDays.toLocaleString()} วัน</b><small className="defect-detail-stat-sub">อายุของ Open/In Progress ที่นานที่สุด</small></div></div>
        <div className="defect-detail-stat"><span className="defect-detail-stat-icon gray" aria-hidden="true">U</span><div><small>ยังไม่มอบหมาย</small><b>{summaryStats.unassignedActive.toLocaleString()} รายการ</b><small className="defect-detail-stat-sub">Open/In Progress ที่ยังไม่มีผู้รับผิดชอบ</small></div></div>
      </div>
    </div>
    <section className="card defect-module-ranking" aria-labelledby="defect-module-ranking-title">
      <div className="defect-module-ranking-head">
        <div>
          <h3 id="defect-module-ranking-title">Defect ตามโมดูล</h3>
          <p>เรียงตามจำนวนที่พบมากที่สุด · รวมทุกสถานะใน Project / Release / Build ที่เลือก</p>
        </div>
        <div className="defect-module-ranking-actions">
          <span>{summaryStats.modules.length.toLocaleString()} กลุ่ม</span>
          {canExport !== false && <button type="button" className="btn" disabled={!statsLoaded || exportingModulePdf} onClick={downloadModulePdf}>{exportingModulePdf ? <><span className="spinner inline" aria-hidden="true" /> กำลังสร้าง PDF...</> : <><span className="material-symbols-outlined" aria-hidden="true">picture_as_pdf</span> ส่งออก PDF A4</>}</button>}
        </div>
      </div>
      {statsError ? <div className="inline-alert error" role="alert"><span>{statsError}</span><button type="button" onClick={() => setStatsReload(value => value + 1)}><span className="material-symbols-outlined" aria-hidden="true">refresh</span> ลองใหม่</button></div> : summaryStats.modules.length > 0 ? <>
        <ol className="defect-module-ranking-list">
        {visibleModuleRanking.map((module, index) => <li key={module.moduleId ?? "unassigned"}>
          <span className="defect-module-rank">{index + 1}</span>
          <button type="button" className={`defect-module-ranking-body${moduleFilter === (module.moduleId ?? "unassigned") ? " is-selected" : ""}`} aria-pressed={moduleFilter === (module.moduleId ?? "unassigned")} aria-label={`ดู Defect ของ ${module.moduleCode ? `${module.moduleCode} ` : ""}${module.moduleName} ${module.count} รายการ`} onClick={() => openModuleDefects(module.moduleId)}>
            <span className="defect-module-ranking-label">
              <span>{module.moduleCode && <b>{module.moduleCode}</b>}{module.moduleName}</span>
              <strong>{module.count.toLocaleString()} <small>Defect</small></strong>
            </span>
            <span className="defect-module-ranking-track" aria-hidden="true"><span style={{ width: `${(module.count / Math.max(1, summaryStats.modules[0].count)) * 100}%` }} /></span>
          </button>
        </li>)}
        </ol>
        {summaryStats.modules.length > 8 && <button type="button" className="defect-ranking-toggle" onClick={() => setShowAllModules(value => !value)}>
          <span className="material-symbols-outlined" aria-hidden="true">{showAllModules ? "expand_less" : "expand_more"}</span>
          {showAllModules ? "แสดงเฉพาะอันดับต้น ๆ" : `แสดงทั้งหมด ${summaryStats.modules.length} กลุ่ม`}
        </button>}
      </> : <p className="chart-empty">ยังไม่มี Defect ในขอบเขตที่เลือก</p>}
    </section>
    <article className="card defect-list-card" id="defect-list-results" tabIndex={-1} aria-label="รายการ Defect">
      <div className="defect-list-head">
        <div>
          <span className="defect-page-eyebrow">DEFECT QUEUE</span>
          <h3>รายการ Defect</h3>
          <p>ตรวจสอบรายละเอียด เปลี่ยนสถานะ และส่งต่อให้ทีมที่เกี่ยวข้องจากรายการเดียว</p>
        </div>
        <div className="defect-list-head-meta">
          <strong>{totalCount.toLocaleString()}</strong>
          <span>รายการในผลลัพธ์</span>
        </div>
      </div>
      <div className="defect-filter-bar">
        <div className="defect-filter-bar-title">
          <span className="material-symbols-outlined" aria-hidden="true">tune</span>
          <div><b>ตัวกรองรายการ</b><small>{activeDefectFilterCount ? `ใช้ตัวกรองอยู่ ${activeDefectFilterCount} รายการ` : "เลือกตัวกรองเพื่อโฟกัสงานที่ต้องติดตาม"}</small></div>
        </div>
        {activeDefectFilterCount > 0 && <button type="button" className="defect-clear-filters" onClick={clearDefectFilters}><span className="material-symbols-outlined" aria-hidden="true">restart_alt</span> ล้างตัวกรอง</button>}
      </div>
      <div className="table-tools defect-table-tools">
        <div>
          <select aria-label="กรองตาม Module" value={moduleFilter} onChange={e => setModuleFilter(e.target.value)}><option value="">ทุก Module</option>{renderModuleSelectOptions(modules)}{summaryStats.modules.some(x => x.moduleId === null) && <option value="unassigned">ไม่ระบุโมดูล</option>}{moduleFilter && moduleFilter !== "unassigned" && !modules.some(x => x.moduleId === moduleFilter) && <option value={moduleFilter}>{summaryStats.modules.find(x => x.moduleId === moduleFilter)?.moduleName ?? "โมดูลที่ถูกลบ"}</option>}</select>
          <select value={severityFilter} onChange={e => setSeverityFilter(e.target.value)}><option value="">ทุก Severity</option>{defectSeverities.map(s => <option key={s}>{s}</option>)}</select>
          <select aria-label="กรองตาม Priority" value={priorityFilter} onChange={e => setPriorityFilter(e.target.value)}><option value="">ทุก Priority</option>{["P0", "P1", "P2", "P3"].map(priority => <option key={priority}>{priority}</option>)}</select>
          <select value={statusFilter} onChange={e => setStatusFilter(e.target.value)}><option value="">ทุก Status</option>{defectStatuses.map(s => <option key={s}>{s}</option>)}</select>
          <select value={assigneeFilter} onChange={e => setAssigneeFilter(e.target.value)}><option value="">ทุก Assignee</option>{users.map(u => <option key={u.userId} value={u.userId}>{u.displayName}</option>)}</select>
        </div>
        <div>
          {canEdit !== false && selectedIds.length > 0 && <>
            <button className="btn" onClick={() => bulkStatus("Resolved")}><span className="material-symbols-outlined" aria-hidden="true">check</span> Resolve ({selectedIds.length})</button>
            <button className="btn" onClick={() => bulkStatus("Closed")}><span aria-hidden="true">⏹</span> Close ({selectedIds.length})</button>
          </>}
          <button className="btn" onClick={exportCsv}><span className="material-symbols-outlined" aria-hidden="true">download</span> Export</button>
          {canEdit !== false && <button className="btn primary" disabled={!projectId} onClick={() => openForm()}>+ Defect</button>}
        </div>
      </div>
      <div className="table-wrap">
        <table className="defect-list-table table-cards">
          <thead><tr>
            <th><input type="checkbox" checked={selectedIds.length === items.length && items.length > 0} onChange={toggleSelectAll} /></th>
            <th>Defect ID</th><th>Title</th><th>Severity</th><th>Status</th><th>CRM</th><th>Module</th><th>Age</th><th>Created</th><th className="actions-col">จัดการ</th>
          </tr></thead>
          <tbody>
            {items.map(x => <tr key={x.defectId}>
              <td><input type="checkbox" checked={selectedIds.includes(x.defectId)} onChange={() => setSelectedIds(prev => prev.includes(x.defectId) ? prev.filter(id => id !== x.defectId) : [...prev, x.defectId])} /></td>
              <td><button className="link-button" onClick={() => openDetail(x)}>{x.defectCode}</button></td>
              <td><span className="defect-title-text" title={x.title}>{x.title}</span>{(x.releaseCode || x.buildNumber) && <small className="cell-sub">{x.releaseCode || "Release ไม่ระบุ"}{x.buildNumber ? ` · Build ${x.buildNumber}` : ""}</small>}</td>
              <td><Badge tone={defectSeverityTones[x.severity] ?? "blue"}>{x.severity}</Badge></td>
              <td><Badge tone={defectStatusTones[x.status] ?? "gray"}>{x.status}</Badge></td>
              <td>
                {canEdit !== false && (x.crmSyncStatus ?? "None") === "None"
                  ? <button className="crm-badge-btn" title={`ส่ง ${x.defectCode} ไป CRM`} aria-label={`ส่ง ${x.defectCode} ไป CRM`} onClick={() => openCrmDialog("send", x)}><Badge tone="gray"><span aria-hidden="true">⇪</span> ส่งไป CRM</Badge></button>
                  : <Badge tone={defectCrmSyncTones[x.crmSyncStatus ?? "None"] ?? "gray"}>{defectCrmSyncLabels[x.crmSyncStatus ?? "None"] ?? "ยังไม่ส่ง"}</Badge>}
              </td>
              <td>{modules.find(m => m.moduleId === x.moduleId)?.moduleName ?? "-"}</td>
              <td>{defectAgeDays(x.createdAt)} วัน</td>
              <td>{fmtAgo(x.createdAt)}</td>
              <td className="actions-col"><div className="row-actions">
                <button className="table-action icon-only" title="ดูรายละเอียด" aria-label={`ดูรายละเอียด ${x.defectCode}`} onClick={() => openDetail(x)}><span className="material-symbols-outlined" aria-hidden="true">info</span></button>
                {canEdit !== false && <>
                  <button className="table-action icon-only" title="แก้ไข" aria-label={`แก้ไข ${x.defectCode}`} onClick={() => openForm(x)}><span className="material-symbols-outlined" aria-hidden="true">edit</span></button>
                  {x.status === "Open" && <button className="table-action icon-only" title="เริ่มดำเนินการ" aria-label={`เริ่มดำเนินการ ${x.defectCode}`} onClick={() => quickStatus(x, "In Progress")}><span className="material-symbols-outlined" aria-hidden="true">play_arrow</span></button>}
                  {x.status === "In Progress" && <button className="table-action icon-only" title="Resolve" aria-label={`Resolve ${x.defectCode}`} onClick={() => quickStatus(x, "Resolved")}><span className="material-symbols-outlined" aria-hidden="true">check</span></button>}
                  <button className="table-action danger-action icon-only" title="ลบ" aria-label={`ลบ ${x.defectCode}`} onClick={() => removeDefect(x)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
                </>}
              </div></td>
            </tr>)}
            {!loading && !items.length && <tr><td colSpan={10} className="muted-row">ยังไม่มี Defect ในขอบเขตที่เลือก</td></tr>}
          </tbody>
          </table>
        </div>
        <div className="pagination suite-pagination">
          <label>แสดง<select value={pageSize} onChange={event => { setPageSize(Number(event.target.value)); setPage(1); }}><option value="30">30</option><option value="50">50</option><option value="100">100</option><option value="150">150</option></select> รายการ</label>
          <span>หน้า {Math.min(page, pageCount)} / {pageCount} · ทั้งหมด {totalCount.toLocaleString()} รายการ</span>
          <button className="btn" disabled={loading || page <= 1} onClick={() => setPage(value => value - 1)}><span className="material-symbols-outlined" aria-hidden="true">chevron_left</span> ก่อนหน้า</button>
          <button className="btn" disabled={loading || page >= pageCount} onClick={() => setPage(value => value + 1)}>ถัดไป <span className="material-symbols-outlined" aria-hidden="true">chevron_right</span></button>
        </div>
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
          <button className="btn primary" disabled={saving || !formTitle.trim()} onClick={saveForm}>{saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">check</span> บันทึก</>}</button>
        </div>
      </ModalShell>}
    {detail && (() => {
      const steps = detail.stepsToReproduce ? parseReproSteps(detail.stepsToReproduce) : null;
      const moduleName = modules.find(m => m.moduleId === detail.moduleId)?.moduleName ?? "-";
      return (
        <ModalShell labelledBy="defect-detail-title" className="cycle-modal cycle-detail-modal defect-detail" onDismiss={() => setDetail(null)}>
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
              <button aria-label="ปิดรายละเอียด Defect" onClick={() => setDetail(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
            </div>
            <div className="defect-detail-stats">
              <div className="defect-detail-stat"><span className="defect-detail-stat-icon orange" aria-hidden="true">⚠</span><div><small>Severity</small><Badge tone={defectSeverityTones[detail.severity] ?? "blue"}>{detail.severity}</Badge></div></div>
              <div className="defect-detail-stat"><span className="defect-detail-stat-icon green" aria-hidden="true">✓</span><div><small>Status</small><Badge tone={defectStatusTones[detail.status] ?? "gray"}>{detail.status}</Badge></div></div>
              <div className="defect-detail-stat"><span className="defect-detail-stat-icon blue" aria-hidden="true">▢</span><div><small>Module</small><b>{moduleName}</b></div></div>
              <div className="defect-detail-stat"><span className="defect-detail-stat-icon purple" aria-hidden="true">◔</span><div><small>Age</small><b>{defectAgeDays(detail.createdAt)} วัน</b></div></div>
              <div className="defect-detail-stat"><span className="defect-detail-stat-icon blue" aria-hidden="true">▤</span><div><small>Created</small><b>{fmtAgo(detail.createdAt)}</b><small className="defect-detail-stat-sub">{formatThaiDateTime(detail.createdAt, { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" })}</small></div></div>
              <div className="defect-detail-stat"><span className="defect-detail-stat-icon gray" aria-hidden="true">U</span><div><small>Assignee</small><b>{detail.assigneeName ?? "ไม่ระบุ"}</b></div></div>
              {detail.crmSyncStatus === "Linked" && detail.crmTicketId && <div className="defect-detail-stat"><span className="defect-detail-stat-icon green" aria-hidden="true">⇪</span><div><small>CRM Ticket</small><a href={`https://bluesea.seniorsoft.com/bluesea/BookLicence/MA/Support/JobDetailsHD?JobNo=${encodeURIComponent(detail.crmTicketId)}&JobType=HD`} target="_blank" rel="noreferrer" title={`เปิด Ticket #${detail.crmTicketId} ใน CRM`}>#{detail.crmTicketId}</a></div></div>}
              {detail.crmSyncStatus === "Failed" && <div className="defect-detail-stat"><span className="defect-detail-stat-icon red" aria-hidden="true">⚠</span><div><small>CRM</small><Badge tone="red">ส่งไม่สำเร็จ</Badge></div></div>}
            </div>
            <div className="defect-detail-split">
              <section className="cycle-detail-section">
                <h3><span className="material-symbols-outlined" aria-hidden="true">description</span> Description</h3>
                <div className="defect-detail-text defect-description-text">
                  {(detail.description || "ไม่มีคำอธิบาย").split(/\r?\n/).map((line, index) => {
                    const labeledLine = line.match(/^(.{2,40}):\s*(.*)$/);
                    return <span key={`${index}-${line}`}>{labeledLine ? <><b>{labeledLine[1]}:</b>{labeledLine[2] && <span className="defect-description-value"> {labeledLine[2]}</span>}</> : (line || "\u00a0")}</span>;
                  })}
                </div>
              </section>
              <section className="cycle-detail-section">
                <h3><span className="material-symbols-outlined" aria-hidden="true">description</span> Steps to Reproduce</h3>
                {detail.stepsToReproduce ? (steps ? (
                  <div className="defect-repro-steps">
                    {steps.map(s => (
                      <div key={s.stepNo} className={"defect-repro-step" + (s.status === "Fail" ? " is-fail" : "")}>
                        <span className="defect-repro-step-no">{s.stepNo}</span>
                        <div className="defect-repro-step-body"><b>{s.action}</b>{s.detail && <span className="defect-repro-step-detail"> {s.detail}</span>}</div>
                        {s.status && <Badge tone={s.status === "Pass" ? "green" : "red"}>{s.status}</Badge>}
                      </div>
                    ))}
                  </div>
                ) : <p className="defect-detail-text">{detail.stepsToReproduce}</p>) : <p className="defect-detail-text muted-text">ไม่มีขั้นตอนการทำซ้ำ</p>}
              </section>
            </div>
            {(detail.expectedResult || detail.actualResult) && (
              <div className="defect-detail-split">
                {detail.expectedResult && <section className="cycle-detail-section"><h3>Expected Result</h3><p className="defect-detail-text">{detail.expectedResult}</p></section>}
                {detail.actualResult && <section className="cycle-detail-section"><h3>Actual Result</h3><p className="defect-detail-text">{detail.actualResult}</p></section>}
              </div>
            )}
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
            {canEdit !== false && (
              <section className="cycle-detail-section">
                <h3>Quick Actions</h3>
                <div className="defect-quick-actions">
                  {detail.status !== "In Progress" && detail.status !== "Closed" && (
                    <span className="quick-action-item">
                      <button className="btn quick-action-blue" onClick={() => { quickStatus(detail, "In Progress"); setDetail(null); }}><span className="material-symbols-outlined" aria-hidden="true">arrow_forward</span> In Progress</button>
                      <span className="quick-action-info" tabIndex={0} title="เปลี่ยนสถานะ Defect นี้เป็น In Progress" aria-label="เปลี่ยนสถานะ Defect นี้เป็น In Progress">ⓘ</span>
                    </span>
                  )}
                  {detail.status !== "Resolved" && detail.status !== "Closed" && (
                    <span className="quick-action-item">
                      <button className="btn quick-action-green" onClick={() => { quickStatus(detail, "Resolved"); setDetail(null); }}><span className="material-symbols-outlined" aria-hidden="true">check</span> Resolve</button>
                      <span className="quick-action-info" tabIndex={0} title="เปลี่ยนสถานะ Defect นี้เป็น Resolved" aria-label="เปลี่ยนสถานะ Defect นี้เป็น Resolved">ⓘ</span>
                    </span>
                  )}
                  {detail.status !== "Closed" && (
                    <span className="quick-action-item">
                      <button className="btn quick-action-purple" onClick={() => { quickStatus(detail, "Closed"); setDetail(null); }}><span aria-hidden="true">⏹</span> Closed</button>
                      <span className="quick-action-info" tabIndex={0} title="เปลี่ยนสถานะ Defect นี้เป็น Closed" aria-label="เปลี่ยนสถานะ Defect นี้เป็น Closed">ⓘ</span>
                    </span>
                  )}
                  <span className="quick-action-item">
                    <button className="btn" onClick={() => { openForm(detail); setDetail(null); }}><span className="material-symbols-outlined" aria-hidden="true">edit</span> แก้ไข</button>
                    <span className="quick-action-info" tabIndex={0} title="เปิดฟอร์มแก้ไขรายละเอียด Defect นี้ (หัวข้อ, Severity, คำอธิบาย, ผู้รับผิดชอบ ฯลฯ)" aria-label="เปิดฟอร์มแก้ไขรายละเอียด Defect นี้">ⓘ</span>
                  </span>
                  <span className="quick-action-item">
                    <button className="btn quick-action-red" onClick={() => { const item = detail; setDetail(null); removeDefect(item); }}><span aria-hidden="true">🗑</span> ลบ</button>
                    <span className="quick-action-info" tabIndex={0} title="ลบ Defect นี้ออกจากระบบ (soft delete — ยังกู้คืนได้จากฐานข้อมูล ไม่แสดงในรายการอีก)" aria-label="ลบ Defect นี้ออกจากระบบ">ⓘ</span>
                  </span>
                  {detail.crmSyncStatus !== "Linked" && (
                    <span className="quick-action-item">
                      <button className="btn quick-action-blue" onClick={() => openCrmDialog("send", detail)}><span aria-hidden="true">⇪</span> ส่งไป CRM</button>
                      <span className="quick-action-info" tabIndex={0} title="สร้าง Ticket ใหม่ใน CRM (BlueSea Helpdesk) จากข้อมูล Defect นี้ ให้เลือกผู้รับผิดชอบฝั่ง Dev แล้วผูก Ticket ID ไว้กับ Defect นี้" aria-label="สร้าง Ticket ใหม่ใน CRM จากข้อมูล Defect นี้">ⓘ</span>
                    </span>
                  )}
                  {detail.crmSyncStatus === "Linked" && (
                    <span className="quick-action-item">
                      <button className="btn quick-action-blue" onClick={() => openCrmDialog("reassign", detail)}><span aria-hidden="true">↺</span> เปลี่ยนผู้รับผิดชอบ CRM</button>
                      <span className="quick-action-info" tabIndex={0} title={`เปลี่ยนผู้รับผิดชอบ (Assignto) บน CRM Ticket #${detail.crmTicketId} เดิมที่ผูกไว้แล้ว ไม่สร้าง Ticket ใหม่`} aria-label="เปลี่ยนผู้รับผิดชอบบน CRM Ticket เดิม ไม่สร้าง Ticket ใหม่">ⓘ</span>
                    </span>
                  )}
                </div>
              </section>
            )}
            <section className="cycle-detail-section">
              {detailError && <div className="inline-alert error" role="alert"><span>{detailError}</span></div>}
              <h3>Activities ({activities.length})</h3>
              <div className="defect-activity-list">
                {activities.length ? activities.map(a => (
                  <div key={a.activityId} className="defect-activity-row">
                    <Badge tone="blue">{defectActionLabels[a.actionType] ?? a.actionType}</Badge>
                    <div><p>{a.message ?? a.actionType}</p><small>{a.actorName ?? "System"} · {fmtAgo(a.performedAt ?? a.createdAt)}</small></div>
                  </div>
                )) : <p className="muted-text">ยังไม่มี Activity</p>}
              </div>
              {canEdit !== false && (
                <div className="defect-comment-box">
                  <input value={commentText} onChange={e => setCommentText(e.target.value)} placeholder="เพิ่มคอมเมนต์..." disabled={commentSending} onKeyDown={e => { if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); postComment(); } }} />
                  <button className="btn primary" onClick={postComment} disabled={!commentText.trim() || commentSending}>
                    {commentSending ? <><span className="spinner inline" aria-hidden="true" /> กำลังส่ง...</> : <><span aria-hidden="true">➤</span> ส่ง</>}
                  </button>
                </div>
              )}
            </section>
            <div className="modal-actions"><button className="btn primary" onClick={() => setDetail(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ปิด</button></div>
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
