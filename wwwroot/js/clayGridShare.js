// ClayGrid Share — clipboard and URL helpers (SH5, SH6)
window.clayGridShare = {
    /**
     * Copies text to clipboard. Uses navigator.clipboard when available
     * (secure context: https or localhost), falls back to textarea method.
     * @param {string} text - text to copy
     * @returns {Promise<boolean>} true if copy succeeded
     */
    copyToClipboard: async function (text) {
        try {
            if (navigator.clipboard && navigator.clipboard.writeText) {
                await navigator.clipboard.writeText(text);
                return true;
            }
        } catch {
            // Fallback for non-secure contexts (e.g. intranet http)
        }

        // Fallback: textarea + execCommand
        const ta = document.createElement('textarea');
        ta.value = text;
        ta.style.position = 'fixed';
        ta.style.left = '-9999px';
        ta.style.top = '-9999px';
        document.body.appendChild(ta);
        ta.focus();
        ta.select();
        try {
            document.execCommand('copy');
            return true;
        } catch {
            return false;
        } finally {
            document.body.removeChild(ta);
        }
    }
};
