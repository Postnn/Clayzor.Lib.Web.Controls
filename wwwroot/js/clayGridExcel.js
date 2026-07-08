// ── Clay Grid — Excel file download ────────────────────────────────────
//
// Downloads an Excel file (.xlsx) generated server‑side.
// The file content is passed as a base64‑encoded string and converted
// to a Blob, then downloaded via a temporary anchor element.
//
// Follows the same IIFE pattern as clayGridPrint.js and clayGridColumnDrag.js.

window.clayGridExcel = (function () {

    /**
     * Triggers a browser download for an Excel file.
     *
     * @param {string} fileName      — suggested file name (should end with .xlsx)
     * @param {string} base64Content — base64‑encoded .xlsx content
     */
    function downloadFile(fileName, base64Content) {
        // Decode base64 to binary
        var binaryStr = atob(base64Content);
        var bytes = new Uint8Array(binaryStr.length);
        for (var i = 0; i < binaryStr.length; i++) {
            bytes[i] = binaryStr.charCodeAt(i);
        }

        // Create a Blob with the proper MIME type
        var blob = new Blob([bytes], {
            type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'
        });

        // Create a temporary download link and click it
        var url = URL.createObjectURL(blob);
        var a = document.createElement('a');
        a.href = url;
        a.download = fileName;
        document.body.appendChild(a);
        a.click();

        // Clean up
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    }

    return {
        downloadFile: downloadFile
    };

})();
