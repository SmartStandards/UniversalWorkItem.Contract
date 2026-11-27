# Epic: Universal Board

## 1. Abstract and Motivation

Work items live in many systems: Azure DevOps, GitHub Issues, Trello, Outlook tasks and others. Every system has its own board, its own state model and its own idea of priority. Teams that work across several of these systems have no single place where they can see **all** relevant cards in **one global order**, drill into area trees and move cards without switching tools.

The `UniversalWorkItem.Contract` already provides the common language for this: a universal `WorkItem` with well-known semantics (`ProgressState` root states, `ConcernArea` paths, `ManualReorderedPriority`, `Origin` identity) and extensible `AdditionalFields`. Every connector exposes this model through the FUSE repository contract (`IWorkItemRepository`).

This epic describes the **Universal Board**: an application that composes such repositories through a thin, decorator-based middle layer and shows them as a dynamic, collapsible "Trello-like" board.

The epic also preserves the original architecture sketch (an older drawing with `CardAccessAPI`, scope filters, `BoardAccessAPI` and display filters) and maps its ideas to today's standard concepts (FUSE repositories, UJMW, decorators).

## 2. Positive Differentiation

| Aspect | Typical tool boards | Universal Board |
| --- | --- | --- |
| Data sources | One system | Any number of `IWorkItemRepository` providers, mixed systems |
| Priority | Per column / per system | One **global** order (`ManualReorderedPriority`) across all sources |
| Columns | Fixed list of states | Freely combinable column levels (Area, Origin, State, Person, ...) with collapsible area trees |
| Grouping | Swimlanes only | Vertical grouping either across all columns (aligned rows) or only within a column (compact) |
| Composition | Closed | Every layer is again a repository, so instances can be chained (remote instance as a data source) |
| Transport | Proprietary | UJMW only, identical for desktop (WebView2) and web host |

## 3. Core Idea (distilled from the original sketch)

The original sketch contained these building blocks. The table shows how they map to today's concepts.

| Original idea | Today's concept | Notes |
| --- | --- | --- |
| Individual data sources | Connector repositories (`AzureDevOpsWorkItemRepository`, `GitHubIssuesWorkItemRepository`, ...) | One instance per concrete store (e.g. one per GitHub repository). |
| Standardized data source as a "cube" | Composite repository over many sources | Routing by `Origin`. |
| Routing via `Origin` field | Source routing decorator | Reads fan out, writes route by `Origin`; new items are routed by the root area segment in the MVP (D14). |
| Field mapping (e.g. TAGS <-> Status) | `FieldMappingRepository` decorator | Bidirectional mapping, e.g. tag `doing` <-> `ProgressState = Working`. |
| Optional cache wrapper | **Generic FUSE caching repository wrapper** | Mandatory for remote sources, see section 7. |
| Replication cache + sync worker (one-way pull / two-way sync) | Later evolution of the caching wrapper | Not part of the MVP. |
| Scope filter (non-UI, mandatory) per board | `MandatoryScopeRepository` decorator | AND across fields; per field `IN` lists and ranges. |
| `CardAccessAPI` (authorization per origin) | Authorization decorator / FUSE mandatory access control | Related to the FUSE epic "mandatory access control". |
| `BoardAccessAPI` (`IsTransitionPossible`, `GetTransitionConstraint`, `ScopeIncludedValues`, `OutOfTransitionsTo`) | Transition interceptor + board management contract | See section 6. |
| Display filter (UI) | View configuration (client-side filter state) | Persisted via the board management contract. |
| Board view (UI) | Board frontend | See skill `universal-board-frontend-development`. |
| ConfigUI / ConfigurationAPI | Board management contract | Board definitions, views, filter state. |
| "A board can be a data source again - but it is a facade, the underlying origins are lost" | Remote coupling via UJMW | Confirmed as a black box with its own connection origin (D12). |

### 3.1 Behavioral rules from the sketch

