window.clayFormWindow = {
    storagePrefix: 'clayFormWindow:',
    refreshId: null,
    previousRefresh: null,

    registerRefresh(registrationId, dotnetRef) {
        if (!this.refreshId) this.previousRefresh = window.refresh;
        this.refreshId = registrationId;
        window.refresh = () => dotnetRef.invokeMethodAsync('RefreshFromChildAsync');
    },

    unregisterRefresh(registrationId) {
        if (this.refreshId !== registrationId) return;
        if (this.previousRefresh) window.refresh = this.previousRefresh;
        else delete window.refresh;
        this.refreshId = null;
        this.previousRefresh = null;
    },

    readSaved(formKey) {
        try {
            const value = localStorage.getItem(this.storagePrefix + formKey);
            return value ? JSON.parse(value) : null;
        } catch (_) {
            return null;
        }
    },

    open(url, formKey, defaults) {
        return this.openHandle(url, formKey, defaults) !== null;
    },

    openHandle(url, formKey, defaults, windowName = formKey) {
        const saved = this.readSaved(formKey);
        const options = { ...defaults, ...(saved || {}) };
        const features = [
            `width=${options.width}`,
            `height=${options.height}`,
            `left=${options.left}`,
            `top=${options.top}`,
            `resizable=${options.resizable ? 'yes' : 'no'}`,
            'scrollbars=yes'
        ].join(',');
        const child = window.open(url, windowName, features);
        if (!child) return null;

        const save = () => {
            try {
                if (child.closed || child.location.origin !== window.location.origin) return;
                const current = {
                    width: child.outerWidth,
                    height: child.outerHeight,
                    left: child.screenX,
                    top: child.screenY,
                    resizable: options.resizable
                };
                localStorage.setItem(this.storagePrefix + formKey, JSON.stringify(current));
            } catch (_) {
                // Внешняя форма другого origin может запретить доступ к свойствам окна.
            }
        };
        const timer = window.setInterval(() => {
            if (child.closed) {
                window.clearInterval(timer);
                return;
            }
            save();
        }, 500);
        try {
            child.addEventListener('beforeunload', save);
        } catch (_) {
            // Browser isolation can prevent access to an external window.
        }
        return child;
    }
};

window.clayOpen = function (url, target, defaults) {
    const targetUrl = new URL(url, window.location.href);
    const formKey = target && !target.startsWith('_')
        ? target
        : 'entity:' + targetUrl.pathname.replace(/[^a-zA-Z0-9]+/g, '-').replace(/^-|-$/g, '').toLowerCase();
    const options = Object.assign({ width: 1100, height: 700, left: 50, top: 50, resizable: true }, defaults || {});
    return window.clayFormWindow.open(targetUrl.toString(), formKey, options);
};
