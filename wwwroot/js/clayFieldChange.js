const fields = new WeakMap();
const dirtyAttribute = "data-clay-field-raw-changed";
const typedAttribute = "data-clay-field-changed";
const lookupAttribute = "data-clay-synced-text";
const restoredEvent = "clay:field-restored";
const restoredAttribute = "data-clay-field-restored";

function text(value) {
    return (value ?? "").replace(/\r\n?/g, "\n");
}

function watchable(root, input) {
    return input?.matches("input, textarea") && input.closest(".clay-field-change") === root &&
        !input.disabled && !input.readOnly && !["password", "file", "hidden", "checkbox", "radio"].includes(input.type);
}

function publish(root, changed) {
    root.toggleAttribute(dirtyAttribute, changed);
    const description = root.querySelector("[data-clay-field-raw-description]");
    if (!description) {
        if (root.getAttribute("aria-describedby")?.startsWith("clay-field-raw-")) {
            root.removeAttribute("aria-describedby");
            if (!root.hasAttribute(typedAttribute)) root.removeAttribute("role");
        }
        return;
    }
    const announceRaw = changed && !root.hasAttribute(typedAttribute);
    const message = announceRaw ? "Есть несохранённое изменение" : "";
    if (description.textContent !== message) description.textContent = message;
    if (announceRaw) {
        root.setAttribute("role", "group");
        root.setAttribute("aria-describedby", description.id);
    } else if (root.getAttribute("aria-describedby") === description.id) {
        root.removeAttribute("aria-describedby");
        if (!root.hasAttribute(typedAttribute)) root.removeAttribute("role");
    }
}

function reconcile(root, state) {
    const edit = state.edit;
    if (!edit || !edit.input.isConnected || !watchable(root, edit.input) || text(edit.input.value) !== edit.value) {
        state.edit = null;
        publish(root, false);
        return;
    }
    const baseline = state.lookup ? edit.input.getAttribute(lookupAttribute) : state.baseline;
    publish(root, baseline !== null && edit.value !== text(baseline));
}

export function update(root, baseline, lookup) {
    if (!root?.isConnected) return;
    if (baseline === null && !lookup) {
        detach(root);
        return;
    }
    let state = fields.get(root);
    if (!state) {
        state = { edit: null };
        state.onEdit = event => {
            const input = event.target;
            if ((!event.isTrusted && event.type !== restoredEvent) || !watchable(root, input) ||
                (state.lookup && !input.hasAttribute(lookupAttribute)))
                return;
            input.removeAttribute(restoredAttribute);
            state.edit = { input, value: text(input.value) };
            reconcile(root, state);
        };
        root.addEventListener("input", state.onEdit, true);
        root.addEventListener("change", state.onEdit, true);
        root.addEventListener(restoredEvent, state.onEdit, true);
        state.observer = new MutationObserver(() => reconcile(root, state));
        state.observer.observe(root, { childList: true, subtree: true, attributes: true,
            attributeFilter: [lookupAttribute, typedAttribute, "readonly", "disabled"] });
        fields.set(root, state);
    }
    state.baseline = baseline;
    state.lookup = lookup;
    for (const input of root.querySelectorAll(`[${restoredAttribute}]`)) {
        if (watchable(root, input)) state.onEdit({ target: input, type: restoredEvent });
    }
    reconcile(root, state);
}

export function detach(root) {
    const state = fields.get(root);
    if (!state) return;
    state.observer.disconnect();
    root.removeEventListener("input", state.onEdit, true);
    root.removeEventListener("change", state.onEdit, true);
    root.removeEventListener(restoredEvent, state.onEdit, true);
    fields.delete(root);
    publish(root, false);
}
