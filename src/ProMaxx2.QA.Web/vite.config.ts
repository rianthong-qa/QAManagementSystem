import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

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
    hmr: {
      protocol: 'wss',
      host: 'promaxx2.qahub.store',
    },
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
