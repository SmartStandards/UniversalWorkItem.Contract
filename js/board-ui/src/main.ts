import "./app.css";
import { getBoards, getCards, tryUpdateCardFields } from "./boardApi";
import { BoardDescriptor, RootStates, WorkItem, getAreaRoot, getRootState } from "./model";
import { DropTarget, renderBoard, renderMobileBoard } from "./boardRenderer";
import { openCardEditor } from "./cardEditor";
import { Assignment, isAreaWithin } from "./columns";
import {
  ColumnDimensionLabels,
  Dimension,
  DimensionLabels,
  UnassignedLabel,
  ViewState,
  createDefaultViewState,
  getDimensionValue,
  normalizeArea
} from "./viewState";

const priorityStep = 1000;

function createSelect<TValue extends string>(labels: Record<TValue, string>): HTMLSelectElement {
  const select = document.createElement("select");
  for (const value of Object.keys(labels) as TValue[]) {
    const option = document.createElement("option");
    option.value = value;
    option.textContent = labels[value];
    select.appendChild(option);
  }
  return select;
}

/**
 * Wraps a native select into a web-style combobox (custom border, chevron, focus ring).
 */
function createCombo(labelText: string, select: HTMLSelectElement): HTMLElement {
  const field = document.createElement("label");
  field.className = "toolbar-field";

  const caption = document.createElement("span");
  caption.className = "field-caption";
  caption.textContent = labelText;

  const combo = document.createElement("span");
  combo.className = "combo";
  combo.appendChild(select);

  field.appendChild(caption);
  field.appendChild(combo);
  return field;
}

/**
 * Creates a toggle switch (checkbox based, so it is keyboard accessible).
 */
function createSwitch(input: HTMLInputElement, offText: string, onText: string): HTMLElement {
  const field = document.createElement("label");
  field.className = "switch-field";

  input.type = "checkbox";
  input.className = "switch-input";

  const offLabel = document.createElement("span");
  offLabel.className = "switch-text switch-off";
  offLabel.textContent = offText;

  const track = document.createElement("span");
  track.className = "switch-track";
  track.appendChild(document.createElement("span")).className = "switch-thumb";

  const onLabel = document.createElement("span");
  onLabel.className = "switch-text switch-on";
  onLabel.textContent = onText;

  field.appendChild(input);
  field.appendChild(offLabel);
  field.appendChild(track);
  field.appendChild(onLabel);
  return field;
}

/**
 * Replaces the area root of 'area' ('/repo-alpha/ui' + '/repo-beta' -> '/repo-beta/ui').
 */
function replaceAreaRoot(area: string, targetRoot: string): string {
  const currentArea = normalizeArea(area);
  const currentRoot = getAreaRoot(currentArea);
  let rest = "";
  if (currentRoot !== "/") {
    rest = currentArea.substring(currentRoot.length);
  }
  if (targetRoot === "/") {
    return rest || "/";
  }
  return targetRoot + rest;
}

/**
 * Applies one assignment of the drop target to the field changes (working on 'pending', the card as it will be).
 * Throws if the dimension cannot be edited or two assignments contradict each other.
 */
function applyAssignment(pending: WorkItem, assignment: Assignment, board: BoardDescriptor | undefined): void {
  const value = assignment.value;

  switch (assignment.dimension) {
    case "none":
      return;
    case "status":
      if (getRootState(pending.progressState) !== value) {
        pending.progressState = value;
      }
      return;
    case "area":
      if (assignment.subtree) {
        if (!isAreaWithin(pending.concernArea, value)) {
          pending.concernArea = value;
        }
      } else if (normalizeArea(pending.concernArea) !== value) {
        pending.concernArea = value;
      }
      return;
    case "areaRoot":
      if (getAreaRoot(pending.concernArea) !== value) {
        pending.concernArea = replaceAreaRoot(pending.concernArea, value);
      }
      return;
    case "assignedTo":
      pending.assignedTo = value === UnassignedLabel ? "" : value;
      return;
    case "origin": {
      if (pending.origin === value) {
        return;
      }
      // composed boards: the origin is changed by moving the card into the area root of the target source
      const source = board === undefined ? undefined : (board.sources || []).find((candidate) => candidate.origin === value);
      if (source === undefined) {
        throw new Error("The origin '" + value + "' is not a source of this board - the card cannot be moved there.");
      }
      pending.concernArea = replaceAreaRoot(pending.concernArea, source.areaRoot);
      return;
    }
    default:
      throw new Error("The dimension '" + DimensionLabels[assignment.dimension] + "' cannot be changed via drag and drop.");
  }
}

