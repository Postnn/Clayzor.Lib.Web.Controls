// ── Clay Grid — Column Header Insert Drag ────────────────────────────────────
//
// Кастомный drag-and-drop заголовков колонок с INSERT-семантикой.
//
// Ключевые принципы:
//   - dragstart/dragover слушаются с capture:true — перехватываем ДО Blazor,
//     чтобы синхронно установить effectAllowed/dropEffect (иначе браузер
//     показывает запрещающий знак ещё до ответа SignalR).
//   - dragover на document с capture:true + e.preventDefault() во всех
//     обработчиках где мы над thead — убирает запрещающий знак.
//   - После drop вызывается C# OnColumnDrop(srcSql, targetSql, insertBefore).
//   - SetDraggedColumn(sql) устанавливает состояние конкретного grid для tray-drop.

window.clayGridColumnDrag = (function () {

    /** @type {Map<string, function>} gridId -> cleanup */
    var _cleanups = new Map();

    function init(gridId, dotnetRef) {
        // Убираем старые обработчики
        if (_cleanups.has(gridId)) {
            _cleanups.get(gridId)();
            _cleanups.delete(gridId);
        }

        var root = document.getElementById(gridId);
        if (!root) return;

        var srcSqlName = null;
        var indicator  = null;
        var scrollTimer = null;
        var scrollDirection = 0;
        var scrollHint = null;
        var lastClientX = 0;

        // ── Индикатор вставки ────────────────────────────────────────────────

        function getOrCreateIndicator() {
            if (!indicator) {
                indicator = document.createElement('div');
                indicator.className = 'clay-grid-drop-indicator';
                document.body.appendChild(indicator);
            }
            return indicator;
        }

        function showIndicator(targetEl, insertBefore) {
            var ind = getOrCreateIndicator();
            var r   = targetEl.getBoundingClientRect();
            var x   = insertBefore ? r.left : r.right;
            ind.style.left    = (x - 1) + 'px';
            ind.style.top     = r.top + 'px';
            ind.style.height  = r.height + 'px';
            ind.style.display = 'block';
        }

        function hideIndicator() {
            if (indicator) indicator.style.display = 'none';
        }

        function removeIndicator() {
            if (indicator) { indicator.remove(); indicator = null; }
        }

        function stopAutoScroll() {
            if (scrollTimer) clearInterval(scrollTimer);
            scrollTimer = null;
            scrollDirection = 0;
            root.classList.remove('clay-grid-auto-scrolling');
            root.removeAttribute('data-auto-scroll');
            if (scrollHint) { scrollHint.remove(); scrollHint = null; }
        }

        function showScrollHint(direction, rect) {
            if (!scrollHint) {
                scrollHint = document.createElement('div');
                scrollHint.className = 'clay-grid-auto-scroll-indicator';
                document.body.appendChild(scrollHint);
            }
            scrollHint.textContent = direction < 0 ? '← Прокрутка' : 'Прокрутка →';
            scrollHint.style.left = (direction < 0 ? rect.left + 8 : rect.right - 8) + 'px';
            scrollHint.style.top = (rect.top + 12) + 'px';
            scrollHint.style.transform = direction < 0 ? 'none' : 'translateX(-100%)';
        }

        function updateAutoScroll(clientX) {
            lastClientX = clientX;
            var container = root.querySelector('.mud-table-container');
            if (!container || container.scrollWidth <= container.clientWidth) {
                stopAutoScroll();
                return;
            }
            var rect = container.getBoundingClientRect();
            var edge = 56;
            var direction = 0;
            if (clientX >= rect.left && clientX <= rect.left + edge && container.scrollLeft > 0)
                direction = -1;
            else if (clientX <= rect.right && clientX >= rect.right - edge &&
                     container.scrollLeft + container.clientWidth < container.scrollWidth)
                direction = 1;
            if (!direction) {
                stopAutoScroll();
                return;
            }
            root.classList.add('clay-grid-auto-scrolling');
            root.setAttribute('data-auto-scroll', direction < 0 ? 'left' : 'right');
            showScrollHint(direction, rect);
            if (scrollTimer && scrollDirection === direction) return;
            if (scrollTimer) clearInterval(scrollTimer);
            scrollDirection = direction;
            scrollTimer = setInterval(function () {
                var before = container.scrollLeft;
                container.scrollLeft = Math.max(0, Math.min(
                    container.scrollWidth - container.clientWidth,
                    before + scrollDirection * 12));
                var target = findDropTarget(lastClientX);
                if (target) showIndicator(target.targetEl, target.insertBefore);
                if (container.scrollLeft === before) stopAutoScroll();
            }, 16);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        function getHeaderCells() {
            var divs = root.querySelectorAll('[data-col-sql]');
            var result = [];
            divs.forEach(function (div) {
                var sql = div.getAttribute('data-col-sql');
                if (!sql || sql === srcSqlName) return;
                var th = div.closest('th');
                if (!th) return;
                // Пропускаем скрытые колонки
                if (th.style.display === 'none' || th.hidden || th.offsetWidth === 0) return;
                result.push({ el: div, sqlName: sql, rect: div.getBoundingClientRect() });
            });
            return result;
        }

        function findDropTarget(clientX) {
            var cells = getHeaderCells();
            if (!cells.length) return null;
            for (var i = 0; i < cells.length; i++) {
                var c = cells[i];
                if (clientX >= c.rect.left && clientX <= c.rect.right) {
                    var mid = c.rect.left + c.rect.width / 2;
                    return { targetSql: c.sqlName, insertBefore: clientX < mid, targetEl: c.el };
                }
            }
            if (clientX < cells[0].rect.left)
                return { targetSql: cells[0].sqlName, insertBefore: true, targetEl: cells[0].el };
            var last = cells[cells.length - 1];
            return { targetSql: last.sqlName, insertBefore: false, targetEl: last.el };
        }

        function isOverOurThead(target) {
            var thead = target.closest ? target.closest('thead') : null;
            return thead && root.contains(thead);
        }

        // ── Обработчики (все с capture:true) ─────────────────────────────────

        function onDragStart(e) {
            var div = e.target.closest ? e.target.closest('[data-col-sql]') : null;
            if (!div || !root.contains(div)) return;
            srcSqlName = div.getAttribute('data-col-sql');
            if (!srcSqlName) return;
            // Синхронно — до Blazor
            e.dataTransfer.effectAllowed = 'move';
            try { e.dataTransfer.setData('text/plain', srcSqlName); } catch(_) {}
            try { e.dataTransfer.setDragImage(div, Math.min(24, div.offsetWidth / 2), div.offsetHeight / 2); } catch(_) {}
            // Уведомляем C# для tray-drop (async — успеет до drop)
            dotnetRef.invokeMethodAsync('SetDraggedColumn', srcSqlName).catch(function(){});
        }

        function onDragOver(e) {
            if (!srcSqlName) return;
            if (!isOverOurThead(e.target)) {
                hideIndicator();
                stopAutoScroll();
                return;
            }
            // Синхронно — до Blazor — убирает запрещающий знак
            e.preventDefault();
            e.stopPropagation();
            e.dataTransfer.dropEffect = 'move';
            updateAutoScroll(e.clientX);
            var target = findDropTarget(e.clientX);
            if (target) showIndicator(target.targetEl, target.insertBefore);
            else hideIndicator();
        }

        function onDrop(e) {
            if (!srcSqlName) return;
            if (!isOverOurThead(e.target)) {
                // Blazor tray-drop читает состояние этого grid до последующего dragend.
                hideIndicator();
                stopAutoScroll();
                return;
            }
            e.preventDefault();
            e.stopPropagation();
            var target = findDropTarget(e.clientX);
            if (!target || target.targetSql === srcSqlName) { cleanup(); return; }
            var src = srcSqlName;
            var tgt = target.targetSql;
            var before = target.insertBefore;
            cleanup();
            dotnetRef.invokeMethodAsync('OnColumnDrop', src, tgt, before).catch(function(){});
        }

        function onDragEnd(e) {
            if (!srcSqlName && !root.contains(e.target)) return;
            cleanup();
        }

        function cleanup() {
            if (srcSqlName) {
                dotnetRef.invokeMethodAsync('SetDraggedColumn', null).catch(function(){});
            }
            srcSqlName = null;
            hideIndicator();
            stopAutoScroll();
        }

        // Все обработчики с capture:true — перехватываем до Blazor
        document.addEventListener('dragstart', onDragStart, true);
        document.addEventListener('dragover',  onDragOver,  true);
        document.addEventListener('drop',      onDrop,      true);
        document.addEventListener('dragend',   onDragEnd,   true);

        _cleanups.set(gridId, function () {
            cleanup();
            document.removeEventListener('dragstart', onDragStart, true);
            document.removeEventListener('dragover',  onDragOver,  true);
            document.removeEventListener('drop',      onDrop,      true);
            document.removeEventListener('dragend',   onDragEnd,   true);
            removeIndicator();
            stopAutoScroll();
        });
    }

    function dispose(gridId) {
        if (_cleanups.has(gridId)) {
            _cleanups.get(gridId)();
            _cleanups.delete(gridId);
        }
    }

    return { init: init, dispose: dispose };

})();
