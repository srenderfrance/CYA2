using cya2.Services;
using Microsoft.Extensions.Localization;

namespace UtilityClasses
{
    /// <summary>
    /// Centralized management of date ranges across pages with AppState persistence
    /// </summary>
    public static class DateRangeManager
    {
        public enum DateRangePreset 
        { 
            AllDates,
            ThisYear, 
            Previous12Months, 
            LastYear, 
            ThisMonth,      // New preset
            LastMonth,      // New preset
            Custom  // New preset for manually selected dates
        }

        public class DateRangeState
        {
            public DateTime StartDate { get; set; }
            public DateTime EndDate { get; set; }
            public DateTime PendingStartDate { get; set; }
            public DateTime PendingEndDate { get; set; }
            public DateRangePreset SelectedPreset { get; set; }
            public bool IsCustomRange { get; set; }
        }

        /// <summary>
        /// Save date range state to AppState
        /// </summary>
        public static void SaveDateRangeToAppState(AppState appState, DateRangeState state)
        {
            appState.SelectedStartDate = state.StartDate;
            appState.SelectedEndDate = state.EndDate;
            appState.SelectedDatePreset = state.SelectedPreset.ToString();
        }

        /// <summary>
        /// Initialize date range state from AppState or defaults
        /// </summary>
        public static DateRangeState InitializeDateRange(AppState appState)
        {
            var state = new DateRangeState();

            // Load from AppState if available
            if (appState.SelectedStartDate.HasValue && appState.SelectedEndDate.HasValue)
            {
                state.StartDate = appState.SelectedStartDate.Value;
                state.EndDate = appState.SelectedEndDate.Value;
                state.PendingStartDate = state.StartDate;
                state.PendingEndDate = state.EndDate;
                
                // ENHANCED: Use explicitly stored preset from AppState if available
                if (!string.IsNullOrEmpty(appState.SelectedDatePreset) && 
                    Enum.TryParse<DateRangePreset>(appState.SelectedDatePreset, out var storedPreset))
                {
                    // Use the explicitly stored preset (preserves user intent)
                    state.SelectedPreset = storedPreset;
                    state.IsCustomRange = storedPreset == DateRangePreset.Custom;
                }
                else
                {
                    // Fallback to detection if no explicit preset stored
                    state.SelectedPreset = DeterminePresetFromDates(state.StartDate, state.EndDate);
                    state.IsCustomRange = state.SelectedPreset == DateRangePreset.Custom;
                }
            }
            else
            {
                // Apply default preset
                state.SelectedPreset = DateRangePreset.ThisYear;
                ApplyPreset(state, state.SelectedPreset);
                state.StartDate = state.PendingStartDate;
                state.EndDate = state.PendingEndDate;
                state.IsCustomRange = false;
            }

            return state;
        }

