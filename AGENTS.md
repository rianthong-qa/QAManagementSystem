# Repository Instructions

## อ่านก่อนเริ่มงานทุกครั้ง (MANDATORY)

ก่อนเริ่มทำงานใด ๆ ใน repository นี้ (โค้ด เอกสาร หรือ config) ต้องอ่าน `Document/02-Developer-Blueprint/SYSTEM_OVERVIEW.md` **ทั้งไฟล์ก่อนเสมอ** เพื่อทำความเข้าใจภาพรวมระบบ สถาปัตยกรรม วิธี build/run/restart และข้อจำกัดของ environment

## Automation work

ก่อนสร้าง แก้ไข รีวิว หรือวางแผนงานที่เกี่ยวข้องกับหน้า Automation, Automation API, Windows Agent, DSL, Queue, Evidence หรือ Automation Database ต้องอ่าน `Document/03-Architecture-and-Plan/AUTOMATION_TODO.md` **ทั้งไฟล์ก่อนเสมอ**

เมื่อทำงาน Automation เสร็จในแต่ละครั้ง ต้องอัปเดตไฟล์ดังกล่าวภายในงานเดียวกัน โดยอย่างน้อยต้อง:

1. ปรับสถานะรายการที่เกี่ยวข้องตามหลักฐานจริง
2. บันทึกวันที่อัปเดตและสรุปผลใน Progress Log
3. เพิ่มรายการใหม่เมื่อพบ gap, defect หรือ technical debt เพิ่มเติม
4. ห้ามทำเครื่องหมายเสร็จ หากยังไม่ได้ตรวจสอบตาม Acceptance Criteria ของรายการนั้น

## UI work

ก่อนสร้าง แก้ไข หรือรีวิว UI ใด ๆ ใน repository นี้ ต้องอ่าน `Document/02-Developer-Blueprint/UI_DESIGN_SYSTEM.md` ทั้งไฟล์และใช้เป็นหลักในการตัดสินใจ

ข้อกำหนด:

1. UI ใหม่และ UI ที่แก้ไขต้องสอดคล้องกับ design tokens, form, modal, responsive และ accessibility rules ในเอกสารดังกล่าว
2. ต้องตรวจทั้ง Desktop และ Mobile และห้ามทำให้เกิด horizontal scroll ระดับหน้าโดยไม่จำเป็น
3. หากงาน UI ทำให้เกิด pattern หรือกฎใหม่ ต้องอัปเดต `UI_DESIGN_SYSTEM.md` และ Change Log ภายในงานเดียวกัน
4. หลังแก้ frontend ต้องรัน `npm.cmd run build`, `npm.cmd run lint` และ `git diff --check`
5. หาก requirement ของผู้ใช้ขัดกับเอกสาร ให้ทำตาม requirement ล่าสุดของผู้ใช้และอัปเดตเอกสารให้ตรงกับผลลัพธ์ใหม่
