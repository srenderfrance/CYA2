// Radzen Dropdown Positioning Fix
window.radzenDropdownFix = {
    lastDropdownOpenTime: 0,
    openDropdowns: new Set(),
    
    init: function() {
        // Track when dropdowns are opened and their trigger elements
        this.trackDropdownOpening();
        
        // Listen for scroll events to reposition dropdowns
        let scrollTimeout;
        window.addEventListener('scroll', (e) => {
            clearTimeout(scrollTimeout);
            scrollTimeout = setTimeout(() => {
                this.repositionOpenDropdowns();
            }, 16); // Use requestAnimationFrame timing
        }, { passive: true });
        
        // Listen for resize events to refresh positioning
        window.addEventListener('resize', () => {
            setTimeout(() => {
                this.repositionOpenDropdowns();
            }, 50);
        });
    },
    
    trackDropdownOpening: function() {
        // Use MutationObserver to detect when dropdown panels appear
        const observer = new MutationObserver((mutations) => {
            mutations.forEach((mutation) => {
                // Check for newly added dropdown panels
                mutation.addedNodes.forEach((node) => {
                    if (node.nodeType === 1 && node.classList?.contains('rz-dropdown-panel')) {
                        this.registerDropdown(node);
                    }
                });
                // Check for style changes that show/hide dropdowns
                if (mutation.type === 'attributes' && 
                    mutation.attributeName === 'style' && 
                    mutation.target.classList?.contains('rz-dropdown-panel')) {
                    
                    const panel = mutation.target;
                    const isVisible = panel.style.display !== 'none' && 
                                     getComputedStyle(panel).display !== 'none';
                    
                    if (isVisible) {
                        this.registerDropdown(panel);
                    } else {
                        this.unregisterDropdown(panel);
                    }
                }
            });
        });
        
        observer.observe(document.body, {
            childList: true,
            subtree: true,
            attributes: true,
            attributeFilter: ['style', 'class']
        });
    },
    
    registerDropdown: function(panel) {
        // Find the dropdown trigger element
        const trigger = this.findDropdownTrigger(panel);
        if (trigger) {
            const dropdownInfo = {
                panel: panel,
                trigger: trigger,
                originalPosition: this.getElementPosition(trigger)
            };
            
            this.openDropdowns.add(dropdownInfo);
            this.lastDropdownOpenTime = Date.now();
            
            // Set initial position as fixed
            this.positionDropdownPanel(dropdownInfo);
        }
    },
    
    unregisterDropdown: function(panel) {
        this.openDropdowns.forEach(info => {
            if (info.panel === panel) {
                this.openDropdowns.delete(info);
            }
        });
    },
    
    findDropdownTrigger: function(panel) {
        // Try to find the dropdown trigger by looking for Radzen dropdown structures
        const dropdowns = document.querySelectorAll('.rz-dropdown');
        for (let dropdown of dropdowns) {
            const dropdownPanel = dropdown.querySelector('.rz-dropdown-panel');
            if (dropdownPanel === panel || dropdown.contains(panel)) {
                return dropdown.querySelector('.rz-dropdown-label, .rz-dropdown-trigger, input[readonly]');
            }
        }
        return null;
    },
    
    getElementPosition: function(element) {
        const rect = element.getBoundingClientRect();
        return {
            top: rect.top + window.scrollY,
            left: rect.left + window.scrollX,
            bottom: rect.bottom + window.scrollY,
            right: rect.right + window.scrollX,
            width: rect.width,
            height: rect.height,
            viewportTop: rect.top,
            viewportLeft: rect.left
        };
    },
    
    positionDropdownPanel: function(dropdownInfo) {
        const { panel, trigger } = dropdownInfo;
        const triggerPos = this.getElementPosition(trigger);
        
        // Position the panel just below the trigger
        const panelTop = triggerPos.viewportTop + triggerPos.height;
        const panelLeft = triggerPos.viewportLeft;
        
        // Ensure the panel stays within viewport bounds
        const viewportWidth = window.innerWidth;
        const viewportHeight = window.innerHeight;
        const panelRect = panel.getBoundingClientRect();
        
        let finalLeft = panelLeft;
        let finalTop = panelTop;
        
        // Adjust horizontal position if it would go off-screen
        if (panelLeft + panelRect.width > viewportWidth - 20) {
            finalLeft = viewportWidth - panelRect.width - 20;
        }
        if (finalLeft < 20) {
            finalLeft = 20;
        }
        
        // Adjust vertical position if it would go off-screen
        if (panelTop + panelRect.height > viewportHeight - 20) {
            finalTop = triggerPos.viewportTop - panelRect.height;
            if (finalTop < 20) {
                finalTop = 20;
            }
        }
        
        // Apply the positioning
        panel.style.position = 'fixed';
        panel.style.top = `${finalTop}px`;
        panel.style.left = `${finalLeft}px`;
        panel.style.zIndex = '9999';
    },
    
    repositionOpenDropdowns: function() {
        if (this.openDropdowns.size === 0) return;
        
        this.openDropdowns.forEach(dropdownInfo => {
            // Check if the panel is still visible
            const isVisible = dropdownInfo.panel.style.display !== 'none' && 
                             getComputedStyle(dropdownInfo.panel).display !== 'none';
            
            if (isVisible) {
                this.positionDropdownPanel(dropdownInfo);
            } else {
                // Panel is no longer visible, remove from tracking
                this.openDropdowns.delete(dropdownInfo);
            }
        });
    },
    
    refreshPositions: function() {
        this.repositionOpenDropdowns();
    }
};

// Auto-initialize when DOM is ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => window.radzenDropdownFix.init());
} else {
    window.radzenDropdownFix.init();
}