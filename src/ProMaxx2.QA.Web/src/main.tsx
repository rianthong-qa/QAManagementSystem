import { StrictMode, Fragment } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { DialogHost } from './components/DialogHost'
import { installTableCardLabels } from './components/tableCardLabels'

installTableCardLabels()

// ผู้ใช้จริงเปิดผ่าน Vite dev server — StrictMode (dev) รัน effect ซ้ำ 2 รอบตอนเปิดหน้า ทุกหน้าจึงยิงคำขอซ้ำ
// (รอบแรกถูกยกเลิกเป็น "canceled" สีแดงใน DevTools หรือส่งครบทั้งสองรอบ) ผ่าน Cloudflare ~0.5 วินาที/คำขอ
// จึงปิดเป็นค่าเริ่มต้น — ตอนพัฒนาเปิดด้วย PowerShell `$env:VITE_QA_STRICT='true'; npm.cmd run dev` (production build ไม่มีผลอยู่แล้ว)
const Root = import.meta.env.VITE_QA_STRICT === 'true' ? StrictMode : Fragment

createRoot(document.getElementById('root')!).render(
  <Root>
    <App />
    <DialogHost />
  </Root>,
)
