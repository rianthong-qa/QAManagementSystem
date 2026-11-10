import type { ReactNode } from "react";
import type { DashboardSummary } from "./Dashboard";

/**
 * มุมมองผู้บริหาร / Dev ของ Dashboard (แนว SonarQube Quality Gate + TestRail + Jira) — ใช้ข้อมูล summary เดิม + /dashboard/defect-insights
 * หลัก: หนึ่งตัวเลขหนึ่งนิยาม, ไม่มีข้อมูล = "—" สีเทา (ไม่ใช่ 0% สีแดง), ทุกการ์ดอยู่ใต้แถบขอบเขตเดียวกัน, ตัวเลขกดไปดูรายการได้
 */
export type DefectItem = { defectId: string; defectCode: string; title: string; severity: string; status: string; moduleName?: string | null; foundInBuild?: string | null; createdAt: string; ageDays: number; newInBuild: boolean; crmLinked: boolean; reopenCount: number; resolvedAt?: string | null };
export type DefectInsights = {
  open: number; unresolved: number; inProgress: number; awaitingRetest: number;
  critical: number; high: number; medium: number; low: number;
  newInBuild: number | null; newInBuildHigh: number | null; closed: number; rejected: number; reopened: number; crmLinked: number;
  averageAgeDays: number | null; ageBuckets: number[];
  modules: { moduleId?: string | null; moduleCode?: string | null; moduleName: string; critical: number; high: number; medium: number; low: number; total: number }[];
  trend: { date: string; created: number; closed: number }[];
  attention: DefectItem[]; awaitingRetestItems: DefectItem[];
};
export type OpenModule = (moduleId: string, target: "defects" | "test-cases") => void;
type Nav = { onOpenModule?: OpenModule; onOpenDefect?: (defectId: string) => void; onOpenDefects?: () => void };

const gate = 90;
const fmt = (n: number) => n.toLocaleString();
const sevOrder = ["Critical", "High", "Medium", "Low"] as const;
const sevClass: Record<string, string> = { Critical: "sev-critical", High: "sev-high", Medium: "sev-medium", Low: "sev-low" };
const decisionInfo: Record<string, { icon: string; label: string }> = {
  GO: { icon: "check_circle", label: "พร้อมปล่อย" }, "NO-GO": { icon: "cancel", label: "ยังไม่พร้อมปล่อย" },
  "CONDITIONAL GO": { icon: "error", label: "ปล่อยได้แบบมีเงื่อนไข" }, "NO DATA": { icon: "help", label: "ยังประเมินไม่ได้" },
};
const sevCount = (x: DefectInsights, s: string) => s === "Critical" ? x.critical : s === "High" ? x.high : s === "Medium" ? x.medium : x.low;
const sevSummary = (x: DefectInsights) => sevOrder.filter(s => sevCount(x, s) > 0).map(s => `${s} ${fmt(sevCount(x, s))}`).join(" · ") || "ไม่มี";

/** ปุ่มหรือข้อความ — หน้าลิงก์แชร์ไม่มีปลายทาง จึงแสดงเป็นข้อความธรรมดา */
function Go({ onClick, children, label }: { onClick?: () => void; children: ReactNode; label?: string }) {
  return onClick ? <button type="button" className="dbv-link" onClick={onClick} aria-label={label}>{children}</button> : <>{children}</>;
}

function SeverityBar({ x }: { x: DefectInsights }) {
  return <span className="dbv-bar" role="img" aria-label={sevSummary(x)}>
    {x.open > 0 ? sevOrder.filter(s => sevCount(x, s) > 0).map(s => <span key={s} className={sevClass[s]} style={{ width: `${sevCount(x, s) / x.open * 100}%` }} />) : <span className="empty" />}
  </span>;
}

function InsightsState({ error }: { error: string }) {
  return <p className="muted-row" role={error ? "alert" : "status"}>{error || "กำลังโหลดข้อมูล Defect..."}</p>;
}

