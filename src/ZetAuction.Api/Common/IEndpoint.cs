namespace ZetAuction.Api.Common;

public interface IEndpoint
{
    static abstract void Map(IEndpointRouteBuilder endpoints);
}
