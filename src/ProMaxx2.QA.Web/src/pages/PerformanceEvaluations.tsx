import { useEffect, useRef, useState, type CSSProperties } from "react";
import { ApiError, apiFetch, isAbortError } from "../api";
import { Badge } from "../components/Badge";
import { DateField } from "../components/DateField";
import { ModalShell } from "../components/ModalShell";
import { confirmDialog, notify, promptDialog } from "../components/dialogStore";
import { errorText, evalStatus, fmtDate, recommendationLabel, roundText, thaiToday, type Metrics } from "./performanceUtils";

export type EvaluationSummary = {
  evaluationId: string; userId: string; userName?: string | null; type: string; round: string; year: number;
  periodStart: string; periodEnd: string; dueDate: string; status: string; overallScore?: number | null; grade?: string | null;
  recommendation?: string | null; evaluatorName?: string | null; completedAt?: string | null; acknowledgedAt?: string | null; isOverdue: boolean;
};
type Score = { evaluationScoreId: string; group: string; name: string; description?: string | null; weight: number; selfScore?: number | null; selfComment?: string | null; managerScore?: number | null; managerComment?: string | null; externalAverage?: number | null; externalCount?: number };
/** ผู้ประเมินจากแผนกอื่น 1 คน — code มีเฉพาะลิงก์ที่ยังรอส่ง (หัวหน้าตรงเท่านั้น) */
type ExternalReview = { reviewId: string; reviewerName: string; reviewerDepartment?: string | null; note?: string | null; status: "Pending" | "Submitted" | "Revoked" | "Expired"; expiresAt: string; createdAt: string; submittedAt?: string | null; weightedScore?: number | null; comment?: string | null; viewCount: number; code?: string | null };
type EvaluationEmployee = { employeeCode?: string | null; position?: string | null; level?: string | null; hireDate?: string | null; supervisorName?: string | null };
type EvaluationDetail = {
  summary: EvaluationSummary; employee?: EvaluationEmployee; templateName: string; selfComment?: string | null; selfSubmittedAt?: string | null; strengths?: string | null; improvements?: string | null;
  nextGoals?: string | null; extendToDate?: string | null; employeeComment?: string | null; reopenReason?: string | null; probationEndDate?: string | null;
  scores: Score[]; metrics: Metrics; metricsIsSnapshot: boolean; canSelfEdit: boolean; canManagerEdit: boolean; canAcknowledge: boolean; canReopen: boolean; managerRestriction?: string | null;
  externalWeight: number; managerScore?: number | null; externalScore?: number | null; externalReviews?: ExternalReview[] | null; canManageExternal: boolean;
};
type Criterion = { group: string; name: string; description?: string | null; weight: number };
type ComparisonRound = { evaluationId: string; type: string; round: string; year: number; periodStart: string; periodEnd: string; overallScore: number; grade: string; groups: { group: string; score: number }[] };
type Comparison = { groups: string[]; rounds: ComparisonRound[] };
type Template = { evaluationTemplateId: string; type: string; name: string; isActive: boolean; criteria: Criterion[]; externalWeight: number };

const scaleLabel = ["", "ต้องปรับปรุงมาก", "ต่ำกว่าที่คาดหวัง", "ตามที่คาดหวัง", "ดีกว่าที่คาดหวัง", "ดีเยี่ยม"];
const criterionGroups = ["ปริมาณงาน", "คุณภาพงาน", "ความรู้และทักษะ", "พฤติกรรม", "การสื่อสาร"];

