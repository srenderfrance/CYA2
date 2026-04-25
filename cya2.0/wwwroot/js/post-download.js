window.postJsonAndDownload = function (url, jsonPayload) {
    try {
        const data = JSON.parse(jsonPayload);
        const form = document.createElement('form');
        form.method = 'POST';
        form.action = url;
        form.style.display = 'none';

        const input = document.createElement('input');
        input.type = 'hidden';
        input.name = 'request';
        input.value = JSON.stringify(data);
        form.appendChild(input);

        document.body.appendChild(form);
        form.submit();
        form.remove();
    } catch (err) {
        console.error('postJsonAndDownload error', err);
    }
};

// Scrolls to the top of the page if the element (by id) is not fully visible in the viewport.
window.scrollToIfHidden = function (elementId) {
    const el = document.getElementById(elementId);
    if (!el) return;
    const rect = el.getBoundingClientRect();
    const inView = rect.top >= 0 && rect.bottom <= window.innerHeight;
    if (!inView) {
        window.scrollTo({ top: 0, behavior: 'smooth' });
    }
};
