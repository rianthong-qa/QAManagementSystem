import { useState, useRef, useEffect } from "react";
import type { ReleaseItem, ProjectItem, BuildItem } from "../shared/types";
import { apiUrl, getJson, isAbortError } from "../api";
import { notify, confirmDialog } from "../components/dialogStore";
import { formatThaiDateTime } from "../dateTime";
import { Badge } from "../components/Badge";
import { ModalShell } from "../components/ModalShell";
import { type SessionUser, masterOptionElements, nextBusinessCode, useMasterOptions } from "../shared/appShared";
import { DateField } from "../components/DateField";
import { BuildDefectsPanel } from "../components/BuildDefectsPanel";
import "./ReleasesPage.css";

// ความหมายสถานะ Release/Build (ReleaseStatuses/BuildStatuses ใน Release.cs) — รูปแบบเดียวกับ
// requirementStatusInformation/testCaseStatusInfo/cycleStatusInfo
const releaseStatusInfo = [
  { value: "Draft", label: "ฉบับร่าง", meaning: "อยู่ระหว่างวางแผน Release ยังไม่เริ่ม Test Cycle ของ Build ใดใน Release นี้", impact: "แก้ไข Version/Scope/Planned Date ได้อิสระ" },
  { value: "Testing", label: "กำลังทดสอบ", meaning: "อยู่ระหว่างทดสอบ (Smoke/Functional/Regression) ยังไม่ผ่านเกณฑ์ Release Gate", impact: "ยังไม่สามารถเข้าสู่ขั้นตอน Sign-off ได้จนกว่าจะผ่านเกณฑ์" },
  { value: "Ready", label: "พร้อมปล่อย", meaning: "ผ่านเกณฑ์ Release Gate แล้ว (P0=0, P1 Blocker=0, Regression/Coverage ผ่าน threshold)", impact: "พร้อมเข้าสู่ขั้นตอน Sign-off (QA Recommendation → Dev Ack → Product Approval → Final Decision)" },
  { value: "Released", label: "ปล่อยแล้ว", meaning: "ปล่อยใช้งานจริงแล้ว ระบบบันทึก Actual Release Date ให้อัตโนมัติ", impact: "ถือเป็นสถานะปิดท้ายของ Release นี้ ไม่ควรย้อนกลับไปสถานะก่อนหน้า" },
  { value: "Cancelled", label: "ยกเลิก", meaning: "ยกเลิก Release นี้ ไม่ปล่อยจริง", impact: "Build/Test Cycle ที่ผูกอยู่ยังอยู่ในระบบเพื่อการตรวจสอบย้อนหลัง แต่ไม่นำไปนับความคืบหน้า Release อีก" },
] as const;
const buildStatusInfo = [
  { value: "Ready", label: "พร้อมทดสอบ", meaning: "Build เข้าระบบแล้ว (QA ตรวจ Package/DB Migration/Module Version แล้ว) รอเริ่ม Smoke Cycle", impact: "ยังไม่มีผลทดสอบผูกกับ Build นี้" },
  { value: "Testing", label: "กำลังทดสอบ", meaning: "มี Test Cycle ที่ใช้ Build นี้กำลัง Execute อยู่", impact: "ยังสรุปผลไม่ได้จนกว่ารอบทดสอบที่เกี่ยวข้องจะเสร็จ" },
  { value: "Passed", label: "ผ่าน", meaning: "ทดสอบผ่านเกณฑ์ที่กำหนดสำหรับ Build นี้ (เช่น Smoke หรือ Full Test)", impact: "ใช้เป็น Candidate Build สำหรับ Test Summary/Release Gate ต่อได้" },
  { value: "Failed", label: "ไม่ผ่าน", meaning: "ทดสอบไม่ผ่าน (เช่น P0 Fail ตอน Smoke)", impact: "ตาม Workflow ต้อง Hold Build นี้ไว้และรอ Build ใหม่จาก Developer" },
  { value: "Blocked", label: "ติดปัญหา", meaning: "ทดสอบต่อไม่ได้เพราะติดปัญหาที่ควบคุมไม่ได้ (Environment ไม่พร้อม, Data ไม่ครบ ฯลฯ) ไม่ใช่บั๊กของ Build โดยตรง", impact: "ต้องแก้ปัญหาที่บล็อกอยู่ก่อน ถึงจะประเมินผลทดสอบของ Build นี้ต่อได้" },
] as const;
/** ลำดับขั้นของ Release ตามปกติ (Cancelled แยกออกไป) — ใช้แสดงแถบขั้นตอนในหน้ารายละเอียด */
const releaseFlow = ["Draft", "Testing", "Ready", "Released"] as const;
const releaseTone = (status?: string) => status === "Ready" ? "green" : status === "Released" ? "purple" : status === "Testing" ? "blue" : status === "Cancelled" ? "red" : "gray";
const releaseLabel = (status: string) => releaseStatusInfo.find((x) => x.value === status)?.label ?? status;
const buildLabel = (status: string) => buildStatusInfo.find((x) => x.value === status)?.label ?? status;
/** สีป้ายสถานะ Build ตาม UI_DESIGN_SYSTEM §6 — เดิมหน้ารายการเป็นสีเขียวเสมอ (Failed/Blocked ก็เขียว) และหน้า detail ใช้ Ready = เขียว นอกนั้นเหลือง */
const buildStatusTone = (status?: string) => status === "Ready" || status === "Passed" ? "green" : status === "Failed" ? "red" : status === "Blocked" ? "yellow" : status === "Testing" ? "blue" : "gray";
export function ReleasesPage({ search, contextProjectId, onOpenExecution }: { search: string; refresh?: number; contextProjectId?: string; onOpenExecution?: (cycleId: string) => void }) {
  const masterOptions = useMasterOptions(), releaseTypes = masterOptions("ReleaseType");
  let canEdit = false;
  try {
    const current: SessionUser = JSON.parse(
      localStorage.getItem("qa.user") ?? "{}",
    );
    canEdit =
      current.roles?.includes("SYS_ADMIN") ||
      current.permissions?.includes("PROJECT.EDIT");
  } catch {
    canEdit = false;
  }
  const [items, setItems] = useState<ReleaseItem[]>([]),
    [allItems, setAllItems] = useState<ReleaseItem[]>([]),
    [projects, setProjects] = useState<ProjectItem[]>([]),
    [builds, setBuilds] = useState<BuildItem[]>([]),
    [selectedId, setSelectedId] = useState(""),
    [loading, setLoading] = useState(true),
    [error, setError] = useState(""),
    [formError, setFormError] = useState(""),
    [reload, setReload] = useState(0),
    [modal, setModal] = useState<"release" | "build" | null>(null),
    [releaseDetail, setReleaseDetail] = useState<ReleaseItem | null>(null),
    [buildDetail, setBuildDetail] = useState<BuildItem | null>(null),
    [editRelease, setEditRelease] = useState<ReleaseItem | null>(null),
    [editBuild, setEditBuild] = useState<BuildItem | null>(null),
    [buildReleaseId, setBuildReleaseId] = useState(""),
    [projectId, setProjectId] = useState(""),
    [code, setCode] = useState(""),
    [name, setName] = useState(""),
    [type, setType] = useState(""),
    [releaseStatus, setReleaseStatus] = useState("Draft"),
    [buildStatus, setBuildStatus] = useState("Ready"),
    [date, setDate] = useState(""),
    [details, setDetails] = useState(""),
    [packageVersion, setPackageVersion] = useState(""),
    [commit, setCommit] = useState(""),
    [issues, setIssues] = useState(""),
    [saving, setSaving] = useState(false),
    [statusFilter, setStatusFilter] = useState("");
  const headers = {
    "Content-Type": "application/json",
    Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
  };
  const contextProjectIdRef = useRef(contextProjectId);
  contextProjectIdRef.current = contextProjectId;
  useEffect(() => {
    setLoading(true);
    const h = {
      Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
    };
    Promise.all([
      fetch(`${apiUrl}/releases`, { headers: h }).then((r) => r.json()),
      fetch(`${apiUrl}/projects`, { headers: h }).then((r) => r.json()),
    ])
      .then(([releaseData, projectData]) => {
        const allReleases = releaseData as ReleaseItem[];
        const active = allReleases.filter((x) => x.status !== "Cancelled" && x.status !== "Closed");
        setAllItems(allReleases);
        setItems(active);
        setProjects((projectData as ProjectItem[]).filter((x) => x.isActive));
        setSelectedId((current) => {
          if (active.some((x) => x.releaseId === current)) return current;
          // อ่าน Project context ผ่าน ref — ใช้เลือก Release เริ่มต้นเท่านั้น การเปลี่ยน context ภายหลังมี effect แยกจัดการ
          const ctxProjectId = contextProjectIdRef.current;
          const scoped = ctxProjectId ? active.filter((x) => x.projectId === ctxProjectId) : active;
          return scoped[0]?.releaseId ?? active[0]?.releaseId ?? "";
        });
      })
      .catch(() => setError("โหลดข้อมูล Release ไม่สำเร็จ"))
      .finally(() => setLoading(false));
  }, [reload]);
  useEffect(() => {
    if (!selectedId) {
      setBuilds([]);
      return;
    }
    const ctrl = new AbortController();
    getJson<BuildItem[]>(`${apiUrl}/releases/${selectedId}/builds`, ctrl.signal)
      .then((data) => setBuilds(data.filter((x) => x.isActive)))
      .catch((e) => { if (!isAbortError(e)) { setBuilds([]); notify(`โหลด Build ของ Release ไม่สำเร็จ (${e instanceof Error ? e.message : "ไม่ทราบสาเหตุ"})`, "error"); } });
    return () => ctrl.abort();
  }, [selectedId, reload]);
  const selected = items.find((x) => x.releaseId === selectedId),
    selectedIsOpen = !!selected && selected.status !== "Closed" && selected.status !== "Cancelled",
    term = search.toLowerCase(),
    scopedReleases = items.filter((x) =>
      (!contextProjectId || x.projectId === contextProjectId) &&
      `${x.releaseCode} ${x.version} ${x.releaseType ?? ""} ${x.status} ${releaseLabel(x.status)}`
        .toLowerCase()
        .includes(term),
    ),
    filteredReleases = statusFilter ? scopedReleases.filter((x) => x.status === statusFilter) : scopedReleases,
    contextProject = projects.find((x) => x.projectId === contextProjectId),
    selectedProject = projects.find((x) => x.projectId === selected?.projectId),
    filteredBuilds = builds.filter((x) =>
      `${x.buildNumber} ${x.applicationVersion ?? ""} ${x.commitReference ?? ""}`
        .toLowerCase()
        .includes(term),
    );
  useEffect(() => {
    if (contextProjectId && !filteredReleases.some((x) => x.releaseId === selectedId)) {
      setSelectedId(filteredReleases[0]?.releaseId ?? "");
    }
  }, [contextProjectId, filteredReleases, selectedId]);
  const openRelease = (item?: ReleaseItem) => {
    setFormError("");
    setEditRelease(item ?? null);
    setEditBuild(null);
    setProjectId(item?.projectId ?? projects[0]?.projectId ?? "");
    const targetProjectId = item?.projectId ?? projects[0]?.projectId ?? "";
    const project = projects.find((x) => x.projectId === targetProjectId);
    setCode(
      item?.releaseCode ??
        nextBusinessCode(
          `${project?.projectCode ?? "PRJ"}-REL`,
          allItems
            .filter((x) => x.projectId === targetProjectId)
            .map((x) => x.releaseCode),
        ),
    );
    setName(item?.version ?? "");
    setType(item?.releaseType ?? releaseTypes[0]?.value ?? "");
    setReleaseStatus(item?.status ?? "Draft");
    setDate(item?.plannedReleaseDate?.slice(0, 10) ?? "");
    setDetails(item?.scope ?? "");
    setModal("release");
  };
  const openBuild = (item?: BuildItem) => {
    setFormError("");
    setEditBuild(item ?? null);
    setEditRelease(null);
    setBuildReleaseId(item?.releaseId ?? selectedId);
    setCode(item?.buildNumber ?? "");
    setName(item?.applicationVersion ?? "");
    setBuildStatus(item?.status ?? "Ready");
    setPackageVersion(item?.packageVersion ?? "");
    setCommit(item?.commitReference ?? "");
    setDate(item?.buildDate?.slice(0, 10) ?? "");
    setDetails(item?.changeNotes ?? "");
    setIssues(item?.knownIssues ?? "");
    setModal("build");
  };
  const save = async () => {
    if (!modal) return;
    setFormError("");
    setSaving(true);
    try {
      let url = "",
        method = "POST",
        body: object;
      if (modal === "release") {
        url = editRelease
          ? `${apiUrl}/releases/${editRelease.releaseId}`
          : `${apiUrl}/projects/${projectId}/releases`;
        method = editRelease ? "PUT" : "POST";
        body = editRelease
          ? {
              version: name,
              releaseType: type,
              plannedReleaseDate: date || null,
              scope: details || null,
              releaseOwnerUserId: null,
            }
          : {
              releaseCode: "",
              version: name,
              releaseType: type,
              plannedReleaseDate: date || null,
              scope: details || null,
              releaseOwnerUserId: null,
            };
      } else {
        url = editBuild
          ? `${apiUrl}/builds/${editBuild.buildId}`
          : `${apiUrl}/releases/${selectedId}/builds`;
        method = editBuild ? "PUT" : "POST";
        body = editBuild
          ? {
              releaseId: buildReleaseId,
              applicationVersion: name || null,
              packageVersion: packageVersion || null,
              commitReference: commit || null,
              buildDate: date || null,
              changeNotes: details || null,
              knownIssues: issues || null,
            }
          : {
              buildNumber: code,
              applicationVersion: name || null,
              packageVersion: packageVersion || null,
              commitReference: commit || null,
              buildDate: date || null,
              changeNotes: details || null,
              knownIssues: issues || null,
            };
      }
      const response = await fetch(url, {
        method,
        headers,
        body: JSON.stringify(body),
      });
      if (!response.ok) {
        const p = await response.json().catch(() => null);
        throw new Error(p?.detail ?? "บันทึกไม่สำเร็จ");
      }
      if (modal === "build" && editBuild && buildReleaseId !== editBuild.releaseId) {
        setSelectedId(buildReleaseId);
      }
      if (
        modal === "release" &&
        editRelease &&
        releaseStatus !== editRelease.status
      ) {
        const statusResponse = await fetch(
          `${apiUrl}/releases/${editRelease.releaseId}/status`,
          {
            method: "POST",
            headers,
            body: JSON.stringify({ status: releaseStatus }),
          },
        );
        if (!statusResponse.ok) {
          const problem = await statusResponse.json();
          throw new Error(problem.detail ?? "เปลี่ยนสถานะ Release ไม่สำเร็จ");
        }
      }
      if (modal === "build" && editBuild && buildStatus !== editBuild.status) {
        const statusResponse = await fetch(
          `${apiUrl}/builds/${editBuild.buildId}/status`,
          {
            method: "POST",
            headers,
            body: JSON.stringify({ status: buildStatus }),
          },
        );
        if (!statusResponse.ok) {
          const problem = await statusResponse.json();
          throw new Error(problem.detail ?? "เปลี่ยนสถานะ Build ไม่สำเร็จ");
        }
      }
      setModal(null);
      setReload((x) => x + 1);
    } catch (e) {
      setFormError(e instanceof Error ? e.message : "บันทึกไม่สำเร็จ");
    } finally {
      setSaving(false);
    }
  };
  const remove = async (
    kind: "release" | "build",
    id: string,
    label: string,
  ) => {
    if (
      !await confirmDialog(
        `ยืนยันลบ ${label}? ข้อมูลจะถูกปิดใช้งานและไม่แสดงในรายการ`,
      )
    )
      return;
    const response = await fetch(
      `${apiUrl}/${kind === "release" ? "releases" : "builds"}/${id}`,
      { method: "DELETE", headers },
    );
    if (!response.ok) {
      notify("ไม่สามารถลบข้อมูลได้", "error");
      return;
    }
    setReload((x) => x + 1);
  };
  const markRc = async (item: BuildItem) => {
    if (!await confirmDialog({ title: "Mark Release Candidate", message: `ตั้ง Build ${item.buildNumber} เป็น Release Candidate ใช่หรือไม่?\nระบบจะเริ่ม Scheduled Regression / Automation ที่ผูกกับ RC ทันที`, confirmLabel: "Mark RC" })) return;
    const response = await fetch(`${apiUrl}/builds/${item.buildId}/mark-release-candidate`, {
      method: "POST",
      headers,
    });
    if (!response.ok) { const p = await response.json().catch(() => null); notify(p?.detail ?? `Mark RC ของ Build ${item.buildNumber} ไม่สำเร็จ`, "error"); return; }
    notify(`ตั้ง Build ${item.buildNumber} เป็น Release Candidate แล้ว`, "success");
    setReload((x) => x + 1);
  };
  const statusCounts = releaseStatusInfo.map((s) => ({ ...s, count: scopedReleases.filter((x) => x.status === s.value).length })).filter((s) => s.count > 0);
  const rcBuild = builds.find((x) => x.isReleaseCandidate);
  const buildCount = (status: string) => builds.filter((x) => x.status === status).length;
  const shortDate = (value?: string) => value ? formatThaiDateTime(value, { day: "numeric", month: "short", year: "numeric" }) : "";
  // เหตุผลที่ยังบันทึกไม่ได้ (ว่าง = พร้อมบันทึก) — เงื่อนไขเดียวกับปุ่มบันทึกเดิม
  const formHint = modal === "release"
    ? (!name.trim() ? "ยังขาด: Version" : !code.trim() ? "ยังไม่มีรหัส Release" : "")
    : modal === "build"
      ? (!code.trim() ? "ยังขาด: Build Number" : editBuild && !buildReleaseId ? "ยังขาด: Release" : "")
      : "";
  if (loading)
    return (
      <article className="card empty" role="status">
        <div className="spinner" />
        <p>กำลังโหลดข้อมูล Release...</p>
      </article>
    );
  if (error)
    return (
      <article className="card empty">
        <div className="login-error" role="alert">{error}</div>
      </article>
    );
  return (
    <div className="rb-layout">
      <aside className="card rb-releases" aria-label="รายการ Release">
        <div className="rb-releases-head">
          <div>
            <h3>Release</h3>
            <p>{filteredReleases.length.toLocaleString("th-TH")} รายการที่ใช้งาน{contextProject ? ` · ${contextProject.projectCode}` : ""}</p>
          </div>
          {canEdit && (
            <button className="btn primary" type="button" onClick={() => openRelease()}>
              <span className="material-symbols-outlined" aria-hidden="true">add</span>Release
            </button>
          )}
        </div>
        {statusCounts.length > 1 && (
          <div className="rb-status-chips" role="group" aria-label="กรองตามสถานะ Release">
            <button type="button" aria-pressed={statusFilter === ""} onClick={() => setStatusFilter("")}>ทั้งหมด <b>{scopedReleases.length}</b></button>
            {statusCounts.map((s) => (
              <button key={s.value} type="button" data-tone={releaseTone(s.value)} aria-pressed={statusFilter === s.value} onClick={() => setStatusFilter(statusFilter === s.value ? "" : s.value)}>{s.label} <b>{s.count}</b></button>
            ))}
          </div>
        )}
        {filteredReleases.length === 0 ? (
          <p className="rb-empty-small">{scopedReleases.length ? "ไม่พบ Release ตามตัวกรอง/คำค้น" : "ยังไม่มี Release ที่ใช้งาน"}</p>
        ) : (
          <ul className="rb-release-list">
            {filteredReleases.map((x) => (
              <li key={x.releaseId}>
                <button type="button" className={selectedId === x.releaseId ? "active" : ""} aria-pressed={selectedId === x.releaseId} onClick={() => setSelectedId(x.releaseId)} data-tone={releaseTone(x.status)}>
                  <span className="rb-release-top"><span className="rb-release-code">{x.releaseCode}</span><Badge tone={releaseTone(x.status)}>{releaseLabel(x.status)}</Badge></span>
                  <b>Version {x.version}</b>
                  <small>{[x.releaseType || "ไม่ระบุประเภท", x.plannedReleaseDate ? `กำหนด ${shortDate(x.plannedReleaseDate)}` : "ไม่ระบุวัน"].join(" · ")}</small>
                </button>
              </li>
            ))}
          </ul>
        )}
      </aside>

      <section className="rb-main" aria-label="รายละเอียด Release และ Build">
        {selected ? (
          <>
            <div className="card rb-release-head">
              <div className="rb-release-title">
                <span className="rb-eyebrow">{selected.releaseCode}{selectedProject ? ` · ${selectedProject.projectName}` : ""}</span>
                <h2>Version {selected.version}</h2>
                <div className="rb-badges">
                  <Badge tone={releaseTone(selected.status)}>{releaseLabel(selected.status)}</Badge>
                  {selected.releaseType && <Badge tone="blue">{selected.releaseType}</Badge>}
                </div>
                {selected.scope && <p className="rb-scope">{selected.scope}</p>}
              </div>
              <div className="rb-release-actions">
                <button className="btn" type="button" onClick={() => setReleaseDetail(selected)}><span className="material-symbols-outlined" aria-hidden="true">visibility</span>รายละเอียด</button>
                {canEdit && (
                  <>
                    <button className="btn" type="button" disabled={!selectedIsOpen} onClick={() => openRelease(selected)}><span className="material-symbols-outlined" aria-hidden="true">edit</span>แก้ไข</button>
                    <button className="btn rb-danger" type="button" disabled={!selectedIsOpen} onClick={() => remove("release", selected.releaseId, selected.releaseCode)} title="ยกเลิกและซ่อน Release นี้"><span className="material-symbols-outlined" aria-hidden="true">block</span>ยกเลิก Release</button>
                  </>
                )}
              </div>
              <dl className="rb-stats">
                <div><dt>กำหนดปล่อย</dt><dd className="rb-stat-text">{shortDate(selected.plannedReleaseDate) || "ไม่ระบุ"}</dd></div>
                <div><dt>Build ทั้งหมด</dt><dd>{builds.length.toLocaleString("th-TH")}</dd></div>
                <div><dt>ผ่าน / ไม่ผ่าน</dt><dd>{buildCount("Passed")} <span>/</span> {buildCount("Failed")}</dd></div>
                <div><dt>Release Candidate</dt><dd className="rb-stat-text">{rcBuild ? rcBuild.buildNumber : "ยังไม่กำหนด"}</dd></div>
              </dl>
            </div>

            <div className="card rb-builds">
              <div className="rb-builds-head">
                <div>
                  <h3><span className="material-symbols-outlined" aria-hidden="true">deployed_code</span>Build</h3>
                  <p>{term ? `${filteredBuilds.length.toLocaleString("th-TH")} จาก ${builds.length.toLocaleString("th-TH")} Build ตรงกับคำค้น` : "กดเลข Build เพื่อดูรายละเอียด · Change Notes และ Known Issues"}</p>
                </div>
                {canEdit && (
                  <button className="btn primary" type="button" disabled={!selectedIsOpen} onClick={() => openBuild()}>
                    <span className="material-symbols-outlined" aria-hidden="true">add</span>Build
                  </button>
                )}
              </div>
              {filteredBuilds.length === 0 ? (
                <div className="rb-empty">
                  <span className="material-symbols-outlined" aria-hidden="true">{term ? "search_off" : "deployed_code"}</span>
                  <b>{term ? "ไม่พบ Build ตามคำค้น" : "ยังไม่มี Build ใน Release นี้"}</b>
                  {!term && canEdit && selectedIsOpen && <button className="btn primary" type="button" onClick={() => openBuild()}><span className="material-symbols-outlined" aria-hidden="true">add</span>เพิ่ม Build แรก</button>}
                </div>
              ) : (
                <ul className="rb-build-list">
                  {filteredBuilds.map((x) => (
                    <li key={x.buildId} data-tone={buildStatusTone(x.status)} className={x.isReleaseCandidate ? "rc" : undefined}>
                      <div className="rb-build-main">
                        <span className="rb-build-name">
                          <button type="button" className="rb-build-link" onClick={() => setBuildDetail(x)}>{x.buildNumber}</button>
                          {x.isReleaseCandidate && <span className="rb-rc"><span className="material-symbols-outlined" aria-hidden="true">workspace_premium</span>Release Candidate</span>}
                        </span>
                        <small>
                          <span>App {x.applicationVersion || "-"}</span>
                          <span>Package {x.packageVersion || "-"}</span>
                          {x.commitReference && <code title="Commit Reference">{x.commitReference}</code>}
                        </small>
                      </div>
                      <span className="rb-build-date">{shortDate(x.buildDate) || "ไม่ระบุวันที่"}</span>
                      <Badge tone={buildStatusTone(x.status)}>{buildLabel(x.status)}</Badge>
                      {canEdit && (
                        <span className="rb-build-actions">
                          {!x.isReleaseCandidate && (
                            <button type="button" title="ตั้งเป็น Release Candidate" aria-label={`ตั้ง ${x.buildNumber} เป็น Release Candidate`} disabled={!selectedIsOpen} onClick={() => markRc(x)}>
                              <span className="material-symbols-outlined" aria-hidden="true">workspace_premium</span>
                            </button>
                          )}
                          <button type="button" title="แก้ไข" aria-label={`แก้ไข ${x.buildNumber}`} disabled={!selectedIsOpen} onClick={() => openBuild(x)}>
                            <span className="material-symbols-outlined" aria-hidden="true">edit</span>
                          </button>
                          <button type="button" className="danger" title="ลบ" aria-label={`ลบ ${x.buildNumber}`} disabled={!selectedIsOpen} onClick={() => remove("build", x.buildId, x.buildNumber)}>
                            <span className="material-symbols-outlined" aria-hidden="true">delete</span>
                          </button>
                        </span>
                      )}
                    </li>
                  ))}
                </ul>
              )}
            </div>
          </>
        ) : (
          <div className="card rb-empty">
            <span className="material-symbols-outlined" aria-hidden="true">rocket_launch</span>
            <b>{filteredReleases.length ? "เลือก Release เพื่อดู Build" : "ยังไม่มี Release"}</b>
            {canEdit && !filteredReleases.length && <button className="btn primary" type="button" onClick={() => openRelease()}><span className="material-symbols-outlined" aria-hidden="true">add</span>สร้าง Release แรก</button>}
          </div>
        )}
      </section>
      {releaseDetail && (() => {
        const r = releaseDetail;
        const project = projects.find((x) => x.projectId === r.projectId);
        const stepIndex = releaseFlow.indexOf(r.status as (typeof releaseFlow)[number]);
        const detailBuilds = r.releaseId === selectedId ? builds : null;
        const info = releaseStatusInfo.find((x) => x.value === r.status);
        return (
          <ModalShell labelledBy="rb-release-detail-title" className="rb-detail" onDismiss={() => setReleaseDetail(null)}>
            <div className="rb-detail-head">
              <div>
                <span className="rb-eyebrow">RELEASE · {r.releaseCode}{project ? ` · ${project.projectName}` : ""}</span>
                <h2 id="rb-release-detail-title">Version {r.version}</h2>
                <div className="rb-badges"><Badge tone={releaseTone(r.status)}>{releaseLabel(r.status)}</Badge>{r.releaseType && <Badge tone="blue">{r.releaseType}</Badge>}</div>
              </div>
              <div className="rb-detail-head-actions">
                {canEdit && r.status !== "Cancelled" && r.status !== "Closed" && <button className="btn primary" type="button" onClick={() => { setReleaseDetail(null); openRelease(r); }}><span className="material-symbols-outlined" aria-hidden="true">edit</span>แก้ไข</button>}
                <button className="rb-icon-close" type="button" aria-label="ปิดรายละเอียด Release" onClick={() => setReleaseDetail(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
              </div>
            </div>
            <div className="rb-detail-body">
              {r.status === "Cancelled" ? (
                <p className="rb-callout" data-tone="red"><span className="material-symbols-outlined" aria-hidden="true">block</span><span><b>Release นี้ถูกยกเลิก</b>{info?.impact}</span></p>
              ) : (
                <ol className="rb-flow" aria-label="ขั้นตอนสถานะ Release">
                  {releaseFlow.map((value, i) => {
                    const step = releaseStatusInfo.find((x) => x.value === value)!;
                    const state = i < stepIndex ? "done" : i === stepIndex ? "current" : "todo";
                    return (
                      <li key={value} className={state} aria-current={state === "current" ? "step" : undefined}>
                        <span className="rb-flow-dot" aria-hidden="true">{state === "done" ? <span className="material-symbols-outlined">check</span> : i + 1}</span>
                        <span><b>{step.label}</b><small>{value}</small></span>
                      </li>
                    );
                  })}
                </ol>
              )}
              {info && r.status !== "Cancelled" && <p className="rb-callout" data-tone={releaseTone(r.status)}><span className="material-symbols-outlined" aria-hidden="true">info</span><span><b>{info.meaning}</b>{info.impact}</span></p>}
              <dl className="rb-detail-meta">
                <div><dt>กำหนดปล่อย</dt><dd>{shortDate(r.plannedReleaseDate) || "ไม่ระบุ"}</dd></div>
                <div><dt>ปล่อยจริง</dt><dd>{shortDate(r.actualReleaseDate) || "-"}</dd></div>
                <div><dt>ประเภท</dt><dd>{r.releaseType || "ไม่ระบุ"}</dd></div>
                <div><dt>สร้างเมื่อ</dt><dd>{shortDate(r.createdAt) || "-"}</dd></div>
              </dl>
              <section className="rb-detail-section">
                <h3>ขอบเขต (Scope)</h3>
                <p className={r.scope ? "rb-detail-text" : "rb-detail-text empty"}>{r.scope || "ยังไม่ได้ระบุขอบเขตของ Release"}</p>
              </section>
              <section className="rb-detail-section">
                <h3>Build ใน Release{detailBuilds ? <small>{detailBuilds.length.toLocaleString("th-TH")} Build</small> : null}</h3>
                {detailBuilds === null ? <p className="rb-detail-text empty">เลือก Release นี้ในรายการเพื่อดู Build</p>
                  : detailBuilds.length === 0 ? <p className="rb-detail-text empty">ยังไม่มี Build ที่ใช้งานใน Release นี้</p>
                    : (
                      <ul className="rb-detail-builds">
                        {detailBuilds.map((build) => (
                          <li key={build.buildId} data-tone={buildStatusTone(build.status)}>
                            <button type="button" onClick={() => { setReleaseDetail(null); setBuildDetail(build); }}>
                              <span><b>{build.buildNumber}</b><small>{build.applicationVersion ? `App ${build.applicationVersion}` : "ไม่ระบุ App Version"}{build.buildDate ? ` · ${shortDate(build.buildDate)}` : ""}</small></span>
                              <span className="rb-detail-build-badges">{build.isReleaseCandidate && <span className="rb-rc"><span className="material-symbols-outlined" aria-hidden="true">workspace_premium</span>RC</span>}<Badge tone={buildStatusTone(build.status)}>{buildLabel(build.status)}</Badge><span className="material-symbols-outlined" aria-hidden="true">chevron_right</span></span>
                            </button>
                          </li>
                        ))}
                      </ul>
                    )}
              </section>
            </div>
          </ModalShell>
        );
      })()}
      {buildDetail && (() => {
        const b = buildDetail;
        const release = allItems.find((x) => x.releaseId === b.releaseId);
        const info = buildStatusInfo.find((x) => x.value === b.status);
        return (
          <ModalShell labelledBy="rb-build-detail-title" className="rb-detail" onDismiss={() => setBuildDetail(null)}>
            <div className="rb-detail-head">
              <div>
                <span className="rb-eyebrow">BUILD · {release ? `${release.releaseCode} · Version ${release.version}` : "Release"}</span>
                <h2 id="rb-build-detail-title">{b.buildNumber}</h2>
                <div className="rb-badges"><Badge tone={buildStatusTone(b.status)}>{buildLabel(b.status)}</Badge>{b.isReleaseCandidate && <span className="rb-rc"><span className="material-symbols-outlined" aria-hidden="true">workspace_premium</span>Release Candidate</span>}</div>
              </div>
              <div className="rb-detail-head-actions">
                {canEdit && <button className="btn primary" type="button" disabled={!selectedIsOpen} onClick={() => { setBuildDetail(null); openBuild(b); }}><span className="material-symbols-outlined" aria-hidden="true">edit</span>แก้ไข</button>}
                <button className="rb-icon-close" type="button" aria-label="ปิดรายละเอียด Build" onClick={() => setBuildDetail(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
              </div>
            </div>
            <div className="rb-detail-body">
              {info && <p className="rb-callout" data-tone={buildStatusTone(b.status)}><span className="material-symbols-outlined" aria-hidden="true">info</span><span><b>{info.meaning}</b>{info.impact}</span></p>}
              <dl className="rb-detail-meta">
                <div><dt>Application Version</dt><dd>{b.applicationVersion || "-"}</dd></div>
                <div><dt>Package Version</dt><dd>{b.packageVersion || "-"}</dd></div>
                <div><dt>Build Date</dt><dd>{shortDate(b.buildDate) || "ไม่ระบุ"}</dd></div>
                <div><dt>Commit Reference</dt><dd>{b.commitReference ? <code>{b.commitReference}</code> : "-"}</dd></div>
              </dl>
              <BuildDefectsPanel buildId={b.buildId} projectId={release?.projectId} isActive={b.isActive !== false} onOpenCycle={onOpenExecution ? (cycleId) => { setBuildDetail(null); onOpenExecution(cycleId); } : undefined} />
              <section className="rb-detail-section">
                <h3>Change Notes</h3>
                <p className={b.changeNotes ? "rb-detail-text" : "rb-detail-text empty"}>{b.changeNotes || "ไม่มี Change Notes"}</p>
              </section>
              <section className="rb-detail-section">
                <h3>Known Issues</h3>
                {b.knownIssues ? <p className="rb-detail-text warn">{b.knownIssues}</p> : <p className="rb-detail-text empty">ไม่พบ Known Issues</p>}
              </section>
            </div>
          </ModalShell>
        );
      })()}
      {modal && (
        <ModalShell onDismiss={() => setModal(null)} labelledBy="rb-form-title" className="rb-form">
          <div className="rb-detail-head">
            <div>
              <span className="rb-eyebrow">{editRelease || editBuild ? "แก้ไข" : "เพิ่ม"} {modal === "release" ? "RELEASE" : "BUILD"}{modal === "release" ? ` · ${code}` : ` · ${selected?.releaseCode ?? ""}`}</span>
              <h2 id="rb-form-title">{modal === "release" ? (name.trim() ? `Version ${name.trim()}` : editRelease ? `Version ${editRelease.version}` : "Release ใหม่") : (editBuild ? editBuild.buildNumber : code.trim() || "Build ใหม่")}</h2>
              {modal === "release" && !editRelease && <small className="rb-form-sub">ระบบสร้างรหัส Release ให้อัตโนมัติตาม Project</small>}
            </div>
            <button className="rb-icon-close" type="button" aria-label="ปิด" onClick={() => setModal(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
          </div>
          <div className="rb-detail-body">
            {formError && <div className="inline-alert error" role="alert"><span>{formError}</span></div>}
            {modal === "release" ? (
              <>
                <fieldset className="rb-form-section">
                  <legend>ข้อมูล Release</legend>
                  <div className="form-grid">
                    {!editRelease && (
                      <label>
                        Project
                        <select value={projectId} onChange={(e) => {
                          const value = e.target.value;
                          setProjectId(value);
                          const project = projects.find((x) => x.projectId === value);
                          setCode(nextBusinessCode(`${project?.projectCode ?? "PRJ"}-REL`, allItems.filter((x) => x.projectId === value).map((x) => x.releaseCode)));
                        }}>
                          {projects.map((x) => <option key={x.projectId} value={x.projectId}>{x.projectName}</option>)}
                        </select>
                      </label>
                    )}
                    <label>
                      <span>Version <span className="required-mark">*</span></span>
                      <input value={name} onChange={(e) => setName(e.target.value)} placeholder="เช่น 2.4.0" />
                    </label>
                    <label>
                      Release Type
                      <select value={type} onChange={(e) => setType(e.target.value)}>{masterOptionElements(releaseTypes, type)}</select>
                    </label>
                    <label>
                      วันที่วางแผนปล่อย
                      <DateField value={date} allowEmpty ariaLabel="วันที่วางแผนปล่อย" onChange={setDate} />
                    </label>
                    <label className="full">
                      ขอบเขต (Scope)
                      <textarea rows={4} value={details} onChange={(e) => setDetails(e.target.value)} placeholder="ฟีเจอร์/การแก้ไขหลักที่อยู่ใน Release นี้" />
                    </label>
                  </div>
                </fieldset>
                {editRelease && <StatusPicker name="release-status" legend="สถานะ Release" options={releaseStatusInfo} value={releaseStatus} tone={releaseTone} onChange={setReleaseStatus} />}
              </>
            ) : (
              <>
                <fieldset className="rb-form-section">
                  <legend>ข้อมูล Build</legend>
                  <div className="form-grid">
                    {!editBuild && (
                      <label>
                        <span>Build Number <span className="required-mark">*</span></span>
                        <input value={code} onChange={(e) => setCode(e.target.value)} placeholder="เช่น 2.4.0-b15" />
                      </label>
                    )}
                    {editBuild && (
                      <label>
                        Release
                        <select value={buildReleaseId} onChange={(e) => setBuildReleaseId(e.target.value)}>
                          {allItems.filter((release) => release.projectId === allItems.find((source) => source.releaseId === editBuild.releaseId)?.projectId && (release.releaseId === editBuild.releaseId || (release.status !== "Released" && release.status !== "Cancelled"))).map((release) => (
                            <option key={release.releaseId} value={release.releaseId}>{release.releaseCode} · Version {release.version}</option>
                          ))}
                        </select>
                        <small>ย้ายได้เฉพาะ Build ที่ยังไม่มีข้อมูลทดสอบหรือรายการอ้างอิง</small>
                      </label>
                    )}
                    <label>
                      Application Version
                      <input value={name} onChange={(e) => setName(e.target.value)} />
                    </label>
                    <label>
                      Package Version
                      <input value={packageVersion} onChange={(e) => setPackageVersion(e.target.value)} />
                    </label>
                    <label>
                      Commit Reference
                      <input value={commit} onChange={(e) => setCommit(e.target.value)} placeholder="commit hash หรือ branch" />
                    </label>
                    <label>
                      Build Date
                      <DateField value={date} allowEmpty ariaLabel="Build Date" onChange={setDate} />
                    </label>
                  </div>
                </fieldset>
                {editBuild && <StatusPicker name="build-status" legend="สถานะ Build" options={buildStatusInfo} value={buildStatus} tone={buildStatusTone} onChange={setBuildStatus} />}
                <fieldset className="rb-form-section">
                  <legend>บันทึกประกอบ</legend>
                  <div className="form-grid">
                    <label className="full">
                      Change Notes
                      <textarea rows={3} value={details} onChange={(e) => setDetails(e.target.value)} />
                    </label>
                    <label className="full">
                      Known Issues
                      <textarea rows={3} value={issues} onChange={(e) => setIssues(e.target.value)} placeholder="ปัญหาที่ทราบและควรระวังระหว่างทดสอบ" />
                    </label>
                  </div>
                </fieldset>
              </>
            )}
          </div>
          <div className="rb-form-foot">
            <span className="rb-form-hint" role="status">{formHint}</span>
            <button className="btn" type="button" onClick={() => setModal(null)}>ยกเลิก</button>
            <button className="btn primary" type="button" disabled={saving || !!formHint} onClick={save}>
              {saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">check</span> บันทึก</>}
            </button>
          </div>
        </ModalShell>
      )}
    </div>
  );
}

/** ตัวเลือกสถานะแบบชิป + ความหมาย/ผลกระทบของสถานะที่เลือก (แทน select + กล่องพับ) */
function StatusPicker({ name, legend, options, value, tone, onChange }: {
  name: string; legend: string; options: readonly { value: string; label: string; meaning: string; impact: string }[];
  value: string; tone: (status?: string) => string; onChange: (value: string) => void;
}) {
  const current = options.find((x) => x.value === value);
  return (
    <fieldset className="rb-form-section">
      <legend>{legend}</legend>
      <div className="rb-status-pick">
        {options.map((x) => (
          <label key={x.value} data-tone={tone(x.value)} className={value === x.value ? "selected" : undefined}>
            <input type="radio" name={name} value={x.value} checked={value === x.value} onChange={() => onChange(x.value)} />
            <b>{x.label}</b><small>{x.value}</small>
          </label>
        ))}
      </div>
      {current && <p className="rb-callout" data-tone={tone(current.value)}><span className="material-symbols-outlined" aria-hidden="true">info</span><span><b>{current.meaning}</b>ผลต่อการใช้งาน: {current.impact}</span></p>}
    </fieldset>
  );
}
