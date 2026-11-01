/**
 * Lets a user rearrange the nodes of a rendered Mermaid flowchart by dragging them.
 *
 * Mermaid lays the chart out once and returns a static SVG. This enhances that SVG in place:
 * a dragged node moves, and the edges and edge labels attached to it are redrawn to follow.
 * Edges between nodes that were never moved keep Mermaid's own paths. Positions are kept as
 * offsets from Mermaid's layout, so a new layout of the same chart still starts from the same
 * places, and they can be stored per diagram so the arrangement survives a reload.
 */

export interface Point { x: number; y: number }
export interface HalfSize { w: number; h: number }

export interface NodeDraggingOptions {
  /** localStorage key under which the arrangement is saved. Omit to keep it for this render only. */
  storageKey?: string;
  /** Called with the number of nodes that are away from Mermaid's layout. */
  onChange?: (movedCount: number) => void;
}

export interface NodeDragging {
  reset(): void;
  movedCount(): number;
  dispose(): void;
}

const DRAG_THRESHOLD_PX = 3;
/** How far a pair of opposite edges bows apart, so their labels do not sit on each other. */
const PAIR_BEND = 64;

export const parseTranslate = (value: string | null): Point | null => {
  if (!value) return null;
  const match = /translate\(\s*(-?[\d.]+(?:e-?\d+)?)[\s,]+(-?[\d.]+(?:e-?\d+)?)\s*\)/i.exec(value);
  return match ? { x: Number(match[1]), y: Number(match[2]) } : null;
};

/**
 * Mermaid ids look like `flowchart-Start-3`, usually behind the render id (`m1-flowchart-Start-3`).
 * The chart's own id is the middle part.
 */
export const logicalNodeId = (domId: string): string => {
  const match = /^(?:.+?-)?(?:flowchart|state|node)-(.+)-\d+$/.exec(domId);
  return match ? match[1] : domId;
};

/**
 * Splits an edge id such as `L_Review_Pay_0` into its two node ids. Node ids may themselves
 * contain underscores, so only a split where both halves are known nodes is accepted.
 */
export const splitEdgeId = (edgeId: string, knownNodes: ReadonlySet<string>): { from: string; to: string } | null => {
  const match = /(?:^|-)L_(.+)_\d+$/.exec(edgeId);
  if (!match) return null;
  const body = match[1];
  for (let i = body.indexOf('_'); i >= 0; i = body.indexOf('_', i + 1)) {
    const from = body.slice(0, i);
    const to = body.slice(i + 1);
    if (knownNodes.has(from) && knownNodes.has(to)) return { from, to };
  }
  return null;
};

/** Where the line from a box's centre towards `toward` leaves the box. */
export const boundaryPoint = (center: Point, half: HalfSize, toward: Point): Point => {
  const dx = toward.x - center.x;
  const dy = toward.y - center.y;
  if (dx === 0 && dy === 0) return center;
  const scale = Math.min(
    dx === 0 ? Infinity : half.w / Math.abs(dx),
    dy === 0 ? Infinity : half.h / Math.abs(dy)
  );
  return { x: center.x + dx * scale, y: center.y + dy * scale };
};

/** A straight edge, or one that bows sideways by `bend` so A->B and B->A do not overlap. */
export const edgeGeometry = (from: Point, to: Point, bend = 0): { d: string; mid: Point } => {
  const dx = to.x - from.x;
  const dy = to.y - from.y;
  const length = Math.hypot(dx, dy) || 1;
  const control = {
    x: (from.x + to.x) / 2 + (-dy / length) * bend,
    y: (from.y + to.y) / 2 + (dx / length) * bend
  };
  return {
    d: `M${from.x},${from.y} Q${control.x},${control.y} ${to.x},${to.y}`,
    mid: {
      x: 0.25 * from.x + 0.5 * control.x + 0.25 * to.x,
      y: 0.25 * from.y + 0.5 * control.y + 0.25 * to.y
    }
  };
};

interface NodeState {
  el: SVGGElement;
  base: Point;
  offset: Point;
  half: HalfSize;
}

interface EdgeState {
  path: SVGPathElement;
  label: SVGGElement | null;
  from: string;
  to: string;
  originalD: string | null;
  originalLabelTransform: string | null;
}

