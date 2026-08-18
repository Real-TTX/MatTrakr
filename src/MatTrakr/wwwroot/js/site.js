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
// quick buttons also stay highlighted to reflect the current selection. When the
// picker is linked to the status shortcuts (data-status-sync) the two stay in
// sync: seasons drive Ungesehen/Teilweise/Gesehen, and the status buttons drive
// the seasons back (Abgebrochen leaves the seasons untouched).
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

function mtSetStatusRadio(form, value) {
    var radio = form.querySelector('[data-status-toggle] input[type="radio"][value="' + value + '"]');
    if (radio) radio.checked = true; // programmatic — does not re-fire change
}

function mtSeasonsToStatus(picker) {
    if (!picker.hasAttribute("data-status-sync")) return;
    var form = picker.closest("form");
    if (!form) return;
    var boxes = mtSeasonBoxes(picker);
    var checked = boxes.filter(function (cb) { return cb.checked; }).length;
    var value = checked === 0 ? "Open" : checked === boxes.length ? "Done" : "Partial";
    mtSetStatusRadio(form, value);
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
    mtSeasonsToStatus(picker);
});

// Toggling an individual season re-evaluates the quick button and the status.
document.addEventListener("change", function (e) {
    var cb = e.target.closest('.season-checks input[type="checkbox"]');
    if (!cb) return;
    var picker = cb.closest("[data-season-picker]");
    if (!picker) return;
    mtSyncSeasonQuick(picker);
    mtSeasonsToStatus(picker);
});

// Choosing a status shortcut drives the linked season selection.
document.addEventListener("change", function (e) {
    var radio = e.target.closest('[data-status-toggle] input[type="radio"]');
    if (!radio || !radio.checked) return;
    var form = radio.closest("form");
    if (!form) return;
    var picker = form.querySelector("[data-season-picker][data-status-sync]");
    if (!picker) return;

    var boxes = mtSeasonBoxes(picker);
    var half = Math.ceil(boxes.length / 2);
    if (radio.value === "Done") boxes.forEach(function (cb) { cb.checked = true; });
    else if (radio.value === "Open") boxes.forEach(function (cb) { cb.checked = false; });
    else if (radio.value === "Partial") boxes.forEach(function (cb, i) { cb.checked = i < half; });
    // Abandoned: leave the seasons as they are
    mtSyncSeasonQuick(picker);
});

// Reflect the initial selection on load.
document.querySelectorAll("[data-season-picker]").forEach(mtSyncSeasonQuick);

// Star rating widget: click to set 1–5, hover to preview, "x" to clear.
function mtRenderStars(widget, value) {
    widget.querySelectorAll(".star-btn").forEach(function (btn) {
        var on = parseInt(btn.getAttribute("data-star"), 10) <= value;
        var i = btn.querySelector("i");
        if (i) i.className = "bi " + (on ? "bi-star-fill" : "bi-star");
        btn.classList.toggle("is-on", on);
    });
}

document.querySelectorAll("[data-star-rating]").forEach(function (widget) {
    var input = widget.querySelector('input[type="hidden"]');
    var current = function () { return parseInt(input.value, 10) || 0; };

    widget.querySelectorAll("[data-star]").forEach(function (btn) {
        var val = parseInt(btn.getAttribute("data-star"), 10) || 0;
        btn.addEventListener("click", function () {
            input.value = val;
            mtRenderStars(widget, val);
        });
        if (btn.classList.contains("star-btn")) {
            btn.addEventListener("mouseenter", function () { mtRenderStars(widget, val); });
        }
    });

    widget.addEventListener("mouseleave", function () { mtRenderStars(widget, current()); });
});
