// ── Clay Grid — Print ───────────────────────────────────────────────────
//
// printHtml:         renders a server-generated HTML string in a hidden iframe,
//                    prints it, and cleans up after the dialog closes.
// showSpinner / hideSpinner:  toggle a CSS spinner next to the grid title
//                             without triggering Blazor re-renders.
//
// Follows the same IIFE pattern as clayGridColumnDrag.js.

window.clayGridPrint = (function () {

    /**
     * Shows a CSS spinner by its DOM id (display:inline-block).
     * @param {string} spinnerId — id of the spinner <span> element
     */
    function showSpinner(spinnerId) {
        var el = document.getElementById(spinnerId);
        if (el) el.style.display = 'inline-block';
    }

    /**
     * Hides a CSS spinner by its DOM id (display:none).
     * @param {string} spinnerId — id of the spinner <span> element
     */
    function hideSpinner(spinnerId) {
        var el = document.getElementById(spinnerId);
        if (el) el.style.display = 'none';
    }

    /**
     * Prints an HTML string in a hidden iframe. The iframe is created,
     * populated with the HTML, printed, and removed after the print dialog
     * closes. Returns a Promise so Blazor can await completion.
     *
     * @param {string} html — complete HTML document string
     * @returns {Promise<void>}
     */
    function printHtml(html) {
        return new Promise(function (resolve) {
            var iframe = document.createElement('iframe');
            // Off‑screen — never covers the visible grid, but still renders for print
            iframe.style.cssText =
                'position:absolute;left:-9999px;top:0;width:800px;height:600px;';

            document.body.appendChild(iframe);

            var doc = iframe.contentWindow.document;
            doc.open();
            doc.write(html);
            doc.close();

            iframe.contentWindow.addEventListener('afterprint', function () {
                document.body.removeChild(iframe);
                resolve();
            });

            iframe.contentWindow.print();
        });
    }

    return {
        showSpinner: showSpinner,
        hideSpinner: hideSpinner,
        printHtml: printHtml
    };

})();
