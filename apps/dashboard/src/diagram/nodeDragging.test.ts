import { beforeEach, describe, expect, test, vi } from 'vitest';
import {
  boundaryPoint,
  edgeGeometry,
  enableNodeDragging,
  logicalNodeId,
  parseTranslate,
  splitEdgeId
} from './nodeDragging';
import { diagramLayoutKey } from '../components/WorkflowGraphVisualizer';

vi.mock('mermaid', () => ({ default: { initialize: vi.fn(), render: vi.fn() } }));

describe('nodeDragging helpers', () => {
  test('reads a translate and the chart id behind Mermaid\'s render prefix', () => {
    expect(parseTranslate('translate(147.5, 35)')).toEqual({ x: 147.5, y: 35 });
    expect(parseTranslate('translate(-3,4)')).toEqual({ x: -3, y: 4 });
    expect(parseTranslate(null)).toBeNull();
    expect(logicalNodeId('m1-flowchart-Start-0')).toBe('Start');
    expect(logicalNodeId('flowchart-Manager_Review-12')).toBe('Manager_Review');
    expect(logicalNodeId('something-else')).toBe('something-else');
  });

  test('splits an edge id even when node ids contain underscores', () => {
    const known = new Set(['Start', 'Manager_Review', 'Pay']);
    expect(splitEdgeId('L_Start_Manager_Review_0', known)).toEqual({ from: 'Start', to: 'Manager_Review' });
    expect(splitEdgeId('m1-L_Manager_Review_Pay_1', known)).toEqual({ from: 'Manager_Review', to: 'Pay' });
    expect(splitEdgeId('L_Start_Nowhere_0', known)).toBeNull();
    expect(splitEdgeId('not-an-edge', known)).toBeNull();
  });

  test('an edge leaves a box at its border, not its centre', () => {
    expect(boundaryPoint({ x: 0, y: 0 }, { w: 50, h: 20 }, { x: 200, y: 0 })).toEqual({ x: 50, y: 0 });
    expect(boundaryPoint({ x: 0, y: 0 }, { w: 50, h: 20 }, { x: 0, y: -100 })).toEqual({ x: 0, y: -20 });
    expect(boundaryPoint({ x: 5, y: 5 }, { w: 50, h: 20 }, { x: 5, y: 5 })).toEqual({ x: 5, y: 5 });
  });

  test('opposite edges bow to opposite sides', () => {
    const forward = edgeGeometry({ x: 0, y: 0 }, { x: 100, y: 0 }, 40);
    const backward = edgeGeometry({ x: 100, y: 0 }, { x: 0, y: 0 }, 40);
    expect(forward.mid.y).toBeCloseTo(20);
    expect(backward.mid.y).toBeCloseTo(-20);
    expect(edgeGeometry({ x: 0, y: 0 }, { x: 100, y: 0 }).mid).toEqual({ x: 50, y: 0 });
  });

  test('layout keys depend on the set of ids, not their order', () => {
    expect(diagramLayoutKey('flowchart', ['a', 'b'])).toBe(diagramLayoutKey('flowchart', ['b', 'a']));
    expect(diagramLayoutKey('flowchart', ['a', 'b'])).not.toBe(diagramLayoutKey('flowchart', ['a', 'c']));
    expect(diagramLayoutKey('flowchart', ['a'])).not.toBe(diagramLayoutKey('statemachine', ['a']));
  });
});

const SVG = `
<svg xmlns="http://www.w3.org/2000/svg">
  <g class="edgePaths">
    <path id="m1-L_A_B_0" data-id="L_A_B_0" class="flowchart-link" d="M10,10L10,90" marker-end="url(#x)"></path>
    <path id="m1-L_B_C_0" data-id="L_B_C_0" class="flowchart-link" d="M10,110L10,190"></path>
  </g>
  <g class="edgeLabels">
    <g class="edgeLabel" transform="translate(10, 50)"><g class="label" data-id="L_A_B_0"></g></g>
    <g class="edgeLabel" transform="translate(10, 150)"><g class="label" data-id="L_B_C_0"></g></g>
  </g>
  <g class="nodes">
    <g class="node" id="m1-flowchart-A-0" transform="translate(10, 0)"><rect/></g>
    <g class="node" id="m1-flowchart-B-1" transform="translate(10, 100)"><rect/></g>
    <g class="node" id="m1-flowchart-C-2" transform="translate(10, 200)"><rect/></g>
  </g>
</svg>`;

