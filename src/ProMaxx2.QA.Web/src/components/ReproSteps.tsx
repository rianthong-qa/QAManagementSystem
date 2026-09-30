import { Badge } from "./Badge";
import type { DefectReproStep } from "../shared/defects";

/** ขั้นตอนการทำซ้ำแบบการ์ด (ใช้ร่วมกันใน Defect detail และหน้าแชร์): เลขข้อ + การกระทำ + Badge ผล,
 * ใต้หัวข้อแสดงส่วน "ป้าย: ค่า" แยกบรรทัดแบบตาราง 2 คอลัมน์ (Mobile ซ้อนกัน) และหมายเหตุใน [ ] เป็นป้ายเล็ก
 * ขั้นที่ Fail ใช้กรอบ/พื้นแดงอ่อน — ข้อความ "[ขั้นนี้ล้มเหลว]" ซ้ำกับ Badge Fail จึงไม่แสดงซ้ำ */
export function ReproSteps({ steps }: { steps: DefectReproStep[] }) {
  return (
    <ol className="defect-repro-steps">
      {steps.map((s) => {
        const notes = s.status === "Fail" ? s.notes.filter((n) => !/ล้มเหลว/.test(n)) : s.notes;
        return (
          <li key={s.stepNo} className={"defect-repro-step" + (s.status === "Fail" ? " is-fail" : s.status === "Pass" ? " is-pass" : "")}>
            <span className="defect-repro-step-no" aria-hidden="true">{s.stepNo}</span>
            <div className="defect-repro-step-body">
              <div className="defect-repro-step-head">
                <b>{s.action}</b>
                {s.status && <Badge tone={s.status === "Pass" ? "green" : "red"}>{s.status}</Badge>}
              </div>
              {s.fields.length > 0 && (
                <dl className="defect-repro-fields">
                  {s.fields.map((f, i) => (
                    <div key={i} className={f.label ? undefined : "is-plain"}>
                      {f.label && <dt>{f.label}</dt>}
                      <dd>{f.value || "-"}</dd>
                    </div>
                  ))}
                </dl>
              )}
              {notes.length > 0 && <div className="defect-repro-notes">{notes.map((n) => <span key={n}>{n}</span>)}</div>}
            </div>
          </li>
        );
      })}
    </ol>
  );
}
