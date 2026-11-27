const watchers = new WeakMap();
const controlSelector = 'input[type="password"], input[data-draft-exclude], [data-draft-exclude] input';
const eventNames = ['beforeinput', 'input', 'change', 'compositionstart', 'compositionend'];

function controls(root) {
    return Array.from(root.querySelectorAll(controlSelector));
}

function current(root, generation) {
    const state = root && watchers.get(root);
    return root?.isConnected && state?.generation === generation ? state : null;
}

function checkControls(root, state) {
    const next = controls(root);
    if (next.length !== state.controls.length ||
        next.some((control, index) => control !== state.controls[index])) {
        state.controls = next;
        state.composing.clear();
        state.revision++;
    }
    return next.length > 0;
}

function unfreeze(root, state) {
    if (!state.freeze) return;
    root.inert = state.freeze.originalInert;
    state.freeze = null;
}

// This module never reads an input value. Its state is limited to a mounted editor.
export function watchTransientSensitiveInputs(root, generation) {
    if (!root?.isConnected || !generation) return false;
    if (current(root, generation)) return true;
    const previous = watchers.get(root);
    if (previous) unwatchTransientSensitiveInputs(root, previous.generation);
    const state = {
        generation, controls: controls(root), composing: new Set(), dirty: false,
        revision: 0, freezeSequence: 0, freeze: null,
    };
    state.listener = event => {
        const control = event.target;
        if (!control?.matches?.(controlSelector) || !root.contains(control) || control.disabled) return;
        checkControls(root, state);
        if (event.type === 'beforeinput') {
            if (state.freeze && event.cancelable) event.preventDefault();
            return;
        }
        if (event.type === 'compositionstart') {
            state.composing.add(control);
            state.revision++;
            return;
        }
        if (event.type === 'compositionend') {
            state.composing.delete(control);
            state.revision++;
            return;
        }
        if (event.isComposing) state.composing.add(control);
        state.dirty = true;
        state.revision++;
    };
    for (const name of eventNames) root.addEventListener(name, state.listener, true);
    watchers.set(root, state);
    return true;
}

export function readTransientSensitiveInputs(root, generation) {
    const state = current(root, generation);
    if (!state || !checkControls(root, state)) return null;
    return { revision: state.revision, dirty: state.dirty, composing: state.composing.size > 0 };
}

export function freezeTransientSensitiveInputs(root, generation, expectedRevision) {
    const state = current(root, generation);
    if (!state || !checkControls(root, state) || state.freeze ||
        state.revision !== expectedRevision || state.composing.size > 0) return null;

    const lease = ++state.freezeSequence;
    state.freeze = { lease, revision: expectedRevision, originalInert: root.inert };
    root.inert = true;
    return lease;
}

export function transientSensitiveFreezeIsCurrent(root, generation, lease) {
    const state = current(root, generation);
    return Boolean(state && checkControls(root, state) && state.freeze?.lease === lease &&
        state.freeze.revision === state.revision && state.composing.size === 0 &&
        root.inert);
}

export function releaseTransientSensitiveInputs(root, generation, lease, resetAccepted = false) {
    const state = root && watchers.get(root);
    if (!state || state.generation !== generation || state.freeze?.lease !== lease) return false;
    if (resetAccepted && transientSensitiveFreezeIsCurrent(root, generation, lease)) {
        state.dirty = false;
        state.revision++;
    }
    unfreeze(root, state);
    return true;
}

export function unwatchTransientSensitiveInputs(root, generation) {
    const state = root && watchers.get(root);
    if (!state || state.generation !== generation) return;
    unfreeze(root, state);
    for (const name of eventNames) root.removeEventListener(name, state.listener, true);
    watchers.delete(root);
}
