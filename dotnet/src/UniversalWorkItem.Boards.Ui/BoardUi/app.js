(function() {
	// src/ujmwClient.ts
	var UjmwFaultError = class extends Error {
		constructor(message) {
			super(message);
			this.name = "UjmwFaultError";
		}
	};
	function toCamelCase(name) {
		if (name.length === 0) return name;
		return name.charAt(0).toLowerCase() + name.substring(1);
	}
	/**
	* Normalizes all object keys to camelCase (the host serializer may use PascalCase or camelCase).
	*/
	function normalizeKeys(value) {
		if (Array.isArray(value)) return value.map((item) => normalizeKeys(item));
		if (value !== null && typeof value === "object") {
			const result = {};
			for (const [key, inner] of Object.entries(value)) result[toCamelCase(key)] = normalizeKeys(inner);
			return result;
		}
		return value;
	}
	async function invokeUjmw(serviceUrl, methodName, requestDto) {
		const response = await fetch(serviceUrl + methodName, {
			method: "POST",
			headers: { "Content-Type": "application/json" },
			body: JSON.stringify(requestDto)
		});
		const text = await response.text();
		let json = null;
		if (text.length > 0) try {
			json = normalizeKeys(JSON.parse(text));
		} catch {
			throw new Error(`HTTP ${response.status}: ${text.substring(0, 500)}`);
		}
		const fault = json !== null ? json["fault"] : null;
		if (typeof fault === "string" && fault.length > 0) throw new UjmwFaultError(fault);
		if (!response.ok) throw new Error(`HTTP ${response.status}: ${text.substring(0, 500)}`);
		if (json === null) return null;
		return json["return"];
	}
	// src/boardApi.ts
	var managementServiceUrl = "boardApi/management/";
	function getWorkItemServiceUrl(boardId) {
		return "boardApi/workItems/" + encodeURIComponent(boardId) + "/";
	}
	/**
	* IBoardManagementService.GetBoards()
	*/
	async function getBoards() {
		return await invokeUjmw(managementServiceUrl, "GetBoards", {}) || [];
	}
	/**
	* IWorkItemRepository.GetEntities() of the given board, ordered by the global priority (DESC).
	*/
	async function getCards(boardId, limit = 1e3) {
		return await invokeUjmw(getWorkItemServiceUrl(boardId), "GetEntities", {
			filter: {
				matchAll: true,
				negate: false,
				predicates: [],
				subTree: []
			},
			sortedBy: ["^ManualReorderedPriority"],
			limit,
			skip: 0
		}) || [];
	}
	// src/model.ts
	/**
	* Well-known root states of 'WorkItem.ProgressState' in board order.
	*/
	var RootStates = [
		"New",
		"Suspended",
		"Working",
		"Done",
		"Discontinued"
	];
	/**
	* 'Working.Implementation' -> 'Working'
	*/
	function getRootState(progressState) {
		if (!progressState) return "New";
		const separatorIndex = progressState.indexOf(".");
		if (separatorIndex < 0) return progressState;
		return progressState.substring(0, separatorIndex);
	}
	/**
	* 'Working.Implementation' -> 'Implementation' (the UI displays only the sub-state name).
	*/
	function getSubStateLabel(progressState) {
		if (!progressState) return "New";
		const separatorIndex = progressState.lastIndexOf(".");
		if (separatorIndex < 0) return progressState;
		return progressState.substring(separatorIndex + 1);
	}
	/**
	* '/repo-alpha/ui/forms' -> '/repo-alpha' (MVP rule D14: the root segment represents the source repository).
	*/
	function getAreaRoot(area) {
		const trimmed = (area || "").replace(/^\/+|\/+$/g, "");
		if (trimmed.length === 0) return "/";
		const separatorIndex = trimmed.indexOf("/");
		if (separatorIndex < 0) return "/" + trimmed;
		return "/" + trimmed.substring(0, separatorIndex);
	}
	// src/viewState.ts
	var DimensionLabels = {
		none: "(none)",
		areaRoot: "Area root (source)",
		area: "Area (full path)",
		assignedTo: "Assigned to",
		origin: "Origin"
	};
	var GroupingModeLabels = {
		crossColumn: "Cross-column rows",
		withinColumn: "Within column"
	};
	function createDefaultViewState() {
		return {
			secondLevel: "none",
			groupBy: "none",
			groupingMode: "crossColumn",
			collapsedColumns: /* @__PURE__ */ new Set(),
			collapsedGroups: /* @__PURE__ */ new Set()
		};
	}
	function getDimensionValue(card, dimension) {
		switch (dimension) {
			case "areaRoot": return getAreaRoot(card.concernArea);
			case "area": return card.concernArea || "/";
			case "assignedTo": return card.assignedTo || "(unassigned)";
			case "origin": return card.origin || "(none)";
			default: return "";
		}
	}
	function getDistinctValues(cards, dimension) {
		const values = /* @__PURE__ */ new Set();
		for (const card of cards) values.add(getDimensionValue(card, dimension));
		return Array.from(values).sort((a, b) => a.localeCompare(b));
	}
	function toggle(set, key) {
		if (set.has(key)) set.delete(key);
		else set.add(key);
	}
	// src/boardRenderer.ts
	var collapsedColumnWidth = "36px";
	var expandedColumnWidth = "minmax(220px, 280px)";
	function createElement(tagName, className, text) {
		const element = document.createElement(tagName);
		element.className = className;
		if (text !== void 0) element.textContent = text;
		return element;
	}
	function getAllStates(cards) {
		const states = RootStates.slice();
		for (const card of cards) {
			const rootState = getRootState(card.progressState);
			if (states.indexOf(rootState) < 0) states.push(rootState);
		}
		return states;
	}
	function buildStateColumns(cards, view) {
		const result = [];
		for (const state of getAllStates(cards)) {
			const stateCards = cards.filter((card) => getRootState(card.progressState) === state);
			const stateCollapsed = view.collapsedColumns.has(state);
			const leaves = [];
			if (view.secondLevel === "none" || stateCollapsed || stateCards.length === 0) leaves.push({
				key: state,
				label: null,
				collapsed: stateCollapsed,
				cards: stateCards
			});
			else for (const value of getDistinctValues(stateCards, view.secondLevel)) {
				const key = state + "|" + value;
				leaves.push({
					key,
					label: value,
					collapsed: view.collapsedColumns.has(key),
					cards: stateCards.filter((card) => getDimensionValue(card, view.secondLevel) === value)
				});
			}
			result.push({
				state,
				collapsed: stateCollapsed,
				cards: stateCards,
				leaves
			});
		}
		return result;
	}
	function createCard(card) {
		const rootState = getRootState(card.progressState);
		const element = createElement("article", "card");
		element.title = "Priority: " + String(card.manualReorderedPriority);
		element.appendChild(createElement("div", "card-title", card.title));
		const meta = createElement("div", "card-meta");
		meta.appendChild(createElement("span", "state-badge state-" + rootState.toLowerCase(), getSubStateLabel(card.progressState)));
		meta.appendChild(createElement("span", "card-id", card.origin + " #" + card.originRelatedId));
		element.appendChild(meta);
		element.appendChild(createElement("div", "card-area", card.concernArea || "/"));
		if (card.assignedTo) element.appendChild(createElement("div", "card-assignee", card.assignedTo));
		if (card.tags && card.tags.length > 0) {
			const tags = createElement("div", "card-tags");
			for (const tag of card.tags) tags.appendChild(createElement("span", "tag", tag));
			element.appendChild(tags);
		}
		return element;
	}
	function appendCards(container, cards) {
		for (const card of cards) container.appendChild(createCard(card));
	}
	function createCell(leaf, column, row) {
		const cell = createElement("div", leaf.collapsed ? "cell collapsed" : "cell");
		cell.style.gridColumn = String(column);
		cell.style.gridRow = String(row);
		return cell;
	}
	function renderWithinColumnGroups(cell, leaf, view, onViewChanged) {
		for (const group of getDistinctValues(leaf.cards, view.groupBy)) {
			const groupKey = "within|" + leaf.key + "|" + group;
			const groupCards = leaf.cards.filter((card) => getDimensionValue(card, view.groupBy) === group);
			const collapsed = view.collapsedGroups.has(groupKey);
			const header = createElement("div", collapsed ? "group-header within collapsed" : "group-header within", (collapsed ? "▸ " : "▾ ") + group + " (" + groupCards.length + (collapsed ? " hidden)" : ")"));
			header.addEventListener("click", () => {
				toggle(view.collapsedGroups, groupKey);
				onViewChanged();
			});
			cell.appendChild(header);
			if (!collapsed) appendCards(cell, groupCards);
		}
	}
	/**
	* Renders the board as one CSS grid: header rows (state level + optional second column level),
	* followed by either one card row or (cross-column grouping) one header row + card row per group.
	* All columns start at the same vertical position; cards are ordered by the global priority (DESC).
	*/
	function renderBoard(host, cards, view, onViewChanged) {
		const sortedCards = cards.slice().sort((a, b) => (Number(b.manualReorderedPriority) || 0) - (Number(a.manualReorderedPriority) || 0));
		const stateColumns = buildStateColumns(sortedCards, view);
		const leaves = [];
		for (const stateColumn of stateColumns) leaves.push(...stateColumn.leaves);
		const hasSecondLevel = view.secondLevel !== "none";
		const grid = createElement("div", hasSecondLevel ? "board-grid two-header-rows" : "board-grid");
		grid.style.gridTemplateColumns = leaves.map((leaf) => leaf.collapsed ? collapsedColumnWidth : expandedColumnWidth).join(" ");
		let column = 1;
		for (const stateColumn of stateColumns) {
			const spansBothRows = hasSecondLevel && stateColumn.leaves.length === 1 && stateColumn.leaves[0].label === null;
			const header = createElement("div", stateColumn.collapsed ? "state-header collapsed" : "state-header");
			header.style.gridColumn = column + " / span " + stateColumn.leaves.length;
			header.style.gridRow = spansBothRows ? "1 / span 2" : "1";
			header.title = (stateColumn.collapsed ? "Expand " : "Collapse ") + stateColumn.state;
			if (stateColumn.collapsed) header.textContent = String(stateColumn.cards.length);
			else header.textContent = stateColumn.state + " (" + stateColumn.cards.length + ")";
			header.addEventListener("click", () => {
				toggle(view.collapsedColumns, stateColumn.state);
				onViewChanged();
			});
			grid.appendChild(header);
			if (hasSecondLevel && !spansBothRows) stateColumn.leaves.forEach((leaf, index) => {
				const leafHeader = createElement("div", leaf.collapsed ? "leaf-header collapsed" : "leaf-header");
				leafHeader.style.gridColumn = String(column + index);
				leafHeader.style.gridRow = "2";
				leafHeader.title = (leaf.collapsed ? "Expand " : "Collapse ") + String(leaf.label);
				if (leaf.collapsed) leafHeader.textContent = String(leaf.cards.length);
				else leafHeader.textContent = String(leaf.label) + " (" + leaf.cards.length + ")";
				leafHeader.addEventListener("click", () => {
					toggle(view.collapsedColumns, leaf.key);
					onViewChanged();
				});
				grid.appendChild(leafHeader);
			});
			column += stateColumn.leaves.length;
		}
		let row = hasSecondLevel ? 3 : 2;
		if (view.groupBy !== "none" && view.groupingMode === "crossColumn") for (const group of getDistinctValues(sortedCards, view.groupBy)) {
			const groupKey = "cross|" + group;
			const groupCards = sortedCards.filter((card) => getDimensionValue(card, view.groupBy) === group);
			const collapsed = view.collapsedGroups.has(groupKey);
			const groupHeader = createElement("div", collapsed ? "group-header cross collapsed" : "group-header cross", (collapsed ? "▸ " : "▾ ") + group + " (" + groupCards.length + (collapsed ? " hidden)" : ")"));
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
					if (!leaf.collapsed) appendCards(cell, leaf.cards.filter((card) => getDimensionValue(card, view.groupBy) === group));
					grid.appendChild(cell);
				});
				row++;
			}
		}
		else leaves.forEach((leaf, index) => {
			const cell = createCell(leaf, index + 1, row);
			if (leaf.collapsed) cell.appendChild(createElement("div", "collapsed-label", leaf.label === null ? leaf.key : leaf.key.replace("|", " / ")));
			else if (view.groupBy !== "none") renderWithinColumnGroups(cell, leaf, view, onViewChanged);
			else appendCards(cell, leaf.cards);
			grid.appendChild(cell);
		});
		host.replaceChildren(grid);
	}
	// src/main.ts
	function createSelect(labels) {
		const select = document.createElement("select");
		for (const value of Object.keys(labels)) {
			const option = document.createElement("option");
			option.value = value;
			option.textContent = labels[value];
			select.appendChild(option);
		}
		return select;
	}
	function createLabeled(labelText, control) {
		const label = document.createElement("label");
		label.className = "toolbar-field";
		label.appendChild(document.createTextNode(labelText));
		label.appendChild(control);
		return label;
	}
	var BoardApp = class {
		constructor(root) {
			this.boards = [];
			this.cards = [];
			this.viewStates = /* @__PURE__ */ new Map();
			this.loadSequence = 0;
			this.boardSelector = document.createElement("select");
			this.readOnlyBadge = document.createElement("span");
			this.secondLevelSelector = createSelect(DimensionLabels);
			this.groupBySelector = createSelect(DimensionLabels);
			this.groupingModeSelector = createSelect(GroupingModeLabels);
			this.reloadButton = document.createElement("button");
			this.statusElement = document.createElement("span");
			this.boardElement = document.createElement("main");
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
				this.currentView.secondLevel = this.secondLevelSelector.value;
				this.render();
			});
			this.groupBySelector.addEventListener("change", () => {
				this.currentView.groupBy = this.groupBySelector.value;
				this.render();
			});
			this.groupingModeSelector.addEventListener("change", () => {
				this.currentView.groupingMode = this.groupingModeSelector.value;
				this.render();
			});
		}
		get currentBoardId() {
			return this.boardSelector.value;
		}
		get currentView() {
			let view = this.viewStates.get(this.currentBoardId);
			if (view === void 0) {
				view = createDefaultViewState();
				this.viewStates.set(this.currentBoardId, view);
			}
			return view;
		}
		start() {
			this.loadBoards();
		}
		async loadBoards() {
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
		async loadCards() {
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
				if (sequence !== this.loadSequence) return;
				this.cards = cards;
				this.render();
				this.setStatus(cards.length + " cards");
			} catch (error) {
				if (sequence === this.loadSequence) this.reportError(error);
			}
		}
		syncViewControls() {
			const board = this.boards.find((candidate) => candidate.boardId === this.currentBoardId);
			this.readOnlyBadge.hidden = !(board && board.isReadOnly);
			const view = this.currentView;
			this.secondLevelSelector.value = view.secondLevel;
			this.groupBySelector.value = view.groupBy;
			this.groupingModeSelector.value = view.groupingMode;
		}
		render() {
			renderBoard(this.boardElement, this.cards, this.currentView, () => this.render());
		}
		setStatus(text) {
			this.statusElement.textContent = text;
			this.statusElement.classList.remove("error");
		}
		reportError(error) {
			const message = error instanceof Error ? error.message : String(error);
			this.statusElement.textContent = "Error: " + message;
			this.statusElement.classList.add("error");
		}
	};
	var appRoot = document.getElementById("app");
	if (appRoot === null) {
		appRoot = document.createElement("div");
		appRoot.id = "app";
		document.body.appendChild(appRoot);
	}
	new BoardApp(appRoot).start();
})();
