using Cya2.Core.Entities;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class CoreEntityInvariantTests
{
    [Fact]
    public void Account_IsValidRequiresCoreAccountFields()
    {
        var account = new Account
        {
            Fund = "Fund",
            AccountingClass = "Class",
            AccountNumber = "1000"
        };

        Assert.True(account.IsValid());

        account.UpdateFund(string.Empty);

        Assert.False(account.IsValid());
    }

    [Fact]
    public void SubAccount_RecognizesOnlySupportedKinds()
    {
        var merged = new SubAccount(42, "Merged Fund", "merged");
        var separate = new SubAccount(42, "Separate Fund", "Separate");
        var invalid = new SubAccount(42, "Invalid Fund", "Other");

        Assert.True(merged.IsMerged());
        Assert.True(merged.IsValid());
        Assert.True(separate.IsSeparate());
        Assert.True(separate.IsValid());
        Assert.False(invalid.IsValidKind());
        Assert.False(invalid.IsValid());
    }
}