const mountSvg = () => {
  document.body.innerHTML = `<div id="host">${SVG}</div>`;
  return document.querySelector('svg') as SVGSVGElement;
};
const node = (id: string) => document.querySelector(`[data-draggable-node="${id}"]`) as SVGGElement;
const drag = (el: Element, dx: number, dy: number) => {
  el.dispatchEvent(new MouseEvent('pointerdown', { bubbles: true, cancelable: true, clientX: 100, clientY: 100, button: 0 }));
  window.dispatchEvent(new MouseEvent('pointermove', { clientX: 100 + dx, clientY: 100 + dy }));
  window.dispatchEvent(new MouseEvent('pointerup', { clientX: 100 + dx, clientY: 100 + dy }));
};

describe('enableNodeDragging', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  test('moving a node redraws only its own edges and labels, and reset restores Mermaid\'s layout', () => {
    const onChange = vi.fn();
    const dragging = enableNodeDragging(mountSvg(), { onChange });

    drag(node('B'), 60, 20);

    expect(node('B').getAttribute('transform')).toBe('translate(70, 120)');
    expect(node('A').getAttribute('transform')).toBe('translate(10, 0)');
    const [ab, bc] = Array.from(document.querySelectorAll('g.edgePaths > path'));
    expect(ab.getAttribute('d')).not.toBe('M10,10L10,90');
    expect(bc.getAttribute('d')).not.toBe('M10,110L10,190');
    expect(document.querySelectorAll('g.edgeLabel')[0].getAttribute('transform')).not.toBe('translate(10, 50)');
    expect(dragging.movedCount()).toBe(1);
    expect(onChange).toHaveBeenLastCalledWith(1);

    dragging.reset();

    expect(node('B').getAttribute('transform')).toBe('translate(10, 100)');
    expect(ab.getAttribute('d')).toBe('M10,10L10,90');
    expect(bc.getAttribute('d')).toBe('M10,110L10,190');
    expect(document.querySelectorAll('g.edgeLabel')[0].getAttribute('transform')).toBe('translate(10, 50)');
    expect(onChange).toHaveBeenLastCalledWith(0);
  });

  test('a movement of a few pixels is a click, not a drag', () => {
    enableNodeDragging(mountSvg());
    drag(node('B'), 1, 1);
    expect(node('B').getAttribute('transform')).toBe('translate(10, 100)');
  });

  test('a node drag does not reach the surrounding pan handler', () => {
    const svg = mountSvg();
    enableNodeDragging(svg);
    const outer = vi.fn();
    document.getElementById('host')!.addEventListener('pointerdown', outer);

    node('B').dispatchEvent(new MouseEvent('pointerdown', { bubbles: true, cancelable: true, button: 0 }));

    expect(outer).not.toHaveBeenCalled();
  });

  test('the arrangement is remembered and applied to the next render', () => {
    const first = enableNodeDragging(mountSvg(), { storageKey: 'k' });
    drag(node('C'), 40, -30);
    first.dispose();
    expect(JSON.parse(localStorage.getItem('k')!)).toEqual({ C: { x: 40, y: -30 } });

    const second = enableNodeDragging(mountSvg(), { storageKey: 'k' });

    expect(node('C').getAttribute('transform')).toBe('translate(50, 170)');
    expect(second.movedCount()).toBe(1);

    second.reset();
    expect(localStorage.getItem('k')).toBeNull();
  });

  test('ignores remembered positions for nodes that no longer exist or are malformed', () => {
    localStorage.setItem('k', JSON.stringify({ Gone: { x: 5, y: 5 }, A: { x: 'bad', y: 1 } }));
    const dragging = enableNodeDragging(mountSvg(), { storageKey: 'k' });
    expect(dragging.movedCount()).toBe(0);
    expect(node('A').getAttribute('transform')).toBe('translate(10, 0)');
  });
});
