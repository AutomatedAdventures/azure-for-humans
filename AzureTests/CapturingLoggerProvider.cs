using Microsoft.Extensions.Logging;

namespace AzureTests;

public class CapturingLoggerProvider(List<string> logs) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(logs);

    public void Dispose() { }

    private sealed class CapturingLogger(List<string> logs) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            logs.Add(formatter(state, exception));
    }
}