        /// <summary>
        /// Apply a preset date range
        /// </summary>
        public static void ApplyPreset(DateRangeState state, DateRangePreset preset)
        {
            var now = DateTime.Now;
            
            switch (preset)
            {
                case DateRangePreset.AllDates:
                    // Represent "All Dates" by using a very early start date and current date as end
                    state.PendingStartDate = new DateTime(1900, 1, 1);
                    state.PendingEndDate = now;
                    break;
                case DateRangePreset.ThisYear:
                    state.PendingStartDate = new DateTime(now.Year, 1, 1);
                    state.PendingEndDate = now;
                    break;
                    
                case DateRangePreset.Previous12Months:
                    // Get the last complete month (previous month)
                    var lastCompleteMonth = new DateTime(now.Year, now.Month, 1).AddMonths(-1);
                    // End date: Last day of previous month  
                    state.PendingEndDate = new DateTime(lastCompleteMonth.Year, lastCompleteMonth.Month, DateTime.DaysInMonth(lastCompleteMonth.Year, lastCompleteMonth.Month));
                    // Start date: First day of 12 months before the end date
                    state.PendingStartDate = state.PendingEndDate.AddMonths(-11);
                    state.PendingStartDate = new DateTime(state.PendingStartDate.Year, state.PendingStartDate.Month, 1);
                    break;
                    
                case DateRangePreset.LastYear:
                    var lastYear = now.Year - 1;
                    state.PendingStartDate = new DateTime(lastYear, 1, 1);
                    state.PendingEndDate = new DateTime(lastYear, 12, 31);
                    break;
                    
                case DateRangePreset.ThisMonth:
                    // This Month: From the first day of the current month to now
                    state.PendingStartDate = new DateTime(now.Year, now.Month, 1);
                    state.PendingEndDate = now;
                    break;
                    
                case DateRangePreset.LastMonth:
                    // Last Month: From the first day of the previous month to the last day of the previous month
                    var firstDayOfLastMonth = new DateTime(now.Year, now.Month, 1).AddMonths(-1);
                    state.PendingStartDate = firstDayOfLastMonth;
                    state.PendingEndDate = new DateTime(firstDayOfLastMonth.Year, firstDayOfLastMonth.Month, DateTime.DaysInMonth(firstDayOfLastMonth.Year, firstDayOfLastMonth.Month));
                    break;
                    
                case DateRangePreset.Custom:
                    // Don't modify dates for custom preset - they should already be set by user
                    break;
            }
            
            state.SelectedPreset = preset;
            state.IsCustomRange = (preset == DateRangePreset.Custom);
        }

        /// <summary>
        /// Apply pending dates and determine if they represent a custom range
        /// </summary>
        public static void ApplyPendingDates(DateRangeState state)
        {
            state.StartDate = state.PendingStartDate;
            state.EndDate = state.PendingEndDate;
            
            // Calculate what dates would be for the currently selected preset
            var testState = new DateRangeState();
            ApplyPreset(testState, state.SelectedPreset);
            
            // If the pending dates exactly match what the current preset would generate,
            // keep the current preset (preserve user intent)
            if (state.PendingStartDate.Date == testState.PendingStartDate.Date && 
                state.PendingEndDate.Date == testState.PendingEndDate.Date)
            {
                // Dates match the selected preset perfectly - keep it
                state.IsCustomRange = false;
                return;
            }
            
            // ENHANCED: For January edge cases, be more conservative about changing presets
            var detectedPreset = DeterminePresetFromDates(state.StartDate, state.EndDate);
            
            // Special handling for January: if current preset is Last Year or Previous 12 Months
            // and they would produce the same dates, don't auto-switch
            if (IsJanuaryAmbiguousCase(state.SelectedPreset, detectedPreset))
            {
                // Keep the user's original selection in ambiguous January cases
                state.IsCustomRange = false;
                return;
            }
            
            // Only change to detected preset if user manually changed dates
            // and it's not Custom and it's different from what they selected
            if (detectedPreset != DateRangePreset.Custom && detectedPreset != state.SelectedPreset)
            {
                state.SelectedPreset = detectedPreset;
                state.IsCustomRange = false;
            }
            else if (detectedPreset == DateRangePreset.Custom)
            {
                // Only set to Custom if no preset matches
                state.SelectedPreset = DateRangePreset.Custom;
                state.IsCustomRange = true;
            }
        }

        /// <summary>
        /// Check if we're in a January ambiguous case where Last Year and Previous 12 Months overlap
        /// </summary>
        private static bool IsJanuaryAmbiguousCase(DateRangePreset currentPreset, DateRangePreset detectedPreset)
        {
            var now = DateTime.Now;
            
            // Only relevant in January
            if (now.Month != 1) return false;
            
            // Check if we have the ambiguous combination
            return (currentPreset == DateRangePreset.LastYear && detectedPreset == DateRangePreset.Previous12Months) ||
                   (currentPreset == DateRangePreset.Previous12Months && detectedPreset == DateRangePreset.LastYear);
        }

        /// <summary>
        /// Check if pending dates differ from current applied dates (user has made changes)
        /// </summary>
        public static bool HasPendingChanges(DateRangeState state)
        {
            return state.PendingStartDate.Date != state.StartDate.Date || 
                   state.PendingEndDate.Date != state.EndDate.Date;
        }

