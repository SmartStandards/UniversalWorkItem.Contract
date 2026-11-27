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
	var configurationServiceUrl = "boardApi/configuration/";
	async function getConnectorConfigurations() {
		return await invokeUjmw(configurationServiceUrl, "GetConnectorConfigurations", {}) || [];
	}
	/**
	* Returns an empty string on success, otherwise the error message.
	*/
	async function testConnectorConfiguration(configuration) {
		return await invokeUjmw(configurationServiceUrl, "TestConnectorConfiguration", { configuration }) || "";
	}
	/**
	* Returns the board id of the saved configuration.
	*/
	async function saveConnectorConfiguration(configuration) {
		return await invokeUjmw(configurationServiceUrl, "SaveConnectorConfiguration", { configuration }) || "";
	}
	async function deleteConnectorConfiguration(boardId) {
		return await invokeUjmw(configurationServiceUrl, "DeleteConnectorConfiguration", { boardId }) === true;
	}
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
	/**
	* IWorkItemRepository.TryUpdateEntityFields() of the given board.
	* The key fields (Origin, OriginRelatedId) are added automatically; 'changedFields' uses the PascalCase field names.
	* Returns the fields which were actually updated (null, if the card does not exist anymore).
	*/
	async function tryUpdateCardFields(boardId, card, changedFields) {
		const fields = {
			Origin: card.origin,
			OriginRelatedId: card.originRelatedId
		};
		for (const [fieldName, value] of Object.entries(changedFields)) fields[fieldName] = value;
		return await invokeUjmw(getWorkItemServiceUrl(boardId), "TryUpdateEntityFields", { fields });
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
	var DimensionLabels = {
		none: "(none)",
		status: "Status",
		areaRoot: "Area root (source)",
		area: "Area (tree)",
		assignedTo: "Assigned to",
		origin: "Origin"
	};
	/** The first column level always needs a dimension ('none' is not offered). */
	var ColumnDimensionLabels = {
		status: "Status",
		areaRoot: "Area root (source)",
		area: "Area (tree)",
		assignedTo: "Assigned to",
		origin: "Origin"
	};
	function createDefaultViewState() {
		return {
			columns: "status",
			secondLevel: "none",
			groupBy: "none",
			groupingMode: "crossColumn",
			collapsedColumns: /* @__PURE__ */ new Set(),
			toggledSplits: /* @__PURE__ */ new Set(),
			expandedAreas: /* @__PURE__ */ new Set(),
			collapsedGroups: /* @__PURE__ */ new Set()
		};
	}
	/**
	* '/a//b/' -> '/a/b', '' -> '/'
	*/
	function normalizeArea(area) {
		return "/" + (area || "").split("/").filter((segment) => segment.length > 0).join("/");
	}
	/**
	* True if 'area' equals 'path' or is located below it.
	*/
	function isAreaWithin(area, path) {
		const normalized = normalizeArea(area);
		if (path === "/") return true;
		return normalized === path || normalized.indexOf(path + "/") === 0;
	}
	function getDimensionValue(card, dimension) {
		switch (dimension) {
			case "status": return getRootState(card.progressState);
			case "areaRoot": return getAreaRoot(card.concernArea);
			case "area": return normalizeArea(card.concernArea);
			case "assignedTo": return card.assignedTo || "(unassigned)";
			case "origin": return card.origin || "(none)";
			default: return "";
		}
	}
	function getDistinctValues(cards, dimension) {
		const values = /* @__PURE__ */ new Set();
		for (const card of cards) values.add(getDimensionValue(card, dimension));
		const result = Array.from(values);
		if (dimension === "status") return result.sort((a, b) => getStateOrder(a) - getStateOrder(b));
		return result.sort((a, b) => a.localeCompare(b));
	}
	/**
	* All well-known root states (also empty ones, so cards can be dropped there) plus unknown ones.
	*/
	function getAllStateValues(cards) {
		const states = RootStates.slice();
		for (const value of getDistinctValues(cards, "status")) if (states.indexOf(value) < 0) states.push(value);
		return states;
	}
	function getStateOrder(state) {
		const index = RootStates.indexOf(state);
		if (index < 0) return RootStates.length;
		return index;
	}
	function toggle(set, key) {
		if (set.has(key)) set.delete(key);
		else set.add(key);
	}
	// src/columns.ts
	function createNode(context, dimensionIndex, parent, key, label, assignment, cards, depth, buildSubAreas) {
		const view = context.view;
		const assignments = [];
		let path = label;
		if (parent !== null) {
			assignments.push(...parent.assignments);
			path = parent.path + " / " + label;
		}
		assignments.push(assignment);
		const node = {
			key,
			label,
			path,
			dimension: assignment.dimension,
			depth,
			cards,
			assignments,
			children: [],
			collapsed: view.collapsedColumns.has(key),
			merged: null,
			areaExpanded: null
		};
		if (node.collapsed) return node;
		if (buildSubAreas !== null) {
			node.areaExpanded = view.expandedAreas.has(key);
			if (node.areaExpanded) {
				node.children = buildSubAreas(node);
				return node;
			}
		}
		const nextIndex = dimensionIndex + 1;
		if (nextIndex < context.dimensions.length && cards.length > 0) {
			const split = context.dimensions[nextIndex] !== "area" !== view.toggledSplits.has(key);
			node.merged = !split;
			if (split) node.children = buildLevel(context, nextIndex, node, cards, depth + 1);
		}
		return node;
	}
	function buildAreaLevel(context, dimensionIndex, parent, cards, parentPath, depth) {
		const keyPrefix = parent === null ? "" : parent.key + "|";
		const direct = [];
		const childCards = /* @__PURE__ */ new Map();
		for (const card of cards) {
			const area = normalizeArea(card.concernArea);
			if (area === parentPath) {
				direct.push(card);
				continue;
			}
			const segment = (parentPath === "/" ? area.substring(1) : area.substring(parentPath.length + 1)).split("/")[0];
			const childPath = (parentPath === "/" ? "" : parentPath) + "/" + segment;
			let list = childCards.get(childPath);
			if (list === void 0) {
				list = [];
				childCards.set(childPath, list);
			}
			list.push(card);
		}
		const result = [];
		if (direct.length > 0) {
			const label = parentPath === "/" ? "/" : "(direct)";
			result.push(createNode(context, dimensionIndex, parent, keyPrefix + "area=" + parentPath + "!", label, {
				dimension: "area",
				value: parentPath,
				subtree: false
			}, direct, depth, null));
		}
		for (const childPath of Array.from(childCards.keys()).sort((a, b) => a.localeCompare(b))) {
			const nodeCards = childCards.get(childPath);
			const hasSubAreas = nodeCards.some((card) => normalizeArea(card.concernArea) !== childPath);
			const label = parentPath === "/" ? childPath : childPath.substring(childPath.lastIndexOf("/") + 1);
			const key = keyPrefix + "area=" + childPath;
			let buildSubAreas = null;
			if (hasSubAreas) buildSubAreas = (self) => buildAreaLevel(context, dimensionIndex, self, nodeCards, childPath, depth + 1);
			result.push(createNode(context, dimensionIndex, parent, key, label, {
				dimension: "area",
				value: childPath,
				subtree: true
			}, nodeCards, depth, buildSubAreas));
		}
		return result;
	}
	function buildLevel(context, dimensionIndex, parent, cards, depth) {
		const dimension = context.dimensions[dimensionIndex];
		if (dimension === "area") return buildAreaLevel(context, dimensionIndex, parent, cards, "/", depth);
		let values;
		if (dimension === "status" && dimensionIndex === 0) values = getAllStateValues(cards);
		else values = getDistinctValues(cards, dimension);
		const keyPrefix = parent === null ? "" : parent.key + "|";
		return values.map((value) => createNode(context, dimensionIndex, parent, keyPrefix + dimension + "=" + value, value, {
			dimension,
			value,
			subtree: false
		}, cards.filter((card) => getDimensionValue(card, dimension) === value), depth, null));
	}
	/**
	* Builds the column tree for the column dimensions of the view (first level + optional second level).
	*/
	function buildColumnTree(cards, view) {
		const dimensions = [view.columns];
		if (view.secondLevel !== "none" && view.secondLevel !== view.columns) dimensions.push(view.secondLevel);
		return buildLevel({
			dimensions,
			view
		}, 0, null, cards, 0);
	}
	function collectLeaves(nodes, result) {
		for (const node of nodes) if (node.children.length === 0) result.push(node);
		else collectLeaves(node.children, result);
		return result;
	}
	function countLeaves(node) {
		if (node.children.length === 0) return 1;
		let count = 0;
		for (const child of node.children) count += countLeaves(child);
		return count;
	}
	function getMaxDepth(nodes) {
		let max = 0;
		for (const node of nodes) {
			let depth = node.depth + 1;
			if (node.children.length > 0) depth = getMaxDepth(node.children);
			if (depth > max) max = depth;
		}
		return max;
	}
	/**
	* Assignment of a group value (groups are flat, so an area group stands for exactly that area).
	*/
	function createGroupAssignment(dimension, value) {
		return {
			dimension,
			value,
			subtree: false
		};
	}
	// src/boardRenderer.ts
	var collapsedColumnWidth = "44px";
	var expandedColumnWidth = "minmax(220px, 280px)";
	var headerRowHeight = 36;
	var svgNamespace = "http://www.w3.org/2000/svg";
	var draggedCard = null;
	var draggedElement = null;
	var dropIndicator = createElement$1("div", "drop-indicator");
	function createElement$1(tagName, className, text) {
		const element = document.createElement(tagName);
		element.className = className;
		if (text !== void 0) element.textContent = text;
		return element;
	}
	/**
	* Small line icons (16x16, stroke = currentColor).
	* 'merge': two columns pushed together, 'split': two columns pushed apart, 'drill': tree branch.
	*/
	function createIcon(kind) {
		const svg = document.createElementNS(svgNamespace, "svg");
		svg.setAttribute("viewBox", "0 0 16 16");
		svg.setAttribute("width", "16");
		svg.setAttribute("height", "16");
		svg.setAttribute("aria-hidden", "true");
		svg.setAttribute("class", "icon");
		const paths = [];
		switch (kind) {
			case "merge":
				paths.push("M2 3v10", "M14 3v10", "M4.5 8h2.5", "M5.5 6l2 2-2 2", "M11.5 8H9", "M10.5 6l-2 2 2 2");
				break;
			case "split":
				paths.push("M8 3v10", "M2 8h4", "M4 6L2 8l2 2", "M14 8h-4", "M12 6l2 2-2 2");
				break;
			case "drillIn":
				paths.push("M4 3v10", "M4 7h5", "M4 12h5", "M11 7h3", "M11 12h3");
				break;
			case "drillOut": paths.push("M4 3v10", "M4 8h8");
		}
		for (const definition of paths) {
			const path = document.createElementNS(svgNamespace, "path");
			path.setAttribute("d", definition);
			svg.appendChild(path);
		}
		return svg;
	}
	function createIconButton(className, icon, title, onClick) {
		const button = createElement$1("button", className);
		button.type = "button";
		button.title = title;
		button.setAttribute("aria-label", title);
		button.appendChild(icon);
		button.addEventListener("click", (event) => {
			event.stopPropagation();
			onClick();
		});
		return button;
	}
	function removeDropIndicator() {
		if (dropIndicator.parentElement !== null) dropIndicator.parentElement.removeChild(dropIndicator);
	}
	function clearDropHighlights() {
		removeDropIndicator();
		for (const element of Array.from(document.querySelectorAll(".drop-target"))) element.classList.remove("drop-target");
	}
	function getCardElements(container) {
		const result = [];
		for (const child of Array.from(container.children)) if (child.classList.contains("card") && child !== draggedElement) result.push(child);
		return result;
	}
	/**
	* Places the drop indicator at the insert position (based on the mouse position) and returns the insert index.
	*/
	function placeDropIndicator(container, clientY) {
		const cardElements = getCardElements(container);
		let index = 0;
		for (const cardElement of cardElements) {
			const rect = cardElement.getBoundingClientRect();
			if (clientY < rect.top + rect.height / 2) {
				if (dropIndicator.nextSibling !== cardElement) container.insertBefore(dropIndicator, cardElement);
				return index;
			}
			index++;
		}
		if (cardElements.length > 0) {
			const lastCard = cardElements[cardElements.length - 1];
			if (lastCard.nextSibling !== dropIndicator) lastCard.after(dropIndicator);
		} else if (dropIndicator.parentElement !== container) container.appendChild(dropIndicator);
		return index;
	}
	/**
	* Turns 'zone' into a drop target. Cards are expected as direct children of 'cardContainer'.
	*/
	function makeDropZone(zone, cardContainer, context, callbacks) {
		const onCardDropped = callbacks.onCardDropped;
		if (onCardDropped === null) return;
		zone.addEventListener("dragover", (event) => {
			if (draggedCard === null) return;
			event.preventDefault();
			event.stopPropagation();
			if (event.dataTransfer !== null) event.dataTransfer.dropEffect = "move";
			if (!zone.classList.contains("drop-target")) {
				clearDropHighlights();
				zone.classList.add("drop-target");
			}
			if (context.cards !== null) placeDropIndicator(cardContainer, event.clientY);
		});
		zone.addEventListener("dragleave", (event) => {
			const related = event.relatedTarget;
			if (related === null || !zone.contains(related)) {
				zone.classList.remove("drop-target");
				if (dropIndicator.parentElement === cardContainer) removeDropIndicator();
			}
		});
		zone.addEventListener("drop", (event) => {
			if (draggedCard === null) return;
			event.preventDefault();
			event.stopPropagation();
			const card = draggedCard;
			let orderedCards = null;
			let insertIndex = 0;
			if (context.cards !== null) {
				insertIndex = placeDropIndicator(cardContainer, event.clientY);
				orderedCards = context.cards.filter((candidate) => candidate !== card);
			}
			clearDropHighlights();
			onCardDropped(card, {
				assignments: context.assignments,
				orderedCards,
				insertIndex
			});
		});
	}
	function createCard(card, callbacks) {
		const rootState = getRootState(card.progressState);
		const element = createElement$1("article", "card state-border-" + rootState.toLowerCase());
		element.title = "Priority: " + String(card.manualReorderedPriority);
		element.appendChild(createElement$1("div", "card-title", card.title));
		const meta = createElement$1("div", "card-meta");
		meta.appendChild(createElement$1("span", "state-badge state-" + rootState.toLowerCase(), getSubStateLabel(card.progressState)));
		meta.appendChild(createElement$1("span", "card-id", card.origin + " #" + card.originRelatedId));
		element.appendChild(meta);
		element.appendChild(createElement$1("div", "card-area", card.concernArea || "/"));
		if (card.assignedTo) element.appendChild(createElement$1("div", "card-assignee", card.assignedTo));
		if (card.tags && card.tags.length > 0) {
			const tags = createElement$1("div", "card-tags");
			for (const tag of card.tags) tags.appendChild(createElement$1("span", "tag", tag));
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
	function appendCards(container, cards, callbacks) {
		for (const card of cards) container.appendChild(createCard(card, callbacks));
	}
	function createCell(leaf, column, row) {
		const cell = createElement$1("div", leaf.collapsed ? "cell collapsed" : "cell");
		cell.style.gridColumn = String(column);
		cell.style.gridRow = String(row);
		return cell;
	}
	function renderWithinColumnGroups(cell, leaf, view, callbacks) {
		for (const group of getDistinctValues(leaf.cards, view.groupBy)) {
			const groupKey = "within|" + leaf.key + "|" + group;
			const groupCards = leaf.cards.filter((card) => getDimensionValue(card, view.groupBy) === group);
			const collapsed = view.collapsedGroups.has(groupKey);
			const section = createElement$1("section", "group-section");
			const header = createElement$1("div", collapsed ? "group-header within collapsed" : "group-header within");
			header.appendChild(createElement$1("span", "group-chevron", collapsed ? "▸" : "▾"));
			header.appendChild(createElement$1("span", "group-title", group));
			header.appendChild(createElement$1("span", "count-pill", String(groupCards.length)));
			header.addEventListener("click", () => {
				toggle(view.collapsedGroups, groupKey);
				callbacks.onViewChanged();
			});
			section.appendChild(header);
			const body = createElement$1("div", "group-body");
			if (!collapsed) appendCards(body, groupCards, callbacks);
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
	function createColumnHeader(node, column, maxDepth, view, callbacks) {
		const header = createElement$1("div", node.depth === 0 ? "column-header level-0" : "column-header level-n");
		if (node.dimension === "status") header.classList.add("state-header-" + node.label.toLowerCase());
		if (node.collapsed) header.classList.add("collapsed");
		let rowSpan = 1;
		if (node.children.length === 0) rowSpan = maxDepth - node.depth;
		header.style.gridColumn = column + " / span " + countLeaves(node);
		header.style.gridRow = node.depth + 1 + " / span " + rowSpan;
		header.style.top = node.depth * headerRowHeight + "px";
		header.title = node.path;
		const title = createElement$1("button", "header-toggle");
		title.type = "button";
		title.title = (node.collapsed ? "Expand " : "Collapse ") + node.path;
		if (!node.collapsed) title.appendChild(createElement$1("span", "header-title", node.label));
		title.appendChild(createElement$1("span", "count-pill", String(node.cards.length)));
		title.addEventListener("click", () => {
			toggle(view.collapsedColumns, node.key);
			callbacks.onViewChanged();
		});
		header.appendChild(title);
		if (node.areaExpanded !== null) {
			let drillTitle = "Show the sub-areas of '" + node.label + "' as separate columns";
			if (node.areaExpanded) drillTitle = "Condense the sub-areas of '" + node.label + "' into one column";
			header.appendChild(createIconButton(node.areaExpanded ? "header-icon-button active" : "header-icon-button", createIcon(node.areaExpanded ? "drillOut" : "drillIn"), drillTitle, () => {
				toggle(view.expandedAreas, node.key);
				callbacks.onViewChanged();
			}));
		}
		if (node.merged !== null) {
			let mergeTitle = "Merge the sub-columns of '" + node.label + "' into one column";
			if (node.merged) mergeTitle = "Split '" + node.label + "' into its sub-columns";
			header.appendChild(createIconButton(node.merged ? "header-icon-button active" : "header-icon-button", createIcon(node.merged ? "split" : "merge"), mergeTitle, () => {
				toggle(view.toggledSplits, node.key);
				callbacks.onViewChanged();
			}));
		}
		return header;
	}
	function appendHeaders(grid, nodes, firstColumn, maxDepth, view, callbacks) {
		let column = firstColumn;
		for (const node of nodes) {
			grid.appendChild(createColumnHeader(node, column, maxDepth, view, callbacks));
			if (node.children.length > 0) appendHeaders(grid, node.children, column, maxDepth, view, callbacks);
			column += countLeaves(node);
		}
	}
	/**
	* Renders the board as one CSS grid: one header row per column level,
	* followed by either one card row or (cross-column grouping) one header row + card row per group.
	* All columns start at the same vertical position; cards are ordered by the global priority (DESC).
	*/
	function renderBoard(host, cards, view, callbacks) {
		const sortedCards = cards.slice().sort((a, b) => (Number(b.manualReorderedPriority) || 0) - (Number(a.manualReorderedPriority) || 0));
		const columns = buildColumnTree(sortedCards, view);
		const leaves = collectLeaves(columns, []);
		const maxDepth = Math.max(1, getMaxDepth(columns));
		const grid = createElement$1("div", "board-grid");
		grid.style.gridTemplateColumns = leaves.map((leaf) => leaf.collapsed ? collapsedColumnWidth : expandedColumnWidth).join(" ");
		if (callbacks.onCardDropped === null) grid.classList.add("read-only");
		const headerBackdrop = createElement$1("div", "header-backdrop");
		headerBackdrop.style.gridColumn = "1 / -1";
		headerBackdrop.style.gridRow = "1 / span " + maxDepth;
		grid.appendChild(headerBackdrop);
		appendHeaders(grid, columns, 1, maxDepth, view, callbacks);
		let row = maxDepth + 1;
		if (view.groupBy !== "none" && view.groupingMode === "crossColumn") for (const group of getDistinctValues(sortedCards, view.groupBy)) {
			const groupKey = "cross|" + view.groupBy + "|" + group;
			const groupCards = sortedCards.filter((card) => getDimensionValue(card, view.groupBy) === group);
			const groupAssignment = createGroupAssignment(view.groupBy, group);
			const collapsed = view.collapsedGroups.has(groupKey);
			const groupHeader = createElement$1("div", collapsed ? "group-header cross collapsed" : "group-header cross");
			if (view.groupBy === "status") groupHeader.classList.add("state-header-" + group.toLowerCase());
			groupHeader.appendChild(createElement$1("span", "group-chevron", collapsed ? "▸" : "▾"));
			groupHeader.appendChild(createElement$1("span", "group-title", group));
			groupHeader.appendChild(createElement$1("span", "count-pill", String(groupCards.length)));
			groupHeader.style.gridColumn = "1 / -1";
			groupHeader.style.gridRow = String(row);
			groupHeader.addEventListener("click", () => {
				toggle(view.collapsedGroups, groupKey);
				callbacks.onViewChanged();
			});
			makeDropZone(groupHeader, groupHeader, {
				assignments: [groupAssignment],
				cards: null
			}, callbacks);
			grid.appendChild(groupHeader);
			row++;
			if (!collapsed) {
				leaves.forEach((leaf, index) => {
					const cell = createCell(leaf, index + 1, row);
					const cellCards = leaf.cards.filter((card) => getDimensionValue(card, view.groupBy) === group);
					if (leaf.collapsed) cell.appendChild(createElement$1("div", cellCards.length > 0 ? "collapsed-count" : "collapsed-count empty", String(cellCards.length)));
					else appendCards(cell, cellCards, callbacks);
					makeDropZone(cell, cell, {
						assignments: leaf.assignments.concat([groupAssignment]),
						cards: leaf.collapsed ? null : cellCards
					}, callbacks);
					grid.appendChild(cell);
				});
				row++;
			}
		}
		else leaves.forEach((leaf, index) => {
			const cell = createCell(leaf, index + 1, row);
			const grouped = view.groupBy !== "none";
			if (leaf.collapsed) {
				cell.appendChild(createElement$1("div", "collapsed-count", String(leaf.cards.length)));
				cell.appendChild(createElement$1("div", "collapsed-label", leaf.path));
			} else if (grouped) renderWithinColumnGroups(cell, leaf, view, callbacks);
			else appendCards(cell, leaf.cards, callbacks);
			makeDropZone(cell, cell, {
				assignments: leaf.assignments,
				cards: leaf.collapsed || grouped ? null : leaf.cards
			}, callbacks);
			grid.appendChild(cell);
		});
		host.replaceChildren(grid);
	}
	/**
	* Mobile layout (Trello-like): exactly one (leaf) column per screen width, the columns are changed by a
	* horizontal swipe (native scroll snapping). Cards are opened by a tap; moving is done in the card editor.
	* Returns the number of columns.
	*/
	function renderMobileBoard(host, cards, view, callbacks, activeIndex, onActiveIndexChanged) {
		const leaves = collectLeaves(buildColumnTree(cards.slice().sort((a, b) => (Number(b.manualReorderedPriority) || 0) - (Number(a.manualReorderedPriority) || 0)), view), []);
		const pageIndex = Math.max(0, Math.min(activeIndex, leaves.length - 1));
		const container = createElement$1("div", "mobile-board");
		const tabs = createElement$1("nav", "mobile-tabs");
		const pages = createElement$1("div", "mobile-pages");
		const tabElements = [];
		leaves.forEach((leaf, index) => {
			const tab = createElement$1("button", "mobile-tab");
			tab.type = "button";
			if (leaf.dimension === "status") tab.classList.add("state-header-" + leaf.label.toLowerCase());
			tab.appendChild(createElement$1("span", "mobile-tab-title", leaf.path));
			tab.appendChild(createElement$1("span", "count-pill", String(leaf.cards.length)));
			tab.addEventListener("click", () => {
				pages.scrollTo({
					left: index * pages.clientWidth,
					behavior: "smooth"
				});
			});
			tabs.appendChild(tab);
			tabElements.push(tab);
			const page = createElement$1("section", "mobile-page");
			const header = createElement$1("div", "column-header level-0 mobile-page-header");
			if (leaf.dimension === "status") header.classList.add("state-header-" + leaf.label.toLowerCase());
			header.appendChild(createElement$1("span", "header-title", leaf.path));
			header.appendChild(createElement$1("span", "count-pill", String(leaf.cards.length)));
			page.appendChild(header);
			const cell = createElement$1("div", "cell mobile-cell");
			if (view.groupBy !== "none") renderWithinColumnGroups(cell, leaf, view, callbacks);
			else appendCards(cell, leaf.cards, callbacks);
			if (leaf.cards.length === 0) cell.appendChild(createElement$1("div", "mobile-empty", "No cards"));
			page.appendChild(cell);
			pages.appendChild(page);
		});
		let currentIndex = pageIndex;
		const markActiveTab = (index) => {
			tabElements.forEach((tab, tabIndex) => tab.classList.toggle("active", tabIndex === index));
			const activeTab = tabElements[index];
			if (activeTab !== void 0) tabs.scrollTo({
				left: activeTab.offsetLeft - (tabs.clientWidth - activeTab.offsetWidth) / 2,
				behavior: "smooth"
			});
		};
		pages.addEventListener("scroll", () => {
			if (pages.clientWidth === 0) return;
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
		pages.scrollLeft = pageIndex * pages.clientWidth;
		markActiveTab(pageIndex);
		return leaves.length;
	}
	// src/cardEditor.ts
	var datalistSequence = 0;
	function asText(value) {
		if (value === null || value === void 0) return "";
		return String(value);
	}
	function parseTags(text) {
		return text.split(",").map((tag) => tag.trim()).filter((tag) => tag.length > 0);
	}
	function formatTags(tags) {
		return (tags || []).join(", ");
	}
	var editorFields = [
		{
			fieldName: "Title",
			label: "Title",
			kind: "text",
			suggestions: null,
			getValue: (card) => asText(card.title)
		},
		{
			fieldName: "ProgressState",
			label: "State",
			kind: "text",
			suggestions: [],
			getValue: (card) => asText(card.progressState)
		},
		{
			fieldName: "ConcernArea",
			label: "Area",
			kind: "text",
			suggestions: [],
			getValue: (card) => asText(card.concernArea)
		},
		{
			fieldName: "AssignedTo",
			label: "Assigned to",
			kind: "text",
			suggestions: [],
			getValue: (card) => asText(card.assignedTo)
		},
		{
			fieldName: "Tags",
			label: "Tags (comma separated)",
			kind: "tags",
			suggestions: null,
			getValue: (card) => formatTags(card.tags)
		},
		{
			fieldName: "ProgressNote",
			label: "Progress note",
			kind: "text",
			suggestions: null,
			getValue: (card) => asText(card["progressNote"])
		},
		{
			fieldName: "BodyContent",
			label: "Description",
			kind: "multiline",
			suggestions: null,
			getValue: (card) => asText(card["bodyContent"])
		}
	];
	function getSuggestions(field, options) {
		switch (field.fieldName) {
			case "ProgressState": return options.states;
			case "ConcernArea": return options.areas;
			case "AssignedTo": return options.assignees;
			default: return field.suggestions;
		}
	}
	function createInput(field, value, options, container) {
		let input;
		if (field.kind === "multiline") {
			const textArea = document.createElement("textarea");
			textArea.rows = 8;
			input = textArea;
		} else {
			const textInput = document.createElement("input");
			textInput.type = "text";
			const suggestions = getSuggestions(field, options);
			if (suggestions !== null && suggestions.length > 0) {
				const datalist = document.createElement("datalist");
				datalist.id = "card-editor-suggestions-" + ++datalistSequence;
				for (const suggestion of suggestions) {
					const option = document.createElement("option");
					option.value = suggestion;
					datalist.appendChild(option);
				}
				container.appendChild(datalist);
				textInput.setAttribute("list", datalist.id);
			}
			input = textInput;
		}
		input.className = "editor-input";
		input.name = field.fieldName;
		input.value = value;
		input.readOnly = options.readOnly;
		return input;
	}
	/**
	* Opens a modal editor for the given card.
	* Resolves the CHANGED fields (PascalCase field names, ready for 'TryUpdateEntityFields')
	* or null if the editor was cancelled / nothing was changed.
	*/
	function openCardEditor(card, options) {
		return new Promise((resolve) => {
			const dialog = document.createElement("dialog");
			dialog.className = "editor-dialog state-border-" + getRootState(card.progressState).toLowerCase();
			const form = document.createElement("form");
			form.method = "dialog";
			form.className = "editor-form";
			const header = document.createElement("div");
			header.className = "editor-header";
			const heading = document.createElement("h2");
			heading.className = "editor-title";
			heading.textContent = options.readOnly ? "Card" : "Edit card";
			const identity = document.createElement("span");
			identity.className = "editor-identity";
			identity.textContent = card.origin + " #" + card.originRelatedId;
			header.appendChild(heading);
			header.appendChild(identity);
			form.appendChild(header);
			const body = document.createElement("div");
			body.className = "editor-body";
			const inputs = /* @__PURE__ */ new Map();
			for (const field of editorFields) {
				const row = document.createElement("label");
				row.className = field.kind === "multiline" ? "editor-row editor-row-wide" : "editor-row";
				const caption = document.createElement("span");
				caption.className = "editor-caption";
				caption.textContent = field.label;
				row.appendChild(caption);
				const input = createInput(field, field.getValue(card), options, row);
				row.appendChild(input);
				body.appendChild(row);
				inputs.set(field, input);
			}
			form.appendChild(body);
			const buttons = document.createElement("div");
			buttons.className = "confirm-buttons";
			const cancelButton = document.createElement("button");
			cancelButton.type = "button";
			cancelButton.className = "button button-secondary";
			cancelButton.textContent = options.readOnly ? "Close" : "Cancel";
			buttons.appendChild(cancelButton);
			let saveButton = null;
			if (!options.readOnly) {
				saveButton = document.createElement("button");
				saveButton.type = "submit";
				saveButton.className = "button";
				saveButton.textContent = "Save";
				buttons.appendChild(saveButton);
			}
			form.appendChild(buttons);
			dialog.appendChild(form);
			document.body.appendChild(dialog);
			let result = null;
			cancelButton.addEventListener("click", () => dialog.close());
			form.addEventListener("submit", () => {
				if (options.readOnly) return;
				const changes = {};
				for (const [field, input] of inputs) {
					const newText = field.kind === "multiline" ? input.value : input.value.trim();
					if (newText === field.getValue(card)) continue;
					if (field.kind === "tags") {
						const newTags = parseTags(newText);
						if (formatTags(newTags) !== field.getValue(card)) changes[field.fieldName] = newTags;
					} else changes[field.fieldName] = newText;
				}
				if (Object.keys(changes).length > 0) result = changes;
			});
			dialog.addEventListener("close", () => {
				dialog.remove();
				resolve(result);
			});
			dialog.showModal();
			const firstInput = inputs.get(editorFields[0]);
			if (firstInput !== void 0 && !options.readOnly) firstInput.focus();
			else cancelButton.focus();
		});
	}
	// src/configurationDialog.ts
	function createNewConfiguration() {
		return {
			boardId: "",
			title: "",
			connectorType: "GitHubIssues",
			owner: "",
			repositoryName: "",
			accessToken: "",
			hasAccessToken: false,
			removeAccessToken: false,
			isReadOnly: false,
			maxIssues: 500
		};
	}
	function createElement(tagName, className, text) {
		const element = document.createElement(tagName);
		element.className = className;
		if (text !== void 0) element.textContent = text;
		return element;
	}
	function createButton(text, className, onClick) {
		const button = createElement("button", className, text);
		button.type = "button";
		button.addEventListener("click", onClick);
		return button;
	}
	function createRow(caption, input, hint, wide) {
		const row = createElement("label", wide ? "editor-row editor-row-wide" : "editor-row");
		row.appendChild(createElement("span", "editor-caption", caption));
		row.appendChild(input);
		if (hint !== void 0) row.appendChild(createElement("span", "config-hint", hint));
		return row;
	}
	function createTextInput(value, type = "text") {
		const input = createElement("input", "editor-input");
		input.type = type;
		input.value = value;
		return input;
	}
	/**
	* Opens the connector configuration (list of connector boards + editor).
	* Resolves the id of the last saved board (null, if nothing was changed).
	*/
	function openConfigurationDialog() {
		return new Promise((resolve) => {
			let changedBoardId = null;
			let changed = false;
			const dialog = createElement("dialog", "editor-dialog config-dialog");
			const form = createElement("form", "editor-form");
			form.method = "dialog";
			const header = createElement("div", "editor-header");
			header.appendChild(createElement("h2", "editor-title", "⚙ Connector configuration"));
			const body = createElement("div", "config-body");
			const list = createElement("div", "config-list");
			const editor = createElement("div", "editor-body config-editor");
			const status = createElement("div", "config-status");
			body.appendChild(list);
			body.appendChild(editor);
			const buttons = createElement("div", "confirm-buttons");
			const closeButton = createButton("Close", "button button-secondary", () => dialog.close());
			buttons.appendChild(closeButton);
			form.appendChild(header);
			form.appendChild(body);
			form.appendChild(status);
			form.appendChild(buttons);
			dialog.appendChild(form);
			document.body.appendChild(dialog);
			const setStatus = (text, isError) => {
				status.textContent = text;
				status.classList.toggle("error", isError);
				status.classList.toggle("success", !isError && text.length > 0);
			};
			const renderEditor = (configuration) => {
				const isNew = configuration.boardId === "";
				const ownerInput = createTextInput(configuration.owner);
				ownerInput.placeholder = "e.g. SmartStandards";
				ownerInput.required = true;
				const repositoryInput = createTextInput(configuration.repositoryName);
				repositoryInput.placeholder = "e.g. UniversalWorkItem.Contract";
				repositoryInput.required = true;
				const titleInput = createTextInput(configuration.title);
				titleInput.placeholder = "default: GitHub: owner/repository";
				const tokenInput = createTextInput("", "password");
				tokenInput.autocomplete = "off";
				tokenInput.placeholder = configuration.hasAccessToken ? "•••••• stored - leave empty to keep" : "ghp_... / github_pat_... (empty = anonymous, public repos only)";
				const maxIssuesInput = createTextInput(String(configuration.maxIssues), "number");
				maxIssuesInput.min = "1";
				maxIssuesInput.max = "5000";
				const readOnlyInput = createElement("input", "");
				readOnlyInput.type = "checkbox";
				readOnlyInput.checked = configuration.isReadOnly;
				const readOnlyRow = createElement("label", "config-checkbox");
				readOnlyRow.appendChild(readOnlyInput);
				readOnlyRow.appendChild(document.createTextNode(" read-only board (no changes are written to GitHub)"));
				const removeTokenInput = createElement("input", "");
				removeTokenInput.type = "checkbox";
				const removeTokenRow = createElement("label", "config-checkbox");
				removeTokenRow.appendChild(removeTokenInput);
				removeTokenRow.appendChild(document.createTextNode(" remove the stored token"));
				const collect = () => ({
					boardId: configuration.boardId,
					title: titleInput.value.trim(),
					connectorType: "GitHubIssues",
					owner: ownerInput.value.trim(),
					repositoryName: repositoryInput.value.trim(),
					accessToken: tokenInput.value.trim(),
					hasAccessToken: configuration.hasAccessToken,
					removeAccessToken: removeTokenInput.checked,
					isReadOnly: readOnlyInput.checked,
					maxIssues: Number(maxIssuesInput.value) || 500
				});
				const actions = createElement("div", "config-actions editor-row-wide");
				const testButton = createButton("Test connection", "button button-secondary", async () => {
					setStatus("Connecting to GitHub...", false);
					try {
						const message = await testConnectorConfiguration(collect());
						if (message === "") setStatus("✔ Connection successful.", false);
						else setStatus("✖ " + message, true);
					} catch (error) {
						setStatus(error instanceof Error ? error.message : String(error), true);
					}
				});
				const saveButton = createButton(isNew ? "Add board" : "Save", "button", async () => {
					if (!ownerInput.reportValidity() || !repositoryInput.reportValidity()) return;
					setStatus("Saving...", false);
					try {
						const boardId = await saveConnectorConfiguration(collect());
						changedBoardId = boardId;
						changed = true;
						setStatus("✔ Saved - the board '" + boardId + "' is available now.", false);
						await reloadList(boardId);
					} catch (error) {
						setStatus(error instanceof Error ? error.message : String(error), true);
					}
				});
				actions.appendChild(testButton);
				actions.appendChild(saveButton);
				if (!isNew) actions.appendChild(createButton("Delete", "button button-danger", async () => {
					if (!window.confirm("Remove the board '" + configuration.boardId + "' (GitHub is not affected)?")) return;
					try {
						await deleteConnectorConfiguration(configuration.boardId);
						changed = true;
						changedBoardId = null;
						setStatus("Board removed.", false);
						await reloadList(null);
					} catch (error) {
						setStatus(error instanceof Error ? error.message : String(error), true);
					}
				}));
				editor.replaceChildren(createElement("div", "config-section-title editor-row-wide", isNew ? "New GitHub Issues board" : "Board '" + configuration.boardId + "'"), createRow("Owner (user / organization)", ownerInput), createRow("Repository", repositoryInput), createRow("Title", titleInput, void 0, true), createRow("Personal access token", tokenInput, "Fine-grained PAT with 'Issues: Read' (or 'Read and write' for editing). Stored encrypted on the server, never sent back to the browser.", true), createRow("Max. issues", maxIssuesInput), readOnlyRow, configuration.hasAccessToken ? removeTokenRow : createElement("span", ""), actions);
			};
			const reloadList = async (selectBoardId) => {
				const configurations = await getConnectorConfigurations();
				list.replaceChildren();
				let selected = null;
				for (const configuration of configurations) {
					const item = createButton("", "config-list-item", () => {
						list.querySelectorAll(".config-list-item").forEach((element) => element.classList.remove("active"));
						item.classList.add("active");
						setStatus("", false);
						renderEditor(configuration);
					});
					item.appendChild(createElement("span", "config-list-title", configuration.title));
					item.appendChild(createElement("span", "config-list-detail", configuration.owner + "/" + configuration.repositoryName + (configuration.hasAccessToken ? " · 🔑" : "")));
					if (configuration.boardId === selectBoardId) {
						item.classList.add("active");
						selected = configuration;
					}
					list.appendChild(item);
				}
				const addItem = createButton("+ Add GitHub Issues board", "config-list-item config-list-add", () => {
					list.querySelectorAll(".config-list-item").forEach((element) => element.classList.remove("active"));
					addItem.classList.add("active");
					setStatus("", false);
					renderEditor(createNewConfiguration());
				});
				list.appendChild(addItem);
				if (selected !== null) renderEditor(selected);
				else {
					addItem.classList.add("active");
					renderEditor(createNewConfiguration());
				}
			};
			dialog.addEventListener("close", () => {
				dialog.remove();
				if (changed) resolve(changedBoardId === null ? "" : changedBoardId);
				else resolve(null);
			});
			dialog.showModal();
			reloadList(null).catch((error) => {
				setStatus(error instanceof Error ? error.message : String(error), true);
			});
		});
	}
	// src/main.ts
	var priorityStep = 1e3;
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
	/**
	* Wraps a native select into a web-style combobox (custom border, chevron, focus ring).
	*/
	function createCombo(labelText, select) {
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
	function createSwitch(input, offText, onText) {
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
	function replaceAreaRoot(area, targetRoot) {
		const currentArea = normalizeArea(area);
		const currentRoot = getAreaRoot(currentArea);
		let rest = "";
		if (currentRoot !== "/") rest = currentArea.substring(currentRoot.length);
		if (targetRoot === "/") return rest || "/";
		return targetRoot + rest;
	}
	/**
	* Applies one assignment of the drop target to the field changes (working on 'pending', the card as it will be).
	* Throws if the dimension cannot be edited or two assignments contradict each other.
	*/
	function applyAssignment(pending, assignment, board) {
		const value = assignment.value;
		switch (assignment.dimension) {
			case "none": return;
			case "status":
				if (getRootState(pending.progressState) !== value) pending.progressState = value;
				return;
			case "area":
				if (assignment.subtree) {
					if (!isAreaWithin(pending.concernArea, value)) pending.concernArea = value;
				} else if (normalizeArea(pending.concernArea) !== value) pending.concernArea = value;
				return;
			case "areaRoot":
				if (getAreaRoot(pending.concernArea) !== value) pending.concernArea = replaceAreaRoot(pending.concernArea, value);
				return;
			case "assignedTo":
				pending.assignedTo = value === "(unassigned)" ? "" : value;
				return;
			case "origin": {
				if (pending.origin === value) return;
				const source = board === void 0 ? void 0 : (board.sources || []).find((candidate) => candidate.origin === value);
				if (source === void 0) throw new Error("The origin '" + value + "' is not a source of this board - the card cannot be moved there.");
				pending.concernArea = replaceAreaRoot(pending.concernArea, source.areaRoot);
				return;
			}
			default: throw new Error("The dimension '" + DimensionLabels[assignment.dimension] + "' cannot be changed via drag and drop.");
		}
	}
	/**
	* Returns the source (origin) which owns the given board area (null = not a composed board / unknown root).
	*/
	function findSourceOrigin(board, area) {
		if (board === void 0 || !board.sources) return null;
		const root = getAreaRoot(area);
		const source = board.sources.find((candidate) => candidate.areaRoot.toLowerCase() === root.toLowerCase());
		return source === void 0 ? null : source.origin;
	}
	function isTransitionAllowed(board, sourceOrigin, targetOrigin) {
		return (board.allowedOriginTransitions || []).some((transition) => transition.sourceOrigin.toLowerCase() === sourceOrigin.toLowerCase() && transition.targetOrigin.toLowerCase() === targetOrigin.toLowerCase());
	}
	/**
	* Modal warning dialog. Resolves true if the user confirmed.
	*/
	function confirmDialog(title, message, confirmText) {
		return new Promise((resolve) => {
			const dialog = document.createElement("dialog");
			dialog.className = "confirm-dialog";
			const heading = document.createElement("h2");
			heading.className = "confirm-title";
			heading.textContent = "⚠ " + title;
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
	function calculatePriority(target) {
		if (target.orderedCards === null || target.orderedCards.length === 0) return null;
		const above = target.insertIndex > 0 ? target.orderedCards[target.insertIndex - 1] : null;
		const below = target.insertIndex < target.orderedCards.length ? target.orderedCards[target.insertIndex] : null;
		if (above !== null && below !== null) {
			const upper = Number(above.manualReorderedPriority) || 0;
			const lower = Number(below.manualReorderedPriority) || 0;
			const middle = Math.floor((upper + lower) / 2);
			if (middle <= lower) return lower + 1;
			return middle;
		}
		if (above !== null) return (Number(above.manualReorderedPriority) || 0) - priorityStep;
		if (below !== null) return (Number(below.manualReorderedPriority) || 0) + priorityStep;
		return null;
	}
	var BoardApp = class {
		constructor(root) {
			this.boards = [];
			this.cards = [];
			this.viewStates = /* @__PURE__ */ new Map();
			this.loadSequence = 0;
			this.boardSelector = document.createElement("select");
			this.readOnlyBadge = document.createElement("span");
			this.columnsSelector = createSelect(ColumnDimensionLabels);
			this.secondLevelSelector = createSelect(DimensionLabels);
			this.groupBySelector = createSelect(DimensionLabels);
			this.crossColumnSwitch = document.createElement("input");
			this.crossColumnField = createSwitch(this.crossColumnSwitch, "Within column", "Cross-column rows");
			this.reloadButton = document.createElement("button");
			this.settingsButton = document.createElement("button");
			this.statusElement = document.createElement("span");
			this.boardElement = document.createElement("main");
			this.mobileQuery = window.matchMedia("(max-width: 700px)");
			this.mobileColumnIndex = 0;
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
			this.reloadButton.textContent = "↻ Reload";
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
			toolbar.appendChild(this.settingsButton);
			toolbar.appendChild(this.statusElement);
			root.replaceChildren(toolbar, this.boardElement);
			this.boardSelector.addEventListener("change", () => this.loadCards());
			this.reloadButton.addEventListener("click", () => this.loadCards());
			this.settingsButton.type = "button";
			this.settingsButton.className = "button button-secondary";
			this.settingsButton.textContent = "⚙ Connectors";
			this.settingsButton.title = "Configure the connectors (e.g. GitHub repository and access token)";
			this.settingsButton.addEventListener("click", async () => {
				const savedBoardId = await openConfigurationDialog();
				if (savedBoardId !== null) await this.loadBoards(savedBoardId);
			});
			this.columnsSelector.addEventListener("change", () => {
				const view = this.currentView;
				view.columns = this.columnsSelector.value;
				if (view.secondLevel === view.columns) view.secondLevel = "none";
				if (view.groupBy === view.columns) view.groupBy = "none";
				view.collapsedColumns.clear();
				view.toggledSplits.clear();
				view.expandedAreas.clear();
				this.syncViewControls();
				this.render();
			});
			this.secondLevelSelector.addEventListener("change", () => {
				this.currentView.secondLevel = this.secondLevelSelector.value;
				this.currentView.toggledSplits.clear();
				this.render();
			});
			this.groupBySelector.addEventListener("change", () => {
				this.currentView.groupBy = this.groupBySelector.value;
				this.syncViewControls();
				this.render();
			});
			this.crossColumnSwitch.addEventListener("change", () => {
				this.currentView.groupingMode = this.crossColumnSwitch.checked ? "crossColumn" : "withinColumn";
				this.render();
			});
			this.mobileQuery.addEventListener("change", () => this.render());
		}
		get currentBoardId() {
			return this.boardSelector.value;
		}
		get currentBoard() {
			return this.boards.find((candidate) => candidate.boardId === this.currentBoardId);
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
		async loadBoards(preferredBoardId = "") {
			try {
				const previousBoardId = this.currentBoardId;
				this.setStatus("Loading boards...");
				this.boards = await getBoards();
				this.boardSelector.replaceChildren();
				for (const board of this.boards) {
					const option = document.createElement("option");
					option.value = board.boardId;
					option.textContent = board.title;
					this.boardSelector.appendChild(option);
				}
				const selection = preferredBoardId || previousBoardId;
				if (selection && this.boards.some((board) => board.boardId === selection)) this.boardSelector.value = selection;
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
		render() {
			const board = this.currentBoard;
			const isReadOnly = board === void 0 || board.isReadOnly;
			if (this.mobileQuery.matches) {
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
		collectDistinct(selector, initial) {
			const values = new Set(initial);
			for (const card of this.cards) {
				const value = selector(card);
				if (value) values.add(value);
			}
			return Array.from(values).sort((a, b) => a.localeCompare(b));
		}
		/**
		* Ensures that an area change which moves the card into another origin (composed boards) is enabled
		* and confirmed. Returns false if the user cancelled.
		*/
		async confirmOriginChange(card, newArea) {
			const board = this.currentBoard;
			if (board === void 0) return true;
			const sourceOrigin = findSourceOrigin(board, card.concernArea);
			const targetOrigin = findSourceOrigin(board, newArea);
			if (sourceOrigin === null || targetOrigin === null || sourceOrigin === targetOrigin) return true;
			if (!isTransitionAllowed(board, sourceOrigin, targetOrigin)) throw new Error("Moving cards from '" + sourceOrigin + "' to '" + targetOrigin + "' is not enabled for this board (the directed origin transition must be allowed in the backend).");
			return confirmDialog("Move card to another origin?", "The card '" + card.title + "' (" + card.origin + " #" + card.originRelatedId + ") will be MOVED from '" + sourceOrigin + "' to '" + targetOrigin + "'. It is created as a new item in the target and DELETED in the original source - its identity (Origin/Id) changes and links to the original item may break.", "Move card");
		}
		/**
		* Opens the card editor and saves the changed fields via 'TryUpdateEntityFields'.
		*/
		async editCard(card) {
			const board = this.currentBoard;
			const isReadOnly = board === void 0 || board.isReadOnly;
			try {
				const changedFields = await openCardEditor(card, {
					readOnly: isReadOnly,
					states: this.collectDistinct((candidate) => candidate.progressState, RootStates),
					areas: this.collectDistinct((candidate) => normalizeArea(candidate.concernArea), []),
					assignees: this.collectDistinct((candidate) => candidate.assignedTo, [])
				});
				if (changedFields === null) return;
				if ("ConcernArea" in changedFields) {
					if (!await this.confirmOriginChange(card, String(changedFields["ConcernArea"]))) {
						this.setStatus("Change cancelled.");
						return;
					}
				}
				this.setStatus("Saving...");
				if (await tryUpdateCardFields(this.currentBoardId, card, changedFields) === null) throw new Error("The card '" + card.origin + " #" + card.originRelatedId + "' was not found (anymore).");
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
		async moveCard(card, target) {
			const board = this.currentBoard;
			const changedFields = {};
			const pending = Object.assign({}, card);
			try {
				for (const assignment of target.assignments) applyAssignment(pending, assignment, board);
				for (const assignment of target.assignments) if (assignment.dimension !== "none" && assignment.dimension !== "origin" && !assignment.subtree && getDimensionValue(pending, assignment.dimension) !== assignment.value) throw new Error("The target contradicts itself: '" + DimensionLabels[assignment.dimension] + "' cannot be '" + assignment.value + "' here.");
				if (pending.progressState !== card.progressState) changedFields["ProgressState"] = pending.progressState;
				if (normalizeArea(pending.concernArea) !== normalizeArea(card.concernArea)) changedFields["ConcernArea"] = pending.concernArea;
				if (pending.assignedTo !== card.assignedTo) changedFields["AssignedTo"] = pending.assignedTo;
				if ("ConcernArea" in changedFields) {
					if (!await this.confirmOriginChange(card, pending.concernArea)) {
						this.render();
						this.setStatus("Move cancelled.");
						return;
					}
				}
				const priority = calculatePriority(target);
				if (priority !== null && priority !== Number(card.manualReorderedPriority)) changedFields["ManualReorderedPriority"] = priority;
				if (Object.keys(changedFields).length === 0) return;
				this.setStatus("Saving...");
				if (await tryUpdateCardFields(this.currentBoardId, card, changedFields) === null) throw new Error("The card '" + card.origin + " #" + card.originRelatedId + "' was not found (anymore).");
				await this.loadCards();
				this.setStatus("Updated: " + Object.keys(changedFields).join(", "));
			} catch (error) {
				this.reportError(error);
				this.render();
			}
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
