using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Cya2.Database.Tests;

internal sealed class TestOutputLogger<T>(ITestOutputHelper output) : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        output.WriteLine($"{logLevel}: {typeof(T).Name}: {formatter(state, exception)}");
        if (exception is not null) output.WriteLine(exception.ToString());
    }
}
