using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZetAuction.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBrighterOutbox : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// Creates the table that backs <c>Paramore.Brighter.Outbox.PostgreSql</c>.
        /// Schema is reproduced from <c>PostgreSqlOutboxBulder.GetDDL</c> in
        /// Brighter v9.9.13 (BIGSERIAL key, UUID MessageId, JSON HeaderBag/Body
        /// stored as TEXT). The table replaces the previous custom
        /// <c>OutboxMessages</c> table — Brighter now owns both the schema and
        /// the read/write paths via <c>PostgreSqlOutbox</c>.
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE outbox_messages
                (
                    Id BIGSERIAL PRIMARY KEY,
                    MessageId UUID UNIQUE NOT NULL,
                    Topic VARCHAR(255) NULL,
                    MessageType VARCHAR(32) NULL,
                    Timestamp timestamptz NULL,
                    CorrelationId uuid NULL,
                    ReplyTo VARCHAR(255) NULL,
                    ContentType VARCHAR(128) NULL,
                    Dispatched timestamptz NULL,
                    HeaderBag TEXT NULL,
                    Body TEXT NULL
                );
            ");

            // Hot path for the dispatcher: ""SELECT ... WHERE Dispatched IS NULL
            // ORDER BY Timestamp"". A partial index keeps the index small —
            // dispatched rows fall out of the working set as soon as they are
            // marked, so the index only ever covers in-flight messages.
            migrationBuilder.Sql(@"
                CREATE INDEX IX_outbox_messages_Outstanding_Timestamp
                    ON outbox_messages (Timestamp)
                    WHERE Dispatched IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_outbox_messages_Outstanding_Timestamp;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS outbox_messages;");
        }
    }
}
