import { WorkItem, getRootState, getSubStateLabel } from "./model";
import { ViewState, getDimensionValue, getDistinctValues, toggle } from "./viewState";
import {
  Assignment,
  ColumnNode,
  buildColumnTree,
  collectLeaves,
  countLeaves,
  createGroupAssignment,
  getMaxDepth
} from "./columns";

// Rendering only - no transport code here (keeps a later UShell migration a shell-only change).

/**
 * Describes where a card was dropped.
 */
export interface DropTarget {
  /** Everything the target zone stands for (column levels + group). */
  assignments: Assignment[];
  /** Cards of the target zone in display order (without the dragged card); null = keep the priority. */
  orderedCards: WorkItem[] | null;
  /** Insert position within 'orderedCards'. */
  insertIndex: number;
}

export interface BoardCallbacks {
  onViewChanged: () => void;
  /** null = read-only board (drag and drop disabled). */
  onCardDropped: ((card: WorkItem, target: DropTarget) => void) | null;
  /** Opens the card (editor). */
  onCardOpened: (card: WorkItem) => void;
}

interface DropZoneContext {
  assignments: Assignment[];
  /** null = the zone does not support ordering (e.g. collapsed columns or group headers). */
  cards: WorkItem[] | null;
}

const collapsedColumnWidth = "44px";
const expandedColumnWidth = "minmax(220px, 280px)";
const headerRowHeight = 36;
const svgNamespace = "http://www.w3.org/2000/svg";

let draggedCard: WorkItem | null = null;
let draggedElement: HTMLElement | null = null;
const dropIndicator = createElement("div", "drop-indicator");

function createElement(tagName: string, className: string, text?: string): HTMLElement {
  const element = document.createElement(tagName);
  element.className = className;
  if (text !== undefined) {
    element.textContent = text;
  }
  return element;
}

/**
 * Small line icons (16x16, stroke = currentColor).
 * 'merge': two columns pushed together, 'split': two columns pushed apart, 'drill': tree branch.
 */
function createIcon(kind: "merge" | "split" | "drillIn" | "drillOut"): SVGElement {
  const svg = document.createElementNS(svgNamespace, "svg");
  svg.setAttribute("viewBox", "0 0 16 16");
  svg.setAttribute("width", "16");
  svg.setAttribute("height", "16");
  svg.setAttribute("aria-hidden", "true");
  svg.setAttribute("class", "icon");

  const paths: string[] = [];
  switch (kind) {
    case "merge":
      // |>  <|
      paths.push("M2 3v10", "M14 3v10", "M4.5 8h2.5", "M5.5 6l2 2-2 2", "M11.5 8H9", "M10.5 6l-2 2 2 2");
      break;
    case "split":
      // <|  |>
      paths.push("M8 3v10", "M2 8h4", "M4 6L2 8l2 2", "M14 8h-4", "M12 6l2 2-2 2");
      break;
    case "drillIn":
      // tree: parent with two children
      paths.push("M4 3v10", "M4 7h5", "M4 12h5", "M11 7h3", "M11 12h3");
      break;
    case "drillOut":
      // collapsed tree: parent only
      paths.push("M4 3v10", "M4 8h8");
      break;
  }

  for (const definition of paths) {
    const path = document.createElementNS(svgNamespace, "path");
    path.setAttribute("d", definition);
    svg.appendChild(path);
  }
  return svg;
}

function createIconButton(className: string, icon: SVGElement, title: string, onClick: () => void): HTMLElement {
  const button = createElement("button", className);
  (button as HTMLButtonElement).type = "button";
  button.title = title;
  button.setAttribute("aria-label", title);
  button.appendChild(icon);
  button.addEventListener("click", (event) => {
    event.stopPropagation();
    onClick();
  });
  return button;
}

// drag and drop

function removeDropIndicator(): void {
  if (dropIndicator.parentElement !== null) {
    dropIndicator.parentElement.removeChild(dropIndicator);
  }
}

function clearDropHighlights(): void {
  removeDropIndicator();
  for (const element of Array.from(document.querySelectorAll(".drop-target"))) {
    element.classList.remove("drop-target");
  }
}

