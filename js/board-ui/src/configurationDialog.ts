import {
  ConnectorConfiguration,
  deleteConnectorConfiguration,
  getConnectorConfigurations,
  saveConnectorConfiguration,
  testConnectorConfiguration
} from "./boardApi";

function createNewConfiguration(): ConnectorConfiguration {
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

function createElement<TElement extends HTMLElement>(tagName: string, className: string, text?: string): TElement {
  const element = document.createElement(tagName) as TElement;
  element.className = className;
  if (text !== undefined) {
    element.textContent = text;
  }
  return element;
}

function createButton(text: string, className: string, onClick: () => void): HTMLButtonElement {
  const button = createElement<HTMLButtonElement>("button", className, text);
  button.type = "button";
  button.addEventListener("click", onClick);
  return button;
}

function createRow(caption: string, input: HTMLElement, hint?: string, wide?: boolean): HTMLElement {
  const row = createElement<HTMLLabelElement>("label", wide ? "editor-row editor-row-wide" : "editor-row");
  row.appendChild(createElement("span", "editor-caption", caption));
  row.appendChild(input);
  if (hint !== undefined) {
    row.appendChild(createElement("span", "config-hint", hint));
  }
  return row;
}

function createTextInput(value: string, type: string = "text"): HTMLInputElement {
  const input = createElement<HTMLInputElement>("input", "editor-input");
  input.type = type;
  input.value = value;
  return input;
}

/**
 * Opens the connector configuration (list of connector boards + editor).
 * Resolves the id of the last saved board (null, if nothing was changed).
 */
export function openConfigurationDialog(): Promise<string | null> {
  return new Promise<string | null>((resolve) => {
    let changedBoardId: string | null = null;
    let changed = false;

    const dialog = createElement<HTMLDialogElement>("dialog", "editor-dialog config-dialog");
    const form = createElement<HTMLFormElement>("form", "editor-form");
    form.method = "dialog";

    const header = createElement("div", "editor-header");
    header.appendChild(createElement("h2", "editor-title", "\u2699 Connector configuration"));

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

    const setStatus = (text: string, isError: boolean): void => {
      status.textContent = text;
      status.classList.toggle("error", isError);
      status.classList.toggle("success", !isError && text.length > 0);
    };

    const renderEditor = (configuration: ConnectorConfiguration): void => {
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
      tokenInput.placeholder = configuration.hasAccessToken ? "\u2022\u2022\u2022\u2022\u2022\u2022 stored - leave empty to keep" : "ghp_... / github_pat_... (empty = anonymous, public repos only)";
      const maxIssuesInput = createTextInput(String(configuration.maxIssues), "number");
      maxIssuesInput.min = "1";
      maxIssuesInput.max = "5000";

      const readOnlyInput = createElement<HTMLInputElement>("input", "");
      readOnlyInput.type = "checkbox";
      readOnlyInput.checked = configuration.isReadOnly;
      const readOnlyRow = createElement<HTMLLabelElement>("label", "config-checkbox");
      readOnlyRow.appendChild(readOnlyInput);
      readOnlyRow.appendChild(document.createTextNode(" read-only board (no changes are written to GitHub)"));

      const removeTokenInput = createElement<HTMLInputElement>("input", "");
      removeTokenInput.type = "checkbox";
      const removeTokenRow = createElement<HTMLLabelElement>("label", "config-checkbox");
      removeTokenRow.appendChild(removeTokenInput);
      removeTokenRow.appendChild(document.createTextNode(" remove the stored token"));

      const collect = (): ConnectorConfiguration => ({
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
          if (message === "") {
            setStatus("\u2714 Connection successful.", false);
          } else {
            setStatus("\u2716 " + message, true);
          }
        } catch (error) {
          setStatus(error instanceof Error ? error.message : String(error), true);
        }
      });

      const saveButton = createButton(isNew ? "Add board" : "Save", "button", async () => {
        if (!ownerInput.reportValidity() || !repositoryInput.reportValidity()) {
          return;
        }
        setStatus("Saving...", false);
        try {
          const boardId = await saveConnectorConfiguration(collect());
          changedBoardId = boardId;
          changed = true;
          setStatus("\u2714 Saved - the board '" + boardId + "' is available now.", false);
          await reloadList(boardId);
        } catch (error) {
          setStatus(error instanceof Error ? error.message : String(error), true);
        }
      });

      actions.appendChild(testButton);
      actions.appendChild(saveButton);

      if (!isNew) {
        actions.appendChild(createButton("Delete", "button button-danger", async () => {
          if (!window.confirm("Remove the board '" + configuration.boardId + "' (GitHub is not affected)?")) {
            return;
          }
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
      }

      editor.replaceChildren(
        createElement("div", "config-section-title editor-row-wide", isNew ? "New GitHub Issues board" : "Board '" + configuration.boardId + "'"),
        createRow("Owner (user / organization)", ownerInput),
        createRow("Repository", repositoryInput),
        createRow("Title", titleInput, undefined, true),
        createRow(
          "Personal access token",
          tokenInput,
          "Fine-grained PAT with 'Issues: Read' (or 'Read and write' for editing). Stored encrypted on the server, never sent back to the browser.",
          true
        ),
        createRow("Max. issues", maxIssuesInput),
        readOnlyRow,
        configuration.hasAccessToken ? removeTokenRow : createElement("span", ""),
        actions
      );
    };

    const reloadList = async (selectBoardId: string | null): Promise<void> => {
      const configurations = await getConnectorConfigurations();
      list.replaceChildren();

      let selected: ConnectorConfiguration | null = null;

      for (const configuration of configurations) {
        const item = createButton("", "config-list-item", () => {
          list.querySelectorAll(".config-list-item").forEach((element) => element.classList.remove("active"));
          item.classList.add("active");
          setStatus("", false);
          renderEditor(configuration);
        });
        item.appendChild(createElement("span", "config-list-title", configuration.title));
        item.appendChild(createElement("span", "config-list-detail", configuration.owner + "/" + configuration.repositoryName + (configuration.hasAccessToken ? " \u00B7 \uD83D\uDD11" : "")));
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

      if (selected !== null) {
        renderEditor(selected);
      } else {
        addItem.classList.add("active");
        renderEditor(createNewConfiguration());
      }
    };

    dialog.addEventListener("close", () => {
      dialog.remove();
      if (changed) {
        resolve(changedBoardId === null ? "" : changedBoardId);
      } else {
        resolve(null);
      }
    });

    dialog.showModal();

    reloadList(null).catch((error) => {
      setStatus(error instanceof Error ? error.message : String(error), true);
    });
  });
}
