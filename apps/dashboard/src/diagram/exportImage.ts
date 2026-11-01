/**
 * Turns a rendered diagram SVG into a file the user can keep: a self-contained SVG, or a PNG.
 * The export shows the diagram as the user arranged it, on a solid background, with the role
 * colour key underneath (the key is page chrome, not part of Mermaid's SVG).
 */

export interface LegendEntry { role: string; stroke: string }

export interface ExportOptions {
  background?: string;
  legend?: LegendEntry[];
  /** Blank space around the drawing, in SVG units. */
  padding?: number;
}

export interface ExportedSvg {
  markup: string;
  width: number;
  height: number;
}

const SVG_NS = 'http://www.w3.org/2000/svg';
const DEFAULT_BACKGROUND = '#0f172a';
const LEGEND_ROW = 22;

/** The area the drawing really covers: dragged nodes can leave Mermaid's own viewBox. */
const contentBox = (svg: SVGSVGElement): { x: number; y: number; width: number; height: number } | null => {
  try {
    const box = svg.getBBox();
    if (box.width > 0 && box.height > 0) return { x: box.x, y: box.y, width: box.width, height: box.height };
  } catch {
    // No layout (jsdom, detached SVG): fall back to the viewBox.
  }
  const view = svg.viewBox?.baseVal;
  if (view && view.width > 0 && view.height > 0) return { x: view.x, y: view.y, width: view.width, height: view.height };
  const [vx, vy, vw, vh] = (svg.getAttribute('viewBox') ?? '').split(/[\s,]+/).map(Number);
  if (vw > 0 && vh > 0) return { x: vx, y: vy, width: vw, height: vh };
  return null;
};

export function serializeDiagramSvg(svg: SVGSVGElement, options: ExportOptions = {}): ExportedSvg | null {
  const box = contentBox(svg);
  if (!box) return null;

  const padding = options.padding ?? 24;
  const legend = (options.legend ?? []).filter(entry => entry.role);
  const legendHeight = legend.length > 0 ? legend.length * LEGEND_ROW + padding : 0;

  const x = box.x - padding;
  const y = box.y - padding;
  const width = Math.ceil(box.width + padding * 2);
  const drawingHeight = Math.ceil(box.height + padding * 2);
  const height = drawingHeight + legendHeight;

  const clone = svg.cloneNode(true) as SVGSVGElement;
  clone.setAttribute('xmlns', SVG_NS);
  clone.setAttribute('xmlns:xlink', 'http://www.w3.org/1999/xlink');
  clone.setAttribute('viewBox', `${x} ${y} ${width} ${height}`);
  clone.setAttribute('width', String(width));
  clone.setAttribute('height', String(height));
  clone.removeAttribute('style');
  clone.querySelectorAll('[data-draggable-node]').forEach(node => {
    node.removeAttribute('data-draggable-node');
    (node as SVGElement).style.removeProperty('cursor');
    (node as SVGElement).style.removeProperty('touch-action');
  });

  const background = document.createElementNS(SVG_NS, 'rect');
  background.setAttribute('x', String(x));
  background.setAttribute('y', String(y));
  background.setAttribute('width', String(width));
  background.setAttribute('height', String(height));
  background.setAttribute('fill', options.background ?? DEFAULT_BACKGROUND);
  clone.insertBefore(background, clone.firstChild);

  if (legend.length > 0) {
    const group = document.createElementNS(SVG_NS, 'g');
    legend.forEach((entry, index) => {
      const rowY = y + drawingHeight + index * LEGEND_ROW;
      const line = document.createElementNS(SVG_NS, 'line');
      line.setAttribute('x1', String(x + padding));
      line.setAttribute('x2', String(x + padding + 28));
      line.setAttribute('y1', String(rowY));
      line.setAttribute('y2', String(rowY));
      line.setAttribute('stroke', entry.stroke);
      line.setAttribute('stroke-width', '3');
      const text = document.createElementNS(SVG_NS, 'text');
      text.setAttribute('x', String(x + padding + 38));
      text.setAttribute('y', String(rowY + 4));
      text.setAttribute('fill', '#e2e8f0');
      text.setAttribute('font-size', '13');
      text.setAttribute('font-family', 'ui-monospace, SFMono-Regular, Menlo, Consolas, monospace');
      text.textContent = entry.role;
      group.append(line, text);
    });
    clone.appendChild(group);
  }

  const markup = new XMLSerializer().serializeToString(clone);
  return { markup: markup.includes('<?xml') ? markup : `<?xml version="1.0" encoding="UTF-8"?>\n${markup}`, width, height };
}

const toDataUrl = (markup: string) => `data:image/svg+xml;charset=utf-8,${encodeURIComponent(markup)}`;

/** Longest side of a PNG, in pixels. Browsers refuse canvases much larger than this. */
const MAX_PNG_SIDE = 8192;

export async function svgToPngBlob(exported: ExportedSvg, scale = 2): Promise<Blob> {
  const factor = Math.max(0.25, Math.min(scale, MAX_PNG_SIDE / Math.max(exported.width, exported.height)));
  const image = new Image();
  image.decoding = 'async';
  await new Promise<void>((resolve, reject) => {
    image.onload = () => resolve();
    image.onerror = () => reject(new Error('The diagram could not be turned into an image.'));
    image.src = toDataUrl(exported.markup);
  });

  const canvas = document.createElement('canvas');
  canvas.width = Math.round(exported.width * factor);
  canvas.height = Math.round(exported.height * factor);
  const context = canvas.getContext('2d');
  if (!context) throw new Error('Image export is not supported in this browser.');
  context.drawImage(image, 0, 0, canvas.width, canvas.height);

  return new Promise<Blob>((resolve, reject) => {
    // A browser that taints the canvas (some drop foreignObject labels) throws or returns null here.
    try {
      canvas.toBlob(blob => (blob ? resolve(blob) : reject(new Error('The browser blocked the image export. Download the SVG instead.'))), 'image/png');
    } catch {
      reject(new Error('The browser blocked the image export. Download the SVG instead.'));
    }
  });
}

export const diagramFileName = (name: string, extension: 'png' | 'svg') => {
  const slug = name.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '') || 'diagram';
  return `${slug}.${extension}`;
};

export function saveBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

export type DiagramImageFormat = 'png' | 'svg';

/** Exports the diagram in `svg`'s container. Resolves with the file name that was saved. */
export async function downloadDiagram(
  svg: SVGSVGElement,
  name: string,
  format: DiagramImageFormat,
  options: ExportOptions = {}
): Promise<string> {
  const exported = serializeDiagramSvg(svg, options);
  if (!exported) throw new Error('There is no diagram to export yet.');
  const fileName = diagramFileName(name, format);
  const blob = format === 'svg'
    ? new Blob([exported.markup], { type: 'image/svg+xml;charset=utf-8' })
    : await svgToPngBlob(exported);
  saveBlob(blob, fileName);
  return fileName;
}

