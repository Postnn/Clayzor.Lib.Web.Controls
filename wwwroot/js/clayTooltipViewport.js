(() => {
    const margin = 8;
    const clamp = tooltip => {
        const left = Number.parseFloat(tooltip.style.left);
        if (!Number.isFinite(left)) return;
        const width = tooltip.getBoundingClientRect().width;
        const availableWidth = document.documentElement.clientWidth;
        const clamped = Math.max(margin, Math.min(left, availableWidth - width - margin));
        if (Math.abs(clamped - left) > 0.5)
            tooltip.style.left = `${clamped}px`;
    };
    const clampAll = () => document.querySelectorAll(".mud-tooltip.mud-popover-open").forEach(clamp);
    const observer = new MutationObserver(clampAll);
    observer.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ["style", "class"] });
    window.addEventListener("resize", clampAll);
    window.addEventListener("scroll", clampAll, true);
})();