/**
 * Returns the source (origin) which owns the given board area (null = not a composed board / unknown root).
 */
function findSourceOrigin(board: BoardDescriptor | undefined, area: string): string | null {
  if (board === undefined || !board.sources) {
    return null;
  }
  const root = getAreaRoot(area);
  const source = board.sources.find((candidate) => candidate.areaRoot.toLowerCase() === root.toLowerCase());
  return source === undefined ? null : source.origin;
}

function isTransitionAllowed(board: BoardDescriptor, sourceOrigin: string, targetOrigin: string): boolean {
  return (board.allowedOriginTransitions || []).some((transition) =>
    transition.sourceOrigin.toLowerCase() === sourceOrigin.toLowerCase() &&
    transition.targetOrigin.toLowerCase() === targetOrigin.toLowerCase()
  );
}

/**
 * Modal warning dialog. Resolves true if the user confirmed.
 */
function confirmDialog(title: string, message: string, confirmText: string): Promise<boolean> {
  return new Promise<boolean>((resolve) => {
    const dialog = document.createElement("dialog");
    dialog.className = "confirm-dialog";

    const heading = document.createElement("h2");
    heading.className = "confirm-title";
    heading.textContent = "\u26A0 " + title;

    const text = document.createElement("p");
    text.className = "confirm-text";
    text.textContent = message;

    const buttons = document.createElement("div");
    buttons.className = "confirm-buttons";

    const cancelButton = document.createElement("button");
    cancelButton.type = "button";
    cancelButton.className = "button";
    cancelButton.textContent = "Cancel";

    const okButton = document.createElement("button");
    okButton.type = "button";
    okButton.className = "button button-danger";
    okButton.textContent = confirmText;

    buttons.appendChild(cancelButton);
    buttons.appendChild(okButton);
    dialog.appendChild(heading);
    dialog.appendChild(text);
    dialog.appendChild(buttons);
    document.body.appendChild(dialog);

    let result = false;
    cancelButton.addEventListener("click", () => dialog.close());
    okButton.addEventListener("click", () => {
      result = true;
      dialog.close();
    });
    dialog.addEventListener("close", () => {
      dialog.remove();
      resolve(result);
    });

    dialog.showModal();
    cancelButton.focus();
  });
}

/**
 * Calculates a priority which places the card between its new neighbours (priority is ordered DESC).
 */
function calculatePriority(target: DropTarget): number | null {
  if (target.orderedCards === null || target.orderedCards.length === 0) {
    return null;
  }

  const above = target.insertIndex > 0 ? target.orderedCards[target.insertIndex - 1] : null;
  const below = target.insertIndex < target.orderedCards.length ? target.orderedCards[target.insertIndex] : null;

  if (above !== null && below !== null) {
    const upper = Number(above.manualReorderedPriority) || 0;
    const lower = Number(below.manualReorderedPriority) || 0;
    const middle = Math.floor((upper + lower) / 2);
    if (middle <= lower) {
      return lower + 1; // no gap left - the card shares the slot with its upper neighbour
    }
    return middle;
  }
  if (above !== null) {
    return (Number(above.manualReorderedPriority) || 0) - priorityStep;
  }
  if (below !== null) {
    return (Number(below.manualReorderedPriority) || 0) + priorityStep;
  }
  return null;
}

class BoardApp {
  private boards: BoardDescriptor[] = [];
  private cards: WorkItem[] = [];
  private readonly viewStates = new Map<string, ViewState>();
  private loadSequence = 0;

