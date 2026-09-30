import { useEffect, useState } from 'react';

/** Keep in lockstep with FlowOsRelease.Major, Minor, and RecordedBuild. */
export const FLOW_OS_MAJOR = 1;
export const FLOW_OS_MINOR = 2;
export const FLOW_OS_RECORDED_BUILD = 2;
export const FLOW_OS_VERSION_SCHEME = 'Major.Minor.Build';

const envBuild = Number(import.meta.env.VITE_FLOWOS_BUILD);
const build = Number.isInteger(envBuild) && envBuild > 0 ? envBuild : FLOW_OS_RECORDED_BUILD;

/** Major.Minor.Build. The build is the git commit count when the dev server or deploy supplies it. */
export const FLOW_OS_VERSION = `${FLOW_OS_MAJOR}.${FLOW_OS_MINOR}.${build}`;

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
      try {
        const parsed = await read('/health');
        if (parsed && active) setVersion(parsed.text);
      } catch {
        /* the dashboard build stays on screen when the API is down */
      }
    })();

    return () => {
      active = false;
    };
  }, []);

  return version;
};
