using Dapper;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Modularity;
using HrSuite.Core.Services;
using HrSuite.Infrastructure.Data;
using HrSuite.Infrastructure.Extensibility;
using HrSuite.Infrastructure.Identity;
using HrSuite.Infrastructure.Modularity;
using HrSuite.Infrastructure.Notifications;
using HrSuite.Infrastructure.Repositories;
using HrSuite.Core.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HrSuite.Infrastructure;

public static class InfrastructureRegistration
{
    public static IServiceCollection AddHrSuiteInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // MySQL columns are snake_case; entities are PascalCase. Map once, globally.
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.AddSingleton<IDbConnectionFactory, MySqlConnectionFactory>();
        services.AddMemoryCache();

        // Identity
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddScoped<IUserAuthRepository, UserAuthRepository>();
        services.AddScoped<IAuthService, AuthService>();

        // Tenancy and licensing
        services.AddScoped<ITenantModuleService, TenantModuleService>();
        services.AddScoped<ITenantIntegrationService, TenantIntegrationService>();

        // Layer 1 HR: repositories and the services that hold the base rules.
        services.AddScoped<IDepartmentRepository, DepartmentRepository>();
        services.AddScoped<IDesignationRepository, DesignationRepository>();
        services.AddScoped<IEmployeeRepository, EmployeeRepository>();
        services.AddScoped<ILeaveRepository, LeaveRepository>();
        services.AddScoped<IDocumentRepository, DocumentRepository>();

        // Screen configuration the tenant edits at run time: print templates, and the
        // fields the product never compiled in. Layer 1 owns the CRUD; what the rows mean
        // is the tenant's decision, which is the same split cfg_field_rule already has.
        services.AddScoped<IPrintTemplateRepository, PrintTemplateRepository>();
        services.AddScoped<ICustomFieldRepository, CustomFieldRepository>();

        services.AddScoped<HookInvoker>();
        services.AddScoped<IDepartmentService, DepartmentService>();
        services.AddScoped<IDesignationService, DesignationService>();
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddScoped<ILeaveService, LeaveService>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IPrintTemplateService, PrintTemplateService>();
        services.AddScoped<ICustomFieldService, CustomFieldService>();

        // Layer 4 boundary. Channels themselves are contributed by integration assemblies.
        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();

        // Layer 5 boundary. These are null objects so base code can call the hook engine
        // unconditionally. A deployed extension assembly registers over them at startup,
        // because the last registration wins.
        services.AddScoped<IHookEngine, NullHookEngine>();
        services.AddScoped<INamedQueryRunner, NullNamedQueryRunner>();

        return services;
    }
}
