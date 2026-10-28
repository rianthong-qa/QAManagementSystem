import { useState, useEffect, type ReactElement, type ReactNode, Fragment as _F } from "react";
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
type Module = DashboardSummary["modules"][number];

/** เวลาที่ผ่านมาแบบอ่านง่าย เช่น "เมื่อสักครู่", "5 นาทีที่แล้ว" */
function ago(iso: string, now: number): string {
  const minutes = Math.floor((now - Date.parse(iso)) / 60_000);
  if (!Number.isFinite(minutes) || minutes < 1) return "เมื่อสักครู่";
  if (minutes < 60) return `${minutes} นาทีที่แล้ว`;
  const hours = Math.floor(minutes / 60);
  return hours < 24 ? `${hours} ชั่วโมงที่แล้ว` : `${Math.floor(hours / 24)} วันที่แล้ว`;
}

/** เกณฑ์ Release Gate ที่ใช้ทั้งหน้า (Coverage / Pass Rate ≥ 90%) */
const gate = 90;
/** ส่วนต่างจากเกณฑ์ปัดทศนิยม 1 ตำแหน่ง — เดิมลบตรง ๆ ได้ 4.2000000000000003 */
const gapTo = (value: number) => Math.max(0, Math.round((gate - value) * 10) / 10);
const pct = (part: number, total: number) => (total ? Math.round(part / total * 1000) / 10 : 0);

/**
 * ผลการทดสอบเป็นแถบซ้อน 100% (แทนกราฟวงกลม) — เดิมกลางวงเขียน Pass Rate 90% (คิดจากที่ทดสอบแล้ว) แต่ส่วนสีเขียวกว้าง 70%
 * (คิดจากทุกรายการรวม Not Run) ผู้อ่านจึงเข้าใจผิด; ตอนนี้แยกตัวเลขสองฐานให้ชัด: แถบ = ทุกรายการ, Pass Rate = ของที่ทดสอบแล้ว
 */
function TestStatusChart({ data }: { data: DashboardSummary }) {
  const statusDist = data.statusDistribution || [];
  const total = statusDist.reduce((s, x) => s + x.count, 0);
  const notRun = statusDist.find(x => x.status === "Not Run")?.count ?? 0;
  const tested = Math.max(0, total - notRun);
  const passTone = data.passRate >= gate ? "is-pass" : data.passRate >= 70 ? "is-warning" : "is-fail";

  return <article className="card db-results">
    <div className="db-card-head"><h3>ผลการทดสอบ</h3><p>สถานะรายการใน Test Cycle (นับซ้ำเมื่อ Test Case อยู่หลาย Cycle)</p></div>
    {/* % ทดสอบแล้วมีที่เดียวคือส่วนหัว (นับ Test Case ไม่ซ้ำ) — การ์ดนี้แสดงจำนวนรายการโดยไม่ใส่ % เพื่อไม่ให้ดูเป็นตัวเลขเดียวกันที่ไม่ตรงกัน */}
    <div className="db-results-figures">
      <div><b className={passTone}>{data.passRate}%</b><span>อัตราผ่าน</span><small>เกณฑ์ ≥ {gate}%</small></div>
      <div className="db-results-count"><span>รันแล้ว <b>{tested.toLocaleString()}</b> จาก <b>{total.toLocaleString()}</b> รายการใน Test Cycle</span><small>อัตราผ่านคิดจากรายการที่รันแล้ว</small></div>
    </div>
    <div className="db-stack" role="img" aria-label={statusDist.map(x => `${x.status} ${x.count} รายการ (${pct(x.count, total)}%)`).join(", ") || "ยังไม่มีผลการทดสอบ"}>
      {total > 0 ? statusDist.filter(x => x.count > 0).map(x => <span key={x.status} className="db-tip" style={{ width: `${x.count / total * 100}%`, background: x.color }} data-tip={`${x.status} · ${x.count.toLocaleString()} รายการ (${pct(x.count, total)}%)`} />) : <span className="empty" />}
    </div>
    <ul className="db-stack-legend">
      {statusDist.map(x => <li key={x.status}><i style={{ background: x.color }} aria-hidden="true" /><span>{x.status}</span><b>{x.count.toLocaleString()}</b><small>{pct(x.count, total)}%</small></li>)}
    </ul>
    {/* กันสับสนกับ % ความคืบหน้าในส่วนหัว — ตัวนั้นนับ Test Case แบบไม่ซ้ำ ส่วนตรงนี้นับจาก cycleCases (นับซ้ำได้เมื่ออยู่หลาย Cycle) */}
    <p className="db-footnote"><span className="material-symbols-outlined" aria-hidden="true">info</span>ความคืบหน้าการทดสอบดูที่ส่วนหัว (นับ Test Case ไม่ซ้ำ) — ตัวเลขในการ์ดนี้นับรายการใน Test Cycle จึงไม่เท่ากัน</p>
  </article>;
}

