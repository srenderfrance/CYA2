// Authentication redirect and diagnostics helper
window.authHelpers = {
    // Detailed diagnostics
    diagnostics: {
        // Save session information to understand authentication state
        captureAuthState: function() {
            try {
                const authState = {
                    timestamp: new Date().toISOString(),
                    url: window.location.href,
                    cookies: document.cookie,
                    localStorage: {},
                    referrer: document.referrer,
                    redirectHistory: JSON.parse(localStorage.getItem('redirectHistory') || '[]')
                };
                
                // Capture localStorage items
                for (let i = 0; i < localStorage.length; i++) {
                    const key = localStorage.key(i);
                    authState.localStorage[key] = localStorage.getItem(key);
                }
                
                // Add to history
                authState.redirectHistory.push({
                    timestamp: new Date().toISOString(),
                    url: window.location.href
                });
                
                // Store diagnostics
                localStorage.setItem('authDiagnostics', JSON.stringify(authState));
                localStorage.setItem('redirectHistory', JSON.stringify(authState.redirectHistory));
                
                console.log('Auth state captured:', authState);
                return authState;
            } catch (e) {
                console.error('Error capturing auth state:', e);
                return { error: e.message };
            }
        },
        
        // Get the current diagnostic information
        getAuthDiagnostics: function() {
            try {
                return JSON.parse(localStorage.getItem('authDiagnostics') || '{}');
            } catch (e) {
                console.error('Error getting auth diagnostics:', e);
                return { error: e.message };
            }
        },
        
        // Clear diagnostic data
        clearDiagnostics: function() {
            localStorage.removeItem('authDiagnostics');
            localStorage.removeItem('redirectHistory');
            console.log('Auth diagnostics cleared');
        },
        
        // Log event with timestamp
        logEvent: function(eventName, data) {
            const timestamp = new Date().toISOString();
            const event = { timestamp, eventName, data };
            
            let events = [];
            try {
                events = JSON.parse(localStorage.getItem('authEvents') || '[]');
            } catch (e) {
                console.error('Error parsing auth events:', e);
            }
            
            events.push(event);
            localStorage.setItem('authEvents', JSON.stringify(events));
            console.log(`Auth Event [${timestamp}]: ${eventName}`, data);
        }
    },
    
    // Redirect helpers
    redirectToHome: function() {
        this.diagnostics.logEvent('redirectToHome', { url: '/' });
        window.location.href = '/';
    },
    
    // OAuth state handling
    prepareForLogin: function() {
        this.diagnostics.logEvent('prepareForLogin', { timestamp: new Date().toISOString() });
        localStorage.setItem('redirectToHome', 'true');
        localStorage.setItem('loginPrepared', new Date().toISOString());
    },
    
    // Check if we need to redirect and do it if needed
    checkAndRedirect: function() {
        const shouldRedirect = localStorage.getItem('redirectToHome') === 'true';
        this.diagnostics.logEvent('checkAndRedirect', { 
            shouldRedirect, 
            loginPrepared: localStorage.getItem('loginPrepared')
        });
        
        if (shouldRedirect) {
            localStorage.removeItem('redirectToHome');
            localStorage.removeItem('loginPrepared');
            this.diagnostics.logEvent('executing-redirect', { destination: '/' });
            window.location.href = '/';
            return true;
        }
        return false;
    },
    
    // Show diagnostics UI
    showDiagnosticsUI: function() {
        const diagnostics = this.diagnostics.getAuthDiagnostics();
        const events = JSON.parse(localStorage.getItem('authEvents') || '[]');
        
        const containerStyle = 'position: fixed; top: 10px; right: 10px; background: #fff; ' + 
                             'border: 1px solid #ccc; padding: 10px; border-radius: 5px; ' +
                             'box-shadow: 0 0 10px rgba(0,0,0,0.1); z-index: 9999; max-width: 400px; ' + 
                             'max-height: 80vh; overflow-y: auto;';
                             
        const container = document.createElement('div');
        container.id = 'auth-diagnostics';
        container.style = containerStyle;
        
        let html = '<h3>Auth Diagnostics</h3>' +
                   '<button onclick="authHelpers.diagnostics.clearDiagnostics(); ' +
                   'document.getElementById(\'auth-diagnostics\').remove();">Clear & Close</button><hr>';
        
        // Add the diagnostics info
        html += '<h4>Auth State</h4>' +
                '<pre>' + JSON.stringify(diagnostics, null, 2) + '</pre>';
                
        // Add events log
        html += '<h4>Auth Events</h4>' +
                '<pre>' + JSON.stringify(events, null, 2) + '</pre>';
                
        container.innerHTML = html;
        document.body.appendChild(container);
    }
};

// Run on page load
(function() {
    console.log('Auth redirect helper loaded');
    window.authHelpers.diagnostics.captureAuthState();
    
    // Check for redirect need
    window.authHelpers.checkAndRedirect();
    
    // Set up keyboard shortcut for diagnostics UI (Alt+D)
    document.addEventListener('keydown', function(e) {
        if (e.altKey && e.key === 'd') {
            window.authHelpers.showDiagnosticsUI();
        }
    });
})();

// Simple auth redirect helper
(function() {
    console.log("Auth redirect script loaded - v2");
    
    // Check if this page is loaded after authentication
    if (document.referrer.includes('signin-google')) {
        console.log("Page loaded after Google authentication, redirecting to home...");
        window.location.href = '/';
    }
})();