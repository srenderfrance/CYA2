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
using Microsoft.AspNetCore.Authorization;

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
    // Configure cookie auth for automatic redirect
    options.LoginPath = "/api/login";
    options.AccessDeniedPath = "/not-authorized";
    options.ExpireTimeSpan = TimeSpan.FromHours(24);
    options.SlidingExpiration = false;
    
    // Add this to ensure redirects work properly after authentication
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        }
        else
        {
            context.Response.Redirect(context.RedirectUri);
        }
        return Task.CompletedTask;
    };
})
.AddGoogle(options =>
{
    options.ClientId = builder.Configuration["Authentication:Google:ClientId"];
    options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
    options.CallbackPath = "/signin-google";
    
    options.SaveTokens = true; // Save the access token
    
    options.Events = new OAuthEvents
    {
        // This runs before the Google challenge is issued - make sure redirect URI is / not /api/login
        OnRedirectToAuthorizationEndpoint = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            
            if (context.Properties.RedirectUri == "/api/login")
            {
                logger.LogWarning("Found incorrect RedirectUri: /api/login, changing to /");
                context.Properties.RedirectUri = "/";
            }
            
            logger.LogInformation("Redirecting to Google with final RedirectUri: {RedirectUri}",
                context.Properties.RedirectUri);
                
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        },
        
        // This runs when the Google authentication process fails
        OnRemoteFailure = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogError("Google authentication failed: {Error}", context.Failure?.Message);
            
            context.Response.Redirect("/not-authorized");
            context.HandleResponse();
            return Task.CompletedTask;
        },
        
        // This runs after successful authentication with Google
        OnTicketReceived = async context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            var userRepository = context.HttpContext.RequestServices.GetRequiredService<UserAuth.UserRepository>();
            var email = context.Principal.FindFirstValue(ClaimTypes.Email);
            var name = context.Principal.FindFirstValue(ClaimTypes.Name);
            var googleId = context.Principal.FindFirstValue(ClaimTypes.NameIdentifier);

            // Step 1: Check if the user exists by email
            var user = await userRepository.GetUserByEmailAsync(email);
            
            if (user == null)
            {
                // User doesn't exist in our system
                logger.LogWarning("Authentication failed: User with email {Email} not found", email);
                
                // Fail the authentication and prevent automatic cookie creation
                context.Fail("User not registered in system");
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                
                // Redirect to not-authorized without letting authentication middleware continue
                context.Response.Redirect("/not-authorized");
                context.HandleResponse();
                return;
            }

            // Step 2: Verify GoogleId or update it
            if (!string.IsNullOrEmpty(user.GoogleId) && user.GoogleId != googleId)
            {
                // GoogleId mismatch
                logger.LogWarning("Authentication failed: GoogleId mismatch for {Email}", email);
                
                // Fail the authentication and prevent automatic cookie creation
                context.Fail("Google account mismatch");
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                
                // Redirect to not-authorized
                context.Response.Redirect("/not-authorized");
                context.HandleResponse();
                return;
            }

            // Step 3: Update GoogleId if needed
            if (string.IsNullOrEmpty(user.GoogleId))
            {
                await userRepository.CompleteUserRegistrationAsync(googleId, email, name);
                logger.LogInformation("Updated GoogleId for {Email}", email);
                
                // Re-fetch user
                user = await userRepository.GetUserByEmailAsync(email);
            }

            // Step 4: Apply claims for the authenticated user
            var identity = (ClaimsIdentity)context.Principal.Identity;
            identity.AddClaim(new Claim(ClaimTypes.Role, user.AuthLevel ?? "User"));
            identity.AddClaim(new Claim("AuthLevel", user.AuthLevel ?? ""));
            identity.AddClaim(new Claim("DefaultAccount", user.DefaultAccount ?? ""));
            identity.AddClaim(new Claim("Language", user.Language ?? ""));
            
            logger.LogInformation("User {Email} successfully authenticated with role {Role}", email, user.AuthLevel ?? "User");
            
            // IMPORTANT ADDITION: Ensure redirect is set properly
            // Make sure we have a valid return URL in authentication properties
            if (context.Properties == null)
            {
                context.Properties = new AuthenticationProperties { RedirectUri = "/" };
            }
            else if (string.IsNullOrEmpty(context.Properties.RedirectUri))
            {
                context.Properties.RedirectUri = "/";
            }
            
            logger.LogInformation("Setting redirect URI to {RedirectUri}", context.Properties.RedirectUri);
        },
        
        // Add this event to ensure successful redirection after authentication
        OnCreatingTicket = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Creating ticket with redirect URI: {RedirectUri}", 
                context.Properties?.RedirectUri ?? "Not set");
            
            // Force override redirect URI to home
            if (context.Properties != null) 
            {
                context.Properties.RedirectUri = "/";
                logger.LogInformation("Force set redirect URI to /");
            }
            
            return Task.CompletedTask;
        }
    };
});

