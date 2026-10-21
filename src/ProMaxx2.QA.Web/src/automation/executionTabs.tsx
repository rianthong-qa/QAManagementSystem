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

  return <section className="automation-execution" aria-label="Automation Execution">
    {(listError || filterLoadError) && <div className="inline-alert error" role="alert"><span>{listError || filterLoadError}</span></div>}
    <header className="automation-section-head"><div><h2>Execution Queue & Run History</h2><p>ติดตามงานที่ Agent รับไปรัน และผลลัพธ์ทั้งหมด — รองรับข้อมูลจำนวนมากด้วยค้นหา/กรอง/แบ่งหน้าฝั่ง Server</p></div></header>
    <div className="automation-kpis">
      <div><small>Queued</small><strong>{queuedJobs.length}</strong><span>รอ Agent รับ</span></div>
      <div><small>Running</small><strong>{kpiRunning}</strong><span>กำลังรัน</span></div>
      <div><small>Passed</small><strong>{kpiPassed}</strong><span>ผ่านทั้งหมด</span></div>
      <div className={kpiFailed ? "needs-review" : ""}><small>Failed</small><strong>{kpiFailed}</strong><span>ไม่ผ่าน</span></div>
      <div><small>Total</small><strong>{executions.length}</strong><span>ผลรัน</span></div>
    </div>
    <ExecutionTrendChart projectId={projectId} releaseId={releaseId} headers={headers} onDrillDown={(mode, bucket) => {
      if (mode === "day") { setExecFrom(bucket.bucketKey); setExecTo(bucket.bucketKey); }
      else if (mode === "build") { setExecBuildFilter(bucket.bucketKey); }
    }} />
    <div className="automation-exec-grid">
      <article className="card">
        <div className="automation-section-head"><h3>Job Queue ({jobsPaged.total.toLocaleString()})</h3><span className="muted-text">{queuedJobs.length} queued</span></div>
        {jobsPaged.rows.length ? <>
          <div className="automation-exec-list">{jobsPaged.rows.map((j) => <div key={j.jobId} className="automation-queue-list"><article><div className="automation-queue-main"><Badge tone={jobStatusTone[j.status] ?? "blue"}>{j.status}</Badge><b>P{j.priority}</b><span>{j.assignedAgentCode ?? "รอ Agent"}</span>{j.retryCount > 0 && <Badge tone="orange">Retry {j.retryCount}</Badge>}</div><div><time dateTime={j.queuedAt}>{formatThaiDateTime(j.queuedAt)}</time>{j.lastError && <small className="queue-error">{j.lastError}</small>}</div></article></div>)}</div>
          <Pager page={jobPage} count={jobPageCount} total={jobsPaged.total} pageSize={pageSize} onPrev={() => setJobPage((p) => Math.max(1, p - 1))} onNext={() => setJobPage((p) => Math.min(jobPageCount, p + 1))} />
        </> : <div className="empty"><p>ไม่มีงานในคิว</p></div>}
      </article>
      <article className="card">
        <div className="automation-section-head"><h3>Run History ({execPaged.total.toLocaleString()})</h3></div>
        <div className="automation-run-toolbar">
          <input type="text" aria-label="ค้นหาด้วยรหัสหรือ Agent" placeholder="ค้นหา Code / Agent..." value={execSearch} onChange={(e) => setExecSearch(e.target.value)} />
          <select aria-label="กรองสถานะ" value={execFilter} onChange={(e) => setExecFilter(e.target.value)}>
            <option value="all">ทุกสถานะ</option>
            {["Passed", "Failed", "Running", "Queued", "Blocked", "Cancelled", "Timeout", "AgentLost"].map((s) => <option key={s} value={s}>{s}</option>)}
          </select>
        </div>
        {/* AUT-P2-002: advanced filters — date/Build/Environment/Agent/Target/Failure Type, all server-side. */}
        <div className="automation-run-toolbar automation-advanced-filters">
          <label>จาก<input type="date" value={execFrom} onChange={(e) => setExecFrom(e.target.value)} aria-label="วันที่เริ่ม" /></label>
          <label>ถึง<input type="date" value={execTo} onChange={(e) => setExecTo(e.target.value)} aria-label="วันที่สิ้นสุด" /></label>
          <select aria-label="กรอง Build" value={execBuildFilter} onChange={(e) => setExecBuildFilter(e.target.value)}><option value="">ทุก Build</option>{execBuilds.map((b) => <option key={b.buildId} value={b.buildId}>{b.buildNumber}{b.applicationVersion ? ` · App ${b.applicationVersion}` : ""}</option>)}</select>
          <select aria-label="กรอง Environment" value={execEnvironmentFilter} onChange={(e) => setExecEnvironmentFilter(e.target.value)}><option value="">ทุก Environment</option>{execEnvironments.map((e) => <option key={e.testEnvironmentId} value={e.testEnvironmentId}>{e.environmentName}</option>)}</select>
          <select aria-label="กรอง Agent" value={execAgentFilter} onChange={(e) => setExecAgentFilter(e.target.value)}><option value="">ทุก Agent</option>{agentOptions.map((a) => <option key={a.agentId} value={a.agentId}>{a.agentCode}</option>)}</select>
          <select aria-label="กรอง Target" value={execTargetFilter} onChange={(e) => setExecTargetFilter(e.target.value)}><option value="">ทุก Target</option><option value="Pos">Pos</option><option value="App">App</option><option value="WindowsUI">WindowsUI</option></select>
          <select aria-label="กรอง Failure Type" value={execFailureTypeFilter} onChange={(e) => setExecFailureTypeFilter(e.target.value)}><option value="">ทุก Failure Type</option>{failureTypeOptions.map((f) => <option key={f} value={f}>{f}</option>)}</select>
          {hasAdvancedFilters && <button type="button" className="table-action" onClick={clearAdvancedFilters}><span className="material-symbols-outlined" aria-hidden="true">close</span> ล้างตัวกรองขั้นสูง</button>}
        </div>
        {execPaged.rows.length ? <div className="table-wrap"><table className="automation-exec-table table-cards"><thead><tr><th>Code</th><th>Target</th><th>Agent</th><th>Status</th><th>Duration</th><th>เวลา</th><th></th></tr></thead><tbody>{execPaged.rows.map((x) => <tr key={x.automationExecutionId} onClick={() => setExecDetail(x)} className="automation-exec-tr"><td><b>{x.automationCode}</b><small>Rev {x.versionNo} · {x.buildNumber}</small></td><td><Badge tone={x.targetApp === "Pos" ? "blue" : x.targetApp === "App" ? "purple" : "gray"}>{x.targetApp ?? "WindowsUI"}</Badge></td><td>{x.agentCode ?? "-"}</td><td><Badge tone={executionStatusTone[x.status] ?? "blue"}>{x.status}</Badge></td><td>{x.durationMs != null ? `${(x.durationMs / 1000).toFixed(1)}s` : "-"}</td><td>{formatThaiDateTime(x.completedAt ?? x.startedAt)}</td><td onClick={(e) => e.stopPropagation()}><div className="automation-row-actions"><button type="button" className="automation-more" title="ดูรายละเอียด" aria-label={`ดูรายละเอียด ${x.automationCode}`} onClick={() => setExecDetail(x)}>⋮</button>{canRun && x.status !== "Running" && x.status !== "Queued" && <button type="button" className="automation-more is-run" title="รันซ้ำ" aria-label={`รันซ้ำ ${x.automationCode}`} onClick={() => onRerun(x)}>▶</button>}{canRun && (x.status === "Running" || x.status === "Queued") && <button type="button" className="automation-more is-danger" title="ยกเลิก" aria-label={`ยกเลิก ${x.automationCode}`} onClick={() => onCancel(x)}>✕</button>}</div></td></tr>)}</tbody></table></div> : <div className="empty"><p>{execSearch || execFilter !== "all" ? "ไม่พบผลการรันที่ตรงเงื่อนไข" : "ยังไม่มีประวัติการรัน"}</p></div>}
        {execPaged.total > pageSize && <Pager page={execPage} count={execPageCount} total={execPaged.total} pageSize={pageSize} onPrev={() => setExecPage((p) => Math.max(1, p - 1))} onNext={() => setExecPage((p) => Math.min(execPageCount, p + 1))} />}
      </article>
    </div>
  </section>;
}

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

  return <article className="card">
    <div className="automation-section-head">
      <h3>Pass / Fail / Flaky Trend{drillReleaseId && groupBy === "build" && " — เจาะดู Build ใน Release ที่เลือก"}</h3>
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
        <small>{groupBy === "release" ? "คลิกที่แท่งกราฟเพื่อดู Build ใน Release นั้น" : "คลิกที่แท่งกราฟเพื่อดูรายการ Execution ใน Run History ด้านล่าง"}</small>
      </div>
    </> : <div className="empty"><p>ยังไม่มีข้อมูล Execution ในช่วง 90 วันล่าสุด</p></div>}
  </article>;
}

