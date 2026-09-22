using TigerRAG.Api.Filters;
using TigerRAG.Api.Hubs;
using TigerRAG.Api.Middleware;
using TigerRAG.Api.Security;
using TigerRAG.Api.Common;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using TigerRAG.Infrastructure.Logging;

namespace TigerRAG.Api;

public static class ApiComposition
{
    public static IServiceCollection AddTigerRagApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var signingKey = configuration["Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
        {
            throw new InvalidOperationException("Jwt:SigningKey must contain at least 32 bytes.");
        }

        services.AddProblemDetails();
        services.AddHealthChecks();
        services.AddSignalR();
        // IExceptionHandler 由 UseExceptionHandler() 自动发现并按注册顺序调用；
        // 返回 false 让默认 ProblemDetails 写入继续走，最终被 ApiResponseMiddleware 包成 ApiResponse。
        services.AddExceptionHandler<LoggingExceptionHandler>();
        services.AddHostedService<ApiLogFlusherService>();
        services
            .AddControllers(options =>
            {
                options.Filters.Add<ApiResponseFilter>();
                options.Filters.Add<MenuEndpointAuthFilter>();
            })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            });
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                var clockSkewSeconds = configuration.GetValue<int>("Jwt:ClockSkewSeconds", 30);
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = configuration["Jwt:Issuer"] ?? "TigerRAG",
                    ValidAudience = configuration["Jwt:Audience"] ?? "TigerRAG",
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                    // 默认 300s 太宽，对称密钥 + NTP 同步环境收紧到 30s 即足以吸收漂移。
                    ClockSkew = TimeSpan.FromSeconds(clockSkewSeconds)
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = JwtRevocationValidator.OnTokenValidated
                };
            });
        // 授权判定统一走 role claim（[Authorize(Roles = ...)]），无需注册 permission 策略。

        // 反向代理场景：必须显式配置 KnownIPNetworks/ForwardedFor 信任范围，
        // 否则任意客户端可伪造 X-Forwarded-* 头绕过 TLS 标记。仅读 X-Forwarded-Proto，
        // 项目目前不依赖客户端真实 IP，不读 X-Forwarded-For。
        services.Configure<ForwardedHeadersOptions>(configuration.GetSection("ReverseProxy"));
        services.PostConfigure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
            // 配置节只填 KnownIPNetworks（CIDR）；已废弃的 KnownNetworks 不读，避免新旧两边漂移。
        });
        return services;
    }

    public static WebApplication MapTigerRagApi(this WebApplication app)
    {
        // 必须最先注册：让 Request.IsHttps 在反向代理后能正确反映 X-Forwarded-Proto。
        app.UseForwardedHeaders();
        app.UseMiddleware<RequestIdMiddleware>();
        app.UseExceptionHandler();
        app.UseMiddleware<ApiResponseMiddleware>();

        app.MapHealthChecks("/health/live");
        app.MapHealthChecks("/health/ready");
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapHub<ChatHub>("/hubs/chat");

        return app;
    }
}
