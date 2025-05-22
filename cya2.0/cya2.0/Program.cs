using cya2._0.Components;
using DataLibrary;
using System.Security.Claims;
using Dapper;
using ModelsLibrary;
using UserAuth;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<IDataAccess, DataAccess>();
builder.Services.AddBlazorBootstrap();

// Add authentication services
builder.Services.AddScoped<UserAuth.UserRepository>(provider =>
{
    var dataAccess = provider.GetRequiredService<IDataAccess>();
    var connectionString = builder.Configuration.GetConnectionString("default");
    return new UserAuth.UserRepository(dataAccess, connectionString);
});

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    // Add this to specify your access denied path
    options.AccessDeniedPath = "/not-authorized";
})

.AddGoogle(options =>
{
    options.ClientId = builder.Configuration["Authentication:Google:ClientId"];
    options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
    options.CallbackPath = "/signin-google"; // The path Google redirects to after authentication

    // Add logging for authentication issues
    options.Events = new OAuthEvents
    {
        OnRemoteFailure = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogError("Google authentication failed: {Error}", context.Failure?.Message);
            return Task.CompletedTask;
        },
        OnTicketReceived = async context =>
        {
            // Get the user's claims
            var userRepository = context.HttpContext.RequestServices.GetRequiredService<UserAuth.UserRepository>();
            var email = context.Principal.FindFirstValue(ClaimTypes.Email);
            var name = context.Principal.FindFirstValue(ClaimTypes.Name);
            var googleId = context.Principal.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(email))
            {
                context.Fail("Email claim not received from Google");
                return;
            }

            // Update the Google ID for the user with matching email
            int rowsAffected = await userRepository.CompleteUserRegistrationAsync(googleId, email, name);

            // If no rows were updated, the user doesn't exist in our database
            if (rowsAffected == 0)
            {
                context.Fail("User not pre-registered in the system");
                context.Response.Redirect("/not-authorized");
                return;
            }

            // Fetch the full user record
            var user = await userRepository.GetUserByGoogleIdAsync(googleId);

            if (user != null)
            {
                var identity = (ClaimsIdentity)context.Principal.Identity;

                // Add the role claim (this is what [Authorize(Roles = "...")] checks)
                identity.AddClaim(new Claim(ClaimTypes.Role, user.AuthLevel ?? "User"));

                // Add custom claims
                identity.AddClaim(new Claim("AuthLevel", user.AuthLevel ?? ""));
                identity.AddClaim(new Claim("DefaultAccount", user.DefaultAccount ?? ""));
                identity.AddClaim(new Claim("Language", user.Language ?? ""));
                        }
        }
    };

});

// Add authorization services
builder.Services.AddAuthorization();

// Add Blazor authentication state provider
builder.Services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

// Add authentication middleware
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add Google authentication endpoints
app.MapGet("/login", (HttpContext context) =>
{
    return Results.Challenge(
        new AuthenticationProperties { RedirectUri = "/" },
        authenticationSchemes: new[] { GoogleDefaults.AuthenticationScheme });
});

app.MapGet("/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login-page");
});

app.Run();

