import { apiUrl, authHeaders } from "../api";

/**
 * ดาวน์โหลดเอกสารในคลังความรู้ด้วย Authorization header (ลิงก์ตรงส่ง header ไม่ได้)
 * ส่งชื่อไฟล์มาเอง — header Content-Disposition อ่านข้ามโดเมนไม่ได้ (API อยู่คนละโดเมนกับหน้าเว็บ และไม่ได้ expose header นี้)
 */
export async function downloadKnowledgeFile(fileId: string, fileName: string) {
  const response = await fetch(`${apiUrl}/knowledge/files/${fileId}/download`, { headers: authHeaders() });
  if (!response.ok) throw new Error(response.status === 404 ? "ไม่พบเอกสาร หรือคุณไม่มีสิทธิ์ใน Project ของเอกสารนี้" : `ดาวน์โหลดไม่สำเร็จ (${response.status})`);
  const url = URL.createObjectURL(await response.blob());
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 30_000);
}