/** แนวโน้ม 30 วัน: Defect เข้าใหม่ เทียบ แก้แล้ว/ปิด (แท่งคู่ต่อวัน) */
function TrendChart({ trend }: { trend: DefectInsights["trend"] }) {
  const max = Math.max(1, ...trend.map(t => Math.max(t.created, t.closed)));
  const created = trend.reduce((s, t) => s + t.created, 0), closed = trend.reduce((s, t) => s + t.closed, 0);
  const day = (iso: string) => new Date(`${iso}T00:00:00`).toLocaleDateString("th-TH", { day: "numeric", month: "short" });
  if (created + closed === 0) return <p className="muted-row">ไม่มี Defect เข้าหรือปิดใน 30 วันที่ผ่านมา</p>;
  return <>
    <p className="dbv-trend-sum">30 วัน: เข้าใหม่ <b>{fmt(created)}</b> · แก้แล้ว/ปิด <b>{fmt(closed)}</b>{created > closed ? <span className="dbv-warn"> · ค้างสะสมเพิ่ม {fmt(created - closed)}</span> : null}</p>
    <div className="dbv-trend" role="img" aria-label={`แนวโน้ม 30 วัน Defect เข้าใหม่ ${created} รายการ แก้แล้วหรือปิด ${closed} รายการ`}>
      {trend.map(t => <span key={t.date} className="dbv-trend-day" title={`${day(t.date)} · เข้าใหม่ ${t.created} · แก้แล้ว/ปิด ${t.closed}`}>
        <i className="in" style={{ height: `${t.created / max * 100}%` }} /><i className="out" style={{ height: `${t.closed / max * 100}%` }} />
      </span>)}
    </div>
    <div className="dbv-trend-axis" aria-hidden="true"><span>{day(trend[0].date)}</span><span>{day(trend[trend.length - 1].date)}</span></div>
    <ul className="dbv-legend"><li><i className="in" />Defect เข้าใหม่</li><li><i className="out" />แก้แล้ว / ปิด</li></ul>
  </>;
}

type Criterion = { label: string; rule: string; value: string; state: "pass" | "fail" | "na"; note?: string };

