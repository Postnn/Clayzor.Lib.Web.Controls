window.clayTree = window.clayTree || {};
window.clayTree.isTextTruncated = function (el) {
    if (!el) return false;
    return el.scrollWidth > el.clientWidth;
};
