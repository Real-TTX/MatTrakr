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

// Season picker: quick-set the season toggle buttons — Alle / Teilweise / Keine.
// "Teilweise" checks the first half (a quick "partway through" preset). The
// quick buttons also stay highlighted to reflect the current selection.
function mtSeasonBoxes(picker) {
    return Array.prototype.slice.call(
        picker.querySelectorAll('.season-checks input[type="checkbox"]'));
}

function mtSyncSeasonQuick(picker) {
    var boxes = mtSeasonBoxes(picker);
    var total = boxes.length;
    var checked = boxes.filter(function (cb) { return cb.checked; }).length;
    var mode = checked === 0 ? "none" : checked === total ? "all" : "half";
    picker.querySelectorAll("[data-season-set]").forEach(function (btn) {
        btn.classList.toggle("active", btn.getAttribute("data-season-set") === mode);
    });
}

document.addEventListener("click", function (e) {
    var trigger = e.target.closest("[data-season-set]");
    if (!trigger) return;
    var picker = trigger.closest("[data-season-picker]");
    if (!picker) return;
    var mode = trigger.getAttribute("data-season-set");
    var boxes = mtSeasonBoxes(picker);
    var half = Math.ceil(boxes.length / 2);
    boxes.forEach(function (cb, i) {
        cb.checked = mode === "all" ? true : mode === "none" ? false : i < half;
    });
    mtSyncSeasonQuick(picker);
});

// Toggling an individual season re-evaluates which quick button is current.
document.addEventListener("change", function (e) {
    var cb = e.target.closest('.season-checks input[type="checkbox"]');
    if (!cb) return;
    var picker = cb.closest("[data-season-picker]");
    if (picker) mtSyncSeasonQuick(picker);
});

// Reflect the initial selection on load.
document.querySelectorAll("[data-season-picker]").forEach(mtSyncSeasonQuick);
