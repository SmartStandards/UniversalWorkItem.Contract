import { WorkItem, RootStates, getAreaRoot, getRootState } from "./model";

export type Dimension = "none" | "status" | "areaRoot" | "area" | "assignedTo" | "origin";

export type GroupingMode = "crossColumn" | "withinColumn";

/**
 * The (not yet persisted) view of a board. It will be persisted via IBoardManagementService (backlog I3).
 */
export interface ViewState {
  /** Dimension of the first column level. */
  columns: Dimension;
  /** Dimension of the second column level ('none' = no sub-columns). */
  secondLevel: Dimension;
  groupBy: Dimension;
  groupingMode: GroupingMode;
  /** Columns which are narrowed to a slim strip (only showing counts). */
  collapsedColumns: Set<string>;
  /** Columns whose sub-column split deviates from the default (area sub-columns are merged by default, all others split). */
  toggledSplits: Set<string>;
  /** Area columns which are drilled down into their sub-areas (default: not drilled down). */
  expandedAreas: Set<string>;
  collapsedGroups: Set<string>;
}

export const UnassignedLabel = "(unassigned)";

export const DimensionLabels: Record<Dimension, string> = {
  none: "(none)",
  status: "Status",
  areaRoot: "Area root (source)",
  area: "Area (tree)",
  assignedTo: "Assigned to",
  origin: "Origin"
};

/** The first column level always needs a dimension ('none' is not offered). */
export const ColumnDimensionLabels: Record<Exclude<Dimension, "none">, string> = {
  status: "Status",
  areaRoot: "Area root (source)",
  area: "Area (tree)",
  assignedTo: "Assigned to",
  origin: "Origin"
};

export function createDefaultViewState(): ViewState {
  return {
    columns: "status",
    secondLevel: "none",
    groupBy: "none",
    groupingMode: "crossColumn",
    collapsedColumns: new Set<string>(),
    toggledSplits: new Set<string>(),
    expandedAreas: new Set<string>(),
    collapsedGroups: new Set<string>()
  };
}

/**
 * '/a//b/' -> '/a/b', '' -> '/'
 */
export function normalizeArea(area: string | null | undefined): string {
  const segments = (area || "").split("/").filter((segment) => segment.length > 0);
  return "/" + segments.join("/");
}

/**
 * True if 'area' equals 'path' or is located below it.
 */
export function isAreaWithin(area: string | null | undefined, path: string): boolean {
  const normalized = normalizeArea(area);
  if (path === "/") {
    return true;
  }
  return normalized === path || normalized.indexOf(path + "/") === 0;
}

export function getDimensionValue(card: WorkItem, dimension: Dimension): string {
  switch (dimension) {
    case "status":
      return getRootState(card.progressState);
    case "areaRoot":
      return getAreaRoot(card.concernArea);
    case "area":
      return normalizeArea(card.concernArea);
    case "assignedTo":
      return card.assignedTo || UnassignedLabel;
    case "origin":
      return card.origin || "(none)";
    default:
      return "";
  }
}

export function getDistinctValues(cards: WorkItem[], dimension: Dimension): string[] {
  const values = new Set<string>();
  for (const card of cards) {
    values.add(getDimensionValue(card, dimension));
  }
  const result = Array.from(values);
  if (dimension === "status") {
    return result.sort((a, b) => getStateOrder(a) - getStateOrder(b));
  }
  return result.sort((a, b) => a.localeCompare(b));
}

/**
 * All well-known root states (also empty ones, so cards can be dropped there) plus unknown ones.
 */
export function getAllStateValues(cards: WorkItem[]): string[] {
  const states = RootStates.slice();
  for (const value of getDistinctValues(cards, "status")) {
    if (states.indexOf(value) < 0) {
      states.push(value);
    }
  }
  return states;
}

function getStateOrder(state: string): number {
  const index = RootStates.indexOf(state);
  if (index < 0) {
    return RootStates.length;
  }
  return index;
}

export function toggle(set: Set<string>, key: string): void {
  if (set.has(key)) {
    set.delete(key);
  } else {
    set.add(key);
  }
}
