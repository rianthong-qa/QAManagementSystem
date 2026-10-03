## สรุป

<!-- เปลี่ยนอะไร และทำไม (1–3 บรรทัด) -->

## ผลกระทบ

- หน้าจอ / API ที่เปลี่ยน:
- ฐานข้อมูล / migration: ไม่มี
- ระบบภายนอก (CRM, Agent ฯลฯ): ไม่มี

## ผลตรวจอัตโนมัติ

<!-- วางตารางสรุปจาก: powershell -ExecutionPolicy Bypass -File tools\pre-pr-check.ps1 -->

```
```

## วิธีทดสอบ

- [ ] ทดสอบ flow หลักในแอปจริงแล้ว:
- [ ] Desktop (~1440px) และ Mobile (~390px) — แนบภาพเมื่อแก้ UI
- [ ] Restart API แล้ว (เมื่อแก้ backend)

## Checklist (ดู Document/02-Developer-Blueprint/PRE_PR_REVIEW.md)

- [ ] `pre-pr-check.ps1` ไม่มี FAIL และอธิบาย WARN ทุกข้อด้านล่าง
- [ ] อ่าน diff ตัวเองครบทุกไฟล์ (ไม่มีโค้ด debug / ไฟล์ชั่วคราว)
- [ ] Endpoint ใหม่ตรวจสิทธิ์และ Project Access
- [ ] ไม่มีข้อมูลลับ (password / key / token)
- [ ] อัปเดตเอกสารที่เกี่ยวข้อง (`UI_DESIGN_SYSTEM.md`, `AUTOMATION_TODO.md`, `API_SPECIFICATION.md`)
- [ ] รัน `/code-review` และจัดการ finding แล้ว

## ความเสี่ยง / สิ่งที่ยังไม่ได้ทำ

<!-- WARN ที่ไม่แก้พร้อมเหตุผล, สิ่งที่ยังไม่ได้ทดสอบ, ขั้นตอน deploy พิเศษ -->
