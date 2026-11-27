import { invokeUjmw } from "./ujmwClient";
import { BoardDescriptor, WorkItem } from "./model";

const managementServiceUrl = "boardApi/management/";

function getWorkItemServiceUrl(boardId: string): string {
  return "boardApi/workItems/" + encodeURIComponent(boardId) + "/";
}

/**
 * IBoardManagementService.GetBoards()
 */
export async function getBoards(): Promise<BoardDescriptor[]> {
  const boards = await invokeUjmw<BoardDescriptor[] | null>(managementServiceUrl, "GetBoards", {});
  return boards || [];
}

/**
 * IWorkItemRepository.GetEntities() of the given board, ordered by the global priority (DESC).
 */
export async function getCards(boardId: string, limit: number = 1000): Promise<WorkItem[]> {
  const cards = await invokeUjmw<WorkItem[] | null>(getWorkItemServiceUrl(boardId), "GetEntities", {
    filter: { matchAll: true, negate: false, predicates: [], subTree: [] },
    sortedBy: ["^ManualReorderedPriority"],
    limit: limit,
    skip: 0
  });
  return cards || [];
}

/**
 * IWorkItemRepository.TryUpdateEntityFields() of the given board.
 * The key fields (Origin, OriginRelatedId) are added automatically; 'changedFields' uses the PascalCase field names.
 * Returns the fields which were actually updated (null, if the card does not exist anymore).
 */
export async function tryUpdateCardFields(
  boardId: string,
  card: WorkItem,
  changedFields: Record<string, unknown>
): Promise<Record<string, unknown> | null> {
  const fields: Record<string, unknown> = {
    Origin: card.origin,
    OriginRelatedId: card.originRelatedId
  };
  for (const [fieldName, value] of Object.entries(changedFields)) {
    fields[fieldName] = value;
  }
  return await invokeUjmw<Record<string, unknown> | null>(getWorkItemServiceUrl(boardId), "TryUpdateEntityFields", {
    fields: fields
  });
}
