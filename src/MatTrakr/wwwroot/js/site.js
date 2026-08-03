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

// Season picker: "Alle" / "Keine" quick-select all/none checkboxes (item detail).
document.addEventListener("click", function (e) {
    var all = e.target.closest("[data-season-all]");
    var none = e.target.closest("[data-season-none]");
    if (!all && !none) return;
    var picker = (all || none).closest("[data-season-picker]");
    if (!picker) return;
    picker.querySelectorAll('.season-checks input[type="checkbox"]').forEach(function (cb) {
        cb.checked = !!all;
    });
});
