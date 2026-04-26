// PagedResult.cs — the standard response wrapper for all paged queries.
// INTERVIEW: Every paged endpoint returns PagedResult<T> — clients always get
// consistent metadata (total count, current page, total pages) regardless of
// which endpoint they're calling. Makes frontend pagination trivially simple.

namespace NexaStore.Application.Common.Models;

public class PagedResult<T>
{
    // The actual data for this page
    public IReadOnlyList<T> Items { get; set; }

    // Total rows matching the filter across ALL pages — needed for UI pagination
    public int TotalCount { get; set; }

    // Which page was returned (1-based)
    public int PageNumber { get; set; }

    // How many items per page
    public int PageSize { get; set; }

    // INTERVIEW: Derived properties — calculated, not stored.
    // TotalPages lets the frontend know when to disable "Next" button.
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

    // True when there is a previous page to navigate to
    public bool HasPreviousPage => PageNumber > 1;

    // True when there are more pages beyond the current one
    public bool HasNextPage => PageNumber < TotalPages;

    public PagedResult(IReadOnlyList<T> items, int totalCount, int pageNumber, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        PageNumber = pageNumber;
        PageSize = pageSize;
    }
}
