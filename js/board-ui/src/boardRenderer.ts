import { WorkItem, RootStates, getRootState, getSubStateLabel } from "./model";
import { ViewState, getDimensionValue, getDistinctValues, toggle } from "./viewState";

// Rendering only - no transport code here (keeps a later UShell migration a shell-only change).

interface LeafColumn {
  key: string;
  label: string | null;
  collapsed: boolean;
  cards: WorkItem[];
}

interface StateColumn {
  state: string;
  collapsed: boolean;
  cards: WorkItem[];
  leaves: LeafColumn[];
}

const collapsedColumnWidth = "36px";
const expandedColumnWidth = "minmax(220px, 280px)";

function createElement(tagName: string, className: string, text?: string): HTMLElement {
  const element = document.createElement(tagName);
  element.className = className;
  if (text !== undefined) {
    element.textContent = text;
  }
  return element;
}

function getAllStates(cards: WorkItem[]): string[] {
  const states = RootStates.slice();
  for (const card of cards) {
    const rootState = getRootState(card.progressState);
    if (states.indexOf(rootState) < 0) {
      states.push(rootState);
    }
  }
  return states;
}

function buildStateColumns(cards: WorkItem[], view: ViewState): StateColumn[] {
  const result: StateColumn[] = [];

  for (const state of getAllStates(cards)) {
    const stateCards = cards.filter((card) => getRootState(card.progressState) === state);
    const stateCollapsed = view.collapsedColumns.has(state);
    const leaves: LeafColumn[] = [];

    if (view.secondLevel === "none" || stateCollapsed || stateCards.length === 0) {
      leaves.push({ key: state, label: null, collapsed: stateCollapsed, cards: stateCards });
    } else {
      for (const value of getDistinctValues(stateCards, view.secondLevel)) {
        const key = state + "|" + value;
        leaves.push({
          key: key,
          label: value,
          collapsed: view.collapsedColumns.has(key),
          cards: stateCards.filter((card) => getDimensionValue(card, view.secondLevel) === value)
        });
      }
    }

    result.push({ state: state, collapsed: stateCollapsed, cards: stateCards, leaves: leaves });
  }

  return result;
}

function createCard(card: WorkItem): HTMLElement {
  const rootState = getRootState(card.progressState);
  const element = createElement("article", "card");
  element.title = "Priority: " + String(card.manualReorderedPriority);

  element.appendChild(createElement("div", "card-title", card.title));

  const meta = createElement("div", "card-meta");
  meta.appendChild(createElement("span", "state-badge state-" + rootState.toLowerCase(), getSubStateLabel(card.progressState)));
  meta.appendChild(createElement("span", "card-id", card.origin + " #" + card.originRelatedId));
  element.appendChild(meta);

  element.appendChild(createElement("div", "card-area", card.concernArea || "/"));

  if (card.assignedTo) {
    element.appendChild(createElement("div", "card-assignee", card.assignedTo));
  }

  if (card.tags && card.tags.length > 0) {
    const tags = createElement("div", "card-tags");
    for (const tag of card.tags) {
      tags.appendChild(createElement("span", "tag", tag));
    }
    element.appendChild(tags);
  }

  return element;
}

function appendCards(container: HTMLElement, cards: WorkItem[]): void {
  for (const card of cards) {
    container.appendChild(createCard(card));
  }
}

function createCell(leaf: LeafColumn, column: number, row: number): HTMLElement {
  const cell = createElement("div", leaf.collapsed ? "cell collapsed" : "cell");
  cell.style.gridColumn = String(column);
  cell.style.gridRow = String(row);
  return cell;
}

function renderWithinColumnGroups(cell: HTMLElement, leaf: LeafColumn, view: ViewState, onViewChanged: () => void): void {
  for (const group of getDistinctValues(leaf.cards, view.groupBy)) {
    const groupKey = "within|" + leaf.key + "|" + group;
    const groupCards = leaf.cards.filter((card) => getDimensionValue(card, view.groupBy) === group);
    const collapsed = view.collapsedGroups.has(groupKey);

    const header = createElement(
      "div",
      collapsed ? "group-header within collapsed" : "group-header within",
      (collapsed ? "\u25B8 " : "\u25BE ") + group + " (" + groupCards.length + (collapsed ? " hidden)" : ")")
    );
    header.addEventListener("click", () => {
      toggle(view.collapsedGroups, groupKey);
      onViewChanged();
    });
    cell.appendChild(header);

    if (!collapsed) {
      appendCards(cell, groupCards);
    }
  }
}

