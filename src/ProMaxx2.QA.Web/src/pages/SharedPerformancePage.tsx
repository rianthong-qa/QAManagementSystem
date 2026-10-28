import { useEffect, useState } from "react";
import { ApiError, apiUrl, getJson, isAbortError } from "../api";
import { formatThaiDateTime } from "../dateTime";
import { EmployeeReportView } from "./EmployeeReport";
import type { EmployeeReport } from "./performanceUtils";
import "./Performance.css";

// หน้าอ่านอย่างเดียวของสรุปผลงานพนักงานจากลิงก์ `?p=<code>` ที่หัวหน้าส่งให้ HR — เปิดได้โดยไม่ต้อง login
// endpoint /shared/performance/* เป็น anonymous สิทธิ์มาจาก code ที่ผูกกับพนักงาน 1 คน + ช่วงวันที่ และมีวันหมดอายุ
type SharedReport = { report: EmployeeReport; expiresAt: string; sharedAt: string; sharedByName?: string | null; note?: string | null };

export default function SharedPerformancePage({ code }: { code: string }) {
  const [data, setData] = useState<SharedReport | null>(null);
  const [error, setError] = useState<{ icon: string; message: string } | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    getJson<SharedReport>(`${apiUrl}/shared/performance/${encodeURIComponent(code)}`, controller.signal)
      .then(setData)
      .catch((e) => {
        if (isAbortError(e)) return;
        if (e instanceof ApiError && e.status === 410) setError({ icon: "timer_off", message: e.message });
        else if (e instanceof ApiError && e.status === 404) setError({ icon: "link_off", message: "ลิงก์ไม่ถูกต้อง กรุณาตรวจสอบลิงก์ที่ได้รับอีกครั้ง" });
        else if (e instanceof ApiError && e.status === 429) setError({ icon: "hourglass_top", message: "เปิดลิงก์ถี่เกินไป กรุณารอสักครู่แล้วลองใหม่" });
        else setError({ icon: "cloud_off", message: "โหลดข้อมูลไม่สำเร็จ กรุณาลองใหม่อีกครั้ง" });
      });
    return () => controller.abort();
  }, [code]);

  useEffect(() => { if (data) document.title = `สรุปผลงาน ${data.report.profile.displayName} · ProMaxx2 QA Hub`; }, [data]);

  if (error) return <div className="card perf-shared-state" role="alert"><span className="material-symbols-outlined" aria-hidden="true">{error.icon}</span><p>{error.message}</p></div>;
  if (!data) return <div className="card perf-shared-state" role="status"><span className="spinner" aria-hidden="true" /><p>กำลังโหลดสรุปผลงาน...</p></div>;

  return (
    <div className="performance-page">
      <EmployeeReportView
        report={data.report}
        actions={<button className="btn" type="button" onClick={() => window.print()}><span className="material-symbols-outlined" aria-hidden="true">print</span>พิมพ์ / บันทึก PDF</button>}
        banner={
          <div className="perf-shared-banner" role="note">
            <span className="material-symbols-outlined" aria-hidden="true">lock</span>
            <span>
              เอกสารภายใน — แชร์โดย <b>{data.sharedByName ?? "หัวหน้าทีม QA"}</b> เมื่อ {formatThaiDateTime(data.sharedAt, { dateStyle: "medium" })}
              {data.note && <> · {data.note}</>} · ลิงก์ใช้ได้ถึง {formatThaiDateTime(data.expiresAt, { dateStyle: "long", timeStyle: "short" })} กรุณาอย่าส่งต่อ
            </span>
          </div>
        }
      />
    </div>
  );
}
