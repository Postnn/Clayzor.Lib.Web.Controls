window.claySelectAdvancedSearch = window.claySelectAdvancedSearch || {
    requests: {},

    open: function (url, requestId, dotnetRef) {
        const target = new URL(url, window.location.href);
        target.searchParams.set('return', '1');
        target.searchParams.set('requestId', requestId);
        target.searchParams.set('returnOrigin', window.location.origin);
        const child = window.clayOpen(target.toString(), 'advanced-search', { width: 1100, height: 700, left: 50, top: 50, resizable: true });
        if (!child) {
            dotnetRef.invokeMethodAsync('CancelAsync', requestId);
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
            if (event.data?.type !== 'clay-select-return' || event.data?.requestId !== requestId) return;
            cleanup();
            dotnetRef.invokeMethodAsync('CompleteAsync', requestId, event.data.value);
            child.close();
        };
        const closeTimer = window.setInterval(() => {
            if (!child.closed) return;
            cleanup();
            dotnetRef.invokeMethodAsync('CancelAsync', requestId);
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
        if (!window.opener) return false;
        const returnOrigin = new URLSearchParams(window.location.search).get('returnOrigin');
        if (!returnOrigin && !document.referrer) return false;
        const targetOrigin = returnOrigin || new URL(document.referrer).origin;
        window.opener.postMessage({ type: 'clay-select-return', requestId, value }, targetOrigin);
        return true;
    }
};
