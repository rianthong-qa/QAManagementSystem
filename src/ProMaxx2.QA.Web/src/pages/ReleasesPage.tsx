import { useState, useRef, useEffect } from "react";
import type { ReleaseItem, ProjectItem, BuildItem } from "../shared/types";
import { apiUrl, getJson, isAbortError } from "../api";
import { notify, confirmDialog } from "../components/dialogStore";
import { formatThaiDateTime } from "../dateTime";
import { Badge } from "../components/Badge";
import { ModalShell } from "../components/ModalShell";
import { type SessionUser, masterOptionElements, nextBusinessCode, useMasterOptions } from "../shared/appShared";

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
/** สีป้ายสถานะ Build ตาม UI_DESIGN_SYSTEM §6 — เดิมหน้ารายการเป็นสีเขียวเสมอ (Failed/Blocked ก็เขียว) และหน้า detail ใช้ Ready = เขียว นอกนั้นเหลือง */
const buildStatusTone = (status?: string) => status === "Ready" || status === "Passed" ? "green" : status === "Failed" ? "red" : status === "Blocked" ? "yellow" : status === "Testing" ? "blue" : "gray";
export function ReleasesPage({ search, contextProjectId }: { search: string; refresh?: number; contextProjectId?: string }) {
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
    [saving, setSaving] = useState(false);
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
    filteredReleases = items.filter((x) =>
      (!contextProjectId || x.projectId === contextProjectId) &&
      `${x.releaseCode} ${x.version} ${x.releaseType ?? ""} ${x.status}`
        .toLowerCase()
        .includes(term),
    ),
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
  if (loading)
    return (
      <article className="card empty">
        <div className="spinner" />
        <p>กำลังโหลดข้อมูล Release...</p>
      </article>
    );
  if (error)
    return (
      <article className="card empty">
        <div className="login-error">{error}</div>
      </article>
    );
  return (
    <div className="release-layout">
      <article className="card release-list">
        <div className="card-title">
          <div>
            <h3>Releases</h3>
            <p>{filteredReleases.length} Release ที่ใช้งาน</p>
          </div>
          {canEdit && (
            <button className="btn primary" onClick={() => openRelease()}>
              + Release
            </button>
          )}
        </div>
        <div className="release-cards">
          {filteredReleases.map((x) => (
            <div key={x.releaseId} className={`release-card${selectedId === x.releaseId ? " active" : ""}`}>
              <button className="release-card-select" aria-label={`เลือก ${x.releaseCode} Version ${x.version}`} aria-pressed={selectedId === x.releaseId} onClick={() => setSelectedId(x.releaseId)}>
                <span>{x.releaseCode}</span>
                <b>Version {x.version}</b>
                <small><span>{x.releaseType || "ไม่ระบุประเภท"}</span><span>{x.plannedReleaseDate ? formatThaiDateTime(x.plannedReleaseDate, { day: "numeric", month: "numeric", year: "numeric" }) : "ไม่ระบุวัน"}</span></small>
                <Badge tone={x.status === "Ready" || x.status === "Released" ? "green" : "yellow"}>{x.status}</Badge>
              </button>
              <button className="release-card-detail" aria-label={`ดูรายละเอียด ${x.releaseCode}`} onClick={() => setReleaseDetail(x)}>รายละเอียด <span aria-hidden="true">›</span></button>
            </div>
          ))}
        </div>
      </article>
      <article className="card build-panel">
        <div className="card-title">
          <div>
            <h3>Builds {selected && <span>· {selected.releaseCode}</span>}</h3>
            <p>{filteredBuilds.length} Build ใน Release ที่เลือก</p>
          </div>
          {selected && (
            <div className="row-actions">
              <button className="btn" onClick={() => setReleaseDetail(selected)}>
                รายละเอียด Release
              </button>
              {canEdit && <><button className="btn" disabled={!selectedIsOpen} onClick={() => openRelease(selected)}>
                  แก้ไข Release
                </button>
                <button className="btn primary" disabled={!selectedIsOpen} onClick={() => openBuild()}>
                  + Build
                </button></>}
            </div>
          )}
        </div>
        {selected ? (
          <div className="table-wrap">
            <table className="table-cards">
              <thead>
                <tr>
                  <th>Build Number</th>
                  <th>App Version</th>
                  <th>Package</th>
                  <th>Commit</th>
                  <th>Build Date</th>
                  <th>Status</th>
                  {canEdit && <th className="actions-col">จัดการ</th>}
                </tr>
              </thead>
              <tbody>
                {filteredBuilds.map((x) => (
                  <tr key={x.buildId}>
                    <td>
                      <button className="release-build-link" onClick={() => setBuildDetail(x)}>{x.buildNumber}</button>
                      {x.isReleaseCandidate && <Badge tone="blue">RC</Badge>}
                    </td>
                    <td>{x.applicationVersion || "-"}</td>
                    <td>{x.packageVersion || "-"}</td>
                    <td>{x.commitReference || "-"}</td>
                    <td>
                      {x.buildDate
                        ? formatThaiDateTime(x.buildDate, { day: "numeric", month: "numeric", year: "numeric" })
                        : "-"}
                    </td>
                    <td>
                      <Badge tone={buildStatusTone(x.status)}>{x.status}</Badge>
                    </td>
                    {canEdit && (
                      <td className="actions-col">
                        <div className="row-actions">
                          <button
                            className="table-action icon-only"
                            title="แก้ไข"
                            aria-label={`แก้ไข ${x.buildNumber}`}
                            disabled={!selectedIsOpen}
                            onClick={() => openBuild(x)}
                          >
                            <span className="material-symbols-outlined" aria-hidden="true">edit</span>
                          </button>
                          {!x.isReleaseCandidate && (
                            <button
                              className="table-action icon-only"
                              title="Mark RC"
                              aria-label={`Mark RC ${x.buildNumber}`}
                              disabled={!selectedIsOpen}
                              onClick={() => markRc(x)}
                            >
                              <span aria-hidden="true">★</span>
                            </button>
                          )}
                          <button
                            className="table-action danger-action icon-only"
                            title="ลบ"
                            aria-label={`ลบ ${x.buildNumber}`}
                            disabled={!selectedIsOpen}
                            onClick={() =>
                              remove("build", x.buildId, x.buildNumber)
                            }
                          >
                            <span className="material-symbols-outlined" aria-hidden="true">close</span>
                          </button>
                        </div>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <div className="empty">
            <p>เลือก Release เพื่อดู Builds</p>
          </div>
        )}
        {canEdit && selected && (
          <div className="project-footer">
            <button
              className="danger-link"
              disabled={!selectedIsOpen}
              onClick={() =>
                remove("release", selected.releaseId, selected.releaseCode)
              }
            >
              ยกเลิกและซ่อน Release นี้
            </button>
          </div>
        )}
      </article>
      {releaseDetail && (
        <ModalShell labelledBy="release-detail-title" className="release-build-detail" onDismiss={() => setReleaseDetail(null)}>
            <div className="modal-head"><div><h2 id="release-detail-title">รายละเอียด Release</h2><small>{releaseDetail.releaseCode}</small></div><button aria-label="ปิดรายละเอียด Release" onClick={() => setReleaseDetail(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
            <div className="release-detail-hero"><div><span className="release-detail-eyebrow">Release</span><b>{releaseDetail.releaseCode}</b><h3>Version {releaseDetail.version}</h3><div className="release-detail-badges"><Badge tone={releaseDetail.status === "Ready" || releaseDetail.status === "Released" ? "green" : "yellow"}>{releaseDetail.status}</Badge>{releaseDetail.releaseType && <Badge tone="blue">{releaseDetail.releaseType}</Badge>}</div></div><div className="release-date-card"><span aria-hidden="true">◫</span><small>Planned Release</small><b>{releaseDetail.plannedReleaseDate ? formatThaiDateTime(releaseDetail.plannedReleaseDate, { day: "numeric", month: "short", year: "numeric" }) : "ไม่ระบุวัน"}</b></div></div>
            <div className="release-detail-meta"><div><span aria-hidden="true">P</span><small>Project<b>{projects.find((x) => x.projectId === releaseDetail.projectId)?.projectName || "-"}</b></small></div><div><span aria-hidden="true">#</span><small>Builds<b>{releaseDetail.releaseId === selectedId ? builds.length : "เลือก Release เพื่อดู"}</b></small></div><div><span aria-hidden="true">S</span><small>Status<b>{releaseDetail.status}</b></small></div></div>
            <section className="release-detail-section"><div className="release-detail-heading"><span aria-hidden="true">≡</span><div><h3>Release Scope</h3><small>ขอบเขตและเป้าหมายของ Release</small></div></div><p>{releaseDetail.scope || "ยังไม่ได้ระบุขอบเขตของ Release"}</p></section>
            <section className="release-detail-section"><div className="release-detail-heading"><span className="material-symbols-outlined" aria-hidden="true">description</span><div><h3>Builds ใน Release</h3><small>รายการ Build ที่พร้อมใช้งาน</small></div></div>{releaseDetail.releaseId === selectedId && builds.length ? <div className="release-detail-builds">{builds.map((build) => <button key={build.buildId} onClick={() => { setReleaseDetail(null); setBuildDetail(build); }}><span><b>{build.buildNumber}</b><small>{build.applicationVersion || "ไม่ระบุ Application Version"}</small></span><span><Badge tone={buildStatusTone(build.status)}>{build.status}</Badge>{build.isReleaseCandidate && <Badge tone="blue">RC</Badge>}<i aria-hidden="true">›</i></span></button>)}</div> : <div className="release-detail-empty">ยังไม่มี Build ที่ใช้งานใน Release นี้</div>}</section>
            <div className="modal-actions"><button className="btn" onClick={() => setReleaseDetail(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ปิด</button>{canEdit && <button className="btn primary" onClick={() => { const item = releaseDetail; setReleaseDetail(null); openRelease(item); }}><span className="material-symbols-outlined" aria-hidden="true">edit</span> แก้ไข Release</button>}</div>
          </ModalShell>
      )}
      {buildDetail && (
        <ModalShell labelledBy="build-detail-title" className="release-build-detail build-read-detail" onDismiss={() => setBuildDetail(null)}>
            <div className="modal-head"><div><h2 id="build-detail-title">รายละเอียด Build</h2><small>{selected?.releaseCode || "Release"}</small></div><button aria-label="ปิดรายละเอียด Build" onClick={() => setBuildDetail(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
            <div className="build-detail-hero"><div><span className="release-detail-eyebrow">Build Number</span><h3>{buildDetail.buildNumber}</h3><div className="release-detail-badges"><Badge tone={buildStatusTone(buildDetail.status)}>{buildDetail.status}</Badge>{buildDetail.isReleaseCandidate && <Badge tone="blue">Release Candidate</Badge>}</div></div><div className="build-version-card"><small>Application Version</small><b>{buildDetail.applicationVersion || "-"}</b><span>Package {buildDetail.packageVersion || "-"}</span></div></div>
            <div className="release-detail-meta build-detail-meta"><div><span aria-hidden="true">◫</span><small>Build Date<b>{buildDetail.buildDate ? formatThaiDateTime(buildDetail.buildDate, { day: "numeric", month: "numeric", year: "numeric" }) : "ไม่ระบุ"}</b></small></div><div><span aria-hidden="true">C</span><small>Commit Reference<b>{buildDetail.commitReference || "ไม่ระบุ"}</b></small></div><div><span aria-hidden="true">S</span><small>Status<b>{buildDetail.status}</b></small></div></div>
            <section className="release-detail-section"><div className="release-detail-heading"><span className="material-symbols-outlined" aria-hidden="true">add</span><div><h3>Change Notes</h3><small>รายการเปลี่ยนแปลงใน Build นี้</small></div></div><p>{buildDetail.changeNotes || "ไม่มี Change Notes"}</p></section>
            <section className="release-detail-section known-issues"><div className="release-detail-heading"><span aria-hidden="true">!</span><div><h3>Known Issues</h3><small>ปัญหาที่ทราบและควรระวัง</small></div></div><p>{buildDetail.knownIssues || "ไม่พบ Known Issues"}</p></section>
            <div className="modal-actions"><button className="btn" onClick={() => setBuildDetail(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ปิด</button>{canEdit && <button className="btn primary" onClick={() => { const item = buildDetail; setBuildDetail(null); openBuild(item); }}><span className="material-symbols-outlined" aria-hidden="true">edit</span> แก้ไข Build</button>}</div>
          </ModalShell>
      )}
      {modal && (
        <ModalShell onDismiss={() => setModal(null)}>
            <div className="modal-head">
              <h2>
                {editRelease || editBuild ? "แก้ไข" : "เพิ่ม"}{" "}
                {modal === "release" ? "Release" : "Build"}
              </h2>
              <button onClick={() => setModal(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
            </div>
            {formError && <div className="login-error" role="alert">{formError}</div>}
            <div className="form-grid">
              {modal === "release" && !editRelease && (
                <label>
                  Project
                  <select
                    value={projectId}
                    onChange={(e) => {
                      const value = e.target.value;
                      setProjectId(value);
                      const project = projects.find(
                        (x) => x.projectId === value,
                      );
                      setCode(
                        nextBusinessCode(
                          `${project?.projectCode ?? "PRJ"}-REL`,
                          allItems
                            .filter((x) => x.projectId === value)
                            .map((x) => x.releaseCode),
                        ),
                      );
                    }}
                  >
                    {projects.map((x) => (
                      <option key={x.projectId} value={x.projectId}>
                        {x.projectName}
                      </option>
                    ))}
                  </select>
                </label>
              )}
              <label>
                {modal === "release" ? "Release Code" : "Build Number"}
                <input
                  disabled={modal === "release" || !!editBuild}
                  value={code}
                  onChange={(e) => setCode(e.target.value)}
                />
              </label>
              <label>
                {modal === "release" ? "Version" : "Application Version"}
                <input value={name} onChange={(e) => setName(e.target.value)} />
              </label>
              {modal === "release" ? (
                <>
                  <label>
                    Release Type
                    <select
                      value={type}
                      onChange={(e) => setType(e.target.value)}
                    >
                      {masterOptionElements(releaseTypes, type)}
                    </select>
                  </label>
                  {editRelease && (
                    <>
                      <label>
                        สถานะ Release
                        <select
                          value={releaseStatus}
                          onChange={(e) => setReleaseStatus(e.target.value)}
                        >
                          <option value="Draft">Draft</option>
                          <option value="Testing">Testing</option>
                          <option value="Ready">Ready</option>
                          <option value="Released">Released</option>
                          <option value="Cancelled">Cancelled</option>
                        </select>
                      </label>
                      <details className="requirement-status-information full">
                        <summary>
                          <span className="information-icon" aria-hidden="true">i</span>
                          <span><b>{releaseStatus} · {releaseStatusInfo.find((x) => x.value === releaseStatus)?.label}</b><small>{releaseStatusInfo.find((x) => x.value === releaseStatus)?.meaning}</small></span>
                          <em>ดูความหมายทั้งหมด</em>
                        </summary>
                        <div className="requirement-status-list">
                          {releaseStatusInfo.map((item) => <article key={item.value} className={releaseStatus === item.value ? "active" : ""}>
                            <div><b>{item.value}</b><span>{item.label}</span></div>
                            <p>{item.meaning}</p>
                            <small><strong>ผลต่อการใช้งาน:</strong> {item.impact}</small>
                          </article>)}
                        </div>
                      </details>
                    </>
                  )}
                  <label>
                    Planned Date
                    <input
                      type="date"
                      value={date}
                      onChange={(e) => setDate(e.target.value)}
                    />
                  </label>
                  <label className="full">
                    Scope
                    <textarea
                      rows={4}
                      value={details}
                      onChange={(e) => setDetails(e.target.value)}
                    />
                  </label>
                </>
              ) : (
                <>
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
                  {editBuild && (
                    <>
                      <label>
                        สถานะ Build
                        <select
                          value={buildStatus}
                          onChange={(e) => setBuildStatus(e.target.value)}
                        >
                          <option value="Ready">Ready</option>
                          <option value="Testing">Testing</option>
                          <option value="Passed">Passed</option>
                          <option value="Failed">Failed</option>
                          <option value="Blocked">Blocked</option>
                        </select>
                      </label>
                      <details className="requirement-status-information full">
                        <summary>
                          <span className="information-icon" aria-hidden="true">i</span>
                          <span><b>{buildStatus} · {buildStatusInfo.find((x) => x.value === buildStatus)?.label}</b><small>{buildStatusInfo.find((x) => x.value === buildStatus)?.meaning}</small></span>
                          <em>ดูความหมายทั้งหมด</em>
                        </summary>
                        <div className="requirement-status-list">
                          {buildStatusInfo.map((item) => <article key={item.value} className={buildStatus === item.value ? "active" : ""}>
                            <div><b>{item.value}</b><span>{item.label}</span></div>
                            <p>{item.meaning}</p>
                            <small><strong>ผลต่อการใช้งาน:</strong> {item.impact}</small>
                          </article>)}
                        </div>
                      </details>
                    </>
                  )}
                  <label>
                    Package Version
                    <input
                      value={packageVersion}
                      onChange={(e) => setPackageVersion(e.target.value)}
                    />
                  </label>
                  <label>
                    Commit Reference
                    <input
                      value={commit}
                      onChange={(e) => setCommit(e.target.value)}
                    />
                  </label>
                  <label>
                    Build Date
                    <input
                      type="date"
                      value={date}
                      onChange={(e) => setDate(e.target.value)}
                    />
                  </label>
                  <label className="full">
                    Change Notes
                    <textarea
                      rows={3}
                      value={details}
                      onChange={(e) => setDetails(e.target.value)}
                    />
                  </label>
                  <label className="full">
                    Known Issues
                    <textarea
                      rows={3}
                      value={issues}
                      onChange={(e) => setIssues(e.target.value)}
                    />
                  </label>
                </>
              )}
            </div>
            <div className="modal-actions">
              <button className="btn" onClick={() => setModal(null)}>
                ยกเลิก
              </button>
              <button
                className="btn primary"
                disabled={
                  saving ||
                  !code.trim() ||
                  (modal === "build" && !!editBuild && !buildReleaseId) ||
                  (modal === "release" && !name.trim())
                }
                onClick={save}
              >
                {saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">check</span> บันทึก</>}
              </button>
            </div>
          </ModalShell>
      )}
    </div>
  );
}
