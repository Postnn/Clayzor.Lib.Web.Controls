const scopeSelector = "[data-clay-edit-scope]";
const modalSelector = '.mud-dialog-container, [aria-modal="true"]';
const editorSelector = 'input, textarea, select, [contenteditable="true"], [data-clay-focus-editor]';
const unavailableSelector = '[hidden], [inert], [aria-hidden="true"], [aria-disabled="true"], .mud-disabled';
const nativePickerSelector = "[data-clay-fixed-picker]";
const textEntrySelector = "[data-clay-text-commit]";
const popupPickerSelector = "[data-clay-picker-key]";
const excludedInputTypes = new Set(["hidden", "button", "submit", "reset", "image"]);
const plainInputTypes = new Set(["text", "search", "email", "url", "tel", "password", "number"]);
const scopes = new WeakMap();
const selectionLeases = new WeakMap();
const nativeSelections = new WeakMap();
const textEntries = new WeakMap();
const popupSelections = new WeakMap();
let clockPointer = null;

let focusGeneration = 0;
let interactionGeneration = 0;
let selectionId = 0;
const incrementGeneration = () => focusGeneration++;
function recordInteraction(event) {
    incrementGeneration();
    if (event?.isTrusted !== false) interactionGeneration++;
}
document.addEventListener("focusin", incrementGeneration, true);
document.addEventListener("pointerdown", event => {
    recordInteraction(event);
    const clock = event?.target?.closest?.(".mud-picker-time-clock-mask");
    clockPointer = event?.isTrusted && event.button === 0 && clock ? { clock, id: event.pointerId } : null;
}, true);
document.addEventListener("pointerup", event => {
    const pointer = clockPointer;
    clockPointer = null;
    const clock = event.target?.closest?.(".mud-picker-time-clock-mask");
    if (!event.isTrusted || pointer?.id !== event.pointerId || pointer?.clock !== clock || !clock)
        return;
    const minutes = clock.querySelector(".mud-time-picker-minute");
    if (minutes && !minutes.matches(".mud-time-picker-dial-hidden"))
        capturePopupSelection(clock.closest(popupPickerSelector));
}, true);
document.addEventListener("keydown", event => {
    recordInteraction(event);
    if (!isPlainEnter(event) || !event.isTrusted || event.repeat)
        return;
    const input = event.target?.closest?.(popupPickerSelector);
    if (input?.tagName === "INPUT") {
        const popup = openPickerPaper(input.getAttribute("data-clay-picker-key"));
        if (popup && (input.getAttribute("data-clay-picker-kind") === "time" ||
            popup.querySelector(".mud-picker-calendar")))
            capturePopupSelection(popup);
    }
    const picker = event.target?.closest?.(nativePickerSelector);
    if (picker?.getAttribute("aria-expanded") === "true") {
        const itemId = picker.getAttribute("aria-activedescendant");
        const item = itemId && document.getElementById(itemId);
        if (item && !item.matches(":disabled") && !item.closest(unavailableSelector))
            captureNativeSelection(picker, item);
    }
}, true);
document.addEventListener("input", recordInteraction, true);
document.addEventListener("click", event => {
    const day = event.target?.closest?.(".mud-picker-calendar-day");
    if (event.isTrusted && day?.tagName === "BUTTON" && !day.matches(":disabled") && !day.closest(unavailableSelector))
        capturePopupSelection(day.closest(popupPickerSelector));
    const option = event.target?.closest?.('[role="option"]');
    if (!event.isTrusted || !option || option.matches(":disabled") || option.closest(unavailableSelector))
        return;
    for (const picker of document.querySelectorAll(nativePickerSelector)) {
        const listId = picker.getAttribute("aria-controls");
        const list = listId && document.getElementById(listId);
        if (picker.getAttribute("aria-expanded") === "true" && list?.contains(option)) {
            captureNativeSelection(picker, option, list);
            return;
        }
    }
}, true);

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
    return root !== null && element.closest(scopeSelector) === root &&
        !element.closest("[data-clay-editor-exclude]") &&
        modalFor(element) === modalFor(root) &&
        visible(element) &&
        element.tabIndex >= 0 &&
        !element.matches(":disabled") &&
        (element.hasAttribute("data-clay-focus-editor") || element.hasAttribute("data-clay-fixed-picker") ||
            !element.closest("[readonly], .mud-readonly")) &&
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
    return captureSelection(origin);
}

function captureSelection(origin, nativeList = null) {
    const root = origin?.closest(scopeSelector);
    const state = scopes.get(root);
    if (!state?.ready || !state.advanceOnEnter || !isEditor(root, origin) || !isActiveScope(root) ||
        !ownsSelectionFocus(origin, nativeList))
        return null;
    const lease = { id: ++selectionId, root, state, epoch: state.epoch, focusGeneration, nativeList };
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
        !isEditor(root, origin) || !isActiveScope(root) || !ownsSelectionFocus(origin, lease.nativeList))
        return false;
    const fields = editors(root);
    const next = fields[fields.indexOf(origin) + 1];
    if (!next)
        return false;
    next.focus();
    return document.activeElement === next;
}

