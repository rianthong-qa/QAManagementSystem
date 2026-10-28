import { useEffect, useState } from "react";
import { ModalShell } from "./components/ModalShell";
import { formatThaiDateTime } from "./dateTime";
import { apiUrl } from "./api";
import "./components/ListPage.css";
import "./AuditLog.css";

type AuditLogItem = {
  timestamp: string;
  actorName?: string | null;
  action: string;
  entity: string;
  entityId: string;
  summary?: string | null;
};

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
  const [pageSize, setPageSize] = useState(25);
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
  }, [page, pageSize, search, entity, refresh]);

  const pageCount = Math.max(1, Math.ceil(total / pageSize));
  const filterCount = [searchInput.trim(), entity].filter(Boolean).length;
  const hasFilters = filterCount > 0;
  const rangeStart = total ? (page - 1) * pageSize + 1 : 0;
  const rangeEnd = Math.min(page * pageSize, total);
  const clearFilters = () => {
    setSearchInput("");
    setSearch("");
    setEntity("");
    setPage(1);
  };

  return (
    <>
      <article className="card lp-card audit-log-page" aria-label="รายการ Audit Log" aria-busy={loading}>
        <div className="lp-head">
          <div className="lp-title">
            <div>
              <strong>{total.toLocaleString("th-TH")}</strong>
              <span>Audit Log{filterCount ? ` · กรอง ${filterCount} เงื่อนไข` : ""}</span>
            </div>
            <small>กิจกรรมล่าสุดแสดงก่อน · ดูรายละเอียดได้อย่างเดียว</small>
          </div>
          <div className="lp-actions">
            <button className="btn" type="button" onClick={() => setRefresh((value) => value + 1)} disabled={loading}>
              <span className="material-symbols-outlined" aria-hidden="true">refresh</span>
              รีเฟรช
            </button>
          </div>
        </div>

        <div className="lp-filters audit-filters" role="group" aria-label="ตัวกรอง Audit Log">
          <label className="lp-search">
            <span className="material-symbols-outlined" aria-hidden="true">search</span>
            <input
              type="search"
              value={searchInput}
              onChange={(event) => setSearchInput(event.target.value)}
              placeholder="ค้นหา Action, ประเภท, ID หรือรายละเอียด"
              aria-label="ค้นหา Audit Log ตาม Action ประเภท ID หรือรายละเอียด"
            />
          </label>
          <select aria-label="ประเภทข้อมูล" value={entity} onChange={(event) => { setEntity(event.target.value); setPage(1); }}>
            <option value="">ทุกประเภทข้อมูล</option>
            <option value="Defect">Defect</option>
            <option value="Regression">Regression</option>
            <option value="TestCycle">Test Cycle</option>
          </select>
          {hasFilters && (
            <button className="btn lp-clear" type="button" onClick={clearFilters}>
              <span className="material-symbols-outlined" aria-hidden="true">filter_alt_off</span>
              ล้างตัวกรอง
            </button>
          )}
        </div>

        {error ? (
          <div className="inline-alert error audit-alert" role="alert">
            <span>{error}</span>
            <button className="btn" type="button" onClick={() => setRefresh((value) => value + 1)}>ลองใหม่</button>
          </div>
        ) : loading ? (
          <div className="audit-feedback" role="status"><span className="audit-spinner" aria-hidden="true" />กำลังโหลด Audit Log...</div>
        ) : items.length === 0 ? (
          <div className="lp-empty audit-empty">
            <span className="material-symbols-outlined" aria-hidden="true">manage_search</span>
            <b>ไม่พบกิจกรรม</b>
            <small>{hasFilters ? "ลองเปลี่ยนคำค้นหาหรือตัวกรอง" : "เมื่อมีกิจกรรมในระบบ รายการจะแสดงที่นี่"}</small>
          </div>
        ) : (
          <>
            <div className="audit-list-heading">
              <span>แสดง {rangeStart.toLocaleString("th-TH")}–{rangeEnd.toLocaleString("th-TH")} จาก {total.toLocaleString("th-TH")} รายการ</span>
              <span className="audit-sort-note"><span className="material-symbols-outlined" aria-hidden="true">schedule</span> ล่าสุดก่อน</span>
            </div>
            <div className="lp-table-wrap">
              <table className="lp-table audit-table">
                <caption className="sr-only">รายการกิจกรรม Audit Log เรียงจากล่าสุดก่อน</caption>
                <thead>
                  <tr>
                    <th className="audit-col-date" scope="col">วันและเวลา</th>
                    <th className="audit-col-actor" scope="col">ผู้ดำเนินการ</th>
                    <th className="audit-col-action" scope="col">Action</th>
                    <th className="audit-col-entity" scope="col">ข้อมูลที่เกี่ยวข้อง</th>
                    <th className="audit-col-summary" scope="col">รายละเอียด</th>
                    <th className="audit-col-actions" scope="col"><span className="sr-only">ดูรายละเอียด</span></th>
                  </tr>
                </thead>
                <tbody>
                  {items.map((item, index) => (
                    <tr key={`${item.timestamp}-${item.entity}-${item.entityId}-${index}`}>
                      <td className="audit-col-date audit-labeled" data-label="วันและเวลา">
                        <time dateTime={item.timestamp}>
                          <strong>{formatThaiDateTime(item.timestamp, dateOptions)}</strong>
                          <small>{formatThaiDateTime(item.timestamp, timeOptions)} น.</small>
                        </time>
                      </td>
                      <td className="audit-col-actor audit-labeled" data-label="ผู้ดำเนินการ">
                        <span className="audit-actor"><span className="audit-avatar" aria-hidden="true">{(item.actorName?.trim() || "S").slice(0, 1).toUpperCase()}</span><span>{item.actorName?.trim() || "System"}</span></span>
                      </td>
                      <td className="audit-col-action audit-labeled" data-label="Action"><span className={`audit-action audit-action-${actionTone(item.action)}`}>{item.action}</span></td>
                      <td className="audit-col-entity audit-labeled" data-label="ข้อมูลที่เกี่ยวข้อง"><span className="audit-entity-name">{item.entity || "-"}</span><small className="audit-entity-id" title={item.entityId}>{item.entityId || "-"}</small></td>
                      <td className="audit-col-summary audit-labeled" data-label="รายละเอียด">{item.summary || "ไม่มีรายละเอียดเพิ่มเติม"}</td>
                      <td className="audit-col-actions"><button className="audit-detail-button" type="button" onClick={() => setSelected(item)} aria-label={`ดูรายละเอียด ${item.action} ${item.entity} ${item.entityId}`} title="ดูรายละเอียด"><span className="material-symbols-outlined" aria-hidden="true">arrow_forward</span></button></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <div className="pagination suite-pagination audit-pagination">
              <label>แสดง<select value={pageSize} onChange={(event) => { setPageSize(Number(event.target.value)); setPage(1); }}><option value="25">25</option><option value="50">50</option><option value="100">100</option></select> รายการ</label>
              <span>หน้า {page.toLocaleString("th-TH")} / {pageCount.toLocaleString("th-TH")} · ทั้งหมด {total.toLocaleString("th-TH")} รายการ</span>
              <div>
                <button className="btn" type="button" onClick={() => setPage((value) => value - 1)} disabled={page <= 1}><span className="material-symbols-outlined" aria-hidden="true">chevron_left</span> ก่อนหน้า</button>
                <button className="btn" type="button" onClick={() => setPage((value) => value + 1)} disabled={page >= pageCount}>ถัดไป <span className="material-symbols-outlined" aria-hidden="true">chevron_right</span></button>
              </div>
            </div>
          </>
        )}
      </article>

      {selected && (
        <ModalShell labelledBy="audit-detail-title" className="audit-detail-dialog" onDismiss={() => setSelected(null)}>
          <div className="modal-head">
            <div><span className="audit-eyebrow">AUDIT DETAIL</span><h2 id="audit-detail-title">รายละเอียดกิจกรรม</h2></div>
            <button type="button" onClick={() => setSelected(null)} aria-label="ปิดรายละเอียด"><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
          </div>
          <div className="audit-detail-body">
            <div className="audit-detail-lead"><span className={`audit-action audit-action-${actionTone(selected.action)}`}>{selected.action}</span><p>{selected.summary || "ไม่มีรายละเอียดเพิ่มเติม"}</p></div>
            <dl>
              <div><dt>วันและเวลา</dt><dd>{formatThaiDateTime(selected.timestamp, { ...dateOptions, ...timeOptions })} น.</dd></div>
              <div><dt>ผู้ดำเนินการ</dt><dd>{selected.actorName?.trim() || "System"}</dd></div>
              <div><dt>ประเภทข้อมูล</dt><dd>{selected.entity || "-"}</dd></div>
              <div><dt>รหัสข้อมูล</dt><dd>{selected.entityId || "-"}</dd></div>
            </dl>
          </div>
          <div className="modal-actions"><button className="btn primary" type="button" onClick={() => setSelected(null)}>ปิด</button></div>
        </ModalShell>
      )}
    </>
  );
}
