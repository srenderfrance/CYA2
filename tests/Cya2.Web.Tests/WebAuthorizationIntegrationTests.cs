using System.Net;
using Xunit;

namespace Cya2.Web.Tests;

public sealed class WebAuthorizationIntegrationTests : IClassFixture<WebAuthorizationTestHost>
{
    private readonly WebAuthorizationTestHost _host;

    public WebAuthorizationIntegrationTests(WebAuthorizationTestHost host)
    {
        _host = host;
    }

    [Fact]
    public async Task UploadPreview_RejectsAnonymousRequest()
    {
        using var client = _host.CreateClient();

        var response = await client.PostAsync("/api/upload/donations/preview", new MultipartFormDataContent());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("regular")]
    [InlineData("unknown")]
    public async Task UploadPreview_RejectsNonAdminIdentity(string identity)
    {
        using var client = CreateClient(identity);

        var response = await client.PostAsync("/api/upload/donations/preview", new MultipartFormDataContent());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UploadPreview_AllowsAdminIdentityThroughAuthorizationFilter()
    {
        using var client = CreateClient("admin");

        var response = await client.PostAsync("/api/upload/donations/preview", new MultipartFormDataContent());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private HttpClient CreateClient(string identity)
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Add(WebAuthorizationTestHost.IdentityHeader, identity);
        return client;
    }
}
