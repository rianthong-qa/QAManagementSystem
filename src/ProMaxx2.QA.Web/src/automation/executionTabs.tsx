import { useEffect, useState } from "react";
import { formatThaiDateTime } from "../dateTime";
import { apiUrl } from "../api";
import {
  automationExecutionTone as executionStatusTone,
  automationJobTone as jobStatusTone,
} from "../automationUtils";
import type { AutomationAgentItem, AutomationJobItem, FailureBreakdownItem, AutomationExecutionItem, ExecutionTrendBucket } from "./types";
import { fetchJson, isAbort, useBuildsAndEnvironments, useDebounced, failureTone } from "./shared";
import { Badge, Pager } from "./ui";

export function ExecutionTab({ projectId, buildId, releaseId, agents: agentOptions, headers, jobs, executions, setExecDetail, execFilter, setExecFilter, canRun, onCancel, onRerun, reload }: {
  projectId: string; buildId?: string; releaseId?: string; agents: AutomationAgentItem[]; headers: Record<string, string>; jobs: AutomationJobItem[]; executions: AutomationExecutionItem[]; setExecDetail: (v: AutomationExecutionItem | null) => void;
  execFilter: string; setExecFilter: (v: string) => void; canRun: boolean; onCancel: (x: AutomationExecutionItem) => void; onRerun: (x: AutomationExecutionItem) => void; reload: number;
}) {
  // AUT-P2-001: Job Queue and Run History are server-paginated for real — their own fetch, own page/filter/sort
  // state, hitting the same paged endpoints as the shared "up to 200" load above. The `jobs`/`executions` props
  // (that shared, capped state) are kept ONLY for the KPI strip below, which is a cross-cutting summary, not a list
  // to page through — per the confirmed scope, KPIs/export/pickers stay on the flat shared load.
  const [execSearch, setExecSearch] = useState("");
  const [execPage, setExecPage] = useState(1);
  const [jobPage, setJobPage] = useState(1);
  const pageSize = 15;
  const [jobsPaged, setJobsPaged] = useState<{ total: number; rows: AutomationJobItem[] }>({ total: 0, rows: [] });
  const [execPaged, setExecPaged] = useState<{ total: number; rows: AutomationExecutionItem[] }>({ total: 0, rows: [] });
  // AUT-P2-002: advanced Run History filters — date range, Build, Environment, Agent, Target, Failure Type.
  // `execBuildFilter` overrides the page-level `buildId` context when set (empty means "all builds").
  const [execFrom, setExecFrom] = useState("");
  const [execTo, setExecTo] = useState("");
  const [execBuildFilter, setExecBuildFilter] = useState("");
  const [execEnvironmentFilter, setExecEnvironmentFilter] = useState("");
  const [execAgentFilter, setExecAgentFilter] = useState("");
  const [execTargetFilter, setExecTargetFilter] = useState("");
  const [execFailureTypeFilter, setExecFailureTypeFilter] = useState("");
  const { builds: execBuilds, environments: execEnvironments, loadError: filterLoadError } = useBuildsAndEnvironments(releaseId);
  const hasAdvancedFilters = execFrom || execTo || execBuildFilter || execEnvironmentFilter || execAgentFilter || execTargetFilter || execFailureTypeFilter;
  const clearAdvancedFilters = () => { setExecFrom(""); setExecTo(""); setExecBuildFilter(""); setExecEnvironmentFilter(""); setExecAgentFilter(""); setExecTargetFilter(""); setExecFailureTypeFilter(""); };

  const queuedJobs = jobs.filter((j) => j.status === "Queued");
  const jobPageCount = Math.max(1, Math.ceil(jobsPaged.total / pageSize));
  const execPageCount = Math.max(1, Math.ceil(execPaged.total / pageSize));
  const kpiRunning = executions.filter((e) => e.status === "Running").length;
  const kpiPassed = executions.filter((e) => e.status === "Passed").length;
  const kpiFailed = executions.filter((e) => e.status === "Failed").length;
  useEffect(() => setExecPage(1), [execSearch, execFilter, execFrom, execTo, execBuildFilter, execEnvironmentFilter, execAgentFilter, execTargetFilter, execFailureTypeFilter]);
  useEffect(() => setJobPage(1), [buildId]);
  const [listError, setListError] = useState("");
  const debouncedExecSearch = useDebounced(execSearch.trim());


  useEffect(() => {
    if (!projectId) { setJobsPaged({ total: 0, rows: [] }); return; }
    const ctrl = new AbortController();
    const qs = new URLSearchParams({ projectId, page: String(jobPage), size: String(pageSize) });
    if (buildId) qs.set("buildId", buildId);
    fetchJson(`${apiUrl}/automation/jobs?${qs}`, headers, ctrl.signal)
      .then((d) => setJobsPaged(d && typeof d === "object" && Array.isArray(d.rows) ? d : { total: 0, rows: [] }))
      .catch((e) => { if (!isAbort(e)) setListError("โหลด Execution Queue ไม่สำเร็จ"); });
    return () => ctrl.abort();
  }, [projectId, buildId, jobPage, headers, reload]);

  useEffect(() => {
    if (!projectId) { setExecPaged({ total: 0, rows: [] }); return; }
    const ctrl = new AbortController();
    const qs = new URLSearchParams({ projectId, page: String(execPage), size: String(pageSize) });
    if (execBuildFilter || buildId) qs.set("buildId", execBuildFilter || buildId!);
    if (execEnvironmentFilter) qs.set("environmentId", execEnvironmentFilter);
    if (execAgentFilter) qs.set("agentId", execAgentFilter);
    if (execTargetFilter) qs.set("targetApp", execTargetFilter);
    if (execFilter !== "all") qs.set("status", execFilter);
    if (execFailureTypeFilter) qs.set("failureType", execFailureTypeFilter);
    if (execFrom) qs.set("from", new Date(execFrom).toISOString());
    if (execTo) qs.set("to", new Date(execTo).toISOString());
    if (debouncedExecSearch) qs.set("search", debouncedExecSearch);
    setListError("");
    fetchJson(`${apiUrl}/automation/executions?${qs}`, headers, ctrl.signal)
      .then((d) => setExecPaged(d && typeof d === "object" && Array.isArray(d.rows) ? d : { total: 0, rows: [] }))
      .catch((e) => { if (!isAbort(e)) setListError("โหลดผลการรัน (Run History) ไม่สำเร็จ"); });
    return () => ctrl.abort();
  }, [projectId, buildId, execPage, execFilter, debouncedExecSearch, execFrom, execTo, execBuildFilter, execEnvironmentFilter, execAgentFilter, execTargetFilter, execFailureTypeFilter, headers, reload]);

  const activeJobs = jobs.filter((j) => j.status === "Queued" || j.status === "Assigned" || j.status === "Running").length;
  // คิวงานกับกราฟแนวโน้มเป็นข้อมูลประกอบ — พับไว้ให้ตารางผลการรันขึ้นก่อน (คิวเปิดเองเมื่อมีงานค้าง)
  const [queueOpen, setQueueOpen] = useState(activeJobs > 0);
  const [trendOpen, setTrendOpen] = useState(false);
  const advancedCount = [execFrom, execTo, execBuildFilter, execEnvironmentFilter, execAgentFilter, execTargetFilter, execFailureTypeFilter].filter(Boolean).length;
  const [showMoreFilters, setShowMoreFilters] = useState(advancedCount > 0);
  const statusChips = [
    { id: "all", label: "ทั้งหมด", count: executions.length, tone: "" },
    { id: "Queued", label: "รอคิว", count: queuedJobs.length, tone: "gray" },
    { id: "Running", label: "กำลังรัน", count: kpiRunning, tone: "blue" },
    { id: "Passed", label: "ผ่าน", count: kpiPassed, tone: "green" },
    { id: "Failed", label: "Fail", count: kpiFailed, tone: "red" },
  ];
  const filtered = execFilter !== "all" || !!execSearch.trim() || advancedCount > 0;

  return <section className="au-runs" aria-label="ผลการรัน">
    {(listError || filterLoadError) && <div className="inline-alert error" role="alert"><span>{listError || filterLoadError}</span></div>}

    <article className="au-runs-card">
      <header className="au-runs-head">
        <h2><strong>{execPaged.total.toLocaleString()}</strong> ผลการรัน{filtered && <small> · กรองอยู่</small>}</h2>
        <label className="au-runs-search">
          <span className="material-symbols-outlined" aria-hidden="true">search</span>
          <input type="search" aria-label="ค้นหาด้วยรหัสหรือ Agent" placeholder="ค้นหารหัส / Agent" value={execSearch} onChange={(e) => setExecSearch(e.target.value)} />
        </label>
        <button type="button" className={`btn au-runs-more${showMoreFilters ? " is-on" : ""}`} aria-expanded={showMoreFilters} onClick={() => setShowMoreFilters((v) => !v)}>
          <span className="material-symbols-outlined" aria-hidden="true">tune</span>ตัวกรอง{advancedCount > 0 && <span className="au-runs-count">{advancedCount}</span>}
        </button>
      </header>

      <div className="au-runs-chips" role="group" aria-label="กรองตามผล">
        {statusChips.map((c) => <button key={c.id} type="button" aria-pressed={execFilter === c.id} className={execFilter === c.id ? "active" : ""} onClick={() => setExecFilter(c.id === execFilter && c.id !== "all" ? "all" : c.id)}>
          {c.tone && <i className={`au-chip-dot t-${c.tone}`} aria-hidden="true" />}{c.label}<b>{c.count}</b>
        </button>)}
        <select aria-label="สถานะอื่น" className="au-runs-other" value={statusChips.some((c) => c.id === execFilter) ? "" : execFilter} onChange={(e) => setExecFilter(e.target.value || "all")}>
          <option value="">สถานะอื่น…</option>
          {["Blocked", "Cancelled", "Timeout", "AgentLost"].map((s) => <option key={s} value={s}>{s}</option>)}
        </select>
      </div>

      {/* AUT-P2-002: ตัวกรองขั้นสูงทั้งหมดกรองฝั่ง Server */}
      {showMoreFilters && <div className="au-runs-filters">
        <label>ตั้งแต่<input type="date" value={execFrom} onChange={(e) => setExecFrom(e.target.value)} /></label>
        <label>ถึง<input type="date" value={execTo} onChange={(e) => setExecTo(e.target.value)} /></label>
        <label>Build<select value={execBuildFilter} onChange={(e) => setExecBuildFilter(e.target.value)}><option value="">ทั้งหมด</option>{execBuilds.map((b) => <option key={b.buildId} value={b.buildId}>{b.buildNumber}{b.applicationVersion ? ` · App ${b.applicationVersion}` : ""}</option>)}</select></label>
        <label>Environment<select value={execEnvironmentFilter} onChange={(e) => setExecEnvironmentFilter(e.target.value)}><option value="">ทั้งหมด</option>{execEnvironments.map((e) => <option key={e.testEnvironmentId} value={e.testEnvironmentId}>{e.environmentName}</option>)}</select></label>
        <label>Agent<select value={execAgentFilter} onChange={(e) => setExecAgentFilter(e.target.value)}><option value="">ทั้งหมด</option>{agentOptions.map((a) => <option key={a.agentId} value={a.agentId}>{a.agentCode}</option>)}</select></label>
        <label>Target<select value={execTargetFilter} onChange={(e) => setExecTargetFilter(e.target.value)}><option value="">ทั้งหมด</option><option value="Pos">Pos</option><option value="App">App</option><option value="WindowsUI">WindowsUI</option></select></label>
        <label>ประเภท Fail<select value={execFailureTypeFilter} onChange={(e) => setExecFailureTypeFilter(e.target.value)}><option value="">ทั้งหมด</option>{failureTypeOptions.map((f) => <option key={f} value={f}>{f}</option>)}</select></label>
        {hasAdvancedFilters && <button type="button" className="au-runs-clear" onClick={clearAdvancedFilters}><span className="material-symbols-outlined" aria-hidden="true">close</span>ล้างตัวกรอง</button>}
      </div>}

      {execPaged.rows.length ? <div className="table-wrap au-runs-wrap"><table className="au-runs-table table-cards">
        <thead><tr><th>Automation Case</th><th>ผล</th><th>Agent · Target</th><th>ใช้เวลา</th><th>เวลารัน</th><th className="actions-col"><span className="sr-only">จัดการ</span></th></tr></thead>
        <tbody>{execPaged.rows.map((x) => <tr key={x.automationExecutionId} onClick={() => setExecDetail(x)} className={`au-runs-tr is-${x.status.toLowerCase()}`}>
          <td data-label="Case"><b>{x.automationCode}</b><small>Rev {x.versionNo} · Build {x.buildNumber}{x.retryCount ? ` · Retry ${x.retryCount}` : ""}</small></td>
          <td data-label="ผล"><Badge tone={executionStatusTone[x.status] ?? "blue"}>{x.status}</Badge>{x.status !== "Passed" && x.classifiedFailureType && <small className="au-runs-why">{x.classifiedFailureType}</small>}</td>
          <td data-label="Agent"><span>{x.agentCode ?? "รอ Agent"}</span><small>{x.targetApp ?? "WindowsUI"}</small></td>
          <td data-label="ใช้เวลา" className="au-runs-num">{formatRunDuration(x.durationMs)}</td>
          <td data-label="เวลารัน" className="au-runs-time">{formatThaiDateTime(x.completedAt ?? x.startedAt)}</td>
          <td className="actions-col" onClick={(e) => e.stopPropagation()}><div className="au-runs-actions">
            <button type="button" className="au-icon-btn" title="ดูผล / Evidence" aria-label={`ดูผล ${x.automationCode}`} onClick={() => setExecDetail(x)}><span className="material-symbols-outlined" aria-hidden="true">visibility</span></button>
            {canRun && x.status !== "Running" && x.status !== "Queued" && <button type="button" className="au-icon-btn" title="รันซ้ำ" aria-label={`รันซ้ำ ${x.automationCode}`} onClick={() => onRerun(x)}><span className="material-symbols-outlined" aria-hidden="true">replay</span></button>}
            {canRun && (x.status === "Running" || x.status === "Queued") && <button type="button" className="au-icon-btn danger" title="ยกเลิก" aria-label={`ยกเลิก ${x.automationCode}`} onClick={() => onCancel(x)}><span className="material-symbols-outlined" aria-hidden="true">cancel</span></button>}
          </div></td>
        </tr>)}</tbody>
      </table></div> : <div className="au-runs-empty">{filtered ? "ไม่พบผลการรันที่ตรงเงื่อนไข — ลองล้างตัวกรอง" : "ยังไม่มีผลการรัน — สั่งรัน Automation Case แล้วผลจะแสดงที่นี่"}</div>}
      {execPaged.total > pageSize && <Pager page={execPage} count={execPageCount} total={execPaged.total} pageSize={pageSize} onPrev={() => setExecPage((p) => Math.max(1, p - 1))} onNext={() => setExecPage((p) => Math.min(execPageCount, p + 1))} />}
    </article>

    <details className="au-fold" open={queueOpen} onToggle={(e) => setQueueOpen((e.currentTarget as HTMLDetailsElement).open)}>
      <summary><span className="material-symbols-outlined" aria-hidden="true">pending_actions</span>คิวงาน<small>{activeJobs ? `${activeJobs} งานรอ / กำลังรัน` : "ไม่มีงานค้าง"} · ทั้งหมด {jobsPaged.total.toLocaleString()}</small><span className="au-guide-chevron material-symbols-outlined" aria-hidden="true">expand_more</span></summary>
      {jobsPaged.rows.length ? <>
        <ul className="au-queue">{jobsPaged.rows.map((j) => <li key={j.jobId}>
          <Badge tone={jobStatusTone[j.status] ?? "blue"}>{j.status}</Badge>
          <span className="au-queue-main"><b>P{j.priority}</b> · {j.assignedAgentCode ?? "รอ Agent"}{j.retryCount > 0 && ` · Retry ${j.retryCount}`}{j.lastError && <small className="queue-error">{j.lastError}</small>}</span>
          <time dateTime={j.queuedAt}>{formatThaiDateTime(j.queuedAt)}</time>
        </li>)}</ul>
        {jobsPaged.total > pageSize && <Pager page={jobPage} count={jobPageCount} total={jobsPaged.total} pageSize={pageSize} onPrev={() => setJobPage((p) => Math.max(1, p - 1))} onNext={() => setJobPage((p) => Math.min(jobPageCount, p + 1))} />}
      </> : <div className="au-runs-empty">ไม่มีงานในคิว</div>}
    </details>

    <details className="au-fold" open={trendOpen} onToggle={(e) => setTrendOpen((e.currentTarget as HTMLDetailsElement).open)}>
      <summary><span className="material-symbols-outlined" aria-hidden="true">bar_chart</span>แนวโน้ม Pass / Fail<small>กดแท่งกราฟเพื่อกรองตารางด้านบน</small><span className="au-guide-chevron material-symbols-outlined" aria-hidden="true">expand_more</span></summary>
      {/* โหลดกราฟเฉพาะตอนเปิด */}
      {trendOpen && <ExecutionTrendChart projectId={projectId} releaseId={releaseId} headers={headers} onDrillDown={(mode, bucket) => {
        if (mode === "day") { setExecFrom(bucket.bucketKey); setExecTo(bucket.bucketKey); }
        else if (mode === "build") { setExecBuildFilter(bucket.bucketKey); }
        setShowMoreFilters(true);
      }} />}
    </details>
  </section>;
}