const readHalfSize = (el: SVGGElement): HalfSize => {
  try {
    const shape = el.querySelector('rect, polygon, circle, ellipse, path') as SVGGraphicsElement | null;
    const box = (shape ?? el).getBBox();
    if (box.width > 0 && box.height > 0) return { w: box.width / 2, h: box.height / 2 };
  } catch {
    // jsdom and detached SVG have no layout.
  }
  return { w: 40, h: 20 };
};

const edgeEnds = (path: Element, knownNodes: ReadonlySet<string>): { from: string; to: string } | null => {
  let from: string | null = null;
  let to: string | null = null;
  path.classList.forEach(name => {
    if (name.startsWith('LS-')) from = name.slice(3);
    if (name.startsWith('LE-')) to = name.slice(3);
  });
  if (from && to) return { from, to };
  return splitEdgeId(path.getAttribute('data-id') || path.id || '', knownNodes);
};

/** The label group that belongs to an edge: matched by edge id, else by position. */
const labelFor = (path: Element, index: number, labels: SVGGElement[]): SVGGElement | null => {
  const edgeId = path.getAttribute('data-id');
  if (edgeId) {
    const match = labels.find(label => label.querySelector('[data-id]')?.getAttribute('data-id') === edgeId);
    if (match) return match;
  }
  return labels[index] ?? null;
};

const loadOffsets = (key?: string): Record<string, Point> => {
  if (!key) return {};
  try {
    const raw = localStorage.getItem(key);
    const parsed = raw ? JSON.parse(raw) : null;
    return parsed && typeof parsed === 'object' ? parsed : {};
  } catch {
    return {};
  }
};

const saveOffsets = (key: string | undefined, offsets: Record<string, Point>) => {
  if (!key) return;
  try {
    if (Object.keys(offsets).length === 0) localStorage.removeItem(key);
    else localStorage.setItem(key, JSON.stringify(offsets));
  } catch {
    // Private mode or quota: the arrangement simply is not remembered.
  }
};

