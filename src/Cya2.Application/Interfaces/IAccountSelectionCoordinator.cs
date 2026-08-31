using Cya2.Core.ValueObjects;

namespace Cya2.Application.Interfaces;

public interface IAccountSelectionCoordinator
{
    Task SelectAsync(
        UserAccountContextAccount account,
        string userId,
        bool isAdminOrViewer,
        bool persistSelection,
        DateRange? donorSummaryRange = null);
}