/**
 * Renders the board as one CSS grid: header rows (state level + optional second column level),
 * followed by either one card row or (cross-column grouping) one header row + card row per group.
 * All columns start at the same vertical position; cards are ordered by the global priority (DESC).
 */
export function renderBoard(host: HTMLElement, cards: WorkItem[], view: ViewState, onViewChanged: () => void): void {
  const sortedCards = cards.slice().sort(
    (a, b) => (Number(b.manualReorderedPriority) || 0) - (Number(a.manualReorderedPriority) || 0)
  );
  const stateColumns = buildStateColumns(sortedCards, view);
  const leaves: LeafColumn[] = [];
  for (const stateColumn of stateColumns) {
    leaves.push(...stateColumn.leaves);
  }

  const hasSecondLevel = view.secondLevel !== "none";
  const grid = createElement("div", hasSecondLevel ? "board-grid two-header-rows" : "board-grid");
  grid.style.gridTemplateColumns = leaves.map((leaf) => (leaf.collapsed ? collapsedColumnWidth : expandedColumnWidth)).join(" ");

  // header rows
  let column = 1;
  for (const stateColumn of stateColumns) {
    const spansBothRows = hasSecondLevel && stateColumn.leaves.length === 1 && stateColumn.leaves[0].label === null;
    const header = createElement("div", stateColumn.collapsed ? "state-header collapsed" : "state-header");
    header.style.gridColumn = column + " / span " + stateColumn.leaves.length;
    header.style.gridRow = spansBothRows ? "1 / span 2" : "1";
    header.title = (stateColumn.collapsed ? "Expand " : "Collapse ") + stateColumn.state;
    if (stateColumn.collapsed) {
      header.textContent = String(stateColumn.cards.length);
    } else {
      header.textContent = stateColumn.state + " (" + stateColumn.cards.length + ")";
    }
    header.addEventListener("click", () => {
      toggle(view.collapsedColumns, stateColumn.state);
      onViewChanged();
    });
    grid.appendChild(header);

    if (hasSecondLevel && !spansBothRows) {
      stateColumn.leaves.forEach((leaf, index) => {
        const leafHeader = createElement("div", leaf.collapsed ? "leaf-header collapsed" : "leaf-header");
        leafHeader.style.gridColumn = String(column + index);
        leafHeader.style.gridRow = "2";
        leafHeader.title = (leaf.collapsed ? "Expand " : "Collapse ") + String(leaf.label);
        if (leaf.collapsed) {
          leafHeader.textContent = String(leaf.cards.length);
        } else {
          leafHeader.textContent = String(leaf.label) + " (" + leaf.cards.length + ")";
        }
        leafHeader.addEventListener("click", () => {
          toggle(view.collapsedColumns, leaf.key);
          onViewChanged();
        });
        grid.appendChild(leafHeader);
      });
    }

    column += stateColumn.leaves.length;
  }

  let row = hasSecondLevel ? 3 : 2;

  // body rows
  if (view.groupBy !== "none" && view.groupingMode === "crossColumn") {
    for (const group of getDistinctValues(sortedCards, view.groupBy)) {
      const groupKey = "cross|" + group;
      const groupCards = sortedCards.filter((card) => getDimensionValue(card, view.groupBy) === group);
      const collapsed = view.collapsedGroups.has(groupKey);

      const groupHeader = createElement(
        "div",
        collapsed ? "group-header cross collapsed" : "group-header cross",
        (collapsed ? "\u25B8 " : "\u25BE ") + group + " (" + groupCards.length + (collapsed ? " hidden)" : ")")
      );
      groupHeader.style.gridColumn = "1 / -1";
      groupHeader.style.gridRow = String(row);
      groupHeader.addEventListener("click", () => {
        toggle(view.collapsedGroups, groupKey);
        onViewChanged();
      });
      grid.appendChild(groupHeader);
      row++;

      if (!collapsed) {
        leaves.forEach((leaf, index) => {
          const cell = createCell(leaf, index + 1, row);
          if (!leaf.collapsed) {
            appendCards(cell, leaf.cards.filter((card) => getDimensionValue(card, view.groupBy) === group));
          }
          grid.appendChild(cell);
        });
        row++;
      }
    }
  } else {
    leaves.forEach((leaf, index) => {
      const cell = createCell(leaf, index + 1, row);
      if (leaf.collapsed) {
        cell.appendChild(createElement("div", "collapsed-label", leaf.label === null ? leaf.key : leaf.key.replace("|", " / ")));
      } else if (view.groupBy !== "none") {
        renderWithinColumnGroups(cell, leaf, view, onViewChanged);
      } else {
        appendCards(cell, leaf.cards);
      }
      grid.appendChild(cell);
    });
  }

  host.replaceChildren(grid);
}
