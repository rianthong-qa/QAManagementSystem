import { useEffect, useRef, useState } from "react";
import { confirmDialog } from "../components/dialogStore";
import { formatThaiDateTime } from "../dateTime";
import { apiUrl } from "../api";
import type { AutomationAgentItem, AutomationScheduleListItem, AutomationScheduleDetailItem, AutomationScheduleRunItem, AutomationScheduleNotificationItem, AutomationBuildTriggerPolicyItem, AutomationBuildTriggerRunItem, AutomationWebhookTokenItem, AutomationWebhookDeliveryItem, AutomationExecutionItem } from "./types";
import { fetchJson, isAbort, useBuildsAndEnvironments, DAY_LABELS } from "./shared";
import { ModalShell, Badge } from "./ui";

const FALLBACK_TIMEZONES = ["UTC", "Asia/Bangkok", "Asia/Singapore", "Asia/Tokyo", "Asia/Ho_Chi_Minh", "Asia/Jakarta", "Asia/Kolkata", "Europe/London", "Europe/Paris", "America/New_York", "America/Los_Angeles"];

const timezoneOptions = (): string[] => {
  try {
    const supported = (Intl as unknown as { supportedValuesOf?: (key: string) => string[] }).supportedValuesOf?.("timeZone");
    if (supported && supported.length) return supported;
  } catch { /* older browser without Intl.supportedValuesOf — fall back to a curated list */ }
  return FALLBACK_TIMEZONES;
};

const describeSchedule = (s: { frequency: string; daysOfWeekMask: number; runAtTime: string; onceOnDate?: string }) => {
  const time = s.runAtTime.slice(0, 5);
  if (s.frequency === "Once") return `ครั้งเดียว ${s.onceOnDate ?? "-"} ${time}`;
  if (s.frequency === "Weekly") {
    const days = DAY_LABELS.filter((d) => (s.daysOfWeekMask & (1 << d.value)) !== 0).map((d) => d.label).join(",");
    return `ทุกสัปดาห์ (${days || "-"}) ${time}`;
  }
  return `ทุกวัน ${time}`;
};

