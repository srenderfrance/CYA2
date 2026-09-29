// Radzen Tooltip Positioning Fix - Scroll handling only
window.radzenTooltipFix = {
    activeTooltips: new Map(),
    
    init: function() {
        // Only track tooltips for scroll repositioning, let Radzen handle initial positioning
        this.observeTooltips();
        
        // Handle scroll events to reposition existing tooltips
        window.addEventListener('scroll', () => {
            this.repositionOnScroll();
        }, { passive: true });
    },
    
    observeTooltips: function() {
        const observer = new MutationObserver((mutations) => {
            mutations.forEach((mutation) => {
                mutation.addedNodes.forEach((node) => {
                    if (node.nodeType === 1 && this.isTooltipElement(node)) {
                        this.trackTooltipForScroll(node);
                    }
                });
            });
        });
        
        observer.observe(document.body, {
            childList: true,
            subtree: true
        });
    },
    
    isTooltipElement: function(element) {
        return element.classList?.contains('rz-tooltip');
    },
    
    trackTooltipForScroll: function(tooltip) {
        const tooltipId = tooltip.id;
        
        // Wait a moment for Radzen to position it, then track its position
        setTimeout(() => {
            const tooltipRect = tooltip.getBoundingClientRect();
            if (tooltipRect.top > 0 && tooltipRect.left > 0) {
                // Find any icon that might be the trigger
                const icon = document.querySelector('.bi-exclamation-circle:hover');
                
                this.activeTooltips.set(tooltipId, {
                    tooltip: tooltip,
                    trigger: icon,
                    originalTop: tooltipRect.top,
                    originalLeft: tooltipRect.left,
                    scrollY: window.scrollY,
                    scrollX: window.scrollX
                });
            }
        }, 100); // Give Radzen time to position it
    },
    
    repositionOnScroll: function() {
        if (this.activeTooltips.size === 0) return;
        
        const currentScrollY = window.scrollY;
        const currentScrollX = window.scrollX;
        
        this.activeTooltips.forEach((info, tooltipId) => {
            const { tooltip, originalTop, originalLeft, scrollY, scrollX } = info;
            
            // Check if tooltip still exists and is visible
            if (!document.body.contains(tooltip) || 
                getComputedStyle(tooltip).display === 'none') {
                this.activeTooltips.delete(tooltipId);
                return;
            }
            
            // Calculate new position based on scroll offset
            const deltaY = currentScrollY - scrollY;
            const deltaX = currentScrollX - scrollX;
            
            const newTop = originalTop - deltaY;
            const newLeft = originalLeft - deltaX;
            
            // Apply the new position
            tooltip.style.position = 'fixed';
            tooltip.style.top = `${newTop}px`;
            tooltip.style.left = `${newLeft}px`;
        });
    }
};

// Initialize
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => window.radzenTooltipFix.init());
} else {
    window.radzenTooltipFix.init();
}