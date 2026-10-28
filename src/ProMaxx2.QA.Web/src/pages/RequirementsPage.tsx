import { useState, useEffect, useMemo } from "react";
import type { ReleaseItem, ProjectItem } from "../shared/types";
import { apiUrl, getJson } from "../api";
import { notify, confirmDialog } from "../components/dialogStore";
import { Badge } from "../components/Badge";
import { ModalShell } from "../components/ModalShell";
import { formatThaiDateTime } from "../dateTime";
import { type AdminUser, type ModuleItem, type RtmItem, buildModuleTree, renderModuleSelectOptions } from "../shared/appShared";

type RequirementItem = {
  requirementId: string;
  projectId: string;
  requirementCode: string;
  title: string;
  description?: string;
  acceptanceCriteria?: string;
  priority: string;
  riskLevel?: string;
  source?: string;
  ownerUserId?: string;
  status: string;
  revisionNo: number;
  isInScope: boolean;
  moduleId: string;
  releaseId?: string;
  testCaseCount?: number;
  createdAt?: string;
};
type RequirementRevisionItem = {
  revisionNo: number;
  title: string;
  description?: string;
  acceptanceCriteria?: string;
  changedBy?: string;
  changedAt: string;
  changeReason?: string;
};
const requirementStatusInformation = [
  { value: "Draft", label: "ฉบับร่าง", meaning: "Requirement ยังอยู่ระหว่างจัดทำและแก้ไขรายละเอียด", impact: "ยังไม่ถือว่าผ่านการตรวจสอบ เหมาะสำหรับเก็บข้อมูลเริ่มต้นก่อนส่งให้ทีม Review" },
  { value: "Review", label: "รอตรวจสอบ", meaning: "Requirement พร้อมให้ผู้เกี่ยวข้องตรวจความครบถ้วนและความถูกต้อง", impact: "ทีมควรตรวจ Description, Acceptance Criteria, Priority และ Scope ก่อนอนุมัติ" },
  { value: "Approved", label: "อนุมัติแล้ว", meaning: "Requirement ผ่านการรับรองและใช้เป็นข้อตกลงอ้างอิงของทีม", impact: "พร้อมนำไปออกแบบ Test Case และวางแผนพัฒนา การแก้สาระสำคัญควรระบุเหตุผลและสร้าง Revision" },
  { value: "Implemented", label: "พัฒนาแล้ว", meaning: "ความสามารถตาม Requirement ถูกนำไปพัฒนาหรือส่งมอบแล้ว", impact: "ไม่ได้หมายความว่าทดสอบผ่านโดยอัตโนมัติ ยังต้องมี Test Case, Execution และผลทดสอบรองรับ" },
  { value: "Cancelled", label: "ยกเลิก", meaning: "Requirement นี้เลิกใช้งานหรือไม่นำไปดำเนินการต่อ", impact: "ระบบยังนับใน RTM/Coverage หากเลือก In Scope อยู่ หากไม่ต้องการให้นับต้องยกเลิก In Scope ด้วย" },
] as const;
export function RequirementsPage({
  search,
  refresh,
  canEdit,
  contextProjectId,
}: {
  search: string;
  refresh: number;
  canEdit: boolean;
  contextProjectId?: string;
}) {
  const [items, setItems] = useState<RequirementItem[]>([]),
    [testCaseCounts, setTestCaseCounts] = useState<Record<string, number>>({}),
    [error, setError] = useState(""),
    [loading, setLoading] = useState(true),
    [reload, setReload] = useState(0),
    [editing, setEditing] = useState<RequirementItem | null>(null),
    [users, setUsers] = useState<AdminUser[]>([]),
    [ownerUserId, setOwnerUserId] = useState(""),
    [statusFilter, setStatusFilter] = useState(""),
    [priorityFilter, setPriorityFilter] = useState(""),
    [scopeFilter, setScopeFilter] = useState(""),
    [coverageFilter, setCoverageFilter] = useState(""),
    [moduleFilter, setModuleFilter] = useState(""),
    [releaseFilter, setReleaseFilter] = useState(""),
    [filterReleases, setFilterReleases] = useState<ReleaseItem[]>([]),
    [filterModules, setFilterModules] = useState<ModuleItem[]>([]),
    [filterProjects, setFilterProjects] = useState<ProjectItem[]>([]),
    [viewing, setViewing] = useState<RequirementItem | null>(null),
    [viewRelease, setViewRelease] = useState<ReleaseItem | null>(null),
    [historyItem, setHistoryItem] = useState<RequirementItem | null>(null),
    [revisions, setRevisions] = useState<RequirementRevisionItem[]>([]),
    [historyLoading, setHistoryLoading] = useState(false), [historyError, setHistoryError] = useState(""),
    [modules, setModules] = useState<ModuleItem[]>([]),
    [releases, setReleases] = useState<ReleaseItem[]>([]),
    [moduleId, setModuleId] = useState(""),
    [releaseId, setReleaseId] = useState(""),
    [title, setTitle] = useState(""),
    [description, setDescription] = useState(""),
    [criteria, setCriteria] = useState(""),
    [priority, setPriority] = useState("P2"),
    [risk, setRisk] = useState("Medium"),
    [source, setSource] = useState(""),
    [status, setStatus] = useState("Draft"),
    [inScope, setInScope] = useState(true),
    [saving, setSaving] = useState(false);
  const headers = {
    "Content-Type": "application/json",
    Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
  };
  useEffect(() => {
    fetch(`${apiUrl}/requirements`, {
      headers: {
        Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
      },
    })
      .then(async (r) => {
        if (!r.ok) throw new Error("โหลด Requirement ไม่สำเร็จ");
        return r.json();
      })
      .then(async (data: RequirementItem[] | { items?: { rows: RequirementItem[] }; rows?: RequirementItem[] }) => {
        const rows = Array.isArray(data) ? data : (data as { items?: { rows: RequirementItem[] } }).items?.rows ?? (data as { rows?: RequirementItem[] }).rows ?? [];
        setItems(rows);
        const releaseIds = [...new Set(rows.map((x) => x.releaseId).filter((x): x is string => !!x))];
        let rtmFailed = false;
        const rtmRows = await Promise.all(releaseIds.map((id) => getJson(`${apiUrl}/releases/${id}/rtm`).catch(() => { rtmFailed = true; return []; })));
        if (rtmFailed) setError("โหลดจำนวน Test Case จาก RTM ไม่สำเร็จบาง Release — Coverage ที่แสดงอาจต่ำกว่าความจริง กด ⟳ เพื่อลองใหม่");
        const rtmItems = rtmRows.map((r: unknown) => Array.isArray(r) ? r : (r as { items?: { rows: unknown[] } }).items?.rows ?? []).flat();
        const counts: Record<string, number> = {};
        rtmItems.forEach((x: RtmItem) => { counts[x.requirementId] = x.testCaseCount; });
        setTestCaseCounts(counts);
      })
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [refresh, reload]);
  useEffect(() => {
    getJson<AdminUser[] | { items?: { rows: AdminUser[] } }>(`${apiUrl}/admin/users`)
      .then((data) => {
        const rows = Array.isArray(data) ? data : data.items?.rows ?? [];
        setUsers(rows.filter((x) => x.isActive));
      })
      .catch(() => { /* ผู้ใช้ที่ไม่มีสิทธิ์ ADMIN ได้ 403 — dropdown เจ้าของเหลือค่าว่าง ไม่ใช่ error ของหน้า */ });
  }, []);
  useEffect(() => {
    getJson<ProjectItem[]>(`${apiUrl}/projects`).then(async (projects) => {
      setFilterProjects(projects);
      const moduleGroups = await Promise.all(projects.map((project) => getJson<ModuleItem[]>(`${apiUrl}/projects/${project.projectId}/modules`)));
      setFilterModules(moduleGroups.flat().filter((module) => module.isActive));
    }).catch(() => setError("โหลดตัวกรอง Project/Module ไม่สำเร็จ — ตัวกรองอาจไม่ครบ"));
  }, []);
  useEffect(() => {
    setModuleFilter("");
    setReleaseFilter("");
  }, [contextProjectId]);
  useEffect(() => {
    if (!contextProjectId) {
      setFilterReleases([]);
      return;
    }
    getJson<ReleaseItem[]>(`${apiUrl}/projects/${contextProjectId}/releases`)
      .then((data) => setFilterReleases(data.filter((x) => x.status !== "Cancelled")))
      .catch(() => { setFilterReleases([]); setError("โหลดรายการ Release สำหรับตัวกรองไม่สำเร็จ"); });
  }, [contextProjectId]);
  const openEdit = async (item: RequirementItem) => {
    let moduleData: ModuleItem[], releaseData: ReleaseItem[];
    try {
      [moduleData, releaseData] = await Promise.all([
        getJson<ModuleItem[]>(`${apiUrl}/projects/${item.projectId}/modules`),
        getJson<ReleaseItem[]>(`${apiUrl}/projects/${item.projectId}/releases`),
      ]);
    } catch (e) {
      notify(`เปิด ${item.requirementCode} เพื่อแก้ไขไม่สำเร็จ (${e instanceof Error ? e.message : "ไม่ทราบสาเหตุ"})`, "error");
      return;
    }
    setModules((moduleData as ModuleItem[]).filter((x) => x.isActive || x.moduleId === item.moduleId));
    setReleases((releaseData as ReleaseItem[]).filter((x) => x.status !== "Cancelled" || x.releaseId === item.releaseId));
    setEditing(item);
    setModuleId(item.moduleId);
    setReleaseId(item.releaseId ?? "");
    setTitle(item.title);
    setDescription(item.description ?? "");
    setCriteria(item.acceptanceCriteria ?? "");
    setPriority(item.priority);
    setRisk(item.riskLevel ?? "Medium");
    setSource(item.source ?? "");
    setOwnerUserId(item.ownerUserId ?? "");
    setStatus(item.status);
    setInScope(item.isInScope);
  };
  const saveEdit = async () => {
    if (!editing) return;
    setSaving(true);
    try {
      const response = await fetch(`${apiUrl}/requirements/${editing.requirementId}`, {
        method: "PUT",
        headers,
        body: JSON.stringify({ releaseId: releaseId || null, moduleId, title, description: description || null, acceptanceCriteria: criteria || null, priority, riskLevel: risk || null, source: source || null, ownerUserId: ownerUserId || null, isInScope: inScope }),
      });
      if (!response.ok) throw new Error((await response.json()).detail ?? "แก้ไข Requirement ไม่สำเร็จ");
      if (status !== editing.status) {
        const statusResponse = await fetch(`${apiUrl}/requirements/${editing.requirementId}/status`, { method: "POST", headers, body: JSON.stringify({ status }) });
        if (!statusResponse.ok) throw new Error((await statusResponse.json()).detail ?? "เปลี่ยนสถานะไม่สำเร็จ");
      }
      setEditing(null);
      setReload((x) => x + 1);
    } catch (e) {
      notify(e instanceof Error ? e.message : "แก้ไข Requirement ไม่สำเร็จ", "error");
    } finally { setSaving(false); }
  };
  const openHistory = async (item: RequirementItem) => {
    setHistoryItem(item);
    setHistoryLoading(true);
    setHistoryError("");
    try {
      setRevisions(await getJson(`${apiUrl}/requirements/${item.requirementId}/revisions`));
    } catch (e) {
      setRevisions([]);
      setHistoryError(`โหลดประวัติ Revision ไม่สำเร็จ (${e instanceof Error ? e.message : "ไม่ทราบสาเหตุ"})`);
    } finally { setHistoryLoading(false); }
  };
  const openDetail = async (item: RequirementItem) => {
    setViewing(item);
    setViewRelease(null);
    if (!item.releaseId) return;
    const response = await fetch(`${apiUrl}/projects/${item.projectId}/releases`, { headers });
    if (response.ok) {
      const data: ReleaseItem[] = await response.json();
      setViewRelease(data.find((x) => x.releaseId === item.releaseId) ?? null);
    }
  };
  const remove = async (item: RequirementItem) => {
    if (!await confirmDialog(`ยืนยันลบ ${item.requirementCode}?\nข้อมูลจะถูกซ่อนและยังเก็บประวัติไว้`)) return;
    const response = await fetch(`${apiUrl}/requirements/${item.requirementId}`, { method: "DELETE", headers });
    if (!response.ok) { notify("ลบ Requirement ไม่สำเร็จ", "error"); return; }
    setReload((x) => x + 1);
  };
  const moduleOrderMap = useMemo(() => {
    const scoped = contextProjectId ? filterModules.filter((x) => x.projectId === contextProjectId && x.isActive) : filterModules.filter((x) => x.isActive);
    return new Map(buildModuleTree(scoped).map(({ module }, index) => [module.moduleId, index]));
  }, [filterModules, contextProjectId]);
  const moduleLookup = useMemo(() => new Map(filterModules.map((m) => [m.moduleId, m])), [filterModules]);
  if (loading)
    return (
      <article className="card empty">
        <div className="spinner" />
        <p>กำลังโหลด Requirement...</p>
      </article>
    );
  if (error)
    return (
      <article className="card empty">
        <div className="login-error">{error}</div>
      </article>
    );
  const scopedItems = contextProjectId ? items.filter((x) => x.projectId === contextProjectId) : items;
  const statusOrder = ["Draft", "Review", "Approved", "Implemented", "Cancelled"];
  const priorityOrder = ["P0", "P1", "P2", "P3"];
  const statusOptions = [
    ...statusOrder.filter((s) => scopedItems.some((x) => x.status === s)),
    ...[...new Set(scopedItems.map((x) => x.status))].filter((s) => !statusOrder.includes(s)).sort(),
  ];
  const priorityOptions = [
    ...priorityOrder.filter((p) => scopedItems.some((x) => x.priority === p)),
    ...[...new Set(scopedItems.map((x) => x.priority))].filter((p) => !priorityOrder.includes(p)).sort(),
  ];
  const countBy = (key: "status" | "priority", value: string) => scopedItems.filter((x) => x[key] === value).length;
  const filtered = items
    .filter((x) =>
      (!contextProjectId || x.projectId === contextProjectId) &&
      `${x.requirementCode} ${x.title} ${x.priority} ${x.status} ${moduleLookup.get(x.moduleId)?.moduleName ?? ""} ${moduleLookup.get(x.moduleId)?.moduleCode ?? ""}`.toLowerCase().includes(search.toLowerCase()) &&
      (!statusFilter || x.status === statusFilter) &&
      (!priorityFilter || x.priority === priorityFilter) &&
      (!scopeFilter || String(x.isInScope) === scopeFilter) &&
      (!coverageFilter || (coverageFilter === "covered" ? (testCaseCounts[x.requirementId] ?? 0) > 0 : (testCaseCounts[x.requirementId] ?? 0) === 0)) &&
      (!releaseFilter || x.releaseId === releaseFilter) &&
      (!moduleFilter || x.moduleId === moduleFilter),
    )
    .sort((a, b) => {
      if (!moduleOrderMap.size) return a.requirementCode.localeCompare(b.requirementCode, undefined, { numeric: true });
      const ia = moduleOrderMap.get(a.moduleId) ?? Number.MAX_SAFE_INTEGER;
      const ib = moduleOrderMap.get(b.moduleId) ?? Number.MAX_SAFE_INTEGER;
      return ia - ib || a.requirementCode.localeCompare(b.requirementCode, undefined, { numeric: true });
    });
  const inScopeCount = scopedItems.filter((x) => x.isInScope).length;
  const approvedCount = scopedItems.filter((x) => x.status === "Approved").length;
  const coveredCount = scopedItems.filter((x) => x.isInScope && (testCaseCounts[x.requirementId] ?? 0) > 0).length;
  const uncoveredCount = Math.max(0, inScopeCount - coveredCount);
  const coveragePercent = inScopeCount ? Math.round((coveredCount / inScopeCount) * 100) : 0;
  const activeFilterCount = [moduleFilter, releaseFilter, scopeFilter, statusFilter, priorityFilter, coverageFilter].filter(Boolean).length;
  const clearFilters = () => {
    setModuleFilter("");
    setReleaseFilter("");
    setScopeFilter("");
    setStatusFilter("");
    setPriorityFilter("");
    setCoverageFilter("");
  };
  return (
    <div className="requirement-page">
      <section className="requirement-overview" aria-label="ภาพรวม Requirement">
        <button type="button" className={`requirement-overview-card total${activeFilterCount === 0 ? " active" : ""}`} onClick={clearFilters} aria-pressed={activeFilterCount === 0}>
          <span className="material-symbols-outlined" aria-hidden="true">description</span>
          <div><small>Requirement ทั้งหมด</small><strong>{scopedItems.length.toLocaleString()}</strong><p>รายการใน Project ปัจจุบัน</p></div>
        </button>
        <button type="button" className={`requirement-overview-card scope${scopeFilter === "true" ? " active" : ""}`} onClick={() => setScopeFilter((value) => value === "true" ? "" : "true")} aria-pressed={scopeFilter === "true"}>
          <span className="material-symbols-outlined" aria-hidden="true">filter_alt</span>
          <div><small>อยู่ในขอบเขต</small><strong>{inScopeCount.toLocaleString()}</strong><p>{scopedItems.length ? `${Math.round((inScopeCount / scopedItems.length) * 100)}% ของทั้งหมด` : "ยังไม่มีข้อมูล"}</p></div>
        </button>
        <button type="button" className={`requirement-overview-card approved${statusFilter === "Approved" ? " active" : ""}`} onClick={() => setStatusFilter((value) => value === "Approved" ? "" : "Approved")} aria-pressed={statusFilter === "Approved"}>
          <span className="material-symbols-outlined" aria-hidden="true">verified</span>
          <div><small>อนุมัติแล้ว</small><strong>{approvedCount.toLocaleString()}</strong><p>พร้อมใช้เป็นข้อมูลอ้างอิง</p></div>
        </button>
        <button type="button" className={`requirement-overview-card coverage${coverageFilter === "uncovered" ? " active" : ""}`} onClick={() => setCoverageFilter((value) => value === "uncovered" ? "" : "uncovered")} aria-pressed={coverageFilter === "uncovered"}>
          <span className="material-symbols-outlined" aria-hidden="true">fact_check</span>
          <div><small>Test Coverage</small><strong>{coveragePercent}%</strong><p className={uncoveredCount ? "is-warning" : "is-complete"}>{uncoveredCount ? `เหลือ ${uncoveredCount} รายการที่ยังไม่มี Test Case` : "ครบทุกรายการใน Scope"}</p></div>
        </button>
      </section>

      <article className="card requirement-page-card">
      <div className="requirement-list-head">
        <div><h2>รายการ Requirement</h2><p>เลือกสถานะหรือการ์ดสรุปเพื่อเจาะดูรายการที่ต้องการ</p></div>
        <div className="result-count"><strong>{filtered.length.toLocaleString()}</strong><span>จาก {scopedItems.length.toLocaleString()} รายการ</span></div>
      </div>
      <div className="requirement-status-tabs" role="group" aria-label="กรองตามสถานะ Workflow">
        <button type="button" className={!statusFilter ? "active" : ""} onClick={() => setStatusFilter("")} aria-pressed={!statusFilter}><span className="status-dot all" />ทั้งหมด <b>{scopedItems.length}</b></button>
        {statusOptions.map((item) => <button type="button" key={item} className={statusFilter === item ? `active status-${item.toLowerCase()}` : `status-${item.toLowerCase()}`} onClick={() => setStatusFilter((value) => value === item ? "" : item)} aria-pressed={statusFilter === item}><span className="status-dot" />{item} <b>{countBy("status", item)}</b></button>)}
      </div>
      <div className="filter-toolbar requirement-filter-toolbar">
        <div className="filter-toolbar-top">
          <div className="requirement-filter-title"><span className="material-symbols-outlined" aria-hidden="true">tune</span><div><b>ตัวกรองเพิ่มเติม</b><small>{activeFilterCount ? `กำลังใช้ ${activeFilterCount} เงื่อนไข` : "แสดงข้อมูลทุกเงื่อนไข"}</small></div></div>
          {activeFilterCount > 0 && <button type="button" className="requirement-clear-filter" onClick={clearFilters}><span className="material-symbols-outlined" aria-hidden="true">filter_alt_off</span> ล้างตัวกรอง</button>}
        </div>
        <div className="filter-toolbar-row">
          <select aria-label="กรองตาม Module" value={moduleFilter} onChange={(e) => setModuleFilter(e.target.value)}>
            <option value="">ทุก Module</option>
            {filterProjects.map((project) => {
              const options = renderModuleSelectOptions(filterModules.filter((x) => x.projectId === project.projectId && x.isActive));
              return options.length ? <optgroup key={project.projectId} label={`${project.projectCode ? `${project.projectCode} · ` : ""}${project.projectName}`}>{options}</optgroup> : null;
            })}
          </select>
          {contextProjectId && filterReleases.length > 0 && (
            <select aria-label="กรองตาม Release" value={releaseFilter} onChange={(e) => setReleaseFilter(e.target.value)}>
              <option value="">ทุก Release</option>
              {filterReleases.map((x) => <option key={x.releaseId} value={x.releaseId}>{x.releaseCode} · {x.version}</option>)}
            </select>
          )}
          <select aria-label="กรองตามขอบเขต" value={scopeFilter} onChange={(e) => setScopeFilter(e.target.value)}>
            <option value="">ทุก Scope</option><option value="true">In Scope</option><option value="false">Out of Scope</option>
          </select>
          <select aria-label="กรองตามสถานะ" value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)}>
            <option value="">ทุกสถานะ</option>
            {statusOptions.map((x) => <option key={x} value={x}>{x} ({countBy("status", x)})</option>)}
          </select>
          <select aria-label="กรองตาม Priority" value={priorityFilter} onChange={(e) => setPriorityFilter(e.target.value)}>
            <option value="">ทุก Priority</option>
            {priorityOptions.map((x) => <option key={x} value={x}>{x} ({countBy("priority", x)})</option>)}
          </select>
          <select aria-label="กรองตาม Test Coverage" value={coverageFilter} onChange={(e) => setCoverageFilter(e.target.value)}>
            <option value="">ทุก Coverage</option><option value="covered">มี Test Case</option><option value="uncovered">ยังไม่มี Test Case</option>
          </select>
        </div>
        {activeFilterCount > 0 && <div className="requirement-active-filters" aria-label="ตัวกรองที่เลือก">
          {moduleFilter && <button type="button" onClick={() => setModuleFilter("")}>Module: {moduleLookup.get(moduleFilter)?.moduleCode ?? "เลือกแล้ว"}<span aria-hidden="true">×</span></button>}
          {releaseFilter && <button type="button" onClick={() => setReleaseFilter("")}>Release: {filterReleases.find((x) => x.releaseId === releaseFilter)?.releaseCode ?? "เลือกแล้ว"}<span aria-hidden="true">×</span></button>}
          {scopeFilter && <button type="button" onClick={() => setScopeFilter("")}>{scopeFilter === "true" ? "In Scope" : "Out of Scope"}<span aria-hidden="true">×</span></button>}
          {statusFilter && <button type="button" onClick={() => setStatusFilter("")}>Status: {statusFilter}<span aria-hidden="true">×</span></button>}
          {priorityFilter && <button type="button" onClick={() => setPriorityFilter("")}>Priority: {priorityFilter}<span aria-hidden="true">×</span></button>}
          {coverageFilter && <button type="button" onClick={() => setCoverageFilter("")}>{coverageFilter === "covered" ? "มี Test Case" : "ยังไม่มี Test Case"}<span aria-hidden="true">×</span></button>}
        </div>}
      </div>
      <div className="table-wrap">
        <table className="requirement-table">
          <thead>
            <tr>
              <th>Requirement</th>
              <th>Priority / Risk</th>
              <th>Scope</th>
              <th>Status</th>
              <th>Test Coverage</th>
              <th>Revision</th>
              <th className="actions-col">จัดการ</th>
            </tr>
          </thead>
          <tbody>
            {filtered.map((x) => (
              <tr key={x.requirementId} className={(testCaseCounts[x.requirementId] ?? 0) === 0 && x.isInScope ? "needs-coverage" : ""}>
                <td data-label="Requirement" className="requirement-main-cell">
                  <div><button className="requirement-id-link" onClick={() => openDetail(x)} aria-label={`ดูรายละเอียด ${x.requirementCode}`}>{x.requirementCode}</button><strong>{x.title}</strong>
                  {(() => {
                    const module = moduleLookup.get(x.moduleId);
                    return module ? <small className="requirement-module">{module.moduleCode ? `${module.moduleCode} · ` : ""}{module.moduleName}</small> : <small className="requirement-module">-</small>;
                  })()}</div>
                </td>
                <td data-label="Priority / Risk"><div className="requirement-priority-cell">
                  <Badge
                    tone={
                      x.priority === "P0" || x.priority === "P1"
                        ? "red"
                        : "blue"
                    }
                  >
                    {x.priority}
                  </Badge>
                  <small>{x.riskLevel ?? "ไม่ระบุ Risk"}</small>
                </div></td>
                <td data-label="Scope"><span className={`requirement-scope-pill ${x.isInScope ? "in" : "out"}`}><span aria-hidden="true">{x.isInScope ? "✓" : "–"}</span>{x.isInScope ? "In Scope" : "Out of Scope"}</span></td>
                <td data-label="Status">
                  <Badge
                    tone={
                      x.status === "Approved" || x.status === "Implemented"
                        ? "green"
                        : "yellow"
                    }
                  >
                    {x.status}
                  </Badge>
                </td>
                <td data-label="Test Coverage"><span className={`requirement-coverage ${testCaseCounts[x.requirementId] ? "covered" : "missing"}`}><span className="material-symbols-outlined" aria-hidden="true">{testCaseCounts[x.requirementId] ? "check_circle" : "error"}</span><b>{testCaseCounts[x.requirementId] ?? 0}</b><small>{testCaseCounts[x.requirementId] ? "Test Cases" : "ยังไม่มี Test Case"}</small></span></td>
                <td data-label="Revision"><button type="button" className="requirement-revision-link" onClick={() => openHistory(x)}>Rev. {x.revisionNo}<span aria-hidden="true">↗</span></button></td>
                <td data-label="จัดการ" className="actions-col"><div className="row-actions">{canEdit ? <><button className="table-action icon-only" title="แก้ไข" aria-label={`แก้ไข ${x.requirementCode}`} onClick={() => openEdit(x)}><span className="material-symbols-outlined" aria-hidden="true">edit</span></button><button className="table-action danger-action icon-only" title="ลบ" aria-label={`ลบ ${x.requirementCode}`} onClick={() => remove(x)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></> : <span className="requirement-readonly">ดูอย่างเดียว</span>}</div></td>
              </tr>
            ))}
            {filtered.length === 0 && <tr><td colSpan={7}><div className="empty requirement-empty"><span className="material-symbols-outlined" aria-hidden="true">search_off</span><h3>ไม่พบ Requirement</h3><p>ลองเปลี่ยนคำค้นหาหรือล้างตัวกรองที่เลือก</p>{activeFilterCount > 0 && <button type="button" className="btn" onClick={clearFilters}>ล้างตัวกรองทั้งหมด</button>}</div></td></tr>}
          </tbody>
        </table>
      </div>
      {viewing && <ModalShell labelledBy="requirement-detail-title" className="requirement-detail-modal" onDismiss={() => setViewing(null)}>
          <div className="modal-head"><div><h2 id="requirement-detail-title">รายละเอียด Requirement</h2><small>{viewing.requirementCode}</small></div><button aria-label="ปิดหน้าต่างรายละเอียด" onClick={() => setViewing(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
          <div className="requirement-detail-title"><div className="requirement-detail-hero-copy"><span>Requirement</span><b>{viewing.requirementCode}</b><h3>{viewing.title}</h3><div className="requirement-detail-badges"><Badge tone={viewing.priority === "P0" || viewing.priority === "P1" ? "red" : "blue"}>{viewing.priority}</Badge><Badge tone={viewing.status === "Approved" || viewing.status === "Implemented" ? "green" : "yellow"}>{viewing.status}</Badge></div></div><div className={`requirement-scope-card ${viewing.isInScope ? "in-scope" : "out-scope"}`}><span aria-hidden="true">{viewing.isInScope ? "✓" : "–"}</span><div><small>Release Scope</small><b>{viewing.isInScope ? "In Scope" : "Out of Scope"}</b></div></div></div>
          <dl className="requirement-detail-grid requirement-detail-meta">
            <div><span className="requirement-meta-icon" aria-hidden="true">P</span><span><dt>Project</dt><dd>{filterProjects.find((x) => x.projectId === viewing.projectId)?.projectName ?? "-"}</dd></span></div>
            <div><span className="requirement-meta-icon" aria-hidden="true">M</span><span><dt>Module</dt><dd>{filterModules.find((x) => x.moduleId === viewing.moduleId)?.moduleName ?? "-"}</dd></span></div>
            <div><span className="requirement-meta-icon" aria-hidden="true">R</span><span><dt>Release</dt><dd>{viewRelease ? `${viewRelease.releaseCode} · Version ${viewRelease.version}` : viewing.releaseId ? "กำลังโหลด..." : "ไม่ระบุ Release"}</dd></span></div>
            <div><span className="requirement-meta-icon" aria-hidden="true">#</span><span><dt>Revision</dt><dd>Rev. {viewing.revisionNo}</dd></span></div>
            <div><span className="requirement-meta-icon risk" aria-hidden="true">!</span><span><dt>Risk</dt><dd>{viewing.riskLevel || "ไม่ระบุ"}</dd></span></div>
            <div><span className="requirement-meta-icon" aria-hidden="true">O</span><span><dt>Owner</dt><dd>{users.find((x) => x.userId === viewing.ownerUserId)?.displayName ?? "ไม่ระบุผู้รับผิดชอบ"}</dd></span></div>
          </dl>
          <section className="requirement-detail-section"><div className="requirement-section-heading"><span aria-hidden="true">S</span><h3>Source</h3></div><p className="requirement-detail-copy">{viewing.source || "ไม่ระบุแหล่งที่มา"}</p></section>
          <section className="requirement-detail-section"><div className="requirement-section-heading"><span aria-hidden="true">D</span><h3>Description</h3></div><p className="requirement-detail-copy">{viewing.description || "ไม่มีรายละเอียด"}</p></section>
          <section className="requirement-detail-section criteria"><div className="requirement-section-heading"><span className="material-symbols-outlined" aria-hidden="true">check</span><h3>Acceptance Criteria</h3></div><p className="requirement-detail-copy requirement-criteria-copy"><span className="material-symbols-outlined" aria-hidden="true">check</span> {viewing.acceptanceCriteria || "ไม่มี Acceptance Criteria"}</p></section>
          <section className={`requirement-detail-status status-${viewing.status.toLowerCase()}`}>
            <div className="requirement-section-heading"><span className="information-icon" aria-hidden="true">i</span><h3>Current Status / Summary</h3></div>
            <b>{viewing.status} · {requirementStatusInformation.find((x) => x.value === viewing.status)?.label}</b>
            <p>{requirementStatusInformation.find((x) => x.value === viewing.status)?.meaning}</p>
            <small>{requirementStatusInformation.find((x) => x.value === viewing.status)?.impact}</small>
          </section>
          <div className="modal-actions"><button className="btn" onClick={() => setViewing(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ปิด</button>{canEdit && <button className="btn primary" onClick={() => { const item = viewing; setViewing(null); openEdit(item); }}><span className="material-symbols-outlined" aria-hidden="true">edit</span> แก้ไข Requirement</button>}</div>
        </ModalShell>}
      {editing && (
        <ModalShell className="requirement-editor" onDismiss={() => setEditing(null)}>
            <div className="modal-head"><h2>แก้ไข Requirement</h2><button onClick={() => setEditing(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
            <div className="form-grid">
              <label>Requirement Code<input value={editing.requirementCode} disabled /></label>
              <label>Module<select value={moduleId} onChange={(e) => setModuleId(e.target.value)}>{renderModuleSelectOptions(modules.filter((x) => x.isActive))}</select></label>
              <label>Release<select value={releaseId} onChange={(e) => setReleaseId(e.target.value)}><option value="">ไม่ระบุ Release</option>{releases.map((x) => <option key={x.releaseId} value={x.releaseId}>{x.releaseCode} · Version {x.version}</option>)}</select></label>
              <label>Title<input value={title} onChange={(e) => setTitle(e.target.value)} /></label>
              <label>Priority<select value={priority} onChange={(e) => setPriority(e.target.value)}>{["P0","P1","P2","P3"].map((x) => <option key={x}>{x}</option>)}</select></label>
              <label>Risk<select value={risk} onChange={(e) => setRisk(e.target.value)}>{["Critical","High","Medium","Low"].map((x) => <option key={x}>{x}</option>)}</select></label>
              <label>Status<select value={status} onChange={(e) => setStatus(e.target.value)}>{["Draft","Review","Approved","Implemented","Cancelled"].map((x) => <option key={x}>{x}</option>)}</select></label>
              <label>Source<input value={source} onChange={(e) => setSource(e.target.value)} /></label>
              <label>Owner<select value={ownerUserId} onChange={(e) => setOwnerUserId(e.target.value)}><option value="">ไม่ระบุผู้รับผิดชอบ</option>{users.map((x) => <option key={x.userId} value={x.userId}>{x.displayName}</option>)}</select></label>
              <label className="check-line"><input type="checkbox" checked={inScope} onChange={(e) => setInScope(e.target.checked)} /> In Scope</label>
              <details className="requirement-status-information full">
                <summary>
                  <span className="information-icon" aria-hidden="true">i</span>
                  <span><b>{status} · {requirementStatusInformation.find((x) => x.value === status)?.label}</b><small>{requirementStatusInformation.find((x) => x.value === status)?.meaning}</small></span>
                  <em>ดูความหมายทั้งหมด</em>
                </summary>
                <div className="requirement-status-list">
                  {requirementStatusInformation.map((item) => <article key={item.value} className={status === item.value ? "active" : ""}>
                    <div><b>{item.value}</b><span>{item.label}</span></div>
                    <p>{item.meaning}</p>
                    <small><strong>ผลต่อการใช้งาน:</strong> {item.impact}</small>
                  </article>)}
                  <p className="status-scope-note"><b>หมายเหตุ:</b> Status ใช้บอกขั้นตอนการทำงานและใช้กรองรายการ ส่วนการนำ Requirement ไปคำนวณ RTM และ Coverage พิจารณาจากช่อง <b>In Scope</b></p>
                </div>
              </details>
              <label className="full">Description<textarea rows={3} value={description} onChange={(e) => setDescription(e.target.value)} /></label>
              <label className="full">Acceptance Criteria<textarea rows={3} value={criteria} onChange={(e) => setCriteria(e.target.value)} /></label>
            </div>
            <div className="modal-actions"><button className="btn" onClick={() => setEditing(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิก</button><button className="btn primary" disabled={saving || !title.trim() || !moduleId} onClick={saveEdit}>{saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">check</span> บันทึก</>}</button></div>
          </ModalShell>
      )}
      {historyItem && <ModalShell className="requirement-history" onDismiss={() => setHistoryItem(null)}><div className="modal-head"><div><h2>Revision History</h2><small>{historyItem.requirementCode} · {historyItem.title}</small></div><button onClick={() => setHistoryItem(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>{historyLoading ? <div className="empty" role="status"><p>กำลังโหลดประวัติ...</p></div> : historyError ? <div className="inline-alert error" role="alert"><span>{historyError}</span></div> : <div className="revision-list">{revisions.length === 0 ? <div className="empty"><p>ยังไม่มีประวัติ Revision</p></div> : revisions.map((x) => <article key={x.revisionNo}><div><b>Rev. {x.revisionNo}</b><time>{formatThaiDateTime(x.changedAt)}</time></div><h3>{x.title}</h3><p>{x.changeReason || "ไม่ระบุเหตุผลการเปลี่ยนแปลง"}</p>{x.acceptanceCriteria && <small>Acceptance Criteria: {x.acceptanceCriteria}</small>}</article>)}</div>}<div className="modal-actions"><button className="btn primary" onClick={() => setHistoryItem(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ปิด</button></div></ModalShell>}
    </article>
    </div>
  );
}
