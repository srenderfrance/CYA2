// Simple auth redirect helper
window.authHelpers = {
    // Essential redirect helper for login preparation
    prepareForLogin: function() {
        console.log('prepareForLogin called');
        // Clear any existing redirect flags and auth state
        sessionStorage.clear();
        
        // Clear specific cookies
        document.cookie = '.AspNetCore.Cookies=; expires=Thu, 01 Jan 1970 00:00:00 UTC; path=/;';
    },
    
    // Clear auth in progress state
    clearAuthInProgress: function() {
        sessionStorage.removeItem('authInProgress');
    }
};

// Run on page load
(function() {
    console.log('Auth redirect helper loaded');
    window.authHelpers.clearAuthInProgress();
})();