using Cya2.Core.Interfaces;
using cya2.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Cya2.Web.Tests;

public sealed class DatabaseCheckPrivacyTests
{
    [Fact]
    public async Task AuthDatabaseFailure_LogsSafeMetadataAndRedirects()
    {
        const string sentinel = "private-person@example.invalid password=secret";
        var logger = new RecordingLogger<DatabaseCheckMiddleware>();
        var middleware = new DatabaseCheckMiddleware(_ => Task.CompletedTask, logger,
            new ConfigurationBuilder().Build(), new ThrowingMonitor(sentinel));
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/login";
        context.TraceIdentifier = "test-trace";

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal("/database-error", context.Response.Headers.Location.ToString());
        var log = string.Join(" ", logger.Entries);
        Assert.DoesNotContain(sentinel, log);
        Assert.Contains("InvalidOperationException", log);
        Assert.Contains("test-trace", log);
    }

    private sealed class ThrowingMonitor(string message) : IDatabaseAvailabilityMonitor
    {
        public bool IsConnected => throw new InvalidOperationException(message);
        public string LastError => string.Empty;
        public bool BypassMonitoring => false;
        public bool AllowMySqlOperations => false;
        public event EventHandler<bool>? ConnectionStatusChanged { add { } remove { } }
        public void Suspend() { }
        public void Resume() { }
        public void SetBypassMonitoring(bool bypass) { }
        public void MarkAsDisconnected(string reason) { }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add(formatter(state, exception) + exception?.ToString());
    }
}
