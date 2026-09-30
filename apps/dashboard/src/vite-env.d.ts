/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_MCP_URL?: string;
  readonly VITE_FLOWOS_BUILD?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
