# Repository Instructions

## UI work

ก่อนสร้าง แก้ไข หรือรีวิว UI ใด ๆ ใน repository นี้ ต้องอ่าน `Document/02-Developer-Blueprint/UI_DESIGN_SYSTEM.md` ทั้งไฟล์และใช้เป็นหลักในการตัดสินใจ

ข้อกำหนด:

1. UI ใหม่และ UI ที่แก้ไขต้องสอดคล้องกับ design tokens, form, modal, responsive และ accessibility rules ในเอกสารดังกล่าว
2. ต้องตรวจทั้ง Desktop และ Mobile และห้ามทำให้เกิด horizontal scroll ระดับหน้าโดยไม่จำเป็น
3. หากงาน UI ทำให้เกิด pattern หรือกฎใหม่ ต้องอัปเดต `UI_DESIGN_SYSTEM.md` และ Change Log ภายในงานเดียวกัน
4. หลังแก้ frontend ต้องรัน `npm.cmd run build`, `npm.cmd run lint` และ `git diff --check`
5. หาก requirement ของผู้ใช้ขัดกับเอกสาร ให้ทำตาม requirement ล่าสุดของผู้ใช้และอัปเดตเอกสารให้ตรงกับผลลัพธ์ใหม่