const formatRunDuration = (ms?: number) => {
  if (ms == null) return "-";
  const s = Math.round(ms / 1000);
  return s < 60 ? `${(ms / 1000).toFixed(1)} วิ` : `${Math.floor(s / 60)} นาที ${s % 60} วิ`;
};

/// AUT-P2-003: lightweight, dependency-free SVG bar chart (this codebase has no charting library) — Pass/Fail bars
/// per bucket plus a small dot marking Flaky activity. "release" grouping is never a drill-down leaf: clicking a
/// release bar switches the chart itself to "build" grouping scoped to that release (a second drill level entirely
/// self-contained here); clicking a "day" or "build" bar calls `onDrillDown` so the parent can filter Run History
/// to that day/build — reusing the exact filter state AUT-P2-002 already built, not a new mechanism.
function ExecutionTrendChart({ projectId, releaseId, headers, onDrillDown }: {
  projectId: string; releaseId?: string; headers: Record<string, string>; onDrillDown: (mode: "day" | "build", bucket: ExecutionTrendBucket) => void;
}) {
  const [groupBy, setGroupBy] = useState<"day" | "build" | "release">("day");
  const [drillReleaseId, setDrillReleaseId] = useState("");
  const [trend, setTrend] = useState<{ groupBy: string; buckets: ExecutionTrendBucket[] }>({ groupBy: "day", buckets: [] });
  const [busy, setBusy] = useState(false);
  const [trendError, setTrendError] = useState("");

  useEffect(() => {
    if (!projectId) { setTrend({ groupBy, buckets: [] }); return; }
    setBusy(true);
    const qs = new URLSearchParams({ projectId, groupBy });
    const effectiveRelease = drillReleaseId || releaseId;
    if (effectiveRelease && groupBy !== "release") qs.set("releaseId", effectiveRelease);
    const ctrl = new AbortController();
    setTrendError("");
    fetchJson(`${apiUrl}/automation/executions/trend?${qs}`, headers, ctrl.signal)
      .then((d) => setTrend(d && typeof d === "object" && Array.isArray(d.buckets) ? d : { groupBy, buckets: [] }))
      .catch((e) => { if (!isAbort(e)) { setTrend({ groupBy, buckets: [] }); setTrendError("โหลดกราฟแนวโน้มไม่สำเร็จ"); } })
      .finally(() => { if (!ctrl.signal.aborted) setBusy(false); });
    return () => ctrl.abort();
  }, [projectId, releaseId, groupBy, drillReleaseId, headers]);

  const changeGroupBy = (g: "day" | "build" | "release") => { setGroupBy(g); setDrillReleaseId(""); };
  const handleBarClick = (b: ExecutionTrendBucket) => {
    if (groupBy === "release") { setGroupBy("build"); setDrillReleaseId(b.bucketKey); return; }
    onDrillDown(groupBy, b);
  };

  const chartHeight = 140;
  const barWidth = 24;
  const gap = 14;
  const maxVal = Math.max(1, ...trend.buckets.map((b) => Math.max(b.passed, b.failed)));

  return <div className="au-trend">
    <div className="au-trend-head">
      <span>{drillReleaseId && groupBy === "build" ? "Build ใน Release ที่เลือก" : "Pass / Fail / Flaky ย้อนหลัง 90 วัน"}</span>
      <div className="automation-trend-toolbar">
        {drillReleaseId && <button type="button" className="table-action" onClick={() => changeGroupBy("release")}>‹ กลับไป Release</button>}
        <select aria-label="จัดกลุ่มตาม" value={groupBy} onChange={(e) => changeGroupBy(e.target.value as "day" | "build" | "release")}>
          <option value="day">ตามวัน</option>
          <option value="build">ตาม Build</option>
          <option value="release">ตาม Release</option>
        </select>
      </div>
    </div>
    {trendError && <div className="inline-alert error" role="alert"><span>{trendError}</span></div>}
    {busy ? <div className="empty"><div className="spinner" /><p>กำลังโหลด...</p></div> : trend.buckets.length ? <>
      <div className="automation-trend-chart-wrap">
        <svg className="automation-trend-chart" width={trend.buckets.length * (barWidth * 2 + gap) + gap} height={chartHeight + 34} role="img" aria-label="กราฟแนวโน้ม Pass / Fail / Flaky">
          {trend.buckets.map((b, i) => {
            const x = gap + i * (barWidth * 2 + gap);
            const passH = Math.round((b.passed / maxVal) * chartHeight);
            const failH = Math.round((b.failed / maxVal) * chartHeight);
            return <g key={b.bucketKey} className="automation-trend-bar-group" tabIndex={0} role="button"
              aria-label={`${b.bucketLabel}: Pass ${b.passed}, Fail ${b.failed}, Flaky ${b.flaky}`}
              onClick={() => handleBarClick(b)} onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") handleBarClick(b); }}>
              <title>{`${b.bucketLabel}: Pass ${b.passed} / Fail ${b.failed} / Flaky ${b.flaky}`}</title>
              <rect className="automation-trend-bar-pass" x={x} y={chartHeight - passH} width={barWidth} height={Math.max(passH, b.passed > 0 ? 2 : 0)} />
              <rect className="automation-trend-bar-fail" x={x + barWidth} y={chartHeight - failH} width={barWidth} height={Math.max(failH, b.failed > 0 ? 2 : 0)} />
              {b.flaky > 0 && <circle className="automation-trend-flaky-dot" cx={x + barWidth} cy={chartHeight - Math.max(passH, failH) - 8} r={4} />}
              <text className="automation-trend-bar-label" x={x + barWidth} y={chartHeight + 16} textAnchor="middle">{b.bucketLabel}</text>
            </g>;
          })}
        </svg>
      </div>
      <div className="automation-trend-legend">
        <span><i className="legend-pass" />Passed</span><span><i className="legend-fail" />Failed</span><span><i className="legend-flaky" />Flaky</span>
        <small>{groupBy === "release" ? "กดแท่งกราฟเพื่อดู Build ใน Release นั้น" : "กดแท่งกราฟเพื่อกรองตารางผลการรันด้านบน"}</small>
      </div>
    </> : <div className="au-runs-empty">ยังไม่มีข้อมูล Execution ในช่วง 90 วันล่าสุด</div>}
  </div>;
}

