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

        document.addEventListener('mousedown', onMouseDown, true);
        root.addEventListener('focusout', onFocusOut);
        this.instances[inputId] = { root, onMouseDown, onFocusOut, close };
    },

    closeOthers: function (exceptInputId) {
        for (const [inputId, instance] of Object.entries(this.instances)) {
            if (inputId !== exceptInputId)
                instance.close();
        }
    },

    unregister: function (inputId) {
        const instance = this.instances[inputId];
        if (!instance) return;
        document.removeEventListener('mousedown', instance.onMouseDown, true);
        instance.root.removeEventListener('focusout', instance.onFocusOut);
        delete this.instances[inputId];
    }
};
