import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    allowedHosts: true, // Allow any host (localhost, flowos.prospectbdltd.com, flowos.gkibria121.com, etc.)
    port: 5173,
    host: true, // Needed for Docker
    proxy: {
      "/api": {
        target: process.env.VITE_API_TARGET || "http://localhost:5183",
        changeOrigin: true,
        secure: false,
      },
    },
  },
});
