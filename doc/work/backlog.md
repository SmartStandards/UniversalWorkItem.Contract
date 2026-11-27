# Backlog

## Universal Board MVP ([epic](../epics/epic-universal-board.md))

| Status | Iteration | Item |
| --- | --- | --- |
| open | - | Answer open question Q13 of the epic (Q14 deferred to post-MVP, MVP rule D14). |
| open | I4 | Verify GitHub marker comment for priority on a real but uncritical repository: no notifications on edit, effect on `updated_at`, edit history, rate limits. |
| done | I1 | Create app projects `UWIboard.Desktop` (WinForms + WebView2 + in-memory ASP.NET Core) and `UWIboard.AspNetCore` (ASP.NET Core service), plus libraries `UniversalWorkItem.Boards` / `.Boards.Ui`; net10.0 only. Demo board via in-memory `BoardRegistry` + FUSE `InMemoryRepository`. |
| open | I1 | Model board persistence in the FUSE ModelDesigner (boards, provider configs, views, pinned areas, user state; `TenantId` / `UserId` scoping) and generate entities, DbContext and typed DataStore (skill `smart-persistence-entityframework-fusefx`). |
| partial | I1 | Define `IBoardManagementService` v0 (boards, views) on top of the board DataStore; default tenant / user until authentication exists. Done: `GetBoards()` on the in-memory `BoardRegistry`; open: views, DataStore backing. |
| done | I1 | Expose the FUSE `WorkItem` repository per board via UJMW contextualization: route `boardApi/workItems/{boardId}`, `ContextualizationHook` -> `BoardWorkItemRepositoryFacade`. |
| done | I1 | TypeScript UI skeleton (`js/board-ui`, Vite IIFE bundle `app.js`/`app.css` built into and embedded by `.Boards.Ui`, committed), async UJMW calls. Build: `cd js/board-ui; npm install; npm run build`. |
| open | I2 | Implement `GitHubIssuesWorkItemRepository` (read) incl. mapping proposal of epic section 8.1. |
| done | I2 | Source routing decorator `AreaRoutingWorkItemRepository` (composite over N repositories), MVP rule D14: root area segment = repository. Demonstrated by board `composed` (3 in-memory mock sources). Not yet supported: `ConcernArea` filters, card moves between sources. |
| partial | I3 | Board engine: columns by root `ProgressState` (sub-state shown on cards), global priority order, optional 2nd column level (area root / area / assignee / origin), collapsible columns, vertical grouping (cross-column / within-column, collapsible with hidden count) are done; open: dynamic collapsible area TREE (n levels) incl. pinned areas. |
| open | I3 | Persist views via `IBoardManagementService`. |
| open | I4 | GitHub write-back (state, area, priority per Q13), create with origin/area routing. |
| open | I4 | UI: drag and drop with priority reordering algorithm, card editor, create dialog. |
| open | I5 | `MandatoryScopeRepository` and `ReadOnlyRepository`; board 1 (Azure DevOps PBIs filtered by tag). |
| open | I6 | PAT / secret storage, long-running loads with progress and cancellation, per-provider error display, app icon. |
| open | I6 | Decide cache strategy (Q12) and integrate the generic FUSE-fx caching wrapper (requirements C1-C6). |

## Next (to be discussed)

| Priority | Item | Notes |
| --- | --- | --- |
| high | Transition interceptor (constraints, veto, "leave board"). | Epic section 3.1. |
| medium | Field mapping decorator (tags <-> state). | |
| medium | Remote coupling via UJMW client repository. | Requires caching wrapper; black box with own connection origin (D12). |
| high | Discuss dynamic routing concept for aggregated sources (create + card moves on field updates). | Epic Q14; after MVP, together with auth and tenancy. |
| medium | Authentication; real tenant / user resolution instead of defaults. | Persistence is already scoped (D8). |
| medium | Organization-level GitHub setup (credentials once, repository listing). | Application-level convenience. |
| low | Replication cache / two-way sync. | Successor of the sync worker idea. |

## Continuous

- Keep `AdditionalFields` handling aligned with the skill `additional-fields-via-smart-extensiondata`.
