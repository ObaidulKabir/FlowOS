import React, { useEffect, useMemo, useState } from 'react';
import {
  AlertTriangle,
  Bot,
  Check,
  Code2,
  Copy,
  ExternalLink,
  Eye,
  EyeOff,
  KeyRound,
  Monitor
} from 'lucide-react';
import { mcpRpcUrl } from '../mcpUrl';

type McpClient = 'cursor' | 'claude-code' | 'claude-desktop';

interface Props {
  tenantId: string;
  tenantName: string;
  sessionApiKey?: string;
  onOpenApiKeys: () => void;
}

const API_KEY_PLACEHOLDER = '<FLOWOS_TENANT_API_KEY>';
const MASKED_COPY_VALUE = '<CURRENT_KEY_INCLUDED_WHEN_COPIED>';

const clientDetails: Record<McpClient, { label: string; destination: string }> = {
  cursor: {
    label: 'Cursor',
    destination: 'Save as .cursor/mcp.json in a project or ~/.cursor/mcp.json globally.'
  },
  'claude-code': {
    label: 'Claude Code',
    destination: 'Save as .mcp.json in the project root, then restart or reconnect MCP.'
  },
  'claude-desktop': {
    label: 'Claude Desktop',
    destination: 'Add to claude_desktop_config.json. This bridge requires Node.js and mcp-remote.'
  }
};

const createConfiguration = (
  client: McpClient,
  endpoint: string,
  tenantId: string,
  apiKey: string
) => {
  if (client === 'claude-desktop') {
    return {
      mcpServers: {
        flowos: {
          command: 'npx',
          args: [
            '-y',
            'mcp-remote',
            endpoint,
            '--header',
            'x-tenant-id:${FLOWOS_TENANT_ID}',
            '--header',
            'X-MCP-API-Key:${FLOWOS_API_KEY}'
          ],
          env: {
            FLOWOS_TENANT_ID: tenantId,
            FLOWOS_API_KEY: apiKey
          }
        }
      }
    };
  }

  return {
    mcpServers: {
      flowos: {
        ...(client === 'claude-code' ? { type: 'http' } : {}),
        url: endpoint,
        headers: {
          'x-tenant-id': tenantId,
          'X-MCP-API-Key': apiKey
        }
      }
    }
  };
};

const writeClipboard = async (text: string) => {
  try {
    await navigator.clipboard.writeText(text);
    return;
  } catch {
    const textarea = document.createElement('textarea');
    textarea.value = text;
    textarea.setAttribute('readonly', '');
    textarea.style.position = 'fixed';
    textarea.style.opacity = '0';
    document.body.appendChild(textarea);
    textarea.select();
    const copied = document.execCommand('copy');
    document.body.removeChild(textarea);
    if (!copied)
      throw new Error('Clipboard copy failed');
  }
};

