namespace Cya2.Core.Entities;

public class UserAccountAccess : BaseEntity
{
    public User User { get; private set; } = null!;
    public int UserId { get; private set; }
    public Account Account { get; private set; } = null!;
    public int AccountId { get; private set; }

    // Private constructor for EF Core
    private UserAccountAccess() { }

    public UserAccountAccess(User user, Account account)
    {
        User = user ?? throw new ArgumentNullException(nameof(user));
        UserId = user.Id;
        Account = account ?? throw new ArgumentNullException(nameof(account));
        AccountId = account.Id;
    }
}