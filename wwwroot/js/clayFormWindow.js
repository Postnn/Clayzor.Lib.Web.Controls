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
        const child = window.open(url, formKey, features);
        if (!child) return false;
        child.opener = window;

        const save = () => {
            try {
                if (child.closed) return;
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
                save();
                return;
            }
            save();
        }, 500);
        child.addEventListener('beforeunload', save);
        return true;
    }
};
