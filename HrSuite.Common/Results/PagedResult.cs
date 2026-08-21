namespace HrSuite.Common.Results;

/// <summary>Every list endpoint returns this. There is no unbounded list contract.</summary>
public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public int TotalCount { get; init; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public static PagedResult<T> Create(IReadOnlyList<T> items, int page, int pageSize, int total)
        => new() { Items = items, Page = page, PageSize = pageSize, TotalCount = total };
}

/// <summary>Inbound paging, clamped so a caller cannot request an unbounded page.</summary>
public sealed class PageRequest
{
    public const int MaxPageSize = 200;

    private int _page = 1;
    private int _pageSize = 25;

    public int Page { get => _page; set => _page = value < 1 ? 1 : value; }
    public int PageSize { get => _pageSize; set => _pageSize = value < 1 ? 25 : Math.Min(value, MaxPageSize); }
    public string? Search { get; set; }
    public int Offset => (Page - 1) * PageSize;
}
