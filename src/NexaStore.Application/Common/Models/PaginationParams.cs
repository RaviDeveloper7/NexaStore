// PaginationParams.cs — shared input model for all paged queries.
// INTERVIEW: Centralising pagination params means every paged endpoint
// behaves consistently — same defaults, same max page size, same naming.
// Prevents one endpoint doing pageSize=1000 and crushing the DB.

namespace NexaStore.Application.Common.Models;

public class PaginationParams
{
    // Sensible default — first page
    public int PageNumber { get; set; } = 1;

    // Default page size — enough for a typical list view
    private int _pageSize = 10;

    // INTERVIEW: Max page size cap prevents abuse — no client can request
    // 10,000 rows in one call. Server controls the ceiling.
    private const int MaxPageSize = 50;

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value > MaxPageSize ? MaxPageSize : value;
    }

    // Optional search term — applied as a LIKE query in the repository
    public string? SearchTerm { get; set; }

    // Column name to sort by — validated in the handler before passing to repo
    public string? SortBy { get; set; }

    // Default ascending — DESC when true
    public bool IsDescending { get; set; } = false;
}
