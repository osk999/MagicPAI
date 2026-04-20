// MagicPAI.Studio/wwwroot/mpai.js
// JS interop helpers invoked from Blazor components (see temporal.md §S.1).
window.mpai = window.mpai || {};
window.mpai.scrollToBottom = function (el) {
    if (el) {
        el.scrollTop = el.scrollHeight;
    }
};
