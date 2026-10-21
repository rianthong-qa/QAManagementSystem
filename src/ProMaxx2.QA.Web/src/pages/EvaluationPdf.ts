// Export ผลการประเมินรายคนเป็น PDF A4 — รูปแบบเดียวกับ DefectModulePdf (render HTML นอกจอ → html2canvas → jsPDF)
// โหลดแบบ dynamic import จากปุ่ม Export เท่านั้น เพื่อไม่ให้ jspdf/html2canvas อยู่ใน chunk ของหน้า
import { escapeHtml } from "../api";
import { fmtDate, type Metrics } from "./performanceUtils";

type PdfScore = { group: string; name: string; weight: number; selfScore?: number | null; selfComment?: string | null; managerScore?: number | null; managerComment?: string | null };

export type EvaluationPdfInput = {
  roundTitle: string;
  statusLabel: string;
  isClosed: boolean;
  employeeName: string;
  employee: { employeeCode?: string | null; position?: string | null; level?: string | null; hireDate?: string | null; supervisorName?: string | null };
  templateName: string;
  periodStart: string;
  periodEnd: string;
  dueDate: string;
  evaluatorName?: string | null;
  overallScore?: number | null;
  grade?: string | null;
  recommendation?: string | null;
  extendToDate?: string | null;
  completedAt?: string | null;
  acknowledgedAt?: string | null;
  selfComment?: string | null;
  strengths?: string | null;
  improvements?: string | null;
  nextGoals?: string | null;
  employeeComment?: string | null;
  scores: PdfScore[];
  metrics: Metrics;
  metricsIsSnapshot: boolean;
};

const page = { width: 794, height: 1123 };

