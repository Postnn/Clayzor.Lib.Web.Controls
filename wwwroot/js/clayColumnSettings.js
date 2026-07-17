// ── Clay Column Settings Dialog — Sortable drag-and-drop ────────────────────
//
// jQuery UI Sortable-подобный механизм.
// Важно: JS не удаляет и не заменяет DOM-узлы Blazor (replaceChild/removeChild
// на Blazor-элементах конфликтует с reconciliation и вызывает insertBefore crash).
//
// Принцип:
//   - Источник скрывается через visibility:hidden (остаётся в Blazor-дереве)
//   - Placeholder — отдельный div, вставляется через insertBefore (Blazor его не знает)
//   - Ghost следует за курсором (position:fixed, вне контейнера)
//   - После mouseup: cleanup() убирает ghost+placeholder, вызывает OnJsDrop
//   - Blazor получает (srcIdx, targetIdx), переставляет _items, делает StateHasChanged
//
// targetIdx — индекс вставки в оригинальном N-элементном массиве Blazor

window.clayColumnSettings = (function () {

    return {
        init: function (container, dotnetRef) {

            var dragging    = false;
            var sourceChip  = null;   // оригинальный DOM-узел (скрыт)
            var sourceIdx   = -1;     // data-col-idx источника
            var ghost       = null;   // клон, следует за курсором
            var placeholder = null;   // маркер позиции вставки
            var grabOffsetX = 0;
            var grabOffsetY = 0;
            var chipH       = 0;
            var chipW       = 0;
            var HYST           = 8;      // мёртвая зона (px) у центра чипа
            var scrollInterval = null;   // интервал авто-прокрутки
            var scrollParent   = null;   // ближайший скроллируемый предок контейнера
            var SCROLL_ZONE    = 40;     // зона авто-прокрутки (px) у краёв контейнера
            var MAX_SCROLL     = 15;     // макс. скорость авто-прокрутки (px/фрейм)

            // ── helpers ──────────────────────────────────────────────────────

            /** Видимые чипы Blazor (без источника, без placeholder). */
            function getChips() {
                return Array.from(container.querySelectorAll('[data-col-idx]'))
                    .filter(function (c) { return c !== sourceChip; });
            }

            function createGhost(chip) {
                var g = chip.cloneNode(true);
                g.className      = 'column-settings-chip column-settings-ghost';
                g.style.width    = chipW + 'px';
                g.style.height   = chipH + 'px';
                g.style.position = 'fixed';
                g.style.zIndex   = '9999';
                g.style.pointerEvents = 'none';
                g.style.margin   = '0';
                var cs = getComputedStyle(container);
                g.style.setProperty('--clay-cs-group-w',  cs.getPropertyValue('--clay-cs-group-w'));
                g.style.setProperty('--clay-cs-filter-w', cs.getPropertyValue('--clay-cs-filter-w'));
                document.body.appendChild(g);
                return g;
            }

            function createPlaceholder() {
                var p = document.createElement('div');
                p.className    = 'column-settings-placeholder';
                p.style.height = chipH + 'px';
                // Пометим чтобы не попал в getChips()
                p.dataset.placeholder = '1';
                return p;
            }

            function moveGhost(x, y) {
                if (!ghost) return;
                ghost.style.left = (x - grabOffsetX) + 'px';
                ghost.style.top  = (y - grabOffsetY) + 'px';
            }

            /**
             * Перемещает placeholder в нужную позицию по курсору.
             * Placeholder вставляется insertBefore — Blazor его не знает, конфликта нет.
             */
            function movePlaceholder(clientY) {
                if (!container || !placeholder) return;
                var chips = getChips();
                for (var i = 0; i < chips.length; i++) {
                    var chip = chips[i];
                    var rect = chip.getBoundingClientRect();
                    var mid  = rect.top + rect.height / 2;
                    if (clientY < mid - HYST) {
                        if (!chip.parentNode) return;
                        if (placeholder.nextSibling !== chip)
                            container.insertBefore(placeholder, chip);
                        return;
                    }
                }
                // Курсор ниже всех — в конец контейнера
                if (container && container.lastChild !== placeholder)
                    container.appendChild(placeholder);
            }

            /**
             * Находит ближайшего предка с прокруткой.
             */
            function getScrollParent(node) {
                while (node && node !== document.body) {
                    if (node.scrollHeight > node.clientHeight) return node;
                    node = node.parentElement;
                }
                return null;
            }

            /**
             * Авто-прокрутка контейнера при приближении курсора к верхнему/нижнему краю.
             * Скорость растёт по мере приближения к краю.
             */
            function autoScroll(clientY) {
                if (!scrollParent) return;

                if (scrollInterval) {
                    clearInterval(scrollInterval);
                    scrollInterval = null;
                }

                var rect = scrollParent.getBoundingClientRect();
                var distFromTop = clientY - rect.top;
                var distFromBottom = rect.bottom - clientY;

                if (distFromTop > 0 && distFromTop < SCROLL_ZONE) {
                    var speed = MAX_SCROLL * (1 - distFromTop / SCROLL_ZONE);
                    scrollInterval = setInterval(function () {
                        scrollParent.scrollTop = Math.max(0, scrollParent.scrollTop - speed);
                    }, 16);
                } else if (distFromBottom > 0 && distFromBottom < SCROLL_ZONE) {
                    var speed = MAX_SCROLL * (1 - distFromBottom / SCROLL_ZONE);
                    scrollInterval = setInterval(function () {
                        scrollParent.scrollTop = Math.min(
                            scrollParent.scrollHeight - scrollParent.clientHeight,
                            scrollParent.scrollTop + speed);
                    }, 16);
                }
            }

            /**
             * Вычисляет targetIdx для Blazor.
             * Считаем количество чипов (data-col-idx) перед placeholder,
             * не считая сам источник (он скрыт но присутствует в DOM).
             * Это даёт позицию вставки в массиве после RemoveAt(sourceIdx).
             */
            function getTargetIdx() {
                if (!placeholder.parentNode) return sourceIdx;
                var allNodes = Array.from(container.childNodes);
                var phPos    = allNodes.indexOf(placeholder);
                var count    = 0;
                for (var i = 0; i < phPos; i++) {
                    var n = allNodes[i];
                    if (n === sourceChip) continue; // не считаем источник
                    if (n.hasAttribute && n.hasAttribute('data-col-idx')) count++;
                }
                return count;
            }

            function cleanup() {
                if (scrollInterval) { clearInterval(scrollInterval); scrollInterval = null; }
                scrollParent = null;
                if (ghost) { ghost.remove(); ghost = null; }
                if (placeholder && placeholder.parentNode)
                    placeholder.parentNode.removeChild(placeholder);
                placeholder = null;
                if (sourceChip) {
                    sourceChip.style.display = '';
                    sourceChip = null;
                }
                dragging  = false;
                sourceIdx = -1;
                document.removeEventListener('mousemove',  onMove);
                document.removeEventListener('mouseup',    onEnd);
                document.removeEventListener('touchmove',  onMove);
                document.removeEventListener('touchend',   onEnd);
            }

            // ── Обработчики ──────────────────────────────────────────────────

            function onKeyDown(e) {
                if (e.key === 'Escape' && dragging) cleanup();
            }

            // ── Общие обработчики движения и отпускания (мышь + touch) ──────

            function onMove(e) {
                if (!dragging) return;
                var pt = e.touches ? e.touches[0] : e;
                moveGhost(pt.clientX, pt.clientY);
                if (!placeholder.parentNode)
                    container.appendChild(placeholder);
                movePlaceholder(pt.clientY);
                autoScroll(pt.clientY);
            }

            function onEnd(e) {
                if (!dragging) return;
                var targetIdx = getTargetIdx();
                var src       = sourceIdx;
                cleanup();
                dotnetRef.invokeMethodAsync('OnJsDrop', src, targetIdx);
            }

            // ── Инициализация ────────────────────────────────────────────────

            function startDrag(e, clientX, clientY) {
                var chip = e.target.closest('[data-col-idx]');
                if (!chip) return;
                if (e.target.closest('input, button, .mud-switch-base, .mud-button-root, .chip-label-clickable, .sort-toggle-area')) return;

                e.preventDefault();

                var rect    = chip.getBoundingClientRect();
                chipW       = rect.width;
                chipH       = rect.height;
                grabOffsetX = clientX - rect.left;
                grabOffsetY = clientY - rect.top;
                sourceIdx   = parseInt(chip.dataset.colIdx, 10);
                sourceChip  = chip;

                scrollParent = getScrollParent(container);

                ghost       = createGhost(chip);
                placeholder = createPlaceholder();

                chip.style.display = 'none';
                void container.offsetHeight;

                moveGhost(clientX, clientY);
                dragging = true;

                document.addEventListener('mousemove',  onMove);
                document.addEventListener('mouseup',    onEnd);
                document.addEventListener('touchmove',  onMove,  { passive: false });
                document.addEventListener('touchend',   onEnd);
            }

            container.addEventListener('mousedown', function (e) {
                if (e.button !== 0) return;
                startDrag(e, e.clientX, e.clientY);
            });

            container.addEventListener('touchstart', function (e) {
                if (e.touches.length !== 1) return;
                startDrag(e, e.touches[0].clientX, e.touches[0].clientY);
            }, { passive: false });

            document.addEventListener('keydown', onKeyDown);
        },

        /**
         * Читает текущий порядок колонок из DOM (по атрибутам data-col-sql).
         * @param {string} elementId — DOM-id корневого элемента грида
         * @returns {string[]} — массив SQL-имён в порядке отображения
         */
        readOrder: function (elementId) {
            var root = document.getElementById(elementId);
            if (!root) return [];
            var chips = root.querySelectorAll('[data-col-sql]');
            return Array.from(chips).map(function (c) { return c.dataset.colSql; });
        }
    };

})();
