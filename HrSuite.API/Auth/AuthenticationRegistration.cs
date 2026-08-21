using System.Text;
using System.Text.Json;
using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace HrSuite.API.Auth;

public static class AuthenticationRegistration
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IServiceCollection AddHrSuiteAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        services.AddHttpContextAccessor();
        services.AddSingleton<IAuthTokenIssuer, JwtTokenIssuer>();

        // Scoped: one tenant context per request, built from that request's validated token.
        services.AddScoped<ITenantContext, HttpTenantContext>();

        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

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
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(jwt.SigningKey)
                            ? new string('0', 32)   // fails validation loudly rather than starting insecure
                            : jwt.SigningKey)),
                    ClockSkew = TimeSpan.FromSeconds(30)
                };

                // Keep the envelope consistent even for challenges raised by the framework.
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = async ctx =>
                    {
                        ctx.HandleResponse();
                        await WriteEnvelope(ctx.Response, StatusCodes.Status401Unauthorized,
                            ErrorCode.Unauthorized, "Sign in to continue.");
                    },
                    OnForbidden = ctx => WriteEnvelope(ctx.Response, StatusCodes.Status403Forbidden,
                        ErrorCode.Forbidden, "You do not have access to this resource.")
                };
            });

        // Authentication is the default, not the exception: an endpoint is protected unless it
        // opts out with [AllowAnonymous]. Forgetting [Authorize] on a new controller cannot
        // silently expose it.
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        return services;
    }

    private static async Task WriteEnvelope(HttpResponse response, int status, string code, string message)
    {
        if (response.HasStarted) return;

        response.StatusCode = status;
        response.ContentType = "application/json";

        var body = ApiResponse.Fail(code, message, response.HttpContext.TraceIdentifier);
        await response.WriteAsync(JsonSerializer.Serialize(body, Json));
    }
}
