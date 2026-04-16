// Radzen switch runtime fix was applying theme variables globally via inline styles and a MutationObserver.
// It caused unintended global theming side effects. Disable by making this file a no-op.