using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZetAuction.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessedEvents : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// Consumer-side idempotency table for the Brighter outbox dispatcher.
        /// <c>BrighterOutboxDispatcherWorker</c> tries to claim each
        /// <c>MessageId</c> here via <c>INSERT … ON CONFLICT DO NOTHING</c>
        /// before calling <c>PublishAsync&lt;T&gt;</c>. Two replicas can read
        /// the same outstanding row from <c>outbox_messages</c> (Brighter
        /// v9.9.13 does not lock with <c>FOR UPDATE SKIP LOCKED</c>) — the
        /// PRIMARY KEY on <c>event_id</c> guarantees only one of them
        /// succeeds, and the loser skips dispatch. Keeps the documented
        /// idempotency contract from being a future foot-gun the moment a
        /// handler grows real side effects.
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE processed_events
                (
                    event_id     UUID         PRIMARY KEY,
                    processed_at timestamptz  NOT NULL
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS processed_events;");
        }
    }
}
