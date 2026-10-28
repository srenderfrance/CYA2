using System.Globalization;
using System.Security.Claims;
using Cya2.Application.Interfaces;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using cya2.Components.Shared.LanguageResources;
using cya2.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cya2.Web.Tests;

public sealed class GoogleSignInCultureTests
{
    [Theory]
    [InlineData("es", "es-US")]
    [InlineData("ES-us", "es-US")]
    [InlineData("en", "en-US")]
    [InlineData("unknown", "en-US")]
    [InlineData(null, "en-US")]
    public async Task SuccessfulSignIn_SetsCultureCookieAndClaim(string? storedLanguage, string expectedCulture)
    {
        using var services = CreateServices(new GoogleSignInResult(new User { Language = storedLanguage! }));
        var context = CreateTicketContext(services);

        await services.GetRequiredService<IOptionsMonitor<GoogleOptions>>()
            .Get(GoogleDefaults.AuthenticationScheme).Events.OnTicketReceived(context);

        Assert.Equal(expectedCulture, context.Principal!.FindFirst("Language")!.Value);
        var cookie = Assert.Single(context.Response.Headers.SetCookie, value =>
            value!.StartsWith(CookieRequestCultureProvider.DefaultCookieName + "=", StringComparison.Ordinal));
        var cookieValue = Uri.UnescapeDataString(cookie!.Split(';')[0].Split('=', 2)[1]);
        var parsedCulture = CookieRequestCultureProvider.ParseCookieValue(cookieValue);
        Assert.Equal(expectedCulture, parsedCulture!.Cultures.Single().Value);
        Assert.Equal(expectedCulture, parsedCulture.UICultures.Single().Value);
    }

    [Fact]
    public async Task RejectedSignIn_DoesNotSetCultureCookie()
    {
        using var services = CreateServices(new GoogleSignInResult(null, "User not registered"));
        var context = CreateTicketContext(services);

        await services.GetRequiredService<IOptionsMonitor<GoogleOptions>>()
            .Get(GoogleDefaults.AuthenticationScheme).Events.OnTicketReceived(context);

        Assert.DoesNotContain(context.Response.Headers.SetCookie, value =>
            value!.StartsWith(CookieRequestCultureProvider.DefaultCookieName + "=", StringComparison.Ordinal));
    }

    [Fact]
    public void SpanishCulture_ResolvesSpanishResources()
    {
        using var services = new ServiceCollection().AddLogging().AddLocalization().BuildServiceProvider();
        var localizer = services.GetRequiredService<IStringLocalizer<Resource>>();
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es-US");
            Assert.Equal("Preferencia de Idioma", localizer["LanguagePreference"].Value);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    private static ServiceProvider CreateServices(GoogleSignInResult result)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Google:ClientId"] = "test-client",
            ["Authentication:Google:ClientSecret"] = "test-secret"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddCya2Authentication(configuration);
        services.AddSingleton<IGoogleSignInService>(new FakeGoogleSignInService(result));
        services.AddSingleton<IDatabaseAvailabilityMonitor, ConnectedDatabaseMonitor>();
        return services.BuildServiceProvider();
    }

    private static TicketReceivedContext CreateTicketContext(IServiceProvider services)
    {
        var httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.Request.Scheme = "https";
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Email, "test@example.invalid"),
            new Claim(ClaimTypes.NameIdentifier, "test-subject")
        }, GoogleDefaults.AuthenticationScheme));
        var scheme = new AuthenticationScheme(GoogleDefaults.AuthenticationScheme, null, typeof(GoogleHandler));
        var options = services.GetRequiredService<IOptionsMonitor<GoogleOptions>>().Get(GoogleDefaults.AuthenticationScheme);
        return new TicketReceivedContext(httpContext, scheme, options,
            new AuthenticationTicket(principal, new AuthenticationProperties(), scheme.Name));
    }

    private sealed class FakeGoogleSignInService(GoogleSignInResult result) : IGoogleSignInService
    {
        public Task<GoogleSignInResult> ResolveUserAsync(string googleId, string email) => Task.FromResult(result);
    }

    private sealed class ConnectedDatabaseMonitor : IDatabaseAvailabilityMonitor
    {
        public bool IsConnected => true;
        public string LastError => string.Empty;
        public bool BypassMonitoring => false;
        public bool AllowMySqlOperations => true;
        public event EventHandler<bool>? ConnectionStatusChanged;
        public void Suspend() { }
        public void Resume() => ConnectionStatusChanged?.Invoke(this, true);
        public void SetBypassMonitoring(bool bypass) { }
        public void MarkAsDisconnected(string reason) { }
    }
}
