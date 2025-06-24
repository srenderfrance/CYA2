// Simple auth redirect helper
window.authHelpers = {
    // Essential redirect helper for login preparation
    prepareForLogin: function() {
        // Clear any existing redirect flags
        sessionStorage.removeItem('authRedirectPath');
    },
    
    // Check if redirect is needed and redirect if necessary
    checkAndRedirect: function() {
        const redirectPath = sessionStorage.getItem('authRedirectPath');
        if (redirectPath) {
            sessionStorage.removeItem('authRedirectPath');
            window.location.href = redirectPath;
            return true;
        }
        return false;
    }
};

// Run on page load
(function() {
    console.log('Auth redirect helper loaded');
    
    // Check for Google sign-in callback in referrer
    if (document.referrer.includes('signin-google')) {
        console.log("Detected return from Google authentication");
        window.authHelpers.checkAndRedirect();
    }
})();