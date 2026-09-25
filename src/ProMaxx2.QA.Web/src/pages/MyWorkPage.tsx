import { useState, useEffect, useMemo } from "react";
import { apiUrl } from "../api";
import { Badge } from "../components/Badge";
import { formatThaiDateTime } from "../dateTime";
import { type Page, type SessionUser } from "../shared/appShared";

type MyWorkItem = {
  testCycleCaseId: string; testCaseId: string; testCaseCode: string; title: string;
  moduleId: string; moduleName?: string; priority?: string; testType?: string;
  testCycleId: string; cycleCode: string; buildId: string; buildNumber?: string;
  currentStatus: string; assignmentStatus: string; dueDate?: string;
  estimatedMinutesSnapshot: number; assignedAt: string; isCritical: boolean; reviewerRequired: boolean;
};

export function MyWorkPage({ user, onOpenExecution }: { user: SessionUser | null; onOpenExecution: (cycleId: string) => void; onNavigate: (page: Page) => void }) {
  const initials = (user?.displayName ?? user?.username ?? "?").trim().split(/\s+/).map((part) => part[0]).slice(0, 2).join("").toUpperCase();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [reload, setReload] = useState(0);
  const [items, setItems] = useState<MyWorkItem[]>([]);
  const [statusFilter, setStatusFilter] = useState("All");
  useEffect(() => {
    if (!user?.userId) { setItems([]); setLoading(false); return; }
    setLoading(true); setError("");
    fetch(`${apiUrl}/my-work`, { headers: { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` } })
      .then(async (response) => {
        if (!response.ok) throw new Error(`โหลดข้อมูลไม่สำเร็จ (${response.status})`);
        const data = await response.json();
        setItems(Array.isArray(data?.rows) ? data.rows : []);
      })
      .catch((reason) => setError(reason instanceof Error ? reason.message : "โหลดข้อมูล My Work ไม่สำเร็จ"))
      .finally(() => setLoading(false));
  }, [user?.userId, reload]);
  const normalizedStatus = (item: MyWorkItem) => item.currentStatus.replaceAll(" ", "").toLowerCase();
  const statusCounts = useMemo(() => items.reduce<Record<string, number>>((result, item) => {
    const key = normalizedStatus(item); result[key] = (result[key] ?? 0) + 1; return result;
  }, {}), [items]);
  const filteredItems = useMemo(() => statusFilter === "All" ? items : items.filter((item) => normalizedStatus(item) === statusFilter.toLowerCase()), [items, statusFilter]);
  const isFinished = (item: MyWorkItem) => ["pass", "completed", "skipped"].includes(normalizedStatus(item));
  const overdueCount = items.filter((item) => item.dueDate && new Date(item.dueDate).getTime() < Date.now() && !isFinished(item)).length;
  const filters = [["All", "ทั้งหมด"], ["notrun", "ยังไม่เริ่ม"], ["inprogress", "กำลังดำเนินการ"], ["pass", "ผ่าน"], ["fail", "ไม่ผ่าน"], ["blocked", "Blocked"]] as const;
  const statusTone = (status: string): "blue" | "green" | "red" | "yellow" => status === "Pass" ? "green" : status === "Fail" || status === "Blocked" ? "red" : status === "InProgress" ? "blue" : "yellow";
  if (loading) return <div className="my-work-page"><div className="card empty-state"><div className="spinner" />กำลังโหลด My Work...</div></div>;
  if (error) return <div className="my-work-page"><div className="card empty-state error-state"><p>ไม่สามารถโหลดข้อมูล My Work ได้</p><small>{error}</small><button className="btn primary" onClick={() => setReload((value) => value + 1)}><span className="material-symbols-outlined" aria-hidden="true">refresh</span> ลองใหม่</button></div></div>;
  return <div className="my-work-page">
    <div className="my-work-user-strip"><span className="my-work-user-avatar" aria-hidden="true">{initials}</span><div><small>งานของผู้ใช้งาน</small><b>{user?.displayName ?? "ผู้ใช้งาน"}</b><span>@{user?.username ?? "-"}{user?.roles?.length ? ` · ${user.roles.join(", ")}` : ""}</span></div></div>
    <div className="my-work-metrics">
      <div className="metric-suite"><span className="material-symbols-outlined" aria-hidden="true">assignment</span><div><b>{items.length}</b><small>งานทั้งหมด</small></div></div>
      <div className="metric-cycle"><span className="material-symbols-outlined" aria-hidden="true">pending_actions</span><div><b>{statusCounts.notrun ?? 0}</b><small>ยังไม่เริ่ม</small></div></div>
      <div className="metric-active"><span className="material-symbols-outlined" aria-hidden="true">play_circle</span><div><b>{statusCounts.inprogress ?? 0}</b><small>กำลังดำเนินการ</small></div></div>
      <div className="metric-done"><span className="material-symbols-outlined" aria-hidden="true">task_alt</span><div><b>{statusCounts.pass ?? 0}</b><small>ผ่าน</small></div></div>
      <div className="metric-progress"><span className="material-symbols-outlined" aria-hidden="true">event_busy</span><div><b>{overdueCount}</b><small>เลยกำหนด</small></div></div>
    </div>
    <section className="card my-work-assignment-card">
      <div className="my-work-assignment-head"><div className="my-work-assignment-title"><span className="material-symbols-outlined" aria-hidden="true">assignment_ind</span><div><h2>งานที่ได้รับมอบหมาย</h2><p>Test Case ที่ Assign ให้บัญชี {user?.username ?? "ปัจจุบัน"}</p></div></div><button className="btn" onClick={() => setReload((value) => value + 1)}><span className="material-symbols-outlined" aria-hidden="true">refresh</span> รีเฟรช</button></div>
      <div className="my-work-filters" role="group" aria-label="กรองสถานะงาน">{filters.map(([value, label]) => <button key={value} className={statusFilter === value ? "active" : ""} onClick={() => setStatusFilter(value)}>{label}<span>{value === "All" ? items.length : statusCounts[value] ?? 0}</span></button>)}</div>
      <div className="table-wrap my-work-table-wrap">
        <table className="my-work-table"><thead><tr><th>Test Case</th><th>Module</th><th>Priority / Type</th><th>Test Cycle / Build</th><th>Status</th><th>Due Date</th><th>เวลา</th><th aria-label="ดำเนินการ" /></tr></thead>
          <tbody>{filteredItems.map((item) => <tr key={item.testCycleCaseId}>
            <td data-label="Test Case"><b>{item.testCaseCode}</b><small>{item.title}</small></td>
            <td data-label="Module">{item.moduleName || "-"}</td>
            <td data-label="Priority / Type"><b>{item.priority || "-"}</b><small>{item.testType || "-"}</small></td>
            <td data-label="Test Cycle / Build"><b>{item.cycleCode}</b><small>Build {item.buildNumber || "-"}</small></td>
            <td data-label="Status"><Badge tone={statusTone(item.currentStatus)}>{item.currentStatus}</Badge><small>{item.assignmentStatus}</small></td>
            <td data-label="Due Date" className={item.dueDate && new Date(item.dueDate).getTime() < Date.now() && !isFinished(item) ? "is-overdue" : ""}>{item.dueDate ? formatThaiDateTime(item.dueDate, { day: "numeric", month: "short", year: "numeric" }) : "-"}</td>
            <td data-label="เวลา">{item.estimatedMinutesSnapshot ? `${item.estimatedMinutesSnapshot} นาที` : "-"}</td>
            <td className="actions-col"><button className="btn primary" onClick={() => onOpenExecution(item.testCycleId)}><span className="material-symbols-outlined" aria-hidden="true">play_arrow</span> Run</button></td>
          </tr>)}</tbody>
        </table>
        {!filteredItems.length && <div className="my-work-empty"><span className="material-symbols-outlined" aria-hidden="true">assignment_ind</span><p>{items.length ? "ไม่มีงานในสถานะที่เลือก" : "ยังไม่มี Test Case ที่ Assign ให้คุณ"}</p></div>}
      </div>
    </section>
  </div>;
}
