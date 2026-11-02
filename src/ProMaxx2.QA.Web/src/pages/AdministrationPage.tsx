import "../components/ListPage.css";
import "./AdministrationPage.css";
import type { ProjectItem } from "../shared/types";
import { useEffect, useId, useMemo, useRef, useState } from "react";
import { apiFetch, apiUrl, getJson, isAbortError } from "../api";
import { notify, confirmDialog } from "../components/dialogStore";
import { Badge } from "../components/Badge";
import { formatThaiDateTime } from "../dateTime";
import { ModalShell } from "../components/ModalShell";
import { type AdminUser, type Page, nav, pageViewPermission } from "../shared/appShared";

type AdminRole = { roleId: string; roleCode: string; roleName: string; description?: string; isActive?: boolean; permissions: string[] };
type AdminPermission = { permissionId: string; permissionCode: string; moduleArea?: string };
type UserListResult = { items: { rows: AdminUser[]; total: number } };

const errorText = (e: unknown, fallback: string) => e instanceof Error && e.message ? e.message : fallback;
const tabKey = "qa.admin.tab";
const SYS_ADMIN = "SYS_ADMIN";

/* ===== แคตตาล็อกสิทธิ์: แถว = เมนูใน Sidebar (ชุดเดียวกับ `nav`) + สิทธิ์ที่ไม่ผูกกับเมนูใดต่อท้าย =====
   แต่ละแถวมีสิทธิ์ "เข้าเมนู" (ตัวที่ Sidebar ใช้ซ่อน/แสดงเมนู — pageViewPermission) และสิทธิ์ทำงานของ area นั้น */
const pageArea: Partial<Record<Page, string>> = {
  dashboard: "DASHBOARD", "my-work": "MYWORK", performance: "PERFORMANCE", knowledge: "KNOWLEDGE", projects: "PROJECT",
  requirements: "REQUIREMENT", rtm: "RTM", "test-cases": "TESTCASE", "test-suites": "TESTSUITE",
  "test-plans": "TESTPLAN", "test-cycles": "TESTCYCLE", execution: "EXECUTION", defects: "DEFECT", regression: "REGRESSION", automation: "AUTOMATION", crm: "CRM",
  summary: "REPORT", risks: "RISK", signoff: "RELEASE",
  users: "ADMIN", settings: "SETTING", "system-monitor": "MONITOR", audit: "AUDIT",
};
const otherAreas: Record<string, { label: string; icon: string }> = {
  WORKLOAD: { label: "Workload ของทีม", icon: "groups" },
  QA: { label: "มอบหมายงาน QA", icon: "assignment_turned_in" },
};
const actionLabels: Record<string, string> = {
  VIEW: "ดู", CREATE: "สร้าง", EDIT: "แก้ไข", DELETE: "ลบ", ASSIGN: "มอบหมาย", EXPORT: "Export", FREEZE: "ปิด / Freeze", APPROVE: "อนุมัติ",
  SIGNOFF: "Sign-off", RUN: "บันทึกผล", EXECUTE: "รัน", MANAGE: "จัดการ", RESOLVE: "Resolve", VALIDATE: "ตรวจ DSL", GENERATEAI: "AI Generate",
  VIEWEVIDENCE: "ดูหลักฐาน", MCP: "เชื่อม MCP", USER: "จัดการผู้ใช้", PERMISSION: "จัดการสิทธิ์", AUTO: "มอบหมายอัตโนมัติ", REASSIGN: "ย้ายผู้รับผิดชอบ",
};
const actionOrder = ["VIEW", "CREATE", "EDIT", "DELETE"];
const actionOf = (code: string) => code.split(".").at(-1)?.toUpperCase() ?? code;
const actionLabel = (code: string) => actionLabels[actionOf(code)] ?? actionOf(code);
const permissionArea = (p: AdminPermission) => {
  const code = p.permissionCode.toUpperCase();
  if (code.startsWith("QA.MYWORK.")) return "MYWORK";
  if (code.startsWith("QA.WORKLOAD.")) return "WORKLOAD";
  return (p.moduleArea || code.split(".")[0] || "OTHER").toUpperCase();
};
const byAction = (a: AdminPermission, b: AdminPermission) => {
  const ia = actionOrder.indexOf(actionOf(a.permissionCode)), ib = actionOrder.indexOf(actionOf(b.permissionCode));
  return (ia < 0 ? 99 : ia) - (ib < 0 ? 99 : ib) || a.permissionCode.localeCompare(b.permissionCode);
};

type CatalogRow = { key: string; label: string; icon: string; accessCode?: string; access?: AdminPermission; actions: AdminPermission[] };
type CatalogGroup = { label: string; rows: CatalogRow[] };

function buildCatalog(permissions: AdminPermission[]): CatalogGroup[] {
  const byCode = new Map(permissions.map((p) => [p.permissionCode.toUpperCase(), p]));
  const byArea = new Map<string, AdminPermission[]>();
  for (const p of permissions) { const area = permissionArea(p); byArea.set(area, [...(byArea.get(area) ?? []), p]); }
  const covered = new Set<string>();
  const groups: CatalogGroup[] = nav.map((g) => ({
    label: g.label,
    rows: g.items.map((item) => {
      const accessCode = pageViewPermission[item.id];
      const access = byCode.get(accessCode.toUpperCase());
      const area = pageArea[item.id];
      const actions = (area ? byArea.get(area) ?? [] : []).filter((p) => p.permissionId !== access?.permissionId).sort(byAction);
      if (access) covered.add(access.permissionId);
      actions.forEach((p) => covered.add(p.permissionId));
      return { key: item.id, label: item.label, icon: item.icon, accessCode, access, actions };
    }),
  }));
  // สิทธิ์ที่ไม่มีเมนูของตัวเอง (Workload, มอบหมายงาน QA ฯลฯ) — ต้องแสดงไว้ ไม่งั้นให้สิทธิ์จากหน้าจอไม่ได้
  const rest = [...byArea.entries()].map(([area, items]) => [area, items.filter((p) => !covered.has(p.permissionId))] as const).filter(([, items]) => items.length > 0);
  if (rest.length) groups.push({ label: "สิทธิ์อื่น ๆ (ไม่มีเมนูของตัวเอง)", rows: rest.map(([area, items]) => ({ key: `area-${area}`, label: otherAreas[area]?.label ?? area, icon: otherAreas[area]?.icon ?? "key", actions: [...items].sort(byAction) })) });
  return groups;
}

/** เมนูที่บทบาท/ชุดสิทธิ์นี้เปิดได้ — SYS_ADMIN เห็นทุกเมนูเสมอ (Sidebar ตรวจ role ก่อน permission) */
function visibleMenus(codes: Set<string>, isSysAdmin: boolean) {
  return nav.flatMap((g) => g.items).filter((item) => isSysAdmin || codes.has(pageViewPermission[item.id].toUpperCase()));
}
const totalMenus = nav.reduce((n, g) => n + g.items.length, 0);

