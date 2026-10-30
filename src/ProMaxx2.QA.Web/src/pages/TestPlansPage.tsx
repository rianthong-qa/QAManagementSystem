import { useEffect, useId, useMemo, useRef, useState } from "react";
import type { BuildItem, ReleaseItem, UserLookup } from "../shared/types";
import { apiFetch, apiUrl, getJson, isAbortError } from "../api";
import { useDebounced } from "../components/useDebounced";
import { confirmDialog, notify, promptDialog } from "../components/dialogStore";
import { Badge } from "../components/Badge";
import { ModalShell } from "../components/ModalShell";
import { DateField } from "../components/DateField";
import { ListPager } from "../components/ListPager";
import { DeletedItemsButton } from "../components/DeletedItemsModal";
import { useUserPageSize } from "../components/usePageSize";
import { formatThaiDateTime } from "../dateTime";
import "../components/ListPage.css";
import "./TestCycleDetail.css";
import "./TestPlansPage.css";

// Test Plan / Configuration Matrix (TEST_PLAN_CONFIGURATION_MATRIX_PLAN.md เฟส 3) — รายการแบบ ListPage กลาง +
// หน้าต่างรายละเอียดใช้ workspace เดียวกับรายละเอียด Test Cycle (cyd-) และแถว Matrix (tp-)

type PlanSummary = { runCount: number; totalCases: number; untested: number; inProgress: number; touched: number; completed: number; passed: number; failed: number; blocked: number; skipped: number; retest: number; retestAttempts: number; progressPercent: number; completionPercent: number; passRate: number | null };
type PlanListItem = { testPlanId: string; projectId: string; releaseId: string; releaseCode?: string | null; planCode: string; planName: string; planType: string; startDate?: string | null; endDate?: string | null; ownerUserId?: string | null; ownerName?: string | null; status: string; lifecycleStatus: string; isFrozen: boolean; runCount: number; totalCases: number; touched: number; completed: number; passed: number; progressPercent: number; passRate: number | null; createdAt: string };
type PlanListResult = { items: { total: number; rows: PlanListItem[] }; summary: PlanSummary };
type PlanRun = { testPlanRunId: string; testPlanId: string; testCycleId: string; cycleCode: string; cycleName: string; status: string; runCode: string; runName: string; configurationKey?: string | null; configurationName?: string | null; configurationJson?: string | null; ownerUserId?: string | null; ownerName?: string | null; buildId: string; buildNumber?: string | null; environmentId: string; environmentName?: string | null; totalCases: number; untested: number; inProgress: number; passed: number; failed: number; blocked: number; skipped: number; progressPercent: number; passRate: number | null; createdAt: string; unassignedCases: number; assignments: RunMember[] };
type RunMember = { testerUserId: string; testerName?: string | null; role: string; assignedAt: string };
type PlanMember = { userId: string; displayName: string };
type PlanDetail = Omit<PlanListItem, "runCount" | "totalCases" | "touched" | "completed" | "passed" | "progressPercent" | "passRate"> & { frozenAt?: string | null; copiedFromTestPlanId?: string | null; notes?: string | null; updatedAt?: string | null; summary: PlanSummary; runs: PlanRun[] };
/** Test Cycle ที่เพิ่มเข้าแผนได้ — จาก GET /test-cycles (มี Suite/Environment/Module/จำนวนเคส ให้แยกแยะ Cycle ชื่อคล้ายกันได้) */
type CycleRow = { testCycleId: string; cycleCode: string; cycleName: string; status: string; buildId: string; buildNumber: string; environmentId: string; environmentName: string; suiteName?: string | null; caseCount: number; executedCount: number; progressPercent: number; modules: { moduleId: string; moduleCode: string; moduleName: string }[] };
type CycleListResult = { items: { total: number; rows: CycleRow[] } };
/** ค่าที่มีจริงในรายการ + จำนวน เรียงตามชื่อ — ใช้สร้างตัวกรอง (มีค่าเดียวไม่ต้องแสดงตัวกรอง) */
const distinctCounts = (pairs: [string, string][]) => {
  const map = new Map<string, { label: string; count: number }>();
  for (const [id, label] of pairs) { const item = map.get(id); if (item) item.count++; else map.set(id, { label, count: 1 }); }
  return [...map.entries()].map(([id, x]) => ({ id, ...x })).sort((a, b) => a.label.localeCompare(b.label, "th", { numeric: true }));
};
type SuiteOption = { testSuiteId: string; suiteCode: string; suiteName: string; caseCount: number };
type EnvironmentOption = { testEnvironmentId: string; environmentName: string; isActive: boolean };

const planTypes = ["Smoke", "Regression", "UAT", "Retest", "Other"];
const planStatuses = ["Draft", "InProgress", "Completed", "Closed", "Cancelled"] as const;
const planStatusInfo: Record<string, { label: string; tone: string; dot: string; meaning: string }> = {
  Draft: { label: "ฉบับร่าง", tone: "gray", dot: "gray", meaning: "ยังไม่มี Run หรือทุก Run ยังไม่เริ่มทดสอบ" },
  InProgress: { label: "กำลังทดสอบ", tone: "blue", dot: "blue", meaning: "มี Run ที่กำลังทดสอบหรือมีเคสที่เริ่มบันทึกผลแล้ว" },
  Completed: { label: "ทดสอบครบ", tone: "green", dot: "green", meaning: "ทุก Run อยู่ในสถานะ Completed / Closed / Cancelled แล้ว — ปิดแผนได้" },
  Closed: { label: "ปิดแล้ว", tone: "green", dot: "green", meaning: "ปิดแผนแล้ว ระบบ Freeze อัตโนมัติ ใช้ Reopen เพื่อแก้ไขต่อ" },
  Cancelled: { label: "ยกเลิก", tone: "red", dot: "red", meaning: "ยกเลิกแผน เก็บ Run และผลการทดสอบไว้ ใช้ Reopen เพื่อนำกลับมา" },
};
const runRoles = ["Tester", "Reviewer", "Observer"] as const;
const runRoleLabel: Record<string, string> = { Tester: "ผู้ทดสอบ", Reviewer: "ผู้ตรวจ", Observer: "ผู้ติดตาม" };
const runStatusInfo: Record<string, { label: string; tone: string }> = {
  Draft: { label: "ฉบับร่าง", tone: "gray" }, InProgress: { label: "กำลังทดสอบ", tone: "blue" }, Completed: { label: "ทดสอบเสร็จ", tone: "green" }, Closed: { label: "ปิดแล้ว", tone: "green" }, Cancelled: { label: "ยกเลิก", tone: "red" },
};
const runStatusLabel = (status: string) => runStatusInfo[status]?.label ?? status;
const caseStatusSegments = [
  { key: "passed", label: "ผ่าน", className: "is-pass" },
  { key: "failed", label: "ไม่ผ่าน", className: "is-fail" },
  { key: "blocked", label: "ติดปัญหา", className: "is-blocked" },
  { key: "skipped", label: "ข้าม", className: "is-skipped" },
  { key: "inProgress", label: "กำลังทำ", className: "is-inprogress" },
  { key: "untested", label: "ยังไม่เริ่ม", className: "is-notrun" },
] as const;

const percentText = (value: number | null | undefined) => value === null || value === undefined ? "—" : `${Number.isInteger(value) ? value : value.toFixed(1)}%`;
const shortDate = (value?: string | null) => value ? formatThaiDateTime(value, { day: "numeric", month: "short", year: "2-digit" }) : "";
const dateOnly = (value?: string | null) => value ? value.slice(0, 10) : "";
/** ช่วงวันที่บรรทัดเดียว — ปีเดียวกันแสดงปีครั้งเดียวท้ายช่วง เช่น "29 ต.ค. – 5 พ.ย. 69" */
const scheduleText = (start?: string | null, end?: string | null) => {
  if (!start && !end) return "ไม่ระบุวันที่";
  const sameYear = !!start && !!end && start.slice(0, 4) === end.slice(0, 4);
  const from = start ? sameYear ? formatThaiDateTime(start, { day: "numeric", month: "short" }) : shortDate(start) : "?";
  return `${from} – ${shortDate(end) || "?"}`;
};
/** วันคงเหลือของแผนที่ยังทดสอบอยู่ (ฉบับร่าง/กำลังทดสอบ) — วันที่เก็บแบบ date-only จึงเทียบกับเที่ยงคืนของวันนี้ แบบเดียวกับรายละเอียด Test Cycle */
const daysLeftOf = (status: string, end?: string | null) => {
  if (!end || (status !== "Draft" && status !== "InProgress")) return null;
  const today = new Date(); today.setHours(0, 0, 0, 0);
  return Math.round((new Date(`${end.slice(0, 10)}T00:00:00`).getTime() - today.getTime()) / 86_400_000);
};
const DueHint = ({ days }: { days: number | null }) => days === null ? null
  : <small className={`tp-due${days < 0 ? " is-late" : days <= 2 ? " is-near" : ""}`}>{days < 0 ? `เลยกำหนด ${-days} วัน` : days === 0 ? "ครบกำหนดวันนี้" : `เหลือ ${days} วัน`}</small>;
/** API รุ่นก่อนเฟส 4 ไม่ส่งทีม/จำนวนเคสที่ยังไม่มีผู้รับผิดชอบของ Run — เติมค่าว่างกันหน้าพังระหว่างรอ deploy API */
const normalizePlan = (plan: PlanDetail): PlanDetail => ({ ...plan, runs: plan.runs.map((r) => ({ ...r, assignments: r.assignments ?? [], unassignedCases: r.unassignedCases ?? 0 })) });
const errorText = (e: unknown, fallback: string) => e instanceof Error && e.message ? e.message : fallback;
/** Configuration ที่อ่านได้: OS / Browser / Device จาก ConfigurationJson (คีย์อื่นเก็บไว้เมื่อแก้ไข) */
const parseConfig = (json?: string | null): Record<string, unknown> => { try { const value = json ? JSON.parse(json) : {}; return value && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : {}; } catch { return {}; } };
const configSummary = (run: PlanRun) => { const c = parseConfig(run.configurationJson); return ["os", "browser", "device"].map((k) => c[k]).filter((v) => typeof v === "string" && v).join(" · "); };
/** Export CSV (UTF-8 BOM + CRLF ให้ Excel เปิดภาษาไทยได้) — รูปแบบเดียวกับ Export ของหน้า Test Cycle / RTM */
const downloadCsv = (fileName: string, rows: (string | number | null | undefined)[][]) => {
  const csv = "\ufeff" + rows.map((row) => row.map((value) => `"${String(value ?? "").replaceAll('"', '""')}"`).join(",")).join("\r\n");
  const url = URL.createObjectURL(new Blob([csv], { type: "text/csv;charset=utf-8" }));
  const link = document.createElement("a"); link.href = url; link.download = fileName; link.click();
  setTimeout(() => URL.revokeObjectURL(url), 60000); // revoke ทันทีหลัง click ทำให้บาง browser ยกเลิกการดาวน์โหลด
};
const fileStamp = () => new Date().toISOString().slice(0, 10).replaceAll("-", "");
const csvPercent = (value: number | null | undefined) => value === null || value === undefined ? "" : `${Number.isInteger(value) ? value : value.toFixed(1)}%`;
/** Plan Summary: ส่วนหัว (ข้อมูลแผน + ตัวนับรวม) แล้วตาม Configuration Matrix ราย Run — ตัวเลขชุดเดียวกับหน้าจอ (§3.4) */
const exportPlanSummary = (plan: PlanDetail) => {
  const s = plan.summary;
  const info = planStatusInfo[plan.status];
  const rows: (string | number | null | undefined)[][] = [
    ["Test Plan Summary"],
    ["Plan Code", plan.planCode], ["Plan Name", plan.planName], ["Type", plan.planType], ["Release", plan.releaseCode],
    ["Owner", plan.ownerName], ["Status", info ? `${plan.status} (${info.label})` : plan.status], ["Frozen", plan.isFrozen ? `Yes (${shortDate(plan.frozenAt)})` : "No"],
    ["Start Date", dateOnly(plan.startDate)], ["End Date", dateOnly(plan.endDate)], ["Exported At", formatThaiDateTime(new Date().toISOString())],
    [],
    ["Runs", "Total Cases", "Passed", "Failed", "Blocked", "Skipped", "In Progress", "Not Run", "Progress (Touched)", "Completion", "Pass Rate", "Retest Cases", "Retest Attempts"],
    [s.runCount, s.totalCases, s.passed, s.failed, s.blocked, s.skipped, s.inProgress, s.untested, csvPercent(s.progressPercent), csvPercent(s.completionPercent), csvPercent(s.passRate), s.retest, s.retestAttempts],
    [],
    ["Run Code", "Run Name", "Configuration", "Configuration Key", "OS", "Browser", "Device", "Test Cycle", "Cycle Status", "Build", "Environment", "Owner", "Team", "Unassigned Cases", "Total Cases", "Passed", "Failed", "Blocked", "Skipped", "In Progress", "Not Run", "Progress", "Pass Rate"],
    ...plan.runs.map((r) => {
      const c = parseConfig(r.configurationJson);
      const text = (key: string) => typeof c[key] === "string" ? c[key] as string : "";
      return [r.runCode, r.runName, r.configurationName, r.configurationKey, text("os"), text("browser"), text("device"), r.cycleCode, r.status, r.buildNumber, r.environmentName, r.ownerName,
        r.assignments.map((m) => `${m.testerName ?? "ผู้ใช้"} (${m.role})`).join("; "), r.unassignedCases, r.totalCases, r.passed, r.failed, r.blocked, r.skipped, r.inProgress, r.untested, csvPercent(r.progressPercent), csvPercent(r.passRate)];
    }),
  ];
  downloadCsv(`TestPlan_${plan.planCode}_${fileStamp()}.csv`, rows);
};

