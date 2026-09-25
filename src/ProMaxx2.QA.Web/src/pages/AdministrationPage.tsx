import type { ProjectItem } from "../shared/types";
import { useState, useRef, useEffect, Fragment as _F } from "react";
import { apiUrl } from "../api";
import { notify, confirmDialog } from "../components/dialogStore";
import { Badge } from "../components/Badge";
import { formatThaiDateTime } from "../dateTime";
import { ModalShell } from "../components/ModalShell";
import { type AdminUser } from "../shared/appShared";

type AdminRole = {
  roleId: string;
  roleCode: string;
  roleName: string;
  description?: string;
  permissions: string[];
};

type AdminPermission = {
  permissionId: string;
  permissionCode: string;
  moduleArea?: string;
};

export function AdministrationPage({ refresh, allProjects }: { refresh: number; allProjects: ProjectItem[] }) {
  const [users, setUsers] = useState<AdminUser[]>([]),
    [roles, setRoles] = useState<AdminRole[]>([]),
    [permissions, setPermissions] = useState<AdminPermission[]>([]),
    [roleId, setRoleId] = useState(""),
    [selected, setSelected] = useState<string[]>([]),
    [saving, setSaving] = useState(false),
    [version, setVersion] = useState(0),
    [userSearch, setUserSearch] = useState("");
  const [editing, setEditing] = useState<AdminUser | null>(null),
    [displayName, setDisplayName] = useState(""),
    [email, setEmail] = useState(""),
    [active, setActive] = useState(true),
    [userRoleIds, setUserRoleIds] = useState<string[]>([]),
    [userProjectIds, setUserProjectIds] = useState<string[]>([]),
    [passwordUser, setPasswordUser] = useState<AdminUser | null>(null),
    [newPassword, setNewPassword] = useState("");
  const [creating, setCreating] = useState(false),
    [newUsername, setNewUsername] = useState(""),
    [newPasswordCreate, setNewPasswordCreate] = useState("");
  const [roleModal, setRoleModal] = useState<"create" | "edit" | null>(null);
  const [adminLoading, setAdminLoading] = useState(true);
  const [adminLoadError, setAdminLoadError] = useState("");
  const [roleCode, setRoleCode] = useState("");
  const [roleName, setRoleName] = useState("");
  const [roleDescription, setRoleDescription] = useState("");
  const headers = {
    "Content-Type": "application/json",
    Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
  };
  const roleIdRef = useRef(roleId);
  useEffect(() => { roleIdRef.current = roleId; }, [roleId]);
  useEffect(() => {
    const requestHeaders = {
      Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
    };
    // เดิมไม่ตรวจ r.ok — ถ้าได้ 403/500 roles จะเป็น object error แล้ว r.length/roles.map ทำหน้าพัง
    const getJson = (url: string) => fetch(url, { headers: requestHeaders }).then((r) => { if (!r.ok) throw new Error(`HTTP ${r.status}`); return r.json(); });
    setAdminLoading(true);
    setAdminLoadError("");
    Promise.all([
      getJson(`${apiUrl}/admin/users`),
      getJson(`${apiUrl}/admin/roles`),
      getJson(`${apiUrl}/admin/permissions`),
    ]).then(([u, r, p]) => {
      if (!Array.isArray(r) || !Array.isArray(p)) throw new Error("รูปแบบข้อมูลไม่ถูกต้อง");
      setUsers(Array.isArray(u) ? u : u?.items?.rows ?? []);
      setRoles(r);
      setPermissions(p);
      if (r.length) {
        // keep the admin's currently-viewed role selected across refreshes triggered by unrelated
        // actions (e.g. toggling a user's active state) instead of always snapping back to roles[0]
        const target = r.find((x: AdminRole) => x.roleId === roleIdRef.current) ?? r[0];
        setRoleId(target.roleId);
        setSelected(
          p
            .filter((x: AdminPermission) =>
              target.permissions.includes(x.permissionCode),
            )
            .map((x: AdminPermission) => x.permissionId),
        );
      }
    }).catch((e) => setAdminLoadError(`โหลดข้อมูลผู้ใช้/สิทธิ์ไม่สำเร็จ (${e instanceof Error ? e.message : "ไม่ทราบสาเหตุ"}) — ตรวจสิทธิ์ ADMIN แล้วลองใหม่`))
      .finally(() => setAdminLoading(false));
  }, [refresh, version]);
  const filteredUsers = users.filter(
    (u) =>
      u.displayName.toLowerCase().includes(userSearch.toLowerCase()) ||
      u.username.toLowerCase().includes(userSearch.toLowerCase()) ||
      (u.email ?? "").toLowerCase().includes(userSearch.toLowerCase()),
  );
  const activeCount = users.filter((u) => u.isActive).length;
  const changeRole = (id: string) => {
    setRoleId(id);
    const role = roles.find((x) => x.roleId === id);
    setSelected(
      permissions
        .filter((x) => role?.permissions.includes(x.permissionCode))
        .map((x) => x.permissionId),
    );
  };
  const togglePermission = (id: string, checked: boolean) =>
    setSelected((current) =>
      checked ? [...current, id] : current.filter((value) => value !== id),
    );
  const savePermissions = async () => {
    setSaving(true);
    try {
      const response = await fetch(
        `${apiUrl}/admin/roles/${roleId}/permissions`,
        {
          method: "POST",
          headers,
          body: JSON.stringify({ permissionIds: selected }),
        },
      );
      if (!response.ok) {
        const payload = await response.json().catch(() => null);
        throw new Error(payload?.detail ?? payload?.title ?? `HTTP ${response.status}`);
      }
      setVersion((current) => current + 1);
      notify("บันทึกสิทธิ์เรียบร้อยแล้ว", "success");
    } catch {
      notify("ไม่สามารถบันทึกสิทธิ์ได้ กรุณาลองใหม่", "error");
    } finally {
      setSaving(false);
    }
  };
  const openRoleModal = (mode: "create" | "edit") => {
    const role = roles.find((x) => x.roleId === roleId);
    setRoleModal(mode);
    setRoleCode(mode === "edit" ? role?.roleCode ?? "" : "");
    setRoleName(mode === "edit" ? role?.roleName ?? "" : "");
    setRoleDescription(mode === "edit" ? role?.description ?? "" : "");
  };
  const saveRole = async () => {
    if (!roleModal || !roleName.trim() || (roleModal === "create" && !roleCode.trim())) return;
    const response = await fetch(roleModal === "create" ? `${apiUrl}/admin/roles` : `${apiUrl}/admin/roles/${roleId}`, {
      method: roleModal === "create" ? "POST" : "PUT",
      headers,
      body: JSON.stringify(roleModal === "create" ? { roleCode, roleName, description: roleDescription } : { roleName, description: roleDescription }),
    });
    if (response.ok) window.location.reload();
    else notify("บันทึกกลุ่มสิทธิ์ไม่สำเร็จ", "error");
  };
  const deleteRole = async () => {
    const role = roles.find((x) => x.roleId === roleId);
    if (!role || !await confirmDialog(`ลบกลุ่มสิทธิ์ ${role.roleName} หรือไม่?`)) return;
    const response = await fetch(`${apiUrl}/admin/roles/${roleId}`, { method: "DELETE", headers });
    if (response.ok) window.location.reload();
    else notify("ลบกลุ่มสิทธิ์ไม่สำเร็จ หรือกลุ่มนี้ยังมีผู้ใช้งานอยู่", "error");
  };
  const openEdit = (user: AdminUser) => {
    setEditing(user);
    setDisplayName(user.displayName);
    setEmail(user.email ?? "");
    setActive(user.isActive);
    setUserRoleIds(
      roles
        .filter((role) => user.roles.includes(role.roleCode))
        .map((role) => role.roleId),
    );
    setUserProjectIds(user.assignedProjectIds ?? []);
  };
  const openCreate = () => {
    setCreating(true);
    setEditing(null);
    setDisplayName(""); setEmail(""); setActive(true); setUserRoleIds([]); setUserProjectIds([]);
    setNewUsername(""); setNewPasswordCreate("");
  };
  const saveUser = async () => {
    if (creating) {
      if (!newUsername.trim() || !displayName.trim() || newPasswordCreate.length < 8) { notify("กรุณากรอก Username, ชื่อที่แสดง และรหัสผ่านอย่างน้อย 8 ตัวอักษร", "error"); return; }
      setSaving(true);
      try {
        const create = await fetch(`${apiUrl}/admin/users`, { method: "POST", headers, body: JSON.stringify({ username: newUsername, displayName, email: email || null, password: newPasswordCreate, roleIds: userRoleIds }) });
        if (!create.ok) { const p = await create.json().catch(() => null); throw new Error(p?.detail ?? "สร้างผู้ใช้ไม่สำเร็จ"); }
        const created = await create.json();
        if (userProjectIds.length) {
          const proj = await fetch(`${apiUrl}/admin/users/${(created as { userId: string }).userId}/projects`, { method: "POST", headers, body: JSON.stringify({ projectIds: userProjectIds }) });
          if (!proj.ok) throw new Error("กำหนด Project ไม่สำเร็จ");
        }
        setCreating(false); setVersion((x) => x + 1);
      } catch (e) { notify(e instanceof Error ? e.message : "ไม่สามารถสร้างผู้ใช้ได้", "error"); } finally { setSaving(false); }
      return;
    }
    if (!editing) return;
    setSaving(true);
    try {
      const update = await fetch(`${apiUrl}/admin/users/${editing.userId}`, {
        method: "PUT",
        headers,
        body: JSON.stringify({
          displayName,
          email: email || null,
          isActive: active,
        }),
      });
      if (!update.ok) throw new Error();
      const assign = await fetch(
        `${apiUrl}/admin/users/${editing.userId}/roles`,
        {
          method: "POST",
          headers,
          body: JSON.stringify({ roleIds: userRoleIds }),
        },
      );
      if (!assign.ok) throw new Error();
      const assignProjects = await fetch(
        `${apiUrl}/admin/users/${editing.userId}/projects`,
        {
          method: "POST",
          headers,
          body: JSON.stringify({ projectIds: userProjectIds }),
        },
      );
      if (!assignProjects.ok) throw new Error();
      setEditing(null);
      setVersion((x) => x + 1);
    } catch {
      notify("ไม่สามารถบันทึกข้อมูลผู้ใช้ได้", "error");
    } finally {
      setSaving(false);
    }
  };
  const toggleActive = async (user: AdminUser) => {
    if (user.isActive && !await confirmDialog({ title: "ปิดใช้งานผู้ใช้", message: `ปิดใช้งาน ${user.displayName} (${user.username}) ใช่หรือไม่?\nผู้ใช้นี้จะเข้าสู่ระบบไม่ได้จนกว่าจะเปิดใช้งานอีกครั้ง`, confirmLabel: "ปิดใช้งาน", tone: "danger" })) return;
    setSaving(true);
    try {
      const response = await fetch(`${apiUrl}/admin/users/${user.userId}`, {
        method: "PUT",
        headers,
        body: JSON.stringify({
          displayName: user.displayName,
          email: user.email ?? null,
          isActive: !user.isActive,
        }),
      });
      if (!response.ok) throw new Error();
      setVersion((x) => x + 1);
    } catch {
      notify("ไม่สามารถเปลี่ยนสถานะผู้ใช้ได้", "error");
    } finally {
      setSaving(false);
    }
  };
  const resetPassword = async () => {
    if (!passwordUser || newPassword.length < 8) return;
    setSaving(true);
    try {
      const response = await fetch(
        `${apiUrl}/admin/users/${passwordUser.userId}/reset-password`,
        { method: "POST", headers, body: JSON.stringify({ newPassword }) },
      );
      if (!response.ok) throw new Error();
      setPasswordUser(null);
      setNewPassword("");
      notify("รีเซ็ตรหัสผ่านเรียบร้อยแล้ว", "success");
    } catch {
      notify("ไม่สามารถรีเซ็ตรหัสผ่านได้", "error");
    } finally {
      setSaving(false);
    }
  };
  const [permFilter, setPermFilter] = useState("");
  // Menu labels/groups below are kept in lockstep with the real sidebar (`nav`, defined near the top of this
  // file) so the permission page always reflects the menus users actually see.
  const areaMenuMap: Record<string, { group: string; icon: string }> = {
    DASHBOARD: { group: "ภาพรวม", icon: "D" },
    MYWORK: { group: "ภาพรวม", icon: "MW" },
    WORKLOAD: { group: "ภาพรวม", icon: "WL" },
    PROJECT: { group: "ภาพรวม", icon: "P" },
    REQUIREMENT: { group: "REQUIREMENT & TEST DESIGN", icon: "REQ" },
    RTM: { group: "REQUIREMENT & TEST DESIGN", icon: "RTM" },
    TESTCASE: { group: "REQUIREMENT & TEST DESIGN", icon: "TC" },
    TESTSUITE: { group: "REQUIREMENT & TEST DESIGN", icon: "TS" },
    TESTCYCLE: { group: "TEST EXECUTION", icon: "TCY" },
    EXECUTION: { group: "TEST EXECUTION", icon: "EX" },
    DEFECT: { group: "TEST EXECUTION", icon: "DEF" },
    REGRESSION: { group: "TEST EXECUTION", icon: "REG" },
    AUTOMATION: { group: "TEST EXECUTION", icon: "AUT" },
    REPORT: { group: "RELEASE GOVERNANCE", icon: "SUM" },
    RISK: { group: "RELEASE GOVERNANCE", icon: "RISK" },
    RELEASE: { group: "RELEASE GOVERNANCE", icon: "REL" },
    ADMIN: { group: "ADMINISTRATION", icon: "ADM" },
    SETTING: { group: "ADMINISTRATION", icon: "SET" },
    MONITOR: { group: "ADMINISTRATION", icon: "MON" },
    AUDIT: { group: "ADMINISTRATION", icon: "AUD" },
  };
  const menuGroupOrder = ["ภาพรวม", "REQUIREMENT & TEST DESIGN", "TEST EXECUTION", "RELEASE GOVERNANCE", "ADMINISTRATION", "Other"];
  const visiblePermissions = permissions.filter((p) => (p.permissionCode + " " + (p.moduleArea ?? "")).toLowerCase().includes(permFilter.toLowerCase()));
  const grouped = menuGroupOrder.map((group) => ({ group, icon: Object.values(areaMenuMap).find((v) => v.group === group)?.icon ?? "…", items: visiblePermissions.filter((p) => { const area = p.moduleArea || "OTHER"; return (areaMenuMap[area]?.group ?? "Other") === group; }) })).filter((g) => g.items.length > 0 || g.group !== "Other");
  // Same groups/items/order as the `nav` sidebar menu, so "Menu" rows here match what users see on the left.
  const menuTree = [
    ["ภาพรวม", [["Dashboard", "DASHBOARD"], ["My Work", "MYWORK"], ["Project / Module", "PROJECT"], ["Release / Build", "PROJECT"]]],
    ["REQUIREMENT & TEST DESIGN", [["Requirement", "REQUIREMENT"], ["RTM", "RTM"], ["Test Case", "TESTCASE"], ["Test Suite", "TESTSUITE"]]],
    ["TEST EXECUTION", [["Test Cycle", "TESTCYCLE"], ["Execution Workspace", "EXECUTION"], ["Defect", "DEFECT"], ["Regression", "REGRESSION"], ["Automation", "AUTOMATION"]]],
    ["RELEASE GOVERNANCE", [["Test Summary", "REPORT"], ["Risk Acceptance", "RISK"], ["Release Sign-off", "RELEASE"]]],
    ["ADMINISTRATION", [["User / Role", "ADMIN"], ["Setting Center", "SETTING"], ["System Monitor", "MONITOR"], ["Audit Log", "AUDIT"]]],
  ] as const;
  const permissionArea = (permission: AdminPermission) => {
    const code = permission.permissionCode.toUpperCase();
    if (code.startsWith("QA.MYWORK.")) return "MYWORK";
    if (code.startsWith("QA.WORKLOAD.")) return "WORKLOAD";
    return (permission.moduleArea || code.split(".")[0] || "OTHER").toUpperCase();
  };
  const matrixGroups = menuTree.map(([group, areas]) => ({ group, areas: areas.map(([label, area]) => ({ label, area, items: visiblePermissions.filter((p) => permissionArea(p) === area) })) }));
  const matrixPermission = (items: AdminPermission[], action: string) => items.find((x) => x.permissionCode.split(".").at(-1)?.toUpperCase() === action || x.permissionCode.toUpperCase().endsWith(`.${action}`));
  // สิทธิ์ที่ตาราง Create/Delete/Edit/View แสดงได้ — ที่เหลือ (เช่น RISK.APPROVE, RELEASE.SIGNOFF, REPORT.EXPORT, AUTOMATION.EXECUTE)
  // เดิมอยู่ในรายการที่ถูกซ่อนด้วย display:none จึงให้สิทธิ์จากหน้าจอไม่ได้เลย — ตอนนี้แสดงเป็น "สิทธิ์เพิ่มเติม"
  const matrixActions = ["CREATE", "DELETE", "EDIT", "VIEW"];
  const matrixCoveredIds = new Set(menuTree.flatMap(([, areas]) => areas.flatMap(([, area]) => {
    const items = permissions.filter((p) => permissionArea(p) === area);
    return matrixActions.map((action) => matrixPermission(items, action)?.permissionId).filter((id): id is AdminPermission["permissionId"] => id !== undefined);
  })));
  const extraGroups = grouped.map((g) => ({ ...g, items: g.items.filter((p) => !matrixCoveredIds.has(p.permissionId)) })).filter((g) => g.items.length > 0);
  const filtering = permFilter.trim().length > 0;
  return (
    <div className="admin-page">
      {adminLoadError && <div className="inline-alert error" role="alert"><span>{adminLoadError}</span><button type="button" className="btn" onClick={() => setVersion((v) => v + 1)}>ลองใหม่</button></div>}
      {adminLoading && !adminLoadError && users.length === 0 && <div className="empty" role="status"><div className="spinner" /><p>กำลังโหลดผู้ใช้และสิทธิ์...</p></div>}
      <header className="admin-page-header">
        <div>
          <h2>จัดการผู้ใช้และสิทธิ์</h2>
          <p>เพิ่ม แก้ไข และกำหนดบทบาท สิทธิ์ และ Project ให้ผู้ใช้ในระบบ</p>
        </div>
      </header>

      <div className="admin-stats-row">
        <div className="admin-stat-card">
          <span className="admin-stat-icon blue">&#x1F465;</span>
          <div>
            <b>{users.length}</b>
            <small>ผู้ใช้ทั้งหมด</small>
          </div>
        </div>
        <div className="admin-stat-card">
          <span className="admin-stat-icon green">&#x2705;</span>
          <div>
            <b>{activeCount}</b>
            <small>ใช้งานอยู่</small>
          </div>
        </div>
        <div className="admin-stat-card">
          <span className="admin-stat-icon orange">&#x1F6E1;</span>
          <div>
            <b>{users.length - activeCount}</b>
            <small>ปิดใช้งาน</small>
          </div>
        </div>
        <div className="admin-stat-card">
          <span className="admin-stat-icon purple">&#x1F3F7;</span>
          <div>
            <b>{roles.length}</b>
            <small>บทบาท</small>
          </div>
        </div>
      </div>

      <article className="card admin-users-card">
        <div className="card-title">
          <div>
            <h3>รายชื่อผู้ใช้งาน</h3>
            <p>เลือกผู้ใช้เพื่อแก้ไขข้อมูล บทบาท และ Project ที่เข้าถึงได้</p>
          </div>
          <div className="admin-users-toolbar">
            <div className="admin-search">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><circle cx="11" cy="11" r="8"/><path d="M21 21l-4.35-4.35"/></svg>
              <input
                placeholder="ค้นหาผู้ใช้..."
                value={userSearch}
                onChange={(e) => setUserSearch(e.target.value)}
              />
            </div>
            <button className="btn primary" onClick={openCreate}>+ เพิ่มผู้ใช้</button>
          </div>
        </div>

        <div className="admin-users-table-wrap">
          <table className="admin-users-table">
            <thead>
              <tr>
                <th>ผู้ใช้งาน</th>
                <th>บทบาท</th>
                <th>Project</th>
                <th>สถานะ</th>
                <th>เข้าสู่ระบบล่าสุด</th>
                <th className="th-action">จัดการ</th>
              </tr>
            </thead>
            <tbody>
              {filteredUsers.map((x) => (
                <tr key={x.userId}>
                  <td data-label="ผู้ใช้งาน" className="td-user">
                    <div className="user-cell">
                      <span className={`user-avatar ${x.isActive ? "" : "inactive"}`}>
                        {x.displayName.slice(0, 1).toUpperCase()}
                      </span>
                      <div className="user-info">
                        <b>{x.displayName}</b>
                        <small>{x.username}{x.email ? ` · ${x.email}` : ""}</small>
                      </div>
                    </div>
                  </td>
                  <td data-label="บทบาท">
                    <div className="role-tags">
                      {x.roles.length
                        ? x.roles.map((role) => <span key={role}>{role}</span>)
                        : <span className="tag-empty">-</span>}
                    </div>
                  </td>
                  <td data-label="Project">
                    <div className="role-tags project-tags">
                      {x.assignedProjectIds?.length
                        ? x.assignedProjectIds.map((pid) => {
                            const proj = allProjects.find((p) => p.projectId === pid);
                            return <span key={pid} className="project-tag">{proj?.projectCode ?? pid.slice(0, 8)}</span>;
                          })
                        : <span className="tag-empty">-</span>}
                    </div>
                  </td>
                  <td data-label="สถานะ">
                    <Badge tone={x.isActive ? "green" : "red"}>
                      {x.isActive ? "ใช้งาน" : "ปิดใช้งาน"}
                    </Badge>
                  </td>
                  <td data-label="เข้าสู่ระบบล่าสุด" className="td-meta">
                    {x.lastLoginAt
                      ? formatThaiDateTime(x.lastLoginAt)
                      : <span className="tag-empty">-</span>}
                  </td>
                  <td data-label="จัดการ" className="td-actions">
                    <button className="table-action icon-only" title="แก้ไข" aria-label={`แก้ไข ${x.username}`} onClick={() => openEdit(x)}>
                      <span className="material-symbols-outlined" aria-hidden="true">edit</span>
                    </button>
                    <button
                      className={`table-action icon-only ${x.isActive ? "table-action-warn" : "table-action-green"}`}
                      title={x.isActive ? "ปิดใช้งาน" : "เปิดใช้งาน"}
                      aria-label={`${x.isActive ? "ปิดใช้งาน" : "เปิดใช้งาน"} ${x.username}`}
                      onClick={() => toggleActive(x)}
                      disabled={saving}
                    >
                      <span className="material-symbols-outlined" aria-hidden="true">power_settings_new</span>
                    </button>
                    <button
                      className="table-action table-action-key icon-only"
                      title="รีเซ็ตรหัสผ่าน"
                      aria-label={`รีเซ็ตรหัสผ่าน ${x.username}`}
                      onClick={() => { setPasswordUser(x); setNewPassword(""); }}
                    >
                      <span aria-hidden="true">⚿</span>
                    </button>
                  </td>
                </tr>
              ))}
              {!filteredUsers.length && (
                <tr>
                  <td colSpan={6} className="empty-cell">
                    <div className="empty-state">
                      <span>&#x1F464;</span>
                      <b>ไม่พบผู้ใช้</b>
                      <small>{userSearch ? "ลองค้นหาด้วยคำอื่น" : "ยังไม่มีผู้ใช้ในระบบ"}</small>
                    </div>
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </article>

      {(editing || creating) && (
        <ModalShell onDismiss={() => { if (saving) return; if (creating) setCreating(false); else setEditing(null); }}>
            <div className="modal-head">
              <h2>{creating ? "เพิ่มผู้ใช้" : `แก้ไขผู้ใช้ — ${editing?.username}`}</h2>
              <button onClick={() => !saving && (creating ? setCreating(false) : setEditing(null))}>&times;</button>
            </div>
            <div className="form-grid form-grid-2col">
              {creating && <label>Username <span className="required">*</span><input value={newUsername} onChange={(e) => setNewUsername(e.target.value)} autoFocus /></label>}
              <label>
                ชื่อที่แสดง <span className="required">*</span>
                <input
                  value={displayName}
                  onChange={(e) => setDisplayName(e.target.value)}
                />
              </label>
              <label>
                อีเมล
                <input
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                />
              </label>
              {creating && <label>รหัสผ่าน <span className="required">*</span><input type="password" value={newPasswordCreate} onChange={(e) => setNewPasswordCreate(e.target.value)} placeholder="อย่างน้อย 8 ตัวอักษร" /></label>}
            </div>
            <div className="modal-section">
              <h3 className="modal-section-title">บทบาทของผู้ใช้</h3>
              <div className="role-checks">
                {roles.map((role) => (
                  <label
                    key={role.roleId}
                    className={userRoleIds.includes(role.roleId) ? "selected" : ""}
                  >
                    <input
                      type="checkbox"
                      checked={userRoleIds.includes(role.roleId)}
                      onChange={(e) =>
                        setUserRoleIds((current) =>
                          e.target.checked
                            ? [...current, role.roleId]
                            : current.filter((id) => id !== role.roleId),
                        )
                      }
                    />
                    <span>
                      <b>{role.roleName}</b>
                      <small>{role.roleCode}</small>
                    </span>
                  </label>
                ))}
              </div>
            </div>
            <div className="modal-section">
              <h3 className="modal-section-title">Project ที่เข้าถึงได้</h3>
              <p className="fieldset-hint">ไม่เลือก Project ใด = ไม่เห็น Project ในระบบ</p>
              <div className="role-checks">
                {allProjects.map((project) => (
                  <label
                    key={project.projectId}
                    className={userProjectIds.includes(project.projectId) ? "selected" : ""}
                  >
                    <input
                      type="checkbox"
                      checked={userProjectIds.includes(project.projectId)}
                      onChange={(e) =>
                        setUserProjectIds((current) =>
                          e.target.checked
                            ? [...current, project.projectId]
                            : current.filter((id) => id !== project.projectId),
                        )
                      }
                    />
                    <span>
                      <b>{project.projectName}</b>
                      <small>{project.projectCode}</small>
                    </span>
                  </label>
                ))}
                {!allProjects.length && <p className="muted-text">ไม่มี Project ในระบบ</p>}
              </div>
            </div>
            <label className="active-switch">
              <input
                type="checkbox"
                checked={active}
                onChange={(e) => setActive(e.target.checked)}
              />
              อนุญาตให้เข้าสู่ระบบ
            </label>
            <div className="modal-actions">
              <button className="btn" disabled={saving} onClick={() => (creating ? setCreating(false) : setEditing(null))}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิก</button>
              <button
                className="btn primary"
                onClick={saveUser}
                disabled={saving || !displayName.trim() || (creating && (!newUsername.trim() || newPasswordCreate.length < 8))}
              >
                {saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : creating ? <><span className="material-symbols-outlined" aria-hidden="true">add</span> สร้างผู้ใช้</> : <><span className="material-symbols-outlined" aria-hidden="true">check</span> บันทึกข้อมูลผู้ใช้</>}
              </button>
            </div>
          </ModalShell>
      )}

      {passwordUser && (
        <ModalShell boxStyle={{ maxWidth: 480 }} onDismiss={() => setPasswordUser(null)}>
            <div className="modal-head">
              <h2>รีเซ็ตรหัสผ่าน — {passwordUser.username}</h2>
              <button onClick={() => setPasswordUser(null)}>&times;</button>
            </div>
            <div className="password-hint-box">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><circle cx="12" cy="12" r="10"/><path d="M12 16v-4"/><path d="M12 8h.01"/></svg>
              <span>รหัสผ่านต้องมีอย่างน้อย 8 ตัวอักษร</span>
            </div>
            <label>
              รหัสผ่านใหม่
              <input
                type="password"
                autoFocus
                value={newPassword}
                onChange={(e) => setNewPassword(e.target.value)}
                placeholder="รหัสผ่านใหม่"
              />
            </label>
            <div className="modal-actions">
              <button className="btn" onClick={() => setPasswordUser(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิก</button>
              <button
                className="btn primary"
                onClick={resetPassword}
                disabled={saving || newPassword.length < 8}
              >
                {saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span aria-hidden="true">⚿</span> ยืนยันรีเซ็ตรหัสผ่าน</>}
              </button>
            </div>
          </ModalShell>
      )}

      <article className="card permission-card">
        <div className="card-title">
          <div>
            <h3>สิทธิ์ตามบทบาท</h3>
            <p>กำหนดเมนูและการดำเนินการที่แต่ละบทบาทเข้าถึงได้</p>
          </div>
          <span className="count-pill blue-pill">
            {selected.length}/{permissions.length}
          </span>
        </div>
        <label className="role-selector">
          <span>บทบาท</span>
          <select value={roleId} onChange={(e) => changeRole(e.target.value)}>
            {roles.map((x) => (
              <option value={x.roleId} key={x.roleId}>
                {x.roleName} ({x.roleCode})
              </option>
            ))}
          </select>
          <div className="role-actions">
            <button type="button" className="btn" onClick={() => openRoleModal("create")}><span className="material-symbols-outlined" aria-hidden="true">add</span> Create group</button>
            <button type="button" className="btn" onClick={() => openRoleModal("edit")} disabled={!roleId}><span className="material-symbols-outlined" aria-hidden="true">edit</span> Edit</button>
            <button type="button" className="btn" onClick={deleteRole} disabled={!roleId}><span className="material-symbols-outlined" aria-hidden="true">close</span> Delete</button>
          </div>
        </label>
        <div className="permission-toolbar">
          <div className="permission-filter"><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><circle cx="11" cy="11" r="8"/><path d="M21 21l-4.35-4.35"/></svg><input aria-label="ค้นหาสิทธิ์" placeholder="ค้นหาสิทธิ์..." value={permFilter} onChange={(e) => setPermFilter(e.target.value)} /></div>
          <div>
            {/* ตอนมีคำค้นหา ให้เลือก/ล้างเฉพาะสิทธิ์ที่แสดงอยู่ — เดิม "เลือกทั้งหมด" แทนที่ทั้งชุดด้วยรายการที่กรอง ทำให้สิทธิ์อื่นหายตอนบันทึก */}
            <button
              type="button"
              onClick={() => setSelected((prev) => [...new Set([...prev, ...visiblePermissions.map((x) => x.permissionId)])])}
            >
              {filtering ? "เลือกที่แสดงอยู่" : "เลือกทั้งหมด"}
            </button>
            <button type="button" onClick={() => { const visible = new Set(visiblePermissions.map((x) => x.permissionId)); setSelected((prev) => filtering ? prev.filter((id) => !visible.has(id)) : []); }}>
              {filtering ? "ล้างที่แสดงอยู่" : "ล้างทั้งหมด"}
            </button>
          </div>
        </div>
        <div className="permission-matrix-wrap">
          <table className="permission-matrix">
            <thead><tr><th>Menu</th><th>Create</th><th>Delete</th><th>Edit</th><th>View only</th></tr></thead>
            <tbody>{matrixGroups.map((g) => <_F key={g.group}><tr className="permission-menu-group"><th colSpan={5}>{g.group}</th></tr>{g.areas.map((row) => <tr key={row.area}><td className="permission-submenu">{row.label}</td>{["CREATE", "DELETE", "EDIT", "VIEW"].map((action) => { const permission = matrixPermission(row.items, action); return <td key={action}><input type="checkbox" aria-label={`${row.label} ${action}`} checked={permission ? selected.includes(permission.permissionId) : false} onChange={(e) => permission && togglePermission(permission.permissionId, e.target.checked)} /></td>; })}</tr>)}</_F>)}</tbody>
          </table>
        </div>
        {extraGroups.length > 0 && <h3 className="permission-extra-title">สิทธิ์เพิ่มเติม <small>อนุมัติ, Sign-off, Export, Execute และสิทธิ์เฉพาะอื่นที่ตารางด้านบนไม่ครอบคลุม</small></h3>}
        <div className="permission-groups">
          {extraGroups.map((g) => (
            <section key={g.group}>
              <h4><span className="perm-group-icon" aria-hidden="true">{g.icon}</span>{g.group}<span className="count-pill">{g.items.length}</span></h4>
              <div className="permission-grid">
                {g.items.map((x) => (
                  <label
                    className={selected.includes(x.permissionId) ? "selected" : ""}
                    key={x.permissionId}
                  >
                    <input
                      type="checkbox"
                      checked={selected.includes(x.permissionId)}
                      onChange={(e) => togglePermission(x.permissionId, e.target.checked)}
                    />
                    <span>
                      <b>{x.permissionCode.split(".").at(-1)}</b>
                      <small>{x.permissionCode}</small>
                    </span>
                  </label>
                ))}
              </div>
            </section>
          ))}
        </div>
        <div className="permission-actions">
          <small>เลือกแล้ว {selected.length} สิทธิ์</small>
          <button className="btn primary" onClick={savePermissions} disabled={!roleId || saving}>
            {saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">check</span> บันทึกการเปลี่ยนแปลง</>}
          </button>
        </div>
      </article>
      {roleModal && (
        <ModalShell className="role-modal" onDismiss={() => setRoleModal(null)}>
            <div className="modal-head">
              <h2>{roleModal === "create" ? "เพิ่มกลุ่มสิทธิ์" : "แก้ไขกลุ่มสิทธิ์"}</h2>
              <button type="button" onClick={() => setRoleModal(null)} aria-label="ปิด"><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
            </div>
            <div className="form-grid">
              <label>Role Code<input value={roleCode} disabled={roleModal === "edit"} onChange={(e) => setRoleCode(e.target.value.toUpperCase())} /></label>
              <label>ชื่อกลุ่มสิทธิ์<input value={roleName} onChange={(e) => setRoleName(e.target.value)} /></label>
              <label className="full">รายละเอียด<textarea value={roleDescription} onChange={(e) => setRoleDescription(e.target.value)} rows={3} /></label>
            </div>
            <div className="modal-actions">
              <button type="button" className="btn" onClick={() => setRoleModal(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิก</button>
              <button type="button" className="btn primary" onClick={saveRole}><span className="material-symbols-outlined" aria-hidden="true">check</span> บันทึก</button>
            </div>
          </ModalShell>
      )}
    </div>
  );
}
