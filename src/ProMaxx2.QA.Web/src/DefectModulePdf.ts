import { escapeHtml } from "./api";

export type DefectModuleReport = {
  total: number;
  open: number;
  inProgress: number;
  resolved: number;
  closed: number;
  rejected: number;
  modules: { moduleId: string | null; moduleCode: string | null; moduleName: string; count: number }[];
};

type ReportContext = { project: string; release: string; build: string };

export async function exportDefectModulePdf(report: DefectModuleReport, context: ReportContext): Promise<void> {
  const [{ default: html2canvas }, { jsPDF }] = await Promise.all([import("html2canvas"), import("jspdf")]);
  const pdf = new jsPDF({ orientation: "portrait", unit: "pt", format: "a4", compress: true });
  pdf.setProperties({ title: "Defect by Module", subject: "Defect distribution by module" });
  const host = document.createElement("div");
  host.style.cssText = "position:fixed;left:-100000px;top:0;width:794px;pointer-events:none;z-index:-1";
  host.innerHTML = `<style>
    .dm-page{box-sizing:border-box;position:relative;width:794px;height:1123px;padding:42px 48px 54px;background:#fff;color:#172b4d;font-family:Tahoma,'Noto Sans Thai',Arial,sans-serif;font-size:13px}
    .dm-hero{padding:24px 28px;border-radius:17px;background:linear-gradient(125deg,#17377e,#2457d6);color:#fff}
    .dm-kicker{font-size:11px;font-weight:600;letter-spacing:1.3px;opacity:.78}
    .dm-hero h1{margin:6px 0 5px;font-size:27px;line-height:1.3;font-weight:700;color:#fff}
    .dm-hero p{margin:0;font-size:12px;opacity:.86}
    .dm-scope{display:flex;flex-wrap:wrap;gap:7px;margin:18px 0}
    .dm-scope span{padding:6px 10px;border:1px solid #dfe7f4;border-radius:7px;background:#f7f9fd;font-size:11px;color:#475467}
    .dm-scope b{color:#172b4d}
    .dm-metrics{display:grid;grid-template-columns:repeat(4,1fr);gap:10px;margin-bottom:17px}
    .dm-metric{padding:12px 14px;border:1px solid #e2e8f1;border-top:3px solid #2457d6;border-radius:10px}
    .dm-metric small{display:block;color:#667085;font-size:10px}
    .dm-metric strong{display:block;margin-top:3px;font-size:21px;line-height:1.2}
    .dm-feature{display:flex;align-items:center;justify-content:space-between;gap:15px;padding:14px 17px;border-radius:11px;background:#eef3ff;margin-bottom:20px}
    .dm-feature small{display:block;color:#52678f;font-size:10px}
    .dm-feature strong{display:block;font-size:15px;overflow-wrap:anywhere}
    .dm-feature>span{flex:0 0 auto;color:#2457d6;font-size:22px;font-weight:700}
    .dm-section{display:flex;justify-content:space-between;align-items:baseline;gap:12px;margin:0 0 10px;padding-bottom:8px;border-bottom:1px solid #dfe7f4}
    .dm-section h2{margin:0;font-size:15px;font-weight:700;color:#172b4d}
    .dm-section span{font-size:11px;color:#667085}
    .dm-row{display:grid;grid-template-columns:34px minmax(0,1fr) 74px;align-items:center;gap:12px;padding:10px 4px;border-bottom:1px solid #eef1f5;break-inside:avoid}
    .dm-rank{display:grid;place-items:center;width:27px;height:27px;border-radius:7px;background:#eef3ff;color:#2457d6;font-size:11px;font-weight:700}
    .dm-row:first-child .dm-rank{background:#2457d6;color:#fff}
    .dm-name{line-height:1.35;overflow-wrap:anywhere;font-size:12px;font-weight:600}
    .dm-code{margin-right:7px;color:#2457d6}
    .dm-track{display:block;height:6px;margin-top:7px;border-radius:99px;background:#eaf0f8;overflow:hidden}
    .dm-track i{display:block;height:100%;border-radius:99px;background:#4d78e1}
    .dm-count{text-align:right;white-space:nowrap;font-size:16px;font-weight:700;color:#172b4d}
    .dm-count small{display:block;color:#667085;font-size:10px;font-weight:400}
    .dm-empty{padding:24px;text-align:center;color:#667085;background:#f8fafc;border-radius:9px}
    .dm-continuation{padding:6px 0 13px;border-bottom:2px solid #2457d6;margin-bottom:20px}
    .dm-continuation small{color:#667085;font-size:11px}
    .dm-continuation h1{margin:3px 0 0;font-size:20px;color:#172b4d}
    .dm-footer{position:absolute;bottom:30px;left:48px;right:48px;display:flex;justify-content:space-between;border-top:1px solid #dfe7f4;padding-top:8px;color:#667085;font-size:10px}
  </style>`;
  const generatedAt = new Date().toLocaleString("th-TH", { dateStyle: "medium", timeStyle: "short" });
  const scope = `<div class="dm-scope"><span><b>Project</b> ${escapeHtml(context.project)}</span><span><b>Release</b> ${escapeHtml(context.release)}</span><span><b>Build</b> ${escapeHtml(context.build)}</span></div>`;
  const top = report.modules[0];
  const firstContent = `<div class="dm-hero"><div class="dm-kicker">PROMAXX2 QA HUB · DEFECT REPORT</div><h1>Defect ตามโมดูล</h1><p>สรุปจำนวนที่พบ เรียงจากมากไปน้อย · ข้อมูล ณ ${escapeHtml(generatedAt)}</p></div>
    ${scope}
    <div class="dm-metrics"><div class="dm-metric"><small>Defect ทั้งหมด</small><strong>${report.total.toLocaleString()}</strong></div><div class="dm-metric"><small>Open / In Progress</small><strong>${(report.open + report.inProgress).toLocaleString()}</strong></div><div class="dm-metric"><small>Resolved / Closed</small><strong>${(report.resolved + report.closed).toLocaleString()}</strong></div><div class="dm-metric"><small>โมดูลที่พบ</small><strong>${report.modules.length.toLocaleString()}</strong></div></div>
    <div class="dm-feature"><div><small>พบ Defect มากที่สุด</small><strong>${top ? escapeHtml(`${top.moduleCode ? `${top.moduleCode} · ` : ""}${top.moduleName}`) : "ยังไม่มีข้อมูล"}</strong></div><span>${top ? top.count.toLocaleString() : "0"}</span></div>`;
  const pages: { element: HTMLElement; list: HTMLElement; footer: HTMLElement }[] = [];
  const newPage = () => {
    const element = document.createElement("section");
    element.className = "dm-page";
    const first = pages.length === 0;
    element.innerHTML = `${first ? firstContent : `<div class="dm-continuation"><small>ProMaxx2 QA Hub · ${escapeHtml(context.project)}</small><h1>Defect ตามโมดูล (ต่อ)</h1></div>`}
      <div class="dm-section"><h2>อันดับโมดูล</h2><span>${report.modules.length.toLocaleString()} กลุ่ม · รวมทุกสถานะ</span></div>
      <div class="dm-list"></div><div class="dm-footer"><span>ProMaxx2 QA Hub · รายงาน Defect ตามโมดูล</span><span class="dm-page-number"></span></div>`;
    host.appendChild(element);
    const page = { element, list: element.querySelector<HTMLElement>(".dm-list")!, footer: element.querySelector<HTMLElement>(".dm-footer")! };
    pages.push(page);
    return page;
  };
  document.body.appendChild(host);
  try {
    await document.fonts.ready;
    let page = newPage();
    if (report.modules.length === 0) page.list.innerHTML = '<div class="dm-empty">ยังไม่มี Defect ในขอบเขตที่เลือก</div>';
    report.modules.forEach((module, index) => {
      const row = document.createElement("div");
      row.className = "dm-row";
      row.innerHTML = `<span class="dm-rank">${index + 1}</span><div><div class="dm-name">${module.moduleCode ? `<span class="dm-code">${escapeHtml(module.moduleCode)}</span>` : ""}${escapeHtml(module.moduleName)}</div><span class="dm-track"><i style="width:${Math.round(module.count / Math.max(1, top.count) * 100)}%"></i></span></div><span class="dm-count">${module.count.toLocaleString()}<small>Defect</small></span>`;
      page.list.appendChild(row);
      if (page.list.getBoundingClientRect().bottom > page.footer.getBoundingClientRect().top - 22 && page.list.children.length > 1) {
        row.remove();
        page = newPage();
        page.list.appendChild(row);
      }
    });
    pages.forEach((item, index) => { item.element.querySelector(".dm-page-number")!.textContent = `${index + 1} / ${pages.length}`; });
    for (const [index, item] of pages.entries()) {
      const canvas = await html2canvas(item.element, { scale: 2, backgroundColor: "#ffffff", useCORS: true });
      if (index > 0) pdf.addPage();
      pdf.addImage(canvas.toDataURL("image/jpeg", 0.94), "JPEG", 0, 0, pdf.internal.pageSize.getWidth(), pdf.internal.pageSize.getHeight());
    }
    const safeProject = context.project.replace(/[\\/:*?"<>|]/g, "-").trim() || "Project";
    pdf.save(`Defect by Module - ${safeProject}.pdf`);
  } finally {
    host.remove();
  }
}
