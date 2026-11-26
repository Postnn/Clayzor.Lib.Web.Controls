window.clayTree = window.clayTree || {};
window.clayTree.isTextTruncated = function (el) {
    if (!el) return false;
    return el.scrollWidth > el.clientWidth;
};

(() => {
    const bindings = new WeakMap();
    const itemSelector = "[data-clay-tree-node]";
    const hiddenSelector = '[hidden], [inert], [aria-hidden="true"]';
    let generation = 0;

    document.addEventListener("focusin", () => generation++, true);
    document.addEventListener("pointerdown", () => generation++, true);

    function owner(node) {
        return node?.closest('[role="tree"]');
    }

    function visibleNodes(root) {
        return [...root.querySelectorAll(itemSelector)].filter(node =>
            owner(node) === root && !node.closest(hiddenSelector) &&
            node.getClientRects().length > 0 && getComputedStyle(node).visibility === "visible");
    }

    function setTabStop(root, node) {
        for (const item of root.querySelectorAll(itemSelector)) {
            if (owner(item) === root)
                item.tabIndex = item === node ? 0 : -1;
        }
    }

    function activateFocus(root, state, node) {
        if (!node)
            return;

        state.anchorId = node.dataset.clayTreeNode;
        setTabStop(root, node);
        if (document.activeElement !== node)
            node.focus();
    }

    function sameModal(root) {
        const modal = root.closest(".mud-dialog-container, [aria-modal]");
        const current = [...document.querySelectorAll('.mud-dialog-container, [aria-modal="true"]')]
            .filter(dialog => !dialog.closest(hiddenSelector) && dialog.getClientRects().length > 0)
            .at(-1);
        return !current || modal === current || current.contains(root);
    }

    function synchronize(root, state) {
        if (!root.isConnected)
            return;
        const visible = visibleNodes(root);
        const saved = visible.find(node => node.dataset.clayTreeNode === state.anchorId);
        const selected = visible.find(node => node.getAttribute("aria-selected") === "true");
        const target = saved ?? selected ?? visible[0];
        setTabStop(root, target);

        const previous = state.focused;
        if (!target || !previous || visible.includes(previous))
            return;

        state.focused = null;
        if (state.generation === generation && document.hasFocus() && sameModal(root) &&
            (!document.activeElement || document.activeElement === document.body || document.activeElement === document.documentElement))
            activateFocus(root, state, target);
    }

    function request(state, node, action) {
        if (state.pending)
            return;
        state.pending = true;
        state.receiver.invokeMethodAsync("InvokeKeyboardActionAsync", node.dataset.clayTreeNode, action)
            .catch(() => {})
            .finally(() => { state.pending = false; });
    }

    function handleKey(root, state, event) {
        if (event.defaultPrevented || event.isComposing || event.keyCode === 229 ||
            event.altKey || event.ctrlKey || event.metaKey || event.shiftKey)
            return;

        const node = event.target;
        if (!node.matches(itemSelector) || owner(node) !== root)
            return;
        const visible = visibleNodes(root);
        const index = visible.indexOf(node);
        if (index < 0)
            return;

        const expanded = node.getAttribute("aria-expanded");
        let target;
        let action;
        switch (event.key) {
            case "ArrowUp": target = visible[index - 1]; break;
            case "ArrowDown": target = visible[index + 1]; break;
            case "Home": target = visible[0]; break;
            case "End": target = visible.at(-1); break;
            case "ArrowLeft":
                if (expanded === "true") action = "collapse";
                else target = node.parentElement?.closest(itemSelector);
                break;
            case "ArrowRight":
                if (expanded === "false") action = "expand";
                else if (expanded === "true" && visible[index + 1]?.parentElement?.closest(itemSelector) === node)
                    target = visible[index + 1];
                break;
            case "Enter": action = "activate"; break;
            case " ":
                if (!node.hasAttribute("aria-checked")) return;
                action = "check";
                break;
            default: return;
        }

        event.preventDefault();
        event.stopPropagation();
        if (action && !event.repeat)
            request(state, node, action);
        else if (target && visible.includes(target))
            activateFocus(root, state, target);
    }

    function captionNode(root, event) {
        const node = event.target.closest("[data-clay-tree-caption]")?.closest(itemSelector);
        return owner(node) === root ? node : null;
    }

    window.clayTree.attachKeyboard = (root, receiver) => {
        if (!root?.isConnected || bindings.has(root))
            return;

        const controller = new AbortController();
        const options = { signal: controller.signal };
        const state = { receiver };
        root.addEventListener("keydown", event => handleKey(root, state, event), options);
        root.addEventListener("focusin", event => {
            if (owner(event.target) !== root || !event.target.matches(itemSelector))
                return;
            state.focused = event.target;
            state.generation = generation;
            state.anchorId = event.target.dataset.clayTreeNode;
            setTabStop(root, event.target);
        }, options);
        root.addEventListener("focusout", event => {
            if (event.target === state.focused && (event.relatedTarget || event.target.isConnected))
                state.focused = null;
        }, options);
        root.addEventListener("click", event => {
            const node = captionNode(root, event);
            if (node)
                activateFocus(root, state, node);
        }, options);
        root.addEventListener("dblclick", event => {
            const node = captionNode(root, event);
            const expanded = node?.getAttribute("aria-expanded");
            if (expanded === "true" || expanded === "false")
                request(state, node, expanded === "true" ? "collapse" : "expand");
        }, options);

        const observer = new MutationObserver(() => synchronize(root, state));
        observer.observe(root, { childList: true, subtree: true, attributes: true,
            attributeFilter: ["hidden", "inert", "aria-hidden", "aria-selected"] });
        bindings.set(root, { controller, observer });
        synchronize(root, state);
    };

    window.clayTree.detachKeyboard = root => {
        const binding = bindings.get(root);
        if (!binding)
            return;
        binding.controller.abort();
        binding.observer.disconnect();
        bindings.delete(root);
    };
})();