function ownsSelectionFocus(origin, nativeList) {
    if (ownsFocus(origin)) return true;
    const active = document.activeElement;
    return nativeList && document.hasFocus() && (active === document.body || nativeList.contains(active));
}

function captureNativeSelection(origin, option, list) {
    const id = captureSelection(origin, list);
    if (id !== null) nativeSelections.set(origin, { id, alreadySelected: option.getAttribute("aria-selected") === "true" });
}

function findNativePicker(key) {
    return findEditor(nativePickerSelector, "data-clay-fixed-picker", key);
}

function findEditor(selector, attribute, key) {
    return Array.from(document.querySelectorAll(selector))
        .find(element => element.getAttribute(attribute) === key &&
            isEditor(element.closest(scopeSelector), element));
}

export function claimNativeSelectionAdvance(key, alreadySelected = false) {
    const origin = findNativePicker(key);
    const gesture = nativeSelections.get(origin);
    if (!gesture || alreadySelected && !gesture.alreadySelected) return null;
    nativeSelections.delete(origin);
    return selectionLeases.get(origin)?.focusGeneration === focusGeneration ? gesture.id : null;
}

export function completeNativeSelectionAdvance(key, id) {
    const origin = findNativePicker(key);
    return origin ? completeSelectionAdvance(origin, id) : false;
}

export function claimTextEntry(key) {
    const origin = findEditor(textEntrySelector, "data-clay-text-commit", key);
    const entry = textEntries.get(origin);
    if (!entry) return null;
    textEntries.delete(origin);
    const lease = selectionLeases.get(origin);
    if (lease?.id !== entry.id || lease.focusGeneration !== focusGeneration || origin.value !== entry.value)
        return null;
    return entry;
}

export function completeTextEntry(key, id) {
    const origin = findEditor(textEntrySelector, "data-clay-text-commit", key);
    const lease = selectionLeases.get(origin);
    if (!lease || lease.id !== id) return false;
    if (origin.value !== lease.textValue) {
        selectionLeases.delete(origin);
        return false;
    }
    return completeSelectionAdvance(origin, id);
}

function popupField(key) {
    return Array.from(document.querySelectorAll(popupPickerSelector))
        .find(element => element.tagName === "INPUT" && element.getAttribute("data-clay-picker-key") === key &&
            isEditor(element.closest(scopeSelector), element));
}

function openPickerPaper(key) {
    return Array.from(document.querySelectorAll(popupPickerSelector))
        .find(element => element.tagName !== "INPUT" && element.getAttribute("data-clay-picker-key") === key &&
            element.closest(".mud-popover-open") && visible(element));
}

function capturePopupSelection(paper) {
    const key = paper?.getAttribute("data-clay-picker-key");
    if (!key || openPickerPaper(key) !== paper) return;
    const origin = popupField(key);
    const root = origin?.closest(scopeSelector);
    const state = scopes.get(root);
    if (!state?.ready || !state.advanceOnEnter || !isActiveScope(root) ||
        !ownsSelectionFocus(origin, paper))
        return;
    popupSelections.set(origin, { id: ++selectionId, root, state, epoch: state.epoch,
        interaction: interactionGeneration, paper, claimed: false });
}

function currentPopupSelection(origin, request) {
    return request && request.interaction === interactionGeneration && scopes.get(request.root) === request.state &&
        request.epoch === request.state.epoch && request.state.ready && isEditor(request.root, origin) &&
        isActiveScope(request.root) && ownsSelectionFocus(origin, request.paper);
}

export function claimPopupSelection(key) {
    const origin = popupField(key);
    const request = popupSelections.get(origin);
    if (!currentPopupSelection(origin, request) || request.claimed) return null;
    request.claimed = true;
    return request.id;
}

export function completePopupSelection(key, id) {
    const origin = popupField(key);
    const request = popupSelections.get(origin);
    if (!request?.claimed || request.id !== id) return;
    popupSelections.delete(origin);
    let attempts = 3;
    const advance = () => {
        if (!currentPopupSelection(origin, request)) return;
        if (openPickerPaper(key)) {
            if (--attempts > 0) requestAnimationFrame(advance);
            return;
        }
        const fields = editors(request.root);
        if (fields.includes(origin)) fields[fields.indexOf(origin) + 1]?.focus();
    };
    requestAnimationFrame(advance);
}

function isPlainEnter(event) {
    return event?.key === "Enter" && !event.defaultPrevented && !event.isComposing && event.keyCode !== 229 &&
        !event.altKey && !event.ctrlKey && !event.shiftKey && !event.metaKey;
}

