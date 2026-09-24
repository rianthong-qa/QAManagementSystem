import { useEffect, useState } from "react";
import { ModalShell } from "./components/ModalShell";
import { formatThaiDateTime } from "./dateTime";
import { apiUrl } from "./api";
import "./AuditLog.css";

type AuditLogItem = {
  timestamp: string;
  actorName?: string | null;
  action: string;
  entity: string;
  entityId: string;
  summary?: string | null;
};

const pageSize = 25;
const dateOptions: Intl.DateTimeFormatOptions = { day: "numeric", month: "short", year: "numeric" };
const timeOptions: Intl.DateTimeFormatOptions = { hour: "2-digit", minute: "2-digit" };

function actionTone(action: string) {
  const value = action.toLowerCase();
  if (/delete|remove|cancel|reject/.test(value)) return "danger";
  if (/create|add|pass|approve/.test(value)) return "success";
  return "info";
}

export function AuditLogPage() {
  const [items, setItems] = useState<AuditLogItem[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState("");
  const [entity, setEntity] = useState("");
  const [refresh, setRefresh] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [selected, setSelected] = useState<AuditLogItem | null>(null);

  useEffect(() => {
    const timeout = window.setTimeout(() => {
      setPage(1);
      setSearch(searchInput.trim());
    }, 300);
    return () => window.clearTimeout(timeout);
  }, [searchInput]);

  useEffect(() => {
    const controller = new AbortController();
    const query = new URLSearchParams({ page: String(page), size: String(pageSize) });
    if (search) query.set("search", search);
    if (entity) query.set("entity", entity);
    setLoading(true);
    setError("");
    fetch(`${apiUrl}/audit-logs?${query}`, {
      signal: controller.signal,
      headers: { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` },
    })
      .then(async (response) => {
        if (!response.ok) throw new Error(`โหลด Audit Log ไม่สำเร็จ (${response.status})`);
        return response.json();
      })
      .then((data) => {
        if (controller.signal.aborted) return;
        setItems(Array.isArray(data.items) ? data.items : []);
        setTotal(Number(data.total) || 0);
      })
      .catch((cause) => {
        if (!controller.signal.aborted) setError(cause instanceof Error ? cause.message : "โหลด Audit Log ไม่สำเร็จ");
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [page, search, entity, refresh]);

  // Escape/focus ของ modal รายละเอียดจัดการโดย ModalShell แล้ว

  const pageCount = Math.max(1, Math.ceil(total / pageSize));
  const hasFilters = Boolean(searchInput || entity);

  return <>
    <section className="content-card audit-log-page" aria-label="รายการ Audit Log">
      <div className="audit-toolbar">
        <label className="audit-search"><span className="material-symbols-outlined" aria-hidden="true">search</span><input type="search" value={searchInput} onChange={(event) => setSearchInput(event.target.value)} placeholder="ค้นหา Action, ประเภท, ID หรือรายละเอียด" aria-label="ค้นหา Audit Log ตาม Action ประเภท ID หรือรายละเอียด" /></label>
        <label className="audit-entity"><span>ประเภทข้อมูล</span><select value={entity} onChange={(event) => { setEntity(event.target.value); setPage(1); }}><option value="">ทุกประเภท</option><option value="Defect">Defect</option><option value="Regression">Regression</option><option value="TestCycle">Test Cycle</option></select></label>
        {hasFilters && <button className="btn audit-clear" type="button" onClick={() => { setSearchInput(""); setSearch(""); setEntity(""); setPage(1); }}>ล้างตัวกรอง</button>}
        <button className="btn audit-refresh" type="button" onClick={() => setRefresh((value) => value + 1)} disabled={loading} aria-label="โหลด Audit Log ใหม่"><span className="material-symbols-outlined" aria-hidden="true">refresh</span><span>รีเฟรช</span></button>
      </div>

      <div className="audit-list-heading"><div><h3>รายการกิจกรรม</h3><p>{loading ? "กำลังโหลดข้อมูล..." : error ? "โหลดข้อมูลไม่สำเร็จ" : total ? `แสดง ${((page - 1) * pageSize + 1).toLocaleString("th-TH")}–${Math.min(page * pageSize, total).toLocaleString("th-TH")} จาก ${total.toLocaleString("th-TH")} รายการ` : "ยังไม่มีรายการที่ตรงกับตัวกรอง"}</p></div><span className="audit-sort-note"><span className="material-symbols-outlined" aria-hidden="true">schedule</span> ล่าสุดก่อน</span></div>

      {error ? <div className="audit-feedback" role="alert"><span className="material-symbols-outlined" aria-hidden="true">error</span><b>{error}</b><button className="btn" type="button" onClick={() => setRefresh((value) => value + 1)}>ลองอีกครั้ง</button></div>
        : loading ? <div className="audit-feedback" role="status"><span className="audit-spinner" aria-hidden="true" />กำลังโหลด Audit Log...</div>
          : items.length === 0 ? <div className="audit-feedback audit-empty"><span className="material-symbols-outlined" aria-hidden="true">manage_search</span><b>ไม่พบกิจกรรม</b><p>{hasFilters ? "ลองเปลี่ยนคำค้นหาหรือตัวกรอง" : "เมื่อมีกิจกรรมในระบบ รายการจะแสดงที่นี่"}</p></div>
            : <><div className="audit-table-wrap"><table className="audit-table"><thead><tr><th>วันและเวลา</th><th>ผู้ดำเนินการ</th><th>Action</th><th>ข้อมูลที่เกี่ยวข้อง</th><th>รายละเอียด</th><th><span className="sr-only">ดูรายละเอียด</span></th></tr></thead><tbody>{items.map((item, index) => <tr key={`${item.timestamp}-${item.entity}-${item.entityId}-${index}`}><td data-label="วันและเวลา"><time dateTime={item.timestamp}><strong>{formatThaiDateTime(item.timestamp, dateOptions)}</strong><small>{formatThaiDateTime(item.timestamp, timeOptions)} น.</small></time></td><td data-label="ผู้ดำเนินการ"><span className="audit-actor"><span className="audit-avatar" aria-hidden="true">{(item.actorName?.trim() || "S").slice(0, 1).toUpperCase()}</span><span>{item.actorName?.trim() || "System"}</span></span></td><td data-label="Action"><span className={`audit-action audit-action-${actionTone(item.action)}`}>{item.action}</span></td><td data-label="ข้อมูลที่เกี่ยวข้อง"><span className="audit-entity-name">{item.entity || "-"}</span><small className="audit-entity-id" title={item.entityId}>{item.entityId || "-"}</small></td><td data-label="รายละเอียด" className="audit-summary">{item.summary || "ไม่มีรายละเอียดเพิ่มเติม"}</td><td className="audit-row-action"><button className="audit-detail-button" type="button" onClick={() => setSelected(item)} aria-label={`ดูรายละเอียด ${item.action} ${item.entity} ${item.entityId}`} title="ดูรายละเอียด"><span className="material-symbols-outlined" aria-hidden="true">arrow_forward</span></button></td></tr>)}</tbody></table></div><div className="audit-pagination"><span>หน้า {page.toLocaleString("th-TH")} จาก {pageCount.toLocaleString("th-TH")}</span><div><button className="btn" type="button" onClick={() => setPage((value) => value - 1)} disabled={page <= 1} aria-label="หน้าก่อนหน้า"><span className="material-symbols-outlined" aria-hidden="true">chevron_left</span></button><button className="btn" type="button" onClick={() => setPage((value) => value + 1)} disabled={page >= pageCount} aria-label="หน้าถัดไป"><span className="material-symbols-outlined" aria-hidden="true">chevron_right</span></button></div></div></>}
    </section>

    {selected && <ModalShell labelledBy="audit-detail-title" className="audit-detail-dialog" onDismiss={() => setSelected(null)}><div className="modal-head"><div><span className="audit-eyebrow">AUDIT DETAIL</span><h2 id="audit-detail-title">รายละเอียดกิจกรรม</h2></div><button type="button" onClick={() => setSelected(null)} aria-label="ปิดรายละเอียด"><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div><div className="audit-detail-body"><div className="audit-detail-lead"><span className={`audit-action audit-action-${actionTone(selected.action)}`}>{selected.action}</span><p>{selected.summary || "ไม่มีรายละเอียดเพิ่มเติม"}</p></div><dl><div><dt>วันและเวลา</dt><dd>{formatThaiDateTime(selected.timestamp, { ...dateOptions, ...timeOptions })} น.</dd></div><div><dt>ผู้ดำเนินการ</dt><dd>{selected.actorName?.trim() || "System"}</dd></div><div><dt>ประเภทข้อมูล</dt><dd>{selected.entity || "-"}</dd></div><div><dt>รหัสข้อมูล</dt><dd>{selected.entityId || "-"}</dd></div></dl></div><div className="modal-actions"><button className="btn primary" type="button" onClick={() => setSelected(null)}>ปิด</button></div></ModalShell>}
  </>;
}