- Between different fields there is **only AND** (no OR).
- For value lists a field may use `IN`; for numbers and dates, allowed **ranges**.
- These rules apply independently of which fields are used for columns or swimlanes.
- **On read:** plain filtering on these fields. UI controls hide values that are not allowed (locked), or mark them with a "move out" arrow icon.
- **On update:** every change of a relevant field passes an **interceptor**. It can:
	- reduce the displayed set (filtering),
	- regulate **transitions** (provide allowed values for a state dropdown, min/max for a date),
	- **veto** an invalid transition,
	- report that the card is going to **leave the board**.
- If a scope-filtered field is used as a column or swimlane, out-of-scope values are hidden. If a transition to such a value is allowed, a narrow column labeled `<value> (leave board)` is shown as a drop target.

## 4. Layer Model

Every layer below the UI is again an `IWorkItemRepository` (FUSE `IRepository<WorkItem, WorkItemIdentity>`). This is the central design principle: a repository can be used

- **a) as a pure data provider** - shown 1:1 by the UI, or
- **b) as a man-in-the-middle** - wrapping another repository with remapping, mandatory filters or interceptors, and exposing a repository again.

```
 +---------------------------------------------------------------+
 |  Board UI (TypeScript, WebView2 or browser)                   |
 |    - data access: FUSE repository of WorkItem via UJMW        |
 |    - board management: IBoardManagementService via UJMW       |
 +---------------------------------------------------------------+
								 | UJMW (async / long-running)
 +---------------------------------------------------------------+
 |  Board layer (one UI-near repository per board)               |
 |    ReadOnly? -> TransitionInterceptor -> MandatoryScope       |
 +---------------------------------------------------------------+
 |  Middle layer (decorators, all IWorkItemRepository)           |
 |    FieldMapping -> OriginRouting (composite) -> Caching       |
 +---------------------------------------------------------------+
 |  Connectors (one instance per concrete store)                 |
 |    AzureDevOps | GitHubIssues x N | Remote (UJMW client) | ...|
 +---------------------------------------------------------------+
```

### 4.1 Remote coupling

An instance can mount the **UI-near repository of a remote instance** as a data source of its own, through a UJMW client repository. There may even be UJMW queue proxies in between, without the consumer knowing it. Consequences:

- Latency and availability of remote sources are unknown, so the **caching wrapper is required** in front of every remote source.
- The remote board is a **black-box facade** (D12): its mandatory scope, mappings and source composition are already applied - it may itself be an emulation composed of many sources. The consuming instance therefore assigns a **new origin per connection** (named after the proxy, e.g. `remote:<connection-name>`). Cards are created only "in A"; routing to the concrete provider happens inside A according to A's own rules.

## 5. Contracts

Decided:

- **All data access** goes through the FUSE repository of `WorkItem` (`IWorkItemRepository`), exposed via UJMW. There is no proprietary card API.
- **Board management, configuration and filter-state persistence** go through a separate interface, working title `IBoardManagementService`, exposed via UJMW.

Sketch of `IBoardManagementService` (to be refined in iteration 1):

| Method (sketch) | Purpose |
| --- | --- |
| `GetBoards() : BoardDescriptor[]` | Lists the boards the caller may see (id, title, read-only flag, capabilities). |
| `GetViews(boardId) : ViewDefinition[]` | Lists the stored views of a board. |
| `SaveView(boardId, view) : string` | Persists a view (column levels, vertical order, grouping mode, display filters, collapsed nodes). |
| `DeleteView(boardId, viewId) : bool` | Removes a view. |
| `GetAreaTree(boardId) : AreaNode[]` | Delivers the area tree for column building: derived dynamically from the `ConcernArea` values of the cards (D6), merged with pinned areas from the board settings. |
| `GetFieldConstraints(boardId, fieldName) : FieldConstraint` | Delivers scope-included values, allowed transitions and ranges (successor of `ScopeIncludedValues` / `OutOfTransitionsTo`). |
| `IsTransitionPossible(boardId, key, fieldName, targetValue) : bool` | Pre-check for drag and drop (successor of the `BoardAccessAPI` sketch). |

Addressing (D10): every board gets its own endpoint via the **last route segment**, using UJMW contextualization with a URL placeholder, e.g. `options.ControllerRoute = "boards/v1/[Controller].svc/{boardId}"` (exact form to be fixed in I1) plus `options.BindContextualArgumentToRouteSegment("BoardId", "boardId")`. The `ContextualizationHook` resolves the composed repository of that board; the business contract (`IWorkItemRepository`) stays free of a board parameter.

