import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

const apiProxy = {
  "/api": {
    target: process.env.VITE_API_TARGET || "http://localhost:5183",
    changeOrigin: true,
    secure: false,
  },
};

export default defineConfig({
  plugins: [react()],
  server: {
    allowedHosts: true, // Allow any host (localhost, flowos.prospectbdltd.com, flowos.gkibria121.com, etc.)
    port: 5173,
    host: true, // Needed for Docker
    proxy: apiProxy,
  },
  preview: {
    port: 4173,
    host: true,
    proxy: apiProxy,
  },
});