const sevColor: Record<string, string> = { Critical: "var(--red)", High: "var(--yellow)", Medium: "var(--info)", Low: "var(--muted)" };
/** การ์ดที่พับได้ — Desktop เปิดตั้งแต่ต้น, จอ ≤760px เริ่มแบบพับเพื่อให้เนื้อหาหลักขึ้นก่อน */
const startOpen = () => { try { return !window.matchMedia("(max-width: 760px)").matches; } catch { return true; } };
function FoldCard({ className, title, subtitle, children }: { className: string; title: string; subtitle: string; children: ReactNode }) {
  const [open, setOpen] = useState(startOpen);
  return <details className={`card db-fold ${className}`} open={open} onToggle={e => setOpen(e.currentTarget.open)}>
    <summary><span className="db-card-head"><h3>{title}</h3><p>{subtitle}</p></span><span className="material-symbols-outlined db-fold-icon" aria-hidden="true">expand_more</span></summary>
    {children}
  </details>;
}

function DefectSeverityChart({ data }: { data: DashboardSummary }) {
  const sevDist = data.defectSeverityDistribution || [];
  const sevOrder = ["Critical", "High", "Medium", "Low"];
  const sevCounts = sevOrder.map(s => ({ sev: s, count: sevDist.find(x => x.severity === s)?.count ?? 0, color: sevColor[s] }));
  const openTotal = sevCounts.reduce((s, x) => s + x.count, 0);
  const maxSev = Math.max(1, ...sevCounts.map(x => x.count));
  const closed = Math.max(0, data.totalDefects - openTotal);

  return <FoldCard className="db-defects" title="Defect ที่เปิดอยู่ตามความรุนแรง" subtitle={`เปิดอยู่ ${openTotal.toLocaleString()} จากทั้งหมด ${data.totalDefects.toLocaleString()} รายการ`}>
    {openTotal === 0 ? <p className="muted-row">ไม่มี Defect ที่เปิดอยู่</p> : <ul className="db-sev">
      {sevCounts.map(x => <li key={x.sev}>
        <span className="db-sev-label">{x.sev}</span>
        <span className="db-sev-track" aria-hidden="true"><span className="db-tip" style={{ width: `${x.count ? Math.max(4, x.count / maxSev * 100) : 0}%`, background: x.color }} data-tip={`${x.sev} · ${x.count.toLocaleString()} รายการ (${pct(x.count, openTotal)}% ของที่เปิดอยู่)`} /></span>
        <b>{x.count.toLocaleString()}</b>
        <small>{pct(x.count, openTotal)}%</small>
      </li>)}
    </ul>}
    <div className="db-closure"><span>ปิดแล้ว (Resolved / Closed)</span><b>{pct(closed, data.totalDefects)}%</b><small>{closed.toLocaleString()} จาก {data.totalDefects.toLocaleString()} รายการ</small></div>
  </FoldCard>;
}

