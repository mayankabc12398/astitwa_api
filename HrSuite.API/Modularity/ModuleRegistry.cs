using HrSuite.Core.Modularity;

namespace HrSuite.API.Modularity;

/// <summary>Immutable record of what the host discovered at startup.</summary>
public sealed class ModuleRegistry : IModuleRegistry
{
    private readonly HashSet<string> _keys;

    public ModuleRegistry(IReadOnlyList<ModuleDescriptor> all, IReadOnlyList<MenuEntry> menu)
    {
        All = all;
        AllMenuEntries = menu.OrderBy(m => m.SeqNo)
                             .ThenBy(m => m.Label, StringComparer.OrdinalIgnoreCase)
                             .ToList();
        _keys = all.Where(m => m.ModuleKey is not null)
                   .Select(m => m.ModuleKey!)
                   .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ModuleDescriptor> All { get; }
    public IReadOnlyList<MenuEntry> AllMenuEntries { get; }
    public bool IsRegistered(string moduleKey) => _keys.Contains(moduleKey);
}
