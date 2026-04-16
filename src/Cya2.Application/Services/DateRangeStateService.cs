using DateRangePreset = Cya2.Application.DTOs.DateRangePreset;
using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;

namespace Cya2.Application.Services;

public class DateRangeStateService : IDateRangeStateService
{
    public void SaveDateRangeToSessionState(ISessionUserStateService sessionState, DateRangeStateDto state)
    {
        sessionState.SelectedStartDate = state.StartDate;
        sessionState.SelectedEndDate = state.EndDate;
        sessionState.SelectedDatePreset = state.SelectedPreset.ToString();
    }

    public DateRangeStateDto InitializeDateRange(ISessionUserStateService sessionState)
    {
        var state = new DateRangeStateDto();

        if (sessionState.SelectedStartDate.HasValue && sessionState.SelectedEndDate.HasValue)
        {
            state.StartDate = sessionState.SelectedStartDate.Value;
            state.EndDate = sessionState.SelectedEndDate.Value;
            state.PendingStartDate = state.StartDate;
            state.PendingEndDate = state.EndDate;

            if (!string.IsNullOrEmpty(sessionState.SelectedDatePreset) &&
                Enum.TryParse<DateRangePreset>(sessionState.SelectedDatePreset, out var storedPreset))
            {
                state.SelectedPreset = storedPreset;
                state.IsCustomRange = storedPreset == DateRangePreset.Custom;
            }
            else
            {
                state.SelectedPreset = DeterminePresetFromDates(state.StartDate, state.EndDate);
                state.IsCustomRange = state.SelectedPreset == DateRangePreset.Custom;
            }
        }
        else
        {
            state.SelectedPreset = DateRangePreset.ThisMonth;
            ApplyPreset(state, state.SelectedPreset);
            state.StartDate = state.PendingStartDate;
            state.EndDate = state.PendingEndDate;
            state.IsCustomRange = false;
        }

        return state;
    }

    public void ApplyPreset(DateRangeStateDto state, DateRangePreset preset)
    {
        var now = DateTime.Now;

        switch (preset)
        {
            case DateRangePreset.AllDates:
                state.PendingStartDate = new DateTime(1900, 1, 1);
                state.PendingEndDate = now;
                break;
            case DateRangePreset.ThisYear:
                state.PendingStartDate = new DateTime(now.Year, 1, 1);
                state.PendingEndDate = now;
                break;
            case DateRangePreset.Previous12Months:
                var lastCompleteMonth = new DateTime(now.Year, now.Month, 1).AddMonths(-1);
                state.PendingEndDate = new DateTime(lastCompleteMonth.Year, lastCompleteMonth.Month, DateTime.DaysInMonth(lastCompleteMonth.Year, lastCompleteMonth.Month));
                state.PendingStartDate = state.PendingEndDate.AddMonths(-11);
                state.PendingStartDate = new DateTime(state.PendingStartDate.Year, state.PendingStartDate.Month, 1);
                break;
            case DateRangePreset.LastYear:
                var lastYear = now.Year - 1;
                state.PendingStartDate = new DateTime(lastYear, 1, 1);
                state.PendingEndDate = new DateTime(lastYear, 12, 31);
                break;
            case DateRangePreset.ThisMonth:
                state.PendingStartDate = new DateTime(now.Year, now.Month, 1);
                state.PendingEndDate = now;
                break;
            case DateRangePreset.LastMonth:
                var firstDayOfLastMonth = new DateTime(now.Year, now.Month, 1).AddMonths(-1);
                state.PendingStartDate = firstDayOfLastMonth;
                state.PendingEndDate = new DateTime(firstDayOfLastMonth.Year, firstDayOfLastMonth.Month, DateTime.DaysInMonth(firstDayOfLastMonth.Year, firstDayOfLastMonth.Month));
                break;
            case DateRangePreset.Custom:
                break;
        }

        state.SelectedPreset = preset;
        state.IsCustomRange = preset == DateRangePreset.Custom;
    }