export function ExecutiveView({ data, insights, insightsError, scope, onOpenModule, onOpenDefects }: { data: DashboardSummary; insights: DefectInsights | null; insightsError: string; scope: ReactNode } & Nav) {
  const notRun = Math.max(0, data.totalCases - data.executedCases);
  const hasRuns = data.executedCases > 0;
  const blockers = data.defectBlockersWithoutRisk ?? data.highDefects;
  const criteria: Criterion[] = [
    { label: "P0 ที่ Fail / Blocked", rule: "เกณฑ์ = 0", value: data.totalCases ? fmt(data.openP0) : "—", state: data.totalCases ? (data.openP0 === 0 ? "pass" : "fail") : "na", note: data.totalCases ? undefined : "ยังไม่มีแผนทดสอบ" },
    { label: "Critical Defect ค้าง", rule: "เกณฑ์ = 0", value: fmt(data.criticalDefects), state: data.criticalDefects === 0 ? "pass" : "fail" },
    { label: "High / รอตรวจซ้ำ ที่ยังไม่ยอมรับความเสี่ยง", rule: "เกณฑ์ = 0 หรืออนุมัติ Risk Acceptance", value: fmt(blockers), state: blockers === 0 ? "pass" : "fail", note: data.defectBlockersWithRisk ? `ยอมรับความเสี่ยงแล้ว ${fmt(data.defectBlockersWithRisk)}` : undefined },
    { label: "อัตราผ่าน (Pass Rate)", rule: `เกณฑ์ ≥ ${gate}% ของที่รันแล้ว`, value: hasRuns ? `${data.passRate}%` : "—", state: hasRuns ? (data.passRate >= gate ? "pass" : "fail") : "na", note: hasRuns ? undefined : "ยังไม่มีผลทดสอบ" },
    { label: "ความครอบคลุม Requirement", rule: `เกณฑ์ ≥ ${gate}%`, value: data.totalRequirements ? `${data.requirementCoverage}%` : "—", state: data.totalRequirements ? (data.requirementCoverage >= gate ? "pass" : "fail") : "na", note: data.totalRequirements ? undefined : "ยังไม่มี Requirement ในขอบเขตนี้" },
  ];
  const passed = criteria.filter(x => x.state === "pass").length, measured = criteria.filter(x => x.state !== "na").length;
  const todo = [
    data.totalCases === 0 ? `สร้าง Test Cycle${data.buildNumber ? ` ของ Build ${data.buildNumber}` : ""} เพื่อเริ่มทดสอบ` : notRun > 0 ? `ทดสอบส่วนที่เหลือ ${fmt(notRun)} รายการ` : null,
    data.criticalDefects > 0 ? `แก้ Critical Defect ${fmt(data.criticalDefects)} รายการ` : null,
    data.openP0 > 0 ? `แก้ P0 ที่ Fail/Blocked ${fmt(data.openP0)} รายการ` : null,
    blockers > 0 ? `แก้ หรืออนุมัติความเสี่ยงของ High/รอตรวจซ้ำ ${fmt(blockers)} รายการ` : null,
    hasRuns && data.passRate < gate ? `เพิ่มอัตราผ่านอีก ${Math.round((gate - data.passRate) * 10) / 10}%` : null,
    data.totalRequirements === 0 ? "เพิ่ม Requirement ของ Release เพื่อวัดความครอบคลุม" : null,
  ].filter((x): x is string => Boolean(x));
  const decision = decisionInfo[data.recommendedDecision] ?? { icon: "help", label: data.recommendedDecision };
  const decisionKey = data.recommendedDecision.toLowerCase().replace(" ", "-");
  const progress = data.totalCases ? Math.round(data.executedCases / data.totalCases * 1000) / 10 : null;
  const statusTotal = data.statusDistribution.reduce((s, x) => s + x.count, 0);
  const risks = [
    blockers > 0 && { tone: "danger", value: fmt(blockers), title: "High / รอตรวจซ้ำ ยังไม่ได้แก้", detail: "ยังไม่มี Risk Acceptance — กันการปล่อย", go: onOpenDefects },
    data.criticalDefects > 0 && { tone: "danger", value: fmt(data.criticalDefects), title: "Critical Defect ค้าง", detail: "ต้องแก้ก่อนปล่อยเสมอ", go: onOpenDefects },
    notRun > 0 && { tone: "warning", value: fmt(notRun), title: "รายการทดสอบที่ยังไม่รัน", detail: `${Math.round(notRun / data.totalCases * 100)}% ของแผนทดสอบ` },
    insights && insights.reopened > 0 && { tone: "warning", value: fmt(insights.reopened), title: "Defect ถูกเปิดซ้ำ", detail: "แก้แล้วแต่ตรวจซ้ำไม่ผ่าน", go: onOpenDefects },
    data.totalRequirements === 0 && { tone: "muted", value: "0", title: "Requirement ในขอบเขตนี้", detail: "วัดไม่ได้ว่าทดสอบครอบคลุมงานครบหรือไม่" },
    data.totalCases === 0 && { tone: "muted", value: "0", title: `Test Cycle${data.buildNumber ? ` ของ ${data.buildNumber}` : ""}`, detail: "ยังไม่เริ่มทดสอบ" },
  ].filter(Boolean) as { tone: string; value: string; title: string; detail: string; go?: () => void }[];
  const topModules = (insights?.modules ?? []).slice(0, 6);
  const maxModule = Math.max(1, ...topModules.map(m => m.total));

  return <>
    <section className={`card dbv-verdict decision-${decisionKey}`} aria-label="คำแนะนำการปล่อย Release">
      {scope}
      <div className="dbv-verdict-body">
        <div className="dbv-verdict-main">
          <small>คำแนะนำการปล่อย Release</small>
          <p className="dbv-verdict-title"><span className="material-symbols-outlined" aria-hidden="true">{decision.icon}</span>{decision.label}<span className="dbv-code">{data.recommendedDecision}</span></p>
          <p className="dbv-verdict-reason">{data.decisionReason || "ผ่านเกณฑ์ความพร้อมทั้งหมด"}</p>
          {todo.length > 0 && <div className="dbv-todo"><b>ต้องทำก่อนปล่อย</b><ul>{todo.slice(0, 4).map(x => <li key={x}>{x}</li>)}</ul></div>}
        </div>
        <div className="dbv-gate">
          <p className="dbv-gate-head">เกณฑ์ปล่อย Release · ผ่าน {passed}/{measured}{measured < criteria.length ? ` · ยังวัดไม่ได้ ${criteria.length - measured}` : ""}</p>
          <ul>{criteria.map(c => <li key={c.label} className={`is-${c.state}`}>
            <span className="material-symbols-outlined" aria-hidden="true">{c.state === "pass" ? "check_circle" : c.state === "fail" ? "cancel" : "do_not_disturb_on"}</span>
            <span className="dbv-gate-label">{c.label}<small>{c.rule}{c.note ? ` · ${c.note}` : ""}</small></span>
            <b>{c.value}</b>
            <span className="sr-only">{c.state === "pass" ? "ผ่าน" : c.state === "fail" ? "ไม่ผ่าน" : "ยังวัดไม่ได้"}</span>
          </li>)}</ul>
        </div>
      </div>
    </section>

    <div className="dbv-grid three">
      <article className="card dbv-kpi" aria-label="ความคืบหน้าการทดสอบ">
        <small>ความคืบหน้าการทดสอบ</small>
        {progress === null ? <><p className="dbv-big na">—</p><p className="dbv-sub">ยังไม่มีแผนทดสอบในขอบเขตนี้</p></> : <>
          <p className="dbv-big">{progress}%</p>
          <p className="dbv-sub">รันแล้ว {fmt(data.executedCases)} จาก {fmt(data.totalCases)} รายการในแผน</p>
          <span className="dbv-bar" role="img" aria-label={data.statusDistribution.map(x => `${x.status} ${x.count}`).join(", ")}>
            {data.statusDistribution.filter(x => x.count > 0).map(x => <span key={x.status} style={{ width: `${x.count / Math.max(1, statusTotal) * 100}%`, background: x.color }} />)}
          </span>
          <ul className="dbv-legend">{data.statusDistribution.filter(x => x.count > 0).map(x => <li key={x.status}><i style={{ background: x.color }} />{x.status === "Not Run" ? "ยังไม่รัน" : x.status} {fmt(x.count)}</li>)}</ul>
        </>}
      </article>
      <article className="card dbv-kpi" aria-label="Defect ค้าง">
        <small>Defect ค้าง{data.buildNumber ? " ณ Build นี้" : ""}</small>
        {insights ? <>
          <p className="dbv-big">{fmt(insights.open)}</p>
          <p className="dbv-sub">{sevSummary(insights)}</p>
          <SeverityBar x={insights} />
          {onOpenDefects && insights.open > 0 && <p className="dbv-more"><Go onClick={onOpenDefects}>ดูรายการ {fmt(insights.open)} รายการ →</Go></p>}
        </> : <InsightsState error={insightsError} />}
      </article>
      <article className="card dbv-kpi" aria-label="สุขภาพการแก้ Defect">
        <small>สุขภาพการแก้ Defect</small>
        {insights ? <dl className="dbv-stats">
          <div><dt>อายุเฉลี่ยที่ค้าง</dt><dd>{insights.averageAgeDays === null ? "—" : <>{insights.averageAgeDays}<small> วัน</small></>}</dd></div>
          <div><dt>ปิดแล้ว</dt><dd>{fmt(insights.closed + insights.rejected)}<small> (Rejected {fmt(insights.rejected)})</small></dd></div>
          <div><dt>พบใน Build นี้ (ทุกสถานะ)</dt><dd>{insights.newInBuild === null ? "—" : fmt(insights.newInBuild)}</dd></div>
          <div><dt>รอ QA ตรวจซ้ำ</dt><dd>{fmt(insights.awaitingRetest)}</dd></div>
        </dl> : <InsightsState error={insightsError} />}
      </article>
    </div>

    <div className="dbv-grid two">
      <article className="card" aria-label="แนวโน้ม Defect">
        <div className="db-card-head"><h3>แนวโน้ม Defect เข้า เทียบ แก้แล้ว/ปิด</h3><p>รายวัน 30 วันล่าสุด · แท่งเข้าใหม่สูงกว่าแท่งปิดต่อเนื่อง = งานค้างกำลังสะสม</p></div>
        {insights ? <TrendChart trend={insights.trend} /> : <InsightsState error={insightsError} />}
      </article>
      <article className="card" aria-label="ความเสี่ยงหลัก">
        <div className="db-card-head"><h3>ความเสี่ยงหลัก</h3><p>เรียงตามผลต่อการปล่อย</p></div>
        {risks.length ? <ul className="dbv-risks">{risks.map(r => <li key={r.title} className={`tone-${r.tone}`}>
          <b>{r.value}</b><span>{r.title}<small>{r.detail}</small></span>
          {r.go ? <button type="button" className="dbv-link" onClick={r.go} aria-label={`ดู ${r.title}`}>ดู →</button> : <span />}
        </li>)}</ul> : <p className="muted-row">ไม่มีความเสี่ยงที่ต้องติดตาม</p>}
      </article>
    </div>

    <article className="card" aria-label="โมดูลที่มี Defect ค้างมากสุด">
      <div className="db-card-head"><h3>โมดูลที่มี Defect ค้างมากสุด</h3><p>6 อันดับแรก · ตัวเลข = Defect ที่ยังไม่ปิด{onOpenModule ? " · กดชื่อโมดูลเพื่อดูรายการ" : ""}</p></div>
      {!insights ? <InsightsState error={insightsError} /> : topModules.length ? <ul className="dbv-modules">{topModules.map(m => <li key={m.moduleId ?? "none"}>
        <span className="dbv-module-name">{m.moduleId && onOpenModule ? <button type="button" className="dbv-link" onClick={() => onOpenModule(m.moduleId!, "defects")}>{m.moduleName}</button> : m.moduleName}</span>
        <span className="dbv-module-track" aria-hidden="true"><span style={{ width: `${m.total / maxModule * 100}%` }} /></span>
        <b>{fmt(m.total)}</b>
      </li>)}</ul> : <p className="muted-row">ไม่มี Defect ค้าง</p>}
    </article>

    <details className="card dbv-glossary">
      <summary>คำอธิบายตัวเลข</summary>
      <ul>
        <li><b>ความคืบหน้า</b> = รายการในแผนทดสอบ (Test Cycle) ที่รันแล้ว ÷ ทั้งหมด</li>
        <li><b>อัตราผ่าน</b> = Pass ÷ รายการที่รันแล้ว</li>
        <li><b>Defect ค้าง ณ Build</b> = ยังไม่ปิด (Open / In Progress / Resolved) ที่เจอใน Build นี้หรือ Build ก่อนหน้าของ Release เดียวกัน</li>
        <li><b>เกณฑ์ Defect</b> ในคำตัดสินนับทั้ง Release รวม Build หลังจากนี้ เพราะของค้างทุก Build กันการปล่อย</li>
        <li><b>—</b> สีเทา = ยังไม่มีข้อมูลให้วัด ไม่ใช่ไม่ผ่าน</li>
      </ul>
    </details>
  </>;
}

