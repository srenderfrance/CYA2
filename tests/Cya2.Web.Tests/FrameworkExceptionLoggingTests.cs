using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Cya2.Application.Interfaces;
using cya2.Services.Imports;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Cya2.Web.Tests;

public sealed class FrameworkExceptionLoggingTests
{
    private const string Sentinel = "sentinel-person@example.invalid sentinel-fund sentinel-password";

    [Fact]
    public async Task ProductionRequest_RethrownException_IsSanitizedInLogsAndResponse()
    {
        using var root = new WebAuthorizationTestHost();
        var logs = new CapturingLoggerProvider();
        var imports = new ThrowingImports();
        using var host = root.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IImportOrchestrationService>();
                services.AddSingleton<IImportOrchestrationService>(imports);
                services.RemoveAll<IAntiforgery>();
                services.AddSingleton<IAntiforgery, AcceptAntiforgery>();
            });
        });
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add(WebAuthorizationTestHost.IdentityHeader, "admin");

        using var response = await client.PostAsJsonAsync("/api/upload/donations/confirm", new { previewId = "test-preview" });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(1, imports.Calls);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain(Sentinel, body);
        Assert.All(logs.Entries, entry => Assert.DoesNotContain(Sentinel, entry.Text));
        Assert.Contains(logs.Entries, entry => entry.Category.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) && entry.ExceptionText.Contains("The request failed."));
        Assert.Contains(logs.Entries, entry => entry.Message.Contains("Request failed.") && entry.Text.Contains("InvalidOperationException"));
        Assert.Contains(logs.Entries, entry => entry.Category == "cya2.Controllers.UploadController" && entry.Message.Contains("Confirming donation import"));
    }

    [Fact]
    public async Task ProductionRequest_AbortedCancellation_IsSanitizedAndNotReportedAsFailure()
    {
        using var root = new WebAuthorizationTestHost();
        var logs = new CapturingLoggerProvider();
        using var host = root.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IImportOrchestrationService>();
                services.AddSingleton<IImportOrchestrationService, CanceledImports>();
                services.RemoveAll<IAntiforgery>();
                services.AddSingleton<IAntiforgery, AcceptAntiforgery>();
            });
        });
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add(WebAuthorizationTestHost.IdentityHeader, "admin");

        using var response = await client.PostAsJsonAsync("/api/upload/donations/confirm", new { previewId = "test-preview" });

        Assert.Equal((HttpStatusCode)499, response.StatusCode);
        Assert.All(logs.Entries, entry => Assert.DoesNotContain(Sentinel, entry.Text));
        Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains("Request failed."));
    }

    [Fact]
    public async Task ProductionRequest_ExceptionAfterResponseStarts_IsSanitizedAndFailsRequest()
    {
        using var root = new WebAuthorizationTestHost();
        var logs = new CapturingLoggerProvider();
        using var host = root.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IImportOrchestrationService>();
                services.AddSingleton<IImportOrchestrationService, StartedResponseImports>();
                services.RemoveAll<IAntiforgery>();
                services.AddSingleton<IAntiforgery, AcceptAntiforgery>();
            });
        });
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add(WebAuthorizationTestHost.IdentityHeader, "admin");

        var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using var response = await client.PostAsJsonAsync("/api/upload/donations/confirm", new { previewId = "test-preview" });
            await response.Content.ReadAsStringAsync();
        });

        Assert.DoesNotContain(Sentinel, exception.ToString());
        Assert.All(logs.Entries, entry => Assert.DoesNotContain(Sentinel, entry.Text));
        Assert.Contains(logs.Entries, entry => entry.Message.Contains("Request failed.") && entry.Text.Contains("InvalidOperationException"));
        Assert.Contains(logs.Entries, entry => entry.Category.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) && entry.ExceptionText.Contains("The request failed."));
    }

    [Fact]
    public async Task ImportWorker_FatalException_IsSanitizedInLogsAndStopsHost()
    {
        using var root = new WebAuthorizationTestHost();
        var logs = new CapturingLoggerProvider();
        var injected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = root.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(injected);
                services.AddSingleton<ILogger<ImportWorkQueue>, FaultingQueueLogger>();
                services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<ImportWorkQueue>());
            });
        });
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var appLifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = appLifetime.ApplicationStopping.Register(() => stopped.TrySetResult());

        var queue = host.Services.GetRequiredService<ImportWorkQueue>();
        await queue.EnqueueAsync(new ImportWorkItem("donations", "preview", Guid.NewGuid().ToString("N"), null, []));
        await injected.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.All(logs.Entries, entry => Assert.DoesNotContain(Sentinel, entry.Text));
        Assert.Contains(logs.Entries, entry => entry.Category.EndsWith("ImportWorkQueue", StringComparison.Ordinal) && entry.Message.Contains("stopped unexpectedly") && entry.Text.Contains("InvalidOperationException"));
        Assert.Contains(logs.Entries, entry => entry.Category.StartsWith("Microsoft.Extensions.Hosting", StringComparison.Ordinal) && entry.ExceptionText.Contains("The import work queue worker stopped unexpectedly."));
    }

    private sealed class ThrowingImports : IImportOrchestrationService
    {
        public int Calls { get; private set; }
        public Task<ImportResult> ImportFromPreviewAsync(string previewId, string importType, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException(Sentinel);
        }
        public Task<FilePreviewResult> PreviewAsync(Stream file, string importType, string fileName, string contentType, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ImportResult> StartImportFromPreviewAsync(string previewId, string importType, string? progressId = null) => throw new NotImplementedException();
    }

    private sealed class CanceledImports(IHttpContextAccessor accessor) : IImportOrchestrationService
    {
        public Task<ImportResult> ImportFromPreviewAsync(string previewId, string importType, CancellationToken cancellationToken = default)
        {
            var canceled = new CancellationToken(canceled: true);
            accessor.HttpContext!.RequestAborted = canceled;
            throw new OperationCanceledException(Sentinel, new Exception(Sentinel), canceled);
        }

        public Task<FilePreviewResult> PreviewAsync(Stream file, string importType, string fileName, string contentType, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ImportResult> StartImportFromPreviewAsync(string previewId, string importType, string? progressId = null) => throw new NotImplementedException();
    }

    private sealed class AcceptAntiforgery : IAntiforgery
    {
        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext context) => new("request", "cookie", "header", "form");
        public AntiforgeryTokenSet GetTokens(HttpContext context) => new("request", "cookie", "header", "form");
        public void SetCookieTokenAndHeader(HttpContext context) { }
        public Task<bool> IsRequestValidAsync(HttpContext context) => Task.FromResult(true);
        public Task ValidateRequestAsync(HttpContext context) => Task.CompletedTask;
    }

    private sealed class StartedResponseImports(IHttpContextAccessor accessor) : IImportOrchestrationService
    {
        public async Task<ImportResult> ImportFromPreviewAsync(string previewId, string importType, CancellationToken cancellationToken = default)
        {
            await accessor.HttpContext!.Response.StartAsync(cancellationToken);
            throw new InvalidOperationException(Sentinel, new Exception(Sentinel));
        }

        public Task<FilePreviewResult> PreviewAsync(Stream file, string importType, string fileName, string contentType, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ImportResult> StartImportFromPreviewAsync(string previewId, string importType, string? progressId = null) => throw new NotImplementedException();
    }

    private sealed class FaultingQueueLogger(ILoggerFactory factory, TaskCompletionSource injected) : ILogger<ImportWorkQueue>
    {
        private readonly ILogger _inner = factory.CreateLogger<ImportWorkQueue>();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _inner.BeginScope(state);
        public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (formatter(state, exception).Contains("Import work item dequeued.", StringComparison.Ordinal))
            {
                injected.TrySetResult();
                throw new InvalidOperationException(Sentinel, new Exception(Sentinel));
            }

            _inner.Log(logLevel, eventId, state, exception, formatter);
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();
        private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName, Entries, () => _scopes);
        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;
        public void Dispose() { }
    }

    private sealed record LogEntry(string Category, string Message, string ExceptionText, string StateText, string ScopeText)
    {
        public string Text => Message + ExceptionText + StateText + ScopeText;
    }

    private static string Describe(object? value)
    {
        if (value is IEnumerable<KeyValuePair<string, object?>> properties)
            return string.Join(" ", properties.Select(pair => $"{pair.Key}={pair.Value}"));
        return value?.ToString() ?? string.Empty;
    }

    private sealed class CaptureLogger(string category, ConcurrentQueue<LogEntry> entries, Func<IExternalScopeProvider> scopes) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => scopes().Push(state);
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var scopeValues = new List<string>();
            scopes().ForEachScope((scope, values) => values.Add(Describe(scope)), scopeValues);
            entries.Enqueue(new LogEntry(category, formatter(state, exception), exception?.ToString() ?? string.Empty,
                Describe(state), string.Join(" ", scopeValues)));
        }
    }
}
