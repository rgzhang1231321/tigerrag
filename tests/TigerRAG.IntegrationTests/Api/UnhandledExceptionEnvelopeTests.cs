using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TigerRAG.Application.Security;
using TigerRAG.Api.Common;

namespace TigerRAG.IntegrationTests.Api;

/// <summary>
/// 锁定"未捕获异常 → 仍按 ApiResponse 格式返回"的契约：单个控制器不必逐处写 try/catch，
/// 由 <c>ApiResponseMiddleware</c> 兜底统一映射为 <c>InternalServerError</c>。
/// </summary>
public sealed class UnhandledExceptionEnvelopeTests
{
    [Fact]
    public async Task SaltEndpoint_WhenDalThrows_ReturnsEnvelopeWithInternalServerError()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:Issuer", "TigerRAG.Tests");
            builder.UseSetting("Jwt:Audience", "TigerRAG.Tests");
            builder.UseSetting("Jwt:SigningKey", "test-only-signing-key-with-at-least-32-characters");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IUserDal>();
                services.AddSingleton<IUserDal>(new ThrowingUserDal());
            });
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/auth/salt?userName=admin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal(FlagStatesOption.InternalServerError, (FlagStatesOption)root.GetProperty("code").GetInt32());
        Assert.False(root.GetProperty("flag").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("data").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("requestId").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task SaltEndpoint_WhenDalThrows_LogsWithSameRequestIdAsResponse()
    {
        var sink = new CapturingLogSink();
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:Issuer", "TigerRAG.Tests");
            builder.UseSetting("Jwt:Audience", "TigerRAG.Tests");
            builder.UseSetting("Jwt:SigningKey", "test-only-signing-key-with-at-least-32-characters");
            builder.ConfigureLogging(logging => logging.AddProvider(sink));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IUserDal>();
                services.AddSingleton<IUserDal>(new ThrowingUserDal());
            });
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/auth/salt?userName=admin");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var envelopeRequestId = body.RootElement.GetProperty("requestId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(envelopeRequestId));

        var errorLog = sink.Records.FirstOrDefault(record =>
            record.Level == LogLevel.Error && record.Exception is InvalidOperationException);
        Assert.NotNull(errorLog);
        Assert.Contains(envelopeRequestId, errorLog!.Message);
        Assert.NotNull(errorLog.Exception);
    }

    private sealed class ThrowingUserDal : IUserDal
    {
        public Task<UserAccount?> ValidateCredentialsAsync(string userName, string passwordHash, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("synthetic dal failure");

        public Task<string?> GetPasswordSaltAsync(string userName, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("synthetic dal failure");

        public Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("synthetic dal failure");

        public Task AssignRolesAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("synthetic dal failure");
    }

    private sealed record CapturedLog(LogLevel Level, string Message, Exception? Exception);

    private sealed class CapturingLogSink : ILoggerProvider
    {
        public ConcurrentBag<CapturedLog> Records { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);
        public void Dispose() { }

        private sealed class CapturingLogger(CapturingLogSink sink) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                sink.Records.Add(new CapturedLog(logLevel, formatter(state, exception), exception));
            }

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();
                public void Dispose() { }
            }
        }
    }
}