function DefectRow({ x, onOpenDefect, showResolved }: { x: DefectItem; onOpenDefect?: (id: string) => void; showResolved?: boolean }) {
  return <li>
    <span className={`dbv-sev ${sevClass[x.severity] ?? ""}`}>{x.severity}</span>
    <span className="dbv-item-body">
      <span className="dbv-item-code">{onOpenDefect ? <button type="button" className="dbv-link" onClick={() => onOpenDefect(x.defectId)}>{x.defectCode}</button> : x.defectCode}
        {x.newInBuild && <span className="dbv-chip new">ใหม่ใน Build นี้</span>}{x.reopenCount > 0 && <span className="dbv-chip">เปิดซ้ำ {x.reopenCount}</span>}</span>
      <span className="dbv-item-title" title={x.title}>{x.title}</span>
      <small>{[x.moduleName, x.foundInBuild ? `พบใน ${x.foundInBuild}` : null, x.crmLinked ? "ส่ง CRM แล้ว" : "ยังไม่ส่ง CRM"].filter(Boolean).join(" · ")}</small>
    </span>
    <small className="dbv-age">{showResolved && x.resolvedAt ? `แก้เมื่อ ${new Date(x.resolvedAt).toLocaleDateString("th-TH", { day: "numeric", month: "short" })}` : x.ageDays === 0 ? "วันนี้" : `${x.ageDays} วัน`}</small>
  </li>;
}