/** สิ่งที่ต้องติดตามนอกเหนือจากเกณฑ์ปล่อย Release (Critical/P0/Coverage/Pass Rate อยู่ในส่วนหัวแล้ว) */
function FollowUpList({ data, notStarted }: { data: DashboardSummary; notStarted: Module[] }) {
  const lowCoverage = data.modules.filter(x => !x.parentModuleId && x.requirements > 0 && x.coveragePercent < 50);
  const items: { tone: "danger" | "warning" | "info"; icon: string; title: string; detail: string }[] = [];
  if (data.highDefects > 0) items.push({ tone: "warning", icon: "bug_report", title: `Defect High ค้าง ${data.highDefects} รายการ`, detail: "ควรตรวจสอบและจัดลำดับการแก้ไข" });
  if (data.openP1 > 0) items.push({ tone: "warning", icon: "priority_high", title: `P1 ที่ Fail/Blocked ${data.openP1} รายการ`, detail: "ตรวจสอบว่าต้องแก้ก่อน Release หรือไม่" });
  if (lowCoverage.length) items.push({ tone: "info", icon: "fact_check", title: `Coverage ต่ำกว่า 50% · ${lowCoverage.length} โมดูล`, detail: lowCoverage.map(x => x.moduleName).join(", ") });
  if (notStarted.length) items.push({ tone: "info", icon: "pending_actions", title: `ยังไม่เริ่มทดสอบ · ${notStarted.length} โมดูล`, detail: notStarted.map(x => x.moduleName).join(", ") });
  const gateFailing = data.criticalDefects > 0 || data.openP0 > 0;

  return <article className="card db-followup">
    <div className="db-card-head"><h3>สิ่งที่ต้องติดตาม</h3><p>นอกเหนือจากเกณฑ์ปล่อย Release ด้านบน</p></div>
    {items.length ? <ul>
      {items.map(x => <li key={x.title} className={`tone-${x.tone}`}><span className="material-symbols-outlined" aria-hidden="true">{x.icon}</span><span><b>{x.title}</b><small>{x.detail}</small></span></li>)}
    </ul> : <p className={`db-followup-clear${gateFailing ? " is-warning" : ""}`}><span className="material-symbols-outlined" aria-hidden="true">{gateFailing ? "info" : "task_alt"}</span>{gateFailing ? "ไม่มีเรื่องอื่นนอกจาก Critical/P0 ในเกณฑ์ด้านบน" : "ไม่มีความเสี่ยงที่ต้องติดตามเพิ่ม"}</p>}
  </article>;
}

/** โครงหน้าระหว่างโหลด — รูปทรงเดียวกับ Dashboard จริง หน้าจึงไม่กระโดดตอนข้อมูลมา */
function DashboardSkeleton() {
  return <div className="executive-dashboard db-skeleton" role="status" aria-label="กำลังโหลด Dashboard">
    <span className="sr-only">กำลังประมวลผลข้อมูลคุณภาพ...</span>
    <div className="db-sk db-sk-hero" />
    <div className="db-sk-row two"><div className="db-sk db-sk-card" /><div className="db-sk db-sk-card" /></div>
    <div className="db-sk-row wide"><div className="db-sk db-sk-card tall" /><div className="db-sk db-sk-card" /></div>
  </div>;
}

/** ปลายทางเมื่อกดโมดูล — ไม่ส่งมา (หน้าลิงก์แชร์ที่อ่านอย่างเดียว) = กดไม่ได้ */
type OpenModule = (moduleId: string, target: "defects" | "test-cases") => void;