export function TestPlansPage({ search, canCreate, canEdit, canDelete, canFreeze, canAssign, canRun, canExport, contextProjectId, contextReleaseId, onOpenExecution, onOpenCycle }: {
  search: string;
  canCreate: boolean;
  canEdit: boolean;
  canDelete: boolean;
  canFreeze: boolean;
  canAssign: boolean;
  canRun: boolean;
  canExport: boolean;
  contextProjectId: string;
  contextReleaseId: string;
  onOpenExecution: (cycleId: string) => void;
  onOpenCycle: (cycleId: string) => void;
}) {
  const [rows, setRows] = useState<PlanListItem[]>([]);
  const [total, setTotal] = useState(0);
  const [summary, setSummary] = useState<PlanSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [reload, setReload] = useState(0);
  const [status, setStatus] = useState("");
  const [planType, setPlanType] = useState("");
  const [owner, setOwner] = useState("");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useUserPageSize("qa.testPlans.pageSize");
  const [users, setUsers] = useState<UserLookup[]>([]);
  // หน้าอื่น (Test Cycle / Execution Workspace) ฝาก TestPlanId ไว้ให้เปิดรายละเอียดแผนทันที — แบบเดียวกับ qa.targetCycleId
  const [detailId, setDetailId] = useState<string | null>(() => { try { const target = localStorage.getItem("qa.targetPlanId"); localStorage.removeItem("qa.targetPlanId"); return target; } catch { return null; } });
  const [exporting, setExporting] = useState(false);
  const [form, setForm] = useState<{ plan?: PlanDetail } | null>(null);
  const [detailVersion, setDetailVersion] = useState(0);
  const debouncedSearch = useDebounced(search.trim(), 300);
  const filterCount = [status, planType, owner].filter(Boolean).length;
  const pageCount = Math.max(1, Math.ceil(total / pageSize));

  useEffect(() => { setPage(1); }, [contextProjectId, contextReleaseId, status, planType, owner, debouncedSearch]);

  useEffect(() => {
    const ctrl = new AbortController();
    getJson<UserLookup[]>(`${apiUrl}/lookups/users`, ctrl.signal).then(setUsers).catch((e) => { if (!isAbortError(e)) setUsers([]); });
    return () => ctrl.abort();
  }, []);

  useEffect(() => {
    const ctrl = new AbortController();
    const params = new URLSearchParams({ page: String(page), size: String(pageSize) });
    if (contextProjectId) params.set("projectId", contextProjectId);
    if (contextReleaseId) params.set("releaseId", contextReleaseId);
    if (status) params.set("status", status);
    if (planType) params.set("planType", planType);
    if (owner) params.set("ownerUserId", owner);
    if (debouncedSearch) params.set("search", debouncedSearch);
    setLoading(true);
    // คงรายการเดิมไว้ระหว่างโหลด และทิ้งผลของคำขอเก่าเมื่อเปลี่ยนตัวกรอง
    getJson<PlanListResult>(`${apiUrl}/test-plans?${params}`, ctrl.signal)
      .then((result) => { setRows(result.items.rows); setTotal(result.items.total); setSummary(result.summary); setError(""); })
      .catch((e) => { if (!isAbortError(e)) setError(errorText(e, "โหลด Test Plan ไม่สำเร็จ")); })
      .finally(() => { if (!ctrl.signal.aborted) setLoading(false); });
    return () => ctrl.abort();
  }, [contextProjectId, contextReleaseId, status, planType, owner, debouncedSearch, page, pageSize, reload]);

  const clearFilters = () => { setStatus(""); setPlanType(""); setOwner(""); };
  // Export รายการแผนตามตัวกรองปัจจุบันทุกหน้า — ขอทุกหน้าพร้อมกัน (หน้าละ 500) ไม่วนทีละหน้า
  const exportList = async () => {
    setExporting(true);
    try {
      const params = new URLSearchParams({ size: "500" });
      if (contextProjectId) params.set("projectId", contextProjectId);
      if (contextReleaseId) params.set("releaseId", contextReleaseId);
      if (status) params.set("status", status);
      if (planType) params.set("planType", planType);
      if (owner) params.set("ownerUserId", owner);
      if (debouncedSearch) params.set("search", debouncedSearch);
      const pages = Math.max(1, Math.ceil(total / 500));
      const results = await Promise.all(Array.from({ length: pages }, (_, i) => { const q = new URLSearchParams(params); q.set("page", String(i + 1)); return getJson<PlanListResult>(`${apiUrl}/test-plans?${q}`); }));
      const plans = results.flatMap((r) => r.items.rows);
      downloadCsv(`TestPlans_${fileStamp()}.csv`, [
        ["Plan Code", "Plan Name", "Type", "Release", "Owner", "Status", "Frozen", "Runs", "Total Cases", "Touched", "Completed", "Passed", "Progress", "Pass Rate", "Start Date", "End Date"],
        ...plans.map((x) => [x.planCode, x.planName, x.planType, x.releaseCode, x.ownerName, planStatusInfo[x.status]?.label ?? x.status, x.isFrozen ? "Yes" : "No", x.runCount, x.totalCases, x.touched, x.completed, x.passed, csvPercent(x.progressPercent), csvPercent(x.passRate), dateOnly(x.startDate), dateOnly(x.endDate)]),
      ]);
      notify(`ส่งออก ${plans.length.toLocaleString()} Test Plan แล้ว`, "success");
    } catch (e) { notify(errorText(e, "ส่งออก Test Plan ไม่สำเร็จ"), "error"); }
    finally { setExporting(false); }
  };
  const createDisabledReason = contextProjectId ? "" : "เลือก Project ที่ด้านบนก่อนสร้าง Test Plan";

  return (
    <>
      <PlanGuide defaultOpen={!loading && !error && total === 0 && !filterCount && !debouncedSearch} canCreate={canCreate} createDisabledReason={createDisabledReason} onCreate={() => setForm({})} />
      <article className="card lp-card tp-list">
        {error && <div className="inline-alert error" role="alert"><span>{error}{rows.length ? " — แสดงข้อมูลชุดก่อนหน้า" : ""}</span><button onClick={() => setReload((x) => x + 1)}><span className="material-symbols-outlined" aria-hidden="true">refresh</span> ลองใหม่</button></div>}
        <div className="lp-head">
          <div className="lp-title">
            <div><strong>{total.toLocaleString()}</strong><span>Test Plan{filterCount ? ` · กรอง ${filterCount} เงื่อนไข` : ""}</span></div>
            {summary && <dl className="tp-stats" aria-label="สรุป Test Plan ตามตัวกรอง">
              <div><dt>Run</dt><dd>{summary.runCount.toLocaleString()}</dd></div>
              <div><dt>Test Case</dt><dd>{summary.totalCases.toLocaleString()}</dd></div>
              <div><dt>เริ่มทดสอบแล้ว</dt><dd>{percentText(summary.progressPercent)}</dd></div>
              <div><dt>อัตราผ่าน</dt><dd>{percentText(summary.passRate)}</dd></div>
              {summary.failed + summary.blocked > 0 && <div className="is-alert"><dt>Fail / Blocked</dt><dd>{(summary.failed + summary.blocked).toLocaleString()}</dd></div>}
            </dl>}
          </div>
          <div className="lp-actions">
            {canExport && <button className="btn" type="button" disabled={exporting || loading || total === 0} onClick={exportList}>{exporting ? <><span className="spinner inline" aria-hidden="true" />กำลัง Export...</> : <><span className="material-symbols-outlined" aria-hidden="true">download</span>Export</>}</button>}
            <DeletedItemsButton type="test-plans" label="Test Plan" projectId={contextProjectId} onRestored={() => setReload((x) => x + 1)} />
            {canCreate && <button className="btn primary" type="button" disabled={!!createDisabledReason} title={createDisabledReason || undefined} onClick={() => setForm({})}><span className="material-symbols-outlined" aria-hidden="true">add</span>Test Plan</button>}
          </div>
        </div>
        {/* สถานะ + ตัวกรองอยู่แถวเดียวกัน (ตัวกรองมีแค่ 2 ช่อง — ไม่ยืดเต็มความกว้าง) */}
        <div className="tp-filterbar">
          <div className="lp-chips" role="group" aria-label="กรองตามสถานะ">
            <button type="button" aria-pressed={status === ""} onClick={() => setStatus("")}>ทั้งหมด</button>
            {planStatuses.map((s) => (
              <button key={s} type="button" aria-pressed={status === s} onClick={() => setStatus((current) => current === s ? "" : s)} title={planStatusInfo[s].meaning}>
                <span className={`lp-dot ${planStatusInfo[s].dot}`} aria-hidden="true" />{planStatusInfo[s].label}
              </button>
            ))}
          </div>
          <div className="lp-filters" role="group" aria-label="ตัวกรอง Test Plan">
            <select aria-label="ประเภทแผน" value={planType} onChange={(e) => setPlanType(e.target.value)}>
              <option value="">ทุกประเภท</option>
              {planTypes.map((t) => <option key={t} value={t}>{t}</option>)}
            </select>
            <select aria-label="เจ้าของแผน" value={owner} onChange={(e) => setOwner(e.target.value)}>
              <option value="">เจ้าของทั้งหมด</option>
              {users.map((u) => <option key={u.userId} value={u.userId}>{u.displayName}</option>)}
            </select>
            {filterCount > 0 && <button type="button" className="btn lp-clear" onClick={clearFilters}><span className="material-symbols-outlined" aria-hidden="true">filter_alt_off</span>ล้างตัวกรอง</button>}
          </div>
        </div>
        {/* แบ่งหน้าด้านบนแสดงเมื่อมีมากกว่า 1 หน้า — หน้าเดียวไม่มีอะไรให้เปลี่ยน (จำนวนต่อหน้ายังปรับได้ที่แถบล่าง) */}
        {pageCount > 1 && <ListPager position="top" page={page} pageCount={pageCount} totalCount={total} pageSize={pageSize} disabled={loading} onPage={setPage} onPageSize={(size) => { setPageSize(size); setPage(1); }} />}
        <div className="lp-table-wrap">
          <table className="lp-table tp-table">
            <thead>
              <tr>
                <th className="lp-col-item">Test Plan</th>
                <th className="lp-col-progress">ความคืบหน้า</th>
                <th className="lp-col-num">อัตราผ่าน</th>
                <th className="lp-col-module lp-hide-md">เจ้าของ</th>
                <th className="lp-col-status">สถานะ</th>
                <th className="lp-col-date">กำหนดการ</th>
              </tr>
            </thead>
            <tbody>
              {loading && !rows.length && <tr className="lp-empty-row"><td colSpan={6}><div className="lp-empty" role="status"><span className="spinner" aria-hidden="true" /><b>กำลังโหลด Test Plan...</b></div></td></tr>}
              {!loading && !error && rows.length === 0 && <tr className="lp-empty-row"><td colSpan={6}><div className="lp-empty"><span className="material-symbols-outlined" aria-hidden="true">event_note</span><b>{filterCount || debouncedSearch ? "ไม่พบ Test Plan ตามตัวกรองที่เลือก" : "ยังไม่มี Test Plan"}</b><small>{filterCount || debouncedSearch ? "ลองเปลี่ยนสถานะ ตัวกรอง หรือคำค้น" : "เริ่มจากขั้นตอนใน “เริ่มต้นใช้งาน Test Plan” ด้านบน — สร้างแผนแล้วเพิ่ม Run ต่อ Configuration"}</small></div></td></tr>}
              {rows.map((x) => {
                const info = planStatusInfo[x.status] ?? planStatusInfo.Draft;
                return (
                  <tr key={x.testPlanId} className={x.status === "Cancelled" ? "is-inactive" : undefined}>
                    <td className="lp-col-item">
                      <button type="button" className="lp-item" onClick={() => setDetailId(x.testPlanId)} aria-label={`เปิด ${x.planCode} ${x.planName}${x.isFrozen ? " (Frozen)" : ""}`}>
                        <span className="lp-code">{x.planCode}{x.isFrozen && <span className="tp-frozen" title="แผนถูก Freeze — แก้ไขแผนและ Run ไม่ได้"><span className="material-symbols-outlined" aria-hidden="true">lock</span>Frozen</span>}<span className="lp-meta">{[x.planType, x.releaseCode].filter(Boolean).join(" · ")}</span></span>
                        <span className="lp-name">{x.planName}</span>
                      </button>
                    </td>
                    <td className="lp-col-progress">
                      <span className={`lp-progress${x.progressPercent >= 100 ? " is-done" : x.progressPercent > 0 ? "" : " is-empty"}`} role="img" aria-label={`เริ่มทดสอบแล้ว ${x.touched} จาก ${x.totalCases} (${percentText(x.progressPercent)}) ใน ${x.runCount} Run`}>
                        <span className="lp-progress-track"><span style={{ width: `${Math.min(100, x.progressPercent)}%` }} /></span>
                        <small><b>{percentText(x.progressPercent)}</b> {x.touched.toLocaleString()}/{x.totalCases.toLocaleString()} เคส · {x.runCount.toLocaleString()} Run</small>
                      </span>
                    </td>
                    <td className="lp-col-num lp-labeled" data-label="อัตราผ่าน"><span className="tp-rate"><b>{percentText(x.passRate)}</b>{x.passRate !== null && <small>ผ่าน {x.passed.toLocaleString()}</small>}</span></td>
                    <td className="lp-col-module lp-hide-md lp-labeled" data-label="เจ้าของ">{x.ownerName || "-"}</td>
                    <td className="lp-col-status"><Badge tone={info.tone}>{info.label}</Badge></td>
                    <td className="lp-col-date"><span className="lp-date">{scheduleText(x.startDate, x.endDate)}<DueHint days={daysLeftOf(x.status, x.endDate)} /></span></td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
        <ListPager position="bottom" page={page} pageCount={pageCount} totalCount={total} pageSize={pageSize} disabled={loading} onPage={setPage} onPageSize={(size) => { setPageSize(size); setPage(1); }} />
      </article>
      {detailId && <PlanDetailModal planId={detailId} version={detailVersion} users={users} canCreate={canCreate} canEdit={canEdit} canDelete={canDelete} canFreeze={canFreeze} canAssign={canAssign} canRun={canRun} canExport={canExport}
        onClose={() => setDetailId(null)}
        onChanged={() => setReload((x) => x + 1)}
        onDeleted={() => { setDetailId(null); setReload((x) => x + 1); }}
        onEdit={(plan) => setForm({ plan })}
        onCloned={(created) => { setReload((x) => x + 1); setDetailId(created.testPlanId); }}
        onOpenExecution={(cycleId) => { setDetailId(null); onOpenExecution(cycleId); }}
        onOpenCycle={(cycleId) => { setDetailId(null); onOpenCycle(cycleId); }} />}
      {form && <PlanFormModal plan={form.plan} projectId={form.plan?.projectId ?? contextProjectId} defaultReleaseId={contextReleaseId} users={users}
        onClose={() => setForm(null)}
        onSaved={(saved, created) => { setForm(null); setReload((x) => x + 1); if (created) setDetailId(saved.testPlanId); else setDetailVersion((x) => x + 1); }} />}
    </>
  );
}

/** คู่มือเริ่มต้นสำหรับผู้ใช้ใหม่ — Flow 5 ขั้น + ตัวอย่างแผน + คำศัพท์; เปิดเองเมื่อยังไม่มีแผน และจำการเปิด/ปิดของผู้ใช้ */
const guideSteps = [
  { icon: "add_task", title: "สร้าง Test Plan", desc: "ตั้งชื่อแผน เลือก Release และประเภท (Smoke / Regression / UAT)", example: "เช่น “Regression ก่อนปล่อย 2026.11” ของ REL-2026.11", where: "ปุ่ม “+ Test Plan” มุมขวาบน" },
  { icon: "playlist_add", title: "เพิ่ม Run ต่อ Configuration", desc: "1 Run = 1 Test Cycle ที่ทดสอบบนสภาพแวดล้อมหนึ่ง เลือก Cycle ที่มีอยู่ หรือสร้างใหม่จาก Test Suite", example: "เช่น 3 Run: Windows · Chrome / Windows · Edge / Android · Pixel 8", where: "เปิดแผน → “เพิ่ม Run”" },
  { icon: "group_add", title: "จัดทีมและแบ่งเคส", optional: true, desc: "เลือกผู้ทดสอบของแต่ละ Run ระบบแบ่งเคสที่ยังไม่มีผู้รับผิดชอบให้แบบวนทีละคน และผู้ทดสอบเห็นงานในหน้า My Work", example: "เช่น Run Chrome: สมชาย (ผู้ทดสอบ) · สมหญิง (ผู้ตรวจ)", where: "ไอคอน “จัดทีม” ในแถว Run" },
  { icon: "play_arrow", title: "เริ่มทดสอบ", desc: "เริ่ม Test Cycle ของ Run ในหน้า Test Cycle แล้วกด “ทดสอบ” เพื่อบันทึกผลใน Execution Workspace — ความคืบหน้าของแผนรวมจากทุก Run ให้อัตโนมัติ", example: "เช่น Run Chrome ทดสอบแล้ว 10/12 เคส ผ่าน 8", where: "ปุ่ม “ทดสอบ” ในแถว Run" },
  { icon: "task_alt", title: "ติดตามผลและปิดแผน", desc: "เทียบผลแต่ละ Configuration ใน Matrix เมื่อทุก Run จบ ให้ปิดแผน — ระบบ Freeze ผลไว้; ได้ Build ใหม่ใช้ Clone / Rerun", example: "เช่น Edge Fail มากกว่า Chrome → แจ้ง Defect เฉพาะ Edge", where: "“จัดการแผน” → “ปิดแผน”" },
];
const guideExampleRuns = [
  { code: "REG-WIN-CHROME", config: "Windows · Chrome", status: "กำลังทดสอบ", tone: "blue", passed: 8, failed: 2, inProgress: 0, untested: 2 },
  { code: "REG-WIN-EDGE", config: "Windows · Edge", status: "ทดสอบเสร็จ", tone: "green", passed: 9, failed: 3, inProgress: 0, untested: 0 },
  { code: "REG-ANDROID", config: "Android · Pixel 8", status: "ฉบับร่าง", tone: "gray", passed: 0, failed: 0, inProgress: 0, untested: 12 },
];
const guideTerms = [
  { term: "Test Plan", meaning: "แผนรวมการทดสอบของ Release หนึ่ง — รวมหลาย Test Cycle ไว้ดูผลที่เดียว" },
  { term: "Run", meaning: "Test Cycle 1 ชุดในแผน ผูกกับ Build, Environment และ Configuration" },
  { term: "Configuration", meaning: "สภาพแวดล้อมที่ทดสอบ เช่น OS · Browser · Device ใช้เทียบผลข้าม Run" },
  { term: "Freeze", meaning: "ล็อกแผนและ Run ไม่ให้แก้ไขหรือบันทึกผลเพิ่ม — ปิดแผนแล้ว Freeze อัตโนมัติ" },
];

function PlanGuide({ defaultOpen, canCreate, createDisabledReason, onCreate }: { defaultOpen: boolean; canCreate: boolean; createDisabledReason: string; onCreate: () => void }) {
  const [pref, setPref] = useState<string | null>(() => { try { return localStorage.getItem("qa.testPlans.guide"); } catch { return null; } });
  const open = pref ? pref === "open" : defaultOpen;
  const toggle = (next: boolean) => {
    if (next === open) return; // React ตั้งค่า open เอง (ข้อมูลเพิ่งโหลด) ไม่ใช่ผู้ใช้กด
    const value = next ? "open" : "closed";
    setPref(value);
    try { localStorage.setItem("qa.testPlans.guide", value); } catch { /* ใช้ค่าเริ่มต้นได้ */ }
  };
  return (
    <details className="tp-guide" open={open} onToggle={(e) => toggle((e.currentTarget as HTMLDetailsElement).open)}>
      <summary>
        <span className="tp-guide-icon material-symbols-outlined" aria-hidden="true">route</span>
        <span className="tp-guide-title"><strong>เริ่มต้นใช้งาน Test Plan</strong><small>รวม Test Cycle หลายชุดของ Release เดียวกันเป็นแผนเดียว แล้วเทียบผลตาม Configuration — 5 ขั้นตอนพร้อมตัวอย่าง</small></span>
        <span className="tp-guide-toggle">{open ? "ซ่อน" : "ดูวิธีใช้"}<span className="material-symbols-outlined" aria-hidden="true">expand_more</span></span>
      </summary>
      <ol className="tp-guide-steps">
        {guideSteps.map((step, i) => (
          <li key={step.title}>
            <span className="tp-guide-no" aria-hidden="true">{i + 1}</span>
            <div>
              <strong>{step.title}{step.optional && <span className="tp-guide-optional">ไม่บังคับ</span>}</strong>
              <p>{step.desc}</p>
              <p className="tp-guide-example"><span className="material-symbols-outlined" aria-hidden="true">lightbulb</span><span>{step.example}</span></p>
              <small><span className="material-symbols-outlined" aria-hidden="true">{step.icon}</span>{step.where}</small>
            </div>
            {i === 0 && canCreate && <button type="button" className="btn primary" disabled={!!createDisabledReason} title={createDisabledReason || undefined} onClick={onCreate}><span className="material-symbols-outlined" aria-hidden="true">add</span>สร้าง Test Plan</button>}
          </li>
        ))}
      </ol>
      <div className="tp-guide-extra">
        <section aria-labelledby="tp-guide-example-title">
          <h3 id="tp-guide-example-title">ตัวอย่าง: Configuration Matrix ของแผน Regression</h3>
          <ul className="tp-guide-matrix">
            {guideExampleRuns.map((r) => {
              const total = r.passed + r.failed + r.inProgress + r.untested;
              const done = total - r.untested;
              return <li key={r.code}>
                <span><b>{r.code}</b><span className="tp-config">{r.config}</span><Badge tone={r.tone}>{r.status}</Badge></span>
                <span className="cyd-bar" role="img" aria-label={`ผ่าน ${r.passed}, ไม่ผ่าน ${r.failed}, ยังไม่เริ่ม ${r.untested}`}>
                  <span className="is-pass" style={{ flexGrow: r.passed }} /><span className="is-fail" style={{ flexGrow: r.failed }} /><span className="is-notrun" style={{ flexGrow: r.untested }} />
                </span>
                <small>{Math.round((done / total) * 100)}% · {done}/{total}</small>
              </li>;
            })}
          </ul>
          <p className="tp-guide-note">เห็นทันทีว่า Edge ทดสอบครบแล้วแต่ Fail มากกว่า Chrome และ Android ยังไม่เริ่ม — เป็นข้อมูลตัวอย่าง ไม่ใช่ข้อมูลจริง</p>
        </section>
        <section aria-labelledby="tp-guide-terms-title">
          <h3 id="tp-guide-terms-title">คำศัพท์ที่ควรรู้</h3>
          <dl className="tp-guide-terms">{guideTerms.map((t) => <div key={t.term}><dt>{t.term}</dt><dd>{t.meaning}</dd></div>)}</dl>
        </section>
      </div>
    </details>
  );
}

/** ขั้นต่อไปของแผนที่เปิดอยู่ ตาม Flow ในคู่มือ — ไม่มีสิทธิ์ทำ = แสดงเฉพาะคำแนะนำ ไม่มีปุ่ม */
type NextStep = { icon: string; text: string; action?: { label: string; icon: string; go: () => void } };
const nextStepOf = (plan: PlanDetail, can: { create: boolean; edit: boolean; freeze: boolean; run: boolean; editable: boolean }, go: { addRun: () => void; openCycle: (id: string) => void; openExecution: (id: string) => void; close: () => void; clone: () => void }): NextStep | null => {
  if (plan.lifecycleStatus !== "Active") return plan.runs.length ? { icon: "content_copy", text: "แผนนี้ปิดแล้ว — เมื่อได้ Build ใหม่ ใช้ Clone / Rerun สร้างแผนรอบใหม่จากแผนนี้ได้ทันที", action: can.create ? { label: "Clone / Rerun", icon: "content_copy", go: go.clone } : undefined } : null;
  if (plan.isFrozen) return null; // แถบ Frozen บอกวิธีแก้อยู่แล้ว
  if (!plan.runs.length) return { icon: "playlist_add", text: "ขั้นต่อไป: เพิ่ม Run แรก — เลือก Test Cycle ที่มีอยู่ หรือสร้างใหม่จาก Test Suite แล้วตั้งชื่อ Configuration เช่น “Windows · Chrome”", action: can.edit && can.editable ? { label: "เพิ่ม Run", icon: "add", go: go.addRun } : undefined };
  const testing = plan.runs.find((r) => r.status === "InProgress");
  if (testing) return { icon: "play_arrow", text: `ขั้นต่อไป: บันทึกผลของ ${testing.runCode} ใน Execution Workspace — ความคืบหน้าของแผนจะอัปเดตเอง`, action: can.run ? { label: "ทดสอบ", icon: "play_arrow", go: () => go.openExecution(testing.testCycleId) } : undefined };
  const draft = plan.runs.find((r) => r.status === "Draft");
  if (draft) return { icon: "rocket_launch", text: `ขั้นต่อไป: เริ่ม Test Cycle ${draft.cycleCode} ของ ${draft.runCode} (จัดทีมก่อนได้ถ้าต้องการ) — เริ่มแล้วปุ่ม “ทดสอบ” จะขึ้นในแถว Run`, action: can.run ? { label: "เปิด Test Cycle", icon: "open_in_new", go: () => go.openCycle(draft.testCycleId) } : undefined };
  if (plan.status === "Completed") return { icon: "task_alt", text: "ทุก Run จบแล้ว — ตรวจผลใน Matrix แล้วปิดแผน ระบบจะ Freeze ผลไว้เป็นหลักฐาน", action: can.freeze ? { label: "ปิดแผน", icon: "task_alt", go: go.close } : undefined };
  return null;
};

type ActionMenuItem ={ key: string; icon: string; label: string; hint?: string; danger?: boolean; disabled?: boolean; onSelect: () => void };
/** เมนูคำสั่งรองในแถบปุ่ม — ↑/↓/Home/End เลื่อน, Esc ปิดเฉพาะเมนู (ไม่ปิดหน้าต่าง) แล้วคืน focus ที่ปุ่ม, คลิกนอกเมนูเพื่อปิด */
function ActionMenu({ label, items, disabled }: { label: string; items: ActionMenuItem[]; disabled: boolean }) {
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const menuId = useId();
  const enabledItems = () => [...(rootRef.current?.querySelectorAll<HTMLButtonElement>('[role="menuitem"]:not(:disabled)') ?? [])];

  useEffect(() => {
    if (!open) return;
    enabledItems()[0]?.focus();
    const onPointerDown = (e: PointerEvent) => { if (!rootRef.current?.contains(e.target as Node)) setOpen(false); };
    document.addEventListener("pointerdown", onPointerDown);
    return () => document.removeEventListener("pointerdown", onPointerDown);
  }, [open]);

  if (!items.length) return null;
  const close = () => { setOpen(false); buttonRef.current?.focus(); };
  const onMenuKeyDown = (e: React.KeyboardEvent) => {
    // ModalShell ฟัง Escape ที่ document — หยุดไว้ที่นี่ไม่ให้หน้าต่างรายละเอียดปิดตาม
    if (e.key === "Escape") { e.preventDefault(); e.stopPropagation(); close(); return; }
    if (e.key === "Tab") { setOpen(false); return; }
    const list = enabledItems();
    if (!list.length || !["ArrowDown", "ArrowUp", "Home", "End"].includes(e.key)) return;
    e.preventDefault();
    const index = list.indexOf(document.activeElement as HTMLButtonElement);
    const next = e.key === "Home" ? 0 : e.key === "End" ? list.length - 1 : (index + (e.key === "ArrowDown" ? 1 : -1) + list.length) % list.length;
    list[next].focus();
  };

  return (
    <div className="tp-menu" ref={rootRef}>
      <button ref={buttonRef} type="button" className="btn" disabled={disabled} aria-haspopup="menu" aria-expanded={open} aria-controls={open ? menuId : undefined}
        onClick={() => setOpen((x) => !x)} onKeyDown={(e) => { if (e.key === "ArrowDown" && !open) { e.preventDefault(); setOpen(true); } }}>
        <span className="material-symbols-outlined" aria-hidden="true">more_horiz</span> {label}
      </button>
      {open && <div id={menuId} className="tp-menu-list" role="menu" aria-label={label} onKeyDown={onMenuKeyDown}>
        {items.map((item) => (
          <button key={item.key} type="button" role="menuitem" className={item.danger ? "is-danger" : undefined} disabled={item.disabled} onClick={() => { setOpen(false); item.onSelect(); }}>
            <span className="material-symbols-outlined" aria-hidden="true">{item.icon}</span>
            <span><b>{item.label}</b>{item.hint && <small>{item.hint}</small>}</span>
          </button>
        ))}
      </div>}
    </div>
  );
}

function PlanDetailModal({ planId, version, users, canCreate, canEdit, canDelete, canFreeze, canAssign, canRun, canExport, onClose, onChanged, onDeleted, onEdit, onCloned, onOpenExecution, onOpenCycle }: {
  planId: string; version: number; users: UserLookup[]; canCreate: boolean; canEdit: boolean; canDelete: boolean; canFreeze: boolean; canAssign: boolean; canRun: boolean; canExport: boolean;
  onClose: () => void; onChanged: () => void; onDeleted: () => void; onEdit: (plan: PlanDetail) => void; onCloned: (plan: PlanDetail) => void;
  onOpenExecution: (cycleId: string) => void; onOpenCycle: (cycleId: string) => void;
}) {
  const [plan, setPlan] = useState<PlanDetail | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [reload, setReload] = useState(0);
  const [runFilter, setRunFilter] = useState("");
  const [runForm, setRunForm] = useState<{ run?: PlanRun } | null>(null);
  const [teamRun, setTeamRun] = useState<PlanRun | null>(null);
  const [cloneOpen, setCloneOpen] = useState(false);

  useEffect(() => {
    const ctrl = new AbortController();
    setLoading(true);
    getJson<PlanDetail>(`${apiUrl}/test-plans/${planId}`, ctrl.signal)
      .then((data) => { setPlan(normalizePlan(data)); setError(""); })
      .catch((e) => { if (!isAbortError(e)) setError(errorText(e, "โหลด Test Plan ไม่สำเร็จ")); })
      .finally(() => { if (!ctrl.signal.aborted) setLoading(false); });
    return () => ctrl.abort();
  }, [planId, version, reload]);

  const refresh = () => { setReload((x) => x + 1); onChanged(); };
  const editable = !!plan && plan.lifecycleStatus === "Active" && !plan.isFrozen;
  const runStatuses = useMemo(() => [...new Set((plan?.runs ?? []).map((r) => r.status))], [plan]);
  const visibleRuns = (plan?.runs ?? []).filter((r) => !runFilter || r.status === runFilter);

  const lifecycle = async (action: "close" | "cancel" | "reopen" | "freeze" | "unfreeze") => {
    if (!plan) return;
    const meta = {
      close: { title: "ปิด Test Plan", message: `ปิด ${plan.planCode}? ระบบจะ Freeze แผนอัตโนมัติ — แก้ไข Run หรือข้อมูลแผนไม่ได้จนกว่าจะ Reopen`, label: "ปิดแผน", done: "ปิด Test Plan แล้ว", tone: "primary" as const },
      cancel: { title: "ยกเลิก Test Plan", message: `ยกเลิก ${plan.planCode}? Run และผลการทดสอบยังเก็บไว้ครบ และ Reopen กลับมาได้`, label: "ยกเลิกแผน", done: "ยกเลิก Test Plan แล้ว", tone: "danger" as const },
      reopen: { title: "เปิด Test Plan อีกครั้ง", message: `เปิด ${plan.planCode} กลับมาใช้งานและยกเลิก Freeze`, label: "Reopen", done: "เปิด Test Plan อีกครั้งแล้ว", tone: "primary" as const },
      freeze: { title: "Freeze Test Plan", message: `Freeze ${plan.planCode}? แก้ข้อมูลแผนและ Run ไม่ได้จนกว่าจะ Unfreeze`, label: "Freeze", done: "Freeze Test Plan แล้ว", tone: "primary" as const },
      unfreeze: { title: "Unfreeze Test Plan", message: `ยกเลิก Freeze ของ ${plan.planCode} เพื่อแก้ไขต่อ`, label: "Unfreeze", done: "Unfreeze Test Plan แล้ว", tone: "primary" as const },
    }[action];
    const reason = await promptDialog({ title: meta.title, message: `${meta.message}\nเหตุผลจะถูกบันทึกใน Audit Log`, placeholder: "ระบุเหตุผล", required: true, multiline: true, confirmLabel: meta.label, tone: meta.tone });
    if (reason === null) return;
    setBusy(true);
    try {
      const updated = await apiFetch<PlanDetail>(`/test-plans/${plan.testPlanId}/${action}`, { method: "POST", json: { reason }, fallbackMessage: `${meta.title} ไม่สำเร็จ` });
      setPlan(normalizePlan(updated));
      onChanged();
      notify(meta.done, "success");
    } catch (e) { notify(errorText(e, `${meta.title} ไม่สำเร็จ`), "error"); }
    finally { setBusy(false); }
  };

  const remove = async () => {
    if (!plan) return;
    if (!(await confirmDialog({ title: "ลบ Test Plan", message: `ลบ ${plan.planCode} ${plan.planName}? แผนจะไม่แสดงในรายการ แต่ Test Cycle และผลการทดสอบยังอยู่ครบ (Cycle ในแผนยังผูกกับแผนนี้)`, confirmLabel: "ลบ", tone: "danger" }))) return;
    setBusy(true);
    try { await apiFetch(`/test-plans/${plan.testPlanId}`, { method: "DELETE", fallbackMessage: "ลบ Test Plan ไม่สำเร็จ" }); notify(`ลบ ${plan.planCode} แล้ว`, "success"); onDeleted(); }
    catch (e) { notify(errorText(e, "ลบ Test Plan ไม่สำเร็จ"), "error"); }
    finally { setBusy(false); }
  };

  const detach = async (run: PlanRun) => {
    if (!(await confirmDialog({ title: "ถอด Run ออกจากแผน", message: `ถอด ${run.runCode} ออกจาก Test Plan? Test Cycle ${run.cycleCode} ยังอยู่ (ถอดได้เฉพาะ Run ที่ยังไม่เคยมีผลการทดสอบ)`, confirmLabel: "ถอดออก", tone: "danger" }))) return;
    setBusy(true);
    try { await apiFetch(`/test-plans/${planId}/runs/${run.testPlanRunId}`, { method: "DELETE", fallbackMessage: "ถอด Run ไม่สำเร็จ" }); notify(`ถอด ${run.runCode} แล้ว`, "success"); refresh(); }
    catch (e) { notify(errorText(e, "ถอด Run ไม่สำเร็จ"), "error"); }
    finally { setBusy(false); }
  };

  const s = plan?.summary;
  const info = plan ? planStatusInfo[plan.status] ?? planStatusInfo.Draft : null;
  const next = plan ? nextStepOf(plan, { create: canCreate, edit: canEdit, freeze: canFreeze, run: canRun, editable }, { addRun: () => setRunForm({}), openCycle: onOpenCycle, openExecution: onOpenExecution, close: () => lifecycle("close"), clone: () => setCloneOpen(true) }) : null;
  return (
    <>
    <ModalShell labelledBy="tp-detail-title" className="cyd tp-detail" onDismiss={() => { if (!busy) onClose(); }}>
      <header className="cyd-head">
        <div className="cyd-head-text">
          <span className="cyd-eyebrow">TEST PLAN{plan?.releaseCode ? ` · ${plan.releaseCode}` : ""}</span>
          <h2 id="tp-detail-title">{plan?.planCode ?? "Test Plan"}</h2>
          <p>{plan?.planName ?? (loading ? "กำลังโหลด..." : "")}</p>
        </div>
        {plan && info && <div className="cyd-badges">
          <Badge tone={info.tone}>{info.label}</Badge>
          {plan.isFrozen && <Badge tone="purple"><span className="material-symbols-outlined cyd-badge-icon" aria-hidden="true">lock</span> Frozen</Badge>}
          <Badge tone="blue">{plan.planType}</Badge>
        </div>}
        <button type="button" className="cyd-close" aria-label="ปิดรายละเอียด Test Plan" disabled={busy} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
      </header>
      {plan && (canCreate || canEdit || canDelete || canFreeze || canExport) && <div className="cyd-toolbar tp-toolbar" role="toolbar" aria-label="การดำเนินการกับ Test Plan">
        {canFreeze && plan.lifecycleStatus !== "Active" && <button type="button" className="btn primary" disabled={busy} onClick={() => lifecycle("reopen")}><span className="material-symbols-outlined" aria-hidden="true">replay</span> Reopen</button>}
        {canEdit && editable && <button type="button" className="btn primary" disabled={busy} onClick={() => setRunForm({})}><span className="material-symbols-outlined" aria-hidden="true">add</span> เพิ่ม Run</button>}
        {canEdit && editable && <button type="button" className="btn" disabled={busy} onClick={() => onEdit(plan)}><span className="material-symbols-outlined" aria-hidden="true">edit</span> แก้ไขแผน</button>}
        {canCreate && plan.runs.length > 0 && <button type="button" className="btn" disabled={busy} onClick={() => setCloneOpen(true)} title="สร้างแผนใหม่จากแผนนี้สำหรับ Build ใหม่ (ไม่คัดลอกผลการทดสอบ)"><span className="material-symbols-outlined" aria-hidden="true">content_copy</span> Clone / Rerun</button>}
        {canExport && <button type="button" className="btn" disabled={busy} onClick={() => { exportPlanSummary(plan); notify(`ส่งออกสรุป ${plan.planCode} แล้ว`, "success"); }} title="ดาวน์โหลดสรุปแผนและ Configuration Matrix เป็น CSV"><span className="material-symbols-outlined" aria-hidden="true">download</span> Export</button>}
        {/* คำสั่งที่ใช้ไม่บ่อยและคำสั่งอันตรายรวมในเมนูเดียว ลดปุ่มในแถบจาก 7–8 ปุ่ม */}
        <ActionMenu label="จัดการแผน" disabled={busy} items={[
          ...(canFreeze && plan.lifecycleStatus === "Active" ? [
            { key: "close", icon: "task_alt", label: "ปิดแผน", hint: plan.status !== "Completed" ? "ปิดได้เมื่อทุก Run จบแล้ว" : "Freeze อัตโนมัติ", disabled: plan.status !== "Completed", onSelect: () => lifecycle("close") },
            { key: "freeze", icon: plan.isFrozen ? "lock_open" : "lock", label: plan.isFrozen ? "Unfreeze" : "Freeze", hint: plan.isFrozen ? "แก้ไขแผนและ Run ได้อีกครั้ง" : "ล็อกแผนและ Run ไม่ให้แก้ไข", onSelect: () => lifecycle(plan.isFrozen ? "unfreeze" : "freeze") },
            { key: "cancel", icon: "block", label: "ยกเลิกแผน", hint: "เก็บ Run และผลไว้ Reopen ได้", danger: true, onSelect: () => lifecycle("cancel") },
          ] : []),
          ...(canDelete && (plan.lifecycleStatus !== "Active" || plan.runs.length === 0) ? [{ key: "delete", icon: "delete", label: "ลบ Test Plan", hint: "Test Cycle และผลยังอยู่ครบ", danger: true, onSelect: remove }] : []),
        ]} />
      </div>}
      {plan && !editable && <p className="tp-locked" role="status"><span className="material-symbols-outlined" aria-hidden="true">lock</span>{plan.lifecycleStatus !== "Active" ? `แผน${info?.label ?? ""} — แก้ไขแผนและ Run ไม่ได้ ใช้ Reopen เพื่อแก้ไขต่อ` : "แผนถูก Freeze — แก้ไขแผนและ Run ไม่ได้จนกว่าจะ Unfreeze"}</p>}
      {next && <div className="tp-next">
        <span className="material-symbols-outlined" aria-hidden="true">{next.icon}</span>
        <p>{next.text}</p>
        {next.action && <button type="button" className="btn" disabled={busy} onClick={next.action.go}><span className="material-symbols-outlined" aria-hidden="true">{next.action.icon}</span>{next.action.label}</button>}
      </div>}
      <div className="cyd-body">
        <div className="cyd-main">
          {error && <div className="inline-alert error" role="alert"><span>{error}</span><button onClick={() => setReload((x) => x + 1)}><span className="material-symbols-outlined" aria-hidden="true">refresh</span> ลองใหม่</button></div>}
          {!plan && loading && <div className="cyd-state" role="status"><span className="spinner inline" aria-hidden="true" /> กำลังโหลด Test Plan...</div>}
          {plan && s && <section className="cyd-progress tp-progress" aria-labelledby="tp-progress-title">
            <h3 id="tp-progress-title" className="sr-only">ความคืบหน้ารวม</h3>
            <dl className="tp-kpis">
              <div><dt>เริ่มทดสอบแล้ว</dt><dd><b>{percentText(s.progressPercent)}</b><small>{s.touched.toLocaleString()} / {s.totalCases.toLocaleString()} เคส</small></dd></div>
              <div><dt>ทดสอบจบ</dt><dd><b>{percentText(s.completionPercent)}</b><small>{s.completed.toLocaleString()} เคส</small></dd></div>
              <div><dt>อัตราผ่าน</dt><dd><b>{percentText(s.passRate)}</b><small>ผ่าน {s.passed.toLocaleString()} เคส</small></dd></div>
              <div title="จำนวน Test Case ที่มีผลจบมากกว่า 1 ครั้ง (ไม่นับการกด Save Progress)"><dt>Retest</dt><dd><b>{s.retest.toLocaleString()}</b><small>{s.retestAttempts > s.retest ? `เคส · รวม ${s.retestAttempts.toLocaleString()} ครั้ง` : "เคส"}</small></dd></div>
            </dl>
            <div className="cyd-bar" role="img" aria-label={caseStatusSegments.map((x) => `${x.label} ${s[x.key]}`).join(", ")}>
              {s.totalCases > 0 ? caseStatusSegments.map((x) => <span key={x.key} className={x.className} style={{ flexGrow: s[x.key] }} title={`${x.label} ${s[x.key]}`} />) : <span className="is-notrun" style={{ flexGrow: 1 }} />}
            </div>
            <ul className="tp-legend" aria-label="จำนวนเคสตามสถานะ">
              {caseStatusSegments.map((x) => <li key={x.key} className={s[x.key] ? undefined : "is-zero"}><span className={`cyd-dot ${x.className}`} aria-hidden="true" />{x.label} <b>{s[x.key].toLocaleString()}</b></li>)}
            </ul>
          </section>}
          {plan && <section className="cyd-cases" aria-labelledby="tp-matrix-title">
            <div className="cyd-cases-head">
              <h3 id="tp-matrix-title"><span className="material-symbols-outlined" aria-hidden="true">grid_view</span> Configuration Matrix <span className="cyd-count">{plan.runs.length.toLocaleString()}</span></h3>
              {runStatuses.length > 1 && <div className="cyd-chips" role="group" aria-label="กรอง Run ตามสถานะ">
                <button type="button" aria-pressed={!runFilter} onClick={() => setRunFilter("")}>ทั้งหมด</button>
                {runStatuses.map((st) => <button key={st} type="button" aria-pressed={runFilter === st} onClick={() => setRunFilter((c) => c === st ? "" : st)}>{runStatusLabel(st)} <b>{plan.runs.filter((r) => r.status === st).length}</b></button>)}
              </div>}
            </div>
            <div className="cyd-case-list">
              {!plan.runs.length ? <div className="cyd-state"><span className="material-symbols-outlined" aria-hidden="true">add_box</span>ยังไม่มี Run — {canEdit && editable ? "กด \"เพิ่ม Run\" เพื่อเพิ่ม Test Cycle ที่มีอยู่หรือสร้างจาก Suite" : "ผู้มีสิทธิ์แก้ไขแผนเพิ่ม Run ได้"}</div>
                : visibleRuns.map((run) => {
                  const config = configSummary(run);
                  // OS/Browser/Device มักซ้ำกับชื่อ Configuration — แสดงเป็นรายการแยกเฉพาะเมื่อให้ข้อมูลเพิ่ม (ที่เหลืออยู่ใน title ของชิป)
                  const configExtra = config && config !== run.configurationName ? config : "";
                  return <article key={run.testPlanRunId} className={`tp-run is-${run.status.toLowerCase()}`}>
                    <div className="tp-run-main">
                      <div className="tp-run-code"><b>{run.runCode}</b><Badge tone={runStatusInfo[run.status]?.tone ?? "blue"}>{runStatusLabel(run.status)}</Badge>{run.configurationName && <span className="tp-config" title={[config, run.configurationKey].filter(Boolean).join(" · ") || undefined}><span className="material-symbols-outlined" aria-hidden="true">devices</span>{run.configurationName}</span>}</div>
                      {run.runName !== run.configurationName && <p>{run.runName}</p>}
                      <ul className="tp-run-meta">
                        <li title="Test Cycle"><span className="material-symbols-outlined" aria-hidden="true">fact_check</span>{canRun ? <button type="button" className="tp-link" onClick={() => onOpenCycle(run.testCycleId)} title="เปิดรายละเอียด Test Cycle">{run.cycleCode}</button> : run.cycleCode}</li>
                        <li title="Build"><span className="material-symbols-outlined" aria-hidden="true">deployed_code</span>Build {run.buildNumber ?? "-"}</li>
                        <li title="Environment"><span className="material-symbols-outlined" aria-hidden="true">dns</span>{run.environmentName ?? "-"}</li>
                        {configExtra && <li title="OS · Browser · Device"><span className="material-symbols-outlined" aria-hidden="true">devices</span>{configExtra}</li>}
                        {run.ownerName && <li title="ผู้รับผิดชอบ Run"><span className="material-symbols-outlined" aria-hidden="true">person</span>{run.ownerName}</li>}
                      </ul>
                      {(run.assignments.length > 0 || run.unassignedCases > 0) && <span className="tp-team">
                        {run.assignments.length > 0 && <span><span className="material-symbols-outlined" aria-hidden="true">group</span>{run.assignments.map((m) => `${m.testerName ?? "ผู้ใช้"} (${runRoleLabel[m.role] ?? m.role})`).join(", ")}</span>}
                        {run.unassignedCases > 0 && <span className="tp-unassigned">ยังไม่มีผู้รับผิดชอบ {run.unassignedCases.toLocaleString()} เคส</span>}
                      </span>}
                    </div>
                    <div className="tp-run-progress">
                      <div className="cyd-bar" role="img" aria-label={`${run.runCode}: ${caseStatusSegments.map((x) => `${x.label} ${run[x.key]}`).join(", ")}`}>
                        {run.totalCases > 0 ? caseStatusSegments.map((x) => <span key={x.key} className={x.className} style={{ flexGrow: run[x.key] }} />) : <span className="is-notrun" style={{ flexGrow: 1 }} />}
                      </div>
                      <small><b>{percentText(run.progressPercent)}</b> {(run.totalCases - run.untested).toLocaleString()}/{run.totalCases.toLocaleString()} · ผ่าน {percentText(run.passRate)}</small>
                    </div>
                    <div className="tp-run-actions">
                      {canRun && run.status === "InProgress" && <button type="button" className="btn" disabled={busy} onClick={() => onOpenExecution(run.testCycleId)} aria-label={`เปิด Execution Workspace ของ ${run.runCode}`} title="เปิด Execution Workspace"><span className="material-symbols-outlined" aria-hidden="true">play_arrow</span>ทดสอบ</button>}
                      {canAssign && editable && <button type="button" className="tp-icon" disabled={busy} aria-label={`จัดทีม ${run.runCode}`} title="จัดทีมและแบ่งเคส" onClick={() => setTeamRun(run)}><span className="material-symbols-outlined" aria-hidden="true">group_add</span></button>}
                      {canEdit && editable && <button type="button" className="tp-icon" disabled={busy} aria-label={`แก้ไข ${run.runCode}`} title="แก้ไข Run" onClick={() => setRunForm({ run })}><span className="material-symbols-outlined" aria-hidden="true">edit</span></button>}
                      {canEdit && editable && <button type="button" className="tp-icon danger" disabled={busy} aria-label={`ถอด ${run.runCode} ออกจากแผน`} title="ถอดออกจากแผน (เฉพาะ Run ที่ยังไม่มีผลการทดสอบ)" onClick={() => detach(run)}><span className="material-symbols-outlined" aria-hidden="true">link_off</span></button>}
                    </div>
                  </article>;
                })}
            </div>
          </section>}
        </div>
        {plan && <aside className="cyd-side" aria-label="ข้อมูล Test Plan">
          <section>
            <h3><span className="material-symbols-outlined" aria-hidden="true">dataset</span> ข้อมูลแผน</h3>
            <dl className="cyd-info">
              <div><dt>Release</dt><dd>{plan.releaseCode ?? "-"}</dd></div>
              <div><dt>เจ้าของแผน</dt><dd>{plan.ownerName ?? "ไม่ระบุ"}</dd></div>
              <div><dt>กำหนดการ</dt><dd className="tp-schedule">{scheduleText(plan.startDate, plan.endDate)}<DueHint days={daysLeftOf(plan.status, plan.endDate)} /></dd></div>
              <div><dt>สร้างเมื่อ</dt><dd>{shortDate(plan.createdAt)}{plan.updatedAt ? <small> · แก้ไขล่าสุด {shortDate(plan.updatedAt)}</small> : null}</dd></div>
              {plan.isFrozen && <div><dt>Freeze เมื่อ</dt><dd>{shortDate(plan.frozenAt)}</dd></div>}
            </dl>
          </section>
          <section>
            <h3><span className="material-symbols-outlined" aria-hidden="true">notes</span> หมายเหตุ</h3>
            <p className={`cyd-notes${plan.notes ? "" : " is-empty"}`}>{plan.notes || "ไม่มีหมายเหตุ"}</p>
          </section>
          <details className="cyd-status-help">
            <summary><span className="material-symbols-outlined" aria-hidden="true">help</span> ความหมายสถานะของแผน</summary>
            <p>Draft / กำลังทดสอบ / ทดสอบครบ คำนวณจากสถานะ Test Cycle ใน Run อัตโนมัติ ส่วนปิดแล้ว / ยกเลิก เป็นคำสั่งของผู้มีสิทธิ์</p>
            <ul>{planStatuses.map((st) => <li key={st} className={plan.status === st ? "is-current" : undefined}><b>{planStatusInfo[st].label}</b>{planStatusInfo[st].meaning}</li>)}</ul>
          </details>
        </aside>}
      </div>
    </ModalShell>
    {/* หน้าต่าง Run อยู่นอกกล่องรายละเอียด — ถ้าซ้อนข้างใน .modal (fixed) จะยึดกับกล่องแม่แทนหน้าจอ */}
    {runForm && plan && (runForm.run
      ? <RunFormModal plan={plan} run={runForm.run} users={users} onClose={() => setRunForm(null)} onSaved={() => { setRunForm(null); refresh(); }} />
      : <AddRunsModal plan={plan} users={users} onClose={() => setRunForm(null)} onSaved={() => { setRunForm(null); refresh(); }} onChanged={refresh} />)}
    {teamRun && plan && <RunTeamModal plan={plan} run={teamRun} onClose={() => setTeamRun(null)} onSaved={() => { setTeamRun(null); refresh(); }} />}
    {cloneOpen && plan && <ClonePlanModal plan={plan} onClose={() => setCloneOpen(false)} onCloned={(created) => { setCloneOpen(false); onCloned(created); }} />}
    </>
  );
}

function PlanFormModal({ plan, projectId, defaultReleaseId, users, onClose, onSaved }: {
  plan?: PlanDetail; projectId: string; defaultReleaseId: string; users: UserLookup[];
  onClose: () => void; onSaved: (saved: PlanDetail, created: boolean) => void;
}) {
  const [releases, setReleases] = useState<ReleaseItem[]>([]);
  const [planName, setPlanName] = useState(plan?.planName ?? "");
  const [planCode, setPlanCode] = useState("");
  const [planType, setPlanType] = useState(plan?.planType ?? "Regression");
  const [releaseId, setReleaseId] = useState(plan?.releaseId ?? "");
  const [ownerUserId, setOwnerUserId] = useState(plan?.ownerUserId ?? "");
  const [startDate, setStartDate] = useState(dateOnly(plan?.startDate));
  const [endDate, setEndDate] = useState(dateOnly(plan?.endDate));
  const [notes, setNotes] = useState(plan?.notes ?? "");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    if (plan) return;
    const ctrl = new AbortController();
    // Release selector แสดงเฉพาะ Release ที่ไม่ Cancelled (UI_DESIGN_SYSTEM: Release selector — Active only rule)
    getJson<ReleaseItem[]>(`${apiUrl}/releases?projectId=${projectId}`, ctrl.signal)
      .then((rows) => {
        const active = rows.filter((x) => x.status !== "Cancelled");
        setReleases(active);
        setReleaseId((current) => current || (active.some((x) => x.releaseId === defaultReleaseId) ? defaultReleaseId : active[0]?.releaseId ?? ""));
      })
      .catch((e) => { if (!isAbortError(e)) setError(errorText(e, "โหลดรายการ Release ไม่สำเร็จ")); });
    return () => ctrl.abort();
  }, [plan, projectId, defaultReleaseId]);

  const missing = [!planName.trim() && "ชื่อแผน", !releaseId && "Release"].filter(Boolean) as string[];
  const dateError = startDate && endDate && endDate < startDate ? "วันสิ้นสุดต้องไม่ก่อนวันเริ่ม" : "";
  const save = async () => {
    setSaving(true); setError("");
    const body = { projectId, releaseId, planCode: plan ? plan.planCode : planCode.trim() || null, planName: planName.trim(), planType, startDate: startDate || null, endDate: endDate || null, ownerUserId: ownerUserId || null, notes: notes.trim() || null };
    try {
      const saved = await apiFetch<PlanDetail>(plan ? `/test-plans/${plan.testPlanId}` : "/test-plans", { method: plan ? "PUT" : "POST", json: body, fallbackMessage: "บันทึก Test Plan ไม่สำเร็จ" });
      notify(plan ? `บันทึก ${saved.planCode} แล้ว` : `สร้าง ${saved.planCode} แล้ว`, "success");
      onSaved(saved, !plan);
    } catch (e) { setError(errorText(e, "บันทึก Test Plan ไม่สำเร็จ")); }
    finally { setSaving(false); }
  };

  return (
    <ModalShell labelledBy="tp-form-title" className="tp-form" onDismiss={() => { if (!saving) onClose(); }}>
      <div className="modal-head"><div><h2 id="tp-form-title">{plan ? `แก้ไข ${plan.planCode}` : "สร้าง Test Plan"}</h2><small>{plan ? `Release ${plan.releaseCode ?? "-"} — เปลี่ยน Release และรหัสแผนไม่ได้` : "รวม Test Cycle ของ Release เดียวกันเป็นแผนเดียว แล้วเพิ่ม Run ภายหลัง"}</small></div><button disabled={saving} aria-label="ปิดหน้าต่าง" onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
      {error && <div className="inline-alert error" role="alert"><span>{error}</span></div>}
      <div className="form-grid">
        <label className="full">ชื่อแผน <span className="required">*</span><input value={planName} disabled={saving} onChange={(e) => setPlanName(e.target.value)} placeholder="เช่น Release 2026.11 Regression" maxLength={200} /></label>
        {!plan && <label>รหัสแผน<input value={planCode} disabled={saving} onChange={(e) => setPlanCode(e.target.value)} placeholder="เว้นว่าง = สร้างอัตโนมัติ" maxLength={100} /><small>เว้นว่างเพื่อให้ระบบสร้าง เช่น PRJ-TP-001</small></label>}
        <label>ประเภท<select value={planType} disabled={saving} onChange={(e) => setPlanType(e.target.value)}>{planTypes.map((t) => <option key={t} value={t}>{t}</option>)}</select></label>
        {!plan && <label>Release <span className="required">*</span><select value={releaseId} disabled={saving} onChange={(e) => setReleaseId(e.target.value)}><option value="">เลือก Release</option>{releases.map((r) => <option key={r.releaseId} value={r.releaseId}>{r.releaseCode}{r.version ? ` · ${r.version}` : ""}</option>)}</select></label>}
        <label>เจ้าของแผน<select value={ownerUserId} disabled={saving} onChange={(e) => setOwnerUserId(e.target.value)}><option value="">ไม่ระบุ</option>{users.map((u) => <option key={u.userId} value={u.userId}>{u.displayName}</option>)}</select></label>
        <label>วันเริ่ม<DateField value={startDate} onChange={setStartDate} disabled={saving} allowEmpty /></label>
        <label>วันสิ้นสุด<DateField value={endDate} onChange={setEndDate} disabled={saving} min={startDate || undefined} invalid={!!dateError} allowEmpty />{dateError && <small className="tp-field-error">{dateError}</small>}</label>
        <label className="full">หมายเหตุ<textarea rows={3} value={notes} disabled={saving} onChange={(e) => setNotes(e.target.value)} placeholder="เช่น ขอบเขตการทดสอบ เงื่อนไขปิดแผน" /></label>
      </div>
      <div className="modal-actions">
        <small className={`tp-form-status${missing.length || dateError ? " is-missing" : ""}`} role="status">{missing.length ? `ยังขาด: ${missing.join(", ")}` : dateError || "ข้อมูลครบ พร้อมบันทึก"}</small>
        <button className="btn" disabled={saving} onClick={onClose}>ยกเลิก</button>
        <button className="btn primary" disabled={saving || missing.length > 0 || !!dateError} onClick={save}>{saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : plan ? "บันทึก" : "สร้าง Test Plan"}</button>
      </div>
    </ModalShell>
  );
}

/** แก้ไข Run — เปลี่ยน Test Cycle/รหัส Run ไม่ได้ (เพิ่ม Run ใหม่ใช้ AddRunsModal) */
function RunFormModal({ plan, run, users, onClose, onSaved }: { plan: PlanDetail; run: PlanRun; users: UserLookup[]; onClose: () => void; onSaved: () => void }) {
  const initialConfig = useMemo(() => parseConfig(run.configurationJson), [run]);
  const [runName, setRunName] = useState(run.runName);
  const [configurationKey, setConfigurationKey] = useState(run.configurationKey ?? "");
  const [configurationName, setConfigurationName] = useState(run.configurationName ?? "");
  const [os, setOs] = useState(typeof initialConfig.os === "string" ? initialConfig.os : "");
  const [browser, setBrowser] = useState(typeof initialConfig.browser === "string" ? initialConfig.browser : "");
  const [device, setDevice] = useState(typeof initialConfig.device === "string" ? initialConfig.device : "");
  const [ownerUserId, setOwnerUserId] = useState(run.ownerUserId ?? "");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  const configJson = () => {
    const merged: Record<string, unknown> = { ...initialConfig };
    for (const [key, value] of [["os", os], ["browser", browser], ["device", device]] as const) { if (value.trim()) merged[key] = value.trim(); else delete merged[key]; }
    return Object.keys(merged).length ? merged : null;
  };
  const keyInvalid = configurationKey.trim() !== "" && !configKeyPattern.test(configurationKey.trim());
  const missing = runName.trim() ? [] : ["ชื่อ Run"];

  const save = async () => {
    setSaving(true); setError("");
    try {
      await apiFetch(`/test-plans/${plan.testPlanId}/runs/${run.testPlanRunId}`, { method: "PUT", json: { runName: runName.trim(), configurationKey: configurationKey.trim().toLowerCase() || null, configurationName: configurationName.trim() || null, configurationJson: configJson(), ownerUserId: ownerUserId || null }, fallbackMessage: "บันทึก Run ไม่สำเร็จ" });
      notify(`บันทึก ${run.runCode} แล้ว`, "success");
      onSaved();
    } catch (e) { setError(errorText(e, "บันทึก Run ไม่สำเร็จ")); }
    finally { setSaving(false); }
  };

  return (
    <ModalShell labelledBy="tp-run-title" className="tp-form tp-run-form" onDismiss={() => { if (!saving) onClose(); }}>
      <div className="modal-head"><div><h2 id="tp-run-title">แก้ไข Run {run.runCode}</h2><small>Test Cycle {run.cycleCode} · Build {run.buildNumber ?? "-"} · {run.environmentName ?? "-"}</small></div><button disabled={saving} aria-label="ปิดหน้าต่าง" onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
      {error && <div className="inline-alert error" role="alert"><span>{error}</span></div>}
      <div className="form-grid">
        <label className="full">ชื่อ Run <span className="required">*</span><input value={runName} disabled={saving} onChange={(e) => setRunName(e.target.value)} maxLength={200} /></label>
      </div>
      <h3 className="modal-section-title tp-config-title">Configuration</h3>
      <div className="form-grid">
        <label>Configuration Name<input value={configurationName} disabled={saving} onChange={(e) => setConfigurationName(e.target.value)} placeholder="เช่น Windows 11 · Chrome" maxLength={200} /></label>
        <label>Configuration Key<input value={configurationKey} disabled={saving} aria-invalid={keyInvalid || undefined} onChange={(e) => setConfigurationKey(e.target.value)} placeholder="เช่น windows-chrome" maxLength={100} /><small className={keyInvalid ? "tp-field-error" : undefined}>{keyInvalid ? "ใช้ได้เฉพาะ a-z, 0-9 และ - (เช่น windows-chrome)" : "ใช้กรอง/เทียบผลข้าม Run — ระบบเก็บเป็นตัวพิมพ์เล็ก"}</small></label>
        <label>OS<input value={os} disabled={saving} onChange={(e) => setOs(e.target.value)} placeholder="เช่น Windows 11" /></label>
        <label>Browser<input value={browser} disabled={saving} onChange={(e) => setBrowser(e.target.value)} placeholder="เช่น Chrome" /></label>
        <label>Device<input value={device} disabled={saving} onChange={(e) => setDevice(e.target.value)} placeholder="เช่น Desktop" /></label>
        <label>ผู้รับผิดชอบ Run<select value={ownerUserId} disabled={saving} onChange={(e) => setOwnerUserId(e.target.value)}><option value="">ไม่ระบุ</option>{users.map((u) => <option key={u.userId} value={u.userId}>{u.displayName}</option>)}</select></label>
      </div>
      <div className="modal-actions">
        <small className={`tp-form-status${missing.length || keyInvalid ? " is-missing" : ""}`} role="status">{missing.length ? `ยังขาด: ${missing.join(", ")}` : keyInvalid ? "Configuration Key ไม่ถูกต้อง" : "ข้อมูลครบ พร้อมบันทึก"}</small>
        <button className="btn" disabled={saving} onClick={onClose}>ยกเลิก</button>
        <button className="btn primary" disabled={saving || missing.length > 0 || keyInvalid} onClick={save}>{saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : "บันทึก"}</button>
      </div>
    </ModalShell>
  );
}

/** ค่าที่กรอกต่อ Run ในหน้าต่างเพิ่ม Run — รหัส/ชื่อ Run ว่าง = ใช้ของ Test Cycle */
type RunDraft = { name: string; key: string; os: string; browser: string; device: string; runCode: string; runName: string; more: boolean };
const emptyDraft = (): RunDraft => ({ name: "", key: "", os: "", browser: "", device: "", runCode: "", runName: "", more: false });
const configKeyPattern = /^[a-z0-9]+(-[a-z0-9]+)*$/i;
/** Key แนะนำจากชื่อ Configuration — "Windows 11 · Chrome" → "windows-11-chrome" (ชื่อที่ไม่มีอักษรอังกฤษ/ตัวเลขไม่มี Key แนะนำ) */
const suggestKey = (name: string) => name.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "").slice(0, 100);
const draftBody = (d: RunDraft, ownerUserId: string) => {
  const config: Record<string, string> = {};
  for (const [key, value] of [["os", d.os], ["browser", d.browser], ["device", d.device]] as const) if (value.trim()) config[key] = value.trim();
  return { runCode: d.runCode.trim() || null, runName: d.runName.trim() || null, configurationKey: (d.key.trim() || suggestKey(d.name)).toLowerCase() || null, configurationName: d.name.trim() || null, configurationJson: Object.keys(config).length ? config : null, ownerUserId: ownerUserId || null, cycleName: null };
};

function RunDraftCard({ title, meta, draft, disabled, codePlaceholder, namePlaceholder, onChange, onRemove }: {
  title: string; meta: string; draft: RunDraft; disabled: boolean; codePlaceholder: string; namePlaceholder: string; onChange: (draft: RunDraft) => void; onRemove?: () => void;
}) {
  const keyInvalid = draft.key.trim() !== "" && !configKeyPattern.test(draft.key.trim());
  const suggested = suggestKey(draft.name);
  const set = (patch: Partial<RunDraft>) => onChange({ ...draft, ...patch });
  const extraCount = [draft.os, draft.browser, draft.device, draft.runCode, draft.runName].filter((x) => x.trim()).length;
  return (
    <li className={`tp-draft${keyInvalid ? " is-invalid" : ""}`}>
      <div className="tp-draft-head">
        <span><b>{title}</b><small>{meta}</small></span>
        {onRemove && <button type="button" className="tp-draft-remove" disabled={disabled} aria-label={`นำ ${title} ออก`} title="นำออก" onClick={onRemove}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>}
      </div>
      <div className="tp-draft-grid">
        <label><span>Configuration Name</span><input value={draft.name} disabled={disabled} onChange={(e) => set({ name: e.target.value })} placeholder="เช่น Windows 11 · Chrome" maxLength={200} /></label>
        <label><span>Configuration Key</span><input value={draft.key} disabled={disabled} aria-invalid={keyInvalid || undefined} onChange={(e) => set({ key: e.target.value })} placeholder={suggested ? `อัตโนมัติ: ${suggested}` : "เช่น windows-chrome"} maxLength={100} /></label>
      </div>
      {keyInvalid && <small className="tp-field-error">Key ใช้ได้เฉพาะ a-z, 0-9 และ - (เช่น windows-chrome)</small>}
      <button type="button" className="tp-draft-more" aria-expanded={draft.more} disabled={disabled} onClick={() => set({ more: !draft.more })}>
        <span className="material-symbols-outlined" aria-hidden="true">tune</span>OS / Browser / Device · รหัส–ชื่อ Run{extraCount > 0 && !draft.more ? ` (กรอกแล้ว ${extraCount})` : ""}
        <span className="material-symbols-outlined" aria-hidden="true">expand_more</span>
      </button>
      {draft.more && <div className="tp-draft-grid is-extra">
        <label><span>OS</span><input value={draft.os} disabled={disabled} onChange={(e) => set({ os: e.target.value })} placeholder="เช่น Windows 11" /></label>
        <label><span>Browser</span><input value={draft.browser} disabled={disabled} onChange={(e) => set({ browser: e.target.value })} placeholder="เช่น Chrome" /></label>
        <label><span>Device</span><input value={draft.device} disabled={disabled} onChange={(e) => set({ device: e.target.value })} placeholder="เช่น Desktop" /></label>
        <label><span>รหัส Run</span><input value={draft.runCode} disabled={disabled} onChange={(e) => set({ runCode: e.target.value })} placeholder={codePlaceholder} maxLength={100} /></label>
        <label className="is-wide"><span>ชื่อ Run</span><input value={draft.runName} disabled={disabled} onChange={(e) => set({ runName: e.target.value })} placeholder={namePlaceholder} maxLength={200} /></label>
      </div>}
    </li>
  );
}

/** เพิ่ม Run หน้าจอเดียว — ซ้าย: เลือก Test Cycle ได้หลายรายการ (หรือสร้างจาก Suite 1 ชุด) | ขวา: Configuration ต่อ Run */
function AddRunsModal({ plan, users, onClose, onSaved, onChanged }: { plan: PlanDetail; users: UserLookup[]; onClose: () => void; onSaved: () => void; onChanged: () => void }) {
  const [mode, setMode] = useState<"attach" | "suite">("attach");
  const [cycles, setCycles] = useState<CycleRow[]>([]);
  const [suites, setSuites] = useState<SuiteOption[]>([]);
  const [builds, setBuilds] = useState<BuildItem[]>([]);
  const [environments, setEnvironments] = useState<EnvironmentOption[]>([]);
  const [optionsLoading, setOptionsLoading] = useState(true);
  const [query, setQuery] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [buildFilter, setBuildFilter] = useState("");
  const [envFilter, setEnvFilter] = useState("");
  const [moduleFilter, setModuleFilter] = useState("");
  const [selected, setSelected] = useState<string[]>([]);
  const [drafts, setDrafts] = useState<Record<string, RunDraft>>({});
  const [suiteId, setSuiteId] = useState("");
  const [buildId, setBuildId] = useState("");
  const [environmentId, setEnvironmentId] = useState("");
  const [requiredOnly, setRequiredOnly] = useState(false);
  const [suiteDraft, setSuiteDraft] = useState<RunDraft>(emptyDraft);
  const [ownerUserId, setOwnerUserId] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [failures, setFailures] = useState<{ code: string; message: string }[]>([]);

  useEffect(() => {
    const ctrl = new AbortController();
    // plan=none: Server ส่งเฉพาะ Cycle ที่ยังไม่อยู่ใน Test Plan ใด (1 Cycle อยู่ได้ 1 แผน) — เกิน 500 รายการขอหน้าที่เหลือพร้อมกัน
    const cycleUrl = (page: number) => `${apiUrl}/test-cycles?projectId=${plan.projectId}&releaseId=${plan.releaseId}&plan=none&size=500&page=${page}`;
    const loadCycles = async () => {
      const first = await getJson<CycleListResult>(cycleUrl(1), ctrl.signal);
      const pages = Math.ceil(first.items.total / 500);
      const rest = await Promise.all(Array.from({ length: Math.max(0, pages - 1) }, (_, i) => getJson<CycleListResult>(cycleUrl(i + 2), ctrl.signal)));
      return [first, ...rest].flatMap((r) => r.items.rows);
    };
    // ตัวเลือกทั้งหมดโหลดพร้อมกันครั้งเดียว (ไม่ยิงต่อแถว)
    Promise.all([
      loadCycles(),
      getJson<{ rows: SuiteOption[] }>(`${apiUrl}/test-suites?projectId=${plan.projectId}&isActive=true&size=500&page=1`, ctrl.signal),
      getJson<BuildItem[]>(`${apiUrl}/releases/${plan.releaseId}/builds`, ctrl.signal),
      getJson<EnvironmentOption[]>(`${apiUrl}/test-environments?projectId=${plan.projectId}`, ctrl.signal),
    ]).then(([cycleRows, suiteRows, buildRows, environmentRows]) => {
      setCycles(cycleRows.map((x) => ({ ...x, modules: x.modules ?? [] })));
      setSuites(suiteRows.rows);
      setBuilds(buildRows);
      setEnvironments(environmentRows.filter((x) => x.isActive));
    }).catch((e) => { if (!isAbortError(e)) setError(errorText(e, "โหลดตัวเลือกไม่สำเร็จ")); })
      .finally(() => { if (!ctrl.signal.aborted) setOptionsLoading(false); });
    return () => ctrl.abort();
  }, [plan]);

  const cycleById = useMemo(() => new Map(cycles.map((c) => [c.testCycleId, c])), [cycles]);
  const statusOptions = useMemo(() => distinctCounts(cycles.map((c) => [c.status, runStatusLabel(c.status)])), [cycles]);
  const buildOptions = useMemo(() => distinctCounts(cycles.map((c) => [c.buildId, c.buildNumber])), [cycles]);
  const envOptions = useMemo(() => distinctCounts(cycles.map((c) => [c.environmentId, c.environmentName])), [cycles]);
  const moduleOptions = useMemo(() => distinctCounts(cycles.flatMap((c) => c.modules.map((m): [string, string] => [m.moduleId, `${m.moduleCode} · ${m.moduleName}`]))), [cycles]);
  const q = query.trim().toLowerCase();
  const visible = cycles.filter((c) => (!statusFilter || c.status === statusFilter) && (!buildFilter || c.buildId === buildFilter) && (!envFilter || c.environmentId === envFilter)
    && (!moduleFilter || c.modules.some((m) => m.moduleId === moduleFilter))
    && (!q || [c.cycleCode, c.cycleName, c.suiteName, c.environmentName, c.buildNumber, ...c.modules.flatMap((m) => [m.moduleCode, m.moduleName])].filter(Boolean).join(" ").toLowerCase().includes(q)));
  const filterCount = [statusFilter, buildFilter, envFilter, moduleFilter, q].filter(Boolean).length;
  const clearCycleFilters = () => { setStatusFilter(""); setBuildFilter(""); setEnvFilter(""); setModuleFilter(""); setQuery(""); };
  const allVisibleSelected = visible.length > 0 && visible.every((c) => selected.includes(c.testCycleId));
  const select = (ids: string[], on: boolean) => {
    setSelected((current) => on ? [...current, ...ids.filter((id) => !current.includes(id))] : current.filter((id) => !ids.includes(id)));
    if (on) setDrafts((current) => ({ ...Object.fromEntries(ids.map((id) => [id, emptyDraft()])), ...current }));
  };
  const cycleMeta = (c: CycleRow) => [c.cycleName, c.environmentName, `Build ${c.buildNumber}`, `${c.caseCount.toLocaleString()} เคส`].join(" · ");
  const suite = suites.find((x) => x.testSuiteId === suiteId);

  const activeDrafts = mode === "attach" ? selected.map((id) => ({ code: cycleById.get(id)?.cycleCode ?? "", draft: drafts[id] ?? emptyDraft() })) : [{ code: "", draft: suiteDraft }];
  const invalidKeys = activeDrafts.filter(({ draft }) => draft.key.trim() && !configKeyPattern.test(draft.key.trim())).length;
  const runCodes = activeDrafts.map(({ code, draft }) => (draft.runCode.trim() || code).toUpperCase()).filter(Boolean);
  const existingCodes = new Set(plan.runs.map((r) => r.runCode.toUpperCase()));
  const duplicateCode = runCodes.find((c, i) => runCodes.indexOf(c) !== i || existingCodes.has(c));
  const missing = (mode === "attach" ? [!selected.length && "Test Cycle อย่างน้อย 1 รายการ"] : [!suiteId && "Test Suite", !buildId && "Build", !environmentId && "Environment"]).filter(Boolean) as string[];
  const problem = missing.length ? `ยังขาด: ${missing.join(", ")}` : invalidKeys ? `Configuration Key ไม่ถูกต้อง ${invalidKeys} รายการ` : duplicateCode ? `รหัส Run ซ้ำ: ${duplicateCode}` : "";
  const runCount = mode === "attach" ? selected.length : 1;

  const save = async () => {
    setSaving(true); setError(""); setFailures([]);
    try {
      if (mode === "suite") {
        await apiFetch(`/test-plans/${plan.testPlanId}/runs`, { method: "POST", json: { ...draftBody(suiteDraft, ownerUserId), createCycleFromSuite: true, testCycleId: null, testSuiteId: suiteId, buildId, environmentId, requiredOnly }, fallbackMessage: "เพิ่ม Run ไม่สำเร็จ" });
        notify("สร้าง Test Cycle และเพิ่ม Run แล้ว", "success");
        onSaved();
        return;
      }
      // API รับทีละ Cycle — ส่งครั้งละ 4 คำขอ (แบบเดียวกับลบหลาย Test Suite); รายการที่ไม่สำเร็จคงไว้ให้แก้แล้วลองใหม่
      const ids = [...selected];
      const failed: { id: string; code: string; message: string }[] = [];
      for (let i = 0; i < ids.length; i += 4) {
        const chunk = ids.slice(i, i + 4);
        const results = await Promise.allSettled(chunk.map((id) => apiFetch(`/test-plans/${plan.testPlanId}/runs`, { method: "POST", json: { ...draftBody(drafts[id] ?? emptyDraft(), ownerUserId), createCycleFromSuite: false, testCycleId: id, testSuiteId: null, buildId: null, environmentId: null, requiredOnly: false }, fallbackMessage: "เพิ่ม Run ไม่สำเร็จ" })));
        results.forEach((result, j) => { if (result.status === "rejected") failed.push({ id: chunk[j], code: cycleById.get(chunk[j])?.cycleCode ?? "", message: errorText(result.reason, "เพิ่ม Run ไม่สำเร็จ") }); });
      }
      const added = ids.length - failed.length;
      if (!failed.length) { notify(`เพิ่ม ${added.toLocaleString()} Run แล้ว`, "success"); onSaved(); return; }
      const failedIds = new Set(failed.map((f) => f.id));
      setSelected(ids.filter((id) => failedIds.has(id)));
      setCycles((current) => current.filter((c) => failedIds.has(c.testCycleId) || !ids.includes(c.testCycleId)));
      setFailures(failed.map(({ code, message }) => ({ code, message })));
      if (added) { notify(`เพิ่ม ${added.toLocaleString()} Run แล้ว · ไม่สำเร็จ ${failed.length.toLocaleString()} รายการ`, "info"); onChanged(); }
    } catch (e) { setError(errorText(e, "เพิ่ม Run ไม่สำเร็จ")); }
    finally { setSaving(false); }
  };

  return (
    <ModalShell labelledBy="tp-add-title" className="tp-form tp-add-runs" onDismiss={() => { if (!saving) onClose(); }}>
      <div className="modal-head"><div><h2 id="tp-add-title">เพิ่ม Run</h2><small>{plan.planCode} · Release {plan.releaseCode ?? "-"} — 1 Run = 1 Test Cycle, Build/Environment ใช้ค่าจาก Test Cycle</small></div><button disabled={saving} aria-label="ปิดหน้าต่าง" onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
      {error && <div className="inline-alert error" role="alert"><span>{error}</span></div>}
      {failures.length > 0 && <div className="inline-alert error tp-add-failures" role="alert"><span><b>เพิ่มไม่สำเร็จ {failures.length.toLocaleString()} รายการ</b> — รายการด้านล่างยังเลือกไว้ แก้แล้วกด “เพิ่ม Run” อีกครั้ง หรือนำออก<ul>{failures.map((f) => <li key={f.code}><b>{f.code}</b> {f.message}</li>)}</ul></span></div>}
      <div className="tp-add-mode" role="radiogroup" aria-label="ที่มาของ Test Cycle">
        <label className={mode === "attach" ? "is-selected" : undefined}><input type="radio" name="tp-add-mode" checked={mode === "attach"} disabled={saving} onChange={() => setMode("attach")} /><span className="material-symbols-outlined" aria-hidden="true">checklist</span><span><b>เลือก Cycle ที่มีอยู่</b><small>เลือกได้หลายรายการ · ไม่คัดลอกผลการทดสอบ</small></span></label>
        <label className={mode === "suite" ? "is-selected" : undefined}><input type="radio" name="tp-add-mode" checked={mode === "suite"} disabled={saving} onChange={() => setMode("suite")} /><span className="material-symbols-outlined" aria-hidden="true">add_circle</span><span><b>สร้าง Cycle ใหม่จาก Suite</b><small>สร้าง Test Cycle (Draft) 1 ชุดแล้วเพิ่มเข้าแผน</small></span></label>
      </div>
      <div className="tp-add-body">
        <section className="tp-add-panel" aria-labelledby="tp-add-source-title">
          {mode === "attach" ? <>
            <div className="tp-add-panel-head">
              <h3 id="tp-add-source-title">Test Cycle ที่เพิ่มได้ <span className="cyd-count">{cycles.length.toLocaleString()}</span></h3>
              <small className="tp-add-panel-note">เฉพาะ Cycle ของ Release นี้ที่ยังไม่อยู่ใน Test Plan ใด</small>
            </div>
            {cycles.length > 0 && <>
              <label className="tp-add-search"><span className="material-symbols-outlined" aria-hidden="true">search</span><input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="ค้นหาชื่อ, รหัส, Suite หรือ Module" aria-label="ค้นหา Test Cycle" /></label>
              {statusOptions.length > 1 && <div className="tp-add-chips" role="group" aria-label="กรองตามสถานะ Test Cycle">
                <button type="button" aria-pressed={!statusFilter} onClick={() => setStatusFilter("")}>ทั้งหมด <b>{cycles.length.toLocaleString()}</b></button>
                {statusOptions.map((x) => <button key={x.id} type="button" aria-pressed={statusFilter === x.id} onClick={() => setStatusFilter((c) => c === x.id ? "" : x.id)}>{x.label} <b>{x.count.toLocaleString()}</b></button>)}
              </div>}
              {(buildOptions.length > 1 || envOptions.length > 1 || moduleOptions.length > 1) && <div className="tp-add-filters" role="group" aria-label="ตัวกรอง Test Cycle">
                {moduleOptions.length > 1 && <select aria-label="Module" value={moduleFilter} onChange={(e) => setModuleFilter(e.target.value)}><option value="">ทุก Module</option>{moduleOptions.map((x) => <option key={x.id} value={x.id}>{x.label} ({x.count})</option>)}</select>}
                {envOptions.length > 1 && <select aria-label="Environment" value={envFilter} onChange={(e) => setEnvFilter(e.target.value)}><option value="">ทุก Environment</option>{envOptions.map((x) => <option key={x.id} value={x.id}>{x.label} ({x.count})</option>)}</select>}
                {buildOptions.length > 1 && <select aria-label="Build" value={buildFilter} onChange={(e) => setBuildFilter(e.target.value)}><option value="">ทุก Build</option>{buildOptions.map((x) => <option key={x.id} value={x.id}>Build {x.label} ({x.count})</option>)}</select>}
              </div>}
              <div className="tp-add-listbar">
                <span>แสดง <b>{visible.length.toLocaleString()}</b>{filterCount ? ` จาก ${cycles.length.toLocaleString()}` : ""} · เลือกแล้ว <b>{selected.length.toLocaleString()}</b></span>
                {filterCount > 0 && <button type="button" className="tp-text-btn" onClick={clearCycleFilters}>ล้างตัวกรอง</button>}
                {visible.length > 0 && <button type="button" className="tp-text-btn" disabled={saving} onClick={() => select(visible.map((c) => c.testCycleId), !allVisibleSelected)}>{allVisibleSelected ? "ยกเลิกที่แสดงอยู่" : filterCount ? `เลือกทั้ง ${visible.length.toLocaleString()} รายการที่แสดง` : "เลือกทั้งหมด"}</button>}
              </div>
            </>}
            <ul className="tp-cycle-list" aria-label="Test Cycle ที่เพิ่มได้">
              {optionsLoading ? <li className="tp-add-empty" role="status"><span className="spinner inline" aria-hidden="true" /> กำลังโหลด Test Cycle...</li>
                : !cycles.length ? <li className="tp-add-empty">ไม่มี Test Cycle ที่เพิ่มได้ใน Release นี้ (ทุก Cycle อยู่ใน Test Plan แล้ว หรือยังไม่มี Cycle) — เลือก “สร้าง Cycle ใหม่จาก Suite” ด้านบน</li>
                  : !visible.length ? <li className="tp-add-empty">ไม่พบ Test Cycle ตามตัวกรอง</li>
                    : visible.map((c) => {
                      const on = selected.includes(c.testCycleId);
                      const modules = c.modules.map((m) => m.moduleName).join(", ");
                      return <li key={c.testCycleId}><label className={on ? "is-selected" : undefined} title={c.cycleName}>
                        <input type="checkbox" checked={on} disabled={saving} onChange={(e) => select([c.testCycleId], e.target.checked)} />
                        <span className="tp-cycle-text">
                          <span className="tp-cycle-name">{c.cycleName}</span>
                          <span className="tp-cycle-sub"><b>{c.cycleCode}</b>{c.suiteName && <span>Suite {c.suiteName}</span>}</span>
                          <span className="tp-cycle-tags">
                            {modules && <span title="Module"><span className="material-symbols-outlined" aria-hidden="true">account_tree</span>{modules}</span>}
                            <span title="Environment"><span className="material-symbols-outlined" aria-hidden="true">dns</span>{c.environmentName}</span>
                            <span title="Build"><span className="material-symbols-outlined" aria-hidden="true">deployed_code</span>{c.buildNumber}</span>
                          </span>
                        </span>
                        <span className="tp-cycle-side">
                          <Badge tone={runStatusInfo[c.status]?.tone ?? "blue"}>{runStatusLabel(c.status)}</Badge>
                          <small><b>{c.caseCount.toLocaleString()}</b> เคส{c.executedCount > 0 ? ` · ${Math.round(c.progressPercent)}%` : ""}</small>
                        </span>
                      </label></li>;
                    })}
            </ul>
          </> : <>
            <div className="tp-add-panel-head"><h3 id="tp-add-source-title">Test Cycle ใหม่</h3></div>
            <div className="tp-add-fields">
              <label><span>Test Suite <span className="required">*</span></span><select value={suiteId} disabled={saving || optionsLoading} onChange={(e) => setSuiteId(e.target.value)}><option value="">เลือก Test Suite</option>{suites.map((x) => <option key={x.testSuiteId} value={x.testSuiteId}>{x.suiteCode} · {x.suiteName} ({x.caseCount} เคส)</option>)}</select></label>
              <label><span>Build <span className="required">*</span></span><select value={buildId} disabled={saving || optionsLoading} onChange={(e) => setBuildId(e.target.value)}><option value="">เลือก Build</option>{builds.filter((b) => b.isActive).map((b) => <option key={b.buildId} value={b.buildId}>{b.buildNumber}{b.applicationVersion ? ` · App ${b.applicationVersion}` : ""}</option>)}</select></label>
              <label><span>Environment <span className="required">*</span></span><select value={environmentId} disabled={saving || optionsLoading} onChange={(e) => setEnvironmentId(e.target.value)}><option value="">เลือก Environment</option>{environments.map((x) => <option key={x.testEnvironmentId} value={x.testEnvironmentId}>{x.environmentName}</option>)}</select></label>
              <label className="tp-add-check"><input type="checkbox" checked={requiredOnly} disabled={saving} onChange={(e) => setRequiredOnly(e.target.checked)} /><span>เฉพาะ Test Case ที่เป็น Required ใน Suite</span></label>
            </div>
          </>}
        </section>
        <section className="tp-add-panel" aria-labelledby="tp-add-runs-title">
          <div className="tp-add-panel-head">
            <h3 id="tp-add-runs-title">Run ที่จะเพิ่ม <span className="cyd-count">{runCount.toLocaleString()}</span></h3>
            <label className="tp-add-owner"><span>ผู้รับผิดชอบ{runCount > 1 ? " (ทุก Run)" : ""}</span><select value={ownerUserId} disabled={saving} onChange={(e) => setOwnerUserId(e.target.value)}><option value="">ไม่ระบุ</option>{users.map((u) => <option key={u.userId} value={u.userId}>{u.displayName}</option>)}</select></label>
          </div>
          {mode === "attach" && !selected.length
            ? <div className="tp-add-empty is-guide"><span className="material-symbols-outlined" aria-hidden="true">arrow_back</span><span>ติ๊กเลือก Test Cycle ทางซ้าย (ได้หลายรายการ) แล้วตั้งชื่อ Configuration ของแต่ละ Run ที่นี่ เช่น “Windows 11 · Chrome”</span></div>
            : <ul className="tp-drafts">
              {mode === "attach" ? selected.map((id) => {
                const c = cycleById.get(id);
                return c && <RunDraftCard key={id} title={c.cycleCode} meta={cycleMeta(c)} draft={drafts[id] ?? emptyDraft()} disabled={saving} codePlaceholder={c.cycleCode} namePlaceholder={c.cycleName}
                  onChange={(draft) => setDrafts((current) => ({ ...current, [id]: draft }))} onRemove={() => select([id], false)} />;
              }) : <RunDraftCard title="Run ใหม่" meta={suite ? `${suite.suiteCode} · ${suite.suiteName} (${suite.caseCount} เคส)` : "เลือก Test Suite ทางซ้าย"} draft={suiteDraft} disabled={saving} codePlaceholder="เว้นว่าง = ใช้รหัส Test Cycle ที่สร้าง" namePlaceholder="เว้นว่าง = ใช้ชื่อ Test Cycle" onChange={setSuiteDraft} />}
            </ul>}
        </section>
      </div>
      <div className="modal-actions">
        <small className={`tp-form-status${problem ? " is-missing" : ""}`} role="status">{problem || (mode === "attach" ? `พร้อมเพิ่ม ${runCount.toLocaleString()} Run` : "ข้อมูลครบ พร้อมสร้าง")}</small>
        <button className="btn" disabled={saving} onClick={onClose}>ยกเลิก</button>
        <button className="btn primary" disabled={saving || optionsLoading || !!problem} onClick={save}>{saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังเพิ่ม...</> : mode === "suite" ? "สร้าง Cycle และเพิ่ม Run" : runCount ? `เพิ่ม ${runCount.toLocaleString()} Run` : "เพิ่ม Run"}</button>
      </div>
    </ModalShell>
  );
}

/** ทีมระดับ Run (แทนที่ทั้งชุด) + แบ่งเคส NotRun ที่ยังไม่มีผู้รับผิดชอบให้ผู้ทดสอบแบบวนทีละคน — ไม่เขียนทับเคสที่มีผู้รับผิดชอบแล้ว */
function RunTeamModal({ plan, run, onClose, onSaved }: { plan: PlanDetail; run: PlanRun; onClose: () => void; onSaved: () => void }) {
  const [members, setMembers] = useState<PlanMember[]>([]);
  const [loading, setLoading] = useState(true);
  const [roles, setRoles] = useState<Record<string, string>>(() => Object.fromEntries(run.assignments.map((m) => [m.testerUserId, m.role])));
  const [query, setQuery] = useState("");
  const [bulk, setBulk] = useState(run.unassignedCases > 0);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    const ctrl = new AbortController();
    getJson<PlanMember[]>(`${apiUrl}/test-plans/${plan.testPlanId}/members`, ctrl.signal)
      .then((list) => {
        setMembers(list);
        // คนที่ออกจาก Project แล้วไม่อยู่ในรายการให้ติ๊กออก — ตัดทิ้งเลย ไม่งั้น Server ปฏิเสธทุกครั้งที่บันทึก (บันทึกแทนทีมทั้งชุด)
        const ids = new Set(list.map((m) => m.userId));
        setRoles((current) => Object.fromEntries(Object.entries(current).filter(([userId]) => ids.has(userId))));
      })
      .catch((e) => { if (!isAbortError(e)) setError(errorText(e, "โหลดสมาชิก Project ไม่สำเร็จ")); })
      .finally(() => { if (!ctrl.signal.aborted) setLoading(false); });
    return () => ctrl.abort();
  }, [plan.testPlanId]);

  const selected = Object.entries(roles);
  const testerCount = selected.filter(([, role]) => role === "Tester").length;
  const visible = members.filter((m) => !query.trim() || m.displayName.toLowerCase().includes(query.trim().toLowerCase()));
  const toggle = (userId: string, on: boolean) => setRoles((current) => { const next = { ...current }; if (on) next[userId] = next[userId] ?? "Tester"; else delete next[userId]; return next; });
  const bulkBlocked = bulk && testerCount === 0;

  const save = async () => {
    setSaving(true); setError("");
    try {
      const result = await apiFetch<{ assignedCases: number }>(`/test-plans/${plan.testPlanId}/runs/${run.testPlanRunId}/assign`, { method: "POST", json: { assignments: selected.map(([testerUserId, role]) => ({ testerUserId, role })), bulkAssignUnassignedCases: bulk }, fallbackMessage: "บันทึกทีมไม่สำเร็จ" });
      notify(`บันทึกทีมของ ${run.runCode} แล้ว${bulk ? ` · แบ่งเคสให้ผู้ทดสอบ ${result.assignedCases.toLocaleString()} เคส` : ""}`, "success");
      onSaved();
    } catch (e) { setError(errorText(e, "บันทึกทีมไม่สำเร็จ")); }
    finally { setSaving(false); }
  };

  return (
    <ModalShell labelledBy="tp-team-title" className="tp-form tp-team-form" onDismiss={() => { if (!saving) onClose(); }}>
      <div className="modal-head"><div><h2 id="tp-team-title">จัดทีม {run.runCode}</h2><small>{run.runName} — เลือกสมาชิก Project และบทบาท (บันทึกแทนทีมเดิมทั้งชุด)</small></div><button disabled={saving} aria-label="ปิดหน้าต่าง" onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
      {error && <div className="inline-alert error" role="alert"><span>{error}</span></div>}
      {members.length > 8 && <label className="tp-member-search"><span className="material-symbols-outlined" aria-hidden="true">search</span><input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="ค้นหาชื่อ" aria-label="ค้นหาสมาชิก" /></label>}
      <div className="tp-members" role="group" aria-label="สมาชิก Project">
        {loading ? <div className="cyd-state" role="status"><span className="spinner inline" aria-hidden="true" /> กำลังโหลดสมาชิก...</div>
          : !members.length ? <div className="cyd-state">ยังไม่มีสมาชิกใน Project นี้ — เพิ่มสมาชิกที่หน้า User / Role</div>
          : visible.map((m) => {
            const role = roles[m.userId];
            return <div key={m.userId} className={`tp-member${role ? " is-selected" : ""}`}>
              <label><input type="checkbox" checked={!!role} disabled={saving} onChange={(e) => toggle(m.userId, e.target.checked)} /><span>{m.displayName}</span></label>
              <select aria-label={`บทบาทของ ${m.displayName}`} value={role ?? "Tester"} disabled={saving || !role} onChange={(e) => setRoles((current) => ({ ...current, [m.userId]: e.target.value }))}>
                {runRoles.map((r) => <option key={r} value={r}>{runRoleLabel[r]} ({r})</option>)}
              </select>
            </div>;
          })}
      </div>
      <label className="tp-check tp-bulk">
        <input type="checkbox" checked={bulk} disabled={saving || run.unassignedCases === 0} onChange={(e) => setBulk(e.target.checked)} />
        <span>แบ่งเคสที่ยังไม่เริ่มและยังไม่มีผู้รับผิดชอบ ({run.unassignedCases.toLocaleString()} เคส) ให้ผู้ทดสอบแบบวนทีละคน<small>เคสที่มีผู้รับผิดชอบแล้วจะไม่ถูกเปลี่ยน · ผู้ทดสอบเห็นงานในหน้า My Work</small></span>
      </label>
      <div className="modal-actions">
        <small className={`tp-form-status${bulkBlocked ? " is-missing" : ""}`} role="status">{bulkBlocked ? "ยังขาด: ผู้ทดสอบ (Tester) อย่างน้อย 1 คนเพื่อแบ่งเคส" : `เลือก ${selected.length} คน · ผู้ทดสอบ ${testerCount} คน`}</small>
        <button className="btn" disabled={saving} onClick={onClose}>ยกเลิก</button>
        <button className="btn primary" disabled={saving || loading || bulkBlocked} onClick={save}>{saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : "บันทึกทีม"}</button>
      </div>
    </ModalShell>
  );
}

