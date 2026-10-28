import { useCallback, useEffect, useMemo, useState } from "react";
import { confirmDialog } from "../components/dialogStore";
import { Badge } from "../components/Badge";
import { apiUrl, escapeHtml } from "../api";
import { formatThaiDateTime } from "../dateTime";
import { defectAgeDays } from "../shared/defects";
import type { ProjectItem, ReleaseItem, BuildItem, DefectItem } from "../shared/types";
import "../TestSummary.css";

type TestSummaryStatusSlice = { status: string; count: number; color: string };
type TestSummarySeveritySlice = { severity: string; count: number; color: string };
type TestSummaryModule = { moduleId: string; parentModuleId?: string | null; moduleCode: string; moduleName: string; testCases: number; executed: number; executionPercent: number; passRate: number; health: string; openDefects?: number };
type TestSummaryData = { totalRequirements: number; coveredRequirements: number; requirementCoverage: number; totalCases: number; executedCases: number; executionProgress: number; passedCases: number; passRate: number; openP0: number; openP1: number; overallScore: number | null; totalDefects: number; openDefects: number; criticalDefects: number; highDefects: number; defectQuality: number; recommendedDecision: string; statusDistribution: TestSummaryStatusSlice[]; defectSeverityDistribution: TestSummarySeveritySlice[]; modules?: TestSummaryModule[]; generatedAt: string };
type TestSummaryEnv = { testEnvironmentId: string; projectId: string; environmentName: string; baseUrl?: string; isActive: boolean };
type TestSummaryNarrative = { knownIssues: string; remainingRisks: string; qaRecommendation: string };
type TestSummarySnapshot = { date: string; passRate: number; executionProgress: number; openP0: number; openDefects: number };

