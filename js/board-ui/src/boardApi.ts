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
