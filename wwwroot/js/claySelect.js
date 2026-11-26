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
        const onKeyDown = event => {
            if ((event.key === 'ArrowDown' || event.key === 'ArrowUp') &&
                root.querySelector('.clay-select-popup')) {
                event.preventDefault();
            }
        };

        const reposition = () => this.positionPopup(inputId);
        document.addEventListener('mousedown', onMouseDown, true);
        document.addEventListener('scroll', reposition, true);
        window.addEventListener('resize', reposition);
        root.addEventListener('focusout', onFocusOut);
        input.addEventListener('keydown', onKeyDown);
        this.instances[inputId] = { root, input, dotnetRef, onMouseDown, onFocusOut, onKeyDown, close, reposition };
    },

    closeOthers: function (exceptInputId) {
        for (const [inputId, instance] of Object.entries(this.instances)) {
            if (inputId !== exceptInputId)
                instance.close();
        }
    },

    restoreDraftText: async function (inputId, text) {
        const instance = this.instances[inputId];
        if (!instance || !instance.input.isConnected || instance.input.disabled) return false;
        const previous = instance.input.value;
        const value = text || '';
        const accepted = await instance.dotnetRef.invokeMethodAsync('RestoreDraftTextAsync', value);
        if (accepted !== true || this.instances[inputId] !== instance || !instance.input.isConnected) return false;
        if (instance.input.value === previous) instance.input.value = value;
        return true;
    },

    containsFocus: function (inputId) {
        const instance = this.instances[inputId];
        return !!instance && instance.root.contains(document.activeElement);
    },

    ensureVisible: function (inputId, elementId) {
        const instance = this.instances[inputId];
        const element = document.getElementById(elementId);
        if (!instance || !element || !instance.root.contains(element)) return;
        element.scrollIntoView({ block: 'nearest' });
    },

    positionPopup: function (inputId) {
        const instance = this.instances[inputId];
        const field = instance?.root.querySelector('.clay-select-field');
        const popup = field?.querySelector('.clay-select-popup');
        if (!field || !popup) return;

        let container = field.parentElement;
        while (container && container !== document.body) {
            const overflowY = getComputedStyle(container).overflowY;
            if (overflowY === 'auto' || overflowY === 'scroll') break;
            container = container.parentElement;
        }
        if (container === document.body) container = null;

        const rect = field.getBoundingClientRect();
        const containerRect = container?.getBoundingClientRect();
        const visibleTop = Math.max(0, containerRect?.top ?? 0);
        const visibleBottom = Math.min(window.innerHeight, containerRect?.bottom ?? window.innerHeight);
        const gap = 4;
        const margin = 8;
        const maxHeight = 280;
        const below = Math.max(0, visibleBottom - rect.bottom - gap - margin);
        const above = Math.max(0, rect.top - visibleTop - gap - margin);
        const openUp = maxHeight > below && above > below;
        const availableHeight = Math.max(0, Math.min(maxHeight, openUp ? above : below));

        popup.style.top = openUp ? 'auto' : `${field.offsetHeight + gap}px`;
        popup.style.bottom = openUp ? `${field.offsetHeight + gap}px` : 'auto';
        popup.style.maxHeight = `${availableHeight}px`;
    },

    unregister: function (inputId) {
        const instance = this.instances[inputId];
        if (!instance) return;
        document.removeEventListener('mousedown', instance.onMouseDown, true);
        document.removeEventListener('scroll', instance.reposition, true);
        window.removeEventListener('resize', instance.reposition);
        instance.root.removeEventListener('focusout', instance.onFocusOut);
        instance.input.removeEventListener('keydown', instance.onKeyDown);
        delete this.instances[inputId];
    }
};