export function TestSummaryPage({ projects, projectId: contextProjectId, releaseId: contextReleaseId, canExport, onOpenSignoff, onOpenRisks }: { projects: ProjectItem[]; projectId?: string; releaseId?: string; buildId?: string; canExport: boolean; onOpenSignoff?: () => void; onOpenRisks?: () => void }) {
  const [projectId, setProjectId] = useState(contextProjectId ?? "");
  const [releases, setReleases] = useState<ReleaseItem[]>([]);
  const [releaseId, setReleaseId] = useState(contextReleaseId ?? "");
  const [summary, setSummary] = useState<TestSummaryData>(null!);
  const [release, setRelease] = useState<ReleaseItem | null>(null);
  const [builds, setBuilds] = useState<BuildItem[]>([]);
  const [envs, setEnvs] = useState<TestSummaryEnv[]>([]);
  const [topDefects, setTopDefects] = useState<DefectItem[]>([]);
  const [presentationDate, setPresentationDate] = useState("");
  const [previousSnapshot, setPreviousSnapshot] = useState<TestSummarySnapshot | null>(null);
  const [presenterMode, setPresenterMode] = useState(false);
  const [selectedGateLabel, setSelectedGateLabel] = useState("Execution Progress");
  const [expandedDefectId, setExpandedDefectId] = useState("");
  const [focusedEvidence, setFocusedEvidence] = useState("");
  const [narrative, setNarrative] = useState<TestSummaryNarrative>({ knownIssues: "", remainingRisks: "", qaRecommendation: "" });
  const [narrativeReleaseId, setNarrativeReleaseId] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [releasesLoaded, setReleasesLoaded] = useState(false);
  const [exportingPdf, setExportingPdf] = useState(false);
  const headers = useMemo(() => ({ Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` }), []);
  // ไม่ OK = throw — เดิมคืน null ทำให้ Test Summary ที่โหลดไม่สำเร็จแสดงเป็น "ยังไม่มีข้อมูล"; ข้อมูลเสริมใส่ .catch(() => null) เอง
  const getJson = useCallback((url: string): Promise<any> => fetch(url, { headers }).then((r) => { if (!r.ok) throw new Error(`โหลดข้อมูลไม่สำเร็จ (${r.status})`); return r.json(); }), [headers]);
  useEffect(() => { if (contextProjectId) setProjectId(contextProjectId); }, [contextProjectId]);
  useEffect(() => { if (!projectId) { setReleases([]); setReleasesLoaded(true); return; } setReleasesLoaded(false); getJson(`${apiUrl}/releases?projectId=${projectId}`).then((rs) => setReleases(Array.isArray(rs) ? (rs as ReleaseItem[]).filter((x) => x.status !== "Cancelled") : [])).catch(() => setError("โหลดรายการ Release ไม่สำเร็จ")).finally(() => setReleasesLoaded(true)); }, [projectId, getJson]);
  useEffect(() => { if (contextReleaseId && !releaseId && releases.some((x) => x.releaseId === contextReleaseId)) setReleaseId(contextReleaseId); }, [contextReleaseId, releaseId, releases]);
  useEffect(() => { if (releasesLoaded && releaseId && !releases.some((x) => x.releaseId === releaseId)) setReleaseId(""); }, [releasesLoaded, releaseId, releases]);
  useEffect(() => {
    if (!presenterMode) return;
    const exitPresenter = (event: KeyboardEvent) => { if (event.key === "Escape") setPresenterMode(false); };
    window.addEventListener("keydown", exitPresenter);
    return () => window.removeEventListener("keydown", exitPresenter);
  }, [presenterMode]);
  const derive = (s: TestSummaryData | null): TestSummaryNarrative => {
    if (!s) return { knownIssues: "", remainingRisks: "", qaRecommendation: "" };
    const issues = [`P0 ที่ยังไม่ผ่าน/ถูกบล็อก: ${s.openP0}`, `P1 ที่ยังไม่ผ่าน/ถูกบล็อก: ${s.openP1}`, `ข้อบกพร่องที่ยังเปิด: ${s.openDefects} (วิกฤต ${s.criticalDefects} / สูง ${s.highDefects})`].filter((x) => !x.endsWith(": 0")).join(" · ") || "ไม่มีข้อบกพร่องหรือเคสค้างที่ต้องติดตาม";
    const risks: string[] = [];
    if (s.requirementCoverage < 90) risks.push(`ความครอบคลุม ${s.requirementCoverage}% ต่ำกว่าเกณฑ์`);
    if (s.passRate < 90) risks.push(`อัตราผ่าน ${s.passRate}% ต่ำกว่าเกณฑ์`);
    if (s.openP1 > 0 || s.highDefects > 0) risks.push("มี P1/High ค้างที่ควรประเมินก่อนวาง");
    if (s.criticalDefects > 0) risks.push(`มี Defect ระดับ Critical ${s.criticalDefects} รายการที่ต้องแก้ก่อนปล่อย`);
    if (s.openP0 > 0) risks.push(`มีผลทดสอบ Priority P0 ค้าง ${s.openP0} รายการที่ต้องแก้ ตรวจสอบ หรืออนุมัติความเสี่ยงก่อนปล่อย`);
    const recText = s.recommendedDecision === "NO-GO" ? "ไม่พร้อมวาง Release — ยังมี P0/วิกฤตหรือ Coverage/Pass rate ไม่ผ่านเกณฑ์ ต้องแก้และทดสอบซ้ำก่อน Sign-off" : s.recommendedDecision === "CONDITIONAL GO" ? "พร้อมแบบมีเงื่อนไข — วาง Release ได้โดยมีเงื่อนไขให้ติดตาม/ปิดความเสี่ยงที่เหลือตามแผน" : s.recommendedDecision === "GO" ? "พร้อมวาง Release — ผ่านเกณฑ์คุณภาพและความครอบคลุมที่กำหนด" : "ยังไม่มีข้อมูลเพียงพอสำหรับการประเมิน กรุณารัน Test ให้ครบและบันทึกผลก่อนสรุป";
    return { knownIssues: issues, remainingRisks: risks.length ? risks.join(" · ") : "ไม่พบความเสี่ยงคงค้างที่เกินเกณฑ์", qaRecommendation: recText };
  };
  const load = useCallback(async (regenerate: boolean) => {
    if (!projectId || !releaseId) { setSummary(null!); setRelease(null); setBuilds([]); setTopDefects([]); setPresentationDate(""); setPreviousSnapshot(null); setNarrativeReleaseId(""); return; }
    setLoading(true); setError("");
    try {
      const [ts, envList, defectList, buildList] = await Promise.all([
        getJson(`${apiUrl}/releases/${releaseId}/test-summary`),
        getJson(`${apiUrl}/master-settings/environments`).catch(() => null),
        getJson(`${apiUrl}/defects?projectId=${projectId}&releaseId=${releaseId}&page=1&size=100`).catch(() => null),
        getJson(`${apiUrl}/releases/${releaseId}/builds`).catch(() => null),
      ]);
      const data = (ts as { release: ReleaseItem; summary: TestSummaryData; generatedAt?: string } | null);
      const generatedAt = data?.generatedAt ?? data?.summary?.generatedAt ?? "";
      setSummary(data?.summary ? { ...data.summary, generatedAt } : null!);
      setRelease((data?.release as ReleaseItem | null) ?? null);
      setBuilds(Array.isArray(buildList) ? (buildList as BuildItem[]).filter((b) => b.isActive) : []);
      setEnvs(Array.isArray(envList) ? (envList as TestSummaryEnv[]).filter((e) => e.projectId === projectId) : []);
      const defectRows = Array.isArray(defectList) ? defectList : Array.isArray(defectList?.rows) ? defectList.rows : Array.isArray(defectList?.items) ? defectList.items : Array.isArray(defectList?.items?.rows) ? defectList.items.rows : [];
      const severityOrder: Record<string, number> = { Critical: 0, High: 1, Medium: 2, Low: 3 };
      setTopDefects((defectRows as DefectItem[])
        .filter((d) => !["Resolved", "Closed", "Rejected"].includes(d.status))
        .sort((a, b) => (severityOrder[a.severity] ?? 9) - (severityOrder[b.severity] ?? 9) || defectAgeDays(b.createdAt) - defectAgeDays(a.createdAt))
        .slice(0, 5));
      const storedPresentationDate = localStorage.getItem(`qa.testSummaryPresentationDate.${releaseId}`) ?? "";
      if (storedPresentationDate) setPresentationDate(storedPresentationDate);
      else {
        const reference = generatedAt ? new Date(generatedAt) : new Date();
        const target = new Date(reference.getFullYear(), reference.getMonth(), 21);
        const referenceDay = new Date(reference.getFullYear(), reference.getMonth(), reference.getDate());
        if (target < referenceDay) target.setMonth(target.getMonth() + 1);
        const pad = (value: number) => String(value).padStart(2, "0");
        setPresentationDate(`${target.getFullYear()}-${pad(target.getMonth() + 1)}-${pad(target.getDate())}`);
      }
      if (data?.summary) {
        const historyKey = `qa.testSummaryHistory.${releaseId}`;
        const history = (() => { try { const value = JSON.parse(localStorage.getItem(historyKey) ?? "[]"); return Array.isArray(value) ? value as TestSummarySnapshot[] : []; } catch { return []; } })();
        const snapshotDate = (generatedAt || new Date().toISOString()).slice(0, 10);
        setPreviousSnapshot([...history].reverse().find((item) => item.date !== snapshotDate) ?? null);
        const currentSnapshot: TestSummarySnapshot = { date: snapshotDate, passRate: data.summary.passRate, executionProgress: data.summary.executionProgress, openP0: data.summary.openP0, openDefects: data.summary.openDefects };
        const nextHistory = [...history.filter((item) => item.date !== snapshotDate), currentSnapshot].slice(-30);
        try { localStorage.setItem(historyKey, JSON.stringify(nextHistory)); } catch { /* ignore */ }
      }
      const persisted = (() => { try { return JSON.parse(localStorage.getItem(`qa.testSummaryNarrative.${releaseId}`) ?? "null"); } catch { return null; } })() as TestSummaryNarrative | null;
      setNarrative(regenerate || !persisted ? derive(data?.summary ?? null) : { ...persisted, ...(persisted.knownIssues || persisted.remainingRisks || persisted.qaRecommendation ? {} : derive(data?.summary ?? null)) });
      setNarrativeReleaseId(releaseId);
    } catch (e) { setError(e instanceof Error ? e.message : "โหลด Test Summary ไม่สำเร็จ"); } finally { setLoading(false); }
  }, [projectId, releaseId, getJson]);
  useEffect(() => { load(false); }, [load]);
  useEffect(() => {
    if (!releaseId || narrativeReleaseId !== releaseId) return;
    try { localStorage.setItem(`qa.testSummaryNarrative.${releaseId}`, JSON.stringify(narrative)); } catch { /* ignore */ }
  }, [releaseId, narrative, narrativeReleaseId]);
  const exportCsv = () => {
    if (!summary) return;
    const rows: [string, string | number][] = [
      ["Release", `${release?.releaseCode ?? ""} ${release?.version ?? ""}`.trim()],
      ["Generated At", summary.generatedAt || "ยังไม่ได้ระบุ"],
      ["Status", release?.status ?? ""],
      ["Requirement Coverage", `${summary.requirementCoverage}%`],
      ["Total Test Cases", summary.totalCases],
      ["Executed", summary.executedCases],
      ["Execution Progress", `${summary.executionProgress}%`],
      ["Passed", summary.passedCases],
      ["Pass Rate", `${summary.passRate}%`],
      ["Open P0 / P1", `${summary.openP0} / ${summary.openP1}`],
      ["Open Defects", summary.openDefects],
      ["Critical Defects", summary.criticalDefects],
      ["High Defects", summary.highDefects],
      ["Defect Quality", summary.defectQuality],
      ["Recommended Decision", summary.recommendedDecision],
      ["Requirement Covered / Not Covered", `${summary.coveredRequirements} / ${Math.max(0, summary.totalRequirements - summary.coveredRequirements)}`],
      ["Defect Total / Open / Resolved-Closed", `${summary.totalDefects} / ${summary.openDefects} / ${Math.max(0, summary.totalDefects - summary.openDefects)}`],
      ["Hard Blockers", summary.criticalDefects || summary.openP0 ? `Critical ${summary.criticalDefects} · P0 ${summary.openP0}` : "ไม่พบ Hard Blocker"],
      ["Warnings", summary.openP1 || summary.highDefects || summary.requirementCoverage < 90 || summary.passRate < 90 ? `P1 ${summary.openP1} · High ${summary.highDefects} · Coverage ${summary.requirementCoverage}% · Pass Rate ${summary.passRate}%` : "ไม่พบ Warning ที่เกินเกณฑ์"],
      ["Release Impact", `ระดับ${releaseImpactLevelLabel}`],
      ["Release Impact Modules", releaseImpactModules.length ? releaseImpactModules.map((module) => `${module.moduleCode || module.moduleName} (${module.openDefects ?? 0} Open Defect)`).join(" · ") : "ไม่พบโมดูลที่มีสัญญาณผลกระทบ"],
      ["Planned Release Date", releaseImpactDate],
      ["Build Change Notes", releaseImpactBuild?.changeNotes?.trim() || "ยังไม่ได้ระบุ"],
      ["Release Impact Action", releaseImpactAction],
      ["Known Issues", narrative.knownIssues],
      ["Remaining Risks", narrative.remainingRisks],
      ["QA Recommendation", narrative.qaRecommendation],
    ];
    const csv = "\uFEFF" + rows.map(([k, v]) => `"${String(k).replaceAll('"', '""')}","${String(v).replaceAll('"', '""')}"`).join("\r\n");
    const blob = new Blob([csv], { type: "text/csv;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url; a.download = `test-summary-${release?.releaseCode || releaseId}.csv`; a.click();
    setTimeout(() => URL.revokeObjectURL(url), 60_000);
  };
  const exportExcel = () => {
    if (!summary) return;
    const s = summary, r = release;
    const esc = escapeHtml;
    const row = (cells: string[]) => `<tr>${cells.map((c) => `<td>${esc(c)}</td>`).join("")}</tr>`;
    const body = `<table border="1"><thead><tr><th colspan="2">Test Summary — ${esc(r ? `${r.releaseCode} · ${r.version}` : "")}</th></tr></thead><tbody>${row(["Generated At", s.generatedAt || "ยังไม่ได้ระบุ"])}${row(["Status", r?.status ?? ""])}${row(["Requirement Coverage", `${s.requirementCoverage}%`])}${row(["Requirement Covered / Not Covered", `${s.coveredRequirements} / ${Math.max(0, s.totalRequirements - s.coveredRequirements)}`])}${row(["Total / Executed", `${s.totalCases} / ${s.executedCases}`])}${row(["Pass Rate", `${s.passRate}%`])}${row(["Open P0 / P1", `${s.openP0} / ${s.openP1}`])}${row(["Open Defects", String(s.openDefects)])}${row(["Defect Total / Open / Resolved-Closed", `${s.totalDefects} / ${s.openDefects} / ${Math.max(0, s.totalDefects - s.openDefects)}`])}${row(["Critical / High", `${s.criticalDefects} / ${s.highDefects}`])}${row(["Defect Quality", String(s.defectQuality)])}${row(["Recommended Decision", s.recommendedDecision])}${row(["Hard Blockers", s.criticalDefects || s.openP0 ? `Critical ${s.criticalDefects} · P0 ${s.openP0}` : "ไม่พบ Hard Blocker"])}${row(["Warnings", s.openP1 || s.highDefects || s.requirementCoverage < 90 || s.passRate < 90 ? `P1 ${s.openP1} · High ${s.highDefects} · Coverage ${s.requirementCoverage}% · Pass Rate ${s.passRate}%` : "ไม่พบ Warning ที่เกินเกณฑ์"])}${row(["Release Impact", `ระดับ${releaseImpactLevelLabel}`])}${row(["Release Impact Modules", releaseImpactModules.length ? releaseImpactModules.map((module) => `${module.moduleCode || module.moduleName} (${module.openDefects ?? 0} Open Defect)`).join(" · ") : "ไม่พบโมดูลที่มีสัญญาณผลกระทบ"])}${row(["Planned Release Date", releaseImpactDate])}${row(["Build Change Notes", releaseImpactBuild?.changeNotes?.trim() || "ยังไม่ได้ระบุ"])}${row(["Release Impact Action", releaseImpactAction])}${row(["Known Issues", narrative.knownIssues])}${row(["Remaining Risks", narrative.remainingRisks])}${row(["QA Recommendation", narrative.qaRecommendation])}</tbody></table>`;
    const html = `<html><head><meta charset="utf-8"></head><body>${body}</body></html>`;
    const blob = new Blob([html], { type: "application/vnd.ms-excel;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url; a.download = `test-summary-${r?.releaseCode || releaseId}.xls`; a.click();
    setTimeout(() => URL.revokeObjectURL(url), 60_000);
  };
  const exportPdf = async () => {
    if (!summary || exportingPdf) return;
    setExportingPdf(true);
    try {
      const [{ default: html2canvas }, { jsPDF }] = await Promise.all([import("html2canvas"), import("jspdf")]);
      const pdf = new jsPDF({ orientation: "portrait", unit: "pt", format: "a4", compress: true });
      const pageWidth = pdf.internal.pageSize.getWidth();
      const pageHeight = pdf.internal.pageSize.getHeight();
      const toneGood = { bg: "#eaf8f1", fg: "#168b58", accent: "#169c63" };
      const toneWarn = { bg: "#fff5d9", fg: "#9a6d00", accent: "#d79a00" };
      const toneBad = { bg: "#fdecec", fg: "#c83a3a", accent: "#d64545" };
      const toneByLevel = (level: "good" | "warn" | "bad") => level === "good" ? toneGood : level === "warn" ? toneWarn : toneBad;
      const decisionTone = summary.recommendedDecision === "GO" ? toneGood : summary.recommendedDecision === "CONDITIONAL GO" ? toneWarn : toneBad;
      // สีของ KPI แต่ละตัวอิงเกณฑ์เดียวกับ Quality Gates ของหน้าจอ (Coverage/Pass Rate ≥ 90%, Execution ต้องครบ 100%)
      const passRateTone = toneByLevel(summary.passRate >= 90 ? "good" : summary.passRate >= 75 ? "warn" : "bad");
      const coverageTone = toneByLevel(summary.requirementCoverage >= 90 ? "good" : summary.requirementCoverage >= 75 ? "warn" : "bad");
      const executionTone = toneByLevel(summary.executionProgress >= 100 ? "good" : summary.executionProgress >= 75 ? "warn" : "bad");
      const defectQualityTone = toneByLevel(summary.defectQuality >= 80 ? "good" : summary.defectQuality >= 50 ? "warn" : "bad");
      const hardBlockersTone = summary.criticalDefects || summary.openP0 ? toneBad : toneGood;
      const warningsTone = summary.openP1 || summary.highDefects || summary.requirementCoverage < 90 || summary.passRate < 90 ? toneWarn : toneGood;
      const openRiskTone = summary.openP0 > 0 ? toneBad : summary.openP1 > 0 ? toneWarn : toneGood;
      const impactTone = releaseImpactLevel === "high" ? toneBad : releaseImpactLevel === "medium" ? toneWarn : releaseImpactLevel === "low" ? toneGood : { bg: "#eef4ff", fg: "#2457d6", accent: "#5b8cff" };
      const currentProject = projects.find((p) => p.projectId === projectId);
      const projectName = currentProject?.projectName ?? "";
      const projectCode = currentProject?.projectCode || projectName || "Project";
      const rcBuild = builds.find((b) => b.isReleaseCandidate) ?? builds[0] ?? null;
      const versionLine = `Version ${release?.version ?? "-"}${rcBuild?.applicationVersion ? ` ${rcBuild.applicationVersion}` : ""}${rcBuild?.buildNumber ? ` ${rcBuild.buildNumber}` : ""}`;
      const generatedAtLabel = summary.generatedAt ? `ข้อมูล ณ ${new Date(summary.generatedAt).toLocaleString("th-TH", { dateStyle: "medium", timeStyle: "short" })}` : "";
      const margin = 24;
      const contentWidth = pageWidth - margin * 2;
      const usableHeight = pageHeight - margin * 2;
      // บังคับความกว้างของพื้นที่แคปให้ใกล้เคียงสัดส่วนจริงของหน้า A4 (แทนความกว้างจอเดสก์ท็อป)
      // ไม่งั้นตัวอักษรจะถูกย่อเล็กลงมากเวลาบีบภาพความกว้างจอกว้าง ๆ ให้พอดีหน้ากระดาษ
      const printFontScale = 0.85;
      const printWidthPx = Math.round(contentWidth / printFontScale);
      const esc = escapeHtml;

      // เนื้อหาหน้าถัดจากปก: คัดเฉพาะสิ่งที่ผู้บริหารต้องใช้ตัดสินใจ (ไม่ใช่ทุกอย่างที่อยู่บนหน้าจอ)
      const pdfRemainingCases = Math.max(0, summary.totalCases - summary.executedCases);
      const pdfUncoveredRequirements = Math.max(0, summary.totalRequirements - summary.coveredRequirements);
      const pdfPassRateGap = Math.max(0, Number((90 - summary.passRate).toFixed(1)));
      const headline = summary.recommendedDecision === "GO" ? "ผลการทดสอบผ่านเกณฑ์ความพร้อม" : summary.recommendedDecision === "CONDITIONAL GO" ? "พร้อมดำเนินการแบบมีเงื่อนไข" : "ผลการทดสอบยังไม่พร้อมอนุมัติ Release";
      const conclusion = summary.recommendedDecision === "GO" ? "พร้อมเสนออนุมัติ Release และดำเนินการ Sign-off" : summary.recommendedDecision === "CONDITIONAL GO" ? "เสนออนุมัติได้เมื่อระบุ Owner เงื่อนไข และกำหนดปิดความเสี่ยงที่เหลือครบถ้วน" : `ยังไม่ควรอนุมัติ Release — เร่งดำเนินการ ${pdfRemainingCases.toLocaleString()} Test Cases ที่เหลือ และจัดการ P0 ${summary.openP0.toLocaleString()} รายการก่อน Sign-off`;
      const hardBlockersText = summary.criticalDefects || summary.openP0 ? `Critical ${summary.criticalDefects} · P0 ${summary.openP0}` : "ไม่พบ Hard Blocker";
      const warningsText = summary.openP1 || summary.highDefects || summary.requirementCoverage < 90 || summary.passRate < 90 ? `P1 ${summary.openP1} · High ${summary.highDefects} · Coverage ${summary.requirementCoverage}% · Pass Rate ${summary.passRate}%` : "ไม่พบ Warning ที่เกินเกณฑ์";
      const nextActionText = summary.recommendedDecision === "GO" ? "ส่งต่อให้ผู้มีอำนาจทำ Sign-off" : "แก้ไข/ประเมินความเสี่ยงและทดสอบซ้ำก่อน Sign-off";
      const pdfImpactModules = releaseImpactModules.length ? releaseImpactModules.map((module) => `${module.moduleCode || module.moduleName} · ${module.openDefects ?? 0} Open Defect · ${module.executionPercent}% Executed`).join(" | ") : "ไม่พบโมดูลที่มีสัญญาณผลกระทบจากข้อมูลการทดสอบ";
      const kpiTiles = [
        ["Pass Rate", `${summary.passRate}%`, `${summary.passedCases}/${summary.executedCases} Passed`, passRateTone],
        ["Requirement Coverage", `${summary.requirementCoverage}%`, `${summary.coveredRequirements}/${summary.totalRequirements} Covered`, coverageTone],
        ["Execution Progress", `${summary.executionProgress}%`, `${summary.executedCases}/${summary.totalCases} Executed`, executionTone],
        ["Defect Quality", `${summary.defectQuality}`, `${summary.openDefects} Open Defects`, defectQualityTone],
      ] as const;
      const facts = [
        ["ความคืบหน้าการทดสอบ", `${summary.executedCases.toLocaleString()}/${summary.totalCases.toLocaleString()}`, `ดำเนินการแล้ว ${summary.executionProgress}% · เหลือ ${pdfRemainingCases.toLocaleString()} รายการ`, executionTone],
        ["ผลการทดสอบ", `${summary.passedCases.toLocaleString()} Passed`, `Pass Rate ${summary.passRate}%${pdfPassRateGap > 0 ? ` · ต่ำกว่าเกณฑ์ ${pdfPassRateGap} จุด` : " · ผ่านเกณฑ์"}`, passRateTone],
        ["Requirement Coverage", `${summary.coveredRequirements.toLocaleString()}/${summary.totalRequirements.toLocaleString()}`, `Coverage ${summary.requirementCoverage}% · เหลือ ${pdfUncoveredRequirements.toLocaleString()} Requirement`, coverageTone],
        ["ความเสี่ยงคงค้าง", `${summary.openP0.toLocaleString()} P0 · ${summary.openP1.toLocaleString()} P1`, `Open Defect ${summary.openDefects.toLocaleString()} · Critical ${summary.criticalDefects.toLocaleString()}`, openRiskTone],
      ] as const;
      const contentHost = document.createElement("div");
      contentHost.style.cssText = `position:fixed;left:-99999px;top:0;width:${printWidthPx}px;pointer-events:none;font-family:'Kanit',Tahoma,'Noto Sans Thai',Arial,sans-serif;color:#1f2937;`;
      contentHost.innerHTML = `
        <div style="border-radius:16px;background:linear-gradient(135deg,#2457d6,#15306f);padding:22px 26px;color:#fff;">
          <div style="display:flex;align-items:center;justify-content:space-between;gap:16px;">
            <div style="display:flex;align-items:center;gap:12px;">
              <div style="width:40px;height:40px;border-radius:11px;background:rgba(255,255,255,.16);display:flex;align-items:center;justify-content:center;font-weight:800;font-size:16px;flex:0 0 auto;">QA</div>
              <div>
                <div style="font-size:11px;letter-spacing:.4px;opacity:.85;">ProMaxx2 QA Management System</div>
                <div style="font-size:10px;opacity:.65;margin-top:1px;">Executive Test Summary Report</div>
              </div>
            </div>
            ${generatedAtLabel ? `<div style="text-align:right;font-size:10px;opacity:.8;flex:0 0 auto;">${esc(generatedAtLabel)}</div>` : ""}
          </div>
          <div style="margin-top:18px;">
            <div style="font-size:11px;opacity:.75;text-transform:uppercase;letter-spacing:.4px;">Release</div>
            <div style="font-size:22px;font-weight:800;margin-top:4px;">${esc(release?.releaseCode ?? "-")} · ${esc(versionLine)}</div>
            ${projectName ? `<div style="font-size:12px;opacity:.75;margin-top:4px;">${esc(projectName)}</div>` : ""}
          </div>
        </div>
        <div style="margin-top:16px;">
          <div style="display:inline-block;background:${decisionTone.bg};color:${decisionTone.fg};padding:9px 18px;border-radius:999px;font-size:14px;font-weight:800;line-height:10px;white-space:nowrap;"><span style="display:inline-block;vertical-align:middle;width:8px;height:8px;margin-right:8px;border-radius:50%;background:${decisionTone.accent};"></span><span style="display:inline-block;vertical-align:middle;">${esc(summary.recommendedDecision)}</span></div>
        </div>
        <div style="display:grid;grid-template-columns:repeat(4,1fr);gap:12px;margin-top:16px;">
          ${kpiTiles.map(([label, value, note, tone]) => `<div style="border:1px solid #e5e7eb;border-left:4px solid ${tone.accent};border-radius:14px;padding:16px;background:#fff;"><div style="font-size:11px;color:#667085;">${label}</div><div style="font-size:24px;font-weight:800;color:${tone.fg};margin-top:5px;">${value}</div><div style="font-size:11px;color:#667085;margin-top:3px;">${note}</div></div>`).join("")}
        </div>
        <div style="border:1px solid #dbe5ff;border-left:4px solid ${decisionTone.accent};border-radius:14px;padding:20px 22px;background:#f8faff;margin-top:16px;">
          <div style="font-size:10px;color:#6b7fa8;text-transform:uppercase;letter-spacing:.08em;font-weight:800;">ข้อความสรุปสำหรับผู้บริหาร</div>
          <div style="font-size:17px;font-weight:800;color:#172b4d;margin-top:6px;">${headline}</div>
          <div style="display:grid;grid-template-columns:repeat(2,1fr);gap:10px;margin-top:14px;">
            ${facts.map(([label, value, note, tone]) => `<div style="padding:12px;border:1px solid #e3eaf5;border-radius:10px;background:#fff;border-left:3px solid ${tone.accent};"><div style="font-size:10px;color:#667085;font-weight:700;">${label}</div><div style="font-size:15px;font-weight:800;color:${tone.fg};margin-top:4px;">${value}</div><div style="font-size:11px;color:#667085;margin-top:2px;">${note}</div></div>`).join("")}
          </div>
          <div style="padding:12px 14px;border-radius:10px;background:${decisionTone.bg};color:${decisionTone.fg};font-size:12px;line-height:1.6;margin-top:14px;"><b>ข้อสรุป: </b>${conclusion}</div>
        </div>
        <div style="border:1px solid ${impactTone.bg};border-left:4px solid ${impactTone.accent};border-radius:14px;padding:16px 18px;background:${impactTone.bg};margin-top:16px;">
          <div style="display:flex;align-items:center;justify-content:space-between;gap:12px;"><div style="font-size:11px;color:#667085;font-weight:800;text-transform:uppercase;letter-spacing:.08em;">Release Impact</div><div style="font-size:12px;color:${impactTone.fg};font-weight:800;white-space:nowrap;">ระดับ${esc(releaseImpactLevelLabel)}</div></div>
          <div style="display:grid;grid-template-columns:repeat(3,1fr);gap:10px;margin-top:12px;">
            <div style="padding:10px;border:1px solid #e3eaf5;border-radius:10px;background:#fff;"><div style="font-size:10px;color:#667085;font-weight:700;">กำหนด Release</div><div style="font-size:12px;color:#172b4d;font-weight:800;margin-top:4px;">${esc(releaseImpactDate)}</div></div>
            <div style="padding:10px;border:1px solid #e3eaf5;border-radius:10px;background:#fff;"><div style="font-size:10px;color:#667085;font-weight:700;">โมดูลที่มีสัญญาณกระทบ</div><div style="font-size:12px;color:#172b4d;font-weight:800;margin-top:4px;">${releaseImpactModules.length}/${summaryModules.length}</div></div>
            <div style="padding:10px;border:1px solid #e3eaf5;border-radius:10px;background:#fff;"><div style="font-size:10px;color:#667085;font-weight:700;">Build อ้างอิง</div><div style="font-size:12px;color:#172b4d;font-weight:800;margin-top:4px;">${esc(releaseImpactBuild?.buildNumber || "ยังไม่ได้ระบุ")}</div></div>
          </div>
          <div style="font-size:11px;color:#344054;line-height:1.6;margin-top:12px;"><b>โมดูลที่ควรติดตาม: </b>${esc(pdfImpactModules)}</div>
          <div style="font-size:11px;color:#344054;line-height:1.6;margin-top:5px;"><b>ข้อเสนอถัดไป: </b>${esc(releaseImpactAction)}</div>
        </div>
        <div style="display:grid;grid-template-columns:repeat(3,1fr);gap:12px;margin-top:16px;">
          <div style="border:1px solid #e5e7eb;border-left:3px solid ${hardBlockersTone.accent};border-radius:12px;padding:14px 16px;background:#fff;"><div style="font-size:11px;color:#667085;font-weight:800;">Hard Blockers</div><div style="font-size:12px;color:${hardBlockersTone.fg};font-weight:700;margin-top:6px;line-height:1.55;">${esc(hardBlockersText)}</div></div>
          <div style="border:1px solid #e5e7eb;border-left:3px solid ${warningsTone.accent};border-radius:12px;padding:14px 16px;background:#fff;"><div style="font-size:11px;color:#667085;font-weight:800;">Warnings</div><div style="font-size:12px;color:${warningsTone.fg};font-weight:700;margin-top:6px;line-height:1.55;">${esc(warningsText)}</div></div>
          <div style="border:1px solid #e5e7eb;border-left:3px solid #94a3b8;border-radius:12px;padding:14px 16px;background:#fff;"><div style="font-size:11px;color:#667085;font-weight:800;">Next Action</div><div style="font-size:12px;color:#344054;margin-top:6px;line-height:1.55;">${esc(nextActionText)}</div></div>
        </div>
        <div style="display:grid;gap:12px;margin-top:16px;">
          <div style="border:1px solid #e5e7eb;border-radius:12px;padding:14px 16px;background:#fff;"><div style="font-size:11px;color:#667085;font-weight:800;text-transform:uppercase;">Known Issues</div><div style="font-size:12px;color:#344054;margin-top:6px;line-height:1.6;white-space:pre-line;">${esc(narrative.knownIssues || "-")}</div></div>
          <div style="border:1px solid #e5e7eb;border-radius:12px;padding:14px 16px;background:#fff;"><div style="font-size:11px;color:#667085;font-weight:800;text-transform:uppercase;">Remaining Risks</div><div style="font-size:12px;color:#344054;margin-top:6px;line-height:1.6;white-space:pre-line;">${esc(narrative.remainingRisks || "-")}</div></div>
          <div style="border:1px solid #e5e7eb;border-radius:12px;padding:14px 16px;background:#fff;"><div style="font-size:11px;color:#667085;font-weight:800;text-transform:uppercase;">QA Recommendation</div><div style="font-size:12px;color:#344054;margin-top:6px;line-height:1.6;white-space:pre-line;">${esc(narrative.qaRecommendation || "-")}</div></div>
        </div>
        <div style="margin-top:16px;padding-top:12px;border-top:1px solid #e5e7eb;font-size:10px;color:#667085;">เอกสารนี้จัดทำโดยระบบ ProMaxx2 QA Management System · สำหรับใช้ภายในองค์กร</div>`;
      document.body.appendChild(contentHost);
      try {
        // แคปรูปทีละบล็อกก่อน แล้วรวมความสูงทั้งหมด ถ้าเกิน 1 หน้าให้ย่อสัดส่วนทุกบล็อกลงเท่า ๆ กัน
        // เพื่อบังคับให้เอกสารจบภายในหน้าเดียวเสมอ แทนที่จะขึ้นหน้าใหม่
        const gap = 10;
        const captures: { canvas: HTMLCanvasElement; height: number }[] = [];
        for (const el of Array.from(contentHost.children)) {
          const canvas = await html2canvas(el as HTMLElement, { scale: 2, useCORS: true, backgroundColor: "#ffffff" });
          captures.push({ canvas, height: (canvas.height * contentWidth) / canvas.width });
        }
        const totalHeight = captures.reduce((sum, c) => sum + c.height, 0) + gap * (captures.length - 1);
        const fitScale = totalHeight > usableHeight ? usableHeight / totalHeight : 1;
        let cursorY = margin;
        for (const { canvas, height } of captures) {
          const drawWidth = contentWidth * fitScale;
          const drawHeight = height * fitScale;
          const x = margin + (contentWidth - drawWidth) / 2;
          pdf.addImage(canvas.toDataURL("image/jpeg", 0.92), "JPEG", x, cursorY, drawWidth, drawHeight);
          cursorY += drawHeight + gap * fitScale;
        }
      } finally {
        document.body.removeChild(contentHost);
      }
      const fileVersion = `${release?.version ?? ""}${rcBuild?.applicationVersion ? ` ${rcBuild.applicationVersion}` : ""}${rcBuild?.buildNumber ? ` ${rcBuild.buildNumber}` : ""}`.trim();
      pdf.save(`Report Test Summary ${projectCode}-${fileVersion}.pdf`);
    } catch {
      setError("สร้างไฟล์ PDF ไม่สำเร็จ กรุณาลองใหม่อีกครั้ง");
    } finally {
      setExportingPdf(false);
    }
  };
  const remainingCases = Math.max(0, (summary?.totalCases ?? 0) - (summary?.executedCases ?? 0));
  const uncoveredRequirements = Math.max(0, (summary?.totalRequirements ?? 0) - (summary?.coveredRequirements ?? 0));
  const passRateGap = Math.max(0, Number((90 - (summary?.passRate ?? 0)).toFixed(1)));
  const qualityGates = summary ? [
    { label: "Requirement Coverage", value: `${summary.requirementCoverage}%`, target: "เกณฑ์ ≥ 90%", status: summary.requirementCoverage >= 90 ? "pass" : "fail", detail: `ครอบคลุม ${summary.coveredRequirements.toLocaleString()} จาก ${summary.totalRequirements.toLocaleString()} Requirements`, action: uncoveredRequirements > 0 ? `เชื่อมโยง Test Case ให้ Requirement ที่เหลือ ${uncoveredRequirements.toLocaleString()} รายการ` : "รักษา Coverage และตรวจความถูกต้องของ Traceability" },
    { label: "Critical Defect", value: summary.criticalDefects.toLocaleString(), target: "เกณฑ์ = 0", status: summary.criticalDefects === 0 ? "pass" : "fail", detail: `พบ Critical Defect ที่ยังเปิด ${summary.criticalDefects.toLocaleString()} รายการ`, action: summary.criticalDefects > 0 ? "แก้ไขและ Retest Critical Defect ทั้งหมดก่อน Sign-off" : "ไม่พบ Critical Defect ให้ติดตามไม่ให้เกิดรายการใหม่" },
    { label: "Execution Progress", value: `${summary.executionProgress}%`, target: "เกณฑ์ = 100%", status: summary.executionProgress >= 100 ? "pass" : "fail", detail: `รันแล้ว ${summary.executedCases.toLocaleString()} จาก ${summary.totalCases.toLocaleString()} Test Cases`, action: remainingCases > 0 ? `รัน Test Case ที่เหลือ ${remainingCases.toLocaleString()} รายการ` : "Execution ครบแล้ว ให้ตรวจสอบผล Fail และ Blocked" },
    { label: "Pass Rate", value: `${summary.passRate}%`, target: "เกณฑ์ ≥ 90%", status: summary.passRate >= 90 ? "pass" : "fail", detail: `ผ่าน ${summary.passedCases.toLocaleString()} จาก ${summary.executedCases.toLocaleString()} Test Cases ที่รันแล้ว`, action: passRateGap > 0 ? `เพิ่ม Pass Rate อีก ${passRateGap.toLocaleString()} จุด ด้วยการแก้ไขและ Retest` : "ผ่านเกณฑ์แล้ว ให้รักษาระดับจนจบรอบ Regression" },
    { label: "Priority P0", value: summary.openP0.toLocaleString(), target: "เกณฑ์ = 0", status: summary.openP0 === 0 ? "pass" : "fail", detail: `มีผลทดสอบ Priority P0 ค้าง ${summary.openP0.toLocaleString()} รายการ`, action: summary.openP0 > 0 ? "ระบุ Owner แก้ไข Retest หรือขออนุมัติ Risk Acceptance" : "ไม่พบ P0 ค้าง สามารถประเมิน Gate ถัดไปได้" },
    { label: "Regression", value: "ยังไม่มีข้อมูล", target: "ต้อง Completed", status: "unknown", detail: "Test Summary API ยังไม่มีหลักฐานผล Regression สำหรับ Release นี้", action: "ดำเนินการ Regression และบันทึกผลก่อนนำเสนอหรือ Sign-off" },
  ] as const : [];
  const selectedGate = qualityGates.find((gate) => gate.label === selectedGateLabel) ?? qualityGates[0];
  const passedGateCount = qualityGates.filter((gate) => gate.status === "pass").length;
  const failedGateCount = qualityGates.filter((gate) => gate.status === "fail").length;
  const forecastTarget = presentationDate ? new Date(`${presentationDate}T00:00:00`) : null;
  const forecastReferenceValue = summary?.generatedAt ? new Date(summary.generatedAt) : new Date();
  const forecastReference = new Date(forecastReferenceValue.getFullYear(), forecastReferenceValue.getMonth(), forecastReferenceValue.getDate());
  const daysToPresentation = forecastTarget && !Number.isNaN(forecastTarget.getTime()) ? Math.max(0, Math.ceil((forecastTarget.getTime() - forecastReference.getTime()) / 86_400_000)) : 0;
  const requiredCasesPerDay = remainingCases > 0 && daysToPresentation > 0 ? Math.ceil(remainingCases / daysToPresentation) : remainingCases;
  const trendMetrics = summary ? [
    { label: "Pass Rate", current: summary.passRate, previous: previousSnapshot?.passRate, unit: "%", lowerIsBetter: false },
    { label: "Execution", current: summary.executionProgress, previous: previousSnapshot?.executionProgress, unit: "%", lowerIsBetter: false },
    { label: "Open P0", current: summary.openP0, previous: previousSnapshot?.openP0, unit: "", lowerIsBetter: true },
    { label: "Open Defects", current: summary.openDefects, previous: previousSnapshot?.openDefects, unit: "", lowerIsBetter: true },
  ] : [];
  const reportEvidence = [
    { label: "Scope", ready: Boolean(release?.scope?.trim()) },
    { label: "Environment", ready: envs.length > 0 },
    { label: "Out-of-Scope", ready: false },
    { label: "Regression", ready: false },
    { label: "Installation / Update", ready: false },
    { label: "Performance", ready: false },
  ];
  const readyEvidenceCount = reportEvidence.filter((item) => item.ready).length;
  const evidenceCompleteness = Math.round(readyEvidenceCount / reportEvidence.length * 100);
  const confidenceLabel = evidenceCompleteness >= 80 ? "High" : evidenceCompleteness >= 50 ? "Medium" : "Low";
  const openGateDetail = (label: string) => {
    setSelectedGateLabel(label);
    document.querySelector(".ts-quality-gates")?.scrollIntoView({ behavior: "smooth", block: "center" });
  };
  const openEvidenceDetail = (label: string) => {
    setFocusedEvidence(label);
    setPresenterMode(false);
    setTimeout(() => document.getElementById("ts-release-report")?.scrollIntoView({ behavior: "smooth", block: "center" }), 0);
  };
  const summaryModules = (summary?.modules ?? []).filter((module) => module.testCases > 0);
  const untestedModules = summaryModules.filter((module) => module.executed === 0);
  const incompleteModules = summaryModules.filter((module) => module.executed > 0 && module.executed < module.testCases);
  const releaseImpactBuild = builds.find((build) => build.isReleaseCandidate) ?? builds[0] ?? null;
  const releaseImpactModules = [...summaryModules]
    .filter((module) => (module.openDefects ?? 0) > 0 || module.executed < module.testCases || module.health === "Risk" || module.health === "Watch")
    .sort((a, b) => (b.openDefects ?? 0) - (a.openDefects ?? 0) || (a.executionPercent ?? 0) - (b.executionPercent ?? 0) || a.moduleName.localeCompare(b.moduleName))
    .slice(0, 6);
  const releaseImpactLevel = !summary ? "unknown" : summary.criticalDefects > 0 || summary.openP0 > 0 ? "high" : summary.openP1 > 0 || summary.highDefects > 0 || summary.executionProgress < 100 || summary.passRate < 90 ? "medium" : "low";
  const releaseImpactLevelLabel = releaseImpactLevel === "high" ? "สูง" : releaseImpactLevel === "medium" ? "กลาง" : releaseImpactLevel === "low" ? "ต่ำ" : "ยังไม่มีข้อมูล";
  const releaseImpactDelivery = !summary ? "ยังไม่มีข้อมูล Test Summary" : remainingCases > 0 ? `เหลือ ${remainingCases.toLocaleString()} Test Cases ที่ยังไม่รัน` : summary.passRate < 90 ? `Execution ครบแล้ว แต่ Pass Rate ยังอยู่ที่ ${summary.passRate}%` : "Execution ครบและ Pass Rate ผ่านเกณฑ์";
  const releaseImpactAction = !summary ? "Generate Test Summary เพื่อประเมินผลกระทบ" : summary.criticalDefects > 0 || summary.openP0 > 0 ? "แก้ไขหรือทำ Risk Acceptance สำหรับ P0/Critical ก่อน Sign-off" : remainingCases > 0 ? "จัดลำดับรัน Test Case ของโมดูลที่ได้รับผลกระทบและทำ Retest" : summary.passRate < 90 ? "วิเคราะห์ Fail และทำ Retest ให้ Pass Rate ถึงเกณฑ์" : "ยืนยันผลกระทบกับ Business Owner ก่อนอนุมัติ Release";
  const releaseImpactDateValue = release?.plannedReleaseDate ? new Date(release.plannedReleaseDate) : null;
  const releaseImpactDate = releaseImpactDateValue && !Number.isNaN(releaseImpactDateValue.getTime()) ? releaseImpactDateValue.toLocaleDateString("th-TH", { dateStyle: "medium" }) : "ยังไม่ได้ระบุ";
  if (releaseId && !summary && !loading && !error) return <article className="test-summary"><div className="empty"><p>ยังไม่มีข้อมูล Test Summary</p></div></article>;
  return (
    <article className={`test-summary${presenterMode ? " is-presenter" : ""}`}>
      {presenterMode && <div className="ts-presenter-bar"><div><span className="material-symbols-outlined" aria-hidden="true">present_to_all</span><p><b>Executive Presenter Mode</b><small>{release ? `${release.releaseCode} · Version ${release.version}` : "Test Summary"}</small></p></div><button type="button" className="btn" onClick={() => setPresenterMode(false)}><span className="material-symbols-outlined" aria-hidden="true">close_fullscreen</span> ออกจากโหมดนำเสนอ</button></div>}
      <header className="test-summary-head">
        <div className="ts-select">
          <label>Project</label>
          <select aria-label="เลือก Project" value={projectId} onChange={(e) => { setProjectId(e.target.value); setReleaseId(""); }}>
            <option value="">เลือก Project</option>
            {projects.map((p) => <option key={p.projectId} value={p.projectId}>{p.projectCode} · {p.projectName}</option>)}
          </select>
        </div>
        <div className="ts-select">
          <label>Release</label>
          <select aria-label="เลือก Release" value={releaseId} onChange={(e) => setReleaseId(e.target.value)} disabled={!releases.length}>
            <option value="">เลือก Release</option>
            {releases.map((r) => <option key={r.releaseId} value={r.releaseId}>{r.releaseCode} · Version {r.version}</option>)}
          </select>
        </div>
        <div>
          {canExport && <button className="btn" disabled={!summary} onClick={exportCsv}><span className="material-symbols-outlined" aria-hidden="true">download</span> Export CSV</button>}
          {canExport && <button className="btn" disabled={!summary} onClick={exportExcel}><span className="material-symbols-outlined" aria-hidden="true">download</span> Export Excel</button>}
          {canExport && <button className="btn" disabled={!summary || exportingPdf} onClick={exportPdf}>{exportingPdf ? <><span className="spinner inline" aria-hidden="true" /> กำลังสร้าง PDF...</> : <><span className="material-symbols-outlined" aria-hidden="true">picture_as_pdf</span> Export PDF</>}</button>}
          <button className="btn" disabled={!summary} onClick={() => setPresenterMode(true)}><span className="material-symbols-outlined" aria-hidden="true">present_to_all</span> Presenter Mode</button>
          <button className="btn primary" disabled={!releaseId || loading} onClick={async () => { if ((narrative.knownIssues || narrative.remainingRisks || narrative.qaRecommendation).trim() && !await confirmDialog({ title: "สร้างข้อความสรุปใหม่", message: "ข้อความ Known Issues / Remaining Risks / QA Recommendation ที่แก้ไว้จะถูกแทนที่ด้วยข้อความที่สร้างใหม่ ต้องการดำเนินการต่อหรือไม่?", confirmLabel: "สร้างใหม่", tone: "danger" })) return; load(true); }}>{loading ? <><span className="spinner inline" aria-hidden="true" /> กำลังโหลด...</> : "✦ Generate / Regenerate"}</button>
          {onOpenSignoff && <button className="btn" disabled={!summary} onClick={onOpenSignoff}>ไปหน้า Sign-off <span className="material-symbols-outlined" aria-hidden="true">arrow_forward</span></button>}
        </div>
      </header>
      {error && <div className="inline-alert error" role="alert"><span>{error}</span><button className="btn" disabled={loading || !releaseId} onClick={() => load(false)}>ลองโหลดใหม่</button></div>}
      {loading && !summary ? <div className="empty" role="status" aria-live="polite"><div className="spinner" aria-hidden="true" /><p>กำลังโหลด Test Summary...</p></div> : !release ? <div className="empty"><p>เลือก Release เพื่อดูสรุปผลการทดสอบ</p></div> : (
        <>
          <section className="card">
            <div className="test-summary-card">
              <div className="test-summary-head">
                <div><span className="ts-badge"><Badge tone={summary?.recommendedDecision === "GO" ? "green" : summary?.recommendedDecision === "CONDITIONAL GO" ? "yellow" : "red"}>{summary?.recommendedDecision ?? "NO DATA"}</Badge></span></div>
                <div className="test-summary-env">{envs.length ? envs.map((e) => <span key={e.testEnvironmentId}>{e.environmentName}</span>) : <span>ไม่ระบุ Environment</span>}</div>
              </div>
              <div className="test-summary-exec">
                <div><small>Pass Rate</small><b>{summary?.passRate ?? 0}%</b><span>{summary?.passedCases}/{summary?.executedCases} Passed</span></div>
                <div><small>Requirement Coverage</small><b>{summary?.requirementCoverage ?? 0}%</b><span>{summary?.coveredRequirements}/{summary?.totalRequirements} Covered</span></div>
                <div><small>Execution Progress</small><b>{summary?.executionProgress ?? 0}%</b><span>{summary?.executedCases}/{summary?.totalCases} Executed</span></div>
                <div><small>Defect Quality</small><b>{summary?.defectQuality ?? 0}</b><span>{summary?.openDefects} Open</span></div>
              </div>
              <div className="ts-progress">
                <div className="ts-progress-row"><span>Execution</span><b>{summary?.executedCases ?? 0} / {summary?.totalCases ?? 0}</b></div>
                <div className="ts-bar"><i style={{ width: `${summary?.executionProgress ?? 0}%` }} /></div>
                <div className="ts-progress-row"><span>Pass Rate</span><b>{summary?.passRate ?? 0}%</b></div>
                <div className="ts-bar"><i className="green" style={{ width: `${summary?.passRate ?? 0}%` }} /></div>
                <div className="ts-legend">{(summary?.statusDistribution ?? []).map((x) => <span key={x.status}><i style={{ background: x.color }} />{x.status} · {x.count}</span>)}</div>
              </div>
            </div>
          </section>
          <section className="test-summary-executive card">
            <div className="ts-section-heading"><div><span className="ts-eyebrow">Executive view</span><h2>ภาพรวมสำหรับการตัดสินใจ</h2><p>สรุปสถานะคุณภาพของ {release.releaseCode} · Version {release.version} จากข้อมูลการทดสอบล่าสุด</p></div><div className="ts-generated"><span>ข้อมูล ณ</span><b>{summary?.generatedAt ? new Date(summary.generatedAt).toLocaleString("th-TH", { dateStyle: "medium", timeStyle: "short" }) : "-"}</b></div></div>
            <div className="ts-executive-grid">
              <div className="ts-decision-note"><span className="ts-eyebrow">Recommendation</span><strong>{summary?.recommendedDecision ?? "ยังไม่มีข้อมูล"}</strong><p>{narrative.qaRecommendation || "กด Generate / Regenerate เพื่อสร้างคำแนะนำจากข้อมูลล่าสุด"}</p></div>
              <div className="ts-mini-list"><span className="ts-eyebrow">Release context</span><div><span>สถานะ Release</span><b>{release.status || "-"}</b></div><div><span>Environment</span><b>{envs.length ? envs.map((e) => e.environmentName).join(", ") : "ไม่ระบุ"}</b></div><div><span>Scope</span><b>{release.scope ? `${release.scope.length > 120 ? `${release.scope.slice(0, 120)}…` : release.scope}` : "ไม่ระบุ"}</b></div></div>
            </div>
            <div className={`ts-management-message is-${summary.recommendedDecision === "GO" ? "go" : summary.recommendedDecision === "CONDITIONAL GO" ? "conditional" : "no-go"}`} aria-live="polite">
              <div className="ts-management-head"><div><span className="ts-eyebrow">ข้อความสรุปสำหรับผู้บริหาร</span><h3>{summary.recommendedDecision === "GO" ? "ผลการทดสอบผ่านเกณฑ์ความพร้อม" : summary.recommendedDecision === "CONDITIONAL GO" ? "พร้อมดำเนินการแบบมีเงื่อนไข" : "ผลการทดสอบยังไม่พร้อมอนุมัติ Release"}</h3></div><Badge tone={summary.recommendedDecision === "GO" ? "green" : summary.recommendedDecision === "CONDITIONAL GO" ? "yellow" : "red"}>{summary.recommendedDecision}</Badge></div>
              <div className="ts-management-facts">
                <article><span className="material-symbols-outlined" aria-hidden="true">fact_check</span><p><small>ความคืบหน้าการทดสอบ</small><b>{summary.executedCases.toLocaleString()} / {summary.totalCases.toLocaleString()}</b><span>ดำเนินการแล้ว {summary.executionProgress}% · เหลือ {remainingCases.toLocaleString()} รายการ</span></p></article>
                <article><span className="material-symbols-outlined" aria-hidden="true">task_alt</span><p><small>ผลการทดสอบ</small><b>{summary.passedCases.toLocaleString()} Passed</b><span>Pass Rate {summary.passRate}%{passRateGap > 0 ? ` · ต่ำกว่าเกณฑ์ ${passRateGap} จุด` : " · ผ่านเกณฑ์"}</span></p></article>
                <article><span className="material-symbols-outlined" aria-hidden="true">account_tree</span><p><small>Requirement Coverage</small><b>{summary.coveredRequirements.toLocaleString()} / {summary.totalRequirements.toLocaleString()}</b><span>Coverage {summary.requirementCoverage}% · เหลือ {uncoveredRequirements.toLocaleString()} Requirement</span></p></article>
                <article><span className="material-symbols-outlined" aria-hidden="true">warning</span><p><small>ความเสี่ยงคงค้าง</small><b>{summary.openP0.toLocaleString()} P0 · {summary.openP1.toLocaleString()} P1</b><span>Open Defect {summary.openDefects.toLocaleString()} · Critical {summary.criticalDefects.toLocaleString()}</span></p></article>
              </div>
              <div className="ts-management-conclusion"><span className="material-symbols-outlined" aria-hidden="true">campaign</span><p><b>ข้อสรุป</b><span>{summary.recommendedDecision === "GO" ? "พร้อมเสนออนุมัติ Release และดำเนินการ Sign-off" : summary.recommendedDecision === "CONDITIONAL GO" ? "เสนออนุมัติได้เมื่อระบุ Owner เงื่อนไข และกำหนดปิดความเสี่ยงครบถ้วน" : `ยังไม่ควรอนุมัติ Release — เร่งดำเนินการ ${remainingCases.toLocaleString()} Test Cases ที่เหลือ และจัดการ P0 ${summary.openP0.toLocaleString()} รายการก่อน Sign-off`}</span></p></div>
            </div>
            <section className={`ts-release-impact is-${releaseImpactLevel}`} aria-labelledby="ts-release-impact-title">
              <div className="ts-section-heading compact"><div><span className="ts-eyebrow">Release impact</span><h3 id="ts-release-impact-title">ผลกระทบต่อการส่งมอบ</h3></div><span className="ts-impact-level"><span className="material-symbols-outlined" aria-hidden="true">{releaseImpactLevel === "high" ? "error" : releaseImpactLevel === "medium" ? "warning" : releaseImpactLevel === "low" ? "check_circle" : "help"}</span>ผลกระทบระดับ{releaseImpactLevelLabel}</span></div>
              <div className="ts-impact-grid">
                <article className="ts-impact-summary"><span className="material-symbols-outlined" aria-hidden="true">campaign</span><div><b>{releaseImpactLevel === "high" ? "มีความเสี่ยงต่อการอนุมัติ Release" : releaseImpactLevel === "medium" ? "อาจกระทบกำหนดการหรือขอบเขต Release" : releaseImpactLevel === "low" ? "ยังไม่พบผลกระทบสำคัญจากข้อมูลล่าสุด" : "ยังไม่มีข้อมูลสำหรับประเมินผลกระทบ"}</b><p>{releaseImpactDelivery}</p></div></article>
                <div className="ts-impact-facts"><div><small>กำหนด Release</small><b>{releaseImpactDate}</b></div><div><small>โมดูลที่มีสัญญาณกระทบ</small><b>{releaseImpactModules.length.toLocaleString()} / {summaryModules.length.toLocaleString()}</b></div><div><small>Build อ้างอิง</small><b>{releaseImpactBuild?.buildNumber || "ยังไม่ได้ระบุ"}</b></div></div>
              </div>
              <div className="ts-impact-columns">
                <div><h4>โมดูลที่ควรติดตาม</h4>{releaseImpactModules.length ? <ul>{releaseImpactModules.map((module) => <li key={module.moduleId}><span><b>{module.moduleCode || "-"}</b> {module.moduleName}</span><small>{(module.openDefects ?? 0).toLocaleString()} Open Defect · {module.executionPercent}% Executed</small></li>)}</ul> : <p className="ts-empty-detail">ไม่พบโมดูลที่มีสัญญาณผลกระทบจากข้อมูลการทดสอบ</p>}</div>
                <div><h4>ข้อมูลที่ผู้บริหารควรพิจารณา</h4><dl className="ts-impact-details"><div><dt>Change Notes</dt><dd>{releaseImpactBuild?.changeNotes?.trim() || "ยังไม่ได้ระบุ"}</dd></div><div><dt>Known Issues</dt><dd>{releaseImpactBuild?.knownIssues?.trim() || (summary.openDefects ? `มี Open Defect ${summary.openDefects.toLocaleString()} รายการ` : "ไม่พบ Known Issue จากข้อมูลล่าสุด")}</dd></div><div><dt>ข้อเสนอถัดไป</dt><dd>{releaseImpactAction}</dd></div></dl></div>
              </div>
            </section>
            <div className="ts-readiness-heading"><span className="ts-eyebrow">Release readiness gaps</span><small>ตัวเลขคำนวณจาก Test Summary ล่าสุด · เกณฑ์ Coverage/Pass Rate ≥ 90%</small></div>
            <div className="ts-readiness-grid">
              <button type="button" className={`${remainingCases === 0 ? "is-ok" : "is-warning"}${selectedGateLabel === "Execution Progress" ? " is-selected" : ""}`} aria-pressed={selectedGateLabel === "Execution Progress"} onClick={() => openGateDetail("Execution Progress")}><small>Execution Progress</small><b>{summary?.executionProgress ?? 0}%</b><span>{remainingCases.toLocaleString()} Test Cases ยังไม่รัน</span><em>เป้าหมาย 100% · กดดูรายละเอียด</em></button>
              <button type="button" className={`${passRateGap === 0 ? "is-ok" : "is-blocked"}${selectedGateLabel === "Pass Rate" ? " is-selected" : ""}`} aria-pressed={selectedGateLabel === "Pass Rate"} onClick={() => openGateDetail("Pass Rate")}><small>Pass Rate</small><b>{summary?.passRate ?? 0}%</b><span>{passRateGap > 0 ? `ต่ำกว่าเกณฑ์ ${passRateGap}%` : "ผ่านเกณฑ์แล้ว"}</span><em>เกณฑ์ ≥ 90% · กดดูรายละเอียด</em></button>
              <button type="button" className={`${(summary?.requirementCoverage ?? 0) >= 90 ? "is-ok" : "is-blocked"}${selectedGateLabel === "Requirement Coverage" ? " is-selected" : ""}`} aria-pressed={selectedGateLabel === "Requirement Coverage"} onClick={() => openGateDetail("Requirement Coverage")}><small>Requirement Coverage</small><b>{summary?.requirementCoverage ?? 0}%</b><span>{uncoveredRequirements > 0 ? `เหลือ ${uncoveredRequirements.toLocaleString()} Requirement` : "ครอบคลุมครบแล้ว"}</span><em>{summary?.coveredRequirements ?? 0}/{summary?.totalRequirements ?? 0} Covered · กดดูรายละเอียด</em></button>
              <button type="button" className={`${(summary?.openP0 ?? 0) === 0 ? "is-ok" : "is-blocked"}${selectedGateLabel === "Priority P0" ? " is-selected" : ""}`} aria-pressed={selectedGateLabel === "Priority P0"} onClick={() => openGateDetail("Priority P0")}><small>Blocking Priority</small><b>{summary?.openP0 ?? 0} P0</b><span>P1 ค้าง {(summary?.openP1 ?? 0).toLocaleString()} รายการ</span><em>เป้าหมาย P0 = 0 · กดดูรายละเอียด</em></button>
            </div>
          </section>
          {summary && <section className="ts-executive-action-grid">
            <div className="card ts-quality-gates">
              <div className="ts-section-heading compact"><div><span className="ts-eyebrow">Quality gates</span><h3>เกณฑ์ความพร้อม Release</h3></div><div className="ts-gate-score"><b>{passedGateCount}/{qualityGates.length}</b><span>ผ่านเกณฑ์</span></div></div>
              <div className="ts-gate-summary"><span className="is-pass">ผ่าน {passedGateCount}</span><span className="is-fail">ไม่ผ่าน {failedGateCount}</span><span className="is-unknown">ไม่มีข้อมูล {qualityGates.length - passedGateCount - failedGateCount}</span></div>
              <div className="ts-gate-list">{qualityGates.map((gate) => <button type="button" key={gate.label} className={`is-${gate.status}${selectedGate?.label === gate.label ? " is-selected" : ""}`} aria-pressed={selectedGate?.label === gate.label} onClick={() => setSelectedGateLabel(gate.label)}><span className="material-symbols-outlined" aria-hidden="true">{gate.status === "pass" ? "check_circle" : gate.status === "fail" ? "cancel" : "help"}</span><p><b>{gate.label}</b><small>{gate.target}</small></p><strong>{gate.value}</strong></button>)}</div>
              {selectedGate && <div className={`ts-gate-detail is-${selectedGate.status}`} aria-live="polite"><div><span className="material-symbols-outlined" aria-hidden="true">{selectedGate.status === "pass" ? "task_alt" : selectedGate.status === "fail" ? "priority_high" : "info"}</span><p><b>{selectedGate.label}</b><small>{selectedGate.detail}</small></p></div><p><b>Next action</b><span>{selectedGate.action}</span></p></div>}
            </div>
            <div className="card ts-forecast-card">
              <div className="ts-section-heading compact"><div><span className="ts-eyebrow">Presentation forecast</span><h3>แผนให้ทันวันนำเสนอ</h3></div></div>
              <label className="ts-forecast-date">วันที่นำเสนอ<input type="date" value={presentationDate} onChange={(e) => { setPresentationDate(e.target.value); if (releaseId) localStorage.setItem(`qa.testSummaryPresentationDate.${releaseId}`, e.target.value); }} /></label>
              <div className="ts-forecast-number"><b>{remainingCases === 0 ? "พร้อม" : daysToPresentation > 0 ? requiredCasesPerDay.toLocaleString() : "เกินกำหนด"}</b><span>{remainingCases === 0 ? "Test Case ครบแล้ว" : daysToPresentation > 0 ? "Test Cases ที่ต้องรันเฉลี่ยต่อวัน" : `${remainingCases.toLocaleString()} Test Cases ยังไม่รัน`}</span></div>
              <div className="ts-forecast-meta"><div><small>เวลาที่เหลือ</small><b>{daysToPresentation.toLocaleString()} วันปฏิทิน</b></div><div><small>งานที่เหลือ</small><b>{remainingCases.toLocaleString()} Test Cases</b></div></div>
              <p className="ts-forecast-note">{remainingCases === 0 ? "Execution ครบตามเป้าหมายแล้ว ให้ติดตาม Pass Rate และ Blocker ต่อ" : daysToPresentation > 0 ? `ทีมต้องทำได้อย่างน้อย ${requiredCasesPerDay.toLocaleString()} รายการต่อวันต่อเนื่องจึงจะรันครบก่อนวันนำเสนอ โดยยังไม่รวมเวลาแก้ไขและ Retest` : "วันนำเสนอถึงกำหนดแล้ว กรุณาปรับวันที่หรือจัดทำ Recovery Plan ทันที"}</p>
            </div>
          </section>}
          {summary && <section className="card ts-top-blockers">
            <div className="ts-section-heading compact"><div><span className="ts-eyebrow">Top blockers</span><h3>Defect สำคัญที่ผู้บริหารควรติดตาม</h3></div><small>เรียงตาม Severity และอายุรายการ · แสดงสูงสุด 5 รายการ</small></div>
            {topDefects.length ? <div className="ts-blocker-list">{topDefects.map((defect) => <article key={defect.defectId} className={expandedDefectId === defect.defectId ? "is-expanded" : ""}>
              <button type="button" className="ts-blocker-toggle" aria-expanded={expandedDefectId === defect.defectId} onClick={() => setExpandedDefectId((id) => id === defect.defectId ? "" : defect.defectId)}><div className="ts-blocker-main"><Badge tone={defect.severity === "Critical" ? "red" : defect.severity === "High" ? "yellow" : "blue"}>{defect.severity}</Badge><p><b>{defect.defectCode} · {defect.title}</b><small>{defect.status} · เปิดมา {defectAgeDays(defect.createdAt).toLocaleString()} วัน</small></p></div><span className="material-symbols-outlined" aria-hidden="true">{expandedDefectId === defect.defectId ? "expand_less" : "expand_more"}</span></button>
              <dl><div><dt>Owner</dt><dd>{defect.assigneeName || "ยังไม่ระบุ"}</dd></div><div><dt>ETA</dt><dd>ยังไม่ระบุ</dd></div></dl>
              {expandedDefectId === defect.defectId && <div className="ts-blocker-detail"><div><b>รายละเอียด</b><p>{defect.description || "ยังไม่ได้ระบุรายละเอียด"}</p></div><div><b>Expected / Actual</b><p><span>Expected: {defect.expectedResult || "ยังไม่ระบุ"}</span><span>Actual: {defect.actualResult || "ยังไม่ระบุ"}</span></p></div></div>}
            </article>)}</div> : <div className="ts-blocker-empty"><span className="material-symbols-outlined" aria-hidden="true">info</span><p><b>{summary.openDefects ? "มี Defect ค้าง แต่ยังโหลดรายละเอียดไม่ได้" : "ไม่พบ Open Defect"}</b><small>{summary.openDefects ? `Summary ระบุ ${summary.openDefects.toLocaleString()} รายการ กรุณาตรวจสอบรายละเอียดที่หน้า Defect` : "ไม่มีรายการที่ต้องแสดงใน Top Blockers"}</small></p></div>}
          </section>}
          {summary && <section className="ts-executive-insight-grid">
            <div className="card ts-trend-card">
              <div className="ts-section-heading compact"><div><span className="ts-eyebrow">Trend</span><h3>เปลี่ยนแปลงจาก Snapshot ก่อนหน้า</h3></div><small>{previousSnapshot ? `เทียบกับ ${formatThaiDateTime(previousSnapshot.date)}` : "เริ่มเก็บ Snapshot แล้ว"}</small></div>
              <div className="ts-trend-grid">{trendMetrics.map((metric) => { const delta = metric.previous === undefined ? null : Number((metric.current - metric.previous).toFixed(1)); const improved = delta === null || delta === 0 ? null : metric.lowerIsBetter ? delta < 0 : delta > 0; return <div key={metric.label}><small>{metric.label}</small><b>{metric.current.toLocaleString()}{metric.unit}</b><span className={improved === null ? "is-neutral" : improved ? "is-better" : "is-worse"}>{delta === null ? "รอ Snapshot วันถัดไป" : delta === 0 ? "ไม่เปลี่ยนแปลง" : `${delta > 0 ? "↑" : "↓"} ${Math.abs(delta).toLocaleString()}${metric.unit}`}</span></div>; })}</div>
            </div>
            <div className="card ts-confidence-card">
              <div className="ts-section-heading compact"><div><span className="ts-eyebrow">Data confidence</span><h3>ความครบถ้วนของรายงาน</h3></div><Badge tone={confidenceLabel === "High" ? "green" : confidenceLabel === "Medium" ? "yellow" : "red"}>{confidenceLabel}</Badge></div>
              <div className="ts-confidence-score"><b>{evidenceCompleteness}%</b><span>{readyEvidenceCount}/{reportEvidence.length} หมวดมีข้อมูล</span></div>
              <div className="ts-confidence-bar"><i style={{ width: `${evidenceCompleteness}%` }} /></div>
              <div className="ts-evidence-checks">{reportEvidence.map((item) => <button type="button" key={item.label} className={`${item.ready ? "is-ready" : "is-missing"}${focusedEvidence === item.label ? " is-selected" : ""}`} aria-pressed={focusedEvidence === item.label} onClick={() => openEvidenceDetail(item.label)}><span className="material-symbols-outlined" aria-hidden="true">{item.ready ? "check_circle" : "error"}</span>{item.label}</button>)}</div>
            </div>
          </section>}
          <section id="ts-release-report" className="card ts-report-sections">
            <div className="ts-section-heading compact"><div><span className="ts-eyebrow">Release report</span><h3>ขอบเขตและหลักฐานประกอบ</h3></div><small>ข้อมูลที่ยังไม่มีแหล่งอ้างอิงจะแสดงเป็น “ยังไม่ได้ระบุ”</small></div>
            <div className="ts-report-section-grid">
              <div className={focusedEvidence === "Scope" ? "is-focused" : ""}><b>Scope</b><p>{release.scope || "ยังไม่ได้ระบุ"}</p></div>
              <div className={focusedEvidence === "Out-of-Scope" ? "is-focused" : ""}><b>Out-of-Scope</b><p>ยังไม่ได้ระบุ</p></div>
              <div className={focusedEvidence === "Environment" ? "is-focused" : ""}><b>Environment</b><p>{envs.length ? envs.map((e) => `${e.environmentName}${e.baseUrl ? ` · ${e.baseUrl}` : ""}`).join("\n") : "ยังไม่ได้ระบุ"}</p></div>
              <div className={focusedEvidence === "Regression" ? "is-focused" : ""}><b>Regression</b><p>ยังไม่ได้ระบุ</p></div>
              <div className={focusedEvidence === "Installation / Update" ? "is-focused" : ""}><b>Installation / Update</b><p>ยังไม่ได้ระบุ</p></div>
              <div className={focusedEvidence === "Performance" ? "is-focused" : ""}><b>Performance</b><p>ยังไม่ได้ระบุ</p></div>
            </div>
          </section>
          {summary && <section className="card ts-evidence-breakdown">
            <div className="ts-section-heading compact"><div><span className="ts-eyebrow">Evidence breakdown</span><h3>สรุปหลักฐานคุณภาพ</h3></div><small>คำนวณจากข้อมูล Summary ล่าสุด · รวม 0 จะแสดงเป็น —</small></div>
            <div className="ts-evidence-grid">
              <div><b>Requirement</b><span>{summary.totalRequirements.toLocaleString()} รวม</span><strong>{summary.coveredRequirements.toLocaleString()} Covered</strong><small>{Math.max(0, summary.totalRequirements - summary.coveredRequirements).toLocaleString()} Not Covered</small></div>
              <div><b>Defect</b><span>{summary.totalDefects.toLocaleString()} รวม</span><strong>{summary.openDefects.toLocaleString()} Open</strong><small>{Math.max(0, summary.totalDefects - summary.openDefects).toLocaleString()} Resolved / Closed</small></div>
              <div><b>Test Case</b><span>{summary.totalCases.toLocaleString()} รวม</span><strong>{summary.passedCases.toLocaleString()} Passed</strong><small>{Math.max(0, summary.totalCases - summary.passedCases).toLocaleString()} อื่น ๆ</small></div>
            </div>
          </section>}
          <section className="card ts-decision-panel"><div className="ts-section-heading compact"><div><span className="ts-eyebrow">Release decision</span><h3>แผงตัดสินใจ Release</h3></div><Badge tone={summary.recommendedDecision === "GO" ? "green" : summary.recommendedDecision === "NO-GO" ? "red" : "yellow"}>{summary.recommendedDecision}</Badge></div><div className="ts-decision-grid"><div><b>Hard Blockers</b><p>{summary.criticalDefects || summary.openP0 ? `Critical ${summary.criticalDefects} · P0 ${summary.openP0}` : "ไม่พบ Hard Blocker"}</p></div><div><b>Warnings</b><p>{summary.openP1 || summary.highDefects || summary.requirementCoverage < 90 || summary.passRate < 90 ? `P1 ${summary.openP1} · High ${summary.highDefects} · Coverage ${summary.requirementCoverage}% · Pass Rate ${summary.passRate}%` : "ไม่พบ Warning ที่เกินเกณฑ์"}</p></div><div><b>Approved Risks</b><p>ตรวจสอบเพิ่มเติมที่หน้า Risk Acceptance</p></div><div><b>Next Action</b><p>{summary.recommendedDecision === "GO" ? "ส่งต่อให้ผู้มีอำนาจทำ Sign-off" : "แก้ไข/ประเมินความเสี่ยงและทดสอบซ้ำก่อน Sign-off"}</p></div></div><div className="modal-actions ts-decision-actions">{onOpenRisks && <button className="btn" onClick={onOpenRisks}>เปิด Risk Acceptance <span className="material-symbols-outlined" aria-hidden="true">arrow_forward</span></button>}{onOpenSignoff && <button className="btn primary" onClick={onOpenSignoff}>ไปหน้า Sign-off <span className="material-symbols-outlined" aria-hidden="true">arrow_forward</span></button>}</div></section>
          <div className="test-summary-grid ts-detail-grid">
            <section className="card"><div className="test-summary-card"><div className="ts-section-heading compact"><div><span className="ts-eyebrow">Test execution</span><h3>สถานะ Test Case</h3></div><b className="ts-total-count">{summary?.totalCases ?? 0} Cases</b></div><div className="ts-status-list">{(summary?.statusDistribution ?? []).map((x) => <div key={x.status}><div><span className="ts-status-dot" style={{ background: x.color }} /><span>{x.status}</span><b>{x.count}</b></div><div className="ts-bar"><i style={{ background: x.color, width: `${summary?.totalCases ? Math.min(100, x.count / summary.totalCases * 100) : 0}%` }} /></div></div>)}{!(summary?.statusDistribution?.length) && <p className="ts-empty-detail">ยังไม่มีข้อมูลสถานะ Test Case</p>}</div></div></section>
            <section className="card"><div className="test-summary-card"><div className="ts-section-heading compact"><div><span className="ts-eyebrow">Risk signals</span><h3>ประเด็นที่ต้องติดตาม</h3></div></div><div className="ts-risk-list"><div className={summary?.openP0 ? "risk-high" : "risk-ok"}><b>{summary?.openP0 ?? 0}</b><span>Open P0</span><small>{summary?.openP0 ? "ต้องแก้ก่อนปล่อย" : "ไม่พบรายการ"}</small></div><div className={summary?.openP1 ? "risk-medium" : "risk-ok"}><b>{summary?.openP1 ?? 0}</b><span>Open P1</span><small>{summary?.openP1 ? "ควรประเมินก่อนปล่อย" : "ไม่พบรายการ"}</small></div><div className={summary?.criticalDefects ? "risk-high" : "risk-ok"}><b>{summary?.criticalDefects ?? 0}</b><span>Critical Defects</span><small>{summary?.criticalDefects ? "มีความเสี่ยงสูง" : "ไม่พบรายการ"}</small></div></div><div className="ts-env-detail"><span className="ts-eyebrow">Test environments</span>{envs.length ? envs.map((e) => <div key={e.testEnvironmentId}><b>{e.environmentName}</b><small>{e.isActive ? "Active" : "Inactive"}{e.baseUrl ? ` · ${e.baseUrl}` : ""}</small></div>) : <p className="ts-empty-detail">ยังไม่ได้ระบุ Environment</p>}</div></div></section>
          </div>
          <section className="card ts-unrun-modules" aria-labelledby="ts-unrun-modules-title">
            <div className="ts-section-heading compact">
              <div><span className="ts-eyebrow">Module readiness</span><h3 id="ts-unrun-modules-title">โมดูลที่ยังไม่ได้ทดสอบ</h3></div>
              <small>{untestedModules.length ? `ยังไม่เริ่ม ${untestedModules.length} โมดูล` : "ทุกโมดูลมีผลทดสอบแล้วอย่างน้อย 1 ครั้ง"}</small>
            </div>
            {summaryModules.length ? <div className="ts-unrun-module-body">
              <div className={`ts-unrun-highlight${untestedModules.length ? " has-gap" : " is-ready"}`}>
                <b>{untestedModules.length.toLocaleString()}</b>
                <span>โมดูลยังไม่เริ่มทดสอบ</span>
                <small>{summaryModules.length.toLocaleString()} โมดูลที่มี Test Case ในขอบเขตนี้</small>
              </div>
              <div className="ts-unrun-module-lists">
                <div>
                  <h4>ยังไม่เริ่มทดสอบ</h4>
                  {untestedModules.length ? <ul>{untestedModules.map((module) => <li key={module.moduleId}><span><b>{module.moduleCode}</b> {module.moduleName}</span><small>{module.testCases.toLocaleString()} Test Cases</small></li>)}</ul> : <p className="ts-empty-detail">ไม่มีโมดูลที่ยังไม่เริ่มทดสอบ</p>}
                </div>
                <div>
                  <h4>ทดสอบแล้วแต่ยังไม่ครบ</h4>
                  {incompleteModules.length ? <ul>{incompleteModules.map((module) => <li key={module.moduleId}><span><b>{module.moduleCode}</b> {module.moduleName}</span><small>{module.executed.toLocaleString()} / {module.testCases.toLocaleString()} Cases · {module.executionPercent}%</small></li>)}</ul> : <p className="ts-empty-detail">ไม่มีโมดูลที่ทดสอบค้างอยู่</p>}
                </div>
              </div>
            </div> : <div className="ts-blocker-empty"><span className="material-symbols-outlined" aria-hidden="true">inventory_2</span><p><b>ยังไม่มีข้อมูลโมดูล</b><small>ไม่พบ Module ที่มี Test Case ในขอบเขต Release นี้</small></p></div>}
          </section>
          <div className="test-summary-grid">
            <section className="card"><div className="test-summary-card"><h3 style={{ margin: 0 }}>Metrics</h3><dl className="ts-kv">
              <div><dt>Requirement Coverage</dt><dd>{summary?.requirementCoverage ?? 0}%</dd></div>
              <div><dt>Total / Executed Cases</dt><dd>{summary?.totalCases ?? 0} / {summary?.executedCases ?? 0}</dd></div>
              <div><dt>Passed</dt><dd>{summary?.passedCases ?? 0}</dd></div>
              <div><dt>Open P0 / P1</dt><dd>{summary?.openP0 ?? 0} / {summary?.openP1 ?? 0}</dd></div>
              <div><dt>Open Defects</dt><dd>{summary?.openDefects ?? 0}</dd></div>
              <div><dt>Critical / High Defects</dt><dd>{summary?.criticalDefects ?? 0} / {summary?.highDefects ?? 0}</dd></div>
              <div><dt>Overall Score</dt><dd>{summary?.overallScore ?? "-"}</dd></div>
            </dl></div></section>
            <section className="card"><div className="test-summary-card"><h3 style={{ margin: 0 }}>Defect Severity</h3><div className="ts-legend" style={{ marginBottom: 8 }}>{(summary?.defectSeverityDistribution ?? []).map((x) => <span key={x.severity}><i style={{ background: x.color }} />{x.severity} · {x.count}</span>)}</div><dl className="ts-kv">
              <div><dt>Total Defects</dt><dd>{summary?.totalDefects ?? 0}</dd></div>
              <div><dt>Open</dt><dd>{summary?.openDefects ?? 0}</dd></div>
              <div><dt>Critical / High</dt><dd>{summary?.criticalDefects ?? 0} / {summary?.highDefects ?? 0}</dd></div>
              <div><dt>Defect Quality</dt><dd>{summary?.defectQuality ?? 0} / 100</dd></div>
            </dl></div></section>
          </div>
          <section className="card ts-narrative-editor"><div className="test-summary-narrative">
            <label>รายละเอียด / ขอบเขต (Scope)</label><textarea value={release?.scope ?? ""} readOnly style={{ background: "#f8fafc" }} aria-label="Release Scope" />
            <label>Known Issues / ปัญหาที่ทราบ<textarea value={narrative.knownIssues} onChange={(e) => setNarrative((n) => ({ ...n, knownIssues: e.target.value }))} /></label>
            <label>Remaining Risks / ความเสี่ยงคงเหลือ<textarea value={narrative.remainingRisks} onChange={(e) => setNarrative((n) => ({ ...n, remainingRisks: e.target.value }))} /></label>
            <label>QA Recommendation / คำแนะนำ<textarea value={narrative.qaRecommendation} onChange={(e) => setNarrative((n) => ({ ...n, qaRecommendation: e.target.value }))} /></label>
            <span className="test-summary-note">ข้อความ Known Issues / Risks / QA Recommendation ปรับได้และ Auto-generate จากข้อมูล ปุ่ม Generate จะรีเซ็ตกลับเป็นค่าแนะนำ · ส่งต่อ Sign-off ที่หน้า Release Sign-off</span>
          </div></section>
        </>
      )}
    </article>
  );
}
