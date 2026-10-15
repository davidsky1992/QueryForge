namespace QueryForge.Application.Common;

public record PagedRequest(int PageNumber = 1, int PageSize = 20)
{
    public const int MaxPageSize = 100;

    public int NormalizedPageNumber => PageNumber < 1 ? 1 : PageNumber;

    public int NormalizedPageSize => PageSize switch
    {
        < 1 => 20,
        > MaxPageSize => MaxPageSize,
        _ => PageSize
    };

    public int Skip => (NormalizedPageNumber - 1) * NormalizedPageSize;
}
