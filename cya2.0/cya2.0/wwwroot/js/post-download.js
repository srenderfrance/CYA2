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
