import { useEffect, useRef, useState, type ReactNode } from "react";
import { copyText } from "../shared/appShared";
import { notify } from "./dialogStore";

/**
 * ปุ่มคัดลอกที่ยืนยันผลบนตัวปุ่มเอง ("✓ คัดลอกแล้ว" 2 วินาที) แทน toast — ผู้ใช้กำลังมองปุ่มอยู่จึงเห็นทันที
 * คัดลอกไม่สำเร็จยังแจ้ง toast error เพราะต้องบอกวิธีทำต่อ
 */
export function CopyButton({ text, children, copiedLabel = "คัดลอกแล้ว", failMessage = "คัดลอกอัตโนมัติไม่สำเร็จ", className = "btn", disabled, title, ariaLabel }: {
  text: string | (() => string); children: ReactNode; copiedLabel?: string; failMessage?: string; className?: string; disabled?: boolean; title?: string; ariaLabel?: string;
}) {
  const [copied, setCopied] = useState(false);
  const timer = useRef<number | undefined>(undefined);
  useEffect(() => () => window.clearTimeout(timer.current), []);
  const copy = async () => {
    if (!await copyText(typeof text === "function" ? text() : text)) { notify(failMessage, "error"); return; }
    setCopied(true);
    window.clearTimeout(timer.current);
    timer.current = window.setTimeout(() => setCopied(false), 2000);
  };
  return (
    <button className={`${className} copy-button${copied ? " copied" : ""}`} type="button" onClick={() => void copy()} disabled={disabled} title={title} aria-label={ariaLabel}>
      <span className="material-symbols-outlined" aria-hidden="true">{copied ? "check" : "content_copy"}</span>
      {copied ? copiedLabel : children}
      <span className="copy-button-status" role="status">{copied ? copiedLabel : ""}</span>
    </button>
  );
}
