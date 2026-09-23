// จุดกลางสำหรับเรียก QA Hub API — ใช้แทนการประกาศ apiUrl/อ่าน token จาก localStorage ซ้ำในแต่ละไฟล์
// (ย้ายโค้ดเดิมมาใช้ทีละส่วน: ไฟล์ใหม่และไฟล์ที่แก้ต้องใช้ helper ในนี้)

export const apiUrl: string = import.meta.env.VITE_API_URL ?? "/api/v1";

const tokenKey = "qa.accessToken";

export function getToken(): string | null {
  try { return localStorage.getItem(tokenKey); } catch { return null; }
}

export function authHeaders(json = false): Record<string, string> {
  const token = getToken();
  return {
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
    ...(json ? { "Content-Type": "application/json" } : {}),
  };
}

/** true เมื่อ URL ชี้ไปที่ QA Hub API (ใช้ตัดสินว่า 401 หมายถึง session หมดอายุจริง ไม่ใช่ 401 จากบริการอื่น) */
export function isApiRequest(url: string): boolean {
  try {
    const base = new URL(apiUrl, window.location.origin);
    const target = new URL(url, window.location.origin);
    return target.origin === base.origin && target.pathname.startsWith(base.pathname);
  } catch {
    return url.startsWith(apiUrl);
  }
}

export class ApiError extends Error {
  readonly status: number;
  constructor(status: number, message: string) {
    super(message);
    this.name = "ApiError";
    this.status = status;
  }
}

type ApiOptions = { method?: string; json?: unknown; form?: FormData; signal?: AbortSignal; fallbackMessage?: string };

/** เรียก API พร้อม Authorization และแปลง error เป็น ApiError ที่มีข้อความจาก ProblemDetails.detail */
export async function apiFetch<T = unknown>(path: string, options: ApiOptions = {}): Promise<T> {
  const { method = "GET", json, form, signal, fallbackMessage = "เรียกข้อมูลจากระบบไม่สำเร็จ" } = options;
  const response = await fetch(`${apiUrl}${path}`, {
    method,
    signal,
    headers: authHeaders(json !== undefined),
    body: form ?? (json !== undefined ? JSON.stringify(json) : undefined),
  });
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { detail?: string; title?: string } | null;
    throw new ApiError(response.status, problem?.detail ?? problem?.title ?? `${fallbackMessage} (${response.status})`);
  }
  if (response.status === 204) return undefined as T;
  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

/** escape ข้อความก่อนใส่ลง HTML string (PDF/Excel export ที่สร้าง markup เอง) */
export function escapeHtml(value: unknown): string {
  return String(value ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c] as string);
}
