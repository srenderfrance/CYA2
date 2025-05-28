// Simple auth redirect helper
window.authHelpers = {
    // Essential redirect helper for login preparation
    prepareForLogin: function() {
        // Use sessionStorage which is cleared when browser is closed
        sessionStorage.setItem('redirectToHome', 'true');
    },
    
    // Check if redirect is needed and redirect if necessary
    checkAndRedirect: function() {
        if (sessionStorage.getItem('redirectToHome') === 'true') {
            sessionStorage.removeItem('redirectToHome');
            window.location.href = '/';
            return true;
        }
        return false;
    }
};

// Run on page load - check for Google auth callback
(function() {
    console.log('Auth redirect helper loaded');
    
    // Check for Google sign-in callback in referrer
    if (document.referrer.includes('signin-google')) {
        console.log("Detected return from Google authentication, redirecting to home");
        window.location.href = '/';
        return;
    }
    
    // Check for stored redirect flag
    window.authHelpers.checkAndRedirect();
})();