  private readonly boardSelector = document.createElement("select");
  private readonly readOnlyBadge = document.createElement("span");
  private readonly columnsSelector = createSelect<Exclude<Dimension, "none">>(ColumnDimensionLabels);
  private readonly secondLevelSelector = createSelect<Dimension>(DimensionLabels);
  private readonly groupBySelector = createSelect<Dimension>(DimensionLabels);
  private readonly crossColumnSwitch = document.createElement("input");
  private readonly crossColumnField = createSwitch(this.crossColumnSwitch, "Within column", "Cross-column rows");
  private readonly reloadButton = document.createElement("button");
  private readonly statusElement = document.createElement("span");
  private readonly boardElement = document.createElement("main");
  private readonly mobileQuery = window.matchMedia("(max-width: 700px)");
  private mobileColumnIndex = 0;

  constructor(root: HTMLElement) {
    const toolbar = document.createElement("header");
    toolbar.className = "toolbar";

    const brand = document.createElement("span");
    brand.className = "brand";
    brand.textContent = "UWI Board";

    this.readOnlyBadge.className = "readonly-badge";
    this.readOnlyBadge.textContent = "read-only";
    this.readOnlyBadge.hidden = true;

    this.reloadButton.type = "button";
    this.reloadButton.className = "button";
    this.reloadButton.textContent = "\u21BB Reload";
    this.statusElement.className = "status";
    this.boardElement.className = "board";

    const boardCombo = document.createElement("span");
    boardCombo.className = "combo combo-board";
    boardCombo.appendChild(this.boardSelector);

    toolbar.appendChild(brand);
    toolbar.appendChild(boardCombo);
    toolbar.appendChild(this.readOnlyBadge);
    toolbar.appendChild(createCombo("Columns", this.columnsSelector));
    toolbar.appendChild(createCombo("Sub-columns", this.secondLevelSelector));
    toolbar.appendChild(createCombo("Group by", this.groupBySelector));
    toolbar.appendChild(this.crossColumnField);
    toolbar.appendChild(this.reloadButton);
    toolbar.appendChild(this.statusElement);

    root.replaceChildren(toolbar, this.boardElement);

    this.boardSelector.addEventListener("change", () => this.loadCards());
    this.reloadButton.addEventListener("click", () => this.loadCards());
    this.columnsSelector.addEventListener("change", () => {
      const view = this.currentView;
      view.columns = this.columnsSelector.value as Dimension;
      if (view.secondLevel === view.columns) {
        view.secondLevel = "none";
      }
      if (view.groupBy === view.columns) {
        view.groupBy = "none";
      }
      // structure changed - the old column keys are meaningless now (area columns start condensed again)
      view.collapsedColumns.clear();
      view.toggledSplits.clear();
      view.expandedAreas.clear();
      this.syncViewControls();
      this.render();
    });
    this.secondLevelSelector.addEventListener("change", () => {
      this.currentView.secondLevel = this.secondLevelSelector.value as Dimension;
      this.currentView.toggledSplits.clear();
      this.render();
    });
    this.groupBySelector.addEventListener("change", () => {
      this.currentView.groupBy = this.groupBySelector.value as Dimension;
      this.syncViewControls();
      this.render();
    });
    this.crossColumnSwitch.addEventListener("change", () => {
      this.currentView.groupingMode = this.crossColumnSwitch.checked ? "crossColumn" : "withinColumn";
      this.render();
    });
    this.mobileQuery.addEventListener("change", () => this.render());
  }

  private get currentBoardId(): string {
    return this.boardSelector.value;
  }

  private get currentBoard(): BoardDescriptor | undefined {
    return this.boards.find((candidate) => candidate.boardId === this.currentBoardId);
  }

  private get currentView(): ViewState {
    let view = this.viewStates.get(this.currentBoardId);
    if (view === undefined) {
      view = createDefaultViewState();
      this.viewStates.set(this.currentBoardId, view);
    }
    return view;
  }

  public start(): void {
    this.loadBoards();
  }

