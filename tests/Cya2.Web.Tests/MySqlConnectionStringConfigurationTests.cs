using cya2.Extensions;
using MySql.Data.MySqlClient;
using Xunit;

namespace Cya2.Web.Tests;

public sealed class MySqlConnectionStringConfigurationTests
{
    [Fact]
    public void TryNormalize_UsesConfiguredPoolSizeForSupportedConnectionString()
    {
        var result = MySqlConnectionStringConfiguration.TryNormalize(
            "Server=localhost;Database=cya2_test;User ID=test;Password=test;SslMode=Disabled;",
            15u,
            out var normalizedConnectionString);

        Assert.True(result);
        Assert.Equal(15u, new MySqlConnectionStringBuilder(normalizedConnectionString).MaximumPoolSize);
    }

    [Fact]
    public void TryNormalize_ReturnsFalseForUnsupportedSslMode()
    {
        var result = MySqlConnectionStringConfiguration.TryNormalize(
            "Server=localhost;Database=cya2_test;User ID=test;Password=test;SslMode=None;",
            15u,
            out var normalizedConnectionString);

        Assert.False(result);
        Assert.Empty(normalizedConnectionString);
    }
}