function getCardElements(container: HTMLElement): HTMLElement[] {
  const result: HTMLElement[] = [];
  for (const child of Array.from(container.children)) {
    if (child.classList.contains("card") && child !== draggedElement) {
      result.push(child as HTMLElement);
    }
  }
  return result;
}

/**
 * Places the drop indicator at the insert position (based on the mouse position) and returns the insert index.
 */
function placeDropIndicator(container: HTMLElement, clientY: number): number {
  const cardElements = getCardElements(container);
  let index = 0;

  for (const cardElement of cardElements) {
    const rect = cardElement.getBoundingClientRect();
    if (clientY < rect.top + rect.height / 2) {
      if (dropIndicator.nextSibling !== cardElement) {
        container.insertBefore(dropIndicator, cardElement);
      }
      return index;
    }
    index++;
  }

  if (cardElements.length > 0) {
    const lastCard = cardElements[cardElements.length - 1];
    if (lastCard.nextSibling !== dropIndicator) {
      lastCard.after(dropIndicator);
    }
  } else if (dropIndicator.parentElement !== container) {
    container.appendChild(dropIndicator);
  }
  return index;
}

/**
 * Turns 'zone' into a drop target. Cards are expected as direct children of 'cardContainer'.
 */
function makeDropZone(zone: HTMLElement, cardContainer: HTMLElement, context: DropZoneContext, callbacks: BoardCallbacks): void {
  const onCardDropped = callbacks.onCardDropped;
  if (onCardDropped === null) {
    return;
  }

  zone.addEventListener("dragover", (event) => {
    if (draggedCard === null) {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    if (event.dataTransfer !== null) {
      event.dataTransfer.dropEffect = "move";
    }
    if (!zone.classList.contains("drop-target")) {
      clearDropHighlights();
      zone.classList.add("drop-target");
    }
    if (context.cards !== null) {
      placeDropIndicator(cardContainer, event.clientY);
    }
  });

  zone.addEventListener("dragleave", (event) => {
    const related = event.relatedTarget as Node | null;
    if (related === null || !zone.contains(related)) {
      zone.classList.remove("drop-target");
      if (dropIndicator.parentElement === cardContainer) {
        removeDropIndicator();
      }
    }
  });

  zone.addEventListener("drop", (event) => {
    if (draggedCard === null) {
      return;
    }
    event.preventDefault();
    event.stopPropagation();

    const card = draggedCard;
    let orderedCards: WorkItem[] | null = null;
    let insertIndex = 0;

    if (context.cards !== null) {
      insertIndex = placeDropIndicator(cardContainer, event.clientY);
      orderedCards = context.cards.filter((candidate) => candidate !== card);
    }

    clearDropHighlights();

    onCardDropped(card, {
      assignments: context.assignments,
      orderedCards: orderedCards,
      insertIndex: insertIndex
    });
  });
}

// cards

function createCard(card: WorkItem, callbacks: BoardCallbacks): HTMLElement {
  const rootState = getRootState(card.progressState);
  const element = createElement("article", "card state-border-" + rootState.toLowerCase());
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

  element.tabIndex = 0;
  element.addEventListener("click", () => callbacks.onCardOpened(card));
  element.addEventListener("keydown", (event) => {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      callbacks.onCardOpened(card);
    }
  });

  if (callbacks.onCardDropped !== null) {
    element.draggable = true;
    element.addEventListener("dragstart", (event) => {
      draggedCard = card;
      draggedElement = element;
      if (event.dataTransfer !== null) {
        event.dataTransfer.effectAllowed = "move";
        event.dataTransfer.setData("text/plain", card.origin + "#" + card.originRelatedId);
      }
      // defer, otherwise the drag image would be rendered semi-transparent as well
      window.setTimeout(() => element.classList.add("dragging"), 0);
    });
    element.addEventListener("dragend", () => {
      element.classList.remove("dragging");
      draggedCard = null;
      draggedElement = null;
      clearDropHighlights();
    });
  }

  return element;
}

function appendCards(container: HTMLElement, cards: WorkItem[], callbacks: BoardCallbacks): void {
  for (const card of cards) {
    container.appendChild(createCard(card, callbacks));
  }
}

