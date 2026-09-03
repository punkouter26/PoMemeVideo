// Typed replacements for the `eval`-built script strings the download/share buttons used to
// send through IJSRuntime. Interpolating C# into JavaScript source made every URL and file
// name unverifiable at compile time — a mistyped route stayed invisible until a user clicked —
// and it is the one thing that would block adding a Content-Security-Policy.
//
// Loaded as a classic script from index.html so pages can call these without module plumbing.
window.poBrowser = (function () {
    function clickAnchor(href, fileName) {
        const a = document.createElement('a');
        a.href = href;
        a.download = fileName;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
    }

    return {
        // Triggers a browser download of a same-origin URL.
        downloadUrl: function (url, fileName) {
            clickAnchor(url, fileName);
        },

        // Triggers a browser download of in-memory text (JSON script exports).
        downloadText: function (text, fileName, mimeType) {
            const blob = new Blob([text], { type: mimeType || 'application/octet-stream' });
            const objectUrl = URL.createObjectURL(blob);
            try {
                clickAnchor(objectUrl, fileName);
            } finally {
                // Revoke after the click has been dispatched, not before — Safari reads the
                // href lazily.
                setTimeout(() => URL.revokeObjectURL(objectUrl), 10000);
            }
        },

        // Reads document.cookie. Named rather than eval'd so the call site is greppable and
        // survives a Content-Security-Policy that forbids unsafe-eval.
        readCookies: function () {
            return document.cookie;
        },

        // Web Share where available, clipboard otherwise. Returns which one ran so the caller
        // can report accurately instead of always claiming "copied to clipboard".
        shareOrCopy: async function (title, text, url) {
            if (navigator.share) {
                try {
                    await navigator.share({ title: title, text: text, url: url });
                    return 'shared';
                } catch (e) {
                    // User dismissed the share sheet, or the browser rejected it — fall through
                    // to the clipboard rather than reporting a failure.
                }
            }
            if (navigator.clipboard) {
                await navigator.clipboard.writeText(url);
                return 'copied';
            }
            return 'unavailable';
        }
    };
})();