### 5.1 Board persistence (D7, D8)

- Board setups, views, pinned areas and per-user state are stored with **EF Core** behind a **FUSE repository layer** (typed DataStore over clean, FUSE-attributed persistence POCOs), following the skill `smart-persistence-entityframework-fusefx` (AI-SKILLS-SMART).
- The entity model is maintained in the FUSE ModelDesigner (fuse schema JSON next to the generated code).
- Authentication is a later iteration, but the model is scoped from the start:
	- board setups (board definition, provider configuration, shared views, pinned areas) carry a `TenantId`;
	- board-usage state (last view, collapsed nodes, personal display filters) carries `TenantId` + `UserId`.
	- Until authentication exists, a fixed default tenant and default user are used.
- `IBoardManagementService` is implemented on top of this DataStore; it never exposes the persistence entities directly.

## 6. Board UI Concept

Short summary; the frontend rules live in the skill `universal-board-frontend-development`.

- **Column levels** are combinable per view, e.g. `Area`, `Origin x Area`, `State x Origin x Area`, `Origin x State`, `State x Area`, `Person`.
- **Area columns are collapsible trees.** Each area node is a column with a header; it can be collapsed into a thin vertical strip. A node can show a "direct" sub-column for cards that sit exactly on that node. Expanding and collapsing means zooming in and out of the area tree.
- **Global priority:** inside every column, cards are ordered by `ManualReorderedPriority` (descending). All columns start at the same vertical position. Reordering uses the algorithm documented on `WorkItem.ManualReorderedPriority` (midpoint between neighbors, Snowflake44 for top, 0 for bottom, collision redistribution).
- **Vertical order / grouping** (e.g. by Person, State, Due date, PrioUid, Severity, root area):
	- **Cross-column mode** (indicator: continuous horizontal line): groups are aligned across all columns, so a group starts on the same vertical position everywhere.
	- **Within-column mode** (space-saving): groups are only separated inside each column.
	- Groups are collapsible (`Collapsed (n)` marker).
- **A view** stores the display configuration **plus** the base data set (which origins, tag filters, ...).

## 7. Caching Wrapper (required)

Decided: the caching wrapper is created **directly in FUSE-fx** (repository `FUSE-fx.RepositoryContract`) as a generic `IRepository<TEntity, TKey>` decorator. It is not specific to work items.

Why it is required:

- Remote sources (other instances, possibly behind UJMW queue proxies) have unknown latency.
- GitHub has strict API rate limits (search 30 requests/min, REST 5000 requests/h per token); 10 providers on one board multiply the load.
- The board needs fast re-rendering for zooming and regrouping without new round trips.

Requirements for the FUSE wrapper (draft, to be aligned in FUSE-fx):

| # | Requirement |
| --- | --- |
| C1 | Generic decorator over any `IRepository<TEntity, TKey>`; exposes the same contract. |
| C2 | Key-based entity cache plus query result cache (by normalized filter, sort, limit, skip). |
| C3 | Write-through: writes go to the inner repository first; on success the cache is updated or invalidated. |
| C4 | Configurable expiry (time to live); optional delta refresh by a "modified since" field. |
| C5 | Explicit invalidation API (whole cache, single key). |
| C6 | Thread safe; no cross-caller leakage of results (security-relevant together with mandatory filters: place the cache **below** caller-specific filters). |
| C7 | Later: background refresh / replication (successor of the "sync worker" in the original sketch). |

Open point: invalidation strategy defaults (Q12) - deferred (D11): this is FUSE-fx development work and will be decided after a first visible UI. The first iterations run **without** a cache.

## 8. Connector Decisions

### 8.1 GitHub Issues

