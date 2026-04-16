function enableAutocompleteTabBehavior(containerId) {
    try {
        const container = document.getElementById(containerId);
        if (!container) return;

        function handleKey(e) {
            try {
                const active = document.activeElement;
                if (!active) return;
                // Only act when focus is inside the container
                if (!container.contains(active)) return;

                if (e.key === 'Tab') {
                    // Prevent default tab navigation
                    e.preventDefault();
                    e.stopPropagation();

                    // If shift held, move up; otherwise move down
                    const arrowKey = e.shiftKey ? 'ArrowUp' : 'ArrowDown';

                    // Dispatch Arrow key events on the active element
                    const arrowEvent = new KeyboardEvent('keydown', {
                        key: arrowKey,
                        code: arrowKey,
                        bubbles: true,
                        cancelable: true
                    });
                    active.dispatchEvent(arrowEvent);

                    // Also dispatch keyup to mimic normal user keypress
                    const arrowUp = new KeyboardEvent('keyup', { key: arrowKey, code: arrowKey, bubbles: true, cancelable: true });
                    active.dispatchEvent(arrowUp);
                }
            }
            catch (err) {
                console.error('autocomplete document handler error', err);
            }
        }

        // Attach once (avoid duplicate attachments)
        if (!container.__autocomplete_tab_attached) {
            document.addEventListener('keydown', handleKey, true);
            container.__autocomplete_tab_attached = true;
        }
    }
    catch (err) {
        console.error('enableAutocompleteTabBehavior error', err);
    }
}

// Expose globally for simple invocation from Blazor
window.enableAutocompleteTabBehavior = enableAutocompleteTabBehavior;
export { enableAutocompleteTabBehavior };
