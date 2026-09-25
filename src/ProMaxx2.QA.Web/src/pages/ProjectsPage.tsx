import { useState, useMemo, useEffect } from "react";
import type { ProjectItem } from "../shared/types";
import { apiUrl, getJson, isAbortError } from "../api";
import { notify, confirmDialog } from "../components/dialogStore";
import { Badge } from "../components/Badge";
import { ModalShell } from "../components/ModalShell";
import { type ModuleItem, type SessionUser, nextBusinessCode } from "../shared/appShared";

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
      .then((data) => setModules(data.filter((x) => x.isActive)))
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
  if (loading)
    return (
      <article className="card empty">
        <div className="spinner" />
        <p>กำลังโหลดข้อมูลโครงการ...</p>
      </article>
    );
  if (error)
    return (
      <article className="card empty">
        <div className="login-error">{error}</div>
      </article>
    );
  return (
    <div className="project-layout">
      <article className="card project-list">
        <div className="card-title">
          <div>
            <h3>Projects</h3>
            <p>{filteredProjects.length} โครงการ · คลิกเพื่อดู Module</p>
          </div>
          {canEdit && (
            <button className="btn primary" onClick={() => openProject()}>
              + Project
            </button>
          )}
        </div>
        <div className="project-cards">
          {filteredProjects.map((x) => (
            <button
              key={x.projectId}
              className={selectedId === x.projectId ? "active" : ""}
              onClick={() => setSelectedId(x.projectId)}
            >
              <span className="project-code">{x.projectCode}</span>
              <b>{x.projectName}</b>
              <small>{x.description || "ไม่มีรายละเอียด"}</small>
              <Badge tone={x.isActive ? "green" : "red"}>
                {x.isActive ? "ใช้งาน" : "ปิดใช้งาน"}
              </Badge>
            </button>
          ))}
        </div>
      </article>
      <article className="card module-panel">
        <div className="card-title">
          <div>
            <h3>Modules {selected && <span>· {selected.projectCode}</span>}</h3>
            <p>
              {modules.length} Module ในโครงการที่เลือก{" "}
              {canEdit && "· ลากเพื่อจัดลำดับหรือวางซ้อนเป็น Module ลูก"}
            </p>
          </div>
          {canEdit && selected?.isActive && (
            <div className="row-actions">
              <button className="btn" onClick={() => openProject(selected)}>
                แก้ไข Project
              </button>
              <button className="btn primary" onClick={() => openModule()}>
                + Module
              </button>
            </div>
          )}
        </div>
        {selected ? (
          <div className="table-wrap">
            <table className="module-tree">
              <thead>
                <tr>
                  <th>Module</th>
                  <th>Description</th>
                  <th>Status</th>
                  {canEdit && <th className="actions-col">จัดการ</th>}
                </tr>
              </thead>
              <tbody>
                {visibleModules.map(({ item: x, level, childCount }) => {
                  const hint = dropHint.startsWith(`${x.moduleId}:`)
                    ? dropHint.split(":")[1]
                    : "";
                  return (
                    <tr
                      key={x.moduleId}
                      draggable={canEdit}
                      className={`${level ? "child-row " : ""}${draggingId === x.moduleId ? "dragging " : ""}${hint ? `drop-${hint}` : ""}`}
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
                          position =
                            ratio < 0.28
                              ? "before"
                              : ratio > 0.72
                                ? "after"
                                : "inside";
                        setDropHint(`${x.moduleId}:${position}`);
                      }}
                      onDrop={(e) => {
                        e.preventDefault();
                        const position = (dropHint.split(":")[1] ||
                          "inside") as "before" | "inside" | "after";
                        moveModule(x, position);
                      }}
                    >
                      <td>
                        <div
                          className="tree-module"
                          style={{ paddingLeft: `${level * 24}px` }}
                        >
                          {canEdit && (
                            <span
                              className="drag-handle"
                              title="ลากเพื่อย้ายตำแหน่ง"
                            >
                              ⋮⋮
                            </span>
                          )}
                          {childCount ? (
                            <button
                              className="tree-toggle"
                              onClick={() =>
                                setExpanded((current) =>
                                  current.includes(x.moduleId)
                                    ? current.filter((id) => id !== x.moduleId)
                                    : [...current, x.moduleId],
                                )
                              }
                              aria-label={
                                expanded.includes(x.moduleId)
                                  ? "ย่อ Module"
                                  : "ขยาย Module"
                              }
                            >
                              {term || expanded.includes(x.moduleId)
                                ? "▾"
                                : "▸"}
                            </button>
                          ) : (
                            <span className="tree-spacer" />
                          )}
                          <span>
                            <b>{x.moduleName}</b>
                          </span>
                          {childCount > 0 && (
                            <span className="child-count">
                              {childCount} Module
                            </span>
                          )}
                        </div>
                      </td>
                      <td>{x.description || "-"}</td>
                      <td>
                        <Badge tone="green">ใช้งาน</Badge>
                      </td>
                      {canEdit && (
                        <td className="actions-col">
                          <div className="row-actions">
                            <button
                              className="table-action icon-only"
                              title="แก้ไข"
                              aria-label={`แก้ไข ${x.moduleName}`}
                              onClick={() => openModule(x)}
                            >
                              <span className="material-symbols-outlined" aria-hidden="true">edit</span>
                            </button>
                            <button
                              className="table-action danger-action icon-only"
                              title="ลบ"
                              aria-label={`ลบ ${x.moduleName}`}
                              onClick={() =>
                                deactivate("module", x.moduleId, x.moduleName)
                              }
                            >
                              <span className="material-symbols-outlined" aria-hidden="true">close</span>
                            </button>
                          </div>
                        </td>
                      )}
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        ) : (
          <div className="empty">
            <p>เลือก Project เพื่อดู Modules</p>
          </div>
        )}
        {canEdit && selected?.isActive && (
          <div className="project-footer">
            <button
              className="danger-link"
              onClick={() =>
                deactivate("project", selected.projectId, selected.projectName)
              }
            >
              ปิดใช้งาน Project นี้
            </button>
          </div>
        )}
      </article>
      {modal && (
        <ModalShell onDismiss={() => setModal(null)}>
            <div className="modal-head">
              <h2>
                {editProject || editModule ? "แก้ไข" : "เพิ่ม"}{" "}
                {modal === "project" ? "Project" : "Module"}
              </h2>
              <button onClick={() => setModal(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
            </div>
            <div className="form-grid">
              <label>
                {modal === "project" ? "Project Code" : "Module Code"}
                <input
                  disabled
                  value={code}
                  onChange={(e) => setCode(e.target.value)}
                  placeholder="ระบบสร้างรหัสอัตโนมัติ"
                />
              </label>
              <label>
                ชื่อ
                <input
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  placeholder="ระบุชื่อ"
                />
              </label>
              {modal === "module" && (
                <label className="full">
                  Parent Module
                  <select
                    value={parentId}
                    onChange={(e) => setParentId(e.target.value)}
                  >
                    <option value="">ไม่มี Parent</option>
                    {modules
                      .filter(
                        (x) =>
                          x.moduleId !== editModule?.moduleId && x.isActive,
                      )
                      .map((x) => (
                        <option key={x.moduleId} value={x.moduleId}>
                          {x.moduleCode} · {x.moduleName}
                        </option>
                      ))}
                  </select>
                </label>
              )}
              <label className="full">
                รายละเอียด
                <textarea
                  rows={4}
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                />
              </label>
            </div>
            <div className="modal-actions">
              <button className="btn" onClick={() => setModal(null)}>
                ยกเลิก
              </button>
              <button
                className="btn primary"
                disabled={saving || !code.trim() || !name.trim()}
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
