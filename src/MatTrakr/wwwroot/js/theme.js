// Theme switcher — light / dark / system.
// The head script (_ThemeHead.cshtml) already applied the resolved theme before
// paint; this wires the toggle buttons, persists the choice, and keeps "system"
// live when the OS preference changes. Storage key/logic must match _ThemeHead.
(function () {
    "use strict";

    var KEY = "mt-theme";
    var root = document.documentElement;
    var media = window.matchMedia("(prefers-color-scheme: dark)");

    function storedMode() {
        try { return localStorage.getItem(KEY) || "system"; } catch (e) { return "system"; }
    }

    function resolve(mode) {
        return (mode === "dark" || (mode === "system" && media.matches)) ? "dark" : "light";
    }

    function apply(mode) {
        var theme = resolve(mode);
        root.setAttribute("data-theme", theme);
        root.setAttribute("data-bs-theme", theme);

        // Reflect the chosen mode (not the resolved theme) on every switch on the page.
        document.querySelectorAll("[data-theme-switch]").forEach(function (group) {
            group.querySelectorAll("[data-theme-option]").forEach(function (btn) {
                var on = btn.getAttribute("data-theme-option") === mode;
                btn.classList.toggle("active", on);
                btn.setAttribute("aria-pressed", on ? "true" : "false");
            });
        });
    }

    function setMode(mode) {
        try { localStorage.setItem(KEY, mode); } catch (e) { /* ignore */ }
        apply(mode);
    }

    document.addEventListener("click", function (e) {
        var btn = e.target.closest("[data-theme-option]");
        if (!btn) return;
        e.preventDefault();
        setMode(btn.getAttribute("data-theme-option"));
    });

    // Follow the OS preference live while in "system" mode.
    media.addEventListener("change", function () {
        if (storedMode() === "system") apply("system");
    });

    // Sync button states on load (theme itself was already set by the head script).
    apply(storedMode());
})();
