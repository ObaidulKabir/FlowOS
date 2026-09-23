import { useEffect, useState } from 'react';
import { mcpRpcPath } from './mcpUrl';

export const VERIFIED_PLATFORM_METRICS = {
  mcpTools: 79,
  tests: {
    total: 648,
    unit: 556,
    endToEnd: 30,
    mcp: 62
  },
  verifiedOn: '2026-09-23'
} as const;

let discoveredMcpToolCount: number | undefined;
let discoveryRequest: Promise<number> | undefined;

const discoverMcpToolCount = async (): Promise<number> => {
  if (discoveredMcpToolCount !== undefined) return discoveredMcpToolCount;

  discoveryRequest ??= fetch(mcpRpcPath(), {
    headers: { Accept: 'application/json' }
  })
    .then(async response => {
      if (!response.ok) throw new Error(`MCP discovery returned HTTP ${response.status}.`);
      const discovery = await response.json();
      const count = Number(discovery.toolsCount);
      if (!Number.isInteger(count) || count < 1) {
        throw new Error('MCP discovery did not return a valid toolsCount.');
      }
      discoveredMcpToolCount = count;
      return count;
    })
    .catch(error => {
      discoveryRequest = undefined;
      throw error;
    });

  return discoveryRequest;
};

export const usePlatformMetrics = () => {
  const [mcpTools, setMcpTools] = useState(
    discoveredMcpToolCount ?? VERIFIED_PLATFORM_METRICS.mcpTools
  );
  const [isLiveMcpCount, setIsLiveMcpCount] = useState(discoveredMcpToolCount !== undefined);

  useEffect(() => {
    let active = true;
    discoverMcpToolCount()
      .then(count => {
        if (!active) return;
        setMcpTools(count);
        setIsLiveMcpCount(true);
      })
      .catch(() => {
        if (!active) return;
        setMcpTools(VERIFIED_PLATFORM_METRICS.mcpTools);
        setIsLiveMcpCount(false);
      });

    return () => {
      active = false;
    };
  }, []);

  return {
    mcpTools,
    isLiveMcpCount,
    tests: VERIFIED_PLATFORM_METRICS.tests,
    verifiedOn: VERIFIED_PLATFORM_METRICS.verifiedOn
  };
};