/** รายการการประเมิน — หัวหน้าเห็นทุกคน (กรองเฉพาะที่ค้างได้), พนักงานเห็นของตัวเอง; ส่ง userId เพื่อแสดงประวัติของคนเดียว */
export function EvaluationsPanel({ canManage, userId, refreshKey, onChanged, compact = false }: {
  canManage: boolean; userId?: string; refreshKey: number; onChanged: () => void; compact?: boolean;
}) {
  const [items, setItems] = useState<EvaluationSummary[]>([]);
  const [openOnly, setOpenOnly] = useState(!userId && canManage);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [selected, setSelected] = useState<string | null>(null);
  // เปิดหน้าต่างประเมินพร้อมฟอร์ม "ขอให้หัวหน้าแผนกอื่นประเมิน" (กดจากปุ่มแชร์ในรายการ)
  const [shareOnOpen, setShareOnOpen] = useState(false);
  const openEvaluation = (id: string, share = false) => { setShareOnOpen(share); setSelected(id); };
  const canShare = (x: EvaluationSummary) => canManage && x.status !== "Completed" && x.status !== "Acknowledged";
  const shareButton = (x: EvaluationSummary) => canShare(x) && (
    <button className="btn perf-eval-share" type="button" onClick={() => openEvaluation(x.evaluationId, true)} aria-label={`แชร์ลิงก์ให้หัวหน้าแผนกอื่นประเมิน ${roundText(x)} ${x.userName ?? ""}`} title="สร้างลิงก์ให้หัวหน้าแผนกอื่นประเมิน (ไม่ต้อง login)">
      <span className="material-symbols-outlined" aria-hidden="true">share</span>แชร์ให้แผนกอื่น
    </button>
  );
  const [quarterOpen, setQuarterOpen] = useState(false);
  const [templatesOpen, setTemplatesOpen] = useState(false);
  const [statusFilter, setStatusFilter] = useState("all");

  useEffect(() => {
    const controller = new AbortController();
    const query = new URLSearchParams({ openOnly: String(openOnly) });
    if (userId) query.set("userId", userId);
    setLoading(true);
    setError("");
    apiFetch<EvaluationSummary[]>(`/performance/evaluations?${query}`, { signal: controller.signal })
      .then(setItems)
      .catch((e) => { if (!isAbortError(e)) setError(errorText(e, "โหลดรายการประเมินไม่สำเร็จ")); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [userId, openOnly, refreshKey]);

  const overdue = items.filter((x) => x.isOverdue).length;
  // มุมมองทีม: แยกการ์ดรายพนักงาน (คนที่มีรอบเลยกำหนดมากก่อน) ภายในการ์ดเรียงตามกำหนดส่ง — ไม่ให้รอบของหลายคนปนกัน
  const grouped = !userId;
  const today = thaiToday();
  const statusCount = (status: string) => items.filter((x) => x.status === status).length;
  const shown = statusFilter === "all" ? items : statusFilter === "overdue" ? items.filter((x) => x.isOverdue) : items.filter((x) => x.status === statusFilter);
  const groups = Object.values(shown.reduce<Record<string, { userId: string; userName?: string | null; items: EvaluationSummary[]; overdue: number }>>((acc, x) => {
    const g = acc[x.userId] ??= { userId: x.userId, userName: x.userName, items: [], overdue: 0 };
    g.items.push(x);
    if (x.isOverdue) g.overdue++;
    return acc;
  }, {}))
    .map((g) => ({ ...g, items: [...g.items].sort((a, b) => a.dueDate.localeCompare(b.dueDate)) }))
    .sort((a, b) => b.overdue - a.overdue || (a.items[0]?.dueDate ?? "").localeCompare(b.items[0]?.dueDate ?? "") || (a.userName ?? "").localeCompare(b.userName ?? "", "th"));
  const overdueDays = (dueDate: string) => Math.max(0, Math.round((Date.parse(today) - Date.parse(dueDate.slice(0, 10))) / 86_400_000));
  const toggleStatus = (value: string) => setStatusFilter((v) => (v === value ? "all" : value));
  const renderRow = (x: EvaluationSummary) => (
    <tr key={x.evaluationId} className={x.isOverdue ? "perf-eval-overdue" : undefined}>
      <td>{compact ? roundText(x) : <><b className="perf-eval-round">{roundText(x)}</b><small className="perf-sub">{fmtDate(x.periodStart)} – {fmtDate(x.periodEnd)}</small></>}</td>
      <td>{fmtDate(x.dueDate)}{x.isOverdue && <small className="perf-eval-late">เลยมา {overdueDays(x.dueDate).toLocaleString("th-TH")} วัน</small>}</td>
      <td><Badge tone={evalStatus[x.status]?.tone ?? "blue"}>{evalStatus[x.status]?.label ?? x.status}</Badge></td>
      <td>
        {x.grade ? <span className="perf-grade">{x.grade}<small>{x.overallScore?.toFixed(2)}</small></span> : "-"}
        {x.recommendation && <small className="perf-sub">{recommendationLabel[x.recommendation]}</small>}
      </td>
      <td className="perf-row-action"><span className="perf-eval-round-actions">{shareButton(x)}<button className="btn" type="button" onClick={() => openEvaluation(x.evaluationId)} aria-label={`เปิดการประเมิน ${roundText(x)} ${x.userName ?? ""}`}>ดูการประเมิน<span className="material-symbols-outlined" aria-hidden="true">arrow_forward</span></button></span></td>
    </tr>
  );
  // แถบขั้นตอน: รอประเมินตนเอง → รอหัวหน้า → ส่งผลแล้ว → รับทราบ + เลยกำหนด — กดเพื่อกรอง (กดซ้ำเพื่อยกเลิก)
  const steps: { value: string; icon: string; label: string; count: number; tone: string }[] = [
    { value: "Open", icon: "edit_note", label: evalStatus.Open.label, count: statusCount("Open"), tone: "warning" },
    { value: "SelfSubmitted", icon: "pending_actions", label: evalStatus.SelfSubmitted.label, count: statusCount("SelfSubmitted"), tone: "info" },
    { value: "Completed", icon: "task", label: evalStatus.Completed.label, count: statusCount("Completed"), tone: "purple" },
    { value: "Acknowledged", icon: "verified", label: evalStatus.Acknowledged.label, count: statusCount("Acknowledged"), tone: "green" },
  ];
  return (
    <>
    <section className={`content-card perf-evaluations${grouped ? " perf-evaluations-team" : ""}`} aria-label="การประเมิน">
      <div className="perf-section-head">
        <div>
          <h3><span className="material-symbols-outlined" aria-hidden="true">rate_review</span>{userId ? "ประวัติการประเมิน" : "รอบการประเมิน"}</h3>
          <p>{loading ? "กำลังโหลด..." : `${items.length.toLocaleString("th-TH")} รายการ${overdue ? ` · เลยกำหนด ${overdue}` : ""}`}</p>
        </div>
        <div className="perf-eval-actions">
          {!userId && (
            <label className="perf-check"><input type="checkbox" checked={openOnly} onChange={(e) => { setOpenOnly(e.target.checked); setStatusFilter("all"); }} />เฉพาะที่ยังไม่ปิด</label>
          )}
          {canManage && !userId && (
            <>
              <button className="btn" type="button" onClick={() => setTemplatesOpen(true)}><span className="material-symbols-outlined" aria-hidden="true">tune</span>แบบประเมิน</button>
              <button className="btn primary" type="button" onClick={() => setQuarterOpen(true)}><span className="material-symbols-outlined" aria-hidden="true">add</span>สร้างรอบรายไตรมาส</button>
            </>
          )}
        </div>
      </div>
      {grouped && !loading && !error && items.length > 0 && (
        <div className="perf-eval-pipeline" role="group" aria-label="กรองตามสถานะ (นับจากรายการที่แสดง)">
          <ol>
            {steps.map((s) => (
              <li key={s.value}>
                <button type="button" className={`tone-${s.tone}`} aria-pressed={statusFilter === s.value} onClick={() => toggleStatus(s.value)} disabled={s.count === 0 && statusFilter !== s.value}>
                  <span className="material-symbols-outlined" aria-hidden="true">{s.icon}</span>
                  <span><b>{s.count.toLocaleString("th-TH")}</b><small>{s.label}</small></span>
                </button>
              </li>
            ))}
          </ol>
          <button type="button" className={`perf-eval-pipeline-overdue${overdue ? " has" : ""}`} aria-pressed={statusFilter === "overdue"} onClick={() => toggleStatus("overdue")} disabled={overdue === 0 && statusFilter !== "overdue"}>
            <span className="material-symbols-outlined" aria-hidden="true">event_busy</span>
            <span><b>{overdue.toLocaleString("th-TH")}</b><small>เลยกำหนด</small></span>
          </button>
        </div>
      )}
      {grouped && statusFilter !== "all" && (
        <p className="perf-eval-filter-note" role="status">
          แสดง {shown.length.toLocaleString("th-TH")} จาก {items.length.toLocaleString("th-TH")} รายการ · {statusFilter === "overdue" ? "เลยกำหนด" : evalStatus[statusFilter]?.label}
          <button className="btn" type="button" onClick={() => setStatusFilter("all")}>แสดงทั้งหมด</button>
        </p>
      )}
      {error ? <div className="inline-alert error" role="alert"><span>{error}</span></div>
        : loading && items.length === 0 ? <div className="perf-feedback" role="status">กำลังโหลด...</div>
          : items.length === 0 ? (
            <div className="empty-state">
              <span className="material-symbols-outlined" aria-hidden="true">rate_review</span>
              <b>{openOnly ? "ไม่มีการประเมินที่ค้างอยู่" : "ยังไม่มีการประเมิน"}</b>
              <small>{canManage ? "รอบทดลองงานถูกสร้างอัตโนมัติเมื่อตั้งสถานะพนักงานเป็นทดลองงาน · รอบไตรมาสสร้างจากปุ่มด้านบน" : "เมื่อหัวหน้าเปิดรอบประเมิน รายการจะแสดงที่นี่"}</small>
            </div>
          ) : grouped ? (
            <ul className="perf-eval-people">
              {groups.map((g) => (
                <li key={g.userId} className={g.overdue ? "has-overdue" : undefined}>
                  <div className="perf-eval-person-head">
                    <span className="perf-person">
                      <span className="perf-avatar" aria-hidden="true">{(g.userName ?? "?").slice(0, 1).toUpperCase()}</span>
                      <span><b>{g.userName ?? "-"}</b><small>{g.items.length.toLocaleString("th-TH")} รอบ{g.overdue ? "" : " · ไม่มีรอบที่เลยกำหนด"}</small></span>
                    </span>
                    {g.overdue > 0 && <Badge tone="red">เลยกำหนด {g.overdue.toLocaleString("th-TH")} รอบ</Badge>}
                  </div>
                  <ul className="perf-eval-rounds">
                    {g.items.map((x) => (
                      <li key={x.evaluationId} className={x.isOverdue ? "overdue" : undefined}>
                        <span className="perf-eval-round-name"><b>{roundText(x)}</b><small>{fmtDate(x.periodStart)} – {fmtDate(x.periodEnd)}</small></span>
                        <span className="perf-eval-round-due"><small>กำหนดส่ง</small>{fmtDate(x.dueDate)}{x.isOverdue && <small className="perf-eval-late">เลยมา {overdueDays(x.dueDate).toLocaleString("th-TH")} วัน</small>}</span>
                        <span className="perf-eval-round-status"><Badge tone={evalStatus[x.status]?.tone ?? "blue"}>{evalStatus[x.status]?.label ?? x.status}</Badge></span>
                        <span className="perf-eval-round-result">
                          {x.grade ? <span className="perf-grade">{x.grade}<small>{x.overallScore?.toFixed(2)}</small></span> : <small className="perf-sub">ยังไม่มีผล</small>}
                          {x.recommendation && <small className="perf-sub">{recommendationLabel[x.recommendation]}</small>}
                        </span>
                        <span className="perf-eval-round-actions">
                          {shareButton(x)}
                          <button className="btn" type="button" onClick={() => openEvaluation(x.evaluationId)} aria-label={`เปิดการประเมิน ${roundText(x)} ${x.userName ?? ""}`}>ดูการประเมิน<span className="material-symbols-outlined" aria-hidden="true">arrow_forward</span></button>
                        </span>
                      </li>
                    ))}
                  </ul>
                </li>
              ))}
            </ul>
          ) : (
            <div className="table-wrap">
              <table className="table-cards perf-eval-table">
                <thead>
                  <tr><th>รอบ</th><th>กำหนดส่ง</th><th>สถานะ</th><th>ผล</th><th><span className="sr-only">เปิด</span></th></tr>
                </thead>
                <tbody>{items.map(renderRow)}</tbody>
              </table>
            </div>
          )}
      {selected && <EvaluationModal id={selected} startShare={shareOnOpen} onClose={() => { setSelected(null); setShareOnOpen(false); }} onChanged={onChanged} />}
      {quarterOpen && <QuarterModal onClose={() => setQuarterOpen(false)} onCreated={() => { setQuarterOpen(false); onChanged(); }} />}
      {templatesOpen && <TemplatesModal onClose={() => setTemplatesOpen(false)} />}
    </section>
    {canManage && !userId && <TeamQuarterComparison refreshKey={refreshKey} onOpen={(id) => openEvaluation(id)} />}
    </>
  );
}

type TeamQuarterCell = { round: string; evaluationId?: string | null; status?: string | null; overallScore?: number | null; grade?: string | null; isOverdue: boolean };
type TeamComparisonRow = { userId: string; displayName: string; position?: string | null; employmentStatus: string; quarters: TeamQuarterCell[]; averageScore?: number | null; previousYearQ4Score?: number | null };
type TeamComparison = { year: number; rows: TeamComparisonRow[]; quarterAverages: (number | null)[] };

/** ลูกศรเทียบกับรอบก่อนหน้า (ต่างกันน้อยกว่า 0.05 ถือว่าคงที่) — ใช้ไอคอน + ข้อความ ไม่สื่อด้วยสีอย่างเดียว */
function Trend({ current, previous }: { current?: number | null; previous?: number | null }) {
  if (current == null || previous == null) return null;
  const diff = Math.round((current - previous) * 100) / 100;
  const dir = Math.abs(diff) < 0.05 ? "flat" : diff > 0 ? "up" : "down";
  const label = dir === "flat" ? "เท่ากับรอบก่อน" : `${dir === "up" ? "เพิ่มขึ้น" : "ลดลง"} ${Math.abs(diff).toFixed(2)} จากรอบก่อน`;
  return <span className={`perf-trend-arrow ${dir}`} title={label} aria-label={label}><span className="material-symbols-outlined" aria-hidden="true">{dir === "up" ? "arrow_upward" : dir === "down" ? "arrow_downward" : "remove"}</span>{dir !== "flat" && Math.abs(diff).toFixed(2)}</span>;
}

/** ตารางเทียบผลรายไตรมาสของทั้งทีม (หัวหน้า) — คลิกช่องที่มีรอบประเมินเพื่อเปิดรายละเอียด */
function TeamQuarterComparison({ refreshKey, onOpen }: { refreshKey: number; onOpen: (evaluationId: string) => void }) {
  const currentYear = Number(thaiToday().slice(0, 4));
  const [year, setYear] = useState(currentYear);
  const [data, setData] = useState<TeamComparison | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError("");
    apiFetch<TeamComparison>(`/performance/evaluations/team-comparison?year=${year}`, { signal: controller.signal })
      .then(setData)
      .catch((e) => { if (!isAbortError(e)) setError(errorText(e, "โหลดผลเทียบรายไตรมาสไม่สำเร็จ")); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [year, refreshKey]);

  const quarters = ["Q1", "Q2", "Q3", "Q4"];
  return (
    <section className="content-card perf-team-quarters" aria-label="เทียบผลรายไตรมาสทั้งทีม">
      <div className="perf-section-head">
        <div><h3><span className="material-symbols-outlined" aria-hidden="true">query_stats</span>เทียบผลรายไตรมาสทั้งทีม</h3><p>คะแนนรวม (1–5) และเกรดของรอบที่ส่งผลแล้ว · ลูกศรเทียบกับไตรมาสก่อนหน้า</p><span className="perf-heat-legend" aria-hidden="true">คะแนนต่ำ<span className="perf-heat-scale" />คะแนนสูง</span></div>
        <label className="perf-year-pick"><span>ปี</span>
          <select value={year} onChange={(e) => setYear(Number(e.target.value))} aria-label="เลือกปีที่เทียบ">
            {Array.from({ length: 5 }, (_, i) => currentYear - i).map((y) => <option key={y} value={y}>{y + 543}</option>)}
          </select>
        </label>
      </div>
      {error ? <div className="inline-alert error" role="alert"><span>{error}</span></div>
        : loading && !data ? <div className="perf-feedback" role="status">กำลังโหลด...</div>
          : !data || data.rows.length === 0 ? <div className="empty-state"><span className="material-symbols-outlined" aria-hidden="true">table_chart</span><b>ยังไม่มีรอบประเมินรายไตรมาสในปี {year + 543}</b><small>สร้างรอบจากปุ่ม "สร้างรอบรายไตรมาส" ด้านล่าง</small></div>
            : (
              <div className="table-wrap">
                <table className="table-cards perf-quarter-table">
                  <thead>
                    <tr><th>พนักงาน</th>{quarters.map((q) => <th key={q}>{roundText({ round: q, year })}</th>)}<th>เฉลี่ยทั้งปี</th></tr>
                  </thead>
                  <tbody>
                    {data.rows.map((row) => (
                      <tr key={row.userId}>
                        <td><span className="perf-person"><span className="perf-avatar" aria-hidden="true">{row.displayName.slice(0, 1).toUpperCase()}</span><span><b>{row.displayName}</b><small className="perf-sub">{row.position || ""}</small></span></span></td>
                        {row.quarters.map((cell, i) => {
                          const previous = i === 0 ? row.previousYearQ4Score : row.quarters[i - 1].overallScore;
                          return (
                            <td key={cell.round} className={`perf-quarter-cell${cell.grade ? " has-score" : ""}`} style={cell.grade && cell.overallScore != null ? { "--perf-heat": `${Math.round(6 + (Math.max(0, cell.overallScore - 1) / 4) * 26)}%` } as CSSProperties : undefined}>
                              {!cell.evaluationId ? <span className="perf-quarter-empty" aria-label="ไม่มีรอบประเมิน">–</span>
                                : (
                                  <button type="button" className="perf-quarter-open" onClick={() => onOpen(cell.evaluationId!)} aria-label={`เปิดการประเมิน ${roundText({ round: cell.round, year })} ของ ${row.displayName}`}>
                                    {cell.grade
                                      ? <><span className="perf-grade">{cell.grade}<small>{cell.overallScore?.toFixed(2)}</small></span><Trend current={cell.overallScore} previous={previous} /></>
                                      : <Badge tone={cell.isOverdue ? "red" : evalStatus[cell.status ?? ""]?.tone ?? "blue"}>{cell.isOverdue ? "เลยกำหนด" : evalStatus[cell.status ?? ""]?.label ?? cell.status}</Badge>}
                                  </button>
                                )}
                            </td>
                          );
                        })}
                        <td className="num">{row.averageScore != null ? <b>{row.averageScore.toFixed(2)}</b> : "–"}</td>
                      </tr>
                    ))}
                  </tbody>
                  <tfoot>
                    <tr><th scope="row">ค่าเฉลี่ยทีม</th>{data.quarterAverages.map((avg, i) => <td key={quarters[i]} className="num" data-label={roundText({ round: quarters[i], year })}>{avg != null ? avg.toFixed(2) : "–"}</td>)}<td /></tr>
                  </tfoot>
                </table>
              </div>
            )}
    </section>
  );
}

type ScoreDraft = { self: number | null; selfComment: string; manager: number | null; managerComment: string };

function EvaluationModal({ id, startShare = false, onClose, onChanged }: { id: string; startShare?: boolean; onClose: () => void; onChanged: () => void }) {
  // เพิ่มค่าทุกครั้งที่ต้องการเปิดฟอร์มสร้างลิงก์แผนกอื่น + เลื่อนไปที่ส่วนนั้น (ปุ่มในรายการ / ปุ่มแถบล่าง)
  const [shareSignal, setShareSignal] = useState(startShare ? 1 : 0);
  const [detail, setDetail] = useState<EvaluationDetail | null>(null);
  const [drafts, setDrafts] = useState<Record<string, ScoreDraft>>({});
  const [form, setForm] = useState({ selfComment: "", strengths: "", improvements: "", nextGoals: "", recommendation: "", extendToDate: "", employeeComment: "" });
  const [loadError, setLoadError] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  const apply = (d: EvaluationDetail) => {
    setDetail(d);
    setDrafts(Object.fromEntries(d.scores.map((s) => [s.evaluationScoreId, { self: s.selfScore ?? null, selfComment: s.selfComment ?? "", manager: s.managerScore ?? null, managerComment: s.managerComment ?? "" }])));
    setForm({ selfComment: d.selfComment ?? "", strengths: d.strengths ?? "", improvements: d.improvements ?? "", nextGoals: d.nextGoals ?? "", recommendation: d.summary.recommendation ?? "", extendToDate: d.extendToDate?.slice(0, 10) ?? "", employeeComment: d.employeeComment ?? "" });
  };

  useEffect(() => {
    const controller = new AbortController();
    apiFetch<EvaluationDetail>(`/performance/evaluations/${id}`, { signal: controller.signal })
      .then(apply)
      .catch((e) => { if (!isAbortError(e)) setLoadError(errorText(e, "โหลดการประเมินไม่สำเร็จ")); });
    return () => controller.abort();
  }, [id]);

  const setDraft = (scoreId: string, patch: Partial<ScoreDraft>) => setDrafts((d) => ({ ...d, [scoreId]: { ...d[scoreId], ...patch } }));
  const set = (patch: Partial<typeof form>) => setForm((f) => ({ ...f, ...patch }));

  const run = async (action: () => Promise<EvaluationDetail>, success: string) => {
    setBusy(true);
    setError("");
    try {
      apply(await action());
      notify(success, "success");
      onChanged();
    } catch (e) {
      setError(errorText(e, "บันทึกไม่สำเร็จ"));
    } finally {
      setBusy(false);
    }
  };

  const saveSelf = async (submit: boolean) => {
    if (!detail) return;
    if (submit && !await confirmDialog({ title: "ส่งแบบประเมินตนเอง", message: "ส่งแล้วจะแก้คะแนนตนเองไม่ได้ ยืนยันส่ง?", confirmLabel: "ส่ง" })) return;
    await run(() => apiFetch<EvaluationDetail>(`/performance/evaluations/${id}/self`, {
      method: "PUT", fallbackMessage: "บันทึกการประเมินตนเองไม่สำเร็จ",
      json: { scores: detail.scores.map((s) => ({ evaluationScoreId: s.evaluationScoreId, score: drafts[s.evaluationScoreId]?.self, comment: drafts[s.evaluationScoreId]?.selfComment || null })), comment: form.selfComment || null, submit },
    }), submit ? "ส่งแบบประเมินตนเองแล้ว" : "บันทึกร่างแล้ว");
  };

  const saveManager = async (complete: boolean) => {
    if (!detail) return;
    if (complete && !await confirmDialog({
      title: "ส่งผลการประเมิน",
      message: (detail.summary.round === "PFinal" && form.recommendation
        ? `ส่งผลแล้วพนักงานจะเห็นคะแนนทันที และระบบจะบันทึกผลทดลองงานเป็น "${recommendationLabel[form.recommendation]}"${form.recommendation === "Pass" ? " (เปลี่ยนเป็นพนักงานประจำ)" : form.recommendation === "Extend" ? " และสร้างรอบสรุปผลใหม่" : ""} ยืนยัน?`
        : "ส่งผลแล้วพนักงานจะเห็นคะแนนและความเห็นทันที และแก้ไขไม่ได้จนกว่าจะเปิดใหม่ ยืนยัน?") +
        (pendingExternal > 0 ? `\n\nยังมีผู้ประเมินจากแผนกอื่นที่ยังไม่ส่ง ${pendingExternal} คน — ระบบจะคำนวณจากคนที่ส่งแล้ว และปิดลิงก์ของคนที่ยังไม่ส่ง` : ""),
      confirmLabel: "ส่งผล",
    })) return;
    await run(() => apiFetch<EvaluationDetail>(`/performance/evaluations/${id}/manager`, {
      method: "PUT", fallbackMessage: "บันทึกผลการประเมินไม่สำเร็จ",
      json: {
        scores: detail.scores.map((s) => ({ evaluationScoreId: s.evaluationScoreId, score: drafts[s.evaluationScoreId]?.manager, comment: drafts[s.evaluationScoreId]?.managerComment || null })),
        strengths: form.strengths || null, improvements: form.improvements || null, nextGoals: form.nextGoals || null,
        recommendation: form.recommendation || null, extendToDate: form.recommendation === "Extend" ? form.extendToDate || null : null, complete,
      },
    }), complete ? "ส่งผลการประเมินแล้ว" : "บันทึกร่างแล้ว");
  };

  const acknowledge = () => run(() => apiFetch<EvaluationDetail>(`/performance/evaluations/${id}/acknowledge`, { method: "POST", json: { comment: form.employeeComment || null }, fallbackMessage: "รับทราบผลไม่สำเร็จ" }), "รับทราบผลการประเมินแล้ว");

  const reopen = async () => {
    const reason = await promptDialog({ title: "เปิดการประเมินใหม่", message: "ระบุเหตุผล (บันทึกใน Audit Log) — ผลต่อสถานะพนักงานจากรอบสรุปที่บันทึกไปแล้วจะไม่ถูกย้อนกลับ", required: true, multiline: true, confirmLabel: "เปิดใหม่" });
    if (!reason) return;
    await run(() => apiFetch<EvaluationDetail>(`/performance/evaluations/${id}/reopen`, { method: "POST", json: { reason }, fallbackMessage: "เปิดการประเมินใหม่ไม่สำเร็จ" }), "เปิดการประเมินใหม่แล้ว");
  };

  const [exporting, setExporting] = useState(false);
  const exportPdf = async () => {
    if (!detail) return;
    const d = detail;
    setExporting(true);
    try {
      const { exportEvaluationPdf } = await import("./EvaluationPdf");
      await exportEvaluationPdf({
        roundTitle: roundText(d.summary), statusLabel: evalStatus[d.summary.status]?.label ?? d.summary.status, isClosed: d.summary.status === "Completed" || d.summary.status === "Acknowledged",
        employeeName: d.summary.userName ?? "-", employee: d.employee ?? {}, templateName: d.templateName,
        periodStart: d.summary.periodStart, periodEnd: d.summary.periodEnd, dueDate: d.summary.dueDate, evaluatorName: d.summary.evaluatorName,
        overallScore: d.summary.overallScore, grade: d.summary.grade, recommendation: d.summary.recommendation ? recommendationLabel[d.summary.recommendation] : null, extendToDate: d.extendToDate,
        completedAt: d.summary.completedAt, acknowledgedAt: d.summary.acknowledgedAt,
        selfComment: d.selfComment, strengths: d.strengths, improvements: d.improvements, nextGoals: d.nextGoals, employeeComment: d.employeeComment,
        scores: d.scores, metrics: d.metrics, metricsIsSnapshot: d.metricsIsSnapshot,
      });
    } catch (e) {
      notify(errorText(e, "สร้าง PDF ไม่สำเร็จ"), "error");
    } finally {
      setExporting(false);
    }
  };

  const s = detail?.summary;
  const groups = detail ? criterionGroupsOf(detail.scores) : [];
  const selfComplete = detail?.scores.every((x) => drafts[x.evaluationScoreId]?.self != null) ?? false;
  const managerComplete = detail?.scores.every((x) => drafts[x.evaluationScoreId]?.manager != null) ?? false;
  const needsDecision = s?.round === "PFinal" && (!form.recommendation || (form.recommendation === "Extend" && !form.extendToDate));
  const showManagerColumn = !!detail && (detail.canManagerEdit || detail.scores.some((x) => x.managerScore != null));
  const weighted = detail && managerComplete ? weightedScore(detail.scores, (x) => drafts[x.evaluationScoreId]?.manager ?? 0) : null;
  const selfAverage = detail && selfComplete ? weightedScore(detail.scores, (x) => drafts[x.evaluationScoreId]?.self ?? 0) : null;
  const managerScored = detail ? detail.scores.filter((x) => drafts[x.evaluationScoreId]?.manager != null).length : 0;
  // ถ้าส่งตอนนี้: หัวหน้าตรง × (100 − w)% + ค่าเฉลี่ยแผนกอื่นที่ส่งแล้ว × w% (สูตรเดียวกับ Evaluation.CombineScores)
  const combinedPreview = weighted != null && detail ? combineScores(weighted, detail.externalScore ?? null, detail.externalWeight) : null;
  const pendingExternal = detail?.externalReviews?.filter((x) => x.status === "Pending").length ?? 0;

  return (
    <ModalShell labelledBy="eval-title" className="perf-modal perf-eval-modal" onDismiss={() => { if (!busy) onClose(); }}>
      <div className="modal-head">
        <div>
          <h2 id="eval-title">{s ? `${roundText(s)} · ${s.userName ?? ""}` : "การประเมิน"}</h2>
          {s && <small>{fmtDate(s.periodStart)} – {fmtDate(s.periodEnd)} · กำหนดส่ง {fmtDate(s.dueDate)} · <Badge tone={evalStatus[s.status]?.tone ?? "blue"}>{evalStatus[s.status]?.label ?? s.status}</Badge></small>}
        </div>
        <button type="button" aria-label="ปิด" disabled={busy} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
      </div>
      {loadError ? <div className="inline-alert error" role="alert"><span>{loadError}</span></div>
        : !detail || !s ? <div className="perf-feedback" role="status">กำลังโหลด...</div>
          : (
            <div className="perf-eval-body">
              <div className="perf-eval-main">
                <EvaluationSummaryBar detail={detail} selfAverage={selfAverage} managerPreview={detail.canManagerEdit ? combinedPreview : null} managerScored={managerScored} />
                {detail.managerRestriction && !s.grade && <div className="inline-alert" role="note"><span><span className="material-symbols-outlined" aria-hidden="true">lock</span> {detail.managerRestriction}</span></div>}
                {detail.reopenReason && <div className="inline-alert" role="note"><span>เปิดใหม่: {detail.reopenReason}</span></div>}
                {detail.externalReviews && <ExternalReviewsSection detail={detail} disabled={busy} openSignal={shareSignal} onChanged={(next) => { setDetail((d) => d ? { ...d, ...next } : d); onChanged(); }} />}

                <ul className="perf-eval-legend" aria-label="ความหมายของคะแนน">
                  {[1, 2, 3, 4, 5].map((n) => <li key={n}><b>{n}</b>{scaleLabel[n]}</li>)}
                </ul>

                {groups.map(([group, scores]) => (
                  <section key={group} className="perf-eval-group" aria-label={group}>
                    <h3>{group}<small>น้ำหนักรวม {scores.reduce((sum, x) => sum + x.weight, 0)}%</small></h3>
                    <div className={`perf-eval-rows${showManagerColumn ? " two" : ""}`}>
                      <div className="perf-eval-row perf-eval-row-head" aria-hidden="true">
                        <span>เกณฑ์</span><span>ประเมินตนเอง</span>{showManagerColumn && <span>หัวหน้า</span>}
                      </div>
                      {scores.map((x) => {
                        const d = drafts[x.evaluationScoreId];
                        return (
                          <div key={x.evaluationScoreId} className="perf-eval-row">
                            <div className="perf-eval-crit">
                              <b>{x.name}</b>
                              {x.description && <p>{x.description}</p>}
                              <span className="perf-eval-weight">น้ำหนัก {x.weight}%</span>
                              {!!x.externalCount && x.externalAverage != null && <span className="perf-eval-ext" title="ค่าเฉลี่ยจากหัวหน้าแผนกอื่นที่ส่งผลแล้ว"><span className="material-symbols-outlined" aria-hidden="true">groups</span>แผนกอื่นเฉลี่ย <b>{x.externalAverage.toFixed(1)}</b> ({x.externalCount} คน)</span>}
                            </div>
                            <div className="perf-eval-cell" data-label="ประเมินตนเอง">
                              {detail.canSelfEdit
                                ? <ScorePicker label={`ประเมินตนเอง: ${x.name}`} name={`self-${x.evaluationScoreId}`} value={d?.self ?? null} disabled={busy} onChange={(v) => setDraft(x.evaluationScoreId, { self: v })} comment={d?.selfComment ?? ""} onComment={(v) => setDraft(x.evaluationScoreId, { selfComment: v })} />
                                : <ScoreValue value={d?.self ?? null} comment={d?.selfComment} empty={s.status === "Open" ? "ยังไม่ได้ประเมิน" : "ไม่ได้ประเมินตนเอง"} />}
                            </div>
                            {showManagerColumn && (
                              <div className="perf-eval-cell" data-label="หัวหน้า">
                                {detail.canManagerEdit
                                  ? <ScorePicker label={`หัวหน้า: ${x.name}`} name={`mgr-${x.evaluationScoreId}`} value={d?.manager ?? null} disabled={busy} onChange={(v) => setDraft(x.evaluationScoreId, { manager: v })} comment={d?.managerComment ?? ""} onComment={(v) => setDraft(x.evaluationScoreId, { managerComment: v })} />
                                  : <ScoreValue value={d?.manager ?? null} comment={d?.managerComment} empty="ยังไม่ได้ให้คะแนน" />}
                              </div>
                            )}
                          </div>
                        );
                      })}
                    </div>
                  </section>
                ))}

                <section className="perf-eval-group perf-eval-notes" aria-label="ความเห็น">
                  <h3>ความเห็น</h3>
                  {detail.canSelfEdit
                    ? <label className="perf-eval-field">ความเห็นของพนักงาน (ประเมินตนเอง)<textarea rows={3} maxLength={4000} value={form.selfComment} disabled={busy} onChange={(e) => set({ selfComment: e.target.value })} placeholder="ผลงานที่ภูมิใจ อุปสรรค หรือสิ่งที่อยากพัฒนา" /></label>
                    : <CommentBlock title="ความเห็นของพนักงาน (ประเมินตนเอง)" text={detail.selfComment} />}
                  {detail.canManagerEdit ? (
                    <>
                      <label className="perf-eval-field">จุดแข็ง<textarea rows={2} maxLength={4000} value={form.strengths} disabled={busy} onChange={(e) => set({ strengths: e.target.value })} /></label>
                      <label className="perf-eval-field">สิ่งที่ควรพัฒนา<textarea rows={2} maxLength={4000} value={form.improvements} disabled={busy} onChange={(e) => set({ improvements: e.target.value })} /></label>
                      <label className="perf-eval-field">เป้าหมายรอบถัดไป<textarea rows={2} maxLength={4000} value={form.nextGoals} disabled={busy} onChange={(e) => set({ nextGoals: e.target.value })} /></label>
                    </>
                  ) : (
                    <>
                      <CommentBlock title="จุดแข็ง" text={detail.strengths} />
                      <CommentBlock title="สิ่งที่ควรพัฒนา" text={detail.improvements} />
                      <CommentBlock title="เป้าหมายรอบถัดไป" text={detail.nextGoals} />
                    </>
                  )}
                  {s.round === "PFinal" && detail.canManagerEdit && (
                    <div className="perf-eval-decision">
                      <label className="perf-eval-field">ผลการทดลองงาน<span className="required-mark"> *</span>
                        <select value={form.recommendation} disabled={busy} onChange={(e) => set({ recommendation: e.target.value })}>
                          <option value="">เลือกผล</option>
                          {Object.entries(recommendationLabel).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
                        </select>
                      </label>
                      {form.recommendation === "Extend" && (
                        <label className="perf-eval-field">ขยายถึงวันที่<span className="required-mark"> *</span>
                          <DateField value={form.extendToDate} min={detail.probationEndDate?.slice(0, 10)} disabled={busy} onChange={(extendToDate) => set({ extendToDate })} />
                          <small className="perf-hint">สิ้นสุดเดิม {fmtDate(detail.probationEndDate)} — การขยายเกิน 119 วันมีผลต่อสิทธิ์ค่าชดเชย ควรยืนยันกับ HR</small>
                        </label>
                      )}
                    </div>
                  )}
                  {detail.canAcknowledge
                    ? <label className="perf-eval-field">ความเห็นต่อผลการประเมิน (ไม่บังคับ)<textarea rows={2} maxLength={4000} value={form.employeeComment} disabled={busy} onChange={(e) => set({ employeeComment: e.target.value })} /></label>
                    : <CommentBlock title="ความเห็นของพนักงานต่อผลการประเมิน" text={detail.employeeComment} />}
                </section>
              </div>

              <aside className="perf-eval-side" aria-label="หลักฐานจากระบบ">
                <h3>หลักฐานจากระบบ</h3>
                <small className="perf-hint">{detail.metricsIsSnapshot ? "ตัวเลข ณ วันที่ส่งผล" : "ตัวเลขล่าสุดในช่วงประเมิน (จะถูกบันทึกตอนส่งผล)"}</small>
                <EvidenceSection icon="play_circle" title="การทดสอบ" rows={[["Execution", detail.metrics.executions], ["Fail / Blocked", `${detail.metrics.failed} / ${detail.metrics.blocked}`], ["Test Case ที่รัน", detail.metrics.distinctTestCases]]} />
                <EvidenceSection icon="fact_check" title="ออกแบบเทส" rows={[["Test Case ที่สร้าง / แก้", `${detail.metrics.testCasesCreated} / ${detail.metrics.testCaseRevisions}`], ["Requirement", detail.metrics.requirementsCreated]]} />
                <EvidenceSection icon="bug_report" title="Defect" rows={[["ที่แจ้ง", detail.metrics.defectsReported], ["Critical / High", `${detail.metrics.defectsCritical} / ${detail.metrics.defectsHigh}`], ["ถูก Reject", detail.metrics.defectsRejected]]} />
                <EvidenceSection icon="support_agent" title="CRM" rows={detail.metrics.hasCrmAccount ? [["Ticket ที่ดูแล", detail.metrics.crmTicketsHandled], ["ส่ง Dev / กลับ Support", `${detail.metrics.crmHandedToDev} / ${detail.metrics.crmReturnedToSupport}`], ["ปิดงาน / ส่งกลับแก้", `${detail.metrics.crmClosed} / ${detail.metrics.crmEditErr ?? 0}`], ["เวลาเฉลี่ยกับ QA", detail.metrics.crmAvgQaHours != null ? `${detail.metrics.crmAvgQaHours.toLocaleString("th-TH", { maximumFractionDigits: 1 })} ชม.` : "-"]] : [["บัญชี CRM", "ยังไม่ได้ผูก"]]} />
                <EvidenceSection icon="edit_note" title="บันทึกงาน" rows={[["บันทึก / ผลงานเด่น", `${detail.metrics.workLogs} / ${detail.metrics.highlights}`], ["Automation ที่สร้าง", detail.metrics.automationCasesCreated]]} />
              </aside>
            </div>
          )}
      {error && <div className="inline-alert error" role="alert"><span>{error}</span></div>}
      <div className="modal-actions">
        <button className="btn" type="button" disabled={busy} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span> ปิด</button>
        {detail && (
          <button className="btn" type="button" disabled={busy || exporting} onClick={exportPdf} title="บันทึกผลการประเมินเป็น PDF (ข้อมูลตามที่แสดงในหน้าต่างนี้ที่บันทึกแล้ว)">
            {exporting ? <><span className="spinner inline" aria-hidden="true" /> กำลังสร้าง PDF...</> : <><span className="material-symbols-outlined" aria-hidden="true">picture_as_pdf</span> Export PDF</>}
          </button>
        )}
        {detail?.canManageExternal && s?.status !== "Completed" && s?.status !== "Acknowledged" && (
          <button className="btn" type="button" disabled={busy} onClick={() => setShareSignal((n) => n + 1)} title="สร้างลิงก์ให้หัวหน้าแผนกอื่นประเมิน (ไม่ต้อง login)"><span className="material-symbols-outlined" aria-hidden="true">share</span> แชร์ให้แผนกอื่นประเมิน</button>
        )}
        {detail?.canReopen && <button className="btn" type="button" disabled={busy} onClick={reopen}><span className="material-symbols-outlined" aria-hidden="true">lock_open</span> เปิดใหม่</button>}
        {detail?.canSelfEdit && (
          <>
            <button className="btn" type="button" disabled={busy} onClick={() => saveSelf(false)}>บันทึกร่าง</button>
            <button className="btn primary" type="button" disabled={busy || !selfComplete} title={selfComplete ? undefined : "ให้คะแนนตนเองให้ครบทุกข้อก่อนส่ง"} onClick={() => saveSelf(true)}><span className="material-symbols-outlined" aria-hidden="true">send</span> ส่งแบบประเมินตนเอง</button>
          </>
        )}
        {detail?.canManagerEdit && (
          <>
            <button className="btn" type="button" disabled={busy} onClick={() => saveManager(false)}>บันทึกร่าง</button>
            <button className="btn primary" type="button" disabled={busy || !managerComplete || needsDecision} title={!managerComplete ? "ให้คะแนนครบทุกข้อก่อนส่งผล" : needsDecision ? "ระบุผลการทดลองงานก่อนส่งผล" : undefined} onClick={() => saveManager(true)}><span className="material-symbols-outlined" aria-hidden="true">task_alt</span> ส่งผลประเมิน</button>
          </>
        )}
        {detail?.canAcknowledge && <button className="btn primary" type="button" disabled={busy} onClick={acknowledge}><span className="material-symbols-outlined" aria-hidden="true">done_all</span> รับทราบผล</button>}
      </div>
    </ModalShell>
  );
}

/** เทียบผลการประเมินที่ส่งแล้วของพนักงานหนึ่งคน — คอลัมน์ = รอบ (เก่า → ใหม่), แถว = คะแนนรวม + กลุ่มเกณฑ์, แสดงส่วนต่างจากรอบก่อนหน้า */
export function EvaluationComparison({ userId, refreshKey }: { userId: string; refreshKey: number }) {
  const [data, setData] = useState<Comparison | null>(null);
  const [error, setError] = useState("");
  const [type, setType] = useState<"all" | "Quarterly" | "Probation">("all");
  const [unavailable, setUnavailable] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    setError("");
    apiFetch<Comparison>(`/performance/users/${userId}/evaluation-comparison`, { signal: controller.signal })
      .then(setData)
      .catch((e) => {
        if (isAbortError(e)) return;
        // 404 = API ยังเป็นรุ่นก่อนมี endpoint นี้ — ซ่อนส่วนนี้แทนการแสดง error ค้างบนหน้า
        if (e instanceof ApiError && e.status === 404) setUnavailable(true);
        else setError(errorText(e, "โหลดผลเปรียบเทียบไม่สำเร็จ"));
      });
    return () => controller.abort();
  }, [userId, refreshKey]);

  // ยังไม่มีรอบที่ปิดเลย = ไม่มีอะไรให้เทียบ — ไม่แสดงการ์ดว่าง (ประวัติการประเมินด้านบนบอกสถานะรอบอยู่แล้ว)
  if (unavailable || (data && data.rounds.length === 0)) return null;

  const hasBothTypes = !!data && data.rounds.some((r) => r.type === "Quarterly") && data.rounds.some((r) => r.type === "Probation");
  const rounds = (data?.rounds ?? []).filter((r) => type === "all" || !hasBothTypes || r.type === type);
  const groups = (data?.groups ?? []).filter((g) => rounds.some((r) => r.groups.some((x) => x.group === g)));
  const scoreOf = (r: ComparisonRound, group: string) => r.groups.find((x) => x.group === group)?.score ?? null;

  return (
    <section className="content-card perf-compare" aria-label="เทียบผลการประเมิน">
      <div className="perf-section-head">
        <div><h3>เทียบผลการประเมิน</h3><p>เฉพาะรอบที่ส่งผลแล้ว (ล่าสุด 8 รอบ) · คะแนนหัวหน้า 1–5 เฉลี่ยถ่วงน้ำหนักรายกลุ่มเกณฑ์ · ลูกศรเทียบกับรอบก่อนหน้า</p></div>
        {hasBothTypes && (
          <label className="perf-compare-filter">
            <span>ประเภท</span>
            <select value={type} onChange={(e) => setType(e.target.value as typeof type)}>
              <option value="all">ทุกรอบ</option>
              <option value="Quarterly">รายไตรมาส</option>
              <option value="Probation">ทดลองงาน</option>
            </select>
          </label>
        )}
      </div>
      {error ? <div className="inline-alert error" role="alert"><span>{error}</span></div>
        : !data ? <div className="perf-feedback" role="status">กำลังโหลด...</div>
          : rounds.length === 0 ? (
            <div className="empty-state">
              <span className="material-symbols-outlined" aria-hidden="true">compare_arrows</span>
              <b>ยังไม่มีผลการประเมินที่ส่งแล้ว</b>
              <small>เมื่อหัวหน้าส่งผลการประเมินแล้ว คะแนนแต่ละรอบจะแสดงเทียบกันที่นี่</small>
            </div>
          ) : (
            <div className="table-wrap">
              <table className="table-cards perf-compare-table">
                <thead>
                  <tr><th>หัวข้อ</th>{rounds.map((r) => <th key={r.evaluationId} className="num">{roundText(r)}<small className="perf-sub">{fmtDate(r.periodStart)} – {fmtDate(r.periodEnd)}</small></th>)}</tr>
                </thead>
                <tbody>
                  <tr className="perf-compare-total">
                    <td>คะแนนรวม</td>
                    {rounds.map((r, i) => <td key={r.evaluationId} className="num" data-label={roundText(r)}><b>{r.overallScore.toFixed(2)}</b> <span className="perf-compare-grade">เกรด {r.grade}</span><Delta value={r.overallScore} prev={i > 0 ? rounds[i - 1].overallScore : null} /></td>)}
                  </tr>
                  {groups.map((g) => (
                    <tr key={g}>
                      <td>{g}</td>
                      {rounds.map((r, i) => {
                        const v = scoreOf(r, g);
                        return <td key={r.evaluationId} className="num" data-label={roundText(r)}>{v == null ? "-" : <>{v.toFixed(2)}<Delta value={v} prev={i > 0 ? scoreOf(rounds[i - 1], g) : null} /></>}</td>;
                      })}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
    </section>
  );
}

function Delta({ value, prev }: { value: number; prev: number | null }) {
  if (prev == null) return null;
  const diff = Math.round((value - prev) * 100) / 100;
  if (diff === 0) return <small className="perf-delta same">คงที่</small>;
  return (
    <small className={`perf-delta ${diff > 0 ? "up" : "down"}`}>
      <span aria-hidden="true">{diff > 0 ? "▲" : "▼"}</span>
      <span className="sr-only">{diff > 0 ? "เพิ่มขึ้น" : "ลดลง"}</span>{Math.abs(diff).toFixed(2)}
    </small>
  );
}

/** ตัวเลือกคะแนน 1–5 (ใช้เฉพาะช่องที่แก้ได้) — แสดงความหมายของคะแนนที่เลือกใต้ปุ่ม */
function ScorePicker({ label, name, value, disabled, onChange, comment, onComment }: {
  label: string; name: string; value: number | null; disabled: boolean; onChange: (v: number) => void; comment: string; onComment: (v: string) => void;
}) {
  return (
    <div className="perf-score">
      <div className="perf-score-options" role="radiogroup" aria-label={label}>
        {[1, 2, 3, 4, 5].map((n) => (
          <label key={n} className={value === n ? "selected" : ""} title={scaleLabel[n]}>
            <input type="radio" name={name} value={n} checked={value === n} disabled={disabled} onChange={() => onChange(n)} />
            <span aria-hidden="true">{n}</span>
            <span className="sr-only">{n} {scaleLabel[n]}</span>
          </label>
        ))}
      </div>
      <small className={`perf-score-meaning${value ? "" : " empty"}`}>{value ? scaleLabel[value] : "ยังไม่ได้เลือกคะแนน"}</small>
      <input className="perf-score-comment" value={comment} maxLength={1000} disabled={disabled} onChange={(e) => onComment(e.target.value)} placeholder="ความเห็น (ไม่บังคับ)" aria-label={`ความเห็น — ${label}`} />
    </div>
  );
}

/** คะแนนแบบอ่านอย่างเดียว — ตัวเลข + ความหมาย (ไม่แสดงปุ่ม 1–5 ที่กดไม่ได้) */
function ScoreValue({ value, comment, empty }: { value: number | null; comment?: string | null; empty: string }) {
  return (
    <div className="perf-score-readonly">
      {value
        ? <span className="perf-score-value"><b>{value}</b><span>{scaleLabel[value]}</span></span>
        : <span className="perf-score-value empty">{empty}</span>}
      {comment && <p className="perf-score-note">“{comment}”</p>}
    </div>
  );
}

function CommentBlock({ title, text }: { title: string; text?: string | null }) {
  if (!text) return null;
  return <div className="perf-eval-comment"><span>{title}</span><p>{text}</p></div>;
}

function EvidenceSection({ icon, title, rows }: { icon: string; title: string; rows: [string, number | string][] }) {
  return (
    <section className="perf-eval-evidence">
      <h4><span className="material-symbols-outlined" aria-hidden="true">{icon}</span>{title}</h4>
      <dl>{rows.map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{typeof value === "number" ? value.toLocaleString("th-TH") : value}</dd></div>)}</dl>
    </section>
  );
}

/** แถบสรุปบนสุดของหน้าต่างประเมิน — ปิดแล้ว: เกรด/คะแนน/ผลทดลองงาน · ยังไม่ปิด: สถานะการประเมินตนเองและความคืบหน้าของหัวหน้า */
function EvaluationSummaryBar({ detail, selfAverage, managerPreview, managerScored }: { detail: EvaluationDetail; selfAverage: number | null; managerPreview: number | null; managerScored: number }) {
  const s = detail.summary;
  const total = detail.scores.length;
  if (s.grade) {
    return (
      <div className="perf-evm-summary closed">
        <span className="perf-grade large" aria-label={`เกรด ${s.grade}`}>{s.grade}</span>
        <div className="perf-evm-summary-main">
          <b>คะแนนรวม {s.overallScore?.toFixed(2)} <small>/ 5</small></b>
          <span>ประเมินโดย {s.evaluatorName ?? "-"}{s.completedAt && ` · ส่งผล ${fmtDate(s.completedAt)}`}{s.acknowledgedAt && ` · พนักงานรับทราบ ${fmtDate(s.acknowledgedAt)}`}</span>
        </div>
        <div className="perf-evm-summary-stats">
          {detail.managerScore != null && detail.externalScore != null && <>
            <div><span>หัวหน้าตรง ({100 - detail.externalWeight}%)</span><b>{detail.managerScore.toFixed(2)}</b></div>
            <div><span>แผนกอื่น ({detail.externalWeight}%)</span><b>{detail.externalScore.toFixed(2)}</b></div>
          </>}
          {selfAverage != null && <div><span>ตนเองเฉลี่ย</span><b>{selfAverage.toFixed(2)}</b></div>}
          {s.recommendation && <div><span>ผลทดลองงาน</span><Badge tone={s.recommendation === "Pass" ? "green" : s.recommendation === "Extend" ? "yellow" : "red"}>{recommendationLabel[s.recommendation]}{detail.extendToDate ? ` ถึง ${fmtDate(detail.extendToDate)}` : ""}</Badge></div>}
        </div>
      </div>
    );
  }
  return (
    <div className="perf-evm-summary">
      <div className="perf-evm-summary-step">
        <span className="material-symbols-outlined" aria-hidden="true">{detail.summary.status === "SelfSubmitted" ? "task_alt" : "pending"}</span>
        <div><span>ประเมินตนเอง</span><b>{detail.summary.status === "SelfSubmitted" ? `ส่งแล้ว${selfAverage != null ? ` · เฉลี่ย ${selfAverage.toFixed(2)}` : ""}` : "ยังไม่ส่ง"}</b></div>
      </div>
      <div className="perf-evm-summary-step">
        <span className="material-symbols-outlined" aria-hidden="true">{managerScored === total ? "task_alt" : "pending"}</span>
        <div><span>หัวหน้าให้คะแนน</span><b>{managerScored}/{total} เกณฑ์</b></div>
      </div>
      {managerPreview != null && (
        <div className="perf-evm-summary-step preview">
          <span className="perf-grade">{gradeFor(managerPreview)}</span>
          <div><span>ถ้าส่งตอนนี้{detail.externalScore != null ? ` (รวมแผนกอื่น ${detail.externalWeight}%)` : ""}</span><b>{managerPreview.toFixed(2)} / 5</b></div>
        </div>
      )}
    </div>
  );
}

function QuarterModal({ onClose, onCreated }: { onClose: () => void; onCreated: () => void }) {
  const today = thaiToday();
  const [year, month] = today.split("-").map(Number);
  // ค่าเริ่มต้น = ไตรมาสที่เพิ่งจบ (ประเมินย้อนหลังเมื่อจบไตรมาส)
  const currentQuarter = Math.floor((month - 1) / 3) + 1;
  const [form, setForm] = useState({ year: currentQuarter === 1 ? year - 1 : year, quarter: currentQuarter === 1 ? 4 : currentQuarter - 1 });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const create = async () => {
    setBusy(true);
    setError("");
    try {
      const r = await apiFetch<{ created: number; skipped: number }>("/performance/evaluations/quarterly", { method: "POST", json: form, fallbackMessage: "สร้างรอบประเมินไม่สำเร็จ" });
      notify(r.created ? `สร้างรอบประเมิน ${r.created} คน${r.skipped ? ` (มีอยู่แล้ว ${r.skipped} คน)` : ""}` : "ทุกคนมีรอบนี้อยู่แล้ว", r.created ? "success" : "info");
      onCreated();
    } catch (e) {
      setError(errorText(e, "สร้างรอบประเมินไม่สำเร็จ"));
    } finally {
      setBusy(false);
    }
  };
  return (
    <ModalShell labelledBy="quarter-title" className="perf-modal perf-modal-small" onDismiss={() => { if (!busy) onClose(); }}>
      <div className="modal-head">
        <div><h2 id="quarter-title">สร้างรอบประเมินรายไตรมาส</h2><small>สร้างให้พนักงานประจำทุกคนในทีม (คนที่มีรอบนี้แล้วจะข้าม) กำหนดส่ง 14 วันหลังจบไตรมาส</small></div>
        <button type="button" aria-label="ปิด" disabled={busy} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
      </div>
      <div className="form-grid">
        <label>ปี (ค.ศ.)<input type="number" min={2000} max={2100} value={form.year} onChange={(e) => setForm((f) => ({ ...f, year: Number(e.target.value) }))} /></label>
        <label>ไตรมาส
          <select value={form.quarter} onChange={(e) => setForm((f) => ({ ...f, quarter: Number(e.target.value) }))}>
            {[1, 2, 3, 4].map((q) => <option key={q} value={q}>ไตรมาส {q} ({["ม.ค.–มี.ค.", "เม.ย.–มิ.ย.", "ก.ค.–ก.ย.", "ต.ค.–ธ.ค."][q - 1]})</option>)}
          </select>
        </label>
      </div>
      {error && <div className="inline-alert error" role="alert"><span>{error}</span></div>}
      <div className="modal-actions">
        <button className="btn" type="button" disabled={busy} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิก</button>
        <button className="btn primary" type="button" disabled={busy || form.year < 2000 || form.year > 2100} onClick={create}>
          {busy ? <><span className="spinner inline" aria-hidden="true" /> กำลังสร้าง...</> : <><span className="material-symbols-outlined" aria-hidden="true">check</span> สร้างรอบประเมิน</>}
        </button>
      </div>
    </ModalShell>
  );
}

function TemplatesModal({ onClose }: { onClose: () => void }) {
  const [templates, setTemplates] = useState<Template[]>([]);
  const [activeId, setActiveId] = useState("");
  const [name, setName] = useState("");
  const [criteria, setCriteria] = useState<Criterion[]>([]);
  const [externalWeight, setExternalWeight] = useState(30);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    const controller = new AbortController();
    apiFetch<Template[]>("/performance/templates", { signal: controller.signal })
      .then((list) => { setTemplates(list); if (list[0]) select(list[0]); })
      .catch((e) => { if (!isAbortError(e)) setError(errorText(e, "โหลดแบบประเมินไม่สำเร็จ")); });
    return () => controller.abort();
  }, []);

  function select(t: Template) {
    setActiveId(t.evaluationTemplateId);
    setName(t.name);
    setCriteria(t.criteria.map((c) => ({ ...c })));
    setExternalWeight(t.externalWeight ?? 30);
    setError("");
  }

  const total = criteria.reduce((sum, c) => sum + (Number(c.weight) || 0), 0);
  const valid = name.trim() !== "" && criteria.length > 0 && criteria.length <= 15 && total === 100 && criteria.every((c) => c.name.trim() && c.weight >= 1 && c.weight <= 100) && externalWeight >= 0 && externalWeight <= 50;
  const update = (i: number, patch: Partial<Criterion>) => setCriteria((list) => list.map((c, idx) => idx === i ? { ...c, ...patch } : c));
  const move = (i: number, dir: -1 | 1) => setCriteria((list) => { const next = [...list]; const j = i + dir; if (j < 0 || j >= next.length) return list; [next[i], next[j]] = [next[j], next[i]]; return next; });

  const save = async () => {
    setBusy(true);
    setError("");
    try {
      const saved = await apiFetch<Template>(`/performance/templates/${activeId}`, { method: "PUT", json: { name: name.trim(), externalWeight, criteria: criteria.map((c) => ({ ...c, name: c.name.trim(), description: c.description?.trim() || null })) }, fallbackMessage: "บันทึกแบบประเมินไม่สำเร็จ" });
      setTemplates((list) => list.map((t) => t.evaluationTemplateId === saved.evaluationTemplateId ? saved : t));
      notify("บันทึกแบบประเมินแล้ว — มีผลกับรอบประเมินที่สร้างหลังจากนี้", "success");
    } catch (e) {
      setError(errorText(e, "บันทึกแบบประเมินไม่สำเร็จ"));
    } finally {
      setBusy(false);
    }
  };

  return (
    <ModalShell labelledBy="templates-title" className="perf-modal perf-eval-modal" onDismiss={() => { if (!busy) onClose(); }}>
      <div className="modal-head">
        <div><h2 id="templates-title">แบบประเมิน</h2><small>แก้แล้วมีผลกับรอบที่สร้างใหม่เท่านั้น รอบเดิมเก็บเกณฑ์ของตัวเองไว้</small></div>
        <button type="button" aria-label="ปิด" disabled={busy} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
      </div>
      <div className="perf-tabs perf-template-tabs" role="tablist" aria-label="ประเภทแบบประเมิน">
        {templates.map((t) => (
          <button key={t.evaluationTemplateId} role="tab" type="button" aria-selected={t.evaluationTemplateId === activeId} className={t.evaluationTemplateId === activeId ? "active" : ""} disabled={busy} onClick={() => select(t)}>
            {t.type === "Probation" ? "ทดลองงาน" : "รายไตรมาส"}
          </button>
        ))}
      </div>
      {activeId && (
        <div className="perf-template-editor">
          <div className="perf-template-head">
            <label className="perf-template-name">ชื่อแบบประเมิน<input value={name} maxLength={200} onChange={(e) => setName(e.target.value)} /></label>
            <label className="perf-template-external">น้ำหนักคะแนนจากแผนกอื่น
              <span className="perf-weight"><input type="number" min={0} max={50} value={externalWeight} aria-describedby="perf-external-weight-hint" onChange={(e) => setExternalWeight(Number(e.target.value))} />%</span>
              <small id="perf-external-weight-hint" className="perf-hint">หัวหน้าตรง {100 - (externalWeight || 0)}% · 0–50% · 0 = ใช้เป็นข้อมูลอ้างอิงเท่านั้น · ไม่มีใครส่ง = หัวหน้าตรง 100%</small>
            </label>
          </div>
          <ol className="perf-template-list">
            {criteria.map((c, i) => (
              <li key={i}>
                <select value={c.group} aria-label={`กลุ่มของเกณฑ์ที่ ${i + 1}`} onChange={(e) => update(i, { group: e.target.value })}>
                  {criterionGroups.map((g) => <option key={g} value={g}>{g}</option>)}
                </select>
                <input value={c.name} maxLength={200} aria-label={`ชื่อเกณฑ์ที่ ${i + 1}`} placeholder="ชื่อเกณฑ์" onChange={(e) => update(i, { name: e.target.value })} />
                <input value={c.description ?? ""} maxLength={500} aria-label={`คำอธิบายเกณฑ์ที่ ${i + 1}`} placeholder="คำอธิบาย (ไม่บังคับ)" onChange={(e) => update(i, { description: e.target.value })} />
                <label className="perf-weight"><input type="number" min={1} max={100} value={c.weight} aria-label={`น้ำหนักเกณฑ์ที่ ${i + 1} (%)`} onChange={(e) => update(i, { weight: Number(e.target.value) })} />%</label>
                <span className="perf-template-row-actions">
                  <button type="button" onClick={() => move(i, -1)} disabled={i === 0} aria-label={`เลื่อนเกณฑ์ที่ ${i + 1} ขึ้น`}><span className="material-symbols-outlined" aria-hidden="true">arrow_upward</span></button>
                  <button type="button" onClick={() => move(i, 1)} disabled={i === criteria.length - 1} aria-label={`เลื่อนเกณฑ์ที่ ${i + 1} ลง`}><span className="material-symbols-outlined" aria-hidden="true">arrow_downward</span></button>
                  <button type="button" onClick={() => setCriteria((list) => list.filter((_, idx) => idx !== i))} disabled={criteria.length <= 1} aria-label={`ลบเกณฑ์ที่ ${i + 1}`}><span className="material-symbols-outlined" aria-hidden="true">delete</span></button>
                </span>
              </li>
            ))}
          </ol>
          <div className="perf-template-foot">
            <button className="btn" type="button" disabled={criteria.length >= 15} onClick={() => setCriteria((list) => [...list, { group: criterionGroups[0], name: "", description: "", weight: 5 }])}><span className="material-symbols-outlined" aria-hidden="true">add</span>เพิ่มเกณฑ์</button>
            <span className={total === 100 ? "perf-weight-total ok" : "perf-weight-total"} role="status">น้ำหนักรวม {total}% {total === 100 ? "✓" : "(ต้องเท่ากับ 100%)"}</span>
          </div>
        </div>
      )}
      {error && <div className="inline-alert error" role="alert"><span>{error}</span></div>}
      <div className="modal-actions">
        <button className="btn" type="button" disabled={busy} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span> ปิด</button>
        <button className="btn primary" type="button" disabled={busy || !valid} onClick={save}>
          {busy ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">check</span> บันทึกแบบประเมิน</>}
        </button>
      </div>
    </ModalShell>
  );
}

function criterionGroupsOf(scores: Score[]): [string, Score[]][] {
  const map = new Map<string, Score[]>();
  for (const s of scores) map.set(s.group, [...(map.get(s.group) ?? []), s]);
  return [...map.entries()];
}

function weightedScore(scores: Score[], value: (s: Score) => number): number {
  const total = scores.reduce((sum, s) => sum + s.weight, 0);
  return total ? Math.round((scores.reduce((sum, s) => sum + value(s) * s.weight, 0) / total) * 100) / 100 : 0;
}

/** ต้องตรงกับ Evaluation.CombineScores ฝั่ง backend */
function combineScores(manager: number, external: number | null, weight: number): number {
  return external != null && weight > 0 ? Math.round((manager * (100 - weight) / 100 + external * weight / 100) * 100) / 100 : manager;
}

const externalStatus: Record<ExternalReview["status"], { label: string; tone: string }> = {
  Pending: { label: "รอประเมิน", tone: "yellow" }, Submitted: { label: "ส่งแล้ว", tone: "green" }, Revoked: { label: "ยกเลิกแล้ว", tone: "gray" }, Expired: { label: "หมดอายุ", tone: "gray" },
};
const externalLink = (code: string) => `${window.location.origin}/?er=${code}`;

/** ผู้ประเมินจากแผนกอื่น — หัวหน้าตรงสร้างลิงก์ 1 ลิงก์ต่อ 1 คน (ไม่ต้อง login) คัดลอก/ยกเลิกได้ และเห็นผลที่ส่งแล้ว */
function ExternalReviewsSection({ detail, disabled, openSignal, onChanged }: { detail: EvaluationDetail; disabled: boolean; openSignal: number; onChanged: (patch: Partial<EvaluationDetail>) => void }) {
  const reviews = detail.externalReviews ?? [];
  const closed = detail.summary.status === "Completed" || detail.summary.status === "Acknowledged";
  const canCreate = detail.canManageExternal && !closed;
  const [form, setForm] = useState({ reviewerName: "", reviewerDepartment: "", note: "", expiresInDays: 14 });
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const submitted = reviews.filter((x) => x.status === "Submitted");
  const pending = reviews.filter((x) => x.status === "Pending").length;
  const sectionRef = useRef<HTMLElement>(null);
  const nameRef = useRef<HTMLInputElement>(null);
  useEffect(() => {
    if (!openSignal) return;
    if (canCreate) setOpen(true);
    // รอให้ฟอร์มแสดงก่อนแล้วเลื่อน + focus ช่องชื่อ
    const t = window.setTimeout(() => { sectionRef.current?.scrollIntoView({ behavior: "smooth", block: "start" }); nameRef.current?.focus({ preventScroll: true }); }, 60);
    return () => window.clearTimeout(t);
  }, [openSignal, canCreate]);

  const reload = async () => {
    const next = await apiFetch<EvaluationDetail>(`/performance/evaluations/${detail.summary.evaluationId}`);
    onChanged({ externalReviews: next.externalReviews, externalScore: next.externalScore, scores: next.scores });
  };
  const copy = async (code: string, name: string) => {
    try { await navigator.clipboard.writeText(externalLink(code)); notify(`คัดลอกลิงก์ของ ${name} แล้ว — ส่งให้ผู้ประเมินทางอีเมลหรือแชต`, "success"); }
    catch { await promptDialog({ title: "คัดลอกลิงก์", message: "คัดลอกลิงก์ด้านล่างไปส่งให้ผู้ประเมิน", initialValue: externalLink(code), confirmLabel: "ปิด" }); }
  };
  const create = async () => {
    setBusy(true); setError("");
    try {
      const r = await apiFetch<ExternalReview>(`/performance/evaluations/${detail.summary.evaluationId}/external-reviews`, {
        method: "POST", fallbackMessage: "สร้างลิงก์ไม่สำเร็จ",
        json: { reviewerName: form.reviewerName.trim(), reviewerDepartment: form.reviewerDepartment.trim() || null, note: form.note.trim() || null, expiresInDays: form.expiresInDays },
      });
      await reload();
      setForm({ reviewerName: "", reviewerDepartment: "", note: "", expiresInDays: form.expiresInDays });
      setOpen(false);
      if (r.code) await copy(r.code, r.reviewerName);
    } catch (e) { setError(errorText(e, "สร้างลิงก์ไม่สำเร็จ")); } finally { setBusy(false); }
  };
  const revoke = async (x: ExternalReview) => {
    if (!await confirmDialog({ title: "ยกเลิกลิงก์ประเมิน", message: `ยกเลิกลิงก์ของ ${x.reviewerName}? ผู้ประเมินจะเปิดลิงก์นี้ไม่ได้อีก`, confirmLabel: "ยกเลิกลิงก์" })) return;
    setBusy(true); setError("");
    try {
      await apiFetch(`/performance/evaluations/${detail.summary.evaluationId}/external-reviews/${x.reviewId}/revoke`, { method: "POST", fallbackMessage: "ยกเลิกลิงก์ไม่สำเร็จ" });
      await reload();
      notify("ยกเลิกลิงก์แล้ว", "success");
    } catch (e) { setError(errorText(e, "ยกเลิกลิงก์ไม่สำเร็จ")); } finally { setBusy(false); }
  };

  return (
    <section ref={sectionRef} className="perf-eval-external" aria-labelledby="perf-eval-external-title">
      <div className="perf-ext-head">
        <span className="perf-ext-icon material-symbols-outlined" aria-hidden="true">groups</span>
        <div className="perf-ext-title">
          <h3 id="perf-eval-external-title">ผู้ประเมินจากแผนกอื่น</h3>
          <small>{reviews.length === 0 ? "ส่งลิงก์ให้หัวหน้าแผนกที่ทำงานร่วมกันประเมินได้โดยไม่ต้อง login" : `ส่งแล้ว ${submitted.length} คน${pending ? ` · รอ ${pending} คน` : ""}${detail.externalScore != null ? ` · เฉลี่ย ${detail.externalScore.toFixed(2)}` : ""}`} · คิดเป็น {detail.externalWeight}% ของผลรวม</small>
        </div>
        {canCreate && !open && <button type="button" className="btn primary perf-ext-add" disabled={disabled} onClick={() => setOpen(true)}><span className="material-symbols-outlined" aria-hidden="true">add_link</span>สร้างลิงก์ประเมิน</button>}
      </div>
      {!detail.canManageExternal && !closed && detail.managerRestriction && <p className="perf-hint">สร้างลิงก์ได้เฉพาะหัวหน้าตรงของพนักงาน — {detail.managerRestriction}</p>}
      {reviews.length > 0 && (
        <ul className="perf-ext-list">
          {reviews.map((x) => (
            <li key={x.reviewId} className={`perf-ext-item is-${x.status.toLowerCase()}`}>
              <div className="perf-ext-who">
                <b>{x.reviewerName}</b>{x.reviewerDepartment && <small>{x.reviewerDepartment}</small>}
              </div>
              <div className="perf-ext-state">
                <Badge tone={externalStatus[x.status].tone}>{externalStatus[x.status].label}</Badge>
                <small>{x.status === "Submitted" ? `ส่ง ${fmtDate(x.submittedAt)}` : x.status === "Pending" ? `ใช้ได้ถึง ${fmtDate(x.expiresAt)}${x.viewCount ? ` · เปิดแล้ว ${x.viewCount} ครั้ง` : " · ยังไม่ได้เปิด"}` : `สร้าง ${fmtDate(x.createdAt)}`}</small>
              </div>
              <div className="perf-ext-score">{x.weightedScore != null ? <><b>{x.weightedScore.toFixed(2)}</b><small>/ 5</small></> : <small>-</small>}</div>
              {x.status === "Pending" && x.code && detail.canManageExternal && (
                <div className="perf-ext-actions">
                  <button type="button" className="btn" disabled={disabled || busy} onClick={() => void copy(x.code!, x.reviewerName)}><span className="material-symbols-outlined" aria-hidden="true">content_copy</span>คัดลอกลิงก์</button>
                  <button type="button" className="btn perf-ext-revoke" disabled={disabled || busy} onClick={() => void revoke(x)} aria-label={`ยกเลิกลิงก์ของ ${x.reviewerName}`} title="ยกเลิกลิงก์"><span className="material-symbols-outlined" aria-hidden="true">link_off</span></button>
                </div>
              )}
              {x.comment && <p className="perf-score-note perf-ext-comment">“{x.comment}”</p>}
            </li>
          ))}
        </ul>
      )}
      {canCreate && open && (
        <div className="perf-ext-form">
          <div className="form-grid">
            <label>ชื่อผู้ประเมิน<span className="required-mark"> *</span><input ref={nameRef} value={form.reviewerName} maxLength={100} disabled={busy} placeholder="เช่น คุณสมชาย ใจดี" onChange={(e) => setForm((f) => ({ ...f, reviewerName: e.target.value }))} /></label>
            <label>แผนก / ตำแหน่ง<input value={form.reviewerDepartment} maxLength={100} disabled={busy} placeholder="เช่น หัวหน้าฝ่ายพัฒนา" onChange={(e) => setForm((f) => ({ ...f, reviewerDepartment: e.target.value }))} /></label>
            <label className="full">ข้อความถึงผู้ประเมิน (ไม่บังคับ)<input value={form.note} maxLength={300} disabled={busy} placeholder="เช่น ช่วยประเมินงานที่ทำร่วมกับทีม Dev ในไตรมาสนี้" onChange={(e) => setForm((f) => ({ ...f, note: e.target.value }))} /></label>
            <label>ลิงก์ใช้ได้
              <select value={form.expiresInDays} disabled={busy} onChange={(e) => setForm((f) => ({ ...f, expiresInDays: Number(e.target.value) }))}>
                {[7, 14, 30, 60].map((d) => <option key={d} value={d}>{d} วัน</option>)}
              </select>
            </label>
          </div>
          {error && <div className="inline-alert error" role="alert"><span>{error}</span></div>}
          <div className="perf-ext-form-actions">
            <button type="button" className="btn" disabled={busy} onClick={() => { setOpen(false); setError(""); }}>ยกเลิก</button>
            <button type="button" className="btn primary" disabled={busy || disabled || !form.reviewerName.trim()} onClick={() => void create()}>
              {busy ? <><span className="spinner inline" aria-hidden="true" /> กำลังสร้าง...</> : <><span className="material-symbols-outlined" aria-hidden="true">add_link</span> สร้างลิงก์และคัดลอก</>}
            </button>
          </div>
        </div>
      )}
      {!open && error && <div className="inline-alert error" role="alert"><span>{error}</span></div>}
    </section>
  );
}

/** ต้องตรงกับ Evaluation.GradeFor ฝั่ง backend */
function gradeFor(score: number): string {
  return score >= 4.5 ? "A" : score >= 3.5 ? "B" : score >= 2.5 ? "C" : score >= 1.5 ? "D" : "E";
}
