const scopeSelector = "[data-clay-edit-scope]";
const modalSelector = '.mud-dialog-container, [aria-modal="true"]';
const editorSelector = 'input, textarea, select, [contenteditable="true"], [data-clay-focus-editor]';
const unavailableSelector = '[hidden], [inert], [aria-hidden="true"], [aria-disabled="true"], .mud-disabled';
const excludedInputTypes = new Set(["hidden", "button", "submit", "reset", "image"]);
const plainInputTypes = new Set(["text", "search", "email", "url", "tel", "password", "number"]);
const scopes = new WeakMap();
const selectionLeases = new WeakMap();

let focusGeneration = 0;
let selectionId = 0;
const incrementGeneration = () => focusGeneration++;
document.addEventListener("focusin", incrementGeneration, true);
document.addEventListener("pointerdown", incrementGeneration, true);
document.addEventListener("keydown", incrementGeneration, true);
document.addEventListener("input", incrementGeneration, true);

function visible(element) {
    return element.isConnected &&
        !element.closest(unavailableSelector) &&
        element.getClientRects().length > 0 &&
        getComputedStyle(element).visibility === "visible";
}

function modalFor(element) {
    // MudDialog may mark its inner container aria-modal; use the enclosing Mud boundary.
    const boundary = element.closest(modalSelector);
    return boundary?.matches(".mud-dialog")
        ? boundary.closest(".mud-dialog-container") ?? boundary
        : boundary;
}

function isActiveScope(root) {
    const modals = Array.from(document.querySelectorAll(modalSelector))
        .filter(element => modalFor(element) === element && visible(element));
    return (modals.at(-1) ?? null) === modalFor(root);
}

function isEditor(root, element) {
    return element.closest(scopeSelector) === root &&
        modalFor(element) === modalFor(root) &&
        visible(element) &&
        element.tabIndex >= 0 &&
        !element.matches(":disabled") &&
        (element.hasAttribute("data-clay-focus-editor") || !element.closest("[readonly], .mud-readonly")) &&
        !(element.tagName === "INPUT" && excludedInputTypes.has(element.type));
}

function editors(root) {
    const order = element => element.tabIndex || Number.MAX_SAFE_INTEGER;
    return Array.from(root.querySelectorAll(editorSelector))
        .filter(element => isEditor(root, element))
        .sort((left, right) => order(left) - order(right));
}

function ownsFocus(origin) {
    const active = document.activeElement;
    return document.hasFocus() && (active === origin || origin.closest(".clay-select")?.contains(active));
}

export function captureSelectionAdvance(origin) {
    const root = origin?.closest(scopeSelector);
    const state = scopes.get(root);
    if (!state?.ready || !state.advanceOnEnter || !isEditor(root, origin) || !isActiveScope(root) || !ownsFocus(origin))
        return null;
    const lease = { id: ++selectionId, root, state, epoch: state.epoch, focusGeneration };
    selectionLeases.set(origin, lease);
    return lease.id;
}

export function completeSelectionAdvance(origin, id) {
    const lease = selectionLeases.get(origin);
    if (!lease || lease.id !== id)
        return false;
    selectionLeases.delete(origin);
    const { root, state } = lease;
    if (scopes.get(root) !== state || state.epoch !== lease.epoch || focusGeneration !== lease.focusGeneration ||
        !isEditor(root, origin) || !isActiveScope(root) || !ownsFocus(origin))
        return false;
    const fields = editors(root);
    const next = fields[fields.indexOf(origin) + 1];
    if (!next)
        return false;
    next.focus();
    return document.activeElement === next;
}

function onKeyDown(root, state, event) {
    if (!state.ready || !state.advanceOnEnter || event.defaultPrevented || event.key !== "Enter" ||
        event.isComposing || event.keyCode === 229 || event.altKey || event.ctrlKey || event.shiftKey || event.metaKey)
        return;

    const origin = event.target;
    if (origin.tagName !== "INPUT" || !plainInputTypes.has(origin.type) ||
        !isEditor(root, origin) || !isActiveScope(root) ||
        origin.closest('[data-clay-enter-native], [data-clay-focus-editor], [role="combobox"], .clay-select, .mud-picker'))
        return;

    event.preventDefault();
    event.stopPropagation();
    if (event.repeat)
        return;

    if (origin.validity?.valid === false) {
        origin.reportValidity();
        return;
    }

    const fields = editors(root);
    fields[fields.indexOf(origin) + 1]?.focus();
}

function tryInitialFocus(root, state) {
    if (!state.ready || state.initialHandled || !visible(root) || !isActiveScope(root))
        return;

    state.initialHandled = true;
    const active = document.activeElement;
    const neutral = !active || active === document.body || active === document.documentElement ||
        active === root || active === modalFor(root);
    if (neutral && state.generation === focusGeneration)
        (editors(root)[0] ?? root).focus();
}

export function update(root, ready, advanceOnEnter) {
    if (!root?.isConnected)
        return;

    let state = scopes.get(root);
    if (!state) {
        state = { generation: focusGeneration, epoch: 0 };
        state.keyDown = event => onKeyDown(root, state, event);
        scopes.set(root, state);
        root.addEventListener("keydown", state.keyDown);
    }

    if (state.ready !== ready || state.advanceOnEnter !== advanceOnEnter)
        state.epoch++;
    state.ready = ready;
    state.advanceOnEnter = advanceOnEnter;
    if (ready && !state.initialHandled && !state.frame) {
        state.frame = requestAnimationFrame(() => {
            state.frame = null;
            tryInitialFocus(root, state);
        });
    }
}

export function detach(root) {
    const state = scopes.get(root);
    if (!state)
        return;

    if (state.frame)
        cancelAnimationFrame(state.frame);
    root.removeEventListener("keydown", state.keyDown);
    scopes.delete(root);
}
