namespace ZetAuction.Shared.Responses;

public class PagedResult
{
    public int CurrentPage { get; set; }

    public int PageCount { get; set; }

    public int PageSize { get; set; }

    public int RowCount { get; set; }

    public int FirstRowOnPage { get; set; }

    public int LastRowOnPage { get; set; }

    public static int Skip(int currentPage, int pageSize)
    {
        return (currentPage - 1) * pageSize;
    }

    public static PagedResult Create(int currentPage, int pageSize, int rowCount)
    {
        var pageCount = (int)Math.Ceiling((double)rowCount / pageSize);
        return new PagedResult
        {
            CurrentPage = currentPage,
            PageSize = pageSize,
            RowCount = rowCount,
            PageCount = pageCount,
            FirstRowOnPage = rowCount > 0 ? (currentPage - 1) * pageSize + 1 : 0,
            LastRowOnPage = Math.Min(currentPage * pageSize, rowCount)
        };
    }

    public int Skip()
    {
        return (CurrentPage - 1) * PageSize;
    }
}
