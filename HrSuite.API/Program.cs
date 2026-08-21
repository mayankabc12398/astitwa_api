using HrSuite.API.Auth;
using HrSuite.API.Middleware;
using HrSuite.API.Modularity;
using HrSuite.Common.Results;
using HrSuite.Configuration;
using HrSuite.Infrastructure;
using HrSuite.Infrastructure.Web;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Secrets never live in appsettings. Environment wins over user-secrets wins over file.
builder.Configuration.AddEnvironmentVariables(prefix: "HRSUITE_");

using var startupLoggerFactory = LoggerFactory.Create(b => b.AddConsole());
var startupLogger = startupLoggerFactory.CreateLogger("HrSuite.Startup");

var mvc = builder.Services.AddControllers(options =>
{
    // Paging binds from the query string by type, not by parameter name. See
    // PageRequestModelBinder for what goes wrong otherwise. Inserted first so it wins over
    // the default complex-object binder, and applied host-wide so plugin controllers get it.
    options.ModelBinderProviders.Insert(0, new PageRequestModelBinderProvider());
});

// Model-binding failures use the same envelope as everything else (section 11).
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var fields = context.ModelState
            .Where(kv => kv.Value?.Errors.Count > 0)
            .Select(kv => new ApiFieldError
            {
                Field = kv.Key,
                Message = kv.Value!.Errors[0].ErrorMessage
            })
            .ToList();

        return new BadRequestObjectResult(ApiResponse.Fail(
            ErrorCode.Validation, "The request could not be accepted.", context.HttpContext.TraceIdentifier, fields));
    };
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHrSuiteAuth(builder.Configuration);
builder.Services.AddHrSuiteInfrastructure(builder.Configuration);
builder.Services.AddHrSuiteConfiguration();

// Layers 3, 4 and 5 are discovered here. No base-code file names any of them.
builder.Services.AddDiscoveredModules(builder.Configuration, startupLogger, mvc);

if (builder.Environment.IsDevelopment())
{
    // Development only. Production is served same-origin behind IIS or a reverse proxy.
    builder.Services.AddCors(o => o.AddPolicy("vite-dev", p => p
        .WithOrigins("http://localhost:5173", "https://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()));
}

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseCors("vite-dev");
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
