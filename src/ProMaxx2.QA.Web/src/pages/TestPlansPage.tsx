import { useEffect, useMemo, useState } from "react";
import type { BuildItem, ReleaseItem, UserLookup } from "../shared/types";
import { apiFetch, apiUrl, getJson, isAbortError } from "../api";
import { useDebounced } from "../components/useDebounced";
import { confirmDialog, notify, promptDialog } from "../components/dialogStore";
import { Badge } from "../components/Badge";
import { ModalShell } from "../components/ModalShell";
import { DateField } from "../components/DateField";
import { ListPager } from "../components/ListPager";
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
type CycleOption = { testCycleId: string; projectId: string; releaseId: string; buildId: string; cycleCode: string; cycleName: string; status: string };
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
const runStatusTone: Record<string, string> = { Draft: "gray", InProgress: "blue", Completed: "green", Closed: "green", Cancelled: "red" };
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
/** API รุ่นก่อนเฟส 4 ไม่ส่งทีม/จำนวนเคสที่ยังไม่มีผู้รับผิดชอบของ Run — เติมค่าว่างกันหน้าพังระหว่างรอ deploy API */
const normalizePlan = (plan: PlanDetail): PlanDetail => ({ ...plan, runs: plan.runs.map((r) => ({ ...r, assignments: r.assignments ?? [], unassignedCases: r.unassignedCases ?? 0 })) });
const errorText = (e: unknown, fallback: string) => e instanceof Error && e.message ? e.message : fallback;
/** Configuration ที่อ่านได้: OS / Browser / Device จาก ConfigurationJson (คีย์อื่นเก็บไว้เมื่อแก้ไข) */
const parseConfig = (json?: string | null): Record<string, unknown> => { try { const value = json ? JSON.parse(json) : {}; return value && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : {}; } catch { return {}; } };
const configSummary = (run: PlanRun) => { const c = parseConfig(run.configurationJson); return ["os", "browser", "device"].map((k) => c[k]).filter((v) => typeof v === "string" && v).join(" · "); };

