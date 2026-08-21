namespace HrSuite.Core.Modularity;

/// <summary>A navigation entry contributed by base code or by an add-on.</summary>
public sealed record MenuEntry(
    string Key,
    string Label,
    string Route,
    string? Icon = null,
    int SeqNo = 100,
    string? ModuleKey = null,
    string? RequiredPermission = null);
