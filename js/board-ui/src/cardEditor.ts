import { WorkItem, getRootState } from "./model";

// Rendering only - no transport code here (the caller persists the returned changes).

export interface CardEditorOptions {
  /** true = the card is only displayed (no save button). */
  readOnly: boolean;
  /** Suggestions for the 'ProgressState' input. */
  states: string[];
  /** Suggestions for the 'ConcernArea' input. */
  areas: string[];
  /** Suggestions for the 'AssignedTo' input. */
  assignees: string[];
}

interface EditorField {
  /** PascalCase field name (as used by 'TryUpdateEntityFields'). */
  fieldName: string;
  label: string;
  kind: "text" | "multiline" | "tags";
  suggestions: string[] | null;
  getValue: (card: WorkItem) => string;
}

let datalistSequence = 0;

function asText(value: unknown): string {
  if (value === null || value === undefined) {
    return "";
  }
  return String(value);
}

function parseTags(text: string): string[] {
  return text
    .split(",")
    .map((tag) => tag.trim())
    .filter((tag) => tag.length > 0);
}

function formatTags(tags: string[] | null): string {
  return (tags || []).join(", ");
}

const editorFields: EditorField[] = [
  { fieldName: "Title", label: "Title", kind: "text", suggestions: null, getValue: (card) => asText(card.title) },
  { fieldName: "ProgressState", label: "State", kind: "text", suggestions: [], getValue: (card) => asText(card.progressState) },
  { fieldName: "ConcernArea", label: "Area", kind: "text", suggestions: [], getValue: (card) => asText(card.concernArea) },
  { fieldName: "AssignedTo", label: "Assigned to", kind: "text", suggestions: [], getValue: (card) => asText(card.assignedTo) },
  { fieldName: "Tags", label: "Tags (comma separated)", kind: "tags", suggestions: null, getValue: (card) => formatTags(card.tags) },
  { fieldName: "ProgressNote", label: "Progress note", kind: "text", suggestions: null, getValue: (card) => asText(card["progressNote"]) },
  { fieldName: "BodyContent", label: "Description", kind: "multiline", suggestions: null, getValue: (card) => asText(card["bodyContent"]) }
];

function getSuggestions(field: EditorField, options: CardEditorOptions): string[] | null {
  switch (field.fieldName) {
    case "ProgressState":
      return options.states;
    case "ConcernArea":
      return options.areas;
    case "AssignedTo":
      return options.assignees;
    default:
      return field.suggestions;
  }
}

function createInput(field: EditorField, value: string, options: CardEditorOptions, container: HTMLElement): HTMLInputElement | HTMLTextAreaElement {
  let input: HTMLInputElement | HTMLTextAreaElement;

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
      datalist.id = "card-editor-suggestions-" + (++datalistSequence);
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
export function openCardEditor(card: WorkItem, options: CardEditorOptions): Promise<Record<string, unknown> | null> {
  return new Promise<Record<string, unknown> | null>((resolve) => {
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
    const inputs = new Map<EditorField, HTMLInputElement | HTMLTextAreaElement>();

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

    let saveButton: HTMLButtonElement | null = null;
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

    let result: Record<string, unknown> | null = null;

    cancelButton.addEventListener("click", () => dialog.close());

    form.addEventListener("submit", () => {
      if (options.readOnly) {
        return;
      }
      const changes: Record<string, unknown> = {};
      for (const [field, input] of inputs) {
        const newText = field.kind === "multiline" ? input.value : input.value.trim();
        if (newText === field.getValue(card)) {
          continue;
        }
        if (field.kind === "tags") {
          const newTags = parseTags(newText);
          if (formatTags(newTags) !== field.getValue(card)) {
            changes[field.fieldName] = newTags;
          }
        } else {
          changes[field.fieldName] = newText;
        }
      }
      if (Object.keys(changes).length > 0) {
        result = changes;
      }
    });

    dialog.addEventListener("close", () => {
      dialog.remove();
      resolve(result);
    });

    dialog.showModal();
    const firstInput = inputs.get(editorFields[0]);
    if (firstInput !== undefined && !options.readOnly) {
      firstInput.focus();
    } else {
      cancelButton.focus();
    }
  });
}
