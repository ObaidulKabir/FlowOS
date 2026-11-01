import { afterEach, describe, expect, test, vi } from 'vitest';
import { diagramFileName, downloadDiagram, serializeDiagramSvg, svgToPngBlob } from './exportImage';

const NS = 'http://www.w3.org/2000/svg';

function sampleSvg() {
  const svg = document.createElementNS(NS, 'svg') as SVGSVGElement;
  svg.setAttribute('viewBox', '0 0 200 100');
  svg.setAttribute('style', 'max-width: 200px');
  const node = document.createElementNS(NS, 'g');
  node.setAttribute('data-draggable-node', 'Start');
  node.setAttribute('style', 'cursor: grab');
  svg.appendChild(node);
  return svg;
}

describe('serializeDiagramSvg', () => {
  test('adds a background, drops drag attributes and leaves the live svg untouched', () => {
    const svg = sampleSvg();
    const out = serializeDiagramSvg(svg, { background: '#123456' })!;
    expect(out.markup.startsWith('<?xml')).toBe(true);
    expect(out.markup).toContain('xmlns="http://www.w3.org/2000/svg"');
    expect(out.markup).toContain('#123456');
    expect(out.markup).not.toContain('data-draggable-node');
    expect(out.markup).not.toContain('cursor');
    expect(out.width).toBeGreaterThan(0);
    expect(svg.querySelector('[data-draggable-node]')).not.toBeNull();
  });

  test('appends a legend row per role and grows the image', () => {
    const plain = serializeDiagramSvg(sampleSvg())!;
    const withLegend = serializeDiagramSvg(sampleSvg(), {
      legend: [{ role: 'Employee', stroke: '#38bdf8' }, { role: 'Manager', stroke: '#f472b6' }]
    })!;
    expect(withLegend.markup).toContain('Employee');
    expect(withLegend.markup).toContain('Manager');
    expect(withLegend.height).toBeGreaterThan(plain.height);
  });
});

describe('diagramFileName', () => {
  test('makes a safe file name with the right extension', () => {
    const name = diagramFileName('Workflow Orchestration Steps', 'png');
    expect(name.endsWith('.png')).toBe(true);
    expect(name).not.toMatch(/[\s/\\:]/);
    expect(diagramFileName('State/Machine: v2', 'svg').endsWith('.svg')).toBe(true);
  });
});

describe('failure paths', () => {
  afterEach(() => vi.unstubAllGlobals());

  test('downloadDiagram explains when there is nothing to export', async () => {
    const empty = document.createElementNS(NS, 'svg') as SVGSVGElement;
    await expect(downloadDiagram(empty, 'x', 'png')).rejects.toThrow();
  });

  test('svgToPngBlob rejects instead of hanging when the image cannot load', async () => {
    class BrokenImage {
      onload: (() => void) | null = null;
      onerror: (() => void) | null = null;
      decoding = '';
      set src(_value: string) { queueMicrotask(() => this.onerror?.()); }
    }
    vi.stubGlobal('Image', BrokenImage);
    const exported = serializeDiagramSvg(sampleSvg())!;
    await expect(svgToPngBlob({ ...exported, markup: 'not svg' })).rejects.toThrow();
  });
});