export async function exportEvaluationPdf(input: EvaluationPdfInput): Promise<void> {
  const [{ default: html2canvas }, { jsPDF }] = await Promise.all([import("html2canvas"), import("jspdf")]);
  const pdf = new jsPDF({ orientation: "portrait", unit: "pt", format: "a4", compress: true });
  pdf.setProperties({ title: `ผลการประเมิน ${input.employeeName} ${input.roundTitle}`, subject: "Performance evaluation" });
  const host = document.createElement("div");
  host.style.cssText = `position:fixed;left:-100000px;top:0;width:${page.width}px;pointer-events:none;z-index:-1`;
  host.innerHTML = `<style>
    .ev-page{box-sizing:border-box;position:relative;width:${page.width}px;height:${page.height}px;padding:40px 46px 56px;background:#fff;color:#172b4d;font-family:Tahoma,'Noto Sans Thai',Arial,sans-serif;font-size:12px;line-height:1.45}
    .ev-hero{display:flex;justify-content:space-between;align-items:flex-start;gap:16px;padding:20px 24px;border-radius:15px;background:linear-gradient(125deg,#17377e,#2457d6);color:#fff}
    .ev-kicker{font-size:10px;font-weight:600;letter-spacing:1.2px;opacity:.8}
    .ev-hero h1{margin:5px 0 3px;font-size:23px;line-height:1.3;color:#fff}
    .ev-hero p{margin:0;font-size:12px;opacity:.88}
    .ev-draft{flex:none;padding:5px 10px;border-radius:999px;background:#fff4d6;color:#9a6d00;font-size:11px;font-weight:700}
    .ev-info{display:grid;grid-template-columns:repeat(3,1fr);gap:8px 14px;margin:16px 0;padding:12px 14px;border:1px solid #e2e8f1;border-radius:10px}
    .ev-info small{display:block;color:#667085;font-size:10px}
    .ev-info b{font-size:12px;overflow-wrap:anywhere}
    .ev-result{display:flex;align-items:center;gap:14px;margin-bottom:16px;padding:12px 16px;border-radius:11px;background:#eef3ff}
    .ev-grade{display:grid;place-items:center;width:52px;height:52px;flex:none;border-radius:12px;background:#2457d6;color:#fff;font-size:26px;font-weight:700}
    .ev-result b{display:block;font-size:15px}
    .ev-result small{color:#52678f;font-size:11px}
    .ev-section{margin:4px 0 8px;padding-bottom:6px;border-bottom:1px solid #dfe7f4;font-size:14px;font-weight:700}
    .ev-row{display:grid;grid-template-columns:minmax(0,1fr) 54px 64px 64px;gap:10px;align-items:start;padding:7px 4px;border-bottom:1px solid #eef1f5}
    .ev-row.head{color:#667085;font-size:10px;font-weight:700;border-bottom:1px solid #dfe7f4}
    .ev-row .n{text-align:center;font-variant-numeric:tabular-nums}
    .ev-row .score{font-size:14px;font-weight:700}
    .ev-group{margin-top:8px;padding:5px 8px;border-radius:6px;background:#f5f7fb;color:#2457d6;font-size:11px;font-weight:700}
    .ev-note{display:block;margin-top:2px;color:#475467;font-size:10px;overflow-wrap:anywhere}
    .ev-text{margin-bottom:10px}
    .ev-text h3{margin:0 0 3px;font-size:12px;color:#475467}
    .ev-text p{margin:0;white-space:pre-wrap;overflow-wrap:anywhere}
    .ev-metrics{display:grid;grid-template-columns:repeat(4,1fr);gap:8px;margin-bottom:6px}
    .ev-metric{padding:8px 10px;border:1px solid #e2e8f1;border-top:3px solid #2457d6;border-radius:8px}
    .ev-metric small{display:block;color:#667085;font-size:10px}
    .ev-metric b{font-size:15px}
    .ev-hint{color:#667085;font-size:10px;margin:0 0 12px}
    .ev-signs{display:grid;grid-template-columns:repeat(3,1fr);gap:18px;margin-top:26px}
    .ev-sign{text-align:center;font-size:11px;color:#475467}
    .ev-sign i{display:block;height:36px;border-bottom:1px dotted #98a2b3;margin-bottom:6px}
    .ev-continuation{padding:4px 0 10px;border-bottom:2px solid #2457d6;margin-bottom:14px}
    .ev-continuation small{color:#667085;font-size:10px}
    .ev-continuation h1{margin:2px 0 0;font-size:17px}
    .ev-footer{position:absolute;bottom:26px;left:46px;right:46px;display:flex;justify-content:space-between;border-top:1px solid #dfe7f4;padding-top:7px;color:#667085;font-size:10px}
  </style>`;

  const generatedAt = new Date().toLocaleString("th-TH", { dateStyle: "medium", timeStyle: "short" });
  const e = input.employee;
  const info: [string, string][] = [
    ["พนักงาน", input.employeeName], ["รหัสพนักงาน", e.employeeCode || "-"], ["ตำแหน่ง / ระดับ", [e.position, e.level].filter(Boolean).join(" · ") || "-"],
    ["วันเริ่มงาน", fmtDate(e.hireDate)], ["หัวหน้า", e.supervisorName || "-"], ["ผู้ประเมิน", input.evaluatorName || "-"],
    ["ช่วงประเมิน", `${fmtDate(input.periodStart)} – ${fmtDate(input.periodEnd)}`], ["กำหนดส่ง", fmtDate(input.dueDate)], ["แบบประเมิน", input.templateName],
  ];
  const blocks: string[] = [
    `<div class="ev-hero"><div><div class="ev-kicker">PROMAXX2 QA HUB · PERFORMANCE EVALUATION</div><h1>${escapeHtml(input.roundTitle)}</h1><p>สถานะ ${escapeHtml(input.statusLabel)} · ข้อมูล ณ ${escapeHtml(generatedAt)}</p></div>${input.isClosed ? "" : '<span class="ev-draft">ร่าง — ยังไม่ส่งผล</span>'}</div>`,
    `<div class="ev-info">${info.map(([k, v]) => `<div><small>${escapeHtml(k)}</small><b>${escapeHtml(v)}</b></div>`).join("")}</div>`,
  ];
  if (input.grade) {
    const decision = input.recommendation ? ` · ${escapeHtml(input.recommendation)}${input.extendToDate ? ` ถึง ${escapeHtml(fmtDate(input.extendToDate))}` : ""}` : "";
    blocks.push(`<div class="ev-result"><span class="ev-grade">${escapeHtml(input.grade)}</span><div><b>คะแนนรวม ${input.overallScore?.toFixed(2) ?? "-"} / 5${decision}</b><small>ส่งผลเมื่อ ${escapeHtml(fmtDate(input.completedAt))}${input.acknowledgedAt ? ` · พนักงานรับทราบเมื่อ ${escapeHtml(fmtDate(input.acknowledgedAt))}` : ""}</small></div></div>`);
  }
  blocks.push('<div class="ev-section">คะแนนรายเกณฑ์ (1–5)</div>', '<div class="ev-row head"><span>เกณฑ์</span><span class="n">น้ำหนัก</span><span class="n">ตนเอง</span><span class="n">หัวหน้า</span></div>');
  let group = "";
  for (const s of input.scores) {
    if (s.group !== group) { group = s.group; blocks.push(`<div class="ev-group">${escapeHtml(group)}</div>`); }
    const notes = [s.selfComment && `ตนเอง: ${s.selfComment}`, s.managerComment && `หัวหน้า: ${s.managerComment}`].filter(Boolean).map((x) => `<span class="ev-note">${escapeHtml(x)}</span>`).join("");
    blocks.push(`<div class="ev-row"><span>${escapeHtml(s.name)}${notes}</span><span class="n">${s.weight}%</span><span class="n score">${s.selfScore ?? "-"}</span><span class="n score">${s.managerScore ?? "-"}</span></div>`);
  }
  const texts: [string, string | null | undefined][] = [
    ["ความเห็นของพนักงาน (ประเมินตนเอง)", input.selfComment], ["จุดแข็ง", input.strengths], ["สิ่งที่ควรพัฒนา", input.improvements],
    ["เป้าหมายรอบถัดไป", input.nextGoals], ["ความเห็นของพนักงานต่อผลการประเมิน", input.employeeComment],
  ];
  const filled = texts.filter(([, v]) => v?.trim());
  if (filled.length) {
    blocks.push('<div class="ev-section" style="margin-top:14px">ความเห็น</div>');
    // ข้อความยาวแบ่งเป็นหลายก้อนตามย่อหน้า เพื่อให้ขึ้นหน้าใหม่ได้แทนการล้นหน้า
    for (const [label, value] of filled) {
      value!.split(/\n{2,}/).forEach((part, i) => blocks.push(`<div class="ev-text">${i === 0 ? `<h3>${escapeHtml(label)}</h3>` : ""}<p>${escapeHtml(part)}</p></div>`));
    }
  }
  const m = input.metrics;
  const metric = (label: string, value: string | number) => `<div class="ev-metric"><small>${escapeHtml(label)}</small><b>${escapeHtml(typeof value === "number" ? value.toLocaleString("th-TH") : value)}</b></div>`;
  blocks.push(
    '<div class="ev-section" style="margin-top:14px">หลักฐานจากระบบในช่วงประเมิน</div>',
    `<div class="ev-metrics">${metric("Execution", m.executions)}${metric("Fail / Blocked", `${m.failed} / ${m.blocked}`)}${metric("Test Case ที่สร้าง / แก้", `${m.testCasesCreated} / ${m.testCaseRevisions}`)}${metric("Defect ที่แจ้ง", m.defectsReported)}</div>`,
    `<div class="ev-metrics">${metric("Critical / High", `${m.defectsCritical} / ${m.defectsHigh}`)}${metric("ถูก Reject", m.defectsRejected)}${metric("CRM Ticket ที่ดูแล", m.hasCrmAccount ? m.crmTicketsHandled : "ไม่ได้ผูก")}${metric("บันทึกงาน / ผลงานเด่น", `${m.workLogs} / ${m.highlights}`)}</div>`,
    `<p class="ev-hint">${input.metricsIsSnapshot ? "ตัวเลข ณ วันที่ส่งผล" : "ตัวเลขล่าสุดในช่วงประเมิน"} · ใช้เป็นหลักฐานประกอบเท่านั้น ไม่ได้คิดเป็นคะแนนอัตโนมัติ</p>`,
    `<div class="ev-signs"><div class="ev-sign"><i></i>ผู้ถูกประเมิน<br>${escapeHtml(input.employeeName)}</div><div class="ev-sign"><i></i>ผู้ประเมิน<br>${escapeHtml(input.evaluatorName || "")}</div><div class="ev-sign"><i></i>ฝ่ายบุคคล (HR)<br>&nbsp;</div></div>`,
  );

  const pages: { element: HTMLElement; body: HTMLElement; footer: HTMLElement }[] = [];
  const newPage = () => {
    const element = document.createElement("section");
    element.className = "ev-page";
    element.innerHTML = `${pages.length === 0 ? "" : `<div class="ev-continuation"><small>ProMaxx2 QA Hub · ${escapeHtml(input.employeeName)}</small><h1>${escapeHtml(input.roundTitle)} (ต่อ)</h1></div>`}<div class="ev-body"></div>
      <div class="ev-footer"><span>ProMaxx2 QA Hub · ผลการประเมิน ${escapeHtml(input.employeeName)}</span><span class="ev-page-number"></span></div>`;
    host.appendChild(element);
    const item = { element, body: element.querySelector<HTMLElement>(".ev-body")!, footer: element.querySelector<HTMLElement>(".ev-footer")! };
    pages.push(item);
    return item;
  };

  document.body.appendChild(host);
  try {
    await document.fonts.ready;
    let current = newPage();
    for (const html of blocks) {
      const wrap = document.createElement("div");
      wrap.innerHTML = html;
      const node = wrap.firstElementChild as HTMLElement;
      current.body.appendChild(node);
      if (node.getBoundingClientRect().bottom > current.footer.getBoundingClientRect().top - 12 && current.body.children.length > 1) {
        node.remove();
        current = newPage();
        current.body.appendChild(node);
      }
    }
    pages.forEach((item, index) => { item.element.querySelector(".ev-page-number")!.textContent = `${index + 1} / ${pages.length}`; });
    for (const [index, item] of pages.entries()) {
      const canvas = await html2canvas(item.element, { scale: 2, backgroundColor: "#ffffff", useCORS: true });
      if (index > 0) pdf.addPage();
      pdf.addImage(canvas.toDataURL("image/jpeg", 0.94), "JPEG", 0, 0, pdf.internal.pageSize.getWidth(), pdf.internal.pageSize.getHeight());
    }
    const safe = (v: string) => v.replace(/[\\/:*?"<>|]/g, "-").trim();
    pdf.save(`ผลการประเมิน - ${safe(input.employeeName) || "พนักงาน"} - ${safe(input.roundTitle)}.pdf`);
  } finally {
    host.remove();
  }
}
