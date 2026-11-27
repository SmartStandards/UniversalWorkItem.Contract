import { WorkItem, getAreaRoot } from "./model";

export type Dimension = "none" | "areaRoot" | "area" | "assignedTo" | "origin";

export type GroupingMode = "crossColumn" | "withinColumn";

/**
 * The (not yet persisted) view of a board. It will be persisted via IBoardManagementService (backlog I3).
 */
export interface ViewState {
  secondLevel: Dimension;
  groupBy: Dimension;
  groupingMode: GroupingMode;
  collapsedColumns: Set<string>;
  collapsedGroups: Set<string>;
}

export const DimensionLabels: Record<Dimension, string> = {
  none: "(none)",
  areaRoot: "Area root (source)",
  area: "Area (full path)",
  assignedTo: "Assigned to",
  origin: "Origin"
};

export const GroupingModeLabels: Record<GroupingMode, string> = {
  crossColumn: "Cross-column rows",
  withinColumn: "Within column"
};

export function createDefaultViewState(): ViewState {
  return {
    secondLevel: "none",
    groupBy: "none",
    groupingMode: "crossColumn",
    collapsedColumns: new Set<string>(),
    collapsedGroups: new Set<string>()
  };
}

export function getDimensionValue(card: WorkItem, dimension: Dimension): string {
  switch (dimension) {
    case "areaRoot":
      return getAreaRoot(card.concernArea);
    case "area":
      return card.concernArea || "/";
    case "assignedTo":
      return card.assignedTo || "(unassigned)";
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
  return Array.from(values).sort((a, b) => a.localeCompare(b));
}

export function toggle(set: Set<string>, key: string): void {
  if (set.has(key)) {
    set.delete(key);
  } else {
    set.add(key);
  }
}
