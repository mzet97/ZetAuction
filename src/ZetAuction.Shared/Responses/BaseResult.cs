namespace ZetAuction.Shared.Responses;

public class BaseResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }

    public BaseResult() { }

    public BaseResult(bool success, string? message = null)
    {
        Success = success;
        Message = message;
    }

    public static BaseResult Ok(string? message = null) => new(true, message);

    public static BaseResult Fail(string? message = null) => new(false, message);
}

public class BaseResultList<T>
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public List<T>? Data { get; set; }
    public PagedResult? PagedResult { get; set; }

    public BaseResultList() { }

    public BaseResultList(List<T>? data, PagedResult? pagedResult, bool success = true, string? message = null)
    {
        Data = data;
        PagedResult = pagedResult;
        Success = success;
        Message = message;
    }

    public static BaseResultList<T> Ok(List<T>? data, PagedResult? pagedResult, string? message = null)
        => new(data, pagedResult, true, message);

    public static BaseResultList<T> Fail(string? message = null)
        => new(null, null, false, message);
}

public class BaseResult<T> : BaseResult
{
    public T? Data { get; set; }

    public BaseResult() { }

    public BaseResult(bool success, string? message = null, T? data = default)
        : base(success, message)
    {
        Data = data;
    }

    public static BaseResult<T> Ok(T? data, string? message = null) => new(true, message, data);

    public new static BaseResult<T> Fail(string? message = null) => new(false, message);
}
