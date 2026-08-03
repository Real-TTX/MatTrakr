// MatTrakr site scripts.

// Toolbar: auto-submit filter/sort selects on desktop only. On mobile the same
// selects live in the fullscreen dialog, where the user applies via the button.
document.addEventListener("change", function (e) {
    var sel = e.target.closest("select[data-toolbar-autosubmit]");
    if (!sel || !sel.form) return;
    if (window.matchMedia("(min-width: 768px)").matches) {
        sel.form.submit();
    }
});

// Season stepper: +/- buttons around a number input (item detail page).
document.addEventListener("click", function (e) {
    var btn = e.target.closest("[data-season-step]");
    if (!btn) return;
    var input = btn.parentElement.querySelector('input[type="number"]');
    if (!input) return;
    var step = parseInt(btn.getAttribute("data-season-step"), 10) || 0;
    var min = input.min !== "" ? parseInt(input.min, 10) : -Infinity;
    var max = input.max !== "" ? parseInt(input.max, 10) : Infinity;
    var val = (parseInt(input.value, 10) || 0) + step;
    input.value = Math.max(min, Math.min(max, val));
});