export function AutomationScheduleTab({ projectId, releaseId, headers, canEdit, agents, setExecDetail }: {
  projectId: string; releaseId?: string; headers: Record<string, string>; canEdit: boolean; agents: AutomationAgentItem[]; setExecDetail: (v: AutomationExecutionItem | null) => void;
}) {
  const [schedules, setSchedules] = useState<AutomationScheduleListItem[]>([]);
  const [activeFilter, setActiveFilter] = useState<"all" | "active" | "inactive">("active");
  const [reload, setReload] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [createModal, setCreateModal] = useState(false);
  const [editSchedule, setEditSchedule] = useState<AutomationScheduleDetailItem | null>(null);
  const [runHistory, setRunHistory] = useState<{ name: string; runs: AutomationScheduleRunItem[] } | null>(null);
  const [unreadCount, setUnreadCount] = useState(0);
  const [notifications, setNotifications] = useState<AutomationScheduleNotificationItem[] | null>(null);

  useEffect(() => {
    if (!projectId) return;
    const qs = new URLSearchParams({ projectId });
    if (activeFilter !== "all") qs.set("isActive", activeFilter === "active" ? "true" : "false");
    const ctrl = new AbortController();
    fetchJson(`${apiUrl}/automation/schedules?${qs}`, headers, ctrl.signal).then((s) => setSchedules(Array.isArray(s) ? s : [])).catch((e) => !isAbort(e) && setError("โหลด Schedule ไม่สำเร็จ"));
    return () => ctrl.abort();
  }, [projectId, activeFilter, headers, reload]);

  useEffect(() => {
    if (!projectId) return;
    fetch(`${apiUrl}/automation/schedules/notifications/unread-count?projectId=${projectId}`, { headers }).then((r) => (r.ok ? r.json() : 0)).then((n) => setUnreadCount(typeof n === "number" ? n : 0)).catch(() => { /* badge just stays at its last known value */ });
  }, [projectId, headers, reload]);

  const openNotifications = async () => {
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/schedules/notifications?projectId=${projectId}&take=100`, { headers });
      if (!r.ok) throw new Error("โหลดการแจ้งเตือนไม่สำเร็จ");
      setNotifications(await r.json());
    } catch (e) { setError(e instanceof Error ? e.message : "โหลดการแจ้งเตือนไม่สำเร็จ"); }
  };

  const markNotificationRead = async (n: AutomationScheduleNotificationItem) => {
    if (n.isRead) return;
    try {
      await fetch(`${apiUrl}/automation/schedules/notifications/${n.automationScheduleNotificationId}/read?projectId=${projectId}`, { method: "POST", headers });
      setNotifications((prev) => prev?.map((x) => x.automationScheduleNotificationId === n.automationScheduleNotificationId ? { ...x, isRead: true } : x) ?? null);
      setUnreadCount((c) => Math.max(0, c - 1));
    } catch { /* best-effort — the notification just stays unread until the next open */ }
  };

  const markAllNotificationsRead = async () => {
    try {
      await fetch(`${apiUrl}/automation/schedules/notifications/mark-all-read?projectId=${projectId}`, { method: "POST", headers });
      setNotifications((prev) => prev?.map((x) => ({ ...x, isRead: true })) ?? null);
      setUnreadCount(0);
    } catch { /* best-effort */ }
  };

  const openNotificationExecution = async (n: AutomationScheduleNotificationItem) => {
    await markNotificationRead(n);
    try {
      const r = await fetch(`${apiUrl}/automation/executions/${n.automationExecutionId}?projectId=${projectId}`, { headers });
      if (!r.ok) throw new Error("โหลด Execution ไม่สำเร็จ");
      setExecDetail(await r.json());
      setNotifications(null);
    } catch (e) { setError(e instanceof Error ? e.message : "โหลด Execution ไม่สำเร็จ"); }
  };

  const openEdit = async (id: string) => {
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/schedules/${id}?projectId=${projectId}`, { headers });
      if (!r.ok) throw new Error("โหลด Schedule ไม่สำเร็จ");
      setEditSchedule(await r.json());
    } catch (e) { setError(e instanceof Error ? e.message : "โหลด Schedule ไม่สำเร็จ"); }
  };

  const createSchedule = async (body: Record<string, unknown>) => {
    setBusy(true); setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/schedules?projectId=${projectId}`, { method: "POST", headers, body: JSON.stringify(body) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "สร้าง Schedule ไม่สำเร็จ"); }
      setCreateModal(false); setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "สร้าง Schedule ไม่สำเร็จ"); } finally { setBusy(false); }
  };

  const updateSchedule = async (id: string, body: Record<string, unknown>) => {
    setBusy(true); setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/schedules/${id}?projectId=${projectId}`, { method: "PUT", headers, body: JSON.stringify(body) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "แก้ไข Schedule ไม่สำเร็จ"); }
      setEditSchedule(null); setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "แก้ไข Schedule ไม่สำเร็จ"); } finally { setBusy(false); }
  };

  const toggleActive = async (row: AutomationScheduleListItem) => {
    if (busy) return;
    if (!await confirmDialog(`${row.isActive ? "ปิด" : "เปิด"}ใช้งาน Schedule "${row.name}"?`)) return;
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/schedules/${row.automationScheduleId}/${row.isActive ? "deactivate" : "activate"}?projectId=${projectId}`, { method: "POST", headers });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "เปลี่ยนสถานะ Schedule ไม่สำเร็จ"); }
      setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "เปลี่ยนสถานะ Schedule ไม่สำเร็จ"); }
  };

  const openRunHistory = async (row: AutomationScheduleListItem) => {
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/schedules/${row.automationScheduleId}/runs?projectId=${projectId}`, { headers });
      if (!r.ok) throw new Error("โหลดประวัติการรันไม่สำเร็จ");
      setRunHistory({ name: row.name, runs: await r.json() });
    } catch (e) { setError(e instanceof Error ? e.message : "โหลดประวัติการรันไม่สำเร็จ"); }
  };

  return <section className="automation-cases" aria-label="Automation Schedule">
    <header className="automation-section-head"><div><h2>Automation Schedule (AUT-P1-005)</h2><p>ตั้งเวลารัน Automation Suite ซ้ำอัตโนมัติ — Once/Daily/Weekly พร้อม timezone และคำนวณรอบถัดไป</p></div><div className="automation-section-head-actions"><button className="btn automation-notif-bell" type="button" onClick={openNotifications} aria-label="การแจ้งเตือน Schedule">🔔 การแจ้งเตือน{unreadCount > 0 && <Badge tone="red">{unreadCount}</Badge>}</button>{canEdit && <button className="btn primary" type="button" onClick={() => setCreateModal(true)}><span className="material-symbols-outlined" aria-hidden="true">add</span> สร้าง Schedule</button>}</div></header>
    {error && <div className="inline-alert error"><span>{error}</span></div>}
    <div className="filter-toolbar">
      <div className="filter-toolbar-top"><div className="result-count"><strong>{schedules.length.toLocaleString()}</strong><span>Schedule</span></div></div>
    <div className="filter-toolbar-row automation-case-toolbar">
      <select aria-label="กรองสถานะ Schedule" value={activeFilter} onChange={(e) => setActiveFilter(e.target.value as "all" | "active" | "inactive")}>
        <option value="active">เปิดใช้งาน</option>
        <option value="inactive">ปิดแล้ว</option>
        <option value="all">ทั้งหมด</option>
      </select>
    </div>
    </div>
    {schedules.length ? <div className="table-wrap"><table className="table-cards"><thead><tr><th>ชื่อ</th><th>Suite</th><th>ตารางเวลา</th><th>Timezone</th><th>รันครั้งถัดไป</th><th>รันล่าสุด</th><th>สถานะ</th><th></th></tr></thead><tbody>{schedules.map((s) => <tr key={s.automationScheduleId}>
      <td><b>{s.name}</b>{s.description && <small>{s.description}</small>}</td>
      <td>{s.suiteCode}</td>
      <td>{describeSchedule(s)}</td>
      <td>{s.timeZoneId}</td>
      <td>{s.isActive ? formatThaiDateTime(s.nextRunAtUtc) : "-"}</td>
      <td>{s.lastRunAtUtc ? formatThaiDateTime(s.lastRunAtUtc) : "ยังไม่เคยรัน"}</td>
      <td><Badge tone={s.isActive ? "green" : "gray"}>{s.isActive ? "เปิดใช้งาน" : "ปิดแล้ว"}</Badge></td>
      <td>{canEdit && <button type="button" className="table-action" onClick={() => openEdit(s.automationScheduleId)}><span className="material-symbols-outlined" aria-hidden="true">edit</span> แก้ไข</button>}<button type="button" className="table-action" onClick={() => openRunHistory(s)}><span aria-hidden="true">↺</span> ประวัติการรัน</button>{canEdit && <button type="button" className={`table-action${s.isActive ? " danger" : ""}`} onClick={() => toggleActive(s)}><span className="material-symbols-outlined" aria-hidden="true">power_settings_new</span> {s.isActive ? "ปิด" : "เปิด"}</button>}</td>
    </tr>)}</tbody></table></div> : <div className="empty"><p>ยังไม่มี Automation Schedule</p><small>ตั้งเวลารัน Automation Suite ที่มีอยู่แล้วให้ทำงานซ้ำอัตโนมัติตามรอบที่กำหนด</small></div>}

    {createModal && <ScheduleFormModal projectId={projectId} releaseId={releaseId} headers={headers} agents={agents} busy={busy} onClose={() => setCreateModal(false)} onSave={createSchedule} />}
    {editSchedule && <ScheduleFormModal projectId={projectId} releaseId={releaseId} headers={headers} agents={agents} busy={busy} schedule={editSchedule} onClose={() => setEditSchedule(null)} onSave={(body) => updateSchedule(editSchedule.automationScheduleId, body)} />}

    {runHistory && <ModalShell labelledBy="automation-schedule-run-history-title" onDismiss={() => setRunHistory(null)}>
      <div className="modal-head"><div><h2 id="automation-schedule-run-history-title">ประวัติการรัน — {runHistory.name}</h2><small>{runHistory.runs.length} รายการ — ล่าสุดก่อน (AUT-P1-006)</small></div><button aria-label="ปิด" onClick={() => setRunHistory(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
      {runHistory.runs.length ? <div className="automation-result-list">{runHistory.runs.map((r) => <div key={r.automationScheduleRunId} className="automation-failure-row">
        <b><Badge tone={r.status === "Succeeded" ? "green" : r.status === "NoReadyCases" ? "yellow" : "red"}>{r.status}</Badge> {formatThaiDateTime(r.firedAtUtc)}</b>
        <span>สร้าง Execution {r.executionsCreated} รายการ{r.skippedCount > 0 && ` · ข้าม ${r.skippedCount} รายการ`}</span>
        {r.errorMessage && <span>{r.errorMessage}</span>}
      </div>)}</div> : <div className="empty"><p>ยังไม่เคยถูกรันจาก Schedule นี้</p></div>}
      <div className="modal-actions"><button className="btn" onClick={() => setRunHistory(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ปิดหน้าต่าง</button></div>
    </ModalShell>}

    {notifications && <ModalShell labelledBy="automation-schedule-notif-title" onDismiss={() => setNotifications(null)}>
      <div className="modal-head"><div><h2 id="automation-schedule-notif-title">การแจ้งเตือน Schedule (AUT-P1-009)</h2><small>{notifications.length} รายการ — Started/Completed/Failed/No Agent ล่าสุดก่อน</small></div><button aria-label="ปิด" onClick={() => setNotifications(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
      {notifications.length > 0 && <div className="modal-actions" style={{ justifyContent: "flex-start" }}><button className="btn" type="button" onClick={markAllNotificationsRead}><span className="material-symbols-outlined" aria-hidden="true">check</span> ทำเครื่องหมายว่าอ่านแล้วทั้งหมด</button></div>}
      {notifications.length ? <div className="automation-result-list">{notifications.map((n) => <div key={n.automationScheduleNotificationId} className={`automation-failure-row automation-notif-item${n.isRead ? "" : " is-unread"}`}>
        <b><Badge tone={n.eventType === "Completed" ? "green" : n.eventType === "Failed" ? "red" : n.eventType === "NoAgent" ? "orange" : "blue"}>{n.eventType}</Badge> {n.scheduleName} · {n.automationCode}{!n.isRead && <Badge tone="gray">ใหม่</Badge>}</b>
        <span>{n.message}</span>
        <span className="muted-text">{formatThaiDateTime(n.createdAtUtc)}</span>
        <div className="modal-actions" style={{ justifyContent: "flex-start", padding: 0 }}>
          <button className="table-action" type="button" onClick={() => openNotificationExecution(n)}><span className="material-symbols-outlined" aria-hidden="true">info</span> ดู Execution</button>
          {!n.isRead && <button className="table-action" type="button" onClick={() => markNotificationRead(n)}><span className="material-symbols-outlined" aria-hidden="true">check</span> ทำเครื่องหมายว่าอ่านแล้ว</button>}
        </div>
      </div>)}</div> : <div className="empty"><p>ยังไม่มีการแจ้งเตือน</p><small>จะมีเมื่อ Schedule เริ่มรัน/รันเสร็จ/ล้มเหลว หรือรันแล้วไม่มี Agent ว่าง</small></div>}
      <div className="modal-actions"><button className="btn" onClick={() => setNotifications(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ปิดหน้าต่าง</button></div>
    </ModalShell>}
  </section>;
}

function ScheduleFormModal({ projectId, releaseId, headers, agents, schedule, busy, onClose, onSave }: {
  projectId: string; releaseId?: string; headers: Record<string, string>; agents: AutomationAgentItem[]; schedule?: AutomationScheduleDetailItem; busy: boolean; onClose: () => void; onSave: (body: Record<string, unknown>) => void;
}) {
  const isEdit = !!schedule;
  const [suites, setSuites] = useState<{ automationSuiteId: string; suiteCode: string; suiteName: string }[]>([]);
  const [suitesError, setSuitesError] = useState("");
  const { builds, environments, loadError } = useBuildsAndEnvironments(releaseId);
  const [automationSuiteId, setAutomationSuiteId] = useState(schedule?.automationSuiteId ?? "");
  const [name, setName] = useState(schedule?.name ?? "");
  const [description, setDescription] = useState(schedule?.description ?? "");
  const [frequency, setFrequency] = useState(schedule?.frequency ?? "Daily");
  const [daysOfWeekMask, setDaysOfWeekMask] = useState(schedule?.daysOfWeekMask ?? 0);
  const [runAtTime, setRunAtTime] = useState((schedule?.runAtTime ?? "09:00").slice(0, 5));
  const [onceOnDate, setOnceOnDate] = useState(schedule?.onceOnDate ?? "");
  const [timeZoneId, setTimeZoneId] = useState(schedule?.timeZoneId ?? "Asia/Bangkok");
  const [buildId, setBuildId] = useState(schedule?.buildId ?? "");
  const [environmentId, setEnvironmentId] = useState(schedule?.environmentId ?? "");
  const [agentId, setAgentId] = useState(schedule?.agentId ?? "");
  const [priority, setPriority] = useState(schedule?.priority ?? 5);

  useEffect(() => {
    let mounted = true;
    fetchJson(`${apiUrl}/automation/suites?projectId=${projectId}&isActive=true`, headers)
      .then((su) => { if (mounted) setSuites(Array.isArray(su) ? su : []); })
      .catch(() => { if (mounted) setSuitesError("โหลดรายการ Suite ไม่สำเร็จ — ปิดหน้าต่างแล้วลองใหม่"); });
    return () => { mounted = false; };
  }, [projectId, headers]);

  const toggleDay = (value: number) => setDaysOfWeekMask((prev) => (prev & (1 << value)) !== 0 ? prev & ~(1 << value) : prev | (1 << value));

  const canSave = name.trim() && automationSuiteId && buildId && environmentId
    && (frequency !== "Weekly" || daysOfWeekMask > 0)
    && (frequency !== "Once" || onceOnDate);

  const save = () => onSave({
    automationSuiteId, name: name.trim(), description: description.trim() || null, frequency,
    daysOfWeekMask: frequency === "Weekly" ? daysOfWeekMask : 0,
    runAtTime: runAtTime.length === 5 ? `${runAtTime}:00` : runAtTime,
    onceOnDate: frequency === "Once" ? onceOnDate : null,
    timeZoneId, buildId, environmentId, agentId: agentId || null, priority,
  });

  return <ModalShell labelledBy="automation-schedule-form-title" onDismiss={() => { if (!busy) onClose(); }} form>
    <div className="modal-head"><div><h2 id="automation-schedule-form-title">{isEdit ? `แก้ไข ${schedule!.name}` : "สร้าง Automation Schedule"}</h2></div><button aria-label="ปิด" disabled={busy} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
    {(suitesError || loadError) && <div className="inline-alert error" role="alert"><span>{(suitesError || loadError)}</span></div>}
    <div className="form-grid">
      <label className="full">Automation Suite{isEdit ? <input type="text" value={`${schedule!.suiteCode} · ${schedule!.suiteName}`} disabled /> : <select value={automationSuiteId} onChange={(e) => setAutomationSuiteId(e.target.value)}><option value="">เลือก Suite</option>{suites.map((s) => <option key={s.automationSuiteId} value={s.automationSuiteId}>{s.suiteCode} · {s.suiteName}</option>)}</select>}</label>
      <label className="full">ชื่อ Schedule<input type="text" value={name} onChange={(e) => setName(e.target.value)} placeholder="เช่น Nightly Smoke" /></label>
      <label className="full">คำอธิบาย (ไม่บังคับ)<textarea rows={2} value={description} onChange={(e) => setDescription(e.target.value)} /></label>
      <label>ความถี่<select value={frequency} onChange={(e) => setFrequency(e.target.value)}><option value="Daily">ทุกวัน</option><option value="Weekly">ทุกสัปดาห์</option><option value="Once">ครั้งเดียว</option></select></label>
      <label>เวลา (ตาม Timezone ที่เลือก)<input type="time" value={runAtTime} onChange={(e) => setRunAtTime(e.target.value)} /></label>
      {frequency === "Weekly" && <div className="full form-grid-label-like"><span>วันในสัปดาห์</span><div className="automation-days-row">{DAY_LABELS.map((d) => <label key={d.value}><input type="checkbox" checked={(daysOfWeekMask & (1 << d.value)) !== 0} onChange={() => toggleDay(d.value)} />{d.label}</label>)}</div></div>}
      {frequency === "Once" && <label>วันที่<input type="date" value={onceOnDate} onChange={(e) => setOnceOnDate(e.target.value)} /></label>}
      <label>Timezone<select value={timeZoneId} onChange={(e) => setTimeZoneId(e.target.value)}>{timezoneOptions().map((tz) => <option key={tz} value={tz}>{tz}</option>)}</select></label>
      <label>Build<select value={buildId} onChange={(e) => setBuildId(e.target.value)}><option value="">เลือก Build</option>{builds.map((b) => <option key={b.buildId} value={b.buildId}>{b.buildNumber}{b.applicationVersion ? ` · App ${b.applicationVersion}` : ""}</option>)}</select></label>
      <label>Environment<select value={environmentId} onChange={(e) => setEnvironmentId(e.target.value)}><option value="">เลือก Env</option>{environments.map((e) => <option key={e.testEnvironmentId} value={e.testEnvironmentId}>{e.environmentName}</option>)}</select></label>
      <label>Agent (ไม่บังคับ)<select value={agentId} onChange={(e) => setAgentId(e.target.value)}><option value="">Agent ใดก็ได้</option>{agents.map((a) => <option key={a.agentId} value={a.agentId}>{a.agentCode}</option>)}</select></label>
      <label>Priority<select value={priority} onChange={(e) => setPriority(Number(e.target.value))}>{[1, 2, 3, 4, 5, 6, 7, 8, 9, 10].map((p) => <option key={p} value={p}>{p}</option>)}</select></label>
    </div>
    <div className="modal-actions"><button className="btn" disabled={busy} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิก</button><button className="btn primary" disabled={busy || !canSave} onClick={save}>{busy ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">check</span> บันทึก</>}</button></div>
  </ModalShell>;
}

const packTone = (pack: string) => (pack === "Smoke" ? "blue" : "purple");

export function AutomationBuildTriggerTab({ projectId, headers, canEdit, agents }: {
  projectId: string; headers: Record<string, string>; canEdit: boolean; agents: AutomationAgentItem[];
}) {
  const [policies, setPolicies] = useState<AutomationBuildTriggerPolicyItem[]>([]);
  const [reload, setReload] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [createModal, setCreateModal] = useState(false);
  const [editPolicy, setEditPolicy] = useState<AutomationBuildTriggerPolicyItem | null>(null);
  const [runHistory, setRunHistory] = useState<{ label: string; runs: AutomationBuildTriggerRunItem[] } | null>(null);

  useEffect(() => {
    if (!projectId) return;
    const ctrl = new AbortController();
    fetchJson(`${apiUrl}/automation/build-triggers?projectId=${projectId}`, headers, ctrl.signal).then((p) => setPolicies(Array.isArray(p) ? p : [])).catch((e) => !isAbort(e) && setError("โหลด Build Trigger ไม่สำเร็จ"));
    return () => ctrl.abort();
  }, [projectId, headers, reload]);

  const createPolicy = async (body: Record<string, unknown>) => {
    setBusy(true); setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/build-triggers?projectId=${projectId}`, { method: "POST", headers, body: JSON.stringify(body) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "สร้าง Build Trigger ไม่สำเร็จ"); }
      setCreateModal(false); setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "สร้าง Build Trigger ไม่สำเร็จ"); } finally { setBusy(false); }
  };

  const updatePolicy = async (id: string, body: Record<string, unknown>) => {
    setBusy(true); setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/build-triggers/${id}?projectId=${projectId}`, { method: "PUT", headers, body: JSON.stringify(body) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "แก้ไข Build Trigger ไม่สำเร็จ"); }
      setEditPolicy(null); setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "แก้ไข Build Trigger ไม่สำเร็จ"); } finally { setBusy(false); }
  };

  const toggleActive = async (row: AutomationBuildTriggerPolicyItem) => {
    if (!await confirmDialog(`${row.isActive ? "ปิด" : "เปิด"}ใช้งาน Build Trigger "${row.pack} · ${row.suiteCode}"?`)) return;
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/build-triggers/${row.automationBuildTriggerPolicyId}/${row.isActive ? "deactivate" : "activate"}?projectId=${projectId}`, { method: "POST", headers });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "เปลี่ยนสถานะไม่สำเร็จ"); }
      setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "เปลี่ยนสถานะไม่สำเร็จ"); }
  };

  const openRunHistory = async (row: AutomationBuildTriggerPolicyItem) => {
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/build-triggers/${row.automationBuildTriggerPolicyId}/runs?projectId=${projectId}`, { headers });
      if (!r.ok) throw new Error("โหลดประวัติการรันไม่สำเร็จ");
      setRunHistory({ label: `${row.pack} · ${row.suiteCode}`, runs: await r.json() });
    } catch (e) { setError(e instanceof Error ? e.message : "โหลดประวัติการรันไม่สำเร็จ"); }
  };

  return <section className="automation-cases" aria-label="Automation Build Trigger">
    <header className="automation-section-head"><div><h2>Build Trigger (AUT-P1-007)</h2><p>Build ใหม่รัน Suite อัตโนมัติตาม policy — Smoke รันทุก Build ใหม่, Regression รันเมื่อ Build ถูกตั้งเป็น Release Candidate</p></div>{canEdit && <button className="btn primary" type="button" onClick={() => setCreateModal(true)}><span className="material-symbols-outlined" aria-hidden="true">add</span> สร้าง Policy</button>}</header>
    {error && <div className="inline-alert error"><span>{error}</span></div>}
    {policies.length ? <div className="table-wrap"><table className="table-cards"><thead><tr><th>Pack</th><th>Suite</th><th>Environment</th><th>Agent</th><th>Priority</th><th>สถานะ</th><th></th></tr></thead><tbody>{policies.map((p) => <tr key={p.automationBuildTriggerPolicyId}>
      <td><Badge tone={packTone(p.pack)}>{p.pack}</Badge></td>
      <td>{p.suiteCode}</td>
      <td>{p.environmentName}</td>
      <td>{p.agentCode ?? "Agent ใดก็ได้"}</td>
      <td>{p.priority}</td>
      <td><Badge tone={p.isActive ? "green" : "gray"}>{p.isActive ? "เปิดใช้งาน" : "ปิดแล้ว"}</Badge></td>
      <td>{canEdit && <button type="button" className="table-action" onClick={() => setEditPolicy(p)}><span className="material-symbols-outlined" aria-hidden="true">edit</span> แก้ไข</button>}<button type="button" className="table-action" onClick={() => openRunHistory(p)}><span aria-hidden="true">↺</span> ประวัติการรัน</button>{canEdit && <button type="button" className={`table-action${p.isActive ? " danger" : ""}`} onClick={() => toggleActive(p)}><span className="material-symbols-outlined" aria-hidden="true">power_settings_new</span> {p.isActive ? "ปิด" : "เปิด"}</button>}</td>
    </tr>)}</tbody></table></div> : <div className="empty"><p>ยังไม่มี Build Trigger Policy</p><small>ตั้ง policy ให้ Build ใหม่รัน Smoke/Regression Suite อัตโนมัติโดยไม่ต้องสั่งรันเอง</small></div>}

    {createModal && <BuildTriggerFormModal projectId={projectId} headers={headers} agents={agents} busy={busy} onClose={() => setCreateModal(false)} onSave={createPolicy} />}
    {editPolicy && <BuildTriggerFormModal projectId={projectId} headers={headers} agents={agents} busy={busy} policy={editPolicy} onClose={() => setEditPolicy(null)} onSave={(body) => updatePolicy(editPolicy.automationBuildTriggerPolicyId, body)} />}

    {runHistory && <ModalShell labelledBy="automation-build-trigger-run-history-title" onDismiss={() => setRunHistory(null)}>
      <div className="modal-head"><div><h2 id="automation-build-trigger-run-history-title">ประวัติการรัน — {runHistory.label}</h2><small>{runHistory.runs.length} รายการ — ล่าสุดก่อน</small></div><button aria-label="ปิด" onClick={() => setRunHistory(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
      {runHistory.runs.length ? <div className="automation-result-list">{runHistory.runs.map((r) => <div key={r.automationBuildTriggerRunId} className="automation-failure-row">
        <b><Badge tone={r.status === "Succeeded" ? "green" : r.status === "NoReadyCases" ? "yellow" : "red"}>{r.status}</Badge> Build {r.buildNumber} · {formatThaiDateTime(r.firedAtUtc)}</b>
        <span>สร้าง Execution {r.executionsCreated} รายการ{r.skippedCount > 0 && ` · ข้าม ${r.skippedCount} รายการ`}</span>
        {r.errorMessage && <span>{r.errorMessage}</span>}
      </div>)}</div> : <div className="empty"><p>ยังไม่เคยถูกรันจาก Policy นี้</p></div>}
      <div className="modal-actions"><button className="btn" onClick={() => setRunHistory(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ปิดหน้าต่าง</button></div>
    </ModalShell>}
  </section>;
}

function BuildTriggerFormModal({ projectId, headers, agents, policy, busy, onClose, onSave }: {
  projectId: string; headers: Record<string, string>; agents: AutomationAgentItem[]; policy?: AutomationBuildTriggerPolicyItem; busy: boolean; onClose: () => void; onSave: (body: Record<string, unknown>) => void;
}) {
  const isEdit = !!policy;
  const [suites, setSuites] = useState<{ automationSuiteId: string; suiteCode: string; suiteName: string }[]>([]);
  const [suitesError, setSuitesError] = useState("");
  const { environments, loadError } = useBuildsAndEnvironments(undefined, { builds: false });
  const [automationSuiteId, setAutomationSuiteId] = useState(policy?.automationSuiteId ?? "");
  const [pack, setPack] = useState(policy?.pack ?? "Smoke");
  const [environmentId, setEnvironmentId] = useState(policy?.environmentId ?? "");
  const [agentId, setAgentId] = useState(policy?.agentId ?? "");
  const [priority, setPriority] = useState(policy?.priority ?? 5);

  useEffect(() => {
    let mounted = true;
    fetchJson(`${apiUrl}/automation/suites?projectId=${projectId}&isActive=true`, headers)
      .then((su) => { if (mounted) setSuites(Array.isArray(su) ? su : []); })
      .catch(() => { if (mounted) setSuitesError("โหลดรายการ Suite ไม่สำเร็จ — ปิดหน้าต่างแล้วลองใหม่"); });
    return () => { mounted = false; };
  }, [projectId, headers]);

  const canSave = automationSuiteId && environmentId;
  const save = () => onSave({ automationSuiteId, pack, environmentId, agentId: agentId || null, priority });

  return <ModalShell labelledBy="automation-build-trigger-form-title" onDismiss={() => { if (!busy) onClose(); }} form>
    <div className="modal-head"><div><h2 id="automation-build-trigger-form-title">{isEdit ? "แก้ไข Build Trigger Policy" : "สร้าง Build Trigger Policy"}</h2></div><button aria-label="ปิด" disabled={busy} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
    {(suitesError || loadError) && <div className="inline-alert error" role="alert"><span>{(suitesError || loadError)}</span></div>}
    <div className="form-grid">
      <label className="full">Automation Suite<select value={automationSuiteId} onChange={(e) => setAutomationSuiteId(e.target.value)}><option value="">เลือก Suite</option>{suites.map((s) => <option key={s.automationSuiteId} value={s.automationSuiteId}>{s.suiteCode} · {s.suiteName}</option>)}</select></label>
      <label>Pack<select value={pack} onChange={(e) => setPack(e.target.value)}><option value="Smoke">Smoke (รันทุก Build ใหม่)</option><option value="Regression">Regression (รันเมื่อตั้งเป็น Release Candidate)</option></select></label>
      <label>Environment<select value={environmentId} onChange={(e) => setEnvironmentId(e.target.value)}><option value="">เลือก Env</option>{environments.map((e) => <option key={e.testEnvironmentId} value={e.testEnvironmentId}>{e.environmentName}</option>)}</select></label>
      <label>Agent (ไม่บังคับ)<select value={agentId} onChange={(e) => setAgentId(e.target.value)}><option value="">Agent ใดก็ได้</option>{agents.map((a) => <option key={a.agentId} value={a.agentId}>{a.agentCode}</option>)}</select></label>
      <label>Priority<select value={priority} onChange={(e) => setPriority(Number(e.target.value))}>{[1, 2, 3, 4, 5, 6, 7, 8, 9, 10].map((p) => <option key={p} value={p}>{p}</option>)}</select></label>
    </div>
    <div className="modal-actions"><button className="btn" disabled={busy} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิก</button><button className="btn primary" disabled={busy || !canSave} onClick={save}>{busy ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">check</span> บันทึก</>}</button></div>
  </ModalShell>;
}

export function AutomationWebhookTab({ projectId, headers, canEdit }: { projectId: string; headers: Record<string, string>; canEdit: boolean }) {
  const [tokens, setTokens] = useState<AutomationWebhookTokenItem[]>([]);
  const [deliveries, setDeliveries] = useState<AutomationWebhookDeliveryItem[]>([]);
  const [reload, setReload] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [createModal, setCreateModal] = useState(false);
  const [newToken, setNewToken] = useState<{ name: string; plainTextToken: string } | null>(null);

  useEffect(() => {
    if (!projectId) return;
    const ctrl = new AbortController();
    setError("");
    fetchJson(`${apiUrl}/automation/webhook-tokens?projectId=${projectId}`, headers, ctrl.signal).then((t) => setTokens(Array.isArray(t) ? t : [])).catch((e) => !isAbort(e) && setError("โหลด Webhook Token ไม่สำเร็จ"));
    fetchJson(`${apiUrl}/automation/webhook-tokens/deliveries?projectId=${projectId}`, headers, ctrl.signal).then((d) => setDeliveries(Array.isArray(d) ? d : [])).catch((e) => !isAbort(e) && setError("โหลดประวัติ Webhook ไม่สำเร็จ"));
    return () => ctrl.abort();
  }, [projectId, headers, reload]);

  const createToken = async (name: string) => {
    setBusy(true); setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/webhook-tokens?projectId=${projectId}`, { method: "POST", headers, body: JSON.stringify({ name }) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "สร้าง Token ไม่สำเร็จ"); }
      const result: { token: AutomationWebhookTokenItem; plainTextToken: string } = await r.json();
      setCreateModal(false); setReload((v) => v + 1);
      setNewToken({ name: result.token.name, plainTextToken: result.plainTextToken });
    } catch (e) { setError(e instanceof Error ? e.message : "สร้าง Token ไม่สำเร็จ"); } finally { setBusy(false); }
  };

  const revokeToken = async (row: AutomationWebhookTokenItem) => {
    if (!await confirmDialog(`เพิกถอน Token "${row.name}"? ระบบ CI/CD ที่ใช้ Token นี้จะเรียก webhook ไม่ได้อีก`)) return;
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/webhook-tokens/${row.automationWebhookTokenId}/revoke?projectId=${projectId}`, { method: "POST", headers });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "เพิกถอน Token ไม่สำเร็จ"); }
      setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "เพิกถอน Token ไม่สำเร็จ"); }
  };

  return <section className="automation-cases" aria-label="Automation Webhook">
    <header className="automation-section-head"><div><h2>CI/CD Webhook (AUT-P1-008)</h2><p>ให้ CI/CD ยิง Build เข้ามาสร้างอัตโนมัติผ่าน webhook ที่ authenticate ด้วย Token — trigger Smoke/Regression ต่อเนื่องจาก Build Trigger ได้ทันที</p></div>{canEdit && <button className="btn primary" type="button" onClick={() => setCreateModal(true)}><span className="material-symbols-outlined" aria-hidden="true">add</span> สร้าง Token</button>}</header>
    {error && <div className="inline-alert error"><span>{error}</span></div>}
    <h3>Webhook Token</h3>
    {tokens.length ? <div className="table-wrap"><table className="table-cards"><thead><tr><th>ชื่อ</th><th>Token</th><th>สร้างเมื่อ</th><th>ใช้ล่าสุด</th><th>สถานะ</th><th></th></tr></thead><tbody>{tokens.map((t) => <tr key={t.automationWebhookTokenId}>
      <td><b>{t.name}</b></td>
      <td><code>{t.tokenPrefix}…</code></td>
      <td>{formatThaiDateTime(t.createdAt)}</td>
      <td>{t.lastUsedAtUtc ? formatThaiDateTime(t.lastUsedAtUtc) : "ยังไม่เคยใช้"}</td>
      <td><Badge tone={t.isActive ? "green" : "gray"}>{t.isActive ? "ใช้งานได้" : "เพิกถอนแล้ว"}</Badge></td>
      <td>{canEdit && t.isActive && <button type="button" className="table-action danger" onClick={() => revokeToken(t)}><span className="material-symbols-outlined" aria-hidden="true">close</span> เพิกถอน</button>}</td>
    </tr>)}</tbody></table></div> : <div className="empty"><p>ยังไม่มี Webhook Token</p><small>สร้าง Token ให้ระบบ CI/CD ใช้ authenticate ตอนยิง webhook เข้ามาสร้าง Build</small></div>}

    <h3>ประวัติการเรียก Webhook</h3>
    {deliveries.length ? <div className="table-wrap"><table className="table-cards"><thead><tr><th>Token</th><th>Request ID</th><th>Build</th><th>สถานะ</th><th>เวลา</th></tr></thead><tbody>{deliveries.map((d) => <tr key={d.automationWebhookDeliveryId}>
      <td>{d.tokenName}</td>
      <td><code>{d.requestId}</code></td>
      <td>{d.buildNumber ?? "-"}</td>
      <td><Badge tone={d.status === "Created" ? "green" : d.status === "Duplicate" ? "yellow" : "red"}>{d.status}</Badge>{d.errorMessage && <small>{d.errorMessage}</small>}</td>
      <td>{formatThaiDateTime(d.receivedAtUtc)}</td>
    </tr>)}</tbody></table></div> : <div className="empty"><p>ยังไม่เคยมี webhook เรียกเข้ามา</p></div>}

    {createModal && <WebhookTokenFormModal busy={busy} onClose={() => setCreateModal(false)} onSave={createToken} />}
    {newToken && <WebhookNewTokenModal name={newToken.name} plainTextToken={newToken.plainTextToken} onClose={() => setNewToken(null)} />}
  </section>;
}

/** AUT-UI-001: Token แสดงครั้งเดียว — ห้ามปิดด้วยคลิกพื้นหลัง, มีปุ่มคัดลอก และยืนยันก่อนปิดถ้ายังไม่ได้คัดลอก */
function WebhookNewTokenModal({ name, plainTextToken, onClose }: { name: string; plainTextToken: string; onClose: () => void }) {
  const [copied, setCopied] = useState(false);
  const [copyError, setCopyError] = useState("");
  const tokenRef = useRef<HTMLInputElement>(null);
  const close = async () => { if (copied || await confirmDialog("ยังไม่ได้คัดลอก Token — ปิดแล้วจะดู Token เต็มไม่ได้อีก ต้องการปิดหรือไม่?")) onClose(); };
  const copy = async () => {
    setCopyError("");
    try {
      await navigator.clipboard.writeText(plainTextToken);
      setCopied(true);
    } catch {
      // clipboard API ใช้ไม่ได้ (เช่น http ที่ไม่ใช่ localhost) — เลือกข้อความให้ผู้ใช้กด Ctrl+C เอง
      tokenRef.current?.select();
      setCopyError("คัดลอกอัตโนมัติไม่ได้ — ข้อความถูกเลือกไว้แล้ว กด Ctrl+C เพื่อคัดลอก");
    }
  };
  return <ModalShell labelledBy="automation-webhook-new-token-title" onDismiss={close} backdropDismiss={false}>
    <div className="modal-head"><div><h2 id="automation-webhook-new-token-title">สร้าง Token "{name}" สำเร็จ</h2></div><button aria-label="ปิด" onClick={close}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
    <div className="inline-alert" role="note">⚠ คัดลอก Token นี้เก็บไว้ตอนนี้ — ระบบจะไม่แสดง Token เต็มให้ดูอีกครั้ง</div>
    <div className="automation-token-copy">
      <input ref={tokenRef} readOnly value={plainTextToken} aria-label="Webhook Token" onFocus={(e) => e.currentTarget.select()} />
      <button type="button" className="btn" onClick={copy}><span className="material-symbols-outlined" aria-hidden="true">{copied ? "check" : "content_copy"}</span> {copied ? "คัดลอกแล้ว" : "คัดลอก"}</button>
    </div>
    {copyError && <p className="field-error" role="alert">{copyError}</p>}
    <p>ใส่ header <code>X-Webhook-Token</code> เวลายิงมาที่ <code>POST /api/v1/webhooks/automation/builds</code> พร้อม <code>releaseId</code>/<code>buildNumber</code>/<code>requestId</code> (idempotency key ป้องกัน trigger ซ้ำ)</p>
    <div className="modal-actions"><button className="btn primary" onClick={close}><span className="material-symbols-outlined" aria-hidden="true">check</span> ปิดหน้าต่าง</button></div>
  </ModalShell>;
}

function WebhookTokenFormModal({ busy, onClose, onSave }: { busy: boolean; onClose: () => void; onSave: (name: string) => void }) {
  const [name, setName] = useState("");
  return <ModalShell labelledBy="automation-webhook-token-form-title" onDismiss={() => { if (!busy) onClose(); }} form>
    <div className="modal-head"><div><h2 id="automation-webhook-token-form-title">สร้าง Webhook Token</h2></div><button aria-label="ปิด" disabled={busy} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
    <div className="form-grid">
      <label className="full">ชื่อ (สำหรับระบุ เช่นชื่อระบบ CI/CD)<input type="text" value={name} onChange={(e) => setName(e.target.value)} placeholder="เช่น Jenkins Nightly" /></label>
    </div>
    <div className="modal-actions"><button className="btn" disabled={busy} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิก</button><button className="btn primary" disabled={busy || !name.trim()} onClick={() => onSave(name.trim())}>{busy ? "กำลังสร้าง..." : "สร้าง"}</button></div>
  </ModalShell>;
}
