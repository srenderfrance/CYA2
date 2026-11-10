// Browser-session authentication resume state. Do not store donor data,
// uploaded files, tokens, or import identifiers here.
window.authHelpers = {
    saveResumeState: function (state) {
        if (!state || typeof state.route !== 'string' || !state.route.startsWith('/') || state.route.startsWith('//')) {
            return;
        }

        sessionStorage.setItem('cya2.auth.resume', JSON.stringify({
            userId: typeof state.userId === 'string' ? state.userId : '',
            role: typeof state.role === 'string' ? state.role : '',
            route: state.route,
            account: typeof state.account === 'string' ? state.account : '',
            startDate: typeof state.startDate === 'string' ? state.startDate : null,
            endDate: typeof state.endDate === 'string' ? state.endDate : null,
            preset: typeof state.preset === 'string' ? state.preset : ''
        }));
    },

    consumeResumeState: function () {
        if (sessionStorage.getItem('cya2.auth.resume.pending') !== '1') {
            return null;
        }
        sessionStorage.removeItem('cya2.auth.resume.pending');
        const value = sessionStorage.getItem('cya2.auth.resume');
        sessionStorage.removeItem('cya2.auth.resume');
        if (!value) {
            return null;
        }

        try {
            const state = JSON.parse(value);
            return state && typeof state.route === 'string' && state.route.startsWith('/') && !state.route.startsWith('//') ? state : null;
        } catch {
            return null;
        }
    },

    markResumeState: function () {
        sessionStorage.setItem('cya2.auth.resume.pending', '1');
    },

    clearResumeState: function () {
        sessionStorage.removeItem('cya2.auth.resume');
        sessionStorage.removeItem('cya2.auth.resume.pending');
    }
};
