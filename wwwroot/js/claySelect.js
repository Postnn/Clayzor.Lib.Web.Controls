window.claySelect = window.claySelect || {
    instances: {},

    register: function (inputId, dotnetRef) {
        const input = document.getElementById(inputId);
        const root = input?.closest('.clay-select');
        if (!root) return;

        this.unregister(inputId);
        const close = () => dotnetRef.invokeMethodAsync('ClosePopupFromOutsideAsync');
        const onMouseDown = event => {
            if (!root.contains(event.target)) close();
        };
        const onFocusOut = event => {
            if (event.relatedTarget && !root.contains(event.relatedTarget)) close();
        };

        const reposition = () => this.positionPopup(inputId);
        document.addEventListener('mousedown', onMouseDown, true);
        document.addEventListener('scroll', reposition, true);
        window.addEventListener('resize', reposition);
        root.addEventListener('focusout', onFocusOut);
        this.instances[inputId] = { root, onMouseDown, onFocusOut, close, reposition };
    },

    closeOthers: function (exceptInputId) {
        for (const [inputId, instance] of Object.entries(this.instances)) {
            if (inputId !== exceptInputId)
                instance.close();
        }
    },

    positionPopup: function (inputId) {
        const instance = this.instances[inputId];
        const field = instance?.root.querySelector('.clay-select-field');
        const popup = field?.querySelector('.clay-select-popup');
        if (!field || !popup) return;

        const rect = field.getBoundingClientRect();
        const gap = 4;
        const margin = 8;
        const maxHeight = 280;
        const viewportWidth = document.documentElement.clientWidth;
        const viewportHeight = window.innerHeight;
        const width = Math.min(rect.width, Math.max(0, viewportWidth - margin * 2));
        const left = Math.min(
            Math.max(margin, rect.left),
            Math.max(margin, viewportWidth - width - margin));
        const below = Math.max(0, viewportHeight - rect.bottom - gap - margin);
        const above = Math.max(0, rect.top - gap - margin);
        const openUp = maxHeight > below && above > below;
        const availableHeight = Math.max(0, Math.min(maxHeight, openUp ? above : below));

        popup.style.left = `${left}px`;
        popup.style.right = 'auto';
        popup.style.width = `${width}px`;
        popup.style.top = openUp ? 'auto' : `${rect.bottom + gap}px`;
        popup.style.bottom = openUp ? `${viewportHeight - rect.top + gap}px` : 'auto';
        popup.style.maxHeight = `${availableHeight}px`;
    },

    unregister: function (inputId) {
        const instance = this.instances[inputId];
        if (!instance) return;
        document.removeEventListener('mousedown', instance.onMouseDown, true);
        document.removeEventListener('scroll', instance.reposition, true);
        window.removeEventListener('resize', instance.reposition);
        instance.root.removeEventListener('focusout', instance.onFocusOut);
        delete this.instances[inputId];
    }
};