const failureTypeOptions = ["EnvironmentFailure", "AssertionFailure", "AutomationFailure", "AgentFailure", "Unknown"];

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

  useEffect(() => {
    if (!projectId) return;
    setBusy(true);
    const qs = new URLSearchParams({ projectId });
    if (from) qs.set("from", new Date(from).toISOString());
    if (to) qs.set("to", new Date(to).toISOString());
    if (buildId) qs.set("buildId", buildId);
    if (agentId) qs.set("agentId", agentId);
    if (failureType) qs.set("failureType", failureType);
    const ctrl = new AbortController();
    setLoadError("");
    Promise.all([
      fetchJson(`${apiUrl}/automation/failures/dashboard?${qs}`, headers, ctrl.signal),
      fetchJson(`${apiUrl}/automation/failures/executions?${qs}`, headers, ctrl.signal),
    ]).then(([b, e]) => { setBreakdown(b); setRows(Array.isArray(e) ? e : []); })
      .catch((err) => { if (!isAbort(err)) setLoadError("โหลด Failure Dashboard ไม่สำเร็จ"); })
      .finally(() => { if (!ctrl.signal.aborted) setBusy(false); });
    return () => ctrl.abort();
  }, [projectId, from, to, buildId, agentId, failureType, headers]);

  const clearFilters = () => { setFrom(""); setTo(""); setBuildId(""); setAgentId(""); setFailureType(""); };
  const hasFilters = from || to || buildId || agentId || failureType;

  return <section className="automation-execution" aria-label="Failure Dashboard">
    <header className="automation-section-head"><div><h2>Failure Dashboard</h2><p>วิเคราะห์ Execution ที่ Fail ตาม Failure Type / Build / Agent / วันที่ พร้อม drill down</p></div></header>
    {(loadError || filterLoadError) && <div className="inline-alert error" role="alert"><span>{loadError || filterLoadError}</span></div>}
    <div className="automation-run-toolbar automation-advanced-filters">
      <label>จาก<input type="date" value={from} onChange={(e) => setFrom(e.target.value)} aria-label="วันที่เริ่ม" /></label>
      <label>ถึง<input type="date" value={to} onChange={(e) => setTo(e.target.value)} aria-label="วันที่สิ้นสุด" /></label>
      <select aria-label="กรอง Build" value={buildId} onChange={(e) => setBuildId(e.target.value)}><option value="">ทุก Build</option>{builds.map((b) => <option key={b.buildId} value={b.buildId}>{b.buildNumber}{b.applicationVersion ? ` · App ${b.applicationVersion}` : ""}</option>)}</select>
      <select aria-label="กรอง Agent" value={agentId} onChange={(e) => setAgentId(e.target.value)}><option value="">ทุก Agent</option>{agents.map((a) => <option key={a.agentId} value={a.agentId}>{a.agentCode}</option>)}</select>
      <select aria-label="กรอง Failure Type" value={failureType} onChange={(e) => setFailureType(e.target.value)}><option value="">ทุก Failure Type</option>{failureTypeOptions.map((f) => <option key={f} value={f}>{f}</option>)}</select>
      {hasFilters && <button type="button" className="table-action icon-only" title="ล้างตัวกรอง" aria-label="ล้างตัวกรอง" onClick={clearFilters}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>}
    </div>
    {breakdown && <div className="automation-kpis">
      <div className="needs-review"><small>Total Failed</small><strong>{breakdown.totalFailed}</strong><span>ตามตัวกรอง</span></div>
      {breakdown.byFailureType.slice(0, 4).map((x) => <div key={x.key}><small>{x.key}</small><strong>{x.count}</strong><span>Failure Type</span></div>)}
    </div>}
    <div className="automation-failure-grid">
      <article className="card">
        <div className="automation-section-head"><h3>By Build</h3></div>
        {breakdown?.byBuild.length ? <div className="automation-result-list">{breakdown.byBuild.map((x) => <div key={x.key} className="automation-failure-stat-row"><b>{x.key}</b><span className="automation-failure-stat-count"><strong>{x.count}</strong><small>fail</small></span></div>)}</div> : <div className="empty"><p>ไม่มีข้อมูล</p></div>}
      </article>
      <article className="card">
        <div className="automation-section-head"><h3>By Agent</h3></div>
        {breakdown?.byAgent.length ? <div className="automation-result-list">{breakdown.byAgent.map((x) => <div key={x.key} className="automation-failure-stat-row"><b>{x.key}</b><span className="automation-failure-stat-count"><strong>{x.count}</strong><small>fail</small></span></div>)}</div> : <div className="empty"><p>ไม่มีข้อมูล</p></div>}
      </article>
      <article className="card">
        <div className="automation-section-head"><h3>Top Automation Case</h3></div>
        {breakdown?.byAutomationCase.length ? <div className="automation-result-list">{breakdown.byAutomationCase.map((x) => <div key={x.key} className="automation-failure-stat-row"><b>{x.key}</b><span className="automation-failure-stat-count"><strong>{x.count}</strong><small>fail</small></span></div>)}</div> : <div className="empty"><p>ไม่มีข้อมูล</p></div>}
      </article>
    </div>
    <article className="card">
      <div className="automation-section-head"><h3>Failed Executions ({rows.length})</h3>{busy && <span className="muted-text"><span className="spinner inline" aria-hidden="true" /> กำลังโหลด...</span>}</div>
      {rows.length ? <div className="table-wrap"><table className="automation-exec-table table-cards"><thead><tr><th>Code</th><th>Classified</th><th>Build</th><th>Agent</th><th>เวลา</th><th className="actions-col">จัดการ</th></tr></thead><tbody>{rows.map((x) => <tr key={x.automationExecutionId} className="automation-exec-tr" onClick={() => setExecDetail(x)}>
        <td><b>{x.automationCode}</b><small>Rev {x.versionNo}</small></td>
        <td>{x.classifiedFailureType ? <Badge tone={failureTone[x.classifiedFailureType] ?? "blue"}>{x.classifiedFailureType}</Badge> : <span className="muted-text">ยังไม่จำแนก</span>}</td>
        <td>{x.buildNumber}</td>
        <td>{x.agentCode ?? "-"}</td>
        <td>{formatThaiDateTime(x.completedAt ?? x.startedAt)}</td>
        <td className="actions-col" onClick={(e) => e.stopPropagation()}><button type="button" className="table-action icon-only" title="ดูรายละเอียด" aria-label={`ดูรายละเอียด ${x.automationCode}`} onClick={() => setExecDetail(x)}><span className="material-symbols-outlined" aria-hidden="true">info</span></button></td>
      </tr>)}</tbody></table></div> : <div className="empty"><p>ไม่พบ Execution ที่ Fail ตามเงื่อนไข</p></div>}
    </article>
  </section>;
}
