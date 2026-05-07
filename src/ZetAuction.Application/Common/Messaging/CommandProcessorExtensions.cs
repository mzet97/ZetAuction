using Paramore.Brighter;

namespace ZetAuction.Application.Common.Messaging;

public static class CommandProcessorExtensions
{
    public static async Task<TResult> SendWithResultAsync<TResult>(
        this IAmACommandProcessor processor,
        ResultCommand<TResult> command,
        CancellationToken cancellationToken = default)
    {
        await processor.SendAsync((dynamic)command, cancellationToken: cancellationToken);
        return command.Result!;
    }
}
