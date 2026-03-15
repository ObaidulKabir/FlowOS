import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    port: 3001,
    allowedHosts: ["localhost", "expense-app.gkibria121.com"], // Allow both localhost and Docker host
  },
});
