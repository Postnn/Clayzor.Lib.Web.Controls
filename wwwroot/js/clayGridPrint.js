// ── Clay Grid — Print ───────────────────────────────────────────────────
//
// printHtml: renders a server-generated HTML string in a hidden iframe,
//            prints it, and cleans up after the dialog closes.
//
// Follows the same IIFE pattern as clayGridColumnDrag.js.

window.clayGridPrint = (function () {

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
        printHtml: printHtml
    };

})();
