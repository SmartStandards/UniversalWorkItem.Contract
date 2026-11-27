import { invokeUjmw } from "./ujmwClient";
import { BoardDescriptor, WorkItem } from "./model";

const managementServiceUrl = "boardApi/management/";
const configurationServiceUrl = "boardApi/configuration/";

/**
 * Connector configuration as exchanged with IConnectorConfigurationService (the access token is write-only).
 */
export interface ConnectorConfiguration {
  boardId: string;
  title: string;
  connectorType: string;
  owner: string;
  repositoryName: string;
  accessToken: string;
  hasAccessToken: boolean;
  removeAccessToken: boolean;
  isReadOnly: boolean;
  maxIssues: number;
}

export async function getConnectorConfigurations(): Promise<ConnectorConfiguration[]> {
  const configurations = await invokeUjmw<ConnectorConfiguration[] | null>(configurationServiceUrl, "GetConnectorConfigurations", {});
  return configurations || [];
}

/**
 * Returns an empty string on success, otherwise the error message.
 */
export async function testConnectorConfiguration(configuration: ConnectorConfiguration): Promise<string> {
  const message = await invokeUjmw<string | null>(configurationServiceUrl, "TestConnectorConfiguration", { configuration: configuration });
  return message || "";
}

/**
 * Returns the board id of the saved configuration.
 */
export async function saveConnectorConfiguration(configuration: ConnectorConfiguration): Promise<string> {
  const boardId = await invokeUjmw<string | null>(configurationServiceUrl, "SaveConnectorConfiguration", { configuration: configuration });
  return boardId || "";
}

export async function deleteConnectorConfiguration(boardId: string): Promise<boolean> {
  const deleted = await invokeUjmw<boolean | null>(configurationServiceUrl, "DeleteConnectorConfiguration", { boardId: boardId });
  return deleted === true;
}

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
