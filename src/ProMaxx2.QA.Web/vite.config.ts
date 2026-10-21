import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// ผู้ใช้จริงเปิดหน้าเว็บผ่าน dev server นี้ (Cloudflare Tunnel) — ถ้าเปิด HMR ไว้ ทุกครั้งที่แก้ไฟล์ใน src/
// หรือ WebSocket หลุด (Vite client จะ poll แล้วสั่ง location.reload()) ทุก browser จะรีโหลดเองและข้อมูลที่กรอกหาย
// จึงปิด HMR/WebSocket เป็นค่าเริ่มต้น — ตอนพัฒนาให้เปิดด้วย PowerShell `$env:QA_VITE_HMR='true'; npm.cmd run dev`
const hmrEnabled = process.env.QA_VITE_HMR === 'true'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    host: '0.0.0.0',
    port: 5173,
    strictPort: true,
    // Allow these hostnames when accessing the dev server through a proxy/Tunnel
    allowedHosts: [
      'promaxx2.qahub.store',
      'qahub.store',
    ],
    ...(hmrEnabled
      ? {
          hmr: {
            protocol: 'wss',
            host: 'promaxx2.qahub.store',
          },
        }
      : { hmr: false, ws: false as const }),
    // Proxy API calls to the backend to avoid CORS during development.
    // Change target if your API runs on a different host/port.
    proxy: {
      '/api': {
        target: 'https://api-promaxx2.qahub.store',
        changeOrigin: true,
        secure: false,
        ws: true,
      },
    },
  },
})
