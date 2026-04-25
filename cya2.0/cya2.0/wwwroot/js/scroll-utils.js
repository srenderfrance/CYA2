// Scrolls to an element (by id) if any part of it is outside the visible viewport.
window.scrollToIfHidden = function (elementId) {
    const el = document.getElementById(elementId);
    if (!el) return;
    const rect = el.getBoundingClientRect();
    const inView = rect.top >= 0 && rect.bottom <= window.innerHeight;
    if (!inView) {
        el.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
};