function createCell(leaf: ColumnNode, column: number, row: number): HTMLElement {
  const cell = createElement("div", leaf.collapsed ? "cell collapsed" : "cell");
  cell.style.gridColumn = String(column);
  cell.style.gridRow = String(row);
  return cell;
}

function renderWithinColumnGroups(cell: HTMLElement, leaf: ColumnNode, view: ViewState, callbacks: BoardCallbacks): void {
  for (const group of getDistinctValues(leaf.cards, view.groupBy)) {
    const groupKey = "within|" + leaf.key + "|" + group;
    const groupCards = leaf.cards.filter((card) => getDimensionValue(card, view.groupBy) === group);
    const collapsed = view.collapsedGroups.has(groupKey);

    const section = createElement("section", "group-section");
    const header = createElement("div", collapsed ? "group-header within collapsed" : "group-header within");
    header.appendChild(createElement("span", "group-chevron", collapsed ? "\u25B8" : "\u25BE"));
    header.appendChild(createElement("span", "group-title", group));
    header.appendChild(createElement("span", "count-pill", String(groupCards.length)));
    header.addEventListener("click", () => {
      toggle(view.collapsedGroups, groupKey);
      callbacks.onViewChanged();
    });
    section.appendChild(header);

    const body = createElement("div", "group-body");
    if (!collapsed) {
      appendCards(body, groupCards, callbacks);
    }
    section.appendChild(body);

    makeDropZone(section, body, {
      assignments: leaf.assignments.concat([createGroupAssignment(view.groupBy, group)]),
      cards: collapsed ? null : groupCards
    }, callbacks);

    cell.appendChild(section);
  }
}

/**
 * Creates the header of a column node (any level). Leaf headers of upper levels span down to the card rows.
 */
function createColumnHeader(node: ColumnNode, column: number, maxDepth: number, view: ViewState, callbacks: BoardCallbacks): HTMLElement {
  const header = createElement("div", node.depth === 0 ? "column-header level-0" : "column-header level-n");
  if (node.dimension === "status") {
    header.classList.add("state-header-" + node.label.toLowerCase());
  }
  if (node.collapsed) {
    header.classList.add("collapsed");
  }

  let rowSpan = 1;
  if (node.children.length === 0) {
    rowSpan = maxDepth - node.depth;
  }
  header.style.gridColumn = column + " / span " + countLeaves(node);
  header.style.gridRow = (node.depth + 1) + " / span " + rowSpan;
  header.style.top = (node.depth * headerRowHeight) + "px";
  header.title = node.path;

  const title = createElement("button", "header-toggle");
  (title as HTMLButtonElement).type = "button";
  title.title = (node.collapsed ? "Expand " : "Collapse ") + node.path;
  if (!node.collapsed) {
    title.appendChild(createElement("span", "header-title", node.label));
  }
  title.appendChild(createElement("span", "count-pill", String(node.cards.length)));
  title.addEventListener("click", () => {
    toggle(view.collapsedColumns, node.key);
    callbacks.onViewChanged();
  });
  header.appendChild(title);

  if (node.areaExpanded !== null) {
    let drillTitle = "Show the sub-areas of '" + node.label + "' as separate columns";
    if (node.areaExpanded) {
      drillTitle = "Condense the sub-areas of '" + node.label + "' into one column";
    }
    header.appendChild(createIconButton(
      node.areaExpanded ? "header-icon-button active" : "header-icon-button",
      createIcon(node.areaExpanded ? "drillOut" : "drillIn"),
      drillTitle,
      () => {
        toggle(view.expandedAreas, node.key);
        callbacks.onViewChanged();
      }
    ));
  }

  if (node.merged !== null) {
    let mergeTitle = "Merge the sub-columns of '" + node.label + "' into one column";
    if (node.merged) {
      mergeTitle = "Split '" + node.label + "' into its sub-columns";
    }
    header.appendChild(createIconButton(
      node.merged ? "header-icon-button active" : "header-icon-button",
      createIcon(node.merged ? "split" : "merge"),
      mergeTitle,
      () => {
        toggle(view.toggledSplits, node.key);
        callbacks.onViewChanged();
      }
    ));
  }

  return header;
}

