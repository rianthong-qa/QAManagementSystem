# Cloudflare Tunnel setup (Cloudflared) — Quick Guide

ไฟล์ในโฟลเดอร์นี้ช่วยให้คุณตั้ง Cloudflare Tunnel บน Windows ได้อย่างรวดเร็ว โดยสรุปขั้นตอนดังนี้

1) เปิด PowerShell เป็น Administrator (เพื่อการติดตั้ง service)

2) รันสคริปต์ช่วยติดตั้งแบบ interactive:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\cloudflare\setup-tunnel.ps1
```

สคริปต์จะ:
- ตรวจสอบ/ติดตั้ง `cloudflared` ผ่าน `winget`
- เปิดหน้าเว็บให้ล็อกอินกับ Cloudflare (`cloudflared tunnel login`)
- สร้าง Tunnel และไฟล์ credentials ใน `%USERPROFILE%\\.cloudflared\`
- เขียน `config.yml` ตัวอย่างที่ `%USERPROFILE%\\.cloudflared\config.yml`
- พยายามสร้าง DNS route (`cloudflared tunnel route dns`)
- ติดตั้งเป็น Windows service ถ้าคุณเลือก

3) ตรวจสอบผลลัพธ์บน Cloudflare Dashboard → Zero Trust → Tunnels

ปรับแต่ง
- ถ้าคุณมีโดเมน/ซับโดเมน: ระบุค่า hostname เมื่อสคริปต์ถาม (เช่น `api.example.com`)
- ถ้าแอปของคุณรันบนพอร์ตอื่น ให้ระบุ service เป็น `http://localhost:PORT`

ถ้าต้องการ ผมสามารถสร้างไฟล์ `config.yml` ที่ระบุค่าเฉพาะสำหรับ `ProMaxx2.QA.Api` (ตัวอย่าง: `api.yourdomain.com -> http://localhost:5000`) — ส่งชื่อโดเมน/พอร์ตมาได้เลย
