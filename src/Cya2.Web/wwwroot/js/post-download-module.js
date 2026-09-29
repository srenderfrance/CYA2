export async function postJsonAndDownload(url, jsonPayload) {
    try {
        const tokenResponse = await fetch('/api/antiforgery-token', {
            method: 'GET',
            credentials: 'same-origin'
        });

        if (!tokenResponse.ok) {
            throw new Error('Could not get antiforgery token.');
        }

        const tokenPayload = await tokenResponse.json();
        const requestToken = tokenPayload?.requestToken;
        if (!requestToken) {
            throw new Error('Antiforgery token missing in response.');
        }

        const postResponse = await fetch(url, {
            method: 'POST',
            credentials: 'same-origin',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': requestToken
            },
            body: jsonPayload
        });

        if (!postResponse.ok) {
            throw new Error(`Download request failed with status ${postResponse.status}.`);
        }

        const contentDisposition = postResponse.headers.get('content-disposition') || '';
        const fileNameMatch = contentDisposition.match(/filename\*=UTF-8''([^;]+)|filename="?([^";]+)"?/i);
        const encodedFileName = fileNameMatch?.[1];
        const plainFileName = fileNameMatch?.[2];
        const fileName = encodedFileName
            ? decodeURIComponent(encodedFileName)
            : (plainFileName || 'donors_export.xlsx');

        const blob = await postResponse.blob();
        const downloadUrl = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = downloadUrl;
        anchor.download = fileName;
        document.body.appendChild(anchor);
        anchor.click();
        anchor.remove();
        URL.revokeObjectURL(downloadUrl);
    } catch (err) {
        console.error('postJsonAndDownload error', err);
        throw err;
    }
}