// Update authorization to require authentication by default
builder.Services.AddAuthorization(options =>
{
    // This sets the default policy to require authentication for all pages
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
        
    // Define a policy for pages that should be accessible without authentication
    options.AddPolicy("AllowAnonymous", policy => policy.RequireAssertion(_ => true));
});

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

// Add post-authentication redirect middleware
app.UsePostAuthenticationRedirect();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Fix the login endpoint
app.MapGet("/api/login", (HttpContext context, ILogger<Program> logger) =>
{
    logger.LogInformation("Login endpoint hit, setting up challenge with home redirect");
    
    // Always use home as redirect URI
    var properties = new AuthenticationProperties
    {
        RedirectUri = "/", // Always redirect to home page
        AllowRefresh = true,
        ExpiresUtc = DateTimeOffset.UtcNow.AddHours(24),
        IsPersistent = true
    };
    
    // Add timestamp to force bypass cache
    properties.Items["ts"] = DateTime.UtcNow.Ticks.ToString();
    
    // Issue an authentication challenge for Google
    logger.LogInformation("Issuing Google challenge with RedirectUri: /");
    return Results.Challenge(properties, new[] { GoogleDefaults.AuthenticationScheme });
});

// Update logout endpoint to use HTTP redirect instead of Results.Redirect
app.MapGet("/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    
    // Use HTTP redirect instead of Results.Redirect
    context.Response.Redirect("/logged-out");
    return Task.CompletedTask;
});

app.Run();

// Define the middleware AFTER all top-level statements
public class PostAuthenticationRedirectMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<PostAuthenticationRedirectMiddleware> _logger;

    public PostAuthenticationRedirectMiddleware(RequestDelegate next, ILogger<PostAuthenticationRedirectMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Check if this is the signin-google path
        bool isSignInCallback = context.Request.Path.StartsWithSegments("/signin-google");

        // Call the next middleware in the pipeline
        await _next(context);

        // If this is the signin-google callback URL
        if (isSignInCallback && context.User?.Identity?.IsAuthenticated == true)
        {
            _logger.LogInformation("Post-auth middleware: User is authenticated on signin-google callback");
            
            // The user has been authenticated - redirect to home using JavaScript
            if (!context.Response.HasStarted)
            {
                _logger.LogInformation("Post-auth middleware: Setting up JS redirect");
                
                // Add a JS script that will redirect
                var script = @"
                <script>
                    console.log('Authentication successful, redirecting to home...');
                    window.localStorage.setItem('redirectToHome', 'true');
                    window.location.href = '/';
                </script>";

                // Append the script to the response
                await context.Response.WriteAsync(script);
            }
            else
            {
                _logger.LogWarning("Post-auth middleware: Cannot set up redirect, response already started");
            }
        }
    }
}

// Extension method for the middleware
public static class PostAuthenticationRedirectMiddlewareExtensions
{
    public static IApplicationBuilder UsePostAuthenticationRedirect(
        this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<PostAuthenticationRedirectMiddleware>();
    }
}


