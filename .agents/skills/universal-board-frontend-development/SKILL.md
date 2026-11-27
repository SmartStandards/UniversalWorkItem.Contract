---
name: universal-board-frontend-development
description: Rules for developing the Universal Board application of this repository - the two launch targets (WinForms + WebView2 + in-memory ASP.NET Core, and the ASP.NET Core web host), the UJMW-only async communication between the TypeScript UI and the backend, and the board rendering concepts (combinable column levels, collapsible area trees, global priority, vertical grouping, views). Load when working on the board UI, its hosting projects or the board management contract. The low-level connector / decorator concept is NOT part of this skill; see doc/epics/epic-universal-board.md.
metadata:
	version: "2026-11-25"
---

# Universal Board Frontend Development

## Scope

This skill covers the **application layer**: hosting, UI, and the contracts the UI consumes. It deliberately does not cover connectors (Azure DevOps, GitHub, ...) or middle-layer decorators. For the overall concept, decisions and open questions see `doc/epics/epic-universal-board.md`; for planned steps see `doc/work/backlog.md`.

## Mandatory base skills

- `asp-engine-webview-coupling` (AI-SKILLS-SMART): WinForms + WebView2 + in-memory ASP.NET Core engine, embedded classic-script bundle, dual hosting, UJMW long-running operations.
- `ujmw` (UnifiedJsonMessageWrapper repository): wire protocol and controller registration.
- `asp-net-core-development-guidelines` (AI-SKILLS-SMART): bootstrap hooks for the web host.
- `fuse-fx` (FUSE-fx.RepositoryContract repository): repository contract, `ExpressionTree` filters.

## Launch targets

| Target | Project | Notes |
| --- | --- | --- |
| Desktop | `UniversalWorkItem.Boards.Desktop` | `net10.0-windows`, WinForms, `WebViewAspNetCoreAdapter` embedded via T4, mount path `/`. |
| Web host | `UniversalWorkItem.Boards.WebHost` | `net10.0`, ASP.NET Core; mounts the same UI under a configurable path. |

Both call the same two extension methods from `UniversalWorkItem.Boards.Ui`: `AddBoardUi(services, config)` and `UseBoardUi(app, mountPath)`. Nothing host-specific goes into the UI library.

## Communication rules

1. The UI talks to the backend **only via UJMW**. No `postMessage` bridge for business calls, no proprietary endpoints. The WebView2 message channel may only be added for push / host capabilities, as described in `asp-engine-webview-coupling`.
2. **All card data** is read and written through the FUSE repository of `WorkItem` (`IWorkItemRepository` semantics: `GetEntities`, `GetEntityRefs`, `TryUpdateEntityFields`, `TryAddEntity`, ...). Do not invent card-specific service methods.
3. **Board management, configuration and filter-state persistence** use `IBoardManagementService` only.
4. All calls are **asynchronous** in the UI. Potentially slow calls (initial loads across many providers) use UJMW long-running operations (`?wait=2`, polling, cancel via `DELETE`).
5. Use **relative URLs** only; the UI must work under any mount path.
6. Prefer `TryUpdateEntityFields` with only the changed fields (state, area, priority) over full-entity updates.

## Board rendering rules

- **Column levels:** a view defines an ordered list of column dimensions (e.g. `State`, `Origin`, `Area`, `Person`). Each level splits the columns of the previous level.
- **Area dimension:** rendered as a tree of nested column headers. Every node can be collapsed into a thin vertical strip (expand/collapse = zoom in/out). A node with direct cards shows a `direct` sub-column.
- **Global priority:** cards are always ordered by `ManualReorderedPriority` descending inside every column. All columns start at the same vertical position.
- **Reordering:** implement exactly the algorithm documented on `WorkItem.ManualReorderedPriority` (midpoint, Snowflake44 at the top, 0 at the bottom, collision redistribution). Only send the changed priorities.
- **Vertical grouping:** two modes, selectable per view:
	- cross-column (shown by a continuous horizontal line; group rows are aligned across all columns),
	- within-column (groups separated per column only; saves space).
	Groups are collapsible and show the hidden card count.
- **ProgressState:** behave according to the root state (`New`, `Suspended`, `Working`, `Done`, `Discontinued`) but display the sub-state name.
- **Read-only boards:** disable drag and drop and editing based on the capabilities returned by the backend; never only by convention in the UI.
- **Out-of-scope values:** hide them; if a transition to such a value is allowed, show a narrow `<value> (leave board)` drop column.

## Views

A view stores: column levels, vertical order field, grouping mode, collapsed nodes / groups, display filters, and the base data set (selected origins, tag filters). Views are persisted through `IBoardManagementService`, never only in local storage.

## UI technology

- TypeScript, built as one classic IIFE bundle (`app.js` + `app.css`) into the embedded folder of `UniversalWorkItem.Boards.Ui`; the bundle is committed.
- The framework decision is open (epic Q9). Keep rendering logic separable from the transport layer so a later UShell module migration only replaces the shell.

## Coding rules (C#)

Follow the repository conventions: explicit types (no `var`), `this.` for instance members, K&R braces, braces for all blocks, `_PascalCase` fields, XML `<summary>` on methods, Newtonsoft.Json, synchronous contract methods (no `async`/`await` in contracts), arrays in public signatures.
