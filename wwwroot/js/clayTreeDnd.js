window.clayTreeDnd = window.clayTreeDnd || {};

/**
 * Определяет зону строки по вертикальной позиции курсора внутри неё.
 * @param {Element} rowEl — DOM-элемент строки (.clay-tree-node-row)
 * @param {number} clientY — ClientY из DragEventArgs
 * @returns {'before' | 'on' | 'after'}
 */
window.clayTreeDnd.zone = function (rowEl, clientY) {
    if (!rowEl) return 'on';
    var rect = rowEl.getBoundingClientRect();
    var y = clientY - rect.top;
    var h = rect.height;
    if (y < h * 0.25) return 'before';
    if (y > h * 0.75) return 'after';
    return 'on';
};