  private async loadBoards(): Promise<void> {
    try {
      this.setStatus("Loading boards...");
      this.boards = await getBoards();
      this.boardSelector.replaceChildren();
      for (const board of this.boards) {
        const option = document.createElement("option");
        option.value = board.boardId;
        option.textContent = board.title;
        this.boardSelector.appendChild(option);
      }
      await this.loadCards();
    } catch (error) {
      this.reportError(error);
    }
  }

  private async loadCards(): Promise<void> {
    const boardId = this.currentBoardId;
    const sequence = ++this.loadSequence;

    this.syncViewControls();

    if (!boardId) {
      this.cards = [];
      this.render();
      this.setStatus("No board available.");
      return;
    }

    try {
      this.setStatus("Loading...");
      const cards = await getCards(boardId);
      if (sequence !== this.loadSequence) {
        return; // a newer load was started meanwhile
      }
      this.cards = cards;
      this.render();
      this.setStatus(cards.length + " cards");
    } catch (error) {
      if (sequence === this.loadSequence) {
        this.reportError(error);
      }
    }
  }

  private syncViewControls(): void {
    const board = this.currentBoard;
    this.readOnlyBadge.hidden = !(board && board.isReadOnly);

    const view = this.currentView;
    this.columnsSelector.value = view.columns;
    this.secondLevelSelector.value = view.secondLevel;
    this.groupBySelector.value = view.groupBy;
    this.crossColumnSwitch.checked = view.groupingMode === "crossColumn";
    this.crossColumnSwitch.disabled = view.groupBy === "none";
    this.crossColumnField.classList.toggle("disabled", view.groupBy === "none");
  }

  private render(): void {
    const board = this.currentBoard;
    const isReadOnly = board === undefined || board.isReadOnly;

    if (this.mobileQuery.matches) {
      // touch devices: no HTML5 drag and drop - cards are moved via the editor
      this.boardElement.classList.add("mobile");
      renderMobileBoard(this.boardElement, this.cards, this.currentView, {
        onViewChanged: () => this.render(),
        onCardDropped: null,
        onCardOpened: (card) => this.editCard(card)
      }, this.mobileColumnIndex, (index) => {
        this.mobileColumnIndex = index;
      });
      return;
    }

    this.boardElement.classList.remove("mobile");
    renderBoard(this.boardElement, this.cards, this.currentView, {
      onViewChanged: () => this.render(),
      onCardDropped: isReadOnly ? null : (card, target) => this.moveCard(card, target),
      onCardOpened: (card) => this.editCard(card)
    });
  }

  private collectDistinct(selector: (card: WorkItem) => string, initial: readonly string[]): string[] {
    const values = new Set<string>(initial);
    for (const card of this.cards) {
      const value = selector(card);
      if (value) {
        values.add(value);
      }
    }
    return Array.from(values).sort((a, b) => a.localeCompare(b));
  }

  /**
   * Ensures that an area change which moves the card into another origin (composed boards) is enabled
   * and confirmed. Returns false if the user cancelled.
   */
  private async confirmOriginChange(card: WorkItem, newArea: string): Promise<boolean> {
    const board = this.currentBoard;
    if (board === undefined) {
      return true;
    }

    const sourceOrigin = findSourceOrigin(board, card.concernArea);
    const targetOrigin = findSourceOrigin(board, newArea);

    if (sourceOrigin === null || targetOrigin === null || sourceOrigin === targetOrigin) {
      return true;
    }
    if (!isTransitionAllowed(board, sourceOrigin, targetOrigin)) {
      throw new Error(
        "Moving cards from '" + sourceOrigin + "' to '" + targetOrigin + "' is not enabled for this board " +
        "(the directed origin transition must be allowed in the backend)."
      );
    }
    return confirmDialog(
      "Move card to another origin?",
      "The card '" + card.title + "' (" + card.origin + " #" + card.originRelatedId + ") will be MOVED from '" +
      sourceOrigin + "' to '" + targetOrigin + "'. It is created as a new item in the target and DELETED in the " +
      "original source - its identity (Origin/Id) changes and links to the original item may break.",
      "Move card"
    );
  }

