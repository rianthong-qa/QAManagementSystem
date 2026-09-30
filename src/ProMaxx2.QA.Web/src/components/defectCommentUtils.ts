import { useEffect, useState } from "react";
import { authHeaders } from "../api";
import type { LightboxImage } from "./ImageLightbox";

export type DefectCommentAttachment = { attachmentId: string; fileName: string; size: number };
export type DefectComment = { commentId: string; source: "QaHub" | "Crm" | string; body: string; authorName: string; createdAt: string; attachments: DefectCommentAttachment[] };

export const toLightboxImages = (attachments: DefectCommentAttachment[], imageSrc: (attachmentId: string) => string | undefined): LightboxImage[] =>
  attachments.map((a) => ({ src: imageSrc(a.attachmentId), name: a.fileName }));

/** โหลดรูปที่ต้องใช้ token (endpoint /defects/{id}/attachments/{attachmentId}) เป็น blob URL — คืน map attachmentId → URL
 * และ revoke URL ทั้งหมดเมื่อชุดรูปเปลี่ยนหรือ component ถูกถอด */
export function useAuthedAttachmentUrls(apiUrl: string, defectId: string | undefined, attachmentIds: string[]): Record<string, string> {
  const [urls, setUrls] = useState<Record<string, string>>({});
  const key = attachmentIds.join(",");
  useEffect(() => {
    if (!defectId || !key) { setUrls({}); return; }
    const controller = new AbortController();
    const created: string[] = [];
    key.split(",").forEach(async (id) => {
      try {
        const response = await fetch(`${apiUrl}/defects/${defectId}/attachments/${id}`, { headers: authHeaders(), signal: controller.signal });
        if (!response.ok) return;
        const url = URL.createObjectURL(await response.blob());
        if (controller.signal.aborted) { URL.revokeObjectURL(url); return; }
        created.push(url);
        setUrls((cur) => ({ ...cur, [id]: url }));
      } catch { /* รูปที่โหลดไม่ได้แสดงเป็นไอคอนแทน */ }
    });
    return () => { controller.abort(); created.forEach((u) => URL.revokeObjectURL(u)); setUrls({}); };
  }, [apiUrl, defectId, key]);
  return urls;
}
