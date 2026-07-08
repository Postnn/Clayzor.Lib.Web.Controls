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
//   - SetDraggedColumn(sql) устанавливает ClayDragState для tray-drop.

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
            // Уведомляем C# для tray-drop (async — успеет до drop)
            dotnetRef.invokeMethodAsync('SetDraggedColumn', srcSqlName).catch(function(){});
        }

        function onDragOver(e) {
            if (!srcSqlName) return;
            if (!isOverOurThead(e.target)) {
                hideIndicator();
                return;
            }
            // Синхронно — до Blazor — убирает запрещающий знак
            e.preventDefault();
            e.stopPropagation();
            e.dataTransfer.dropEffect = 'move';
            var target = findDropTarget(e.clientX);
            if (target) showIndicator(target.targetEl, target.insertBefore);
            else hideIndicator();
        }

        function onDrop(e) {
            if (!srcSqlName) return;
            if (!isOverOurThead(e.target)) {
                // Drop on grouping tray — don't call cleanup(), let Blazor's
                // OnTrayDrop read ClayDragState.DraggedColumn. We only clear
                // JS-side state; the C# handler will call SetDraggedColumn(null).
                srcSqlName = null;
                hideIndicator();
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
        }

        // Все обработчики с capture:true — перехватываем до Blazor
        document.addEventListener('dragstart', onDragStart, true);
        document.addEventListener('dragover',  onDragOver,  true);
        document.addEventListener('drop',      onDrop,      true);
        document.addEventListener('dragend',   onDragEnd,   true);

        _cleanups.set(gridId, function () {
            document.removeEventListener('dragstart', onDragStart, true);
            document.removeEventListener('dragover',  onDragOver,  true);
            document.removeEventListener('drop',      onDrop,      true);
            document.removeEventListener('dragend',   onDragEnd,   true);
            removeIndicator();
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
