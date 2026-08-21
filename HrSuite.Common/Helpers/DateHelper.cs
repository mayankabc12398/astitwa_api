namespace HrSuite.Common.Helpers;

public static class DateHelper
{
    /// <summary>Whole years elapsed, calendar-correct (no 365.25 fudge).</summary>
    public static int Age(DateTime dob, DateTime? asOf = null)
    {
        var on = (asOf ?? DateTime.UtcNow).Date;
        var age = on.Year - dob.Year;
        if (dob.Date > on.AddYears(-age)) age--;
        return age < 0 ? 0 : age;
    }

    /// <summary>Inclusive day span. Returns 0 when the range is inverted.</summary>
    public static decimal InclusiveDays(DateTime from, DateTime to)
    {
        if (to.Date < from.Date) return 0m;
        return (decimal)(to.Date - from.Date).TotalDays + 1m;
    }

    public static string IsoDate(DateTime value) => value.ToString("yyyy-MM-dd");
}
