using Cya2.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Cya2.Web.Tests;

public sealed class DatabaseMonitorPrivacyTests
{
    [Fact]
    public void Disconnect_DoesNotStoreOrLogSuppliedReason()
    {
        const string sentinel = "private-person@example.invalid password=secret";
        var logger = new RecordingLogger<DatabaseMonitorService>();
        var monitor = new DatabaseMonitorService(new ConfigurationBuilder().Build(), logger);
        var notified = false;
        monitor.ConnectionStatusChanged += (_, connected) => notified = !connected;

        monitor.MarkAsDisconnected(sentinel);

        Assert.False(monitor.IsConnected);
        Assert.True(notified);
        Assert.DoesNotContain(sentinel, monitor.LastError);
        Assert.DoesNotContain(sentinel, string.Join(" ", logger.Entries));
        Assert.NotEmpty(monitor.LastError);
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