/** Clone/Rerun Plan (§4.4): เลือก Release + Build ครั้งเดียวทั้งแผน, Environment เดิมเป็นค่าเริ่มต้น เปลี่ยนได้ราย Run — Preview ทุก Run ก่อนยืนยัน */
function ClonePlanModal({ plan, onClose, onCloned }: { plan: PlanDetail; onClose: () => void; onCloned: (created: PlanDetail) => void }) {
  const [releases, setReleases] = useState<ReleaseItem[]>([]);
  const [environments, setEnvironments] = useState<EnvironmentOption[]>([]);
  const [builds, setBuilds] = useState<BuildItem[]>([]);
  const [releaseId, setReleaseId] = useState("");
  const [buildId, setBuildId] = useState("");
  const [planName, setPlanName] = useState(`${plan.planName} (Rerun)`);
  const [targets, setTargets] = useState<Record<string, string>>(() => Object.fromEntries(plan.runs.map((r) => [r.testPlanRunId, r.environmentId])));
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    const ctrl = new AbortController();
    Promise.all([
      getJson<ReleaseItem[]>(`${apiUrl}/releases?projectId=${plan.projectId}`, ctrl.signal),
      getJson<EnvironmentOption[]>(`${apiUrl}/test-environments?projectId=${plan.projectId}`, ctrl.signal),
    ]).then(([releaseRows, environmentRows]) => {
      // Test Cycle สร้างใน Release ที่ปล่อยแล้ว/ยกเลิกไม่ได้ จึงแสดงเฉพาะ Release ที่ยังใช้งาน
      const open = releaseRows.filter((x) => x.status !== "Cancelled" && x.status !== "Released");
      setReleases(open);
      setReleaseId(open.some((x) => x.releaseId === plan.releaseId) ? plan.releaseId : open[0]?.releaseId ?? "");
      setEnvironments(environmentRows.filter((x) => x.isActive));
    }).catch((e) => { if (!isAbortError(e)) setError(errorText(e, "โหลดตัวเลือกไม่สำเร็จ")); })
      .finally(() => { if (!ctrl.signal.aborted) setLoading(false); });
    return () => ctrl.abort();
  }, [plan.projectId, plan.releaseId]);

  useEffect(() => {
    setBuildId("");
    if (!releaseId) { setBuilds([]); return; }
    const ctrl = new AbortController();
    getJson<BuildItem[]>(`${apiUrl}/releases/${releaseId}/builds`, ctrl.signal)
      .then((rows) => setBuilds(rows.filter((x) => x.isActive)))
      .catch((e) => { if (!isAbortError(e)) setError(errorText(e, "โหลด Build ไม่สำเร็จ")); });
    return () => ctrl.abort();
  }, [releaseId]);

  const activeEnvironmentIds = new Set(environments.map((x) => x.testEnvironmentId));
  const missingEnvironment = plan.runs.filter((r) => !activeEnvironmentIds.has(targets[r.testPlanRunId]));
  const missing = [!releaseId && "Release", !buildId && "Build", !planName.trim() && "ชื่อแผนใหม่", ...(loading ? [] : missingEnvironment.map((r) => `Environment ของ ${r.runCode}`))].filter(Boolean) as string[];

  const save = async () => {
    setSaving(true); setError("");
    try {
      const environmentOverrides = plan.runs.filter((r) => targets[r.testPlanRunId] !== r.environmentId).map((r) => ({ sourceRunId: r.testPlanRunId, targetEnvironmentId: targets[r.testPlanRunId] }));
      const created = await apiFetch<PlanDetail>(`/test-plans/${plan.testPlanId}/clone`, { method: "POST", json: { targetReleaseId: releaseId, targetBuildId: buildId, cloneMode: "SourceSnapshot", environmentOverrides, planName: planName.trim(), planCode: null }, fallbackMessage: "Clone Test Plan ไม่สำเร็จ" });
      notify(`สร้าง ${created.planCode} จาก ${plan.planCode} แล้ว (${created.runs.length} Run)`, "success");
      onCloned(created);
    } catch (e) { setError(errorText(e, "Clone Test Plan ไม่สำเร็จ")); }
    finally { setSaving(false); }
  };

  return (
    <ModalShell labelledBy="tp-clone-title" className="tp-form tp-clone-form" onDismiss={() => { if (!saving) onClose(); }}>
      <div className="modal-head"><div><h2 id="tp-clone-title">Clone / Rerun {plan.planCode}</h2><small>สร้างแผนใหม่สำหรับ Build ใหม่ — แผนเดิมไม่ถูกแก้ไข</small></div><button disabled={saving} aria-label="ปิดหน้าต่าง" onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
      {error && <div className="inline-alert error" role="alert"><span>{error}</span></div>}
      <div className="form-grid">
        <label className="full">ชื่อแผนใหม่ <span className="required">*</span><input value={planName} disabled={saving} onChange={(e) => setPlanName(e.target.value)} maxLength={200} /></label>
        <label>Release <span className="required">*</span><select value={releaseId} disabled={saving || loading} onChange={(e) => setReleaseId(e.target.value)}><option value="">เลือก Release</option>{releases.map((r) => <option key={r.releaseId} value={r.releaseId}>{r.releaseCode}{r.version ? ` · ${r.version}` : ""}</option>)}</select></label>
        <label>Build เป้าหมาย <span className="required">*</span><select value={buildId} disabled={saving || !releaseId} onChange={(e) => setBuildId(e.target.value)}><option value="">{releaseId && !builds.length ? "ไม่มี Build ที่ใช้งานใน Release นี้" : "เลือก Build"}</option>{builds.map((b) => <option key={b.buildId} value={b.buildId}>{b.buildNumber}{b.applicationVersion ? ` · App ${b.applicationVersion}` : ""}</option>)}</select><small>ทุก Run ใช้ Build นี้</small></label>
      </div>
      <h3 className="modal-section-title tp-config-title">Run ที่จะสร้าง ({plan.runs.length})</h3>
      <ul className="tp-clone-runs">
        {plan.runs.map((r) => {
          const sourceInactive = !loading && !activeEnvironmentIds.has(r.environmentId);
          return <li key={r.testPlanRunId} className={!loading && !activeEnvironmentIds.has(targets[r.testPlanRunId]) ? "is-missing" : undefined}>
            <span className="tp-clone-run"><b>{r.runCode}</b>{r.configurationName && <span className="tp-config">{r.configurationName}</span>}<small>{r.runName} · {r.totalCases.toLocaleString()} เคส · เดิม {r.environmentName ?? "-"}{sourceInactive ? " (ปิดใช้งานแล้ว)" : ""}</small></span>
            <select aria-label={`Environment ของ ${r.runCode}`} value={activeEnvironmentIds.has(targets[r.testPlanRunId]) ? targets[r.testPlanRunId] : ""} disabled={saving || loading} onChange={(e) => setTargets((current) => ({ ...current, [r.testPlanRunId]: e.target.value }))}>
              <option value="">เลือก Environment</option>
              {environments.map((x) => <option key={x.testEnvironmentId} value={x.testEnvironmentId}>{x.environmentName}{x.testEnvironmentId === r.environmentId ? " (เดิม)" : ""}</option>)}
            </select>
          </li>;
        })}
      </ul>
      <p className="tp-note"><span className="material-symbols-outlined" aria-hidden="true">info</span><span>คัดลอก Test Case + Revision (Source Snapshot), Configuration, ผู้รับผิดชอบ Run และทีม — <b>ไม่คัดลอก</b> ผลการทดสอบ, Defect, หลักฐาน และผู้รับผิดชอบรายเคส; Test Cycle ใหม่เริ่มเป็น Draft</span></p>
      <div className="modal-actions">
        <small className={`tp-form-status${missing.length ? " is-missing" : ""}`} role="status">{missing.length ? `ยังขาด: ${missing.join(", ")}` : "ข้อมูลครบ พร้อมสร้าง"}</small>
        <button className="btn" disabled={saving} onClick={onClose}>ยกเลิก</button>
        <button className="btn primary" disabled={saving || loading || missing.length > 0} onClick={save}>{saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังสร้าง...</> : `สร้างแผนใหม่ (${plan.runs.length} Run)`}</button>
      </div>
    </ModalShell>
  );
}
