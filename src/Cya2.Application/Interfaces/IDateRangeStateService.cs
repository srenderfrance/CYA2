using Cya2.Application.DTOs;

namespace Cya2.Application.Interfaces;

public interface IDateRangeStateService
{
    DateRangeStateDto InitializeDateRange(ISessionUserStateService sessionState);
    void SaveDateRangeToSessionState(ISessionUserStateService sessionState, DateRangeStateDto state);
    void ApplyPreset(DateRangeStateDto state, Cya2.Application.DTOs.DateRangePreset preset);
    void ApplyPendingDates(DateRangeStateDto state);
    bool HasPendingChanges(DateRangeStateDto state);
    Cya2.Application.DTOs.DateRangePreset DeterminePresetFromDates(DateTime start, DateTime end);
}
