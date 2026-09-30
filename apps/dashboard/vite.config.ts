import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

const legalPaths = [
  "/terms",
  "/privacy",
  "/refunds",
  "/cancellation",
  "/acceptable-use",
  "/contact",
  "/product",
  "/pricing",
  "/security",
  "/ai-agents",
  "/company",
];

const rewriteLegalPage = (
  req: { url?: string },
  _res: unknown,
  next: () => void
) => {
  const [path, query] = (req.url ?? "").split("?");
  const bare = path.endsWith("/") && path.length > 1 ? path.slice(0, -1) : path;
  if (legalPaths.includes(bare)) {
    req.url = `${bare}.html${query ? `?${query}` : ""}`;
  }
  next();
};

const legalPageRoutes = () => ({
  name: "flowos-legal-routes",
  configureServer(server: { middlewares: { use: (fn: typeof rewriteLegalPage) => void } }) {
    server.middlewares.use(rewriteLegalPage);
  },
  configurePreviewServer(server: { middlewares: { use: (fn: typeof rewriteLegalPage) => void } }) {
    server.middlewares.use(rewriteLegalPage);
  },
});

const apiProxy = {
  "/health": {
    target: process.env.VITE_API_TARGET || "http://localhost:5183",
    changeOrigin: true,
    secure: false,
  },
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
  plugins: [legalPageRoutes(), react()],
  server: {
    allowedHosts: true, // Allow any host (localhost, flowos.prospectbdltd.com, flowos.gkibria121.com, etc.)
    port: 5173,
    host: true, // Needed for Docker
    headers: {
      "Cache-Control": "no-store, no-cache, must-revalidate",
    },
    proxy: apiProxy,
  },
  preview: {
    port: 4173,
    host: true,
    headers: {
      "Cache-Control": "no-store, no-cache, must-revalidate",
    },
    proxy: apiProxy,
  },
});
