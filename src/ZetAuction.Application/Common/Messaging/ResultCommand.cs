using Paramore.Brighter;

namespace ZetAuction.Application.Common.Messaging;

public abstract class ResultCommand<TResult> : Command
{
    // Brighter v9 Command base takes a Guid; v10's Id.Random() helper
    // doesn't exist on this version (see DomainEvent for the same shift).
    protected ResultCommand() : base(Guid.NewGuid())
    {
    }

    public TResult? Result { get; set; }
}
