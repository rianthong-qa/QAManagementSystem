import { useState, useEffect, type ReactElement, Fragment as _F } from "react";
import { apiUrl, isAbortError } from "../api";
import { formatThaiDateTime } from "../dateTime";

type DashboardSummary = {
  totalRequirements: number; coveredRequirements: number; requirementCoverage: number;
  totalCases: number; executedCases: number; executionProgress: number; passedCases: number; passRate: number;
  openP0: number; openP1: number; overallScore?: number; totalDefects: number; openDefects: number; criticalDefects: number; highDefects: number; defectQuality: number; recommendedDecision: string;
  totalTestCaseCount: number; testedTestCaseCount: number; testCaseProgress: number; totalExecutionCount: number; generatedAt: string;
  modules: { moduleId: string; parentModuleId?: string; moduleCode?: string; moduleName: string; sortOrder?: number; requirements: number; coveredRequirements: number; testCases: number; executed: number; passed: number; failed: number; blocked: number; coveragePercent: number; executionPercent: number; passRate: number; health: string; openDefects?: number }[];
  users: { userId: string; displayName: string; executions: number; passed: number; failed: number; blocked: number; passRate: number; lastExecutedAt?: string }[];
  statusDistribution: { status: string; count: number; color: string }[];
  defectSeverityDistribution: { severity: string; count: number; color: string }[];
  projectName?: string;
  releaseCode?: string; releaseVersion?: string; buildNumber?: string;
};

