window.claySelectAdvancedSearch = window.claySelectAdvancedSearch || {
    requests: Object.create(null),

    open: function (url, requestId, dotnetRef) {
        const target = new URL(url, window.location.href);
        if (!['http:', 'https:'].includes(target.protocol) || target.username || target.password)
            throw new Error('Invalid advanced search URL');
        target.searchParams.set('return', '1');
        target.searchParams.set('requestId', requestId);
        target.searchParams.set('returnOrigin', window.location.origin);
        const formKey = 'advanced-search:' + target.origin + target.pathname.toLowerCase().replace(/\.aspx$/, '');
        const child = window.clayFormWindow.openHandle(target.toString(), formKey,
            { width: 1100, height: 700, left: 50, top: 50, resizable: true }, `clay-search-${requestId}`);
        if (!child) {
            void dotnetRef.invokeMethodAsync('CancelAsync', requestId).catch(() => {});
            return;
        }

        const request = { child, cleanup: null };
        const cleanup = () => {
            window.removeEventListener('message', onMessage);
            window.clearInterval(closeTimer);
            if (this.requests[requestId] === request)
                delete this.requests[requestId];
        };
        request.cleanup = cleanup;
        this.requests[requestId] = request;

        const onMessage = event => {
            if (event.origin !== target.origin || event.source !== child) return;
            if (event.data?.type !== 'clay-select-return' || event.data?.requestId !== requestId
                || !Object.hasOwn(event.data, 'value') || event.data.value == null) return;
            cleanup();
            void dotnetRef.invokeMethodAsync('CompleteAsync', requestId, event.data.value)
                .catch(() => {}).finally(() => child.close());
        };
        const closeTimer = window.setInterval(() => {
            if (!child.closed) return;
            cleanup();
            void dotnetRef.invokeMethodAsync('CancelAsync', requestId).catch(() => {});
        }, 500);
        window.addEventListener('message', onMessage);
    },

    cancel: function (requestId) {
        const request = this.requests[requestId];
        if (!request) return;
        request.cleanup();
        if (!request.child.closed)
            request.child.close();
    },

    returnValue: function (requestId, value) {
        if (!window.opener || window.opener.closed || !/^[a-f\d]{32}$/i.test(requestId)) return false;
        const query = new URLSearchParams(window.location.search);
        if (query.get('return') !== '1' || query.get('requestId') !== requestId) return false;
        let openerOrigin;
        try { openerOrigin = window.opener.location.origin; }
        catch (_) { openerOrigin = document.referrer ? new URL(document.referrer).origin : null; }
        if (!openerOrigin || openerOrigin === 'null' || query.get('returnOrigin') !== openerOrigin) return false;
        window.opener.postMessage({ type: 'clay-select-return', requestId, value }, openerOrigin);
        return true;
    },

    close: function () { if (window.opener) window.close(); }
};