export function AdministrationPage({ refresh, allProjects }: { refresh: number; allProjects: ProjectItem[] }) {
  const [tab, setTab] = useState<"users" | "roles">(() => { try { return localStorage.getItem(tabKey) === "roles" ? "roles" : "users"; } catch { return "users"; } });
  const [users, setUsers] = useState<AdminUser[]>([]);
  const [roles, setRoles] = useState<AdminRole[]>([]);
  const [permissions, setPermissions] = useState<AdminPermission[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState("");
  const [version, setVersion] = useState(0);
  const [roleId, setRoleId] = useState("");
  const [rolesDirty, setRolesDirty] = useState(false);
  const tabsId = useId();

  useEffect(() => {
    const ctrl = new AbortController();
    // API ผู้ใช้แบ่งหน้า (สูงสุด 100/หน้า) — เดิมเรียกครั้งเดียวได้แค่ 20 คนแรก
    const loadUsers = async () => {
      const rows: AdminUser[] = [];
      for (let page = 1; page <= 50; page++) {
        const r = await getJson<UserListResult>(`${apiUrl}/admin/users?page=${page}&size=100`, ctrl.signal);
        rows.push(...r.items.rows);
        if (rows.length >= r.items.total || r.items.rows.length === 0) break;
      }
      return rows;
    };
    setLoading(true);
    Promise.all([loadUsers(), getJson<AdminRole[]>(`${apiUrl}/admin/roles`, ctrl.signal), getJson<AdminPermission[]>(`${apiUrl}/admin/permissions`, ctrl.signal)])
      .then(([u, r, p]) => { setUsers(u); setRoles(r); setPermissions(p); setLoadError(""); })
      .catch((e) => { if (!isAbortError(e)) setLoadError(errorText(e, "โหลดข้อมูลผู้ใช้และสิทธิ์ไม่สำเร็จ")); })
      .finally(() => { if (!ctrl.signal.aborted) setLoading(false); });
    return () => ctrl.abort();
  }, [refresh, version]);

  const activeRoles = useMemo(() => roles.filter((r) => r.isActive !== false), [roles]);
  const reload = () => setVersion((v) => v + 1);
  const switchTab = async (next: "users" | "roles") => {
    if (next === tab) return;
    if (tab === "roles" && rolesDirty && !await confirmDialog({ title: "ทิ้งการแก้ไขสิทธิ์?", message: "สิทธิ์ของบทบาทที่แก้ไว้ยังไม่ได้บันทึก — ออกจากแท็บนี้จะทิ้งการแก้ไขทั้งหมด", confirmLabel: "ทิ้งการแก้ไข", tone: "danger" })) return;
    setTab(next);
    try { localStorage.setItem(tabKey, next); } catch { /* จำแท็บไม่ได้ก็ไม่เป็นไร */ }
  };
  const tabs = [
    { key: "users" as const, icon: "group", label: "ผู้ใช้", count: users.length },
    { key: "roles" as const, icon: "shield_person", label: "บทบาทและสิทธิ์", count: activeRoles.length },
  ];

  return (
    <div className="ua-page">
      <header className="ua-header">
        <p className="ua-intro"><span className="material-symbols-outlined" aria-hidden="true">info</span><span>ผู้ใช้ได้สิทธิ์จาก <b>บทบาท</b> ที่กำหนดให้ และเห็นข้อมูลเฉพาะ <b>Project</b> ที่ได้รับ — สิทธิ์ที่เปลี่ยนมีผลเมื่อผู้ใช้ Login ใหม่</span></p>
        <div className="ua-tabs" role="tablist" aria-label="ส่วนของหน้า"
          onKeyDown={(e) => { if (e.key !== "ArrowLeft" && e.key !== "ArrowRight") return; e.preventDefault(); const next = tab === "users" ? "roles" : "users"; void switchTab(next); document.getElementById(`${tabsId}-${next}`)?.focus(); }}>
          {tabs.map((t) => (
            <button key={t.key} id={`${tabsId}-${t.key}`} type="button" role="tab" aria-selected={tab === t.key} aria-controls={`${tabsId}-${t.key}-panel`} tabIndex={tab === t.key ? 0 : -1} onClick={() => void switchTab(t.key)}>
              <span className="material-symbols-outlined" aria-hidden="true">{t.icon}</span>{t.label}<b>{t.count}</b>
            </button>
          ))}
        </div>
      </header>
      {loadError && <div className="inline-alert error" role="alert"><span>{loadError}{users.length ? " — แสดงข้อมูลชุดก่อนหน้า" : " — ตรวจสิทธิ์ ADMIN แล้วลองใหม่"}</span><button type="button" className="btn" onClick={reload}><span className="material-symbols-outlined" aria-hidden="true">refresh</span> ลองใหม่</button></div>}
      <div id={`${tabsId}-${tab}-panel`} role="tabpanel" aria-labelledby={`${tabsId}-${tab}`}>
        {tab === "users"
          ? <UsersTab users={users} roles={activeRoles} projects={allProjects} loading={loading} onChanged={reload} />
          : <RolesTab roles={activeRoles} users={users} permissions={permissions} loading={loading} roleId={roleId} onRoleId={setRoleId} onDirty={setRolesDirty} onChanged={reload} />}
      </div>
    </div>
  );
}

/* ================================ แท็บผู้ใช้ ================================ */

function UsersTab({ users, roles, projects, loading, onChanged }: { users: AdminUser[]; roles: AdminRole[]; projects: ProjectItem[]; loading: boolean; onChanged: () => void }) {
  const [status, setStatus] = useState<"" | "active" | "inactive" | "no-role" | "no-project">("");
  const [search, setSearch] = useState("");
  const [roleFilter, setRoleFilter] = useState("");
  const [projectFilter, setProjectFilter] = useState("");
  const [form, setForm] = useState<{ user?: AdminUser } | null>(null);
  const [passwordUser, setPasswordUser] = useState<AdminUser | null>(null);
  const [busyId, setBusyId] = useState("");
  const roleName = (code: string) => roles.find((r) => r.roleCode === code)?.roleName ?? code;
  const projectOf = (id: string) => projects.find((p) => p.projectId === id);

  const counts = {
    active: users.filter((u) => u.isActive).length,
    inactive: users.filter((u) => !u.isActive).length,
    "no-role": users.filter((u) => u.isActive && !u.roles.length).length,
    "no-project": users.filter((u) => u.isActive && !u.assignedProjectIds?.length && !u.roles.includes(SYS_ADMIN)).length,
  };
  const term = search.trim().toLowerCase();
  const rows = users.filter((u) =>
    (status === "" || (status === "active" && u.isActive) || (status === "inactive" && !u.isActive)
      || (status === "no-role" && u.isActive && !u.roles.length)
      || (status === "no-project" && u.isActive && !u.assignedProjectIds?.length && !u.roles.includes(SYS_ADMIN)))
    && (!roleFilter || u.roles.includes(roleFilter))
    && (!projectFilter || u.assignedProjectIds?.includes(projectFilter))
    && (!term || [u.displayName, u.username, u.email ?? ""].some((v) => v.toLowerCase().includes(term))))
    .sort((a, b) => Number(b.isActive) - Number(a.isActive) || a.displayName.localeCompare(b.displayName, "th"));
  const filterCount = [status, roleFilter, projectFilter, term].filter(Boolean).length;
  const clearFilters = () => { setStatus(""); setRoleFilter(""); setProjectFilter(""); setSearch(""); };

  const toggleActive = async (user: AdminUser) => {
    if (user.isActive && !await confirmDialog({ title: "ปิดใช้งานผู้ใช้", message: `ปิดใช้งาน ${user.displayName} (${user.username})?\nผู้ใช้นี้จะ Login ไม่ได้จนกว่าจะเปิดใช้งานอีกครั้ง ข้อมูลและประวัติงานยังอยู่ครบ`, confirmLabel: "ปิดใช้งาน", tone: "danger" })) return;
    setBusyId(user.userId);
    try {
      await apiFetch(`/admin/users/${user.userId}`, { method: "PUT", json: { displayName: user.displayName, email: user.email ?? null, isActive: !user.isActive }, fallbackMessage: "เปลี่ยนสถานะผู้ใช้ไม่สำเร็จ" });
      notify(user.isActive ? `ปิดใช้งาน ${user.displayName} แล้ว` : `เปิดใช้งาน ${user.displayName} แล้ว`, "success");
      onChanged();
    } catch (e) { notify(errorText(e, "เปลี่ยนสถานะผู้ใช้ไม่สำเร็จ"), "error"); } finally { setBusyId(""); }
  };

  const chips = [
    { key: "" as const, label: "ทั้งหมด", count: users.length },
    { key: "active" as const, label: "ใช้งาน", count: counts.active, dot: "green" },
    { key: "inactive" as const, label: "ปิดใช้งาน", count: counts.inactive, dot: "gray" },
    { key: "no-role" as const, label: "ยังไม่มีบทบาท", count: counts["no-role"], dot: "yellow", hint: "ผู้ใช้ที่ใช้งานอยู่แต่ยังไม่มีบทบาท — Login ได้แต่ไม่เห็นเมนูใด" },
    { key: "no-project" as const, label: "ยังไม่มี Project", count: counts["no-project"], dot: "yellow", hint: "ผู้ใช้ที่ใช้งานอยู่แต่ยังไม่ได้รับ Project — ไม่เห็นข้อมูลของ Project ใดเลย" },
  ].filter((c) => c.key === "" || c.key === "active" || c.key === "inactive" || c.count > 0 || status === c.key);

  return (
    <article className="card lp-card ua-users">
      <div className="lp-head">
        <div className="lp-title">
          <div><strong>{rows.length.toLocaleString()}</strong><span>ผู้ใช้{filterCount ? ` · กรอง ${filterCount} เงื่อนไข จากทั้งหมด ${users.length}` : ""}</span></div>
          <small>ใช้งาน {counts.active} · ปิดใช้งาน {counts.inactive}</small>
        </div>
        <div className="lp-actions">
          <button className="btn primary" type="button" onClick={() => setForm({})}><span className="material-symbols-outlined" aria-hidden="true">person_add</span>เพิ่มผู้ใช้</button>
        </div>
      </div>
      <div className="lp-chips" role="group" aria-label="กรองตามสถานะผู้ใช้">
        {chips.map((c) => (
          <button key={c.key || "all"} type="button" aria-pressed={status === c.key} title={c.hint} onClick={() => setStatus((cur) => cur === c.key ? "" : c.key)}>
            {c.dot && <span className={`lp-dot ${c.dot}`} aria-hidden="true" />}{c.label}<b>{c.count}</b>
          </button>
        ))}
      </div>
      <div className="lp-filters" role="group" aria-label="ตัวกรองผู้ใช้">
        <label className="lp-search"><span className="material-symbols-outlined" aria-hidden="true">search</span><input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="ค้นหาชื่อ, Username หรืออีเมล" aria-label="ค้นหาผู้ใช้" /></label>
        <select aria-label="บทบาท" value={roleFilter} onChange={(e) => setRoleFilter(e.target.value)}>
          <option value="">ทุกบทบาท</option>
          {roles.map((r) => <option key={r.roleId} value={r.roleCode}>{r.roleName}</option>)}
        </select>
        <select aria-label="Project" value={projectFilter} onChange={(e) => setProjectFilter(e.target.value)}>
          <option value="">ทุก Project</option>
          {projects.map((p) => <option key={p.projectId} value={p.projectId}>{p.projectCode} · {p.projectName}</option>)}
        </select>
        {filterCount > 0 && <button type="button" className="btn lp-clear" onClick={clearFilters}><span className="material-symbols-outlined" aria-hidden="true">filter_alt_off</span>ล้างตัวกรอง</button>}
      </div>
      <div className="lp-table-wrap">
        <table className="lp-table ua-table">
          <thead>
            <tr>
              <th className="lp-col-item">ผู้ใช้</th>
              <th className="ua-col-roles">บทบาท</th>
              <th className="ua-col-projects lp-hide-md">Project</th>
              <th className="lp-col-status">สถานะ</th>
              <th className="lp-col-date">Login ล่าสุด</th>
              <th className="lp-col-actions"><span className="sr-only">จัดการ</span></th>
            </tr>
          </thead>
          <tbody>
            {loading && !users.length && <tr className="lp-empty-row"><td colSpan={6}><div className="lp-empty" role="status"><span className="spinner" aria-hidden="true" /><b>กำลังโหลดผู้ใช้...</b></div></td></tr>}
            {!loading && !rows.length && <tr className="lp-empty-row"><td colSpan={6}><div className="lp-empty"><span className="material-symbols-outlined" aria-hidden="true">person_search</span><b>{filterCount ? "ไม่พบผู้ใช้ตามตัวกรอง" : "ยังไม่มีผู้ใช้"}</b><small>{filterCount ? "ลองเปลี่ยนคำค้นหรือตัวกรอง" : "กด “เพิ่มผู้ใช้” เพื่อสร้างบัญชีแรก"}</small>{filterCount > 0 && <button type="button" className="btn" onClick={clearFilters}>ล้างตัวกรอง</button>}</div></td></tr>}
            {rows.map((u) => {
              const ids = u.assignedProjectIds ?? [];
              return (
                <tr key={u.userId} className={u.isActive ? undefined : "is-inactive"}>
                  <td className="lp-col-item">
                    <button type="button" className="lp-item ua-user" onClick={() => setForm({ user: u })} aria-label={`แก้ไข ${u.displayName} (${u.username})`}>
                      <span className="ua-avatar" aria-hidden="true">{u.displayName.trim().slice(0, 1).toUpperCase() || "?"}</span>
                      <span className="ua-user-text"><span className="lp-name">{u.displayName}</span><small>{u.username}{u.email ? ` · ${u.email}` : ""}</small></span>
                    </button>
                  </td>
                  <td className="ua-col-roles">
                    {u.roles.length
                      ? <ul className="ua-tags" aria-label="บทบาท">{u.roles.map((r) => <li key={r} className={r === SYS_ADMIN ? "is-admin" : undefined}>{roleName(r)}</li>)}</ul>
                      : <span className="ua-warn"><span className="material-symbols-outlined" aria-hidden="true">warning</span>ยังไม่มีบทบาท</span>}
                  </td>
                  <td className="ua-col-projects lp-hide-md">
                    {u.roles.includes(SYS_ADMIN) && !ids.length
                      ? <span className="lp-muted">ทุก Project (SYS_ADMIN)</span>
                      : ids.length
                        ? <ul className="ua-tags is-project" aria-label="Project" title={ids.map((id) => { const p = projectOf(id); return p ? `${p.projectCode} · ${p.projectName}` : id; }).join("\n")}>
                            {ids.slice(0, 3).map((id) => <li key={id}>{projectOf(id)?.projectCode ?? id.slice(0, 8)}</li>)}
                            {ids.length > 3 && <li className="is-more">+{ids.length - 3}</li>}
                          </ul>
                        : <span className="ua-warn"><span className="material-symbols-outlined" aria-hidden="true">warning</span>ยังไม่มี Project</span>}
                  </td>
                  <td className="lp-col-status"><Badge tone={u.isActive ? "green" : "gray"}>{u.isActive ? "ใช้งาน" : "ปิดใช้งาน"}</Badge></td>
                  <td className="lp-col-date lp-labeled" data-label="Login ล่าสุด"><span className="lp-date">{u.lastLoginAt ? formatThaiDateTime(u.lastLoginAt) : "ยังไม่เคย"}</span></td>
                  <td className="lp-col-actions">
                    <div className="lp-row-actions">
                      <button type="button" title="แก้ไขข้อมูล บทบาท และ Project" aria-label={`แก้ไข ${u.username}`} onClick={() => setForm({ user: u })}><span className="material-symbols-outlined" aria-hidden="true">edit</span></button>
                      <button type="button" title="ตั้งรหัสผ่านใหม่" aria-label={`ตั้งรหัสผ่านใหม่ ${u.username}`} onClick={() => setPasswordUser(u)}><span className="material-symbols-outlined" aria-hidden="true">key</span></button>
                      <button type="button" className={u.isActive ? "danger" : undefined} disabled={busyId === u.userId} title={u.isActive ? "ปิดใช้งาน" : "เปิดใช้งาน"} aria-label={`${u.isActive ? "ปิดใช้งาน" : "เปิดใช้งาน"} ${u.username}`} onClick={() => toggleActive(u)}>
                        <span className="material-symbols-outlined" aria-hidden="true">{u.isActive ? "person_off" : "person_check"}</span>
                      </button>
                    </div>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      {form && <UserFormModal user={form.user} roles={roles} projects={projects} onClose={() => setForm(null)} onSaved={() => { setForm(null); onChanged(); }} />}
      {passwordUser && <PasswordModal user={passwordUser} onClose={() => setPasswordUser(null)} />}
    </article>
  );
}

function UserFormModal({ user, roles, projects, onClose, onSaved }: { user?: AdminUser; roles: AdminRole[]; projects: ProjectItem[]; onClose: () => void; onSaved: () => void }) {
  const creating = !user;
  const [username, setUsername] = useState("");
  const [displayName, setDisplayName] = useState(user?.displayName ?? "");
  const [email, setEmail] = useState(user?.email ?? "");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [active, setActive] = useState(user?.isActive ?? true);
  const [roleIds, setRoleIds] = useState<string[]>(() => roles.filter((r) => user?.roles.includes(r.roleCode)).map((r) => r.roleId));
  const [projectIds, setProjectIds] = useState<string[]>(user?.assignedProjectIds ?? []);
  const [projectQuery, setProjectQuery] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const titleId = useId();

  const chosenRoles = roles.filter((r) => roleIds.includes(r.roleId));
  const isSysAdmin = chosenRoles.some((r) => r.roleCode === SYS_ADMIN);
  const menus = visibleMenus(new Set(chosenRoles.flatMap((r) => r.permissions.map((c) => c.toUpperCase()))), isSysAdmin);
  const missing = [creating && !username.trim() && "Username", !displayName.trim() && "ชื่อที่แสดง", creating && password.length < 8 && "รหัสผ่าน (อย่างน้อย 8 ตัว)"].filter(Boolean) as string[];
  const pq = projectQuery.trim().toLowerCase();
  const shownProjects = projects.filter((p) => !pq || `${p.projectCode} ${p.projectName}`.toLowerCase().includes(pq));
  const toggle = (list: string[], id: string, on: boolean) => on ? [...new Set([...list, id])] : list.filter((x) => x !== id);

  const save = async () => {
    if (missing.length) return;
    setSaving(true); setError("");
    try {
      let userId = user?.userId;
      if (creating) {
        const created = await apiFetch<{ userId: string }>("/admin/users", { method: "POST", json: { username: username.trim(), displayName: displayName.trim(), email: email.trim() || null, password, roleIds }, fallbackMessage: "สร้างผู้ใช้ไม่สำเร็จ" });
        userId = created.userId;
      } else {
        await apiFetch(`/admin/users/${userId}`, { method: "PUT", json: { displayName: displayName.trim(), email: email.trim() || null, isActive: active }, fallbackMessage: "บันทึกข้อมูลผู้ใช้ไม่สำเร็จ" });
        await apiFetch(`/admin/users/${userId}/roles`, { method: "POST", json: { roleIds }, fallbackMessage: "บันทึกบทบาทไม่สำเร็จ" });
      }
      if (!creating || projectIds.length) await apiFetch(`/admin/users/${userId}/projects`, { method: "POST", json: { projectIds }, fallbackMessage: "บันทึก Project ไม่สำเร็จ" });
      notify(creating ? `สร้างผู้ใช้ ${username.trim()} แล้ว` : `บันทึก ${displayName.trim()} แล้ว — มีผลเมื่อผู้ใช้ Login ใหม่`, "success");
      onSaved();
    } catch (e) { setError(errorText(e, "บันทึกผู้ใช้ไม่สำเร็จ")); } finally { setSaving(false); }
  };

  return (
    <ModalShell labelledBy={titleId} className="ua-form ua-user-form" onDismiss={() => { if (!saving) onClose(); }}>
      <div className="modal-head">
        <div><h2 id={titleId}>{creating ? "เพิ่มผู้ใช้" : `แก้ไข ${user.displayName}`}</h2><small>{creating ? "สร้างบัญชี กำหนดบทบาท และ Project ที่เข้าถึงได้ในหน้าเดียว" : `Username ${user.username} — เปลี่ยน Username ไม่ได้`}</small></div>
        <button type="button" disabled={saving} aria-label="ปิดหน้าต่าง" onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
      </div>
      {error && <div className="inline-alert error" role="alert"><span>{error}</span></div>}
      <section className="ua-section" aria-labelledby={`${titleId}-account`}>
        <h3 id={`${titleId}-account`}><span className="ua-step">1</span>ข้อมูลบัญชี</h3>
        <div className="form-grid form-grid-2col">
          {creating && <label>Username <span className="required">*</span><input value={username} onChange={(e) => setUsername(e.target.value)} autoComplete="off" autoFocus /></label>}
          <label>ชื่อที่แสดง <span className="required">*</span><input value={displayName} onChange={(e) => setDisplayName(e.target.value)} /></label>
          <label>อีเมล<input type="email" value={email} onChange={(e) => setEmail(e.target.value)} /></label>
          {creating && <label>รหัสผ่าน <span className="required">*</span>
            <span className="ua-password"><input type={showPassword ? "text" : "password"} value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="new-password" aria-describedby={`${titleId}-pw`} />
              <button type="button" aria-label={showPassword ? "ซ่อนรหัสผ่าน" : "แสดงรหัสผ่าน"} aria-pressed={showPassword} onClick={() => setShowPassword((x) => !x)}><span className="material-symbols-outlined" aria-hidden="true">{showPassword ? "visibility_off" : "visibility"}</span></button></span>
            <small id={`${titleId}-pw`} className={password && password.length < 8 ? "is-error" : undefined}>อย่างน้อย 8 ตัวอักษร{password ? ` · ตอนนี้ ${password.length} ตัว` : ""}</small>
          </label>}
          {!creating && <label className="ua-switch"><input type="checkbox" checked={active} onChange={(e) => setActive(e.target.checked)} /><span><b>อนุญาตให้ Login</b><small>{active ? "ใช้งานอยู่" : "ปิดใช้งาน — Login ไม่ได้ ข้อมูลยังอยู่ครบ"}</small></span></label>}
        </div>
      </section>
      <section className="ua-section" aria-labelledby={`${titleId}-roles`}>
        <h3 id={`${titleId}-roles`}><span className="ua-step">2</span>บทบาท<small>สิทธิ์ของผู้ใช้ = สิทธิ์รวมของทุกบทบาทที่เลือก</small></h3>
        <div className="ua-role-picks">
          {roles.map((r) => {
            const count = visibleMenus(new Set(r.permissions.map((c) => c.toUpperCase())), r.roleCode === SYS_ADMIN).length;
            return (
              <label key={r.roleId} className={roleIds.includes(r.roleId) ? "is-selected" : undefined}>
                <input type="checkbox" checked={roleIds.includes(r.roleId)} onChange={(e) => setRoleIds((cur) => toggle(cur, r.roleId, e.target.checked))} />
                <span><b>{r.roleName}</b><small>{r.roleCode === SYS_ADMIN ? "ทุกเมนู ทุกสิทธิ์" : `เห็น ${count} เมนู · ${r.permissions.length} สิทธิ์`}</small></span>
              </label>
            );
          })}
        </div>
        <p className={`ua-preview${menus.length ? "" : " is-warning"}`} role="status">
          <span className="material-symbols-outlined" aria-hidden="true">{menus.length ? "visibility" : "visibility_off"}</span>
          {menus.length
            ? <span>ผู้ใช้นี้จะเห็น <b>{menus.length}</b> จาก {totalMenus} เมนู: {menus.map((m) => m.label).join(", ")}</span>
            : <span>ยังไม่เห็นเมนูใดเลย — เลือกอย่างน้อย 1 บทบาท</span>}
        </p>
      </section>
      <section className="ua-section" aria-labelledby={`${titleId}-projects`}>
        <h3 id={`${titleId}-projects`}><span className="ua-step">3</span>Project ที่เข้าถึงได้<small>เลือกแล้ว {projectIds.length} จาก {projects.length}</small></h3>
        <div className="ua-project-tools">
          {projects.length > 8 && <label className="ua-mini-search"><span className="material-symbols-outlined" aria-hidden="true">search</span><input value={projectQuery} onChange={(e) => setProjectQuery(e.target.value)} placeholder="ค้นหา Project" aria-label="ค้นหา Project" /></label>}
          <button type="button" className="btn" disabled={!shownProjects.length} onClick={() => setProjectIds((cur) => [...new Set([...cur, ...shownProjects.map((p) => p.projectId)])])}>เลือกทั้งหมด{pq ? "ที่แสดง" : ""}</button>
          <button type="button" className="btn" disabled={!projectIds.length} onClick={() => setProjectIds([])}>ล้าง</button>
        </div>
        <div className="ua-project-picks">
          {shownProjects.map((p) => (
            <label key={p.projectId} className={projectIds.includes(p.projectId) ? "is-selected" : undefined}>
              <input type="checkbox" checked={projectIds.includes(p.projectId)} onChange={(e) => setProjectIds((cur) => toggle(cur, p.projectId, e.target.checked))} />
              <span><b>{p.projectCode}</b><small>{p.projectName}</small></span>
            </label>
          ))}
          {!projects.length && <p className="lp-muted">ยังไม่มี Project ในระบบ</p>}
          {projects.length > 0 && !shownProjects.length && <p className="lp-muted">ไม่พบ Project ที่ค้นหา</p>}
        </div>
        {!projectIds.length && !isSysAdmin && <p className="ua-preview is-warning" role="status"><span className="material-symbols-outlined" aria-hidden="true">warning</span><span>ไม่ได้เลือก Project — ผู้ใช้จะไม่เห็นข้อมูลของ Project ใดเลย</span></p>}
      </section>
      <div className="modal-actions ua-form-actions">
        <span className={`ua-status${missing.length ? " is-missing" : ""}`} role="status">{missing.length ? `ยังขาด: ${missing.join(", ")}` : "ข้อมูลครบ พร้อมบันทึก"}</span>
        <button type="button" className="btn" disabled={saving} onClick={onClose}>ยกเลิก</button>
        <button type="button" className="btn primary" disabled={saving || missing.length > 0} onClick={save}>
          {saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">{creating ? "person_add" : "check"}</span>{creating ? "สร้างผู้ใช้" : "บันทึก"}</>}
        </button>
      </div>
    </ModalShell>
  );
}

function PasswordModal({ user, onClose }: { user: AdminUser; onClose: () => void }) {
  const [password, setPassword] = useState("");
  const [show, setShow] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const titleId = useId();
  const save = async () => {
    if (password.length < 8) return;
    setSaving(true); setError("");
    try {
      await apiFetch(`/admin/users/${user.userId}/reset-password`, { method: "POST", json: { newPassword: password }, fallbackMessage: "ตั้งรหัสผ่านใหม่ไม่สำเร็จ" });
      notify(`ตั้งรหัสผ่านใหม่ให้ ${user.displayName} แล้ว`, "success");
      onClose();
    } catch (e) { setError(errorText(e, "ตั้งรหัสผ่านใหม่ไม่สำเร็จ")); } finally { setSaving(false); }
  };
  return (
    <ModalShell labelledBy={titleId} className="ua-form ua-password-form" onDismiss={() => { if (!saving) onClose(); }}>
      <div className="modal-head">
        <div><h2 id={titleId}>ตั้งรหัสผ่านใหม่</h2><small>{user.displayName} ({user.username}) — แจ้งรหัสใหม่ให้ผู้ใช้ด้วยช่องทางที่ปลอดภัย</small></div>
        <button type="button" disabled={saving} aria-label="ปิดหน้าต่าง" onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
      </div>
      {error && <div className="inline-alert error" role="alert"><span>{error}</span></div>}
      <form className="form-grid" onSubmit={(e) => { e.preventDefault(); void save(); }}>
        <label>รหัสผ่านใหม่ <span className="required">*</span>
          <span className="ua-password"><input type={show ? "text" : "password"} value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="new-password" autoFocus aria-describedby={`${titleId}-hint`} />
            <button type="button" aria-label={show ? "ซ่อนรหัสผ่าน" : "แสดงรหัสผ่าน"} aria-pressed={show} onClick={() => setShow((x) => !x)}><span className="material-symbols-outlined" aria-hidden="true">{show ? "visibility_off" : "visibility"}</span></button></span>
          <small id={`${titleId}-hint`} className={password && password.length < 8 ? "is-error" : undefined}>อย่างน้อย 8 ตัวอักษร{password ? ` · ตอนนี้ ${password.length} ตัว` : ""}</small>
        </label>
      </form>
      <div className="modal-actions">
        <button type="button" className="btn" disabled={saving} onClick={onClose}>ยกเลิก</button>
        <button type="button" className="btn primary" disabled={saving || password.length < 8} onClick={save}>{saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">key</span>ตั้งรหัสผ่านใหม่</>}</button>
      </div>
    </ModalShell>
  );
}

/* ============================ แท็บบทบาทและสิทธิ์ ============================ */

function RowToggle({ checked, mixed, label, onChange }: { checked: boolean; mixed: boolean; label: string; onChange: (on: boolean) => void }) {
  const ref = useRef<HTMLInputElement>(null);
  useEffect(() => { if (ref.current) ref.current.indeterminate = mixed; }, [mixed]);
  return <input ref={ref} type="checkbox" className="ua-row-toggle" checked={checked} aria-label={label} onChange={(e) => onChange(e.target.checked)} />;
}

function RolesTab({ roles, users, permissions, loading, roleId, onRoleId, onDirty, onChanged }: {
  roles: AdminRole[]; users: AdminUser[]; permissions: AdminPermission[]; loading: boolean; roleId: string;
  onRoleId: (id: string) => void; onDirty: (dirty: boolean) => void; onChanged: () => void;
}) {
  const role = roles.find((r) => r.roleId === roleId) ?? roles[0];
  const catalog = useMemo(() => buildCatalog(permissions), [permissions]);
  const original = useMemo(() => new Set(permissions.filter((p) => role?.permissions.some((c) => c.toUpperCase() === p.permissionCode.toUpperCase())).map((p) => p.permissionId)), [permissions, role]);
  const [selected, setSelected] = useState<Set<string>>(original);
  const [query, setQuery] = useState("");
  const [onlyGranted, setOnlyGranted] = useState(false);
  const [saving, setSaving] = useState(false);
  const [roleForm, setRoleForm] = useState<"create" | "edit" | null>(null);
  useEffect(() => { setSelected(original); }, [original]);

  const changes = [...selected].filter((id) => !original.has(id)).length + [...original].filter((id) => !selected.has(id)).length;
  useEffect(() => { onDirty(changes > 0); }, [changes, onDirty]);
  useEffect(() => () => onDirty(false), [onDirty]);
  const isSysAdmin = role?.roleCode === SYS_ADMIN;
  const roleUsers = role ? users.filter((u) => u.roles.includes(role.roleCode)) : [];
  const selectedCodes = new Set(permissions.filter((p) => selected.has(p.permissionId)).map((p) => p.permissionCode.toUpperCase()));
  const menus = visibleMenus(selectedCodes, isSysAdmin);

  const pickRole = async (id: string) => {
    if (id === role?.roleId) return;
    if (changes > 0 && !await confirmDialog({ title: "ทิ้งการแก้ไขสิทธิ์?", message: `สิทธิ์ของ ${role?.roleName} ที่แก้ไว้ ${changes} รายการยังไม่ได้บันทึก — เปลี่ยนบทบาทจะทิ้งการแก้ไขนี้`, confirmLabel: "ทิ้งการแก้ไข", tone: "danger" })) return;
    setQuery(""); onRoleId(id);
  };
  const setMany = (ids: string[], on: boolean) => setSelected((cur) => { const next = new Set(cur); ids.forEach((id) => on ? next.add(id) : next.delete(id)); return next; });
  const rowPerms = (row: CatalogRow) => [...(row.access ? [row.access] : []), ...row.actions];

  const term = query.trim().toLowerCase();
  const shownGroups = catalog.map((g) => ({
    ...g,
    rows: g.rows.filter((row) => {
      const perms = rowPerms(row);
      if (onlyGranted && !perms.some((p) => selected.has(p.permissionId))) return false;
      return !term || row.label.toLowerCase().includes(term) || g.label.toLowerCase().includes(term)
        || perms.some((p) => p.permissionCode.toLowerCase().includes(term) || actionLabel(p.permissionCode).toLowerCase().includes(term));
    }),
  })).filter((g) => g.rows.length > 0);
  const shownIds = shownGroups.flatMap((g) => g.rows.flatMap((r) => rowPerms(r).map((p) => p.permissionId)));
  const filtering = !!term || onlyGranted;

  const save = async () => {
    if (!role) return;
    setSaving(true);
    try {
      await apiFetch(`/admin/roles/${role.roleId}/permissions`, { method: "POST", json: { permissionIds: [...selected] }, fallbackMessage: "บันทึกสิทธิ์ไม่สำเร็จ" });
      notify(`บันทึกสิทธิ์ของ ${role.roleName} แล้ว — ผู้ใช้ ${roleUsers.length} คนต้อง Login ใหม่เพื่อให้มีผล`, "success");
      onChanged();
    } catch (e) { notify(errorText(e, "บันทึกสิทธิ์ไม่สำเร็จ"), "error"); } finally { setSaving(false); }
  };
  const remove = async () => {
    if (!role || !await confirmDialog({ title: "ลบบทบาท", message: `ลบบทบาท ${role.roleName} (${role.roleCode})?\nบทบาทจะถูกปิดใช้งานและไม่แสดงในรายการอีก`, confirmLabel: "ลบบทบาท", tone: "danger" })) return;
    try {
      await apiFetch(`/admin/roles/${role.roleId}`, { method: "DELETE", fallbackMessage: "ลบบทบาทไม่สำเร็จ" });
      notify(`ลบบทบาท ${role.roleName} แล้ว`, "success");
      onRoleId(""); onChanged();
    } catch (e) { notify(errorText(e, "ลบบทบาทไม่สำเร็จ"), "error"); }
  };
  const deleteBlock = isSysAdmin ? "SYS_ADMIN ลบไม่ได้" : roleUsers.length ? `ยังมีผู้ใช้ ${roleUsers.length} คนในบทบาทนี้ — ย้ายผู้ใช้ออกก่อน` : "";

  if (loading && !roles.length) return <div className="card ua-loading" role="status"><span className="spinner" aria-hidden="true" /> กำลังโหลดบทบาทและสิทธิ์...</div>;
  return (
    <div className="ua-roles">
      <aside className="card ua-role-list" aria-label="บทบาท">
        <div className="ua-role-list-head"><h3>บทบาท <b>{roles.length}</b></h3><button type="button" className="btn" onClick={() => setRoleForm("create")}><span className="material-symbols-outlined" aria-hidden="true">add</span>บทบาท</button></div>
        <ul>
          {roles.map((r) => {
            const count = users.filter((u) => u.roles.includes(r.roleCode)).length;
            const menuCount = visibleMenus(new Set(r.permissions.map((c) => c.toUpperCase())), r.roleCode === SYS_ADMIN).length;
            return (
              <li key={r.roleId}>
                <button type="button" aria-pressed={r.roleId === role?.roleId} onClick={() => void pickRole(r.roleId)}>
                  <span className="ua-role-icon" aria-hidden="true"><span className="material-symbols-outlined">{r.roleCode === SYS_ADMIN ? "admin_panel_settings" : "badge"}</span></span>
                  <span className="ua-role-text"><b>{r.roleName}</b><small>{count} ผู้ใช้ · {r.roleCode === SYS_ADMIN ? "ทุกเมนู" : `${menuCount} เมนู`}</small></span>
                  {r.roleId === role?.roleId && changes > 0 && <span className="ua-dirty-dot" title="มีการแก้ไขที่ยังไม่บันทึก"><span className="sr-only">มีการแก้ไขที่ยังไม่บันทึก</span></span>}
                </button>
              </li>
            );
          })}
        </ul>
      </aside>
      {role && <section className="card ua-role-detail" aria-label={`สิทธิ์ของ ${role.roleName}`}>
        <header className="ua-role-head">
          <div>
            <span className="ua-eyebrow">บทบาท · {role.roleCode}</span>
            <h3>{role.roleName}</h3>
            {role.description && <p>{role.description}</p>}
          </div>
          <div className="ua-role-actions">
            <button type="button" className="btn" onClick={() => setRoleForm("edit")}><span className="material-symbols-outlined" aria-hidden="true">edit</span>แก้ไข</button>
            <button type="button" className="btn ua-danger" disabled={!!deleteBlock} title={deleteBlock || undefined} onClick={remove}><span className="material-symbols-outlined" aria-hidden="true">delete</span>ลบ</button>
          </div>
        </header>
        <dl className="ua-role-stats">
          <div><dt>ผู้ใช้</dt><dd>{roleUsers.length}<small title={roleUsers.map((u) => u.displayName).join("\n") || undefined}>{roleUsers.length ? roleUsers.slice(0, 3).map((u) => u.displayName).join(", ") + (roleUsers.length > 3 ? ` +${roleUsers.length - 3}` : "") : "ยังไม่มีผู้ใช้"}</small></dd></div>
          <div><dt>เห็นเมนู</dt><dd>{menus.length}<small>จาก {totalMenus} เมนู</small></dd></div>
          <div><dt>สิทธิ์ที่ให้</dt><dd>{selected.size}<small>จาก {permissions.length} สิทธิ์</small></dd></div>
        </dl>
        {isSysAdmin && <p className="ua-note"><span className="material-symbols-outlined" aria-hidden="true">info</span>SYS_ADMIN เข้าได้ทุกเมนูและทำได้ทุกอย่างเสมอ ไม่ขึ้นกับการติ๊กด้านล่าง</p>}
        <div className="ua-perm-tools">
          <label className="ua-mini-search"><span className="material-symbols-outlined" aria-hidden="true">search</span><input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="ค้นหาเมนูหรือสิทธิ์ เช่น Defect, ลบ, EXPORT" aria-label="ค้นหาเมนูหรือสิทธิ์" /></label>
          <label className="ua-only"><input type="checkbox" checked={onlyGranted} onChange={(e) => setOnlyGranted(e.target.checked)} />เฉพาะที่ให้สิทธิ์แล้ว</label>
          <span className="ua-perm-bulk">
            <button type="button" className="btn" disabled={!shownIds.length} onClick={() => setMany(shownIds, true)}>ให้ทั้งหมด{filtering ? "ที่แสดง" : ""}</button>
            <button type="button" className="btn" disabled={!shownIds.some((id) => selected.has(id))} onClick={() => setMany(shownIds, false)}>ล้าง{filtering ? "ที่แสดง" : "ทั้งหมด"}</button>
          </span>
        </div>
        <p className="ua-legend"><span><span className="ua-chip-demo is-access" aria-hidden="true" />เข้าเมนู = เห็นเมนูใน Sidebar</span><span><span className="ua-chip-demo" aria-hidden="true" />สิทธิ์ทำงานในเมนูนั้น</span><span>ชี้ที่ปุ่มเพื่อดูรหัสสิทธิ์</span></p>
        <div className="ua-perm-groups">
          {!shownGroups.length && <div className="lp-empty"><span className="material-symbols-outlined" aria-hidden="true">search_off</span><b>ไม่พบเมนูหรือสิทธิ์ที่ค้นหา</b></div>}
          {shownGroups.map((g) => {
            const menuRows = g.rows.filter((r) => r.accessCode);
            const openCount = menuRows.filter((r) => isSysAdmin || (r.access && selected.has(r.access.permissionId))).length;
            return (
              <section key={g.label} className="ua-perm-group">
                <h4>{g.label}{menuRows.length > 0 && <small>เห็น {openCount}/{menuRows.length} เมนู</small>}</h4>
                <ul>
                  {g.rows.map((row) => {
                    const perms = rowPerms(row);
                    const on = perms.filter((p) => selected.has(p.permissionId)).length;
                    const opens = !row.accessCode || isSysAdmin || (row.access ? selected.has(row.access.permissionId) : false);
                    return (
                      <li key={row.key} className={opens ? undefined : "is-closed"}>
                        <div className="ua-perm-menu">
                          {perms.length > 0 ? <RowToggle checked={on === perms.length} mixed={on > 0 && on < perms.length} label={`ให้ทุกสิทธิ์ของ ${row.label}`} onChange={(v) => setMany(perms.map((p) => p.permissionId), v)} /> : <span className="ua-row-toggle-space" />}
                          <span className="material-symbols-outlined" aria-hidden="true">{row.icon}</span>
                          <span className="ua-perm-name"><b>{row.label}</b><small>{!row.accessCode ? `${on}/${perms.length} สิทธิ์` : !row.access ? "เฉพาะ SYS_ADMIN" : opens ? "เห็นเมนู" : "ไม่เห็นเมนู"}</small></span>
                        </div>
                        <div className="ua-perm-chips" role="group" aria-label={`สิทธิ์ของ ${row.label}`}>
                          {row.accessCode && !row.access && <span className="ua-chip is-locked" title={`ต้องใช้สิทธิ์ ${row.accessCode} ซึ่งยังไม่มีในระบบ — เฉพาะ SYS_ADMIN เข้าเมนูนี้ได้`}><span className="material-symbols-outlined" aria-hidden="true">lock</span>เข้าเมนู</span>}
                          {perms.map((p) => {
                            const isAccess = p.permissionId === row.access?.permissionId;
                            const checked = selected.has(p.permissionId);
                            return (
                              <label key={p.permissionId} className={`ua-chip${isAccess ? " is-access" : ""}${checked ? " is-on" : ""}`} title={`${p.permissionCode}${isAccess ? " — สิทธิ์นี้ทำให้เห็นเมนูใน Sidebar" : ""}`}>
                                <input type="checkbox" checked={checked} onChange={(e) => setMany([p.permissionId], e.target.checked)} />
                                {isAccess && <span className="material-symbols-outlined" aria-hidden="true">login</span>}
                                {isAccess ? "เข้าเมนู" : actionLabel(p.permissionCode)}
                                <span className="sr-only"> ({p.permissionCode})</span>
                              </label>
                            );
                          })}
                        </div>
                      </li>
                    );
                  })}
                </ul>
              </section>
            );
          })}
        </div>
        <footer className={`ua-save-bar${changes ? " is-dirty" : ""}`}>
          <span role="status">{changes ? <><b>แก้ไข {changes} รายการ</b> ยังไม่บันทึก — ผู้ใช้ {roleUsers.length} คนต้อง Login ใหม่หลังบันทึก</> : "ไม่มีการเปลี่ยนแปลง"}</span>
          <button type="button" className="btn" disabled={!changes || saving} onClick={() => setSelected(original)}>ยกเลิกการแก้ไข</button>
          <button type="button" className="btn primary" disabled={!changes || saving} onClick={save}>{saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">check</span>บันทึกสิทธิ์</>}</button>
        </footer>
      </section>}
      {roleForm && <RoleFormModal mode={roleForm} role={roleForm === "edit" ? role : undefined} roles={roles} permissions={permissions} onClose={() => setRoleForm(null)}
        onSaved={(id) => { setRoleForm(null); if (id) onRoleId(id); onChanged(); }} />}
    </div>
  );
}

function RoleFormModal({ mode, role, roles, permissions, onClose, onSaved }: { mode: "create" | "edit"; role?: AdminRole; roles: AdminRole[]; permissions: AdminPermission[]; onClose: () => void; onSaved: (roleId?: string) => void }) {
  const [code, setCode] = useState(role?.roleCode ?? "");
  const [name, setName] = useState(role?.roleName ?? "");
  const [description, setDescription] = useState(role?.description ?? "");
  const [copyFrom, setCopyFrom] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const titleId = useId();
  const codeInvalid = mode === "create" && !!code && !/^[A-Z][A-Z0-9_]{1,49}$/.test(code);
  const duplicate = mode === "create" && roles.some((r) => r.roleCode === code);
  const missing = [mode === "create" && !code && "Role Code", !name.trim() && "ชื่อบทบาท"].filter(Boolean) as string[];

  const save = async () => {
    if (missing.length || codeInvalid || duplicate) return;
    setSaving(true); setError("");
    try {
      if (mode === "create") {
        const created = await apiFetch<AdminRole>("/admin/roles", { method: "POST", json: { roleCode: code, roleName: name.trim(), description: description.trim() }, fallbackMessage: "สร้างบทบาทไม่สำเร็จ" });
        const source = roles.find((r) => r.roleId === copyFrom);
        if (source && created?.roleId) {
          const ids = permissions.filter((p) => source.permissions.includes(p.permissionCode)).map((p) => p.permissionId);
          await apiFetch(`/admin/roles/${created.roleId}/permissions`, { method: "POST", json: { permissionIds: ids }, fallbackMessage: "คัดลอกสิทธิ์ไม่สำเร็จ" });
        }
        notify(`สร้างบทบาท ${name.trim()} แล้ว${source ? ` (คัดลอกสิทธิ์จาก ${source.roleName})` : " — กำหนดสิทธิ์ต่อได้เลย"}`, "success");
        onSaved(created?.roleId);
      } else if (role) {
        await apiFetch(`/admin/roles/${role.roleId}`, { method: "PUT", json: { roleName: name.trim(), description: description.trim() }, fallbackMessage: "บันทึกบทบาทไม่สำเร็จ" });
        notify(`บันทึกบทบาท ${name.trim()} แล้ว`, "success");
        onSaved(role.roleId);
      }
    } catch (e) { setError(errorText(e, "บันทึกบทบาทไม่สำเร็จ")); } finally { setSaving(false); }
  };

  return (
    <ModalShell labelledBy={titleId} className="ua-form ua-role-form" onDismiss={() => { if (!saving) onClose(); }}>
      <div className="modal-head">
        <div><h2 id={titleId}>{mode === "create" ? "เพิ่มบทบาท" : `แก้ไขบทบาท ${role?.roleName ?? ""}`}</h2><small>{mode === "create" ? "ตั้งชื่อบทบาท แล้วกำหนดสิทธิ์ในหน้าบทบาทต่อ" : `Role Code ${role?.roleCode} — เปลี่ยนไม่ได้`}</small></div>
        <button type="button" disabled={saving} aria-label="ปิดหน้าต่าง" onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
      </div>
      {error && <div className="inline-alert error" role="alert"><span>{error}</span></div>}
      <div className="form-grid form-grid-2col">
        {mode === "create" && <label>Role Code <span className="required">*</span><input value={code} onChange={(e) => setCode(e.target.value.toUpperCase().replace(/\s+/g, "_"))} placeholder="เช่น QA_REVIEWER" aria-invalid={codeInvalid || duplicate} autoFocus />
          <small className={codeInvalid || duplicate ? "is-error" : undefined}>{duplicate ? "รหัสนี้มีอยู่แล้ว" : "ตัวพิมพ์ใหญ่ ตัวเลข และ _ เท่านั้น"}</small></label>}
        <label>ชื่อบทบาท <span className="required">*</span><input value={name} onChange={(e) => setName(e.target.value)} placeholder="เช่น QA Reviewer" autoFocus={mode === "edit"} /></label>
        {mode === "create" && <label>คัดลอกสิทธิ์จาก<select value={copyFrom} onChange={(e) => setCopyFrom(e.target.value)}>
          <option value="">ไม่คัดลอก (เริ่มจากไม่มีสิทธิ์)</option>
          {roles.filter((r) => r.roleCode !== SYS_ADMIN).map((r) => <option key={r.roleId} value={r.roleId}>{r.roleName} · {r.permissions.length} สิทธิ์</option>)}
        </select><small>ปรับสิทธิ์เพิ่ม/ลดได้หลังสร้าง</small></label>}
        <label className="full">รายละเอียด<textarea value={description} onChange={(e) => setDescription(e.target.value)} rows={3} placeholder="บทบาทนี้ใช้กับใคร ทำอะไรได้บ้าง" /></label>
      </div>
      <div className="modal-actions ua-form-actions">
        <span className={`ua-status${missing.length || codeInvalid || duplicate ? " is-missing" : ""}`} role="status">{missing.length ? `ยังขาด: ${missing.join(", ")}` : codeInvalid || duplicate ? "Role Code ไม่ถูกต้อง" : "ข้อมูลครบ พร้อมบันทึก"}</span>
        <button type="button" className="btn" disabled={saving} onClick={onClose}>ยกเลิก</button>
        <button type="button" className="btn primary" disabled={saving || missing.length > 0 || codeInvalid || duplicate} onClick={save}>{saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">check</span>{mode === "create" ? "สร้างบทบาท" : "บันทึก"}</>}</button>
      </div>
    </ModalShell>
  );
}
