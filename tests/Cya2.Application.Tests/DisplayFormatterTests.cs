using Cya2.Shared.Utilities;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class DisplayFormatterTests
{
    [Theory]
    [InlineData("Account", "Account")]
    [InlineData("Account: Detail", "Account")]
    [InlineData("  Account : Detail: More  ", "Account")]
    [InlineData("Intern: Jane Doe", "Intern")]
    public void FormatFundDisplay_ReturnsTextBeforeFirstColon(string value, string expected)
    {
        Assert.Equal(expected, DisplayFormatter.FormatFundDisplay(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FormatFundDisplay_ReturnsEmptyForBlankValues(string? value)
    {
        Assert.Equal(string.Empty, DisplayFormatter.FormatFundDisplay(value));
    }
}