export function enableNodeDragging(svg: SVGSVGElement, options: NodeDraggingOptions = {}): NodeDragging {
  const nodes = new Map<string, NodeState>();
  svg.querySelectorAll<SVGGElement>('g.node').forEach(el => {
    const base = parseTranslate(el.getAttribute('transform'));
    if (!base) return;
    nodes.set(logicalNodeId(el.id), { el, base, offset: { x: 0, y: 0 }, half: readHalfSize(el) });
  });

  const pathElements = Array.from(svg.querySelectorAll<SVGPathElement>('g.edgePaths > path, path.flowchart-link'))
    .filter((path, index, all) => all.indexOf(path) === index);
  const labelElements = Array.from(svg.querySelectorAll<SVGGElement>('g.edgeLabels > g.edgeLabel'));
  const edges: EdgeState[] = [];
  const knownNodes = new Set(nodes.keys());
  pathElements.forEach((path, index) => {
    const ends = edgeEnds(path, knownNodes);
    if (!ends) return;
    const label = labelFor(path, index, labelElements);
    edges.push({
      path,
      label,
      from: ends.from,
      to: ends.to,
      originalD: path.getAttribute('d'),
      originalLabelTransform: label?.getAttribute('transform') ?? null
    });
  });

  const centerOf = (node: NodeState): Point => ({ x: node.base.x + node.offset.x, y: node.base.y + node.offset.y });
  const isMoved = (node: NodeState) => node.offset.x !== 0 || node.offset.y !== 0;
  const movedCount = () => Array.from(nodes.values()).filter(isMoved).length;

  const redrawEdge = (edge: EdgeState) => {
    const from = nodes.get(edge.from);
    const to = nodes.get(edge.to);
    if (!from || !to || from === to) return;

    if (!isMoved(from) && !isMoved(to)) {
      if (edge.originalD) edge.path.setAttribute('d', edge.originalD);
      if (edge.label) {
        if (edge.originalLabelTransform) edge.label.setAttribute('transform', edge.originalLabelTransform);
        else edge.label.removeAttribute('transform');
      }
      return;
    }

    const hasOpposite = edges.some(other => other !== edge && other.from === edge.to && other.to === edge.from);
    const fromCenter = centerOf(from);
    const toCenter = centerOf(to);
    const { d, mid } = edgeGeometry(
      boundaryPoint(fromCenter, from.half, toCenter),
      boundaryPoint(toCenter, to.half, fromCenter),
      hasOpposite ? PAIR_BEND : 0
    );
    edge.path.setAttribute('d', d);
    edge.label?.setAttribute('transform', `translate(${mid.x}, ${mid.y})`);
  };

  const place = (id: string) => {
    const node = nodes.get(id);
    if (!node) return;
    const center = centerOf(node);
    node.el.setAttribute('transform', `translate(${center.x}, ${center.y})`);
    edges.filter(edge => edge.from === id || edge.to === id).forEach(redrawEdge);
  };

  const snapshot = (): Record<string, Point> => {
    const result: Record<string, Point> = {};
    nodes.forEach((node, id) => { if (isMoved(node)) result[id] = { ...node.offset }; });
    return result;
  };

  const notify = () => options.onChange?.(movedCount());

  // Apply a remembered arrangement.
  const saved = loadOffsets(options.storageKey);
  Object.entries(saved).forEach(([id, offset]) => {
    const node = nodes.get(id);
    if (node && Number.isFinite(offset?.x) && Number.isFinite(offset?.y)) {
      node.offset = { x: offset.x, y: offset.y };
      place(id);
    }
  });
  svg.style.overflow = 'visible';
  notify();

  // Convert a pointer movement on screen to SVG units, including viewBox scaling and CSS zoom.
  const unitsPerPixel = (): Point => {
    const matrix = typeof svg.getScreenCTM === 'function' ? svg.getScreenCTM() : null;
    return matrix && matrix.a && matrix.d ? { x: 1 / matrix.a, y: 1 / matrix.d } : { x: 1, y: 1 };
  };

  const cleanups: Array<() => void> = [];

  nodes.forEach((node, id) => {
    node.el.style.cursor = 'grab';
    node.el.style.touchAction = 'none';
    node.el.setAttribute('data-draggable-node', id);

    const onPointerDown = (event: PointerEvent | MouseEvent) => {
      if (event.button !== 0) return;
      // Keep the surrounding pan-the-canvas handler out of a node drag.
      event.stopPropagation();
      event.preventDefault();

      const startX = event.clientX;
      const startY = event.clientY;
      const startOffset = { ...node.offset };
      const scale = unitsPerPixel();
      let dragged = false;
      node.el.style.cursor = 'grabbing';

      const onMove = (move: PointerEvent | MouseEvent) => {
        const movedX = move.clientX - startX;
        const movedY = move.clientY - startY;
        if (!dragged && Math.hypot(movedX, movedY) < DRAG_THRESHOLD_PX) return;
        dragged = true;
        node.offset = { x: startOffset.x + movedX * scale.x, y: startOffset.y + movedY * scale.y };
        place(id);
      };

      const onUp = () => {
        window.removeEventListener('pointermove', onMove);
        window.removeEventListener('pointerup', onUp);
        window.removeEventListener('pointercancel', onUp);
        node.el.style.cursor = 'grab';
        if (dragged) {
          // A drag must not also count as a click on the node.
          const swallowClick = (click: Event) => { click.stopPropagation(); click.preventDefault(); };
          node.el.addEventListener('click', swallowClick, { capture: true, once: true });
          setTimeout(() => node.el.removeEventListener('click', swallowClick, { capture: true }), 0);
          saveOffsets(options.storageKey, snapshot());
          notify();
        }
      };

      window.addEventListener('pointermove', onMove);
      window.addEventListener('pointerup', onUp);
      window.addEventListener('pointercancel', onUp);
    };

    node.el.addEventListener('pointerdown', onPointerDown);
    cleanups.push(() => node.el.removeEventListener('pointerdown', onPointerDown));
  });

  return {
    movedCount,
    reset() {
      nodes.forEach((node, id) => {
        node.offset = { x: 0, y: 0 };
        place(id);
      });
      saveOffsets(options.storageKey, {});
      notify();
    },
    dispose() {
      cleanups.forEach(fn => fn());
    }
  };
}
