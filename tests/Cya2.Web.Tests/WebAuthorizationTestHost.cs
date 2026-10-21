using System.Security.Claims;
using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cya2.Web.Tests;

public sealed class WebAuthorizationTestHost : WebApplicationFactory<Program>
{
    public const string AuthenticationScheme = "IntegrationTest";
    public const string IdentityHeader = "X-Test-Identity";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IDatabaseAvailabilityMonitor>();
            services.RemoveAll<IDatabaseStartupProbe>();
            services.RemoveAll<IAdminCacheWarmupCoordinator>();

            services.AddSingleton<IDatabaseAvailabilityMonitor, ConnectedDatabaseMonitor>();
            services.AddSingleton<IDatabaseStartupProbe, ConnectedDatabaseStartupProbe>();
            services.AddSingleton<IAdminCacheWarmupCoordinator, NoOpAdminCacheWarmupCoordinator>();

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = AuthenticationScheme;
                options.DefaultChallengeScheme = AuthenticationScheme;
                options.DefaultForbidScheme = AuthenticationScheme;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(AuthenticationScheme, _ => { });
        });
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(IdentityHeader, out var identity))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, identity.ToString()),
                new(ClaimTypes.Name, identity.ToString())
            };

            if (string.Equals(identity, "admin", StringComparison.OrdinalIgnoreCase))
            {
                claims.Add(new Claim(ClaimTypes.Role, "Admin"));
                claims.Add(new Claim("AuthLevel", "Admin"));
            }
            else if (string.Equals(identity, "regular", StringComparison.OrdinalIgnoreCase))
            {
                claims.Add(new Claim(ClaimTypes.Role, "User"));
                claims.Add(new Claim("AuthLevel", "User"));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationScheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, AuthenticationScheme)));
        }
    }

    private sealed class ConnectedDatabaseMonitor : IDatabaseAvailabilityMonitor
    {
        public bool IsConnected => true;
        public string LastError => string.Empty;
        public bool BypassMonitoring => true;
        public bool AllowMySqlOperations => true;
        public event EventHandler<bool>? ConnectionStatusChanged;
        public void Suspend() { }
        public void Resume() => ConnectionStatusChanged?.Invoke(this, true);
        public void SetBypassMonitoring(bool bypass) { }
        public void MarkAsDisconnected(string reason) { }
    }

    private sealed class ConnectedDatabaseStartupProbe : IDatabaseStartupProbe
    {
        public bool Check(CancellationToken cancellationToken = default) => true;
    }

    private sealed class NoOpAdminCacheWarmupCoordinator : IAdminCacheWarmupCoordinator
    {
        public void QueueWarmup(bool warmSummaries = true) { }
        public Task WaitForWarmupAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