// เดิมทั้งสองกราฟนี้อยู่ใน component เดียวกัน (QualityOverviewCharts) เรนเดอร์คู่กันใน .charts-grid เสมอ
// — แยกเป็น 2 component อิสระเพื่อให้ผู้ใช้สลับตำแหน่งได้ (สรุป Defect ตามระดับความรุนแรง ย้ายไปอยู่คู่กับ
// Risks & Blockers แทน ส่วนโมดูลที่ต้องติดตามเป็นพิเศษย้ายมาอยู่คู่กับกราฟภาพรวมผลการทดสอบแทน)
function TestStatusChart({ data }: { data: DashboardSummary }) {
  const statusDist = data.statusDistribution || [];
  const totalStatus = Math.max(1, statusDist.reduce((s, x) => s + x.count, 0));
  const countStatus = (status: string) => statusDist.find(x => x.status === status)?.count ?? 0;
  const failCount = countStatus("Fail");
  const blockedCount = countStatus("Blocked");
  const notRunCount = countStatus("Not Run");
  const coverageGap = Math.max(0, 90 - data.requirementCoverage);

  // Build conic gradient for donut — เว้นช่องว่างเล็กๆ ระหว่างเซกเมนต์ให้ดูเป็นสัดส่วนชัดเจนขึ้น
  // (ไม่เว้นช่องถ้ามีสถานะเดียวที่มีค่า เพราะจะกลายเป็นวงแหวนขาดครึ่งดวง)
  const activeSegments = statusDist.filter(x => x.count > 0);
  const gapDeg = activeSegments.length > 1 ? 3 : 0;
  let angle = 0;
  const donutSegments = activeSegments.map(x => {
    const start = angle;
    const pct = x.count / totalStatus * 100;
    angle += pct / 100 * 360;
    const end = Math.max(start, angle - gapDeg);
    return `${x.color} ${start}deg ${end}deg, #fff ${end}deg ${angle}deg`;
  }).join(", ") || "#e2e8f0 0deg 360deg";

  return <article className="card chart-card">
    <div className="chart-card-head">
      <div className="chart-card-heading">
        <h3>ภาพรวมผลการทดสอบ</h3>
        <p>สรุปผลล่าสุดจากรายการที่อยู่ใน Test Cycle</p>
      </div>
      <div className="chart-card-meta">
        <span>{totalStatus.toLocaleString()} รายการ</span>
        <strong className={data.passRate >= 90 ? "is-pass" : "is-warning"}>{data.passRate >= 90 ? "ผ่านเกณฑ์คุณภาพ" : `ต่ำกว่าเกณฑ์ ${(90 - data.passRate).toFixed(1)}%`}</strong>
      </div>
    </div>
    <div className="chart-donut-wrap">
      <div className="chart-donut" style={{background:`conic-gradient(${donutSegments})`}}>
        <div className="chart-donut-hole">
          <b style={{color: data.passRate >= 90 ? "#16a36a" : data.passRate >= 70 ? "#d97706" : "#dc2626"}}>{data.passRate}%</b>
          <span>อัตราผ่าน</span>
          <small>เกณฑ์ผ่าน ≥ 90%</small>
        </div>
      </div>
      <div className="chart-donut-legend">
        {statusDist.map(x => <div key={x.status} className="legend-item">
          <i style={{background:x.color}} />
          <span className="legend-label">{x.status}</span>
          <span className="legend-count" style={{background:`${x.color}1a`,color:x.color}}>{x.count}</span>
          <span className="legend-pct">{Math.round(x.count / totalStatus * 100)}%</span>
        </div>)}
      </div>
    </div>
    {/* กันสับสนกับ % ความคืบหน้าการทดสอบใน Hero ด้านบน — ตัวนั้นนับ Test Case แบบไม่ซ้ำ (Tested ÷ Total
        distinct Test Case) ส่วน executedCases/totalCases ตรงนี้นับจาก cycleCases (แถว Assign
        Test Case เข้า Test Cycle) ถ้า Test Case เดียวถูกใช้ในหลาย Cycle จะถูกนับซ้ำได้ — ต้องบอกให้ชัด
        ว่าเป็นคนละฐานการนับ ไม่ใช่แค่ "คนละคำถาม" เฉยๆ กันเข้าใจผิดว่า totalCases = จำนวน Test Case จริง */}
    <div className="chart-note"><span className="material-symbols-outlined" aria-hidden="true">info</span><p><b>ฐานการคำนวณ</b> กราฟนี้นับรายการที่ Assign เข้า Test Cycle ({data.executedCases.toLocaleString()}/{data.totalCases.toLocaleString()}) จึงอาจนับ Test Case เดิมซ้ำเมื่ออยู่หลาย Cycle</p></div>
    <div className="qa-quality-metrics" aria-label="ตัวชี้วัดคุณภาพ QA">
      <div><span>Test Case</span><b>{data.testedTestCaseCount.toLocaleString()}/{data.totalTestCaseCount.toLocaleString()}</b><small>ทดสอบแล้ว</small></div>
      <div><span>Pass Rate</span><b>{data.passRate}%</b><small>{data.passedCases.toLocaleString()} ผ่าน</small></div>
      <div><span>Open Defect</span><b>{data.openDefects.toLocaleString()}</b><small>ยังเปิดอยู่</small></div>
      <div><span>Coverage</span><b>{data.requirementCoverage}%</b><small>{data.coveredRequirements.toLocaleString()}/{data.totalRequirements.toLocaleString()} Requirement</small></div>
    </div>
    <div className="qa-action-summary" aria-label="ประเด็นที่ต้องดำเนินการ">
      <div className="is-neutral"><span className="material-symbols-outlined" aria-hidden="true">pending_actions</span><p><small>ยังไม่ทดสอบ</small><b>{notRunCount.toLocaleString()} รายการ</b></p></div>
      <div className="is-danger"><span className="material-symbols-outlined" aria-hidden="true">error</span><p><small>Fail / Blocked</small><b>{(failCount + blockedCount).toLocaleString()} รายการ</b></p></div>
      <div className="is-warning"><span className="material-symbols-outlined" aria-hidden="true">fact_check</span><p><small>Coverage Gap</small><b>{data.totalRequirements === 0 ? "ยังไม่มีข้อมูล" : coverageGap === 0 ? "ผ่านเกณฑ์แล้ว" : `ขาดอีก ${coverageGap}%`}</b></p></div>
    </div>
  </article>;
}
function DefectSeverityChart({ data }: { data: DashboardSummary }) {
  const sevDist = data.defectSeverityDistribution || [];
  const sevOrder = ["Critical","High","Medium","Low"];
  const sevColor: Record<string, string> = { Critical: "#dc2626", High: "#f59e0b", Medium: "#2563eb", Low: "#94a3b8" };
  const sevCounts = sevOrder.map(s => { const found = sevDist.find(x => x.severity === s); return { sev: s, count: found?.count ?? 0, color: sevColor[s] }; });
  const totalDefects = sevCounts.reduce((s, x) => s + x.count, 0);
  const maxSev = Math.max(1, ...sevCounts.map(x => x.count));

  return <article className="card chart-card">
    <div className="chart-card-head">
      <h3>สรุป Defect ตามระดับความรุนแรง</h3>
      <span>Open {totalDefects.toLocaleString()} / Total {data.totalDefects.toLocaleString()}</span>
    </div>
    <div className="chart-bars">
      {sevCounts.map(x => <div key={x.sev} className="bar-row">
        <div className="bar-label">{x.sev}</div>
        <div className="bar-track">
          <div className="bar-fill" style={{width:`${Math.max(x.count / maxSev * 100, x.count > 0 ? 8 : 0)}%`,background:`linear-gradient(90deg, ${x.color}cc, ${x.color})`}}>
          </div>
        </div>
        <strong className="severity-count">{x.count.toLocaleString()}</strong>
        <span className="severity-pct">{totalDefects ? `${(x.count / totalDefects * 100).toFixed(1)}%` : "0%"}</span>
      </div>)}
    </div>
    {totalDefects === 0 && <p className="chart-empty">ยังไม่มีข้อมูล Defect</p>}
    <div className="defect-closure-note"><span>Defect Closure Rate</span><b>{data.totalDefects ? Math.round((data.totalDefects - totalDefects) / data.totalDefects * 1000) / 10 : 0}%</b><small>{Math.max(0, data.totalDefects - totalDefects).toLocaleString()} Resolved / Closed จากทั้งหมด {data.totalDefects.toLocaleString()}</small></div>
  </article>;
}

