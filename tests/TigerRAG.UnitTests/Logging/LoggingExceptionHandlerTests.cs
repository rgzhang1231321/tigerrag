using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using TigerRAG.Infrastructure.Logging;

namespace TigerRAG.UnitTests.Logging;

/// <summary>
/// 验证 MVC 管道异常处理：必须带堆栈 LogError，必须携带当前请求的 RequestId，
/// 必须返回 false 让默认 ProblemDetails 流继续走（以便 ApiResponseMiddleware 仍能包信封）。
/// </summary>
public sealed class LoggingExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_LogsErrorWithStackAndRequestId_AndReturnsFalse()
    {
        var sink = new TestLogSink();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestLoggerProvider(sink)));
        var handler = new LoggingExceptionHandler(loggerFactory.CreateLogger<LoggingExceptionHandler>());

        var context = new DefaultHttpContext();
        context.Items[RequestIdKeys.ItemKey] = "exception-req-id";

        var result = await handler.TryHandleAsync(context, new InvalidOperationException("synthetic"), CancellationToken.None);

        Assert.False(result);
        var record = Assert.Single(sink.Records);
        Assert.Equal(LogLevel.Error, record.Level);
        Assert.NotNull(record.Exception);
        Assert.IsType<InvalidOperationException>(record.Exception);
        Assert.Contains("exception-req-id", record.Message);
        Assert.Contains("synthetic", record.Message);
    }

    private sealed class TestLogSink
    {
        public List<LogRecord> Records { get; } = new();
    }

    private sealed record LogRecord(LogLevel Level, string Message, Exception? Exception);

    private sealed class TestLoggerProvider(TestLogSink sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new TestLogger(sink);
        public void Dispose() { }
    }

    private sealed class TestLogger(TestLogSink sink) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            sink.Records.Add(new LogRecord(logLevel, formatter(state, exception), exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}