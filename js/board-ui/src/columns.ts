import { WorkItem } from "./model";
import {
  Dimension,
  ViewState,
  getAllStateValues,
  getDimensionValue,
  getDistinctValues,
  isAreaWithin,
  normalizeArea
} from "./viewState";

// Builds the (multi-level) column tree of the board. Rendering only - no transport code here.

/**
 * What a column (or group) stands for. Dropping a card there applies all assignments of the target.
 * 'subtree' (area only): the card is fine anywhere below 'value'; otherwise the area is set to exactly 'value'.
 */
export interface Assignment {
  dimension: Dimension;
  value: string;
  subtree: boolean;
}

export interface ColumnNode {
  key: string;
  label: string;
  /** Labels of all levels (for tooltips and collapsed columns). */
  path: string;
  dimension: Dimension;
  depth: number;
  cards: WorkItem[];
  assignments: Assignment[];
  /** Visible child columns (empty = leaf column). */
  children: ColumnNode[];
  collapsed: boolean;
  /** null = no sub-column split possible, otherwise the current state of the merge/split toggle. */
  merged: boolean | null;
  /** null = no sub-areas available, otherwise whether the sub-areas are shown. */
  areaExpanded: boolean | null;
}

interface LevelContext {
  dimensions: Dimension[];
  view: ViewState;
}

function createNode(
  context: LevelContext,
  dimensionIndex: number,
  parent: ColumnNode | null,
  key: string,
  label: string,
  assignment: Assignment,
  cards: WorkItem[],
  depth: number,
  buildSubAreas: ((self: ColumnNode) => ColumnNode[]) | null
): ColumnNode {
  const view = context.view;
  const assignments: Assignment[] = [];
  let path = label;

  if (parent !== null) {
    assignments.push(...parent.assignments);
    path = parent.path + " / " + label;
  }
  assignments.push(assignment);

  const node: ColumnNode = {
    key: key,
    label: label,
    path: path,
    dimension: assignment.dimension,
    depth: depth,
    cards: cards,
    assignments: assignments,
    children: [],
    collapsed: view.collapsedColumns.has(key),
    merged: null,
    areaExpanded: null
  };

  if (node.collapsed) {
    return node;
  }

  if (buildSubAreas !== null) {
    node.areaExpanded = view.expandedAreas.has(key);
    if (node.areaExpanded) {
      node.children = buildSubAreas(node);
      return node;
    }
  }

  const nextIndex = dimensionIndex + 1;
  if (nextIndex < context.dimensions.length && cards.length > 0) {
    const nextDimension = context.dimensions[nextIndex];
    // deep area trees would explode the board - so area sub-columns start merged
    const splitByDefault = nextDimension !== "area";
    const toggled = view.toggledSplits.has(key);
    const split = splitByDefault !== toggled;

    node.merged = !split;
    if (split) {
      node.children = buildLevel(context, nextIndex, node, cards, depth + 1);
    }
  }

  return node;
}

function buildAreaLevel(
  context: LevelContext,
  dimensionIndex: number,
  parent: ColumnNode | null,
  cards: WorkItem[],
  parentPath: string,
  depth: number
): ColumnNode[] {
  const keyPrefix = parent === null ? "" : parent.key + "|";
  const direct: WorkItem[] = [];
  const childCards = new Map<string, WorkItem[]>();

  for (const card of cards) {
    const area = normalizeArea(card.concernArea);
    if (area === parentPath) {
      direct.push(card);
      continue;
    }
    const rest = parentPath === "/" ? area.substring(1) : area.substring(parentPath.length + 1);
    const segment = rest.split("/")[0];
    const childPath = (parentPath === "/" ? "" : parentPath) + "/" + segment;
    let list = childCards.get(childPath);
    if (list === undefined) {
      list = [];
      childCards.set(childPath, list);
    }
    list.push(card);
  }

  const result: ColumnNode[] = [];

  if (direct.length > 0) {
    const label = parentPath === "/" ? "/" : "(direct)";
    result.push(createNode(
      context, dimensionIndex, parent, keyPrefix + "area=" + parentPath + "!", label,
      { dimension: "area", value: parentPath, subtree: false }, direct, depth, null
    ));
  }

  for (const childPath of Array.from(childCards.keys()).sort((a, b) => a.localeCompare(b))) {
    const nodeCards = childCards.get(childPath) as WorkItem[];
    const hasSubAreas = nodeCards.some((card) => normalizeArea(card.concernArea) !== childPath);
    const label = parentPath === "/" ? childPath : childPath.substring(childPath.lastIndexOf("/") + 1);
    const key = keyPrefix + "area=" + childPath;
    let buildSubAreas: ((self: ColumnNode) => ColumnNode[]) | null = null;

    if (hasSubAreas) {
      buildSubAreas = (self) => buildAreaLevel(context, dimensionIndex, self, nodeCards, childPath, depth + 1);
    }

    result.push(createNode(
      context, dimensionIndex, parent, key, label,
      { dimension: "area", value: childPath, subtree: true }, nodeCards, depth, buildSubAreas
    ));
  }

  return result;
}

function buildLevel(context: LevelContext, dimensionIndex: number, parent: ColumnNode | null, cards: WorkItem[], depth: number): ColumnNode[] {
  const dimension = context.dimensions[dimensionIndex];

  if (dimension === "area") {
    return buildAreaLevel(context, dimensionIndex, parent, cards, "/", depth);
  }

  let values: string[];
  if (dimension === "status" && dimensionIndex === 0) {
    values = getAllStateValues(cards);
  } else {
    values = getDistinctValues(cards, dimension);
  }

  const keyPrefix = parent === null ? "" : parent.key + "|";

  return values.map((value) => createNode(
    context, dimensionIndex, parent, keyPrefix + dimension + "=" + value, value,
    { dimension: dimension, value: value, subtree: false },
    cards.filter((card) => getDimensionValue(card, dimension) === value),
    depth, null
  ));
}

/**
 * Builds the column tree for the column dimensions of the view (first level + optional second level).
 */
export function buildColumnTree(cards: WorkItem[], view: ViewState): ColumnNode[] {
  const dimensions: Dimension[] = [view.columns];
  if (view.secondLevel !== "none" && view.secondLevel !== view.columns) {
    dimensions.push(view.secondLevel);
  }
  return buildLevel({ dimensions: dimensions, view: view }, 0, null, cards, 0);
}

export function collectLeaves(nodes: ColumnNode[], result: ColumnNode[]): ColumnNode[] {
  for (const node of nodes) {
    if (node.children.length === 0) {
      result.push(node);
    } else {
      collectLeaves(node.children, result);
    }
  }
  return result;
}

export function countLeaves(node: ColumnNode): number {
  if (node.children.length === 0) {
    return 1;
  }
  let count = 0;
  for (const child of node.children) {
    count += countLeaves(child);
  }
  return count;
}

export function getMaxDepth(nodes: ColumnNode[]): number {
  let max = 0;
  for (const node of nodes) {
    let depth = node.depth + 1;
    if (node.children.length > 0) {
      depth = getMaxDepth(node.children);
    }
    if (depth > max) {
      max = depth;
    }
  }
  return max;
}

/**
 * Assignment of a group value (groups are flat, so an area group stands for exactly that area).
 */
export function createGroupAssignment(dimension: Dimension, value: string): Assignment {
  return { dimension: dimension, value: value, subtree: false };
}

export { isAreaWithin };