function appendHeaders(grid: HTMLElement, nodes: ColumnNode[], firstColumn: number, maxDepth: number, view: ViewState, callbacks: BoardCallbacks): void {
  let column = firstColumn;
  for (const node of nodes) {
    grid.appendChild(createColumnHeader(node, column, maxDepth, view, callbacks));
    if (node.children.length > 0) {
      appendHeaders(grid, node.children, column, maxDepth, view, callbacks);
    }
    column += countLeaves(node);
  }
}

/**
 * Renders the board as one CSS grid: one header row per column level,
 * followed by either one card row or (cross-column grouping) one header row + card row per group.
 * All columns start at the same vertical position; cards are ordered by the global priority (DESC).
 */
export function renderBoard(host: HTMLElement, cards: WorkItem[], view: ViewState, callbacks: BoardCallbacks): void {
  const sortedCards = cards.slice().sort(
    (a, b) => (Number(b.manualReorderedPriority) || 0) - (Number(a.manualReorderedPriority) || 0)
  );
  const columns = buildColumnTree(sortedCards, view);
  const leaves = collectLeaves(columns, []);
  const maxDepth = Math.max(1, getMaxDepth(columns));

  const grid = createElement("div", "board-grid");
  grid.style.gridTemplateColumns = leaves.map((leaf) => (leaf.collapsed ? collapsedColumnWidth : expandedColumnWidth)).join(" ");
  if (callbacks.onCardDropped === null) {
    grid.classList.add("read-only");
  }

  // opaque band behind all header rows: covers the column gaps and rounded corners,
  // so scrolled cells / group rows never shine through between or above the sticky headers
  const headerBackdrop = createElement("div", "header-backdrop");
  headerBackdrop.style.gridColumn = "1 / -1";
  headerBackdrop.style.gridRow = "1 / span " + maxDepth;
  grid.appendChild(headerBackdrop);

  appendHeaders(grid, columns, 1, maxDepth, view, callbacks);

  let row = maxDepth + 1;

  if (view.groupBy !== "none" && view.groupingMode === "crossColumn") {
    for (const group of getDistinctValues(sortedCards, view.groupBy)) {
      const groupKey = "cross|" + view.groupBy + "|" + group;
      const groupCards = sortedCards.filter((card) => getDimensionValue(card, view.groupBy) === group);
      const groupAssignment = createGroupAssignment(view.groupBy, group);
      const collapsed = view.collapsedGroups.has(groupKey);

      const groupHeader = createElement("div", collapsed ? "group-header cross collapsed" : "group-header cross");
      if (view.groupBy === "status") {
        groupHeader.classList.add("state-header-" + group.toLowerCase());
      }
      groupHeader.appendChild(createElement("span", "group-chevron", collapsed ? "\u25B8" : "\u25BE"));
      groupHeader.appendChild(createElement("span", "group-title", group));
      groupHeader.appendChild(createElement("span", "count-pill", String(groupCards.length)));
      groupHeader.style.gridColumn = "1 / -1";
      groupHeader.style.gridRow = String(row);
      groupHeader.addEventListener("click", () => {
        toggle(view.collapsedGroups, groupKey);
        callbacks.onViewChanged();
      });
      makeDropZone(groupHeader, groupHeader, { assignments: [groupAssignment], cards: null }, callbacks);
      grid.appendChild(groupHeader);
      row++;

      if (!collapsed) {
        leaves.forEach((leaf, index) => {
          const cell = createCell(leaf, index + 1, row);
          const cellCards = leaf.cards.filter((card) => getDimensionValue(card, view.groupBy) === group);
          if (leaf.collapsed) {
            // collapsed columns show the number of cards per group
            cell.appendChild(createElement("div", cellCards.length > 0 ? "collapsed-count" : "collapsed-count empty", String(cellCards.length)));
          } else {
            appendCards(cell, cellCards, callbacks);
          }
          makeDropZone(cell, cell, {
            assignments: leaf.assignments.concat([groupAssignment]),
            cards: leaf.collapsed ? null : cellCards
          }, callbacks);
          grid.appendChild(cell);
        });
        row++;
      }
    }
  } else {
    leaves.forEach((leaf, index) => {
      const cell = createCell(leaf, index + 1, row);
      const grouped = view.groupBy !== "none";
      if (leaf.collapsed) {
        cell.appendChild(createElement("div", "collapsed-count", String(leaf.cards.length)));
        cell.appendChild(createElement("div", "collapsed-label", leaf.path));
      } else if (grouped) {
        renderWithinColumnGroups(cell, leaf, view, callbacks);
      } else {
        appendCards(cell, leaf.cards, callbacks);
      }
      makeDropZone(cell, cell, {
        assignments: leaf.assignments,
        cards: leaf.collapsed || grouped ? null : leaf.cards
      }, callbacks);
      grid.appendChild(cell);
    });
  }

  host.replaceChildren(grid);
}