const failureTypeOptions = ["EnvironmentFailure", "AssertionFailure", "AutomationFailure", "AgentFailure", "Unknown"];

// ความหมายของประเภท Fail (จาก AutomationFailureClassifier ฝั่ง API) และสิ่งที่ควรทำต่อ — ให้ผู้ใช้ใหม่อ่านเข้าใจโดยไม่ต้องรู้ชื่อ enum
const failureInfo: Record<string, { label: string; meaning: string }> = {
  AssertionFailure: { label: "ผลไม่ตรงที่คาด", meaning: "ขั้นตอนทำงานครบแต่ผลจริงไม่ตรง Expected — อาจเป็น Bug จริงหรือข้อมูลทดสอบ/Expected ผิด" },
  AutomationFailure: { label: "สคริปต์ / Object", meaning: "Agent หาปุ่มหรือช่องกรอกไม่เจอ หรือ DSL ผิด — ไม่ใช่ Bug ของโปรแกรม ต้องซ่อม DSL หรือ Object" },
  EnvironmentFailure: { label: "สภาพแวดล้อม", meaning: "เปิดโปรแกรมไม่ได้ หรือเชื่อมต่อ Database ไม่ได้ — ตรวจ Environment แล้วรันซ้ำ" },
  AgentFailure: { label: "Agent", meaning: "เครื่อง Agent หลุด ค้าง หรือหมดเวลา — ตรวจเครื่อง Agent แล้วรันซ้ำ" },
  ApplicationFailure: { label: "Bug ของโปรแกรม", meaning: "โปรแกรมทำงานผิดพลาด — ตรวจสอบแล้วสร้าง Defect" },
  TestDataFailure: { label: "ข้อมูลทดสอบ", meaning: "ข้อมูลที่ใช้ทดสอบไม่พร้อม — เตรียมหรือ Seed ข้อมูลใหม่แล้วรันซ้ำ" },
  Unknown: { label: "ยังไม่ทราบสาเหตุ", meaning: "ระบบจำแนกไม่ได้ — เปิดผลเต็มเพื่อดู Log และภาพหน้าจอ" },
};
const recommendationTh: Record<string, string> = {
  QAReviewBeforeCreateDefect: "QA ตรวจผลและภาพหน้าจอก่อน — ถ้าเป็น Bug จริงให้สร้าง Defect",
  MaintenanceRequired: "ซ่อม DSL หรือ Object Repository แล้ว Validate ใหม่ (Case ถูกตั้งเป็น \"ต้องซ่อม\")",
  RetryOrCheckEnvironment: "ตรวจ Environment / Database แล้วรันซ้ำ",
  Retry: "ตรวจเครื่อง Agent แล้วรันซ้ำ",
};
const failureLabel = (t?: string) => (t ? failureInfo[t]?.label ?? t : "ยังไม่จำแนก");

