using HrSuite.Core.Modularity;

namespace HrSuite.API.Modularity;

public static class PluginRegistrationExtensions
{
    /// <summary>
    /// Discovers every IPluginModule under plugins\, lets each one register its own services,
    /// and adds its assembly as an MVC application part so its controllers are routed.
    /// The host never names a module type.
    /// </summary>
    public static IServiceCollection AddDiscoveredModules(
        this IServiceCollection services,
        IConfiguration configuration,
        ILogger logger,
        IMvcBuilder mvc)
    {
        var plugins = PluginLoader.Discover(AppContext.BaseDirectory, logger);

        var descriptors = new List<ModuleDescriptor>();
        var menu = new List<MenuEntry>(BaseMenu.Entries);

        foreach (var plugin in plugins)
        {
            plugin.Module.Register(services, configuration);
            mvc.AddApplicationPart(plugin.Assembly);

            descriptors.Add(new ModuleDescriptor(
                plugin.Module.ModuleKey,
                plugin.Module.DisplayName,
                plugin.Module.Layer,
                plugin.Assembly.GetName().Name ?? "unknown",
                plugin.Module.SeqNo));

            menu.AddRange(plugin.Module.MenuEntries);
        }

        services.AddSingleton<IModuleRegistry>(new ModuleRegistry(descriptors, menu));
        return services;
    }
}

/// <summary>Menu contributed by base code itself. Add-on entries are appended by their own modules.</summary>
public static class BaseMenu
{
    public static readonly IReadOnlyList<MenuEntry> Entries = new[]
    {
        new MenuEntry("hr.employee",     "Employees",        "/hr/employee",       "users",     10,  null, "hr.employee.view"),
        new MenuEntry("hr.department",   "Departments",      "/hr/department",     "sitemap",   20,  null, "hr.department.view"),
        new MenuEntry("hr.designation",  "Designations",     "/hr/designation",    "badge",     30,  null, "hr.designation.view"),
        new MenuEntry("hr.leave",        "Leave Requests",   "/hr/leave",          "calendar",  40,  null, "hr.leave.view"),
        new MenuEntry("hr.leaveApproval","Leave Approvals",  "/hr/leave/approval", "check",     50,  null, "hr.leave.approve"),
        new MenuEntry("hr.documents",    "Documents",        "/hr/documents",      "file",      60,  null, "hr.document.view"),
        new MenuEntry("admin.printDesigner","Print Designer","/hr/print-designer", "printer",   800, null, "admin.printTemplate"),
        new MenuEntry("admin.fieldBuilder","Field Builder",  "/hr/field-builder",  "sliders",   810, null, "admin.customField"),
        new MenuEntry("admin.hooks",     "Script Hooks",     "/admin/hooks",       "code",      900, null, "admin.extensions"),
        new MenuEntry("admin.queries",   "Named Queries",    "/admin/queries",     "database",  910, null, "admin.extensions"),
        new MenuEntry("admin.hookLog",   "Hook Log",         "/admin/hook-log",    "list",      920, null, "admin.extensions")
    };
}