  /**
   * Opens the card editor and saves the changed fields via 'TryUpdateEntityFields'.
   */
  private async editCard(card: WorkItem): Promise<void> {
    const board = this.currentBoard;
    const isReadOnly = board === undefined || board.isReadOnly;

    try {
      const changedFields = await openCardEditor(card, {
        readOnly: isReadOnly,
        states: this.collectDistinct((candidate) => candidate.progressState, RootStates),
        areas: this.collectDistinct((candidate) => normalizeArea(candidate.concernArea), []),
        assignees: this.collectDistinct((candidate) => candidate.assignedTo, [])
      });

      if (changedFields === null) {
        return;
      }

      if ("ConcernArea" in changedFields) {
        const confirmed = await this.confirmOriginChange(card, String(changedFields["ConcernArea"]));
        if (!confirmed) {
          this.setStatus("Change cancelled.");
          return;
        }
      }

      this.setStatus("Saving...");
      const updated = await tryUpdateCardFields(this.currentBoardId, card, changedFields);
      if (updated === null) {
        throw new Error("The card '" + card.origin + " #" + card.originRelatedId + "' was not found (anymore).");
      }
      await this.loadCards();
      this.setStatus("Updated: " + Object.keys(changedFields).join(", "));
    } catch (error) {
      this.reportError(error);
    }
  }

  /**
   * Translates a drop into field updates: every column level and the group of the target zone stand for
   * a dimension value (status, area, assignee, origin), the position stands for the priority.
   * Moving a card into another origin (source) requires a confirmation and an enabled backend transition.
   */
  private async moveCard(card: WorkItem, target: DropTarget): Promise<void> {
    const board = this.currentBoard;
    const changedFields: Record<string, unknown> = {};
    const pending: WorkItem = Object.assign({}, card);

    try {
      for (const assignment of target.assignments) {
        applyAssignment(pending, assignment, board);
      }

      for (const assignment of target.assignments) {
        const checkable = assignment.dimension !== "none" && assignment.dimension !== "origin" && !assignment.subtree;
        if (checkable && getDimensionValue(pending, assignment.dimension) !== assignment.value) {
          throw new Error("The target contradicts itself: '" + DimensionLabels[assignment.dimension] + "' cannot be '" + assignment.value + "' here.");
        }
      }

      if (pending.progressState !== card.progressState) {
        changedFields["ProgressState"] = pending.progressState;
      }
      if (normalizeArea(pending.concernArea) !== normalizeArea(card.concernArea)) {
        changedFields["ConcernArea"] = pending.concernArea;
      }
      if (pending.assignedTo !== card.assignedTo) {
        changedFields["AssignedTo"] = pending.assignedTo;
      }

      if ("ConcernArea" in changedFields) {
        const confirmed = await this.confirmOriginChange(card, pending.concernArea);
        if (!confirmed) {
          this.render();
          this.setStatus("Move cancelled.");
          return;
        }
      }

      const priority = calculatePriority(target);
      if (priority !== null && priority !== Number(card.manualReorderedPriority)) {
        changedFields["ManualReorderedPriority"] = priority;
      }

      if (Object.keys(changedFields).length === 0) {
        return;
      }

      this.setStatus("Saving...");
      const updated = await tryUpdateCardFields(this.currentBoardId, card, changedFields);
      if (updated === null) {
        throw new Error("The card '" + card.origin + " #" + card.originRelatedId + "' was not found (anymore).");
      }
      await this.loadCards();
      this.setStatus("Updated: " + Object.keys(changedFields).join(", "));
    } catch (error) {
      this.reportError(error);
      this.render();
    }
  }

  private setStatus(text: string): void {
    this.statusElement.textContent = text;
    this.statusElement.classList.remove("error");
  }

  private reportError(error: unknown): void {
    const message = error instanceof Error ? error.message : String(error);
    this.statusElement.textContent = "Error: " + message;
    this.statusElement.classList.add("error");
  }
}

let appRoot = document.getElementById("app");
if (appRoot === null) {
  appRoot = document.createElement("div");
  appRoot.id = "app";
  document.body.appendChild(appRoot);
}

new BoardApp(appRoot).start();
