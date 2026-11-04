using System.Net;
using System.Security.Claims;
using Cya2.Core.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cya2.Web.Tests;

public sealed class HealthEndpointTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Liveness_IsAnonymousAndIndependentOfDatabase(bool connected)
    {
        using var root = new WebAuthorizationTestHost();
        var monitor = new TestMonitor { IsConnected = connected };
        using var host = CreateHost(root, monitor);
        using var client = CreateClient(host);

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, monitor.ReadCount);
        Assert.Null(response.Headers.Location);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData("/health/ready", true)]
    [InlineData("/health/ready", false)]
    [InlineData("/health", true)]
    [InlineData("/health", false)]
    public async Task Readiness_RejectsAnonymousWithoutRedirectOrDependencyCheck(string path, bool connected)
    {
        using var root = new WebAuthorizationTestHost();
        var monitor = new TestMonitor { IsConnected = connected };
        using var host = CreateHost(root, monitor);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        Assert.Equal(0, monitor.ReadCount);
    }

    [Theory]
    [InlineData("/health/ready")]
    [InlineData("/health")]
    public async Task Readiness_UsesApplicationCookie_AndReportsOutageAndRecovery(string path)
    {
        using var root = new WebAuthorizationTestHost();
        var monitor = new TestMonitor();
        using var host = CreateHost(root, monitor);
        using var client = CreateClient(host);
        AddApplicationCookie(host, client);

        using var healthy = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);
        Assert.Equal("Healthy", await healthy.Content.ReadAsStringAsync());
        Assert.True(healthy.Headers.CacheControl?.NoStore);

        monitor.IsConnected = false;
        using var unavailable = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Equal("Unhealthy", await unavailable.Content.ReadAsStringAsync());
        Assert.Null(unavailable.Headers.Location);
        Assert.True(unavailable.Headers.CacheControl?.NoStore);

        monitor.IsConnected = true;
        using var recovered = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal("Healthy", await recovered.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task NormalPage_StillRedirectsToLimitedModeDuringOutage()
    {
        using var root = new WebAuthorizationTestHost();
        using var host = CreateHost(root, new TestMonitor { IsConnected = false });
        using var client = CreateClient(host);
        client.DefaultRequestHeaders.Add(WebAuthorizationTestHost.IdentityHeader, "regular");

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/database-error", response.Headers.Location?.OriginalString);
    }

    private static WebApplicationFactory<Program> CreateHost(WebAuthorizationTestHost root, TestMonitor monitor) =>
        root.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDatabaseAvailabilityMonitor>();
            services.AddSingleton<IDatabaseAvailabilityMonitor>(monitor);
        }));

    private static HttpClient CreateClient(WebApplicationFactory<Program> host) => host.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });

    private static void AddApplicationCookie(WebApplicationFactory<Program> host, HttpClient client)
    {
        var scheme = CookieAuthenticationDefaults.AuthenticationScheme;
        var options = host.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(scheme);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "health-test")], scheme));
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5)
        }, scheme);
        client.DefaultRequestHeaders.Add("Cookie", $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}");
    }

    private sealed class TestMonitor : IDatabaseAvailabilityMonitor
    {
        private bool _connected = true;
        public int ReadCount { get; private set; }
        public bool IsConnected
        {
            get { ReadCount++; return _connected; }
            set => _connected = value;
        }
        public string LastError => throw new InvalidOperationException("Sensitive diagnostic details must not be accessed by health endpoints.");
        public bool BypassMonitoring => false;
        public bool AllowMySqlOperations => _connected;
        public event EventHandler<bool>? ConnectionStatusChanged;
        public void Suspend() { }
        public void Resume() { }
        public void SetBypassMonitoring(bool bypass) { }
        public void MarkAsDisconnected(string reason)
        {
            _connected = false;
            ConnectionStatusChanged?.Invoke(this, false);
        }
    }
}
