import type { ReactNode } from "react";
import "./JiraParts.css";

/* ชิ้นส่วนแบบ Jira ที่หลายหน้าใช้ร่วม (หน้า Defect, CRM): avatar อักษรย่อ, ชื่อคน + avatar และป้ายสถานะ (lozenge)
   class คง prefix dj- เดิมของหน้า Defect — สีของป้ายตามสถานะเฉพาะหน้าอยู่ใน CSS ของหน้านั้น (เช่น .dj-lozenge.is-open) */

// ตัวอักษรแรกของชื่อ (ข้ามสระหน้าภาษาไทย เ แ โ ใ ไ) สูงสุด 2 คำ; สีเลือกจากชื่อให้คนเดิมได้สีเดิมเสมอ
const initials = (name: string) => name.trim().replace(/^\d+\s+/, "").split(/\s+/).slice(0, 2).map((word) => word.replace(/^[เแโใไ]/, "").charAt(0)).join("").toUpperCase();
const avatarTone = (name: string) => [...name].reduce((sum, ch) => sum + ch.charCodeAt(0), 0) % 6;

export function Avatar({ name, size = 24 }: { name?: string | null; size?: number }) {
  if (!name) return <span className="dj-avatar is-empty" style={{ width: size, height: size }} title="ยังไม่มอบหมาย" aria-hidden="true"><span className="material-symbols-outlined">person</span></span>;
  return <span className={`dj-avatar tone-${avatarTone(name)}`} style={{ width: size, height: size, fontSize: Math.max(11, Math.round(size * 0.42)) }} title={name} aria-hidden="true">{initials(name) || "?"}</span>;
}

export function AssigneeCell({ name, empty = "ยังไม่มอบหมาย" }: { name?: string | null; empty?: string }) {
  return <span className="dj-person"><Avatar name={name} /><span className={name ? undefined : "dj-muted"}>{name ?? empty}</span></span>;
}


export type LozengeTone = "gray" | "blue" | "green" | "green-solid" | "purple" | "red" | "outline";
/** ป้ายสถานะตัวพิมพ์ใหญ่ 11px — tone ตามหมวดสถานะ (ไม่สื่อด้วยสีอย่างเดียว ข้อความคือชื่อสถานะเสมอ) */
export function Lozenge({ tone, children, title }: { tone: LozengeTone; children: ReactNode; title?: string }) {
  return <span className={`dj-lozenge tone-${tone}`} title={title}>{children}</span>;
}
