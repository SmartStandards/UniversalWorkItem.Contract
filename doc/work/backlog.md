# Backlog

## Universal Board MVP ([epic](../epics/epic-universal-board.md))

| Status | Iteration | Item |
| --- | --- | --- |
| open | - | Answer open question Q13 of the epic (Q14 deferred to post-MVP, MVP rule D14). |
| open | I4 | Verify GitHub marker comment for priority on a real but uncritical repository: no notifications on edit, effect on `updated_at`, edit history, rate limits. |
| open | I1 | Create app projects `UWIboard.Desktop` (WinForms + WebView2 + in-memory ASP.NET Core) and `UWIboard.AspNetCore` (ASP.NET Core service), plus libraries `UniversalWorkItem.Boards` / `.Boards.Ui`; net10.0 only. |
| open | I1 | Model board persistence in the FUSE ModelDesigner (boards, provider configs, views, pinned areas, user state; `TenantId` / `UserId` scoping) and generate entities, DbContext and typed DataStore (skill `smart-persistence-entityframework-fusefx`). |
| open | I1 | Define `IBoardManagementService` v0 (boards, views) on top of the board DataStore; default tenant / user until authentication exists. |
| open | I1 | Expose the FUSE `WorkItem` repository per board via UJMW contextualization (board id as route placeholder in the last segment). |
| open | I1 | TypeScript UI skeleton (`js/board-ui`, IIFE bundle embedded in `.Boards.Ui`), flat board by `ProgressState`, async UJMW calls. |
| open | I2 | Implement `GitHubIssuesWorkItemRepository` (read) incl. mapping proposal of epic section 8.1. |
| open | I2 | Implement the source routing decorator (composite over N repositories), MVP rule: root area segment = repository (D14). |
| open | I3 | Board engine: column levels, dynamic collapsible area tree incl. pinned areas, global priority, vertical grouping (cross-column / within-column). |
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