        /// <summary>
        /// Determine which preset matches the given dates with smart priority for January edge cases
        /// </summary>
        public static DateRangePreset DeterminePresetFromDates(DateTime start, DateTime end)
        {
            var now = DateTime.Now;
            // Detect AllDates: very early start date and end near now
            if (start.Date <= new DateTime(1900, 1, 1).Date && end.Date >= now.AddDays(-1).Date)
                return DateRangePreset.AllDates;
            var thisYearStart = new DateTime(now.Year, 1, 1);
            var lastYearStart = new DateTime(now.Year - 1, 1, 1);
            var lastYearEnd = new DateTime(now.Year - 1, 12, 31);

            // Check This Month (exact match: first day of this month to now)
            if (start.Date == new DateTime(now.Year, now.Month, 1).Date && end.Date >= now.AddDays(-1).Date && end.Date <= now.AddDays(1).Date)
                return DateRangePreset.ThisMonth;

            // Check Last Month (exact match: first day of last month to last day of last month)
            var firstDayOfLastMonth = new DateTime(now.Year, now.Month, 1).AddMonths(-1);
            var lastDayOfLastMonth = new DateTime(firstDayOfLastMonth.Year, firstDayOfLastMonth.Month, DateTime.DaysInMonth(firstDayOfLastMonth.Year, firstDayOfLastMonth.Month));
            
            if (start.Date == firstDayOfLastMonth.Date && end.Date == lastDayOfLastMonth.Date)
                return DateRangePreset.LastMonth;
                
            // Check This Year (January 1 to now)
            if (start.Date == thisYearStart.Date && end.Date >= now.AddDays(-1).Date && end.Date <= now.AddDays(1).Date)
                return DateRangePreset.ThisYear;

            // ENHANCED: Check Last Year BEFORE Previous 12 Months for better January handling
            // In January, both presets produce identical ranges, but Last Year is more explicit
            if (start.Date == lastYearStart.Date && end.Date == lastYearEnd.Date)
            {
                // If we're in January and the range matches both Last Year and Previous 12 Months,
                // prefer Last Year as it's more semantically clear
                return DateRangePreset.LastYear;
            }
                
            // Check Previous 12 Months (12 complete months ending with last complete month)
            var lastCompleteMonth = new DateTime(now.Year, now.Month, 1).AddMonths(-1);
            var prev12End = new DateTime(lastCompleteMonth.Year, lastCompleteMonth.Month, DateTime.DaysInMonth(lastCompleteMonth.Year, lastCompleteMonth.Month));
            var prev12Start = prev12End.AddMonths(-11);
            prev12Start = new DateTime(prev12Start.Year, prev12Start.Month, 1);
            
            if (start.Date == prev12Start.Date && end.Date == prev12End.Date)
                return DateRangePreset.Previous12Months;

            // If none match, it's a custom range
            return DateRangePreset.Custom;
        }

        /// <summary>
        /// Get display text for preset options
        /// </summary>
        public static List<PresetOption> GetPresetOptions(IStringLocalizer localizer)
        {
            return new List<PresetOption>
            {
                new() { Text = localizer["AllDates"], Value = DateRangePreset.AllDates },
                new() { Text = localizer["ThisYear"], Value = DateRangePreset.ThisYear },
                new() { Text = localizer["Previous12Months"], Value = DateRangePreset.Previous12Months },
                new() { Text = localizer["LastYear"], Value = DateRangePreset.LastYear },
                new() { Text = localizer["ThisMonth"], Value = DateRangePreset.ThisMonth },
                new() { Text = localizer["LastMonth"], Value = DateRangePreset.LastMonth },
                new() { Text = localizer["CustomRange"], Value = DateRangePreset.Custom }
            };
        }

        /// <summary>
        /// Check if two date ranges are effectively the same (ignoring time components)
        /// </summary>
        public static bool AreDatesEqual(DateTime date1, DateTime date2)
        {
            return date1.Date == date2.Date;
        }

        public class PresetOption
        {
            public string Text { get; set; } = string.Empty;
            public DateRangePreset Value { get; set; }
        }
    }
}