export function FailureDashboardTab({ projectId, releaseId, agents, headers, setExecDetail }: {
  projectId: string; releaseId?: string; agents: AutomationAgentItem[]; headers: Record<string, string>; setExecDetail: (v: AutomationExecutionItem | null) => void;
}) {
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [buildId, setBuildId] = useState("");
  const [agentId, setAgentId] = useState("");
  const [failureType, setFailureType] = useState("");
  const { builds, loadError: filterLoadError } = useBuildsAndEnvironments(releaseId);
  const [breakdown, setBreakdown] = useState<FailureBreakdownItem | null>(null);
  const [rows, setRows] = useState<AutomationExecutionItem[]>([]);
  const [busy, setBusy] = useState(false);
  const [loadError, setLoadError] = useState("");
  const [retry, setRetry] = useState(0);
  const [openRow, setOpenRow] = useState("");
  const [showFilters, setShowFilters] = useState(false);
  // ตัวเลขแยกตามประเภทคิดจากชุดที่ไม่กรองประเภท — กดชิปประเภทอื่นแล้วตัวเลขชิปไม่หายไป
  const [typeCounts, setTypeCounts] = useState<{ key: string; count: number }[]>([]);

  useEffect(() => {
    if (!projectId) return;
    setBusy(true);
    const qs = new URLSearchParams({ projectId });
    if (from) qs.set("from", new Date(from).toISOString());
    if (to) qs.set("to", new Date(to).toISOString());
    if (buildId) qs.set("buildId", buildId);
    if (agentId) qs.set("agentId", agentId);
    const all = new URLSearchParams(qs);
    if (failureType) qs.set("failureType", failureType);
    const ctrl = new AbortController();
    setLoadError("");
    Promise.all([
      fetchJson(`${apiUrl}/automation/failures/dashboard?${qs}`, headers, ctrl.signal),
      fetchJson(`${apiUrl}/automation/failures/executions?${qs}`, headers, ctrl.signal),
      failureType ? fetchJson(`${apiUrl}/automation/failures/dashboard?${all}`, headers, ctrl.signal) : Promise.resolve(null),
    ]).then(([b, e, unfiltered]) => {
      setBreakdown(b);
      setRows(Array.isArray(e) ? e : []);
      setTypeCounts(((unfiltered ?? b) as FailureBreakdownItem | null)?.byFailureType ?? []);
    })
      .catch((err) => { if (!isAbort(err)) setLoadError("โหลดข้อมูลวิเคราะห์ Fail ไม่สำเร็จ"); })
      .finally(() => { if (!ctrl.signal.aborted) setBusy(false); });
    return () => ctrl.abort();
  }, [projectId, from, to, buildId, agentId, failureType, headers, retry]);

  const clearFilters = () => { setFrom(""); setTo(""); setBuildId(""); setAgentId(""); setFailureType(""); };
  const advancedCount = [from, to, buildId, agentId].filter(Boolean).length;
  const hasFilters = advancedCount > 0 || !!failureType;
  const totalAll = typeCounts.reduce((s, x) => s + x.count, 0);
  const buildIdByNumber = new Map(builds.map((b) => [b.buildNumber, b.buildId]));
  const agentIdByCode = new Map(agents.map((a) => [a.agentCode, a.agentId]));
  const groups: { title: string; items: { key: string; count: number }[]; pick?: (key: string) => void; active?: string }[] = [
    { title: "Build ที่ Fail มาก", items: breakdown?.byBuild ?? [], pick: (k) => { const id = buildIdByNumber.get(k); if (id) setBuildId(id === buildId ? "" : id); }, active: builds.find((b) => b.buildId === buildId)?.buildNumber },
    { title: "Agent ที่ Fail มาก", items: breakdown?.byAgent ?? [], pick: (k) => { const id = agentIdByCode.get(k); if (id) setAgentId(id === agentId ? "" : id); }, active: agents.find((a) => a.agentId === agentId)?.agentCode },
    { title: "Case ที่ Fail บ่อย", items: breakdown?.byAutomationCase ?? [] },
  ];

  return <section className="au-runs" aria-label="วิเคราะห์ Fail">
    {(loadError || filterLoadError) && <div className="inline-alert error" role="alert"><span>{loadError || filterLoadError}</span>{loadError && <button type="button" className="btn" onClick={() => setRetry((v) => v + 1)}>ลองใหม่</button>}</div>}
    <article className="au-runs-card">
      <header className="au-runs-head">
        <h2><strong>{(breakdown?.totalFailed ?? 0).toLocaleString()}</strong> ครั้งที่ Fail{hasFilters && <small> · กรองอยู่</small>}{busy && <span className="spinner inline au-fail-busy" aria-label="กำลังโหลด" />}</h2>
        {hasFilters && <button type="button" className="au-runs-clear" onClick={clearFilters}><span className="material-symbols-outlined" aria-hidden="true">close</span>ล้างตัวกรอง</button>}
        <button type="button" className={`btn au-runs-more${showFilters ? " is-on" : ""}`} aria-expanded={showFilters} onClick={() => setShowFilters((v) => !v)}>
          <span className="material-symbols-outlined" aria-hidden="true">tune</span>ตัวกรอง{advancedCount > 0 && <span className="au-runs-count">{advancedCount}</span>}
        </button>
      </header>

      <div className="au-runs-chips" role="group" aria-label="กรองตามประเภท Fail">
        <button type="button" aria-pressed={!failureType} className={!failureType ? "active" : ""} onClick={() => setFailureType("")}>ทุกประเภท<b>{totalAll}</b></button>
        {typeCounts.map((t) => <button key={t.key} type="button" aria-pressed={failureType === t.key} className={failureType === t.key ? "active" : ""} title={failureInfo[t.key]?.meaning} onClick={() => setFailureType(failureType === t.key ? "" : t.key)}>
          <i className={`au-chip-dot t-${failureTone[t.key] ?? "gray"}`} aria-hidden="true" />{failureLabel(t.key)}<b>{t.count}</b>
        </button>)}
      </div>
      {failureType && failureInfo[failureType] && <p className="au-fail-meaning"><span className="material-symbols-outlined" aria-hidden="true">lightbulb</span>{failureInfo[failureType].meaning}</p>}

      {showFilters && <div className="au-runs-filters">
        <label>ตั้งแต่<input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></label>
        <label>ถึง<input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></label>
        <label>Build<select value={buildId} onChange={(e) => setBuildId(e.target.value)}><option value="">ทั้งหมด</option>{builds.map((b) => <option key={b.buildId} value={b.buildId}>{b.buildNumber}{b.applicationVersion ? ` · App ${b.applicationVersion}` : ""}</option>)}</select></label>
        <label>Agent<select value={agentId} onChange={(e) => setAgentId(e.target.value)}><option value="">ทั้งหมด</option>{agents.map((a) => <option key={a.agentId} value={a.agentId}>{a.agentCode}</option>)}</select></label>
      </div>}

      {(breakdown?.totalFailed ?? 0) > 0 && <div className="au-fail-groups">
        {groups.map((g) => { const max = Math.max(1, ...g.items.map((x) => x.count)); return <section key={g.title} className="au-fail-group">
          <h3>{g.title}</h3>
          {g.items.length ? <ol>{g.items.slice(0, 5).map((x) => { const clickable = !!g.pick; const body = <><span className="au-fail-key" title={x.key}>{x.key}</span><span className="au-fail-bar" aria-hidden="true"><i style={{ width: `${(x.count / max) * 100}%` }} /></span><b>{x.count}</b></>;
            return <li key={x.key}>{clickable ? <button type="button" aria-pressed={g.active === x.key} className={g.active === x.key ? "active" : ""} title={`กรองเฉพาะ ${x.key}`} onClick={() => g.pick!(x.key)}>{body}</button> : <div>{body}</div>}</li>; })}</ol>
            : <p className="au-fail-none">ไม่มีข้อมูล</p>}
        </section>; })}
      </div>}

      {rows.length ? <ul className="au-fail-list">{rows.map((x) => { const open = openRow === x.automationExecutionId; const info = x.classifiedFailureType ? failureInfo[x.classifiedFailureType] : undefined; return <li key={x.automationExecutionId} className={open ? "is-open" : ""}>
        <button type="button" className="au-fail-row" aria-expanded={open} onClick={() => setOpenRow(open ? "" : x.automationExecutionId)}>
          <span className="au-fail-main"><b>{x.automationCode}</b><small>{[x.testCaseCode, x.testCaseTitle].filter(Boolean).join(" · ") || `Rev ${x.versionNo}`}</small></span>
          <span className="au-fail-type"><Badge tone={failureTone[x.classifiedFailureType ?? ""] ?? "gray"}>{failureLabel(x.classifiedFailureType)}</Badge>{x.errorCode && <code>{x.errorCode}</code>}</span>
          <span className="au-fail-meta">Build {x.buildNumber}<small>{x.agentCode ?? "-"}</small></span>
          <span className="au-fail-time">{formatThaiDateTime(x.completedAt ?? x.startedAt)}</span>
          <span className="au-fail-chevron material-symbols-outlined" aria-hidden="true">expand_more</span>
        </button>
        {open && <div className="au-fail-detail">
          {x.errorMessage && <div className="au-fail-error"><span>ข้อความ Error</span><p>{x.errorMessage}</p></div>}
          <dl>
            <div><dt>ประเภท</dt><dd>{failureLabel(x.classifiedFailureType)}{info && <small>{info.meaning}</small>}</dd></div>
            <div><dt>ควรทำต่อ</dt><dd>{x.classifiedRecommendation ? recommendationTh[x.classifiedRecommendation] ?? x.classifiedRecommendation : "-"}</dd></div>
            <div><dt>Version / Build</dt><dd>Rev {x.versionNo} · Build {x.buildNumber}</dd></div>
            <div><dt>Environment / Agent</dt><dd>{x.environmentName ?? "-"} · {x.agentCode ?? "-"}</dd></div>
            <div><dt>ใช้เวลา</dt><dd>{x.durationMs != null ? `${(x.durationMs / 1000).toFixed(1)} วินาที` : "-"}{x.retryCount ? ` · รันซ้ำครั้งที่ ${x.retryCount}` : ""}</dd></div>
            {x.defectId && <div><dt>Defect</dt><dd>สร้าง Defect แล้ว</dd></div>}
          </dl>
          <div className="au-fail-actions"><button type="button" className="btn primary au-runs-more" onClick={() => setExecDetail(x)}><span className="material-symbols-outlined" aria-hidden="true">open_in_new</span>ดูผลเต็ม / ภาพหน้าจอ / สร้าง Defect</button></div>
        </div>}
      </li>; })}</ul> : !busy && <div className="au-runs-empty">{hasFilters ? "ไม่พบผลที่ Fail ตามตัวกรอง — ลองล้างตัวกรอง" : "ยังไม่มีผลที่ Fail"}</div>}
    </article>
  </section>;
}
