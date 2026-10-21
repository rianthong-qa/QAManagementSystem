import { useState, useMemo, useEffect } from "react";
import type { ProjectItem } from "../shared/types";
import { apiUrl, getJson, isAbortError } from "../api";
import { notify, confirmDialog } from "../components/dialogStore";
import { ModalShell } from "../components/ModalShell";
import { type ModuleItem, type SessionUser, buildModuleTree, nextBusinessCode, renderModuleSelectOptions } from "../shared/appShared";
import { formatThaiDateTime } from "../dateTime";
import "./ProjectsPage.css";

/** จำนวน Module สูงสุดที่ขยายทุกชั้นให้อัตโนมัติตอนเปิด Project (Project จริงมี 70+ Module ทำให้หน้ายาวหลายพัน px) */
const autoExpandLimit = 30;

export function ProjectsPage({ search }: { search: string; refresh?: number }) {
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
  const [items, setItems] = useState<ProjectItem[]>([]),
    [modules, setModules] = useState<ModuleItem[]>([]),
    [selectedId, setSelectedId] = useState(""),
    [loading, setLoading] = useState(true),
    [error, setError] = useState(""),
    [reload, setReload] = useState(0),
    [modal, setModal] = useState<"project" | "module" | null>(null),
    [editProject, setEditProject] = useState<ProjectItem | null>(null),
    [editModule, setEditModule] = useState<ModuleItem | null>(null),
    [code, setCode] = useState(""),
    [name, setName] = useState(""),
    [description, setDescription] = useState(""),
    [parentId, setParentId] = useState(""),
    [saving, setSaving] = useState(false),
    [expanded, setExpanded] = useState<string[]>([]),
    [draggingId, setDraggingId] = useState(""),
    [dropHint, setDropHint] = useState("");
  const headers = useMemo(() => ({
    "Content-Type": "application/json",
    Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
  }), []);
  useEffect(() => {
    setLoading(true);
    const h = {
      Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
    };
    fetch(`${apiUrl}/projects`, { headers: h })
      .then(async (r) => {
        if (!r.ok)
          throw new Error(
            r.status === 401
              ? "Session หมดอายุ กรุณาเข้าสู่ระบบใหม่"
              : "โหลดข้อมูลโครงการไม่สำเร็จ",
          );
        return r.json();
      })
      .then((data: ProjectItem[]) => {
        const activeProjects = data.filter((x) => x.isActive);
        setItems(activeProjects);
        setSelectedId((current) =>
          activeProjects.some((x) => x.projectId === current)
            ? current
            : (activeProjects[0]?.projectId ?? ""),
        );
      })
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [reload]);
  useEffect(() => {
    if (!selectedId) {
      setModules([]);
      return;
    }
    // เดิมไม่ตรวจ r.ok — API ตอบ error แล้ว data.filter พัง; AbortController กันผลของ Project ก่อนหน้ามาทับ
    const ctrl = new AbortController();
    getJson<ModuleItem[]>(`${apiUrl}/projects/${selectedId}/modules`, ctrl.signal)
      .then((data) => {
        const active = data.filter((x) => x.isActive);
        setModules(active);
        // เปิด Project ที่ Module ไม่เกิน autoExpandLimit ให้เห็นโครงสร้างทั้งหมด; ใหญ่กว่านั้นแสดงเฉพาะ Module หลัก (ใช้ปุ่มขยายทั้งหมดเอง)
        // หลังบันทึก/ย้ายยังคงสถานะเดิม
        if (active.length <= autoExpandLimit) setExpanded((current) => current.length ? current : [...new Set(active.map((x) => x.parentModuleId).filter(Boolean) as string[])]);
      })
      .catch((e) => { if (!isAbortError(e)) { setModules([]); notify(`โหลด Module ของ Project ไม่สำเร็จ (${e instanceof Error ? e.message : "ไม่ทราบสาเหตุ"})`, "error"); } });
    return () => ctrl.abort();
  }, [selectedId, reload]);
  const selected = items.find((x) => x.projectId === selectedId),
    term = search.toLowerCase();
  const filteredProjects = items.filter((x) =>
    `${x.projectCode} ${x.projectName} ${x.description ?? ""}`
      .toLowerCase()
      .includes(term),
  );
  const includedIds = new Set(
    modules
      .filter(
        (x) =>
          !term ||
          `${x.moduleCode} ${x.moduleName} ${x.description ?? ""}`
            .toLowerCase()
            .includes(term),
      )
      .map((x) => x.moduleId),
  );
  if (term) {
    for (const module of modules.filter((x) => includedIds.has(x.moduleId))) {
      let parent = modules.find((x) => x.moduleId === module.parentModuleId);
      while (parent) {
        includedIds.add(parent.moduleId);
        parent = modules.find((x) => x.moduleId === parent?.parentModuleId);
      }
    }
  }
  const visibleModules: {
    item: ModuleItem;
    level: number;
    childCount: number;
  }[] = [];
  const visited = new Set<string>();
  const appendModules = (parent: string | null | undefined, level: number) => {
    for (const item of modules
      .filter((x) => x.parentModuleId === parent && includedIds.has(x.moduleId))
      .sort(
        (a, b) =>
          a.sortOrder - b.sortOrder || a.moduleCode.localeCompare(b.moduleCode),
      )) {
      if (visited.has(item.moduleId)) continue;
      visited.add(item.moduleId);
      const children = modules.filter(
        (x) =>
          x.parentModuleId === item.moduleId && includedIds.has(x.moduleId),
      );
      visibleModules.push({ item, level, childCount: children.length });
      if (term || expanded.includes(item.moduleId))
        appendModules(item.moduleId, level + 1);
    }
  };
  appendModules(null, 0);
  appendModules(undefined, 0);
  for (const orphan of modules.filter(
    (x) =>
      includedIds.has(x.moduleId) &&
      !visited.has(x.moduleId) &&
      !modules.some((parent) => parent.moduleId === x.parentModuleId),
  ))
    appendModules(orphan.parentModuleId, 0);
  const openProject = (item?: ProjectItem) => {
    setEditProject(item ?? null);
    setEditModule(null);
    setCode(
      item?.projectCode ??
        nextBusinessCode("PRJ", items.map((x) => x.projectCode)),
    );
    setName(item?.projectName ?? "");
    setDescription(item?.description ?? "");
    setModal("project");
  };
  const openModule = (item?: ModuleItem) => {
    setEditModule(item ?? null);
    setEditProject(null);
    setCode(
      item?.moduleCode ??
        nextBusinessCode(
          `${selected?.projectCode ?? "PRJ"}-MOD`,
          modules.map((x) => x.moduleCode),
        ),
    );
    setName(item?.moduleName ?? "");
    setDescription(item?.description ?? "");
    setParentId(item?.parentModuleId ?? "");
    setModal("module");
  };
  const save = async () => {
    if (!modal) return;
    setSaving(true);
    try {
      let url = "",
        method = "POST",
        body: object;
      if (modal === "project") {
        url = editProject
          ? `${apiUrl}/projects/${editProject.projectId}`
          : `${apiUrl}/projects`;
        method = editProject ? "PUT" : "POST";
        body = editProject
          ? {
              projectName: name,
              description: description || null,
              ownerUserId: null,
            }
          : {
              projectCode: "",
              projectName: name,
              description: description || null,
              ownerUserId: null,
            };
      } else {
        url = editModule
          ? `${apiUrl}/modules/${editModule.moduleId}`
          : `${apiUrl}/projects/${selectedId}/modules`;
        method = editModule ? "PUT" : "POST";
        body = editModule
          ? {
              moduleName: name,
              parentModuleId: parentId || null,
              description: description || null,
              ownerUserId: null,
            }
          : {
              moduleCode: "",
              moduleName: name,
              parentModuleId: parentId || null,
              description: description || null,
              ownerUserId: null,
            };
      }
      const response = await fetch(url, {
        method,
        headers,
        body: JSON.stringify(body),
      });
      if (!response.ok) {
        const p = await response.json();
        throw new Error(p.detail ?? "บันทึกข้อมูลไม่สำเร็จ");
      }
      setModal(null);
      setReload((x) => x + 1);
    } catch (e) {
      notify(e instanceof Error ? e.message : "บันทึกข้อมูลไม่สำเร็จ", "error");
    } finally {
      setSaving(false);
    }
  };
  const deactivate = async (
    kind: "project" | "module",
    id: string,
    label: string,
  ) => {
    if (!await confirmDialog(`ยืนยันปิดใช้งาน ${label}?`)) return;
    const response = await fetch(
      `${apiUrl}/${kind === "project" ? "projects" : "modules"}/${id}`,
      { method: "DELETE", headers },
    );
    if (!response.ok) {
      notify("ไม่สามารถปิดใช้งานข้อมูลได้", "error");
      return;
    }
    setReload((x) => x + 1);
  };
  const moveModule = async (
    target: ModuleItem,
    position: "before" | "inside" | "after",
  ) => {
    if (!draggingId || draggingId === target.moduleId) return;
    const dragged = modules.find((x) => x.moduleId === draggingId);
    if (!dragged) return;
    const parentModuleId =
      position === "inside" ? target.moduleId : (target.parentModuleId ?? null);
    const siblings = modules
      .filter(
        (x) =>
          x.moduleId !== dragged.moduleId &&
          (x.parentModuleId ?? null) === parentModuleId,
      )
      .sort(
        (a, b) =>
          a.sortOrder - b.sortOrder || a.moduleCode.localeCompare(b.moduleCode),
      );
    const targetIndex =
      position === "inside"
        ? siblings.length
        : Math.max(
            0,
            siblings.findIndex((x) => x.moduleId === target.moduleId) +
              (position === "after" ? 1 : 0),
          );
    const response = await fetch(`${apiUrl}/modules/${dragged.moduleId}/move`, {
      method: "POST",
      headers,
      body: JSON.stringify({ parentModuleId, sortOrder: targetIndex }),
    });
    setDraggingId("");
    setDropHint("");
    if (!response.ok) {
      const problem = await response.json();
      notify(problem.detail ?? "ไม่สามารถย้าย Module ได้", "error");
      return;
    }
    if (parentModuleId)
      setExpanded((current) =>
        current.includes(parentModuleId)
          ? current
          : [...current, parentModuleId],
      );
    setReload((x) => x + 1);
  };
  const parentIds = new Set(modules.map((x) => x.parentModuleId).filter(Boolean) as string[]);
  const rootCount = modules.filter((x) => !x.parentModuleId || !modules.some((p) => p.moduleId === x.parentModuleId)).length;
  const maxDepth = buildModuleTree(modules).reduce((max, x) => Math.max(max, x.depth + 1), 0);
  // Module ที่เลือกเป็น Parent ไม่ได้: ตัวเอง และลูกหลานทั้งหมด (กันวนลูป)
  const blockedParents = new Set<string>();
  if (editModule) {
    const stack = [editModule.moduleId];
    while (stack.length) {
      const id = stack.pop()!;
      blockedParents.add(id);
      modules.filter((x) => x.parentModuleId === id).forEach((x) => stack.push(x.moduleId));
    }
  }
  if (loading)
    return (
      <article className="card empty" role="status">
        <div className="spinner" />
        <p>กำลังโหลดข้อมูลโครงการ...</p>
      </article>
    );
  if (error)
    return (
      <article className="card empty">
        <div className="login-error" role="alert">{error}</div>
      </article>
    );
  return (
    <div className="pm-layout">
      <aside className="card pm-projects" aria-label="รายการ Project">
        <div className="pm-projects-head">
          <div>
            <h3>Project</h3>
            <p>{filteredProjects.length.toLocaleString("th-TH")} โครงการ{term ? " ตรงกับคำค้น" : ""}</p>
          </div>
          {canEdit && (
            <button className="btn primary" type="button" onClick={() => openProject()}>
              <span className="material-symbols-outlined" aria-hidden="true">add</span>Project
            </button>
          )}
        </div>
        {filteredProjects.length === 0 ? (
          <p className="pm-empty-small">{items.length ? "ไม่พบ Project ตามคำค้น" : "ยังไม่มี Project"}</p>
        ) : (
          <ul className="pm-project-list">
            {filteredProjects.map((x) => (
              <li key={x.projectId}>
                <button type="button" className={selectedId === x.projectId ? "active" : ""} aria-pressed={selectedId === x.projectId} onClick={() => { if (x.projectId !== selectedId) { setExpanded([]); setSelectedId(x.projectId); } }}>
                  <span className="pm-project-mark" aria-hidden="true">{x.projectCode.slice(0, 2).toUpperCase()}</span>
                  <span className="pm-project-text">
                    <b>{x.projectName}</b>
                    <small>{x.projectCode}{x.description ? ` · ${x.description}` : ""}</small>
                  </span>
                  <span className="material-symbols-outlined pm-project-chevron" aria-hidden="true">chevron_right</span>
                </button>
              </li>
            ))}
          </ul>
        )}
      </aside>

      <section className="pm-main" aria-label="รายละเอียด Project และ Module">
        {selected ? (
          <>
            <div className="card pm-project-head">
              <div className="pm-project-title">
                <span className="pm-eyebrow">{selected.projectCode}</span>
                <h2>{selected.projectName}</h2>
                <p>{selected.description || "ไม่มีรายละเอียด"}</p>
              </div>
              {canEdit && selected.isActive && (
                <div className="pm-project-actions">
                  <button className="btn" type="button" onClick={() => openProject(selected)}>
                    <span className="material-symbols-outlined" aria-hidden="true">edit</span>แก้ไข Project
                  </button>
                  <button className="btn pm-danger" type="button" onClick={() => deactivate("project", selected.projectId, selected.projectName)}>
                    <span className="material-symbols-outlined" aria-hidden="true">block</span>ปิดใช้งาน
                  </button>
                </div>
              )}
              <dl className="pm-stats">
                <div><dt>Module ทั้งหมด</dt><dd>{modules.length.toLocaleString("th-TH")}</dd></div>
                <div><dt>Module หลัก</dt><dd>{rootCount.toLocaleString("th-TH")}</dd></div>
                <div><dt>จำนวนชั้น</dt><dd>{maxDepth.toLocaleString("th-TH")}</dd></div>
                <div><dt>สร้างเมื่อ</dt><dd className="pm-stat-text">{formatThaiDateTime(selected.createdAt, { dateStyle: "medium" })}</dd></div>
              </dl>
            </div>

            <div className="card pm-modules">
              <div className="pm-modules-head">
                <div>
                  <h3><span className="material-symbols-outlined" aria-hidden="true">account_tree</span>Module</h3>
                  <p>{term ? `แสดง ${visibleModules.length.toLocaleString("th-TH")} รายการที่ตรงกับคำค้น (รวม Parent)` : canEdit ? "ลาก ⠿ เพื่อเรียงลำดับ หรือวางกลางแถวเพื่อย้ายเป็น Module ลูก" : "โครงสร้าง Module ของ Project นี้"}</p>
                </div>
                <div className="pm-modules-actions">
                  {parentIds.size > 0 && !term && (
                    <>
                      <button className="btn" type="button" onClick={() => setExpanded([...parentIds])}><span className="material-symbols-outlined" aria-hidden="true">unfold_more</span>ขยายทั้งหมด</button>
                      <button className="btn" type="button" onClick={() => setExpanded([])}><span className="material-symbols-outlined" aria-hidden="true">unfold_less</span>ย่อทั้งหมด</button>
                    </>
                  )}
                  {canEdit && selected.isActive && (
                    <button className="btn primary" type="button" onClick={() => openModule()}>
                      <span className="material-symbols-outlined" aria-hidden="true">add</span>Module
                    </button>
                  )}
                </div>
              </div>
              {visibleModules.length === 0 ? (
                <div className="pm-empty">
                  <span className="material-symbols-outlined" aria-hidden="true">{term ? "search_off" : "account_tree"}</span>
                  <b>{term ? "ไม่พบ Module ตามคำค้น" : "ยังไม่มี Module"}</b>
                  {!term && canEdit && selected.isActive && <button className="btn primary" type="button" onClick={() => openModule()}><span className="material-symbols-outlined" aria-hidden="true">add</span>เพิ่ม Module แรก</button>}
                </div>
              ) : (
                <ul className="pm-tree" role="tree" aria-label={`Module ของ ${selected.projectName}`}>
                  {visibleModules.map(({ item: x, level, childCount }) => {
                    const hint = dropHint.startsWith(`${x.moduleId}:`) ? dropHint.split(":")[1] : "";
                    const open = !!term || expanded.includes(x.moduleId);
                    return (
                      <li
                        key={x.moduleId}
                        role="treeitem"
                        aria-level={level + 1}
                        aria-expanded={childCount ? open : undefined}
                        draggable={canEdit}
                        className={`pm-node${level === 0 ? " root" : ""}${draggingId === x.moduleId ? " dragging" : ""}${hint ? ` drop-${hint}` : ""}`}
                        onDragStart={(e) => {
                          setDraggingId(x.moduleId);
                          e.dataTransfer.effectAllowed = "move";
                        }}
                        onDragEnd={() => {
                          setDraggingId("");
                          setDropHint("");
                        }}
                        onDragOver={(e) => {
                          if (!canEdit || draggingId === x.moduleId) return;
                          e.preventDefault();
                          const box = e.currentTarget.getBoundingClientRect(),
                            ratio = (e.clientY - box.top) / box.height,
                            position = ratio < 0.28 ? "before" : ratio > 0.72 ? "after" : "inside";
                          setDropHint(`${x.moduleId}:${position}`);
                        }}
                        onDrop={(e) => {
                          e.preventDefault();
                          const position = (dropHint.split(":")[1] || "inside") as "before" | "inside" | "after";
                          moveModule(x, position);
                        }}
                      >
                        {canEdit && <span className="material-symbols-outlined pm-drag" title="ลากเพื่อย้ายตำแหน่ง" aria-hidden="true">drag_indicator</span>}
                        <span className="pm-indent" style={{ width: `${level * 22}px` }} aria-hidden="true" />
                        {childCount ? (
                          <button
                            type="button"
                            className={`pm-toggle${open ? " open" : ""}`}
                            disabled={!!term}
                            onClick={() => setExpanded((current) => current.includes(x.moduleId) ? current.filter((id) => id !== x.moduleId) : [...current, x.moduleId])}
                            aria-label={`${open ? "ย่อ" : "ขยาย"} ${x.moduleName}`}
                          >
                            <span className="material-symbols-outlined" aria-hidden="true">chevron_right</span>
                          </button>
                        ) : <span className="pm-toggle-spacer" aria-hidden="true" />}
                        <span className="material-symbols-outlined pm-node-icon" aria-hidden="true">{childCount ? (open ? "folder_open" : "folder") : "description"}</span>
                        <span className="pm-node-text">
                          <span className="pm-node-name"><b>{x.moduleName}</b><code>{x.moduleCode}</code>{childCount > 0 && <span className="pm-node-count">{childCount} ย่อย</span>}</span>
                          {x.description && <small>{x.description}</small>}
                        </span>
                        {canEdit && (
                          <span className="pm-node-actions">
                            <button type="button" title="แก้ไข" aria-label={`แก้ไข ${x.moduleName}`} onClick={() => openModule(x)}>
                              <span className="material-symbols-outlined" aria-hidden="true">edit</span>
                            </button>
                            <button type="button" className="danger" title="ปิดใช้งาน" aria-label={`ปิดใช้งาน ${x.moduleName}`} onClick={() => deactivate("module", x.moduleId, x.moduleName)}>
                              <span className="material-symbols-outlined" aria-hidden="true">block</span>
                            </button>
                          </span>
                        )}
                      </li>
                    );
                  })}
                </ul>
              )}
            </div>
          </>
        ) : (
          <div className="card pm-empty">
            <span className="material-symbols-outlined" aria-hidden="true">folder_off</span>
            <b>เลือก Project เพื่อดู Module</b>
          </div>
        )}
      </section>

      {modal && (
        <ModalShell onDismiss={() => setModal(null)} labelledBy="pm-modal-title" className="pm-modal">
          <div className="modal-head">
            <div>
              <h2 id="pm-modal-title">{editProject || editModule ? "แก้ไข" : "เพิ่ม"} {modal === "project" ? "Project" : "Module"}</h2>
              <small className="pm-modal-sub">
                {modal === "project" ? (editProject ? editProject.projectCode : "ระบบสร้างรหัส Project ให้อัตโนมัติ") : `${selected?.projectCode ?? ""} · ${editModule ? editModule.moduleCode : "ระบบสร้างรหัส Module ให้อัตโนมัติ"}`}
              </small>
            </div>
            <button type="button" aria-label="ปิด" onClick={() => setModal(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
          </div>
          <div className="form-grid">
            <label className="full">
              <span>ชื่อ {modal === "project" ? "Project" : "Module"} <span className="required-mark">*</span></span>
              <input value={name} onChange={(e) => setName(e.target.value)} placeholder="ระบุชื่อ" />
            </label>
            {modal === "module" && (
              <label className="full">
                Parent Module
                <select value={parentId} onChange={(e) => setParentId(e.target.value)}>
                  <option value="">ไม่มี (เป็น Module หลัก)</option>
                  {renderModuleSelectOptions(modules.filter((x) => x.isActive && !blockedParents.has(x.moduleId)))}
                </select>
                <small>Module ตัวเองและ Module ลูกของตัวเองเลือกเป็น Parent ไม่ได้</small>
              </label>
            )}
            <label className="full">
              รายละเอียด
              <textarea rows={4} value={description} onChange={(e) => setDescription(e.target.value)} />
            </label>
          </div>
          <div className="modal-actions">
            <button className="btn" type="button" onClick={() => setModal(null)}>ยกเลิก</button>
            <button className="btn primary" type="button" disabled={saving || !code.trim() || !name.trim()} onClick={save}>
              {saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">check</span> บันทึก</>}
            </button>
          </div>
        </ModalShell>
      )}
    </div>
  );
}
