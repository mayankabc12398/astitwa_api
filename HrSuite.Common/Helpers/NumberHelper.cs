namespace HrSuite.Common.Helpers;

public static class NumberHelper
{
    public static decimal Round(decimal value, int digits = 2)
        => Math.Round(value, Math.Clamp(digits, 0, 10), MidpointRounding.AwayFromZero);

    public static bool IsEmpty(object? value) => value switch
    {
        null            => true,
        string s        => string.IsNullOrWhiteSpace(s),
        System.Collections.ICollection c => c.Count == 0,
        _               => false
    };
}
