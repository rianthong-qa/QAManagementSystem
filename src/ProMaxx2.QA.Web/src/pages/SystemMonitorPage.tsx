import { useState, useCallback, useEffect } from "react";
import { apiUrl } from "../api";
import { confirmDialog, notify } from "../components/dialogStore";
import { Badge } from "../components/Badge";
import { formatThaiDateTime } from "../dateTime";

type SystemMonitorData = {
  checkedAt: string;
  machineName: string;
  environment: string;
  api: { status: string; processId: number; uptime: string; memoryBytes: number; processorCount: number };
  database: { status: string; responseMilliseconds: number; error?: string };
  services: { key: string; displayName: string; description?: string; status: string; isRunning: boolean; error?: string }[];
};
export function SystemMonitorPage() {
  const [data, setData] = useState<SystemMonitorData | null>(null),
    [error, setError] = useState(""),
    [loading, setLoading] = useState(true),
    [busy, setBusy] = useState("");
  const headers = { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` };
  const load = useCallback(async (silent = false) => {
    if (!silent) { setLoading(true); setError(""); }
    try {
      const response = await fetch(`${apiUrl}/system-monitor`, { headers: { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` } });
      if (!response.ok) throw new Error(response.status === 403 ? "หน้านี้สำหรับ System Admin เท่านั้น" : "โหลดสถานะระบบไม่สำเร็จ");
      setData(await response.json());
      if (silent) setError("");
    } catch (e) {
      // silent background poll failures keep the last-known-good data on screen instead of blanking the whole dashboard
      if (!silent) setError(e instanceof Error ? e.message : "โหลดสถานะระบบไม่สำเร็จ");
    }
    finally { if (!silent) setLoading(false); }
  }, []);
  useEffect(() => { load(); const timer = window.setInterval(() => load(true), 15000); return () => window.clearInterval(timer); }, [load]);
  const control = async (service: SystemMonitorData["services"][number], action: "start" | "restart") => {
    const verb = action === "restart" ? "Restart" : "Start";
    if (!await confirmDialog(`ยืนยัน ${verb} ${service.displayName}?\nการเชื่อมต่ออาจหยุดชั่วคราว`)) return;
    setBusy(service.key);
    try {
      const response = await fetch(`${apiUrl}/system-monitor/services/${encodeURIComponent(service.key)}/${action}`, { method: "POST", headers });
      if (!response.ok) { const problem = await response.json(); throw new Error(problem.detail ?? `${verb} Service ไม่สำเร็จ`); }
      await load(true);
    } catch (e) { notify(e instanceof Error ? e.message : `${verb} Service ไม่สำเร็จ`, "error"); }
    finally { setBusy(""); }
  };
  if (loading) return <article className="card empty"><p>กำลังตรวจสอบสถานะระบบ...</p></article>;
  if (error || !data) return <article className="card empty"><div className="login-error">{error}</div><button className="btn" onClick={() => load()}><span className="material-symbols-outlined" aria-hidden="true">refresh</span> ลองใหม่</button></article>;
  const statusTone = (status: string) => status === "Online" || status === "Running" ? "green" : status === "Starting" || status === "Stopping" ? "yellow" : "red";
  return <div className="system-monitor-page">
    <div className="monitor-summary">
      <article className="card monitor-card"><div className="monitor-card-head"><span className="monitor-icon">API</span><Badge tone={statusTone(data.api.status)}>{data.api.status}</Badge></div><h3>QA Management API</h3><p>Process #{data.api.processId} · Uptime {data.api.uptime}</p><div className="monitor-metrics"><span><b>{(data.api.memoryBytes / 1048576).toFixed(1)} MB</b><small>Memory</small></span><span><b>{data.api.processorCount}</b><small>CPU Cores</small></span></div></article>
      <article className="card monitor-card"><div className="monitor-card-head"><span className="monitor-icon">DB</span><Badge tone={statusTone(data.database.status)}>{data.database.status}</Badge></div><h3>QA Database</h3><p>{data.database.error || "เชื่อมต่อฐานข้อมูลสำเร็จ"}</p><div className="monitor-metrics"><span><b>{data.database.responseMilliseconds.toFixed(0)} ms</b><small>Response</small></span><span><b>{data.machineName}</b><small>Machine</small></span></div></article>
    </div>
    <article className="card monitor-services"><div className="monitor-section-head"><div><h3>Managed Services</h3><p>แสดงเฉพาะ Service ที่อนุญาตไว้ใน Server configuration</p></div><button className="btn" onClick={() => load()} disabled={loading || !!busy}><span className="material-symbols-outlined" aria-hidden="true">refresh</span> Refresh</button></div>
      <div className="monitor-service-list">{data.services.length === 0 ? <div className="empty"><p>ยังไม่มี Service ในรายการที่อนุญาต</p></div> : data.services.map((service) => <div className="monitor-service" key={service.key}><span className={`service-light ${service.isRunning ? "online" : "offline"}`} /><div><b>{service.displayName}</b><small>{service.description || service.key}</small>{service.error && <em>{service.error}</em>}</div><Badge tone={statusTone(service.status)}>{service.status}</Badge><div className="row-actions"><button className="btn" disabled={!!busy || service.isRunning} onClick={() => control(service, "start")}>{busy === service.key ? "กำลังทำงาน..." : "Start"}</button><button className="btn primary" disabled={!!busy || !service.isRunning} onClick={() => control(service, "restart")}>{busy === service.key ? "กำลังทำงาน..." : "Restart"}</button></div></div>)}</div>
    </article>
    <footer className="monitor-footer">ตรวจล่าสุด {formatThaiDateTime(data.checkedAt)} · {data.environment}</footer>
  </div>;
}
