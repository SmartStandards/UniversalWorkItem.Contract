import "./app.css";
import { getBoards, getCards } from "./boardApi";
import { BoardDescriptor, WorkItem } from "./model";
import { renderBoard } from "./boardRenderer";
import {
  Dimension,
  DimensionLabels,
  GroupingMode,
  GroupingModeLabels,
  ViewState,
  createDefaultViewState
} from "./viewState";

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

function createLabeled(labelText: string, control: HTMLElement): HTMLElement {
  const label = document.createElement("label");
  label.className = "toolbar-field";
  label.appendChild(document.createTextNode(labelText));
  label.appendChild(control);
  return label;
}

class BoardApp {
  private boards: BoardDescriptor[] = [];
  private cards: WorkItem[] = [];
  private readonly viewStates = new Map<string, ViewState>();
  private loadSequence = 0;

  private readonly boardSelector = document.createElement("select");
  private readonly readOnlyBadge = document.createElement("span");
  private readonly secondLevelSelector = createSelect<Dimension>(DimensionLabels);
  private readonly groupBySelector = createSelect<Dimension>(DimensionLabels);
  private readonly groupingModeSelector = createSelect<GroupingMode>(GroupingModeLabels);
  private readonly reloadButton = document.createElement("button");
  private readonly statusElement = document.createElement("span");
  private readonly boardElement = document.createElement("main");

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
    this.reloadButton.textContent = "Reload";
    this.statusElement.className = "status";
    this.boardElement.className = "board";

    toolbar.appendChild(brand);
    toolbar.appendChild(this.boardSelector);
    toolbar.appendChild(this.readOnlyBadge);
    toolbar.appendChild(createLabeled("Columns:", this.secondLevelSelector));
    toolbar.appendChild(createLabeled("Group by:", this.groupBySelector));
    toolbar.appendChild(createLabeled("Grouping:", this.groupingModeSelector));
    toolbar.appendChild(this.reloadButton);
    toolbar.appendChild(this.statusElement);

    root.replaceChildren(toolbar, this.boardElement);

    this.boardSelector.addEventListener("change", () => this.loadCards());
    this.reloadButton.addEventListener("click", () => this.loadCards());
    this.secondLevelSelector.addEventListener("change", () => {
      this.currentView.secondLevel = this.secondLevelSelector.value as Dimension;
      this.render();
    });
    this.groupBySelector.addEventListener("change", () => {
      this.currentView.groupBy = this.groupBySelector.value as Dimension;
      this.render();
    });
    this.groupingModeSelector.addEventListener("change", () => {
      this.currentView.groupingMode = this.groupingModeSelector.value as GroupingMode;
      this.render();
    });
  }

  private get currentBoardId(): string {
    return this.boardSelector.value;
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
    const board = this.boards.find((candidate) => candidate.boardId === this.currentBoardId);
    this.readOnlyBadge.hidden = !(board && board.isReadOnly);

    const view = this.currentView;
    this.secondLevelSelector.value = view.secondLevel;
    this.groupBySelector.value = view.groupBy;
    this.groupingModeSelector.value = view.groupingMode;
  }

  private render(): void {
    renderBoard(this.boardElement, this.cards, this.currentView, () => this.render());
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