- **One provider instance per GitHub repository** in the contract project (`GitHubIssuesWorkItemRepository`).
- **Organization-level convenience** (enter credentials once, list repositories, select them) is an **application-level** feature. Internally it still creates one connector instance per repository.
- **Priority via label** (first version). Proposal: a label with a fixed prefix, e.g. `prio:<value>`.
	- Risk: every distinct numeric value creates a repository label. With Snowflake44 values each card gets its own label, which pollutes the label list.
	- Decided against (D13): GitHub Projects (v2) number fields. Reasons: GraphQL only, extra token scope, issue must be added to a project, one owner per project.
	- **Candidate: dedicated, always-edited marker comment** per issue (e.g. a comment whose body is only `<!-- uwi:prio=1234567890 -->`, optionally minimized via GraphQL `minimizeComment`). Known facts and risks:
		- Creating the comment once **does** notify subscribers (a new comment); this happens once per issue on first reorder.
		To be verified later on a real but uncritical repository (no separate test program), including the GitHub mobile app and email settings.
		- Every edit is kept in the visible edit history of the comment ("edited" dropdown); an `issue_comment` webhook with action `edited` fires on each edit (relevant if workflows react to comments).
		- Only the comment author or users with write/triage permission can edit it; with several PATs (different users) all of them need write access.
		- Content-creating/-editing requests count against GitHub's secondary rate limits; mass reordering must be throttled. Whether comment edits bump the issue's `updated_at` (relevant for delta refresh) is to be verified.
		- Reading needs one extra comments request per issue unless GraphQL is used to fetch issues and comments in one query.
	- **Alternative: hidden marker in the issue body** (no extra comment, no notification on edit) - but risk of lost updates when a human edits the body concurrently and a visible "edited" marker on the issue description.
	- **Alternative: overlay store** in the board persistence (D7) keyed by `WorkItemIdentity` and board - works for every provider (also read-only ones), but the order is invisible in GitHub itself.
	- Decision pending (Q13).
- Proposal for the remaining mapping (to be confirmed in iteration 2):

| WorkItem field | GitHub source |
| --- | --- |
| `Origin` | `github:<owner>/<repo>` |
| `OriginRelatedId` | Issue number |
| `Class` | `GitHub.Issue` |
| `StorageArea` | `/` (the repository is the store) |
| `ConcernArea` | Label with prefix `area:` (path segments separated by `/`), default `/` |
| `ProgressState` | `open` -> `New`, `closed` (completed) -> `Done`, `closed` (not planned) -> `Discontinued`; label prefix `state:` refines sub-states, e.g. `state:Working.Implementation` |
| `AssignedTo` | First assignee |
| `Tags` | All labels without the reserved prefixes |
| `Title` / `BodyContent` | Title / body (`text/markdown`) |
| Timestamps / authors | `created_at`, `updated_at`, `closed_at`, `user` |
| `AdditionalFields` | Milestone, further assignees, reactions etc. |

### 8.2 Azure DevOps

Already reworked: `DateTime?` timestamps, process and custom fields mirrored into `AdditionalFields`, `ConcernArea` from `System.AreaPath`. For board 1, the tag filter is applied by the mandatory scope decorator and pushed down into WIQL where possible.

## 9. MVP

| Board | Sources | Scope | Write |
| --- | --- | --- | --- |
| **Board 1 - DevOps PBIs** | 1 Azure DevOps provider (PAT) | `Class = AzureDevOps.Product Backlog Item` AND tag = configured tag | Read-only |
| **Board 2 - GitHub** | 10 GitHub Issues providers (one per repository, PAT) | All cards | Write-back and create; `Origin` / area decides where a new card is created |

Launch targets:

1. **Desktop:** WinForms EXE with WebView2 and in-memory ASP.NET Core engine (skill `asp-engine-webview-coupling`).
2. **Web host:** ASP.NET Core (net10.0) service hosting the same UI and services.

Both hosts share the same UI library (`AddBoardUi` / `UseBoardUi`). The UI talks **only** via UJMW, asynchronously (long-running operations for slow loads).

### 9.1 Planned projects (working names)

| Project | Content |
| --- | --- |
| `UniversalWorkItem.Boards` | Middle layer decorators (area-based source routing, mandatory scope, field mapping, transition interceptor, read-only), board composition. |
| `UniversalWorkItem.Boards.Ui` | `IBoardManagementService` contract and implementation, UJMW registration, embedded UI bundle, hosting extensions. |
| `UWIboard.Desktop` | App level: WinForms + WebView2 shell with in-memory ASP.NET Core engine (launch target 1). |
| `UWIboard.AspNetCore` | App level: ASP.NET Core service / web host (launch target 2). |
| `js/board-ui` | TypeScript UI, built into the embedded folder of `UniversalWorkItem.Boards.Ui`. |