export const TenantMcpConfiguration: React.FC<Props> = ({
  tenantId,
  tenantName,
  sessionApiKey,
  onOpenApiKeys
}) => {
  const [client, setClient] = useState<McpClient>('cursor');
  const [apiKey, setApiKey] = useState('');
  const [showApiKey, setShowApiKey] = useState(false);
  const [copied, setCopied] = useState(false);
  const [copyError, setCopyError] = useState<string | null>(null);
  const endpoint = mcpRpcUrl();

  useEffect(() => {
    setApiKey('');
    setShowApiKey(false);
    setCopied(false);
    setCopyError(null);
  }, [tenantId]);

  const copyValue = apiKey.trim() || API_KEY_PLACEHOLDER;
  const previewValue = apiKey.trim() ? MASKED_COPY_VALUE : API_KEY_PLACEHOLDER;

  const configuration = useMemo(
    () => JSON.stringify(createConfiguration(client, endpoint, tenantId, copyValue), null, 2),
    [client, endpoint, tenantId, copyValue]
  );
  const preview = useMemo(
    () => JSON.stringify(createConfiguration(client, endpoint, tenantId, previewValue), null, 2),
    [client, endpoint, tenantId, previewValue]
  );

  const copyConfiguration = async () => {
    setCopyError(null);
    try {
      await writeClipboard(configuration);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      setCopyError('Clipboard access was denied. Select the configuration and copy it manually.');
    }
  };

  return (
    <div className="space-y-5">
      <div className="rounded-2xl border border-cyan-500/25 bg-gradient-to-r from-cyan-950/30 via-slate-900 to-slate-900 p-5">
        <div className="flex flex-col lg:flex-row lg:items-center justify-between gap-4">
          <div>
            <div className="flex items-center gap-2">
              <span className="rounded-xl border border-cyan-500/30 bg-cyan-500/10 p-2 text-cyan-300">
                <Bot size={18} />
              </span>
              <div>
                <h3 className="text-lg font-bold text-white">Connect Cursor or Claude to FlowOS</h3>
                <p className="text-xs text-slate-400">
                  Tenant-scoped configuration for <strong className="text-slate-200">{tenantName}</strong>.
                </p>
              </div>
            </div>
          </div>
          <a
            href={endpoint}
            target="_blank"
            rel="noreferrer"
            className="inline-flex items-center justify-center gap-1.5 rounded-xl border border-cyan-500/30 bg-cyan-500/10 px-3 py-2 text-xs font-semibold text-cyan-300 hover:bg-cyan-500/20"
          >
            Open MCP discovery <ExternalLink size={12} />
          </a>
        </div>
      </div>

      <div className="grid gap-3 md:grid-cols-2">
        <div className="rounded-xl border border-slate-700 bg-slate-900/70 p-3">
          <div className="text-[10px] font-bold uppercase tracking-wider text-slate-500">MCP endpoint</div>
          <code className="mt-1 block break-all text-xs text-cyan-300">{endpoint}</code>
        </div>
        <div className="rounded-xl border border-slate-700 bg-slate-900/70 p-3">
          <div className="text-[10px] font-bold uppercase tracking-wider text-slate-500">Tenant ID</div>
          <code className="mt-1 block break-all text-xs text-blue-300">{tenantId}</code>
        </div>
      </div>

      <div className="rounded-2xl border border-slate-700 bg-slate-900/70 p-4 space-y-3">
        <div className="flex flex-col md:flex-row md:items-center justify-between gap-2">
          <div>
            <div className="flex items-center gap-1.5 text-xs font-bold text-slate-200">
              <KeyRound size={14} className="text-amber-400" />
              Tenant API key
            </div>
            <p className="mt-1 text-[11px] text-slate-500">
              Paste a one-time full key. Masked key identifiers cannot authenticate.
            </p>
          </div>
          <div className="flex flex-wrap gap-2">
            {sessionApiKey && (
              <button
                type="button"
                onClick={() => setApiKey(sessionApiKey)}
                className="rounded-lg border border-emerald-500/30 bg-emerald-500/10 px-2.5 py-1.5 text-[10px] font-semibold text-emerald-300 hover:bg-emerald-500/20"
              >
                Use current session key
              </button>
            )}
            <button
              type="button"
              onClick={onOpenApiKeys}
              className="rounded-lg border border-amber-500/30 bg-amber-500/10 px-2.5 py-1.5 text-[10px] font-semibold text-amber-300 hover:bg-amber-500/20"
            >
              Generate an API key
            </button>
          </div>
        </div>
        <div className="relative">
          <input
            type={showApiKey ? 'text' : 'password'}
            value={apiKey}
            onChange={event => setApiKey(event.target.value)}
            placeholder={API_KEY_PLACEHOLDER}
            autoComplete="off"
            spellCheck={false}
            className="w-full rounded-xl border border-slate-700 bg-slate-950 px-3 py-2.5 pr-11 font-mono text-xs text-slate-200 outline-none focus:border-cyan-500"
          />
          <button
            type="button"
            onClick={() => setShowApiKey(value => !value)}
            className="absolute right-3 top-1/2 -translate-y-1/2 text-slate-500 hover:text-white"
            aria-label={showApiKey ? 'Hide API key' : 'Show API key'}
          >
            {showApiKey ? <EyeOff size={15} /> : <Eye size={15} />}
          </button>
        </div>
        <p className="text-[10px] text-slate-500">
          {apiKey.trim()
            ? 'The preview masks the key; the copied configuration includes the entered value.'
            : 'No key entered: the copied configuration will contain a replacement placeholder.'}
        </p>
      </div>

      <div className="rounded-2xl border border-slate-700 bg-slate-900/70 overflow-hidden">
        <div className="flex flex-wrap border-b border-slate-700 bg-slate-950/60 p-2 gap-2">
          {([
            ['cursor', 'Cursor', Code2],
            ['claude-code', 'Claude Code', Bot],
            ['claude-desktop', 'Claude Desktop', Monitor]
          ] as const).map(([id, label, Icon]) => (
            <button
              key={id}
              type="button"
              onClick={() => setClient(id)}
              className={`inline-flex items-center gap-1.5 rounded-lg px-3 py-2 text-xs font-semibold transition-colors ${
                client === id
                  ? 'bg-cyan-600 text-white'
                  : 'text-slate-400 hover:bg-slate-800 hover:text-white'
              }`}
            >
              <Icon size={13} />
              {label}
            </button>
          ))}
        </div>

        <div className="p-4">
          <div className="mb-3 flex flex-col sm:flex-row sm:items-center justify-between gap-2">
            <div>
              <div className="text-xs font-bold text-white">{clientDetails[client].label} configuration</div>
              <div className="text-[10px] text-slate-500">{clientDetails[client].destination}</div>
            </div>
            <button
              type="button"
              onClick={copyConfiguration}
              className="inline-flex items-center justify-center gap-1.5 rounded-lg bg-cyan-600 px-3 py-2 text-xs font-bold text-white hover:bg-cyan-500"
            >
              {copied ? <Check size={14} /> : <Copy size={14} />}
              {copied ? 'Copied' : 'Copy configuration'}
            </button>
          </div>

          <pre className="max-h-[430px] overflow-auto rounded-xl border border-slate-800 bg-slate-950 p-4 font-mono text-[11px] leading-relaxed text-emerald-300 select-all">
            {preview}
          </pre>

          {client === 'claude-desktop' && (
            <div className="mt-3 flex items-start gap-2 rounded-xl border border-amber-500/20 bg-amber-500/10 p-3 text-[11px] text-amber-200">
              <AlertTriangle size={14} className="mt-0.5 shrink-0" />
              Claude Desktop&apos;s file config runs a local <code>mcp-remote</code> bridge. Claude Code and Cursor connect directly over HTTP.
            </div>
          )}
          {copyError && <p className="mt-2 text-[11px] text-rose-300">{copyError}</p>}
        </div>
      </div>
    </div>
  );
};