function onKeyDown(root, state, event) {
    if (!state.ready || !state.advanceOnEnter || !isPlainEnter(event))
        return;

    const origin = event.target;
    if (origin.tagName !== "INPUT" || !plainInputTypes.has(origin.type) ||
        !isEditor(root, origin) || !isActiveScope(root) ||
        origin.closest('[data-clay-enter-native], [data-clay-focus-editor], [role="combobox"], .clay-select, .mud-picker'))
        return;

    event.preventDefault();
    if (origin.hasAttribute("data-clay-text-commit")) {
        if (!event.repeat && event.isTrusted && origin.validity?.valid !== false && origin.type !== "password") {
            const id = captureSelection(origin);
            if (id !== null) {
                selectionLeases.get(origin).textValue = origin.value;
                textEntries.set(origin, { id, value: origin.value });
                if (origin.hasAttribute("data-clay-text-deferred"))
                    origin.dispatchEvent(new Event("change", { bubbles: true }));
            }
            // The control commits and validates before the focus can advance.
            return;
        }
        event.stopPropagation();
        if (origin.validity?.valid === false) origin.reportValidity();
        return;
    }
    event.stopPropagation();
    if (event.repeat)
        return;

    if (origin.validity?.valid === false) {
        origin.reportValidity();
        return;
    }
    if (origin.getAttribute("aria-invalid") === "true")
        return;

    const fields = editors(root);
    fields[fields.indexOf(origin) + 1]?.focus();
}

function tryInitialFocus(root, state) {
    if (!state.ready || state.initialHandled || !visible(root) || !isActiveScope(root))
        return;

    const modal = modalFor(root);
    const active = document.activeElement;
    const modalPlaceholder = modal && isTrapPlaceholder(active, modal);
    const noInteraction = state.interaction === interactionGeneration;
    if (modal && active && !isNeutralFocus(active, root) && !modal.contains(active) && noInteraction && !state.initialRetried) {
        state.initialRetried = true;
        scheduleInitialFocus(root, state);
        return;
    }

    state.initialHandled = true;
    if ((isNeutralFocus(active, root) && state.generation === focusGeneration) || (modalPlaceholder && noInteraction))
        (editors(root)[0] ?? root).focus();
}

function isNeutralFocus(active, root) {
    return !active || active === document.body || active === document.documentElement || active === root ||
        active === modalFor(root);
}

function isTrapPlaceholder(active, modal) {
    const trap = active?.closest(".mud-focus-trap");
    return trap && modalFor(trap) === modal && (active === trap || active.parentElement === trap &&
        (active.matches(".mud-focus-trap-child-container") ||
            active.matches(".fixed") && active.matches(".pointer-events-none")));
}

function scheduleInitialFocus(root, state) {
    state.frame = requestAnimationFrame(() => {
        state.frame = null;
        tryInitialFocus(root, state);
    });
}

export function captureDialogReturn(root) {
    const state = scopes.get(root);
    if (!state || !root.isConnected || !isActiveScope(root)) return null;
    const id = ++selectionId;
    state.dialogReturn = { id, opener: document.activeElement };
    return id;
}

export function restoreDialogReturn(root, id) {
    const state = scopes.get(root);
    const request = state?.dialogReturn;
    if (request?.id !== id) return;
    state.dialogReturn = null;
    const interaction = interactionGeneration;
    let attempts = 3;
    const restore = () => {
        if (scopes.get(root) !== state || !root.isConnected || interaction !== interactionGeneration || !document.hasFocus())
            return;
        if (!state.ready || !isActiveScope(root)) {
            if (--attempts > 0) requestAnimationFrame(restore);
            return;
        }
        const active = document.activeElement;
        if (!isNeutralFocus(active, root)) return;
        const opener = request.opener;
        const owner = root.closest("[data-draft-owner]") ?? root;
        if (opener && owner.contains(opener) && modalFor(opener) === modalFor(root) &&
            visible(opener) && !opener.matches(":disabled"))
            opener.focus();
        else
            editors(root)[0]?.focus();
    };
    requestAnimationFrame(restore);
}

export function update(root, ready, advanceOnEnter) {
    if (!root?.isConnected)
        return;

    let state = scopes.get(root);
    if (!state) {
        state = { generation: focusGeneration, interaction: interactionGeneration, epoch: 0 };
        state.keyDown = event => onKeyDown(root, state, event);
        scopes.set(root, state);
        root.addEventListener("keydown", state.keyDown);
    }

    if (state.ready !== ready || state.advanceOnEnter !== advanceOnEnter)
        state.epoch++;
    state.ready = ready;
    state.advanceOnEnter = advanceOnEnter;
    if (ready && !state.initialHandled && !state.frame)
        scheduleInitialFocus(root, state);
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