export function TestPlansPage({ search, canEdit, canFreeze, canAssign, canRun, contextProjectId, contextReleaseId, onOpenExecution, onOpenCycle }: {
  search: string;
  canEdit: boolean;
  canFreeze: boolean;
  canAssign: boolean;
  canRun: boolean;
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
  const [detailId, setDetailId] = useState<string | null>(null);
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
  const createDisabledReason = contextProjectId ? "" : "เลือก Project ที่ด้านบนก่อนสร้าง Test Plan";

  return (
    <>
      <article className="card lp-card tp-list">
        {error && <div className="inline-alert error" role="alert"><span>{error}{rows.length ? " — แสดงข้อมูลชุดก่อนหน้า" : ""}</span><button onClick={() => setReload((x) => x + 1)}><span className="material-symbols-outlined" aria-hidden="true">refresh</span> ลองใหม่</button></div>}
        <div className="lp-head">
          <div className="lp-title">
            <div><strong>{total.toLocaleString()}</strong><span>Test Plan{filterCount ? ` · กรอง ${filterCount} เงื่อนไข` : ""}</span></div>
            {summary && <small>{summary.runCount.toLocaleString()} Run · Test Case {summary.totalCases.toLocaleString()} · เริ่มทดสอบแล้ว {percentText(summary.progressPercent)} · อัตราผ่าน {percentText(summary.passRate)}{summary.failed + summary.blocked > 0 ? ` · Fail/Blocked ${(summary.failed + summary.blocked).toLocaleString()}` : ""}</small>}
          </div>
          {canEdit && <div className="lp-actions">
            <button className="btn primary" type="button" disabled={!!createDisabledReason} title={createDisabledReason || undefined} onClick={() => setForm({})}><span className="material-symbols-outlined" aria-hidden="true">add</span>Test Plan</button>
          </div>}
        </div>
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
        <ListPager position="top" page={page} pageCount={pageCount} totalCount={total} pageSize={pageSize} disabled={loading} onPage={setPage} onPageSize={(size) => { setPageSize(size); setPage(1); }} />
        <div className="lp-table-wrap">
          <table className="lp-table tp-table">
            <thead>
              <tr>
                <th className="lp-col-item">Test Plan</th>
                <th className="lp-col-num">Run</th>
                <th className="lp-col-progress">ความคืบหน้า</th>
                <th className="lp-col-num">อัตราผ่าน</th>
                <th className="lp-col-module lp-hide-md">เจ้าของ</th>
                <th className="lp-col-status">สถานะ</th>
                <th className="lp-col-date">กำหนดการ</th>
              </tr>
            </thead>
            <tbody>
              {loading && !rows.length && <tr className="lp-empty-row"><td colSpan={7}><div className="lp-empty" role="status"><span className="spinner" aria-hidden="true" /><b>กำลังโหลด Test Plan...</b></div></td></tr>}
              {!loading && !error && rows.length === 0 && <tr className="lp-empty-row"><td colSpan={7}><div className="lp-empty"><span className="material-symbols-outlined" aria-hidden="true">event_note</span><b>{filterCount || debouncedSearch ? "ไม่พบ Test Plan ตามตัวกรองที่เลือก" : "ยังไม่มี Test Plan"}</b><small>{filterCount || debouncedSearch ? "ลองเปลี่ยนสถานะ ตัวกรอง หรือคำค้น" : "Test Plan ใช้รวม Test Cycle หลายชุดของ Release เดียวกันและเทียบผลตาม Configuration"}</small></div></td></tr>}
              {rows.map((x) => {
                const info = planStatusInfo[x.status] ?? planStatusInfo.Draft;
                const schedule = x.startDate || x.endDate ? [x.startDate, x.endDate].map((d) => shortDate(d) || "?").join(" – ") : "ไม่ระบุวันที่";
                return (
                  <tr key={x.testPlanId} className={x.status === "Cancelled" ? "is-inactive" : undefined}>
                    <td className="lp-col-item">
                      <button type="button" className="lp-item" onClick={() => setDetailId(x.testPlanId)} aria-label={`เปิด ${x.planCode} ${x.planName}`}>
                        <span className="lp-code">{x.planCode}<span className="lp-meta">{[x.planType, x.releaseCode].filter(Boolean).join(" · ")}</span></span>
                        <span className="lp-name">{x.planName}</span>
                      </button>
                    </td>
                    <td className="lp-col-num lp-labeled" data-label="Run"><b>{x.runCount.toLocaleString()}</b></td>
                    <td className="lp-col-progress">
                      <span className={`lp-progress${x.progressPercent >= 100 ? " is-done" : x.progressPercent > 0 ? "" : " is-empty"}`} role="img" aria-label={`เริ่มทดสอบแล้ว ${x.touched} จาก ${x.totalCases} (${percentText(x.progressPercent)})`}>
                        <span className="lp-progress-track"><span style={{ width: `${Math.min(100, x.progressPercent)}%` }} /></span>
                        <small><b>{percentText(x.progressPercent)}</b> {x.touched.toLocaleString()}/{x.totalCases.toLocaleString()}</small>
                      </span>
                    </td>
                    <td className="lp-col-num lp-labeled" data-label="อัตราผ่าน">{percentText(x.passRate)}</td>
                    <td className="lp-col-module lp-hide-md lp-labeled" data-label="เจ้าของ">{x.ownerName || "-"}</td>
                    <td className="lp-col-status"><span className="tp-status"><Badge tone={info.tone}>{info.label}</Badge>{x.isFrozen && <Badge tone="purple"><span className="material-symbols-outlined tp-badge-icon" aria-hidden="true">lock</span> Frozen</Badge>}</span></td>
                    <td className="lp-col-date"><span className="lp-date">{schedule}</span></td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
        <ListPager position="bottom" page={page} pageCount={pageCount} totalCount={total} pageSize={pageSize} disabled={loading} onPage={setPage} onPageSize={(size) => { setPageSize(size); setPage(1); }} />
      </article>
      {detailId && <PlanDetailModal planId={detailId} version={detailVersion} users={users} canEdit={canEdit} canFreeze={canFreeze} canAssign={canAssign} canRun={canRun}
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

function PlanDetailModal({ planId, version, users, canEdit, canFreeze, canAssign, canRun, onClose, onChanged, onDeleted, onEdit, onCloned, onOpenExecution, onOpenCycle }: {
  planId: string; version: number; users: UserLookup[]; canEdit: boolean; canFreeze: boolean; canAssign: boolean; canRun: boolean;
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
      {plan && (canEdit || canFreeze) && <div className="cyd-toolbar" role="toolbar" aria-label="การดำเนินการกับ Test Plan">
        {canEdit && editable && <button type="button" className="btn primary" disabled={busy} onClick={() => setRunForm({})}><span className="material-symbols-outlined" aria-hidden="true">add</span> เพิ่ม Run</button>}
        {canEdit && editable && <button type="button" className="btn" disabled={busy} onClick={() => onEdit(plan)}><span className="material-symbols-outlined" aria-hidden="true">edit</span> แก้ไขแผน</button>}
        {canEdit && plan.runs.length > 0 && <button type="button" className="btn" disabled={busy} onClick={() => setCloneOpen(true)} title="สร้างแผนใหม่จากแผนนี้สำหรับ Build ใหม่ (ไม่คัดลอกผลการทดสอบ)"><span className="material-symbols-outlined" aria-hidden="true">content_copy</span> Clone / Rerun</button>}
        {canFreeze && plan.lifecycleStatus === "Active" && <button type="button" className="btn" disabled={busy || plan.status !== "Completed"} title={plan.status !== "Completed" ? "ปิดได้เมื่อทุก Run จบแล้ว (Completed / Closed / Cancelled)" : undefined} onClick={() => lifecycle("close")}><span className="material-symbols-outlined" aria-hidden="true">task_alt</span> ปิดแผน</button>}
        {canFreeze && plan.lifecycleStatus === "Active" && <button type="button" className="btn" disabled={busy} onClick={() => lifecycle(plan.isFrozen ? "unfreeze" : "freeze")}><span className="material-symbols-outlined" aria-hidden="true">{plan.isFrozen ? "lock_open" : "lock"}</span> {plan.isFrozen ? "Unfreeze" : "Freeze"}</button>}
        {canFreeze && plan.lifecycleStatus !== "Active" && <button type="button" className="btn primary" disabled={busy} onClick={() => lifecycle("reopen")}><span className="material-symbols-outlined" aria-hidden="true">replay</span> Reopen</button>}
        <span className="tp-toolbar-end">
          {canFreeze && plan.lifecycleStatus === "Active" && <button type="button" className="btn tp-danger-text" disabled={busy} onClick={() => lifecycle("cancel")}><span className="material-symbols-outlined" aria-hidden="true">block</span> ยกเลิกแผน</button>}
          {canEdit && (plan.lifecycleStatus !== "Active" || plan.runs.length === 0) && <button type="button" className="btn tp-danger-text" disabled={busy} onClick={remove}><span className="material-symbols-outlined" aria-hidden="true">delete</span> ลบ</button>}
        </span>
      </div>}
      {plan && !editable && <p className="tp-locked" role="status"><span className="material-symbols-outlined" aria-hidden="true">lock</span>{plan.lifecycleStatus !== "Active" ? `แผน${info?.label ?? ""} — แก้ไขแผนและ Run ไม่ได้ ใช้ Reopen เพื่อแก้ไขต่อ` : "แผนถูก Freeze — แก้ไขแผนและ Run ไม่ได้จนกว่าจะ Unfreeze"}</p>}
      <div className="cyd-body">
        <div className="cyd-main">
          {error && <div className="inline-alert error" role="alert"><span>{error}</span><button onClick={() => setReload((x) => x + 1)}><span className="material-symbols-outlined" aria-hidden="true">refresh</span> ลองใหม่</button></div>}
          {!plan && loading && <div className="cyd-state" role="status"><span className="spinner inline" aria-hidden="true" /> กำลังโหลด Test Plan...</div>}
          {plan && s && <section className="cyd-progress" aria-labelledby="tp-progress-title">
            <div className="cyd-progress-head">
              <h3 id="tp-progress-title"><span className="material-symbols-outlined" aria-hidden="true">monitoring</span> ความคืบหน้ารวม</h3>
              <p><b>{percentText(s.progressPercent)}</b> เริ่มทดสอบแล้ว {s.touched.toLocaleString()} จาก {s.totalCases.toLocaleString()} · ทดสอบจบ {percentText(s.completionPercent)}</p>
            </div>
            <div className="cyd-bar" role="img" aria-label={caseStatusSegments.map((x) => `${x.label} ${s[x.key]}`).join(", ")}>
              {s.totalCases > 0 ? caseStatusSegments.map((x) => <span key={x.key} className={x.className} style={{ flexGrow: s[x.key] }} title={`${x.label} ${s[x.key]}`} />) : <span className="is-notrun" style={{ flexGrow: 1 }} />}
            </div>
            <dl className="tp-counters">
              {caseStatusSegments.map((x) => <div key={x.key}><dt><span className={`cyd-dot ${x.className}`} aria-hidden="true" />{x.label}</dt><dd>{s[x.key].toLocaleString()}</dd></div>)}
              <div><dt>อัตราผ่าน</dt><dd>{percentText(s.passRate)}</dd></div>
              <div title="จำนวน Test Case ที่มีผลจบมากกว่า 1 ครั้ง (ไม่นับการกด Save Progress)"><dt>Retest</dt><dd>{s.retest.toLocaleString()}{s.retestAttempts > s.retest ? <small> ({s.retestAttempts} ครั้ง)</small> : null}</dd></div>
            </dl>
          </section>}
          {plan && <section className="cyd-cases" aria-labelledby="tp-matrix-title">
            <div className="cyd-cases-head">
              <h3 id="tp-matrix-title"><span className="material-symbols-outlined" aria-hidden="true">grid_view</span> Configuration Matrix <span className="cyd-count">{plan.runs.length.toLocaleString()}</span></h3>
              {runStatuses.length > 1 && <div className="cyd-chips" role="group" aria-label="กรอง Run ตามสถานะ">
                <button type="button" aria-pressed={!runFilter} onClick={() => setRunFilter("")}>ทั้งหมด</button>
                {runStatuses.map((st) => <button key={st} type="button" aria-pressed={runFilter === st} onClick={() => setRunFilter((c) => c === st ? "" : st)}>{st} <b>{plan.runs.filter((r) => r.status === st).length}</b></button>)}
              </div>}
            </div>
            <div className="cyd-case-list">
              {!plan.runs.length ? <div className="cyd-state"><span className="material-symbols-outlined" aria-hidden="true">add_box</span>ยังไม่มี Run — {canEdit && editable ? "กด \"เพิ่ม Run\" เพื่อเพิ่ม Test Cycle ที่มีอยู่หรือสร้างจาก Suite" : "ผู้มีสิทธิ์แก้ไขแผนเพิ่ม Run ได้"}</div>
                : visibleRuns.map((run) => {
                  const config = configSummary(run);
                  return <article key={run.testPlanRunId} className={`tp-run is-${run.status.toLowerCase()}`}>
                    <div className="tp-run-main">
                      <div className="tp-run-code"><b>{run.runCode}</b><Badge tone={runStatusTone[run.status] ?? "blue"}>{run.status}</Badge>{run.configurationName && <span className="tp-config" title={run.configurationKey ?? undefined}>{run.configurationName}</span>}</div>
                      <p>{run.runName}</p>
                      <small>
                        {canRun ? <button type="button" className="tp-link" onClick={() => onOpenCycle(run.testCycleId)} title="เปิดรายละเอียด Test Cycle">{run.cycleCode}</button> : run.cycleCode}
                        {` · Build ${run.buildNumber ?? "-"} · ${run.environmentName ?? "-"}`}{config ? ` · ${config}` : ""}{run.ownerName ? ` · ${run.ownerName}` : ""}
                      </small>
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
              <div><dt>กำหนดการ</dt><dd>{plan.startDate || plan.endDate ? `${shortDate(plan.startDate) || "?"} – ${shortDate(plan.endDate) || "?"}` : "ไม่ระบุวันที่"}</dd></div>
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
    {runForm && plan && <RunFormModal plan={plan} run={runForm.run} users={users} onClose={() => setRunForm(null)} onSaved={() => { setRunForm(null); refresh(); }} />}
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

function RunFormModal({ plan, run, users, onClose, onSaved }: { plan: PlanDetail; run?: PlanRun; users: UserLookup[]; onClose: () => void; onSaved: () => void }) {
  const initialConfig = useMemo(() => parseConfig(run?.configurationJson), [run]);
  const [mode, setMode] = useState<"attach" | "suite">("attach");
  const [cycles, setCycles] = useState<CycleOption[]>([]);
  const [suites, setSuites] = useState<SuiteOption[]>([]);
  const [builds, setBuilds] = useState<BuildItem[]>([]);
  const [environments, setEnvironments] = useState<EnvironmentOption[]>([]);
  const [optionsLoading, setOptionsLoading] = useState(!run);
  const [cycleId, setCycleId] = useState("");
  const [suiteId, setSuiteId] = useState("");
  const [buildId, setBuildId] = useState("");
  const [environmentId, setEnvironmentId] = useState("");
  const [requiredOnly, setRequiredOnly] = useState(false);
  const [runCode, setRunCode] = useState("");
  const [runName, setRunName] = useState(run?.runName ?? "");
  const [configurationKey, setConfigurationKey] = useState(run?.configurationKey ?? "");
  const [configurationName, setConfigurationName] = useState(run?.configurationName ?? "");
  const [os, setOs] = useState(typeof initialConfig.os === "string" ? initialConfig.os : "");
  const [browser, setBrowser] = useState(typeof initialConfig.browser === "string" ? initialConfig.browser : "");
  const [device, setDevice] = useState(typeof initialConfig.device === "string" ? initialConfig.device : "");
  const [ownerUserId, setOwnerUserId] = useState(run?.ownerUserId ?? "");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    if (run) return;
    const ctrl = new AbortController();
    const linked = new Set(plan.runs.map((r) => r.testCycleId));
    // ตัวเลือกทั้งหมดโหลดพร้อมกันครั้งเดียว (ไม่ยิงต่อแถว)
    Promise.all([
      getJson<CycleOption[]>(`${apiUrl}/test-cycles/options?projectId=${plan.projectId}`, ctrl.signal),
      getJson<{ rows: SuiteOption[] }>(`${apiUrl}/test-suites?projectId=${plan.projectId}&isActive=true&size=500&page=1`, ctrl.signal),
      getJson<BuildItem[]>(`${apiUrl}/releases/${plan.releaseId}/builds`, ctrl.signal),
      getJson<EnvironmentOption[]>(`${apiUrl}/test-environments?projectId=${plan.projectId}`, ctrl.signal),
    ]).then(([cycleRows, suiteRows, buildRows, environmentRows]) => {
      // Cycle ที่ผูกกับแผนอื่นอยู่แล้ว Server ปฏิเสธพร้อมเหตุผล — ที่นี่ตัดเฉพาะ Cycle ที่อยู่ในแผนนี้แล้วและคนละ Release
      setCycles(cycleRows.filter((x) => x.releaseId === plan.releaseId && !linked.has(x.testCycleId)));
      setSuites(suiteRows.rows);
      setBuilds(buildRows.filter((x) => x.isActive));
      setEnvironments(environmentRows.filter((x) => x.isActive));
    }).catch((e) => { if (!isAbortError(e)) setError(errorText(e, "โหลดตัวเลือกไม่สำเร็จ")); })
      .finally(() => { if (!ctrl.signal.aborted) setOptionsLoading(false); });
    return () => ctrl.abort();
  }, [run, plan]);

  const configJson = () => {
    const merged: Record<string, unknown> = { ...initialConfig };
    for (const [key, value] of [["os", os], ["browser", browser], ["device", device]] as const) { if (value.trim()) merged[key] = value.trim(); else delete merged[key]; }
    return Object.keys(merged).length ? merged : null;
  };
  const keyInvalid = configurationKey.trim() !== "" && !/^[a-z0-9]+(-[a-z0-9]+)*$/i.test(configurationKey.trim());
  const missing = (run ? [!runName.trim() && "ชื่อ Run"]
    : mode === "attach" ? [!cycleId && "Test Cycle"]
      : [!suiteId && "Test Suite", !buildId && "Build", !environmentId && "Environment"]).filter(Boolean) as string[];

  const save = async () => {
    setSaving(true); setError("");
    const common = { configurationKey: configurationKey.trim().toLowerCase() || null, configurationName: configurationName.trim() || null, configurationJson: configJson(), ownerUserId: ownerUserId || null };
    try {
      if (run) await apiFetch(`/test-plans/${plan.testPlanId}/runs/${run.testPlanRunId}`, { method: "PUT", json: { runName: runName.trim(), ...common }, fallbackMessage: "บันทึก Run ไม่สำเร็จ" });
      else await apiFetch(`/test-plans/${plan.testPlanId}/runs`, {
        method: "POST",
        json: { runCode: runCode.trim() || null, runName: runName.trim() || null, ...common, createCycleFromSuite: mode === "suite", testCycleId: mode === "attach" ? cycleId : null, testSuiteId: mode === "suite" ? suiteId : null, buildId: mode === "suite" ? buildId : null, environmentId: mode === "suite" ? environmentId : null, cycleName: null, requiredOnly },
        fallbackMessage: "เพิ่ม Run ไม่สำเร็จ",
      });
      notify(run ? `บันทึก ${run.runCode} แล้ว` : "เพิ่ม Run แล้ว", "success");
      onSaved();
    } catch (e) { setError(errorText(e, "บันทึก Run ไม่สำเร็จ")); }
    finally { setSaving(false); }
  };

  return (
    <ModalShell labelledBy="tp-run-title" className="tp-form tp-run-form" onDismiss={() => { if (!saving) onClose(); }}>
      <div className="modal-head"><div><h2 id="tp-run-title">{run ? `แก้ไข Run ${run.runCode}` : "เพิ่ม Run"}</h2><small>{run ? `Test Cycle ${run.cycleCode} · Build ${run.buildNumber ?? "-"} · ${run.environmentName ?? "-"}` : `${plan.planCode} · Release ${plan.releaseCode ?? "-"} — Build/Environment ใช้ค่าจาก Test Cycle`}</small></div><button disabled={saving} aria-label="ปิดหน้าต่าง" onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
      {error && <div className="inline-alert error" role="alert"><span>{error}</span></div>}
      {!run && <fieldset className="tp-mode" disabled={saving}>
        <legend>ที่มาของ Test Cycle</legend>
        <label className={mode === "attach" ? "is-selected" : undefined}><input type="radio" name="tp-run-mode" checked={mode === "attach"} onChange={() => setMode("attach")} /><span><b>เพิ่ม Cycle ที่มีอยู่</b><small>ไม่คัดลอก Test Case หรือผลการทดสอบ</small></span></label>
        <label className={mode === "suite" ? "is-selected" : undefined}><input type="radio" name="tp-run-mode" checked={mode === "suite"} onChange={() => setMode("suite")} /><span><b>สร้าง Cycle ใหม่จาก Suite</b><small>สร้าง Test Cycle (Draft) แล้วเพิ่มเข้าแผนทันที</small></span></label>
      </fieldset>}
      {!run && optionsLoading && <div className="cyd-state" role="status"><span className="spinner inline" aria-hidden="true" /> กำลังโหลดตัวเลือก...</div>}
      <div className="form-grid">
        {!run && mode === "attach" && <label className="full">Test Cycle <span className="required">*</span>
          <select value={cycleId} disabled={saving || optionsLoading} onChange={(e) => setCycleId(e.target.value)}>
            <option value="">{cycles.length ? "เลือก Test Cycle ของ Release นี้" : "ไม่มี Test Cycle ที่เพิ่มได้ใน Release นี้"}</option>
            {cycles.map((c) => <option key={c.testCycleId} value={c.testCycleId}>{c.cycleCode} · {c.cycleName} ({c.status})</option>)}
          </select>
          <small>Cycle ที่อยู่ในแผนอื่นแล้วจะเพิ่มไม่ได้ (1 Cycle อยู่ได้ 1 แผน)</small>
        </label>}
        {!run && mode === "suite" && <>
          <label className="full">Test Suite <span className="required">*</span><select value={suiteId} disabled={saving || optionsLoading} onChange={(e) => setSuiteId(e.target.value)}><option value="">เลือก Test Suite</option>{suites.map((x) => <option key={x.testSuiteId} value={x.testSuiteId}>{x.suiteCode} · {x.suiteName} ({x.caseCount} เคส)</option>)}</select></label>
          <label>Build <span className="required">*</span><select value={buildId} disabled={saving || optionsLoading} onChange={(e) => setBuildId(e.target.value)}><option value="">เลือก Build</option>{builds.map((b) => <option key={b.buildId} value={b.buildId}>{b.buildNumber}{b.applicationVersion ? ` · App ${b.applicationVersion}` : ""}</option>)}</select></label>
          <label>Environment <span className="required">*</span><select value={environmentId} disabled={saving || optionsLoading} onChange={(e) => setEnvironmentId(e.target.value)}><option value="">เลือก Environment</option>{environments.map((x) => <option key={x.testEnvironmentId} value={x.testEnvironmentId}>{x.environmentName}</option>)}</select></label>
          <label className="tp-check full"><input type="checkbox" checked={requiredOnly} disabled={saving} onChange={(e) => setRequiredOnly(e.target.checked)} /><span>เฉพาะ Test Case ที่เป็น Required ใน Suite</span></label>
        </>}
        {!run && <label>รหัส Run<input value={runCode} disabled={saving} onChange={(e) => setRunCode(e.target.value)} placeholder="เว้นว่าง = ใช้รหัส Test Cycle" maxLength={100} /></label>}
        <label className={run ? "full" : undefined}>ชื่อ Run{run && <> <span className="required">*</span></>}<input value={runName} disabled={saving} onChange={(e) => setRunName(e.target.value)} placeholder={run ? undefined : "เว้นว่าง = ใช้ชื่อ Test Cycle"} maxLength={200} /></label>
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
        <button className="btn primary" disabled={saving || missing.length > 0 || keyInvalid || (!run && optionsLoading)} onClick={save}>{saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : run ? "บันทึก" : mode === "suite" ? "สร้าง Cycle และเพิ่ม Run" : "เพิ่ม Run"}</button>
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
      .then(setMembers)
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
