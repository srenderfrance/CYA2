window.customDropdown = (function(){
    const registry = new Map();

    function onDocumentClick(e) {
        for (const [id, info] of registry.entries()) {
            const root = document.getElementById(id);
            if (!root) continue;
            if (!root.contains(e.target)) {
                // tell dotnet to close
                if (info.dotNetRef) {
                    try { info.dotNetRef.invokeMethodAsync('CloseDropdown'); } catch (err) { }
                }
            }
        }
    }

    function register(dotNetRef, id) {
        registry.set(id, { dotNetRef });
        if (!window._customDropdownDocListener) {
            window.addEventListener('click', onDocumentClick, true);
            window._customDropdownDocListener = true;
        }
    }

    function unregister(id) {
        const info = registry.get(id);
        if (info && info.dotNetRef) {
            try { /* no-op */ } catch (e) {}
        }
        registry.delete(id);
        if (registry.size === 0 && window._customDropdownDocListener) {
            window.removeEventListener('click', onDocumentClick, true);
            window._customDropdownDocListener = false;
        }
    }

    return { register, unregister };
})();
