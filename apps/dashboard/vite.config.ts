import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

const apiProxy = {
  "/api": {
    target: process.env.VITE_API_TARGET || "http://localhost:5183",
    changeOrigin: true,
    secure: false,
    configure: (proxy) => {
      proxy.on("proxyReq", (proxyReq, req) => {
        for (const name of [
          "authorization",
          "x-api-key",
          "x-tenant-id",
          "x-mock-role",
          "x-mock-userid",
        ]) {
          const value = req.headers[name];
          const header = Array.isArray(value) ? value.find(Boolean) : value;
          if (typeof header === "string" && header) {
            proxyReq.setHeader(name, header);
          }
        }
      });
    },
  },
};

export default defineConfig({
  plugins: [react()],
  server: {
    allowedHosts: true, // Allow any host (localhost, flowos.prospectbdltd.com, flowos.gkibria121.com, etc.)
    port: 5173,
    host: true, // Needed for Docker
    headers: {
      "Cache-Control": "no-store",
    },
    proxy: apiProxy,
  },
  preview: {
    port: 4173,
    host: true,
    headers: {
      "Cache-Control": "no-store",
    },
    proxy: apiProxy,
  },
});
