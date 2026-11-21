(() => {
    const instances = new Map();
    const itemSelector = ":scope > .clay-navigation-menu__list > .clay-navigation-menu__item > .clay-navigation-menu__link:not([aria-disabled=\"true\"]):not([disabled])";

    function pathDepth(path) {
        return path.split(".").length - 1;
    }

    function invoke(instance, method, path) {
        return instance.reference.invokeMethodAsync(method, path).catch(() => { });
    }

    function clearTimer(instance, path) {
        const timer = instance.timers.get(path);
        if (timer) clearTimeout(timer);
        instance.timers.delete(path);
    }

    function schedule(instance, path, opening) {
        clearTimer(instance, path);
        instance.timers.set(path, setTimeout(() => {
            instance.timers.delete(path);
            invoke(instance, opening ? "OpenAsync" : "ClosePanelAsync", path);
        }, instance.hoverDelay));
    }

    function focusPanel(instance, path) {
        let attempts = 0;
        const find = () => {
            const panel = document.getElementById(`${instance.id}-panel-${path}`);
            if (panel) {
                const first = panel.querySelector(itemSelector);
                if (first) first.focus();
                return;
            }
            if (++attempts < 40) requestAnimationFrame(find);
        };
        find();
    }

    function focusItem(panel, target) {
        if (!panel) return;
        const items = [...panel.querySelectorAll(itemSelector)];
        if (!items.length) return;
        const current = items.indexOf(document.activeElement);
        let index = target;
        if (target === "next") index = current < 0 ? 0 : (current + 1) % items.length;
        if (target === "previous") index = current < 0 ? items.length - 1 : (current - 1 + items.length) % items.length;
        if (target === "first") index = 0;
        if (target === "last") index = items.length - 1;
        items[index].focus();
        items[index].scrollIntoView({ block: "nearest" });
    }

    function positionPanels(instance, margin, maxWidth) {
        const viewportWidth = document.documentElement.clientWidth;
        const viewportHeight = document.documentElement.clientHeight;
        instance.root.querySelectorAll("[data-menu-panel]").forEach(panel => {
            const anchor = document.getElementById(panel.dataset.anchorId);
            if (!anchor) return;
            panel.style.visibility = "hidden";
            panel.style.maxWidth = `${maxWidth}px`;
            panel.style.maxHeight = `${viewportHeight - margin * 2}px`;
            let measured = panel.getBoundingClientRect();
            const scaleX = measured.width / panel.offsetWidth || 1;
            const scaleY = measured.height / panel.offsetHeight || 1;
            const widthLimit = Math.max(40, Math.min(maxWidth, viewportWidth / scaleX - margin * 2));
            panel.style.maxWidth = `${widthLimit}px`;
            measured = panel.getBoundingClientRect();
            const naturalWidth = Math.min(measured.width, widthLimit * scaleX);
            const naturalHeight = Math.min(measured.height, viewportHeight - margin * 2);
            const path = panel.dataset.menuPanel;
            const rect = anchor.getBoundingClientRect();
            let left;
            let top;
            let availableHeight;

            if (pathDepth(path) === 0) {
                const rightSpace = viewportWidth - rect.right - margin;
                left = rightSpace >= naturalWidth ? rect.left : rect.right - naturalWidth;
                top = rect.bottom + 2;
                const bottomSpace = viewportHeight - top - margin;
                const topSpace = rect.top - margin;
                if (naturalHeight > bottomSpace && topSpace > bottomSpace) {
                    availableHeight = Math.max(24, topSpace);
                    top = Math.max(margin, rect.top - Math.min(naturalHeight, availableHeight) - 2);
                } else {
                    availableHeight = Math.max(24, bottomSpace);
                }
            } else {
                const rightSpace = viewportWidth - rect.right - margin;
                left = rightSpace >= naturalWidth
                    ? rect.right + 2
                    : rect.left - naturalWidth - 2;
                top = rect.top;
                const bottomSpace = viewportHeight - top - margin;
                const topSpace = rect.bottom - margin;
                if (naturalHeight > bottomSpace && topSpace > bottomSpace) {
                    availableHeight = Math.max(24, topSpace);
                    top = Math.max(margin, rect.bottom - Math.min(naturalHeight, availableHeight));
                } else {
                    availableHeight = Math.max(24, bottomSpace);
                }
            }

            left = Math.max(margin, Math.min(left, viewportWidth - naturalWidth - margin));
            panel.style.maxHeight = `${Math.min(availableHeight, viewportHeight - margin * 2) / scaleY}px`;
            const finalHeight = Math.min(naturalHeight, availableHeight);
            top = Math.max(margin, Math.min(top, viewportHeight - finalHeight - margin));
            panel.style.left = `${left / scaleX}px`;
            panel.style.top = `${top / scaleY}px`;
            panel.style.visibility = "visible";
        });
    }

    function refresh(instance, margin, maxWidth) {
        instance.root.querySelectorAll("[data-menu-container]").forEach(container => {
            const activator = container.querySelector(":scope > [role=menuitem]");
            if (activator?.getAttribute("aria-haspopup") !== "true") {
                container.onpointerenter = null;
                container.onpointerleave = null;
                return;
            }
            const path = container.dataset.menuContainer;
            container.onpointerenter = event => {
                if (event.pointerType && event.pointerType !== "mouse") return;
                schedule(instance, path, true);
            };
            container.onpointerleave = event => {
                if (event.pointerType && event.pointerType !== "mouse") return;
                schedule(instance, path, false);
            };
        });
        positionPanels(instance, margin, maxWidth);
    }

    function onKeyDown(instance, event) {
        if (!instance.root.contains(event.target)) return;
        if (event.key === "Escape") {
            event.preventDefault();
            const open = instance.root.querySelector("[data-menu-panel]");
            const anchor = open ? document.getElementById(open.dataset.anchorId) : instance.root.querySelector("[role=menuitem]");
            invoke(instance, "CloseAllAsync");
            if (anchor) anchor.focus();
            return;
        }

        const panel = event.target.closest("[data-menu-panel]");
        if (!panel) {
            if (event.key !== "ArrowLeft" && event.key !== "ArrowRight" && event.key !== "ArrowDown") return;
            const roots = [...instance.root.querySelectorAll(":scope > .clay-navigation-menu__roots > .clay-navigation-menu__root > [role=menuitem]")];
            const current = roots.indexOf(event.target.closest("[role=menuitem]"));
            if (event.key === "ArrowDown") {
                const path = event.target.closest("[data-menu-container]")?.dataset.menuContainer;
                if (path !== undefined) {
                    event.preventDefault();
                    invoke(instance, "OpenAsync", path);
                    focusPanel(instance, path);
                }
                return;
            }
            if (!roots.length) return;
            event.preventDefault();
            const direction = event.key === "ArrowRight" ? 1 : -1;
            roots[(current + direction + roots.length) % roots.length].focus();
            return;
        }

        if (event.key === "ArrowDown" || event.key === "ArrowUp" || event.key === "Home" || event.key === "End") {
            event.preventDefault();
            focusItem(panel, event.key === "ArrowDown" ? "next" : event.key === "ArrowUp" ? "previous" : event.key === "Home" ? "first" : "last");
            return;
        }

        const path = panel.dataset.menuPanel;
        if (event.key === "ArrowRight") {
            const container = event.target.closest("[data-menu-container]");
            const childPath = container?.dataset.menuContainer;
            if (childPath && childPath.startsWith(`${path}.`)) {
                event.preventDefault();
                invoke(instance, "OpenAsync", childPath);
                focusPanel(instance, childPath);
            }
            return;
        }
        if (event.key === "ArrowLeft") {
            event.preventDefault();
            invoke(instance, "ClosePanelAsync", path);
            const anchor = document.getElementById(panel.dataset.anchorId);
            if (anchor) anchor.focus();
            return;
        }
        if (event.key === "Tab") {
            setTimeout(() => {
                if (!instance.root.contains(document.activeElement)) invoke(instance, "CloseAllAsync");
            }, 0);
        }
    }

    window.clayNavigationMenu = {
        init(id, reference, hoverDelay) {
            const root = document.getElementById(id);
            if (!root) return;
            const instance = {
                id,
                root,
                reference,
                hoverDelay: Math.max(0, hoverDelay || 300),
                timers: new Map(),
                margin: 8,
                maxWidth: 360,
                pointerHandler: null,
                keyHandler: null,
                resizeHandler: null,
                scrollHandler: null,
                errorHandler: null
            };
            instance.pointerHandler = event => {
                if (!root.contains(event.target)) invoke(instance, "CloseAllAsync");
            };
            instance.keyHandler = event => onKeyDown(instance, event);
            instance.resizeHandler = () => positionPanels(instance, instance.margin, instance.maxWidth);
            instance.scrollHandler = event => {
                if (event.target instanceof Element && root.contains(event.target)) return;
                positionPanels(instance, instance.margin, instance.maxWidth);
            };
            instance.errorHandler = event => {
                if (event.target instanceof HTMLImageElement) event.target.style.display = "none";
            };
            document.addEventListener("pointerdown", instance.pointerHandler);
            document.addEventListener("keydown", instance.keyHandler);
            window.addEventListener("resize", instance.resizeHandler);
            window.addEventListener("scroll", instance.scrollHandler, true);
            root.addEventListener("error", instance.errorHandler, true);
            instances.set(id, instance);
        },
        refresh(id, margin, maxWidth) {
            const instance = instances.get(id);
            if (!instance) return;
            instance.margin = Math.max(0, margin);
            instance.maxWidth = Math.max(40, maxWidth);
            refresh(instance, instance.margin, instance.maxWidth);
        },
        dispose(id) {
            const instance = instances.get(id);
            if (!instance) return;
            instance.timers.forEach(timer => clearTimeout(timer));
            document.removeEventListener("pointerdown", instance.pointerHandler);
            document.removeEventListener("keydown", instance.keyHandler);
            window.removeEventListener("resize", instance.resizeHandler);
            window.removeEventListener("scroll", instance.scrollHandler, true);
            instance.root.removeEventListener("error", instance.errorHandler, true);
            instances.delete(id);
        }
    };
})();
