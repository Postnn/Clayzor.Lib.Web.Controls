// Наблюдение за сентинелом пагинации уровня дерева (IntersectionObserver).
// Используется в ClayTreeNodeView для режима Scroll.
window.clayTreePaging = (function () {
    var _observer = null;

    function ensureObserver() {
        if (_observer) return _observer;
        _observer = new IntersectionObserver(function (entries) {
            for (var i = 0; i < entries.length; i++) {
                var e = entries[i];
                if (e.isIntersecting && e.target._clayDotNetRef) {
                    e.target._clayDotNetRef.invokeMethodAsync("OnSentinelVisible");
                }
            }
        }, { root: null, threshold: 0 });
        return _observer;
    }

    return {
        observe: function (element, dotNetRef) {
            if (!element) return;
            element._clayDotNetRef = dotNetRef;
            ensureObserver().observe(element);
        },
        unobserve: function (element) {
            if (!element || !_observer) return;
            _observer.unobserve(element);
            delete element._clayDotNetRef;
        }
    };
})();
