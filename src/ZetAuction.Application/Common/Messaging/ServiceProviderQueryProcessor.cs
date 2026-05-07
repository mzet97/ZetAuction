using Microsoft.Extensions.DependencyInjection;
using Paramore.Darker;

namespace ZetAuction.Application.Common.Messaging;

public sealed class ServiceProviderQueryProcessor : IQueryProcessor
{
    private readonly IServiceProvider _serviceProvider;

    public ServiceProviderQueryProcessor(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public TResult Execute<TResult>(IQuery<TResult> query)
    {
        return ExecuteAsync(query).GetAwaiter().GetResult();
    }

    public async Task<TResult> ExecuteAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
    {
        var handlerType = typeof(IQueryHandler<,>).MakeGenericType(query.GetType(), typeof(TResult));
        var handler = _serviceProvider.GetService(handlerType);

        if (handler is null)
        {
            throw new InvalidOperationException($"No handler registered for query type {query.GetType().Name}");
        }

        var executeAsyncMethod = handlerType.GetMethod("ExecuteAsync");
        if (executeAsyncMethod == null)
        {
            throw new InvalidOperationException($"Handler for query type {query.GetType().Name} does not implement ExecuteAsync.");
        }

        var task = (Task)executeAsyncMethod.Invoke(handler, new object[] { query, cancellationToken })!;
        await task.ConfigureAwait(false);
        var resultProperty = task.GetType().GetProperty("Result");
        if (resultProperty == null)
        {
            throw new InvalidOperationException("ExecuteAsync did not return a result.");
        }
        return (TResult)resultProperty.GetValue(task)!;
    }
}
