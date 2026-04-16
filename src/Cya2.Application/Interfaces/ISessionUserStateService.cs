namespace Cya2.Application.Interfaces;

public interface ISessionUserStateService
{
    string CurrentUserId { get; set; }
    string SelectedAccountFund { get; set; }
    string DefaultAccountFund { get; set; }

    DateTime? SelectedStartDate { get; set; }
    DateTime? SelectedEndDate { get; set; }
    string SelectedDatePreset { get; set; }

    void ResetForUser(string userId);
}
