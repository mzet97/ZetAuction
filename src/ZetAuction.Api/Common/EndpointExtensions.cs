using System.Reflection;

namespace ZetAuction.Api.Common;

public static class EndpointExtensions
{
    public static IEndpointRouteBuilder MapEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var endpointTypes = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } &&
                        t.GetInterfaces().Any(i => i.Name == nameof(IEndpoint)));

        foreach (var type in endpointTypes)
        {
            var mapMethod = type.GetMethod("Map", BindingFlags.Public | BindingFlags.Static, null, [typeof(IEndpointRouteBuilder)], null);
            if (mapMethod == null)
            {
                var interfaceType = type.GetInterfaces().FirstOrDefault(i => i.Name == nameof(IEndpoint));
                if (interfaceType != null)
                {
                    mapMethod = interfaceType.GetMethod("Map", BindingFlags.Public | BindingFlags.Static);
                }
            }
            mapMethod?.Invoke(null, [endpoints]);
        }

        return endpoints;
    }
}
