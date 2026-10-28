import { useEffect, useMemo, useState } from "react";
import { ApiError, apiUrl, getJson, isAbortError } from "../api";
import { confirmDialog } from "../components/dialogStore";
import { formatThaiDateTime } from "../dateTime";
import { fmtDate } from "./performanceUtils";
import "./Performance.css";

// แบบประเมินสำหรับหัวหน้าแผนกอื่นจากลิงก์ `?er=<code>` — เปิดได้โดยไม่ต้อง login
// endpoint /shared/evaluation-reviews/* เป็น anonymous: 1 ลิงก์ = ผู้ประเมิน 1 คน ส่งได้ครั้งเดียว มีวันหมดอายุ
// หน้านี้ไม่เห็นคะแนนตนเอง/หัวหน้าตรงของพนักงาน — ให้คะแนน 1–5 ทุกเกณฑ์ของรอบนั้นอย่างอิสระ
type Criterion = { evaluationScoreId: string; group: string; name: string; description?: string | null; weight: number };
type ReviewForm = {
  reviewerName: string; reviewerDepartment?: string | null; note?: string | null; requestedByName?: string | null;
  employeeName: string; position?: string | null; type: string; round: string; year: number; periodStart: string; periodEnd: string; templateName: string;
  expiresAt: string; status: "Pending" | "Submitted" | "Revoked" | "Expired"; submittedAt?: string | null; criteria: Criterion[];
};
type Draft = { score: number | null; comment: string };

const scaleLabel = ["", "ต้องปรับปรุงมาก", "ต่ำกว่าที่คาดหวัง", "ตามที่คาดหวัง", "ดีกว่าที่คาดหวัง", "ดีเยี่ยม"];
const roundLabel = (f: ReviewForm) => f.type === "Probation"
  ? ({ P30: "ทดลองงาน 30 วัน", P60: "ทดลองงาน 60 วัน", P90: "ทดลองงาน 90 วัน", PFinal: "สรุปผลทดลองงาน" } as Record<string, string>)[f.round] ?? f.round
  : `ไตรมาส ${f.round.replace("Q", "")}/${f.year}`;

