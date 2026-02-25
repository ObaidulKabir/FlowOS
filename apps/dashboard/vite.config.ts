import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [react()],

  server: {
    allowedHosts: ['flowos.gkibria121.com'],
    port: 5173,
    host: true, // Needed for Docker
    proxy: {
      '/api': {
        target: process.env.VITE_API_TARGET || 'http://localhost:5183', // Updated default to match current backend
        changeOrigin: true,
      }
    }
  }
})
