using TigerRAG.Api.Filters;
using TigerRAG.Api.Hubs;
using TigerRAG.Api.Middleware;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using TigerRAG.Application.Security;
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
            .AddControllers(options => options.Filters.Add<ApiResponseFilter>())
            .AddJsonOptions(options =>
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never);
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = configuration["Jwt:Issuer"] ?? "TigerRAG",
                    ValidAudience = configuration["Jwt:Audience"] ?? "TigerRAG",
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey))
                };
            });
        services.AddAuthorization(options =>
        {
            foreach (var permission in SystemPermissions.All)
            {
                options.AddPolicy(permission, policy =>
                    policy.RequireRole(RolePermissionMap.RolesFor(permission)));
            }
        });
        return services;
    }

    public static WebApplication MapTigerRagApi(this WebApplication app)
    {
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
