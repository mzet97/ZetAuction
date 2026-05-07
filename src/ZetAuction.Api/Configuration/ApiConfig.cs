using Microsoft.AspNetCore.Diagnostics;
using Paramore.Brighter.Extensions.DependencyInjection;
using Paramore.Darker;
using ZetAuction.Api.Configuration;
using ZetAuction.Api.Middleware;
using ZetAuction.Api.Services;
using ZetAuction.Application.Auctions.Queries;
using ZetAuction.Application.Bids.Queries;
using ZetAuction.Application.Common.Messaging;
using ZetAuction.Application.Users.Queries;
using ZetAuction.Domain.Repositories;
using ZetAuction.Infrastructure;
using ZetAuction.Infrastructure.Persistence.Repositories;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Api.Configuration;

public static class ApiConfig
{
    public static IServiceCollection AddApiConfig(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddInfrastructure(configuration);

        // RFC 7807 ProblemDetails. The exception handler maps domain
        // exceptions to stable type URIs the client can program against,
        // and the customizer enriches every problem response with the
        // correlation id surfaced via traceId.
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Instance = context.HttpContext.Request.Path;
                context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
                if (System.Diagnostics.Activity.Current is { } activity)
                {
                    context.ProblemDetails.Extensions["activityId"] = activity.Id;
                }
            };
        });

        services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

        services.AddBrighter(options =>
        {
            options.HandlerLifetime = ServiceLifetime.Scoped;
        })
        .AutoFromAssemblies(new[] { typeof(ZetAuction.Application.Auth.Commands.LoginCommand).Assembly });

        services.AddScoped<IAuctionReadRepository, AuctionReadRepository>();
        services.AddScoped<IUserReadRepository, UserReadRepository>();
        services.AddScoped<IBidReadRepository, BidReadRepository>();

        services.AddScoped<IQueryHandler<GetUserByIdQuery, BaseResult<ZetAuction.Application.Users.ViewModels.UserViewModel>>, GetUserByIdQueryHandler>();
        services.AddScoped<IQueryHandler<GetAllUsersQuery, BaseResultList<ZetAuction.Application.Users.ViewModels.UserViewModel>>, GetAllUsersQueryHandler>();
        services.AddScoped<IQueryHandler<GetAuctionsQuery, BaseResultList<ZetAuction.Application.Auctions.ViewModels.AuctionViewModel>>, GetAuctionsQueryHandler>();
        services.AddScoped<IQueryHandler<GetAuctionByIdQuery, BaseResult<ZetAuction.Application.Auctions.ViewModels.AuctionViewModel>>, GetAuctionByIdQueryHandler>();
        services.AddScoped<IQueryHandler<GetActiveAuctionsQuery, BaseResultList<ZetAuction.Application.Auctions.ViewModels.AuctionViewModel>>, GetActiveAuctionsQueryHandler>();
        services.AddScoped<IQueryHandler<GetAuctionsByStatusQuery, BaseResultList<ZetAuction.Application.Auctions.ViewModels.AuctionViewModel>>, GetAuctionsByStatusQueryHandler>();
        services.AddScoped<IQueryHandler<GetBidHistoryQuery, BaseResultList<ZetAuction.Application.Bids.ViewModels.BidViewModel>>, GetBidHistoryQueryHandler>();
        services.AddScoped<IQueryHandler<GetHighestBidQuery, BaseResult<ZetAuction.Application.Bids.ViewModels.BidViewModel>>, GetHighestBidQueryHandler>();

        services.AddScoped<IQueryProcessor, ServiceProviderQueryProcessor>();
        services.AddHostedService<AuctionFinalizationWorker>();
        services.AddHostedService<BrighterOutboxDispatcherWorker>();

        services.AddAuthConfig(configuration);

        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        });

        services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
        {
            options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        });

        services.AddOpenApi();

        services.AddHealthCheckConfiguration(configuration);

        services.AddObservability(configuration);

        services.AddRateLimitingConfig();

        return services;
    }
}
