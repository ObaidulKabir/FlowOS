import React, { ReactNode, useCallback, useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import { Maximize2, Minimize2 } from 'lucide-react';

interface Props {
  /** Content to render inside the shell (the simulator). */
  children: ReactNode;
  /**
   * Label shown in the fullscreen title bar.
   * @default "Sandbox Simulator"
   */
  label?: string;
  /** Extra classes on the normal (non-fullscreen) container. */
  className?: string;
}

/**
 * Wraps any simulator with a fullscreen toggle button.
 *
 * Usage:
 *   <FullscreenSimulatorShell label="Sandbox Simulator">
 *     <DraftSimulator definition={def} />
 *   </FullscreenSimulatorShell>
 *
 * The ⤢ button appears in the top-right corner of the wrapper.
 * Pressing Escape also exits fullscreen.
 */
export const FullscreenSimulatorShell: React.FC<Props> = ({
  children,
  label = 'Sandbox Simulator',
  className = '',
}) => {
  const [isFullscreen, setIsFullscreen] = useState(false);
  const toggle = useCallback(() => setIsFullscreen(f => !f), []);

  // Lock body scroll while fullscreen
  useEffect(() => {
    if (!isFullscreen) return;
    const prev = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => { document.body.style.overflow = prev; };
  }, [isFullscreen]);

  // Escape exits fullscreen
  useEffect(() => {
    if (!isFullscreen) return;
    const handler = (e: KeyboardEvent) => { if (e.key === 'Escape') setIsFullscreen(false); };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [isFullscreen]);

  const toggleBtn = (
    <button
      type="button"
      onClick={toggle}
      title={isFullscreen ? 'Exit fullscreen (Esc)' : 'Enter fullscreen'}
      className="absolute top-2 right-2 z-10 p-1.5 rounded-lg bg-slate-900/80 border border-slate-700 text-slate-400 hover:text-white hover:border-emerald-500 transition-all backdrop-blur-sm"
    >
      {isFullscreen ? <Minimize2 size={14} /> : <Maximize2 size={14} />}
    </button>
  );

  if (isFullscreen) {
    return (
      <>
        {/* Invisible placeholder keeps layout stable */}
        <div className={`relative ${className} opacity-0 pointer-events-none select-none`} aria-hidden="true">
          {children}
        </div>
        {createPortal(
          <div className="fixed inset-0 z-[9999] bg-slate-950 flex flex-col overflow-hidden">
            {/* Fullscreen title bar */}
            <div className="flex items-center justify-between px-4 py-2 border-b border-slate-800 bg-slate-900 shrink-0">
              <span className="text-sm font-semibold text-emerald-300">{label}</span>
              <div className="flex items-center gap-2 text-xs text-slate-500">
                <span>Press Esc to exit</span>
                <button
                  type="button"
                  onClick={toggle}
                  className="p-1.5 rounded-lg border border-slate-700 text-slate-400 hover:text-white hover:border-emerald-500 transition-all"
                >
                  <Minimize2 size={14} />
                </button>
              </div>
            </div>
            {/* Simulator fills remaining height */}
            <div className="flex-1 overflow-hidden">
              {children}
            </div>
          </div>,
          document.body
        )}
      </>
    );
  }

  return (
    <div className={`relative ${className}`}>
      {toggleBtn}
      {children}
    </div>
  );
};
