import { useEffect, useState } from 'react';

/** Keep in lockstep with FlowOsRelease.Version. Tests fail if this drifts. */
export const FLOW_OS_VERSION = '1.2.2';
export const FLOW_OS_VERSION_SCHEME = 'Major.Minor.Build';

export const parseFlowOsVersion = (value: string | undefined | null) => {
  if (!value) return null;
  const match = value.trim().match(/^(\d+)\.(\d+)\.(\d+)$/);
  if (!match) return null;
  return {
    major: Number(match[1]),
    minor: Number(match[2]),
    build: Number(match[3]),
    text: `${match[1]}.${match[2]}.${match[3]}`
  };
};

const readVersionFromJson = (payload: unknown) => {
  if (!payload || typeof payload !== 'object') return null;
  const record = payload as Record<string, unknown>;
  return parseFlowOsVersion(
    typeof record.flowOsVersion === 'string'
      ? record.flowOsVersion
      : typeof record.version === 'string'
        ? record.version
        : null
  );
};

export const useFlowOsVersion = () => {
  const [version, setVersion] = useState(FLOW_OS_VERSION);

  useEffect(() => {
    let active = true;

    const read = async (url: string) => {
      const response = await fetch(url, { headers: { Accept: 'application/json' } });
      if (!response.ok) return null;
      return readVersionFromJson(await response.json());
    };

    (async () => {
      for (const url of ['/health', '/.well-known/mcp.json']) {
        try {
          const parsed = await read(url);
          if (parsed && active) {
            setVersion(parsed.text);
            return;
          }
        } catch {
          /* try the next source */
        }
      }
    })();

    return () => {
      active = false;
    };
  }, []);

  return version;
};
