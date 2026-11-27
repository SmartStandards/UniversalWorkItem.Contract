export interface BoardSource {
  /** Root segment of 'ConcernArea' including the leading slash, e.g. '/repo-alpha'. */
  areaRoot: string;
  origin: string;
}

/**
 * A DIRECTED transition (explicitly enabled in the backend) which allows moving cards between origins.
 */
export interface OriginTransition {
  sourceOrigin: string;
  targetOrigin: string;
}

export interface BoardDescriptor {
  boardId: string;
  title: string;
  isReadOnly: boolean;
  sources: BoardSource[] | null;
  allowedOriginTransitions: OriginTransition[] | null;
}

/**
 * The (camelCase normalized) wire shape of 'Collaboration.WorkTracking.WorkItem'.
 * Additional fields are flattened onto the root level.
 */
export interface WorkItem {
  origin: string;
  originRelatedId: string;
  title: string;
  concernArea: string;
  progressState: string;
  manualReorderedPriority: number;
  assignedTo: string;
  tags: string[] | null;
  [fieldName: string]: unknown;
}

/**
 * Well-known root states of 'WorkItem.ProgressState' in board order.
 */
export const RootStates: readonly string[] = ["New", "Suspended", "Working", "Done", "Discontinued"];

/**
 * 'Working.Implementation' -> 'Working'
 */
export function getRootState(progressState: string | null | undefined): string {
  if (!progressState) {
    return "New";
  }
  const separatorIndex = progressState.indexOf(".");
  if (separatorIndex < 0) {
    return progressState;
  }
  return progressState.substring(0, separatorIndex);
}

/**
 * 'Working.Implementation' -> 'Implementation' (the UI displays only the sub-state name).
 */
export function getSubStateLabel(progressState: string | null | undefined): string {
  if (!progressState) {
    return "New";
  }
  const separatorIndex = progressState.lastIndexOf(".");
  if (separatorIndex < 0) {
    return progressState;
  }
  return progressState.substring(separatorIndex + 1);
}

/**
 * '/repo-alpha/ui/forms' -> '/repo-alpha' (MVP rule D14: the root segment represents the source repository).
 */
export function getAreaRoot(area: string | null | undefined): string {
  const trimmed = (area || "").replace(/^\/+|\/+$/g, "");
  if (trimmed.length === 0) {
    return "/";
  }
  const separatorIndex = trimmed.indexOf("/");
  if (separatorIndex < 0) {
    return "/" + trimmed;
  }
  return "/" + trimmed.substring(0, separatorIndex);
}