    public void ApplyPendingDates(DateRangeStateDto state)
    {
        state.StartDate = state.PendingStartDate;
        state.EndDate = state.PendingEndDate;

        var testState = new DateRangeStateDto();
        ApplyPreset(testState, state.SelectedPreset);

        if (state.PendingStartDate.Date == testState.PendingStartDate.Date &&
            state.PendingEndDate.Date == testState.PendingEndDate.Date)
        {
            state.IsCustomRange = false;
            return;
        }

        var detectedPreset = DeterminePresetFromDates(state.StartDate, state.EndDate);

        if (IsJanuaryAmbiguousCase(state.SelectedPreset, detectedPreset))
        {
            state.IsCustomRange = false;
            return;
        }

        if (detectedPreset != DateRangePreset.Custom && detectedPreset != state.SelectedPreset)
        {
            state.SelectedPreset = detectedPreset;
            state.IsCustomRange = false;
        }
        else if (detectedPreset == DateRangePreset.Custom)
        {
            state.SelectedPreset = DateRangePreset.Custom;
            state.IsCustomRange = true;
        }
    }

    public bool HasPendingChanges(DateRangeStateDto state)
    {
        return state.PendingStartDate.Date != state.StartDate.Date ||
               state.PendingEndDate.Date != state.EndDate.Date;
    }

    public DateRangePreset DeterminePresetFromDates(DateTime start, DateTime end)
    {
        var now = DateTime.Now;

        if (start.Date <= new DateTime(1900, 1, 1).Date && end.Date >= now.AddDays(-1).Date)
            return DateRangePreset.AllDates;

        var thisYearStart = new DateTime(now.Year, 1, 1);
        var lastYearStart = new DateTime(now.Year - 1, 1, 1);
        var lastYearEnd = new DateTime(now.Year - 1, 12, 31);

        if (start.Date == new DateTime(now.Year, now.Month, 1).Date && end.Date >= now.AddDays(-1).Date && end.Date <= now.AddDays(1).Date)
            return DateRangePreset.ThisMonth;

        var firstDayOfLastMonth = new DateTime(now.Year, now.Month, 1).AddMonths(-1);
        var lastDayOfLastMonth = new DateTime(firstDayOfLastMonth.Year, firstDayOfLastMonth.Month, DateTime.DaysInMonth(firstDayOfLastMonth.Year, firstDayOfLastMonth.Month));

        if (start.Date == firstDayOfLastMonth.Date && end.Date == lastDayOfLastMonth.Date)
            return DateRangePreset.LastMonth;

        if (start.Date == thisYearStart.Date && end.Date >= now.AddDays(-1).Date && end.Date <= now.AddDays(1).Date)
            return DateRangePreset.ThisYear;

        if (start.Date == lastYearStart.Date && end.Date == lastYearEnd.Date)
            return DateRangePreset.LastYear;

        var lastCompleteMonth = new DateTime(now.Year, now.Month, 1).AddMonths(-1);
        var prev12End = new DateTime(lastCompleteMonth.Year, lastCompleteMonth.Month, DateTime.DaysInMonth(lastCompleteMonth.Year, lastCompleteMonth.Month));
        var prev12Start = prev12End.AddMonths(-11);
        prev12Start = new DateTime(prev12Start.Year, prev12Start.Month, 1);

        if (start.Date == prev12Start.Date && end.Date == prev12End.Date)
            return DateRangePreset.Previous12Months;

        return DateRangePreset.Custom;
    }

    private static bool IsJanuaryAmbiguousCase(DateRangePreset currentPreset, DateRangePreset detectedPreset)
    {
        var now = DateTime.Now;
        if (now.Month != 1) return false;

        return (currentPreset == DateRangePreset.LastYear && detectedPreset == DateRangePreset.Previous12Months) ||
               (currentPreset == DateRangePreset.Previous12Months && detectedPreset == DateRangePreset.LastYear);
    }
}
