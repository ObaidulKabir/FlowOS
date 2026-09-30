import { execSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

const gitCommitCount = () => {
  try {
    const count = execSync("git rev-list --count HEAD", {
      encoding: "utf8",
      stdio: ["ignore", "pipe", "ignore"],
    }).trim();
    return /^\d+$/.test(count) && count !== "0" ? count : "";
  } catch {
    return "";
  }
};

if (!process.env.VITE_FLOWOS_BUILD) {
  process.env.VITE_FLOWOS_BUILD = process.env.FLOWOS_BUILD || gitCommitCount();
}

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

const liveBuild = () => {
  const raw = Number(process.env.VITE_FLOWOS_BUILD);
  return Number.isInteger(raw) && raw > 0 ? raw : 2;
};

const serveLiveDiscovery = (
  req: { url?: string },
  res: { setHeader: (name: string, value: string) => void; end: (body: string) => void },
  next: () => void
) => {
  const pathOnly = (req.url ?? "").split("?")[0];
  if (pathOnly !== "/.well-known/mcp" && pathOnly !== "/.well-known/mcp.json") {
    next();
    return;
  }

  const file = path.resolve(process.cwd(), "public", ".well-known", "mcp.json");
  const manifest = JSON.parse(fs.readFileSync(file, "utf8")) as {
    version?: string;
    flowOsVersion?: string;
  };
  const [major, minor] = String(manifest.flowOsVersion ?? manifest.version ?? "1.2.2").split(".");
  const live = `${major}.${minor}.${liveBuild()}`;
  manifest.version = live;
  manifest.flowOsVersion = live;
  res.setHeader("Content-Type", "application/json; charset=utf-8");
  res.setHeader("Cache-Control", "no-store");
  res.end(JSON.stringify(manifest));
};

const legalPageRoutes = () => ({
  name: "flowos-legal-routes",
  configureServer(server: { middlewares: { use: (fn: (req: never, res: never, next: never) => void) => void } }) {
    server.middlewares.use(serveLiveDiscovery);
    server.middlewares.use(rewriteLegalPage);
  },
  configurePreviewServer(server: { middlewares: { use: (fn: (req: never, res: never, next: never) => void) => void } }) {
    server.middlewares.use(serveLiveDiscovery);
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