export default function SharedEvaluationReviewPage({ code }: { code: string }) {
  const [form, setForm] = useState<ReviewForm | null>(null);
  const [drafts, setDrafts] = useState<Record<string, Draft>>({});
  const [comment, setComment] = useState("");
  const [error, setError] = useState<{ icon: string; message: string } | null>(null);
  const [submitError, setSubmitError] = useState("");
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    getJson<ReviewForm>(`${apiUrl}/shared/evaluation-reviews/${encodeURIComponent(code)}`, controller.signal)
      .then((f) => { setForm(f); setDrafts(Object.fromEntries(f.criteria.map((c) => [c.evaluationScoreId, { score: null, comment: "" }]))); })
      .catch((e) => {
        if (isAbortError(e)) return;
        if (e instanceof ApiError && e.status === 410) setError({ icon: "timer_off", message: e.message });
        else if (e instanceof ApiError && e.status === 404) setError({ icon: "link_off", message: "ลิงก์ไม่ถูกต้อง กรุณาตรวจสอบลิงก์ที่ได้รับอีกครั้ง" });
        else if (e instanceof ApiError && e.status === 429) setError({ icon: "hourglass_top", message: "เปิดลิงก์ถี่เกินไป กรุณารอสักครู่แล้วลองใหม่" });
        else setError({ icon: "cloud_off", message: "โหลดแบบประเมินไม่สำเร็จ กรุณาลองใหม่อีกครั้ง" });
      });
    return () => controller.abort();
  }, [code]);

  useEffect(() => { if (form) document.title = `ประเมิน ${form.employeeName} · ProMaxx2 QA Hub`; }, [form]);

  const groups = useMemo(() => {
    const map = new Map<string, Criterion[]>();
    for (const c of form?.criteria ?? []) map.set(c.group, [...(map.get(c.group) ?? []), c]);
    return [...map.entries()];
  }, [form]);
  const total = form?.criteria.length ?? 0;
  const scored = form ? form.criteria.filter((c) => drafts[c.evaluationScoreId]?.score != null).length : 0;
  const setDraft = (id: string, patch: Partial<Draft>) => setDrafts((d) => ({ ...d, [id]: { ...d[id], ...patch } }));

  const submit = async () => {
    if (!form || scored < total) return;
    if (!await confirmDialog({ title: "ส่งผลประเมิน", message: `ส่งผลประเมินของ ${form.employeeName}? ส่งแล้วแก้ไขไม่ได้`, confirmLabel: "ส่งผลประเมิน" })) return;
    setBusy(true);
    setSubmitError("");
    try {
      const response = await fetch(`${apiUrl}/shared/evaluation-reviews/${encodeURIComponent(code)}`, {
        method: "POST", headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ scores: form.criteria.map((c) => ({ evaluationScoreId: c.evaluationScoreId, score: drafts[c.evaluationScoreId]?.score, comment: drafts[c.evaluationScoreId]?.comment.trim() || null })), comment: comment.trim() || null }),
      });
      if (!response.ok) {
        const problem = await response.json().catch(() => null) as { detail?: string } | null;
        if (response.status === 410) { setError({ icon: "timer_off", message: problem?.detail ?? "ลิงก์นี้ใช้ไม่ได้แล้ว" }); return; }
        throw new Error(problem?.detail ?? (response.status === 429 ? "ส่งถี่เกินไป กรุณารอสักครู่แล้วลองใหม่" : "ส่งผลประเมินไม่สำเร็จ กรุณาลองใหม่อีกครั้ง"));
      }
      setForm(await response.json() as ReviewForm);
      window.scrollTo({ top: 0, behavior: "smooth" });
    } catch (e) {
      setSubmitError(e instanceof Error ? e.message : "ส่งผลประเมินไม่สำเร็จ");
    } finally {
      setBusy(false);
    }
  };

  if (error) return <div className="card perf-shared-state" role="alert"><span className="material-symbols-outlined" aria-hidden="true">{error.icon}</span><p>{error.message}</p></div>;
  if (!form) return <div className="card perf-shared-state" role="status"><span className="spinner" aria-hidden="true" /><p>กำลังโหลดแบบประเมิน...</p></div>;

  const head = (
    <header className="card perf-xr-head">
      <span className="perf-xr-eyebrow">แบบประเมินพนักงาน · {roundLabel(form)}</span>
      <h1>{form.employeeName}{form.position && <small>{form.position}</small>}</h1>
      <dl className="perf-xr-meta">
        <div><dt>ช่วงที่ประเมิน</dt><dd>{fmtDate(form.periodStart)} – {fmtDate(form.periodEnd)}</dd></div>
        <div><dt>ผู้ประเมิน</dt><dd>{form.reviewerName}{form.reviewerDepartment ? ` · ${form.reviewerDepartment}` : ""}</dd></div>
        <div><dt>ขอโดย</dt><dd>{form.requestedByName ?? "หัวหน้าทีม QA"}</dd></div>
      </dl>
    </header>
  );

  if (form.status === "Submitted") {
    return (
      <div className="performance-page perf-xr">
        {head}
        <div className="card perf-shared-state perf-xr-done" role="status">
          <span className="material-symbols-outlined" aria-hidden="true">task_alt</span>
          <p><b>ส่งผลประเมินเรียบร้อยแล้ว ขอบคุณครับ/ค่ะ</b></p>
          <small>ส่งเมื่อ {formatThaiDateTime(form.submittedAt ?? "", { dateStyle: "long", timeStyle: "short" })} · ผลของคุณถูกส่งถึง {form.requestedByName ?? "หัวหน้าทีม QA"} แล้ว ปิดหน้านี้ได้เลย</small>
        </div>
      </div>
    );
  }

  return (
    <div className="performance-page perf-xr">
      {head}
      <div className="perf-shared-banner" role="note">
        <span className="material-symbols-outlined" aria-hidden="true">lock</span>
        <span>
          {form.note ? <>“{form.note}” · </> : null}
          ให้คะแนนตามที่เห็นจากการทำงานร่วมกัน · คะแนนของคุณใช้คำนวณผลประเมินร่วมกับหัวหน้าตรง · ส่งได้ครั้งเดียว · ลิงก์ใช้ได้ถึง {formatThaiDateTime(form.expiresAt, { dateStyle: "long", timeStyle: "short" })} กรุณาอย่าส่งต่อ
        </span>
      </div>
      <ul className="perf-eval-legend perf-xr-legend" aria-label="ความหมายของคะแนน">
        {[1, 2, 3, 4, 5].map((n) => <li key={n}><b>{n}</b>{scaleLabel[n]}</li>)}
      </ul>
      {groups.map(([group, items]) => (
        <section key={group} className="card perf-xr-group" aria-label={group}>
          <h2>{group}</h2>
          {items.map((c) => {
            const d = drafts[c.evaluationScoreId];
            return (
              <div key={c.evaluationScoreId} className="perf-xr-row">
                <div className="perf-xr-crit"><b>{c.name}</b>{c.description && <p>{c.description}</p>}</div>
                <div className="perf-xr-pick" role="radiogroup" aria-label={c.name}>
                  {[1, 2, 3, 4, 5].map((n) => (
                    <label key={n} className={d?.score === n ? "active" : undefined} title={scaleLabel[n]}>
                      <input type="radio" name={`xr-${c.evaluationScoreId}`} value={n} checked={d?.score === n} disabled={busy} onChange={() => setDraft(c.evaluationScoreId, { score: n })} />
                      <b>{n}</b><span>{scaleLabel[n]}</span>
                    </label>
                  ))}
                </div>
                <input className="perf-xr-note" value={d?.comment ?? ""} maxLength={1000} disabled={busy} aria-label={`ความเห็นเกณฑ์ ${c.name} (ไม่บังคับ)`} placeholder="ความเห็น / ตัวอย่าง (ไม่บังคับ)" onChange={(e) => setDraft(c.evaluationScoreId, { comment: e.target.value })} />
              </div>
            );
          })}
        </section>
      ))}
      <section className="card perf-xr-group" aria-label="ความเห็นภาพรวม">
        <h2>ความเห็นภาพรวม (ไม่บังคับ)</h2>
        <textarea rows={4} maxLength={2000} value={comment} disabled={busy} placeholder="จุดเด่นจากการทำงานร่วมกัน และสิ่งที่อยากให้พัฒนา" onChange={(e) => setComment(e.target.value)} aria-label="ความเห็นภาพรวม" />
      </section>
      <div className="perf-xr-submit">
        {submitError && <div className="inline-alert error" role="alert"><span>{submitError}</span></div>}
        <span role="status">ให้คะแนนแล้ว <b>{scored}/{total}</b> เกณฑ์</span>
        <button type="button" className="btn primary" disabled={busy || scored < total} onClick={() => void submit()} title={scored < total ? "ให้คะแนนให้ครบทุกเกณฑ์ก่อนส่ง" : undefined}>
          {busy ? <><span className="spinner inline" aria-hidden="true" /> กำลังส่ง...</> : <><span className="material-symbols-outlined" aria-hidden="true">send</span> ส่งผลประเมิน</>}
        </button>
      </div>
    </div>
  );
}