const healthLabelTH: Record<string, string> = { Healthy: "ปกติ", Watch: "เฝ้าระวัง", Risk: "เสี่ยง", "No Data": "ไม่มีข้อมูล" };
const decisionInfo: Record<string, { icon: string; label: string }> = {
  GO: { icon: "check_circle", label: "พร้อมปล่อย" }, "NO-GO": { icon: "cancel", label: "ยังไม่พร้อมปล่อย" },
  "CONDITIONAL GO": { icon: "error", label: "ปล่อยได้แบบมีเงื่อนไข" }, "NO DATA": { icon: "help", label: "ยังประเมินไม่ได้" },
};
export function Dashboard({ projectId, releaseId, buildId, shareCode, shareToken, projectName, releaseLabel, buildLabel, onOpenModule: onOpenModuleProp }: { projectId?: string; releaseId?: string; buildId?: string; shareCode?: string; shareToken?: string; projectName?: string; releaseLabel?: string; buildLabel?: string; onOpenModule?: OpenModule }) {
  // หน้าลิงก์แชร์เป็นอ่านอย่างเดียว — ไม่มีหน้าปลายทางให้ไป จึงปิดการกดแม้ผู้เรียกจะส่งมา
  const onOpenModule = shareCode || shareToken ? undefined : onOpenModuleProp;
  const [expandedModules, setExpandedModules] = useState<Set<string>>(new Set());
  const [moduleSort, setModuleSort] = useState<"tree" | "defects">("tree");
  const [data, setData] = useState<DashboardSummary | null>(null), [loading, setLoading] = useState(true), [error, setError] = useState(""), [retryKey, setRetryKey] = useState(0);
  const [refreshing, setRefreshing] = useState(false), [now, setNow] = useState(() => Date.now());
  const [scope, setScope] = useState("");
  // นาฬิกาสำหรับ "x นาทีที่แล้ว" — อัปเดตทุก 30 วินาที
  useEffect(() => { const t = window.setInterval(() => setNow(Date.now()), 30_000); return () => window.clearInterval(t); }, []);
  const scopeKey = [projectId, releaseId, buildId, shareCode, shareToken].join("|");
  useEffect(() => {
    // สลับขอบเขต (Project/Release/Build/ลิงก์) = ล้างแล้วแสดง skeleton; กดรีเฟรช/ลองใหม่ = คงข้อมูลเดิมไว้จนข้อมูลใหม่มา
    if (scope !== scopeKey) { setScope(scopeKey); setData(null); setLoading(true); }
    setError("");
    const params = new URLSearchParams({ ...(projectId && { projectId }), ...(releaseId && { releaseId }), ...(buildId && { buildId }) });
    const url = shareCode ? `${apiUrl}/dashboard/shared/${encodeURIComponent(shareCode)}` : shareToken ? `${apiUrl}/dashboard/shared?token=${encodeURIComponent(shareToken)}` : `${apiUrl}/dashboard/summary?${params}`;
    // AbortController: สลับ Project/Release/Build เร็ว ๆ แล้วผลของบริบทเก่าต้องไม่มาทับ
    const ctrl = new AbortController();
    fetch(url, shareCode || shareToken ? { signal: ctrl.signal } : { headers: { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` }, signal: ctrl.signal })
      .then(async r => { if (!r.ok) throw new Error(r.status === 401 ? "ลิงก์แชร์ไม่ถูกต้องหรือหมดอายุ" : "ไม่สามารถโหลดข้อมูล Dashboard ได้"); return r.json(); })
      .then(d => { setData(d); setNow(Date.now()); }).catch(e => { if (!isAbortError(e)) setError(e.message); }).finally(() => { if (!ctrl.signal.aborted) { setLoading(false); setRefreshing(false); } });
    return () => ctrl.abort();
    // scope ใช้แค่เทียบว่าเปลี่ยนขอบเขตหรือไม่ — ไม่ต้องยิงซ้ำเมื่อ scope ถูกตั้งค่า
  }, [projectId, releaseId, buildId, shareCode, shareToken, retryKey]); // eslint-disable-line react-hooks/exhaustive-deps
  const refresh = () => { setRefreshing(true); setRetryKey(k => k + 1); };
  if (loading && !data) return <DashboardSkeleton />;
  if (!data) return <div className="executive-error" role="alert">
    <span className="material-symbols-outlined" aria-hidden="true">cloud_off</span>
    <b>{error || "ไม่พบข้อมูล"}</b>
    <small>ตรวจสอบการเชื่อมต่อแล้วลองอีกครั้ง</small>
    <button className="btn" type="button" onClick={() => setRetryKey(k => k + 1)}><span className="material-symbols-outlined" aria-hidden="true">refresh</span>ลองใหม่</button>
  </div>;
  const decisionReason = data.recommendedDecision === "NO DATA" ? "ยังไม่มี Requirement หรือ Test Cycle สำหรับประเมิน"
    : data.criticalDefects > 0 ? `พบ Critical Defect ค้าง ${data.criticalDefects} รายการ`
    : data.openP0 > 0 ? `พบ P0 ค้าง ${data.openP0} รายการ`
    : data.highDefects > 0 ? `พบ High Defect ค้าง ${data.highDefects} รายการ`
    : data.openP1 > 0 ? `พบ P1 ค้าง ${data.openP1} รายการ`
    : data.requirementCoverage < gate ? `Requirement Coverage ${data.requirementCoverage}% ต่ำกว่าเกณฑ์ ${gate}%`
    : data.passRate < gate ? `Pass Rate ${data.passRate}% ต่ำกว่าเกณฑ์ ${gate}%`
    : "ผ่านเกณฑ์ P0/P1, Coverage, Pass Rate และ Defect";
  const sortModules = (list: Module[]) => [...list].sort((a, b) => (a.sortOrder ?? 999) - (b.sortOrder ?? 999) || (a.moduleCode ?? "").localeCompare(b.moduleCode ?? ""));
  const rootModules = sortModules(data.modules.filter(x => !x.parentModuleId));
  const childModules = new Map<string, Module[]>();
  for (const m of data.modules) if (m.parentModuleId) { const list = childModules.get(m.parentModuleId) ?? []; list.push(m); childModules.set(m.parentModuleId, list); }
  type ModuleRoll = { cases: number; executed: number; passed: number; failed: number; blocked: number; defects: number; subs: number };
  const rollup = new Map<string, ModuleRoll>();
  const rollOf = (id: string): ModuleRoll => {
    const cached = rollup.get(id);
    if (cached) return cached;
    const m = data.modules.find(x => x.moduleId === id);
    const acc: ModuleRoll = { cases: m?.testCases ?? 0, executed: m?.executed ?? 0, passed: m?.passed ?? 0, failed: m?.failed ?? 0, blocked: m?.blocked ?? 0, defects: m?.openDefects ?? 0, subs: 0 };
    for (const c of childModules.get(id) ?? []) { const r = rollOf(c.moduleId); acc.cases += r.cases; acc.executed += r.executed; acc.passed += r.passed; acc.failed += r.failed; acc.blocked += r.blocked; acc.defects += r.defects; acc.subs += 1 + r.subs; }
    rollup.set(id, acc);
    return acc;
  };
  data.modules.forEach(m => rollOf(m.moduleId));
  const totalCasesAll = data.modules.reduce((s, m) => s + m.testCases, 0);
  const parentIds = [...childModules.keys()];
  const modulesWithDefects = data.modules.filter(m => (m.openDefects ?? 0) > 0).sort((a, b) => (b.openDefects ?? 0) - (a.openDefects ?? 0) || a.moduleName.localeCompare(b.moduleName));
  const toggleModule = (id: string) => setExpandedModules(prev => { const next = new Set(prev); if (next.has(id)) next.delete(id); else next.add(id); return next; });
  /** แถวโมดูล — โหมด tree ใช้ตัวเลขรวมโมดูลย่อย; โหมดเรียงตาม Defect เป็นรายการแบน ใช้ตัวเลขของโมดูลนั้นเอง (ตรงกับหน้า Defect ที่กรองทีละโมดูล) */
  const renderModule = (m: Module, depth: number, flat = false): ReactElement => {
    const children = flat ? [] : sortModules(childModules.get(m.moduleId) ?? []);
    const hasChildren = children.length > 0;
    const isExpanded = expandedModules.has(m.moduleId);
    const agg: ModuleRoll = flat ? { cases: m.testCases, executed: m.executed, passed: m.passed, failed: m.failed, blocked: m.blocked, defects: m.openDefects ?? 0, subs: 0 } : rollOf(m.moduleId);
    const childCases = Math.max(0, agg.cases - m.testCases);
    const den = Math.max(1, agg.executed);
    const pPct = Math.round(agg.passed / den * 100);
    const fPct = Math.round(agg.failed / den * 100);
    const bPct = Math.round(agg.blocked / den * 100);
    const healthClass = m.health.toLowerCase().replace(/\s+/g, "");
    const hasSub = !flat && (childModules.get(m.moduleId)?.length ?? 0) > 0;
    const sub = hasSub ? " (รวมโมดูลย่อย)" : "";
    return <_F key={m.moduleId}>
      <li className={`db-module${depth === 0 ? " root" : ""}`} aria-level={depth + 1} aria-expanded={hasChildren ? isExpanded : undefined}>
        <span className="db-module-indent" style={{ width: depth * 22 }} aria-hidden="true" />
        {hasChildren
          ? <button type="button" className={`db-module-toggle${isExpanded ? " open" : ""}`} aria-label={(isExpanded ? "ย่อ " : "ขยาย ") + m.moduleName} onClick={() => toggleModule(m.moduleId)}><span className="material-symbols-outlined" aria-hidden="true">chevron_right</span></button>
          : <span className="db-module-toggle-spacer" aria-hidden="true" />}
        <span className="db-module-main">
          <span className="db-module-name"><b>{m.moduleName}</b>{m.moduleCode && <code>{m.moduleCode}</code>}{!(m.health === "No Data" && agg.executed > 0) && <span className={`health-badge health-${healthClass}`}>{healthLabelTH[m.health] ?? m.health}</span>}</span>
          <span className="db-module-bar" role="img" aria-label={agg.executed > 0 ? `Pass ${pPct}% Fail ${fPct}% Blocked ${bPct}%` : "ยังไม่มีผล Execution"}>
            {agg.executed > 0 && <><span className="pass" style={{ width: `${pPct}%` }} /><span className="fail" style={{ width: `${fPct}%` }} /><span className="blocked" style={{ width: `${bPct}%` }} /></>}
          </span>
        </span>
        <span className="db-module-pct">{agg.executed > 0 ? <><span className="pass">Pass {pPct}%</span><span className="fail">Fail {fPct}%</span>{bPct > 0 && <span className="blocked">Blocked {bPct}%</span>}</> : <span className="none">ยังไม่มีผล</span>}</span>
        <span className="db-module-num" title={hasSub ? `${m.testCases.toLocaleString()} ในโมดูลนี้ + ${childCases.toLocaleString()} จาก ${agg.subs} โมดูลย่อย` : undefined}><b>{agg.cases.toLocaleString()}</b><small>เคส</small></span>
        <span className={`db-module-num defects${agg.defects ? " has" : ""}`} title={`Open Defect${sub}`}><b>{agg.defects.toLocaleString()}</b><small>Defect</small></span>
        {onOpenModule && <span className="db-module-links">
          <button type="button" title={`ดู Test Case ของโมดูลนี้${hasSub ? " (ไม่รวมโมดูลย่อย)" : ""}`} aria-label={`ดู Test Case ของ ${m.moduleName}${hasSub ? " ไม่รวมโมดูลย่อย" : ""}`} onClick={() => onOpenModule(m.moduleId, "test-cases")}><span className="material-symbols-outlined" aria-hidden="true">fact_check</span></button>
          <button type="button" title={`ดู Defect ของโมดูลนี้${hasSub ? " (ไม่รวมโมดูลย่อย)" : ""}`} aria-label={`ดู Defect ของ ${m.moduleName}${hasSub ? " ไม่รวมโมดูลย่อย" : ""}`} onClick={() => onOpenModule(m.moduleId, "defects")}><span className="material-symbols-outlined" aria-hidden="true">bug_report</span></button>
        </span>}
      </li>
      {isExpanded && children.map(c => renderModule(c, depth + 1))}
    </_F>;
  };
  const criteria: { label: string; value: string; note: string; passed: boolean; noData?: boolean }[] = [
    data.totalRequirements === 0
      ? { label: "ความครอบคลุม Requirement", value: "—", note: "ยังไม่มี Requirement ในขอบเขตนี้", passed: false, noData: true }
      : { label: "ความครอบคลุม Requirement", value: `${data.requirementCoverage}%`, note: data.requirementCoverage >= gate ? `${data.coveredRequirements.toLocaleString()}/${data.totalRequirements.toLocaleString()} Requirement` : `ขาดอีก ${gapTo(data.requirementCoverage)}%`, passed: data.requirementCoverage >= gate },
    { label: "อัตราผ่าน (Pass Rate)", value: `${data.passRate}%`, note: data.passRate >= gate ? `${data.passedCases.toLocaleString()} รายการผ่าน` : `ขาดอีก ${gapTo(data.passRate)}%`, passed: data.passRate >= gate },
    { label: "Defect ระดับ Critical", value: `${data.criticalDefects}`, note: data.criticalDefects ? "ต้องแก้ก่อน Release" : "ไม่มีค้าง", passed: data.criticalDefects === 0 },
    { label: "P0 ที่ติดขัด (Blocker)", value: `${data.openP0}`, note: data.openP0 ? "P0 ที่ Fail หรือ Blocked" : "ไม่มีค้าง", passed: data.openP0 === 0 },
  ];
  const decision = decisionInfo[data.recommendedDecision] ?? { icon: "help", label: data.recommendedDecision };
  const decisionKey = data.recommendedDecision.toLowerCase().replace(" ", "-");
  const passedCount = criteria.filter(x => x.passed).length;
  const noDataCount = criteria.filter(x => x.noData).length;
  const remaining = Math.round((100 - data.testCaseProgress) * 10) / 10;
  const hasCases = data.totalTestCaseCount > 0;
  return <div className="executive-dashboard">
    <section className={`card db-hero decision-${decisionKey}`} aria-label="ความพร้อมปล่อย Release">
      <div className="db-hero-scope">
        <b>{projectName || data.projectName || "ทุก Project"}</b>
        <span>Release {data.releaseCode ? `${data.releaseCode}${data.releaseVersion ? ` · ${data.releaseVersion}` : ""}` : releaseLabel || "ทุก Release"}</span>
        <span>Build {data.buildNumber || buildLabel || "ทุก Build"}</span>
        <span className="db-hero-time" title={`ข้อมูล ณ ${formatThaiDateTime(data.generatedAt, { day: "numeric", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit" })}`}>
          ข้อมูล ณ {formatThaiDateTime(data.generatedAt, { hour: "2-digit", minute: "2-digit" })} น. · {ago(data.generatedAt, now)}
          <button type="button" className={`db-refresh${refreshing ? " spinning" : ""}`} onClick={refresh} disabled={refreshing} aria-label="โหลดข้อมูลล่าสุด" title="โหลดข้อมูลล่าสุด"><span className="material-symbols-outlined" aria-hidden="true">refresh</span></button>
        </span>
        {error && <span className="db-hero-error" role="alert">{error} — แสดงข้อมูลชุดก่อนหน้า</span>}
      </div>
      <div className="db-hero-body">
        <div className="db-decision">
          <span className="material-symbols-outlined" aria-hidden="true">{decision.icon}</span>
          <div>
            <small>คำแนะนำการปล่อย Release</small>
            <strong>{data.recommendedDecision}<span>{decision.label}</span></strong>
            <p>{decisionReason}</p>
          </div>
        </div>
        {/* ความคืบหน้าอยู่ข้างคำตัดสิน (ตัวเลขหลักที่ผู้บริหารดูเป็นอันดับสอง) — วงแหวนสีเดียว ตัวเลขใหญ่กลางวง สีข้อความ */}
        <div className="db-progress">
          <div className="db-progress-ring" role="progressbar" aria-valuemin={0} aria-valuemax={100} aria-valuenow={hasCases ? data.testCaseProgress : undefined} aria-label="ความคืบหน้าการทดสอบ (นับ Test Case ไม่ซ้ำ)">
            <svg viewBox="0 0 120 120" aria-hidden="true">
              <circle className="track" cx="60" cy="60" r="52" />
              {hasCases && data.testCaseProgress > 0 && <circle className="value" cx="60" cy="60" r="52" pathLength={100} strokeDasharray={`${Math.min(100, data.testCaseProgress)} 100`} />}
            </svg>
            <span className="db-progress-value">{hasCases ? <><b>{data.testCaseProgress}<small>%</small></b><em>ทดสอบแล้ว</em></> : <b className="na">N/A</b>}</span>
          </div>
          <div className="db-progress-info">
            <small>ความคืบหน้าการทดสอบ</small>
            <span className="db-progress-basis">นับ Test Case ไม่ซ้ำ</span>
            {hasCases ? <dl>
              <div><dt>ทดสอบแล้ว</dt><dd>{data.testedTestCaseCount.toLocaleString()}</dd></div>
              <div><dt>คงเหลือ</dt><dd>{(data.totalTestCaseCount - data.testedTestCaseCount).toLocaleString()}<small> · {remaining}%</small></dd></div>
              <div><dt>ทั้งหมด</dt><dd>{data.totalTestCaseCount.toLocaleString()}</dd></div>
            </dl> : <p>ยังไม่มี Test Case ในขอบเขตนี้</p>}
          </div>
        </div>
        <div className="db-criteria">
          <span className="db-criteria-head">เกณฑ์ปล่อย Release · ผ่าน {passedCount}/{criteria.length}{noDataCount ? ` · ไม่มีข้อมูล ${noDataCount}` : ""}</span>
          <ul>
            {criteria.map(x => <li key={x.label} className={x.noData ? "is-nodata" : x.passed ? "is-pass" : "is-fail"}>
              <span className="material-symbols-outlined" aria-hidden="true">{x.noData ? "do_not_disturb_on" : x.passed ? "check_circle" : "cancel"}</span>
              <span className="db-criteria-label">{x.label}<small>{x.note}</small></span>
              <b>{x.value}</b>
              <span className="sr-only">{x.noData ? "ไม่มีข้อมูล" : x.passed ? "ผ่าน" : "ไม่ผ่าน"}</span>
            </li>)}
          </ul>
        </div>
      </div>
    </section>
    <div className="db-row two">
      <TestStatusChart data={data} />
      <DefectSeverityChart data={data} />
    </div>
    <div className="db-row wide">
      <article className="card db-modules">
        <div className="db-card-head db-modules-head">
          <div>
            <h3>โมดูล</h3>
            <p>{totalCasesAll.toLocaleString()} Test Case · {data.modules.length} โมดูล ({rootModules.length} โมดูลหลัก){moduleSort === "tree" ? " · ตัวเลขรวมโมดูลย่อย" : " · ตัวเลขของโมดูลนั้นเอง"}</p>
          </div>
          <div className="db-modules-actions">
            <div className="db-segment" role="group" aria-label="มุมมองโมดูล">
              <button type="button" aria-pressed={moduleSort === "tree"} onClick={() => setModuleSort("tree")}><span className="material-symbols-outlined" aria-hidden="true">account_tree</span>ตามโครงสร้าง</button>
              <button type="button" aria-pressed={moduleSort === "defects"} onClick={() => setModuleSort("defects")} disabled={!modulesWithDefects.length}><span className="material-symbols-outlined" aria-hidden="true">bug_report</span>Defect มากสุด</button>
            </div>
            {moduleSort === "tree" && parentIds.length > 0 && <>
              <button className="btn" type="button" onClick={() => setExpandedModules(new Set(parentIds))} aria-label="ขยายทั้งหมด" title="ขยายทั้งหมด"><span className="material-symbols-outlined" aria-hidden="true">unfold_more</span></button>
              <button className="btn" type="button" onClick={() => setExpandedModules(new Set())} aria-label="ย่อทั้งหมด" title="ย่อทั้งหมด"><span className="material-symbols-outlined" aria-hidden="true">unfold_less</span></button>
            </>}
          </div>
        </div>
        {moduleSort === "defects"
          ? <ul className="db-module-tree" aria-label="โมดูลเรียงตาม Open Defect">{modulesWithDefects.map(m => renderModule(m, 0, true))}</ul>
          : rootModules.length ? <ul className="db-module-tree" role="tree" aria-label="สถานะการทดสอบแต่ละโมดูล">{rootModules.map(m => renderModule(m, 0))}</ul> : <p className="muted-row">ยังไม่มีโมดูลในขอบเขตนี้</p>}
      </article>
      <FollowUpList data={data} notStarted={rootModules.filter(m => { const r = rollOf(m.moduleId); return r.cases > 0 && r.executed === 0; })} />
    </div>
    <FoldCard className="db-qa" title="งาน QA รายคน" subtitle={`รวม ${data.totalExecutionCount.toLocaleString()} Execution · เรียงตามจำนวน Execution ไม่ใช่การจัดอันดับผลงาน`}>
      {data.users.length ? <div className="table-wrap">
        <table className="table-cards db-qa-table">
          <thead><tr><th>ผู้ทดสอบ</th><th>Execution (สัดส่วนงาน)</th><th>ผ่าน</th><th>Fail</th><th>Blocked</th><th>อัตราผ่าน</th><th>ทดสอบล่าสุด</th></tr></thead>
          <tbody>
            {data.users.map(u => { const share = pct(u.executions, data.totalExecutionCount); return <tr key={u.userId}>
              <td><span className="db-qa-name"><span className="db-qa-avatar" aria-hidden="true">{u.displayName.slice(0, 1).toUpperCase()}</span><b>{u.displayName}</b></span></td>
              <td><span className="db-qa-share"><b>{u.executions.toLocaleString()}</b><small>{share}%</small><span aria-hidden="true"><span style={{ width: `${share}%` }} /></span></span></td>
              <td className="num">{u.passed.toLocaleString()}</td>
              <td className={`num${u.failed ? " db-fail" : ""}`}>{u.failed.toLocaleString()}</td>
              <td className={`num${u.blocked ? " db-blocked" : ""}`}>{u.blocked.toLocaleString()}</td>
              <td className="num"><b>{u.passRate}%</b></td>
              <td>{u.lastExecutedAt ? formatThaiDateTime(u.lastExecutedAt, { dateStyle: "short", timeStyle: "short" }) : "-"}</td>
            </tr>; })}
          </tbody>
        </table>
      </div> : <p className="muted-row">ยังไม่มีข้อมูลการทดสอบ</p>}
    </FoldCard>
  </div>;
}
