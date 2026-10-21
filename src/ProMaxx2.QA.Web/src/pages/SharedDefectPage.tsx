import { type ReactNode, useEffect, useState } from "react";
import { apiUrl, getJson, isAbortError, ApiError } from "../api";
import { Badge } from "../components/Badge";
import { DefectCommentList } from "../components/DefectComments";
import { type DefectComment, toLightboxImages } from "../components/defectCommentUtils";
import { ImageLightbox } from "../components/ImageLightbox";
import { ReproSteps } from "../components/ReproSteps";
import { parseReproSteps } from "../shared/defects";
import { defectStatusTones, fmtDateTimeBE } from "../shared/appShared";
import "./SharedDefect.css";

// หน้าอ่านอย่างเดียวของ Defect จากลิงก์ `?d=<code>` ที่แนบท้าย Description ของ CRM ticket
// เปิดได้โดยไม่ต้อง login — endpoint /shared/defects/* เป็น anonymous และสิทธิ์มาจาก code ที่ผูกกับ Defect เดียว
type SharedDefect = {
  defectCode: string; title: string; severity: string; status: string;
  projectName?: string | null; module?: string | null; release?: string | null; build?: string | null;
  reportedBy?: string | null; assignee?: string | null;
  description?: string | null; stepsToReproduce?: string | null; expectedResult?: string | null; actualResult?: string | null;
  createdAt: string; updatedAt?: string | null; crmTicketId?: string | null;
  testCases: { testCaseCode: string; title: string }[];
  attachments: { attachmentId: string; fileName: string; size: number }[];
  comments?: DefectComment[];
};

const severityTones: Record<string, string> = { Critical: "red", High: "red", Medium: "yellow", Low: "blue" };

// รายละเอียดที่ Execution Workspace สร้างเป็นบรรทัด "ป้าย: ค่า" — แสดงป้ายเป็นตัวหนาเหมือนหน้า Defect detail
function LabeledText({ text }: { text: string }) {
  return (
    <div className="shared-defect-lines">
      {text.split(/\r?\n/).map((line, i) => {
        const m = line.match(/^(.{2,40}?):\s*(.*)$/);
        return <p key={i}>{m ? <><b>{m[1]}:</b> {m[2]}</> : (line || " ")}</p>;
      })}
    </div>
  );
}

export default function SharedDefectPage({ token }: { token: string }) {
  const [data, setData] = useState<SharedDefect | null>(null);
  const [error, setError] = useState("");
  const [viewerIndex, setViewerIndex] = useState<number | null>(null);
  const base = `${apiUrl}/shared/defects/${encodeURIComponent(token)}`;
  const imageSrc = (attachmentId: string) => `${base}/attachments/${attachmentId}`;

  useEffect(() => {
    const controller = new AbortController();
    getJson<SharedDefect>(base, controller.signal)
      .then(setData)
      .catch((e) => {
        if (isAbortError(e)) return;
        setError(e instanceof ApiError && e.status === 404 ? "ลิงก์ไม่ถูกต้อง หรือ Defect นี้ถูกลบแล้ว" : "โหลดข้อมูล Defect ไม่สำเร็จ กรุณาลองใหม่อีกครั้ง");
      });
    return () => controller.abort();
  }, [base]);

  useEffect(() => { if (data) document.title = `${data.defectCode} · ProMaxx2 QA Hub`; }, [data]);

  if (error) return <div className="card shared-defect-state" role="alert"><span className="material-symbols-outlined" aria-hidden="true">link_off</span><p>{error}</p></div>;
  if (!data) return <div className="card shared-defect-state" role="status"><span className="spinner" aria-hidden="true" /><p>กำลังโหลดข้อมูล Defect...</p></div>;

  // ลิงก์เดียวกับปุ่ม CRM Ticket ในหน้า Defect detail — เปิดหน้า Ticket ใน BlueSea (ต้อง login CRM ของตัวเอง)
  const crmTicket = data.crmTicketId
    ? <a className="shared-defect-link" href={`https://bluesea.seniorsoft.com/bluesea/BookLicence/MA/Support/JobDetailsHD?JobNo=${encodeURIComponent(data.crmTicketId)}&JobType=HD`} target="_blank" rel="noreferrer" aria-label={`เปิด Ticket ${data.crmTicketId} ใน CRM (แท็บใหม่)`}>{data.crmTicketId}<span className="material-symbols-outlined" aria-hidden="true">open_in_new</span></a>
    : null;
  const meta: [string, ReactNode][] = [
    ["Project", data.projectName], ["Module", data.module], ["Release", data.release], ["Build", data.build],
    ["ผู้แจ้ง", data.reportedBy], ["ผู้รับผิดชอบ", data.assignee], ["CRM Ticket", crmTicket],
    ["สร้างเมื่อ", fmtDateTimeBE(data.createdAt)], ["แก้ไขล่าสุด", data.updatedAt ? fmtDateTimeBE(data.updatedAt) : null],
  ];
  const sections: [string, string | null | undefined][] = [
    ["รายละเอียด", data.description], ["ขั้นตอนการทำซ้ำ", data.stepsToReproduce],
    ["ผลที่คาดหวัง", data.expectedResult], ["ผลที่เกิดขึ้นจริง", data.actualResult],
  ];

  return (
    <article className="shared-defect">
      <header className="card shared-defect-hero">
        <div className="shared-defect-badges">
          <Badge tone="blue">{data.defectCode}</Badge>
          <Badge tone={severityTones[data.severity] ?? "blue"}>{data.severity}</Badge>
          <Badge tone={defectStatusTones[data.status] ?? "blue"}>{data.status}</Badge>
        </div>
        <h1>{data.title}</h1>
        <dl className="shared-defect-meta">
          {meta.filter(([, v]) => v).map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value}</dd></div>)}
        </dl>
      </header>

      {sections.filter(([, v]) => v?.trim()).map(([label, value]) => {
        const steps = label === "ขั้นตอนการทำซ้ำ" ? parseReproSteps(value!) : null;
        return (
          <section key={label} className="card shared-defect-section">
            <h2>{label}</h2>
            {steps ? <ReproSteps steps={steps} /> : label === "รายละเอียด" ? <LabeledText text={value!} /> : <p>{value}</p>}
          </section>
        );
      })}

      {data.testCases.length > 0 && (
        <section className="card shared-defect-section">
          <h2>Test Case ที่เกี่ยวข้อง</h2>
          <ul className="shared-defect-cases">{data.testCases.map((tc) => <li key={tc.testCaseCode}><b>{tc.testCaseCode}</b> {tc.title}</li>)}</ul>
        </section>
      )}

      {data.attachments.length > 0 && (
        <section className="card shared-defect-section">
          <h2>รูปภาพแนบ ({data.attachments.length})</h2>
          <div className="shared-defect-images">
            {data.attachments.map((a, i) => (
              <button type="button" key={a.attachmentId} onClick={() => setViewerIndex(i)} aria-label={`ดูรูป ${a.fileName} ขนาดใหญ่`}><img src={imageSrc(a.attachmentId)} alt={a.fileName} loading="lazy" /><span>{a.fileName}</span></button>
            ))}
          </div>
        </section>
      )}

      <section className="card shared-defect-section">
        <h2>คอมเมนต์ ({data.comments?.length ?? 0})</h2>
        <DefectCommentList comments={data.comments ?? []} imageSrc={imageSrc} />
      </section>

      {viewerIndex !== null && <ImageLightbox images={toLightboxImages(data.attachments, imageSrc)} index={viewerIndex} onIndexChange={setViewerIndex} onClose={() => setViewerIndex(null)} />}
    </article>
  );
}