export function DevView({ insights, insightsError, scope, mine, onMineChange, buildNumber, onOpenModule, onOpenDefect, onOpenDefects }: { insights: DefectInsights | null; insightsError: string; scope: ReactNode; mine: boolean; onMineChange?: (mine: boolean) => void; buildNumber?: string } & Nav) {
  const buckets = insights?.ageBuckets ?? [0, 0, 0, 0];
  const maxBucket = Math.max(1, ...buckets);
  const kpis = insights ? [
    { label: "Defect ค้างทั้งหมด", value: fmt(insights.open), sub: sevSummary(insights) },
    { label: buildNumber ? `พบใหม่ใน ${buildNumber}` : "พบใหม่ใน Build นี้", value: insights.newInBuild === null ? "—" : fmt(insights.newInBuild), sub: insights.newInBuild === null ? "เลือก Build เพื่อดู" : `ทุกสถานะ · High/Critical ${fmt(insights.newInBuildHigh ?? 0)}` },
    { label: "ยังไม่ได้แก้", value: fmt(insights.unresolved), sub: `กำลังแก้ (In Progress) ${fmt(insights.inProgress)}` },
    { label: "รอ QA ตรวจซ้ำ", value: fmt(insights.awaitingRetest), sub: "Resolved รอผลตรวจใน Build ถัดไป" },
    { label: "ถูกเปิดซ้ำ", value: fmt(insights.reopened), sub: "แก้แล้วแต่ตรวจซ้ำไม่ผ่าน" },
    { label: "ส่ง CRM แล้ว", value: fmt(insights.crmLinked), sub: `จาก ${fmt(insights.open)} ที่ค้าง` },
  ] : [];
  const totals = insights ? { critical: insights.critical, high: insights.high, medium: insights.medium, low: insights.low, total: insights.open } : null;

  return <>
    <section className="card dbv-devhead" aria-label="ขอบเขตข้อมูล">
      {scope}
      {onMineChange && <div className="db-segment" role="group" aria-label="กรองผู้รับผิดชอบ">
        <button type="button" aria-pressed={!mine} onClick={() => onMineChange(false)}><span className="material-symbols-outlined" aria-hidden="true">groups</span>ทั้งทีม</button>
        <button type="button" aria-pressed={mine} onClick={() => onMineChange(true)}><span className="material-symbols-outlined" aria-hidden="true">person</span>ของฉัน</button>
      </div>}
    </section>
    {!insights ? <article className="card"><InsightsState error={insightsError} /></article> : <>
      <div className="dbv-grid kpis">{kpis.map(k => <article key={k.label} className="card dbv-kpi small"><small>{k.label}</small><p className="dbv-big">{k.value}</p><p className="dbv-sub">{k.sub}</p></article>)}</div>

      <article className="card" aria-label="Defect ค้างตามโมดูลและความรุนแรง">
        <div className="db-card-head"><h3>Defect ค้าง: โมดูล × ความรุนแรง</h3><p>ยังไม่ปิด (Open / In Progress / Resolved){onOpenModule ? " · กดชื่อโมดูลเพื่อเปิดหน้า Defect ที่กรองโมดูลนั้น" : ""}</p></div>
        {insights.modules.length ? <div className="table-wrap"><table className="dbv-matrix">
          <thead><tr><th className="dbv-matrix-name">โมดูล</th>{sevOrder.map(s => <th key={s} className="num">{s}</th>)}<th className="num">รวม</th></tr></thead>
          <tbody>
            {insights.modules.map(m => <tr key={m.moduleId ?? "none"}>
              <td>{m.moduleId && onOpenModule ? <button type="button" className="dbv-link" onClick={() => onOpenModule(m.moduleId!, "defects")}>{m.moduleName}</button> : <span className={m.moduleId ? "" : "muted"}>{m.moduleName}</span>}</td>
              {sevOrder.map(s => { const n = s === "Critical" ? m.critical : s === "High" ? m.high : s === "Medium" ? m.medium : m.low; return <td key={s} className="num">{n ? <span className={`dbv-cell ${sevClass[s]}`}>{fmt(n)}</span> : <span className="dbv-zero">0</span>}</td>; })}
              <td className="num"><b>{fmt(m.total)}</b></td>
            </tr>)}
          </tbody>
          {totals && <tfoot><tr><td><b>รวม</b></td><td className="num">{fmt(totals.critical)}</td><td className="num">{fmt(totals.high)}</td><td className="num">{fmt(totals.medium)}</td><td className="num">{fmt(totals.low)}</td><td className="num"><b>{fmt(totals.total)}</b></td></tr></tfoot>}
        </table></div> : <p className="muted-row">ไม่มี Defect ค้าง{mine ? "ที่คุณรับผิดชอบ" : ""}</p>}
      </article>

      <div className="dbv-grid two">
        <article className="card" aria-label="ต้องดูก่อน">
          <div className="db-card-head"><h3>ต้องดูก่อน</h3><p>พบใหม่ใน Build นี้ → ความรุนแรง → ค้างนานสุด (ยังไม่ได้แก้)</p></div>
          {insights.attention.length ? <ul className="dbv-items">{insights.attention.map(x => <DefectRow key={x.defectId} x={x} onOpenDefect={onOpenDefect} />)}</ul> : <p className="muted-row">ไม่มี Defect ที่ยังไม่ได้แก้</p>}
          {onOpenDefects && insights.unresolved > insights.attention.length && <p className="dbv-more"><Go onClick={onOpenDefects}>ดูทั้งหมดในหน้า Defect →</Go></p>}
        </article>
        <div className="dbv-stack">
          <article className="card" aria-label="รอ QA ตรวจซ้ำ">
            <div className="db-card-head"><h3>รอ QA ตรวจซ้ำ</h3><p>Dev แก้แล้ว (Resolved) เรียงจากรอนานสุด</p></div>
            {insights.awaitingRetestItems.length ? <ul className="dbv-items">{insights.awaitingRetestItems.map(x => <DefectRow key={x.defectId} x={x} onOpenDefect={onOpenDefect} showResolved />)}</ul> : <p className="muted-row">ไม่มีรายการที่รอตรวจซ้ำ</p>}
          </article>
          <article className="card" aria-label="อายุ Defect ที่ยังไม่ได้แก้">
            <div className="db-card-head"><h3>อายุ Defect ที่ยังไม่ได้แก้</h3><p>{insights.averageAgeDays === null ? "ไม่มี Defect ที่ยังไม่ได้แก้" : `เฉลี่ย ${insights.averageAgeDays} วัน นับจากวันที่แจ้ง`}</p></div>
            <div className="dbv-age-chart" role="img" aria-label={`≤ 3 วัน ${buckets[0]}, 4–7 วัน ${buckets[1]}, 8–14 วัน ${buckets[2]}, มากกว่า 14 วัน ${buckets[3]}`}>
              {["≤ 3 วัน", "4–7 วัน", "8–14 วัน", "> 14 วัน"].map((label, i) => <span key={label} className={`age-${i}`}><b>{fmt(buckets[i])}</b><i style={{ height: `${buckets[i] / maxBucket * 100}%` }} /><small>{label}</small></span>)}
            </div>
          </article>
        </div>
      </div>
    </>}
  </>;
}
