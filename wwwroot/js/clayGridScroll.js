// Сохранение и восстановление позиции прокрутки тела грида вокруг перезагрузки строк.
// FixedHeader+Height у MudDataGrid делают скроллящимся внутренний .mud-table-container.
window.clayGridScroll = (function () {
    function container(gridId) {
        var root = document.getElementById(gridId);
        return root ? root.querySelector('.mud-table-container') : null;
    }
    return {
        capture: function (gridId) {
            var c = container(gridId);
            return c ? c.scrollTop : 0;
        },
        // Восстанавливаем после того, как новый DOM отрисован. requestAnimationFrame
        // ждёт следующего кадра — к этому моменту MudDataGrid уже перестроил тело.
        restore: function (gridId, top) {
            if (!top) return;
            requestAnimationFrame(function () {
                var c = container(gridId);
                if (c) c.scrollTop = top;
            });
        }
    };
})();
