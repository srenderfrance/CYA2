namespace Cya2.Application.Interfaces;

public interface IUserSelectionService
{
    void SetSelectedAccount(string userId, string account, TimeSpan? ttl = null);
    bool TryGetSelectedAccount(string userId, out string account);
    void RemoveSelectedAccount(string userId);
}