/**
 * Mobile layout (Trello-like): exactly one (leaf) column per screen width, the columns are changed by a
 * horizontal swipe (native scroll snapping). Cards are opened by a tap; moving is done in the card editor.
 * Returns the number of columns.
 */
export function renderMobileBoard(
  host: HTMLElement,
  cards: WorkItem[],
  view: ViewState,
  callbacks: BoardCallbacks,
  activeIndex: number,
  onActiveIndexChanged: (index: number) => void
): number {
  const sortedCards = cards.slice().sort(
    (a, b) => (Number(b.manualReorderedPriority) || 0) - (Number(a.manualReorderedPriority) || 0)
  );
  const leaves = collectLeaves(buildColumnTree(sortedCards, view), []);
  const pageIndex = Math.max(0, Math.min(activeIndex, leaves.length - 1));

  const container = createElement("div", "mobile-board");
  const tabs = createElement("nav", "mobile-tabs");
  const pages = createElement("div", "mobile-pages");
  const tabElements: HTMLElement[] = [];

  leaves.forEach((leaf, index) => {
    const tab = createElement("button", "mobile-tab");
    (tab as HTMLButtonElement).type = "button";
    if (leaf.dimension === "status") {
      tab.classList.add("state-header-" + leaf.label.toLowerCase());
    }
    tab.appendChild(createElement("span", "mobile-tab-title", leaf.path));
    tab.appendChild(createElement("span", "count-pill", String(leaf.cards.length)));
    tab.addEventListener("click", () => {
      pages.scrollTo({ left: index * pages.clientWidth, behavior: "smooth" });
    });
    tabs.appendChild(tab);
    tabElements.push(tab);

    const page = createElement("section", "mobile-page");
    const header = createElement("div", "column-header level-0 mobile-page-header");
    if (leaf.dimension === "status") {
      header.classList.add("state-header-" + leaf.label.toLowerCase());
    }
    header.appendChild(createElement("span", "header-title", leaf.path));
    header.appendChild(createElement("span", "count-pill", String(leaf.cards.length)));
    page.appendChild(header);

    const cell = createElement("div", "cell mobile-cell");
    if (view.groupBy !== "none") {
      renderWithinColumnGroups(cell, leaf, view, callbacks);
    } else {
      appendCards(cell, leaf.cards, callbacks);
    }
    if (leaf.cards.length === 0) {
      cell.appendChild(createElement("div", "mobile-empty", "No cards"));
    }
    page.appendChild(cell);
    pages.appendChild(page);
  });

  let currentIndex = pageIndex;

  const markActiveTab = (index: number): void => {
    tabElements.forEach((tab, tabIndex) => tab.classList.toggle("active", tabIndex === index));
    const activeTab = tabElements[index];
    if (activeTab !== undefined) {
      tabs.scrollTo({ left: activeTab.offsetLeft - (tabs.clientWidth - activeTab.offsetWidth) / 2, behavior: "smooth" });
    }
  };

  pages.addEventListener("scroll", () => {
    if (pages.clientWidth === 0) {
      return;
    }
    const index = Math.round(pages.scrollLeft / pages.clientWidth);
    if (index !== currentIndex) {
      currentIndex = index;
      markActiveTab(index);
      onActiveIndexChanged(index);
    }
  }, { passive: true });

  container.appendChild(tabs);
  container.appendChild(pages);
  host.replaceChildren(container);

  // restore the active column (after layout, otherwise the width is unknown)
  pages.scrollLeft = pageIndex * pages.clientWidth;
  markActiveTab(pageIndex);

  return leaves.length;
}