function ModuleAttentionPanel({ projectId, releaseId, buildId, modules, totalOpenDefects, shareCode, shareToken }: { projectId?: string; releaseId?: string; buildId?: string; modules: DashboardSummary["modules"]; totalOpenDefects: number; shareCode?: string; shareToken?: string }) {
  const [showAll, setShowAll] = useState(false);
  useEffect(() => setShowAll(false), [projectId, releaseId, buildId, shareCode, shareToken]);

  if (!projectId && !shareCode && !shareToken) return null;
  const rows = modules
    .filter(m => (m.openDefects ?? 0) > 0)
    .map(m => ({ moduleId: m.moduleId, count: m.openDefects ?? 0, name: m.moduleName }))
    .sort((a, b) => b.count - a.count || a.name.localeCompare(b.name));
  const maxCount = Math.max(1, ...rows.map(r => r.count));
  const previewLimit = 8;
  const visibleRows = showAll ? rows : rows.slice(0, previewLimit);
  const hasMore = rows.length > previewLimit;

  return <article className="card attention-card">
    <div className="attention-head">
      <div className="attention-heading">
        <h3>โมดูลที่ต้องติดตามเป็นพิเศษ</h3>
        <p>จัดอันดับโมดูลตามจำนวน Defect ที่ยังเปิดอยู่</p>
      </div>
      {rows.length > 0 && <div className="attention-summary" aria-label={`${rows.length} โมดูลมีปัญหา และ ${totalOpenDefects} Open Defects`}>
        <span className="attention-summary-chip is-warning"><span className="material-symbols-outlined" aria-hidden="true">warning</span>{rows.length} โมดูลมีปัญหา</span>
        <span className="attention-summary-chip is-info"><span className="material-symbols-outlined" aria-hidden="true">bug_report</span>{totalOpenDefects.toLocaleString()} Open Defects</span>
      </div>}
    </div>
    {rows.length ? <>
      <div className={`attention-list${showAll ? " is-expanded" : ""}`} id="attention-module-list">
        {visibleRows.map((r, index) => <div className={`attention-row ${index < 3 ? "is-risk" : index < previewLimit ? "is-warning" : "is-info"}`} key={r.moduleId}>
          <span className="attention-rank" aria-hidden="true">{index + 1}</span>
          <span className="attention-label" title={r.name}>{r.name}</span>
          <span className="attention-count">{r.count} Defect</span>
          <div className="attention-bar-track" aria-hidden="true"><span className="attention-bar-fill" style={{width:`${Math.max(r.count / maxCount * 100, 8)}%`}} /></div>
        </div>)}
      </div>
      {hasMore && <button type="button" className="attention-toggle" aria-expanded={showAll} aria-controls="attention-module-list" onClick={() => setShowAll(value => !value)}>
        {showAll ? `แสดงเฉพาะ Top ${previewLimit}` : `ดูทั้งหมด ${rows.length} โมดูล`}
        <span className="material-symbols-outlined" aria-hidden="true">{showAll ? "expand_less" : "chevron_right"}</span>
      </button>}
    </> : <p className="muted-row">ยังไม่มี Defect ที่เปิดอยู่ในระบบ</p>}
  </article>;
}