### 9.2 Iterations

| # | Goal | Done when |
| --- | --- | --- |
| I1 | Skeleton and transport | Both launch targets start; an in-memory demo repository is shown as a flat board by `ProgressState` via UJMW (async) on a board-specific route. `IBoardManagementService` v0 lists boards from the EF/FUSE board store (default tenant / user). |
| I2 | GitHub read + composition | `GitHubIssuesWorkItemRepository` reads issues; the routing layer composes 10 instances with the MVP rule "root area = repository" (D14, no cache yet). Board 2 shows all cards read-only. |
| I3 | Board engine | Combinable column levels, dynamic collapsible area tree with pinned areas, global priority order, both vertical grouping modes, views persisted via `IBoardManagementService`. |
| I4 | Board 2 write | Drag and drop (state / area / priority), card editing, create card with origin/area-based routing. |
| I5 | Board 1 | Azure DevOps provider behind `MandatoryScopeRepository` (PBI + tag) and `ReadOnlyRepository`. |
| I6 | Hardening | PAT / secret storage, long-running loads with progress and cancellation, error display per provider, app icon; decide and integrate the FUSE caching wrapper (Q12). |

### 9.3 Post-MVP

- Transition interceptor with constraints, veto and "leave board" columns.
- Field mapping decorator (tags <-> state etc.).
- Remote coupling (UJMW client repository) and queue proxies.
- Replication cache / two-way sync.
- Authorization per origin (`CardAccessAPI` idea) and per board, aligned with the FUSE mandatory access control epic.
- Organization-level GitHub convenience (credentials once, repository listing).
- Further connectors (Trello, Outlook).

## 10. Decisions

| # | Decision |
| --- | --- |
| D1 | The caching wrapper is created directly in FUSE-fx as a generic repository decorator (timing: see D11). |
| D2 | All data access goes through the FUSE repository of `WorkItem`; board management, configuration and filter-state persistence use a separate interface (`IBoardManagementService`). |
| D3 | GitHub priority as label was the first idea; replaced by Q13 because labels do not scale for Snowflake44 values. |
| D4 | The 10 GitHub providers are repositories. One connector instance per repository; organization-level setup is an application-level convenience. |
| D5 | The adapter / decorator concept stays in this epic for now (Q10). |
| D6 | Areas are derived maximally dynamically from the cards' `ConcernArea` values (may be cached in between). Board settings may **pin** areas so that a column does not disappear when it is empty (Q5). |
| D7 | Board persistence: EF Core with a FUSE repository layer over an internal, cleanly attributed POCO entity model (Q6). |
| D8 | Authentication comes in a later iteration, but board setups are tenant-scoped and board-usage state is user-scoped in the persistence model from the start (Q7). |
| D9 | Middle layer targets .NET 10 only; UI is TypeScript (Q9). |
| D10 | Board-specific UJMW route: last route segment, bound via UJMW contextualization (URL placeholder) (Q11). |
| D11 | Caching is deferred: no cache until a first rough UI is visible; the strategy is FUSE-fx development work (Q12). |
| D12 | Remote coupling: a mounted remote board is a black box. The consumer assigns a new origin per connection (named after the proxy); cards are created only in the remote board, which routes internally by its own rules (Q8). |
| D13 | GitHub Projects (v2) fields are not used for priority. |
| D14 | Source routing (Q14) is refined after the MVP, together with authentication and tenancy. For the MVP the routing layer exists as a decorator but is hard-coded on the `ConcernArea`: the **root area segment represents the addressed source repository 1:1** (e.g. `/<repo>/<area-from-labels>`). Reads prepend the root segment, creates are routed by it, and changing the root segment of an existing card is rejected (no card moves in the MVP). |

## 11. Open Questions

| # | Question |
| --- | --- |
| Q13 | GitHub priority storage: marker comment (always edited), hidden marker in the issue body, or overlay store in the board persistence? See section 8.1. |
| Q14 | (Post-MVP, see D14) **Dynamic routing when aggregating several sources** (needs its own discussion):