const healthLabelTH: Record<string, string> = { Healthy: "ปกติ", Watch: "เฝ้าระวัง", Risk: "เสี่ยง", "No Data": "ไม่มีข้อมูล" };
export function Dashboard({ projectId, releaseId, buildId, shareCode, shareToken, projectName, releaseLabel, buildLabel }: { projectId?: string; releaseId?: string; buildId?: string; shareCode?: string; shareToken?: string; projectName?: string; releaseLabel?: string; buildLabel?: string }) {
  const [expandedModules, setExpandedModules] = useState<Set<string>>(new Set());
  const [data, setData] = useState<DashboardSummary | null>(null), [loading, setLoading] = useState(true), [error, setError] = useState("");
  useEffect(() => {
    setLoading(true); setError("");
    const params = new URLSearchParams({ ...(projectId && { projectId }), ...(releaseId && { releaseId }), ...(buildId && { buildId }) });
    const url = shareCode ? `${apiUrl}/dashboard/shared/${encodeURIComponent(shareCode)}` : shareToken ? `${apiUrl}/dashboard/shared?token=${encodeURIComponent(shareToken)}` : `${apiUrl}/dashboard/summary?${params}`;
    // AbortController: สลับ Project/Release/Build เร็ว ๆ แล้วผลของบริบทเก่าต้องไม่มาทับ
    const ctrl = new AbortController();
    fetch(url, shareCode || shareToken ? { signal: ctrl.signal } : { headers: { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` }, signal: ctrl.signal })
      .then(async r => { if (!r.ok) throw new Error(r.status === 401 ? "ลิงก์แชร์ไม่ถูกต้องหรือหมดอายุ" : "ไม่สามารถโหลดข้อมูล Dashboard ได้"); return r.json(); })
      .then(setData).catch(e => { if (!isAbortError(e)) setError(e.message); }).finally(() => { if (!ctrl.signal.aborted) setLoading(false); });
    return () => ctrl.abort();
  }, [projectId, releaseId, buildId, shareCode, shareToken]);
  if (loading) return <div className="executive-loading">กำลังประมวลผลข้อมูลคุณภาพ...</div>;
  if (error || !data) return <div className="executive-error">{error || "ไม่พบข้อมูล"}</div>;
  const decisionReason = data.recommendedDecision === "NO DATA" ? "ยังไม่มี Requirement หรือ Test Cycle สำหรับประเมิน"
    : data.criticalDefects > 0 ? `พบ Critical Defect ค้าง ${data.criticalDefects} รายการ`
    : data.openP0 > 0 ? `พบ P0 ค้าง ${data.openP0} รายการ`
    : data.highDefects > 0 ? `พบ High Defect ค้าง ${data.highDefects} รายการ`
    : data.openP1 > 0 ? `พบ P1 ค้าง ${data.openP1} รายการ`
    : data.requirementCoverage < 90 ? `Requirement Coverage ${data.requirementCoverage}% ต่ำกว่าเกณฑ์ 90%`
    : data.passRate < 90 ? `Pass Rate ${data.passRate}% ต่ำกว่าเกณฑ์ 90%`
    : "ผ่านเกณฑ์ P0/P1, Coverage, Pass Rate และ Defect";
  const sortModules = (list: DashboardSummary["modules"]) => [...list].sort((a,b) => (a.sortOrder ?? 999) - (b.sortOrder ?? 999) || (a.moduleCode ?? "").localeCompare(b.moduleCode ?? ""));
  const rootModules = sortModules(data.modules.filter(x => !x.parentModuleId));
  const childModules = new Map<string, DashboardSummary["modules"]>();
  for (const m of data.modules) if (m.parentModuleId) { const list = childModules.get(m.parentModuleId) ?? []; list.push(m); childModules.set(m.parentModuleId, list); }
  type ModuleRoll = { cases: number; executed: number; passed: number; failed: number; blocked: number; subs: number };
  const rollup = new Map<string, ModuleRoll>();
  const rollOf = (id: string): ModuleRoll => {
    const cached = rollup.get(id);
    if (cached) return cached;
    const m = data.modules.find(x => x.moduleId === id);
    const acc: ModuleRoll = { cases: m?.testCases ?? 0, executed: m?.executed ?? 0, passed: m?.passed ?? 0, failed: m?.failed ?? 0, blocked: m?.blocked ?? 0, subs: 0 };
    for (const c of childModules.get(id) ?? []) { const r = rollOf(c.moduleId); acc.cases += r.cases; acc.executed += r.executed; acc.passed += r.passed; acc.failed += r.failed; acc.blocked += r.blocked; acc.subs += 1 + r.subs; }
    rollup.set(id, acc);
    return acc;
  };
  data.modules.forEach(m => rollOf(m.moduleId));
  const totalCasesAll = data.modules.reduce((s,m) => s + m.testCases, 0);
  const renderModule = (m: DashboardSummary["modules"][number], depth: number): ReactElement => {
    const children = sortModules(childModules.get(m.moduleId) ?? []);
    const hasChildren = children.length > 0;
    const isExpanded = expandedModules.has(m.moduleId);
    const agg = rollOf(m.moduleId);
    const childCases = Math.max(0, agg.cases - m.testCases);
    const den = Math.max(1, agg.executed);
    const pPct = Math.round(agg.passed / den * 100);
    const fPct = Math.round(agg.failed / den * 100);
    const bPct = Math.round(agg.blocked / den * 100);
    const healthClass = m.health.toLowerCase().replace(/\s+/g, "");
    const hasSubCases = hasChildren && childCases > 0;
    return <_F key={m.moduleId}>
      <div className={"module-tree-row" + (hasChildren ? " has-children" : "")} style={{ paddingLeft: depth * 24 }}>
        {hasChildren ? <button type="button" className={"tree-expand-btn" + (isExpanded ? " open" : "")} aria-expanded={isExpanded} aria-label={(isExpanded ? "ย่อ " : "ขยาย ") + m.moduleName} onClick={() => setExpandedModules(prev => { const next = new Set(prev); if (next.has(m.moduleId)) next.delete(m.moduleId); else next.add(m.moduleId); return next; })}>▸</button> : <span className="tree-expand-spacer" />}
        <div className="module-tree-info">
          <div className="module-tree-name">{m.moduleName}{m.moduleCode && <span className="module-code-chip">{m.moduleCode}</span>}<span className={`health-badge health-${healthClass}`}>{healthLabelTH[m.health] ?? m.health}</span></div>
          <small>{hasSubCases ? `${m.testCases.toLocaleString()} ในโมดูลนี้ + ${childCases.toLocaleString()} จาก ${agg.subs} โมดูลย่อย` : `${m.testCases.toLocaleString()} Cases`}</small>
          <div className="module-tree-bars">
            <div className="status-bar-track">{agg.executed > 0 && <><span style={{width:`${pPct}%`,background:"#16a34a"}} /><span style={{width:`${fPct}%`,background:"#dc2626"}} /><span style={{width:`${bPct}%`,background:"#d97706"}} /></>}</div>
            <div className="status-bar-labels">{agg.executed > 0 ? <><span className="sb-pass">Pass {pPct}%</span><span className="sb-fail">Fail {fPct}%</span><span className="sb-block">Blocked {bPct}%</span></> : <span className="sb-none">ยังไม่มีผล Execution</span>}</div>
          </div>
        </div>
        <div className="module-cases-pill" title={hasSubCases ? `รวม ${agg.cases.toLocaleString()} Cases (${m.testCases.toLocaleString()} ในโมดูลนี้ + ${childCases.toLocaleString()} จาก Submodules)` : `${agg.cases.toLocaleString()} Cases`}><b>{agg.cases.toLocaleString()}</b><span>Cases</span></div>
      </div>
      {isExpanded && children.map(c => renderModule(c, depth + 1))}
    </_F>;
  };
  return <div className="executive-dashboard">
    <section className="exec-hero">
      <div className="exec-hero-accent" />
      <div className="exec-hero-body">
        <div className="exec-hero-top">
          <div className="exec-hero-info">
            <span className="exec-hero-eyebrow">Executive Quality Summary</span>
            <h2 className="exec-hero-title">{projectName || data.projectName || "แดชบอร์ดความพร้อมปล่อย Release"}</h2>
          </div>
          <div className="exec-hero-score">
            <strong>{data.totalTestCaseCount > 0 ? `${data.testCaseProgress}%` : "N/A"}</strong>
            <small>ความคืบหน้าการทดสอบ (เสร็จแล้ว)</small>
            {data.totalTestCaseCount > 0 && (() => {
              const remaining = Math.round((100 - data.testCaseProgress) * 10) / 10;
              return <>
                <div className="exec-hero-progress-track"><span style={{ width: `${data.testCaseProgress}%` }} /></div>
                <div className="exec-hero-progress-labels">
                  <span className="done">เสร็จแล้ว {data.testCaseProgress}%</span>
                  <span className="remaining">เหลืออีก {remaining}%</span>
                </div>
              </>;
            })()}
            <div className="exec-hero-score-detail">
              <span>{data.testedTestCaseCount.toLocaleString()} / {data.totalTestCaseCount.toLocaleString()} Test Case ที่ทดสอบแล้ว</span>
            </div>
          </div>
        </div>
        <div className="exec-hero-bottom">
          <div className={`exec-hero-decision decision-${data.recommendedDecision.toLowerCase().replace(" ", "-")}`}>
            <span className="decision-icon">{data.recommendedDecision === "GO" ? "✓" : data.recommendedDecision === "NO-GO" ? "✕" : "!"}</span>
            <div className="decision-text">
              <strong>{data.recommendedDecision}</strong>
              <span>{decisionReason}</span>
            </div>
          </div>
          <div className="exec-hero-context">
            <span className="ctx-scope">Release: {data.releaseCode ? `${data.releaseCode}${data.releaseVersion ? ` · ${data.releaseVersion}` : ""}` : releaseLabel || "ทุก Release"}</span>
            <span className="ctx-scope">Build: {data.buildNumber || buildLabel || "ทุก Build"}</span>
            {data.criticalDefects > 0 && <span className="ctx-alert">Defect Critical {data.criticalDefects} รายการ</span>}
            <span className="ctx-time">{formatThaiDateTime(data.generatedAt, {day:"numeric", month:"short", year:"numeric", hour:"2-digit", minute:"2-digit"})}</span>
          </div>
        </div>
      </div>
    </section>
    <section className="readiness-strip" aria-label="Release Readiness">
      <div className="readiness-head"><b>Release Readiness</b><span>เกณฑ์ประเมินจากข้อมูล QA ปัจจุบัน · P0 Blocker = Test Case ระดับ P0 ที่ Fail หรือ Blocked</span></div>
      <div className="readiness-items">
        {[
          ["Requirement Coverage", `${data.requirementCoverage}%`, data.requirementCoverage >= 90],
          ["Pass Rate", `${data.passRate}%`, data.passRate >= 90],
          ["Critical Defect", `${data.criticalDefects} รายการ`, data.criticalDefects === 0],
          ["P0 Blocker", `${data.openP0} รายการ`, data.openP0 === 0],
        ].map(([label, value, passed]) => <div className={passed ? "readiness-item is-pass" : "readiness-item is-fail"} key={label as string}><span aria-hidden="true">{passed ? "✓" : "!"}</span><div><b>{label}</b><small>{value}</small></div></div>)}
      </div>
    </section>
    <div className="charts-grid">
      <TestStatusChart data={data} />
      <ModuleAttentionPanel projectId={projectId} releaseId={releaseId} buildId={buildId} modules={data.modules} totalOpenDefects={data.openDefects} shareCode={shareCode} shareToken={shareToken} />
    </div>
    <div className="dashboard-module-row">
      <article className="card" style={{padding:24}}>
        <div className="module-overview-head">
          <div className="module-overview-title">
            <h3>สถานะการทดสอบแต่ละโมดูล</h3>
            <p>โครงสร้าง Module แบบ Tree พร้อมจำนวน Test Case รวมทุกโมดูลย่อยและสถานะการทดสอบ</p>
          </div>
          <div className="module-overview-total">
            <strong>{totalCasesAll.toLocaleString()}</strong>
            <span>Test Cases ทั้งหมด</span>
            <small>{data.modules.length} โมดูล · {rootModules.length} โมดูลหลัก</small>
          </div>
        </div>
        <div className="module-tree-list">
          {rootModules.map(m => renderModule(m, 0))}
        </div>
      </article>
      <div className="dashboard-module-side">
        <DefectSeverityChart data={data} />
        <article className="card" style={{padding:24}}>
          <h3 style={{margin:"0 0 4px",fontSize:16,fontWeight:800,color:"#1f2937"}}>ปัญหาและความเสี่ยงที่ต้องติดตาม</h3>
          <p style={{margin:"0 0 20px",fontSize:12,color:"#697386",lineHeight:1.5}}>ความเสี่ยงและสิ่งกีดขวางที่ต้องติดตาม</p>
          <div className="risks-grid">
            {data.criticalDefects > 0 && <div className="risk-card"><div className="risk-icon">!</div><div className="risk-body"><b>Defect Critical</b><span>พบ Critical Defect ค้าง {data.criticalDefects} รายการ ต้องแก้ไขก่อน Release</span></div></div>}
            {data.openP0 > 0 && <div className="risk-card"><div className="risk-icon">!</div><div className="risk-body"><b>P0 ที่เป็น Blocker</b><span>พบ P0 ค้าง {data.openP0} รายการ เป็น Blocker สำหรับ Release</span></div></div>}
            {data.highDefects > 0 && <div className="risk-card risk-warning"><div className="risk-icon">⚠</div><div className="risk-body"><b>Defect High</b><span>พบ High Defect ค้าง {data.highDefects} รายการ ควรตรวจสอบและจัดลำดับ</span></div></div>}
            {data.openP1 > 0 && <div className="risk-card risk-warning"><div className="risk-icon">⚠</div><div className="risk-body"><b>P1 ที่พบปัญหา</b><span>พบ P1 ค้าง {data.openP1} รายการ ตรวจสอบว่าต้องแก้ก่อน Release หรือไม่</span></div></div>}
            {data.modules.filter(x => !x.parentModuleId && x.coveragePercent < 50).length > 0 && <div className="risk-card risk-info"><div className="risk-icon">i</div><div className="risk-body"><b>โมดูลที่ Coverage ต่ำ</b><span>{data.modules.filter(x => !x.parentModuleId && x.coveragePercent < 50).map(x => x.moduleName).join(", ")} มี Coverage ต่ำกว่า 50%</span></div></div>}
            {data.requirementCoverage < 90 && <div className="risk-card risk-info"><div className="risk-icon">i</div><div className="risk-body"><b>ความครอบคลุม Requirement ต่ำ</b><span>Requirement Coverage อยู่ที่ {data.requirementCoverage}% ต่ำกว่าเกณฑ์ 90%</span></div></div>}
            {data.criticalDefects === 0 && data.openP0 === 0 && data.highDefects === 0 && data.openP1 === 0 && <div className="risk-card" style={{background:"#f0fdf4",borderColor:"#bbf7d0"}}><div className="risk-icon" style={{background:"#dcfce7",color:"#16a34a"}}>✓</div><div className="risk-body"><b>ไม่มีความเสี่ยงร้ายแรง</b><span>ไม่พบ Critical Defect, P0 หรือ High Defect ค้าง — สถานะปกติ</span></div></div>}
          </div>
        </article>
      </div>
    </div>
    <article className="card" style={{padding:24}}>
      <h3 style={{margin:"0 0 4px",fontSize:16,fontWeight:800,color:"#1f2937"}}>ภาพรวมงาน QA</h3>
      <p style={{margin:"0 0 20px",fontSize:12,color:"#697386",lineHeight:1.5}}>ผลการดำเนินงานของผู้ทดสอบแต่ละคน · รวม {data.totalExecutionCount.toLocaleString()} Execution</p>
      <div className="qa-list">
        {data.users.length ? data.users.map((u, i) => { const workload = data.totalExecutionCount ? Math.round(u.executions / data.totalExecutionCount * 1000) / 10 : 0; return <div className="qa-card" key={u.userId}><div className="qa-icon">{i + 1}</div><div className="qa-body"><div className="qa-top"><b>{u.displayName}</b><span>{u.passRate}% Pass Rate</span></div><div className="qa-desc">{u.executions} Executions · ผ่าน {u.passed} · Failed {u.failed} · Blocked {u.blocked}</div><div className="qa-progress"><span style={{width:`${u.passRate}%`}} /></div><div className="qa-meta"><span>สัดส่วนงาน {workload}%</span><span>ล่าสุด {u.lastExecutedAt ? formatThaiDateTime(u.lastExecutedAt, { dateStyle:"short", timeStyle:"short" }) : "ยังไม่มีข้อมูล"}</span></div></div></div>; }) : <p className="muted-row">ยังไม่มีข้อมูลการทดสอบ</p>}
      </div>
    </article>
  </div>;
}
