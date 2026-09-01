namespace QuotesApi.Models;

// One row per successfully processed Service Bus message. The unique index on
// MessageId (see QuotesDbContext) is both the audit record AND the idempotency
// ledger for AuditWorker - there's no separate "have I seen this id" table,
// because for this handler writing the row *is* the work.
public sealed class AuditLogEntry
{
    public int Id { get; set; }
    public string MessageId { get; set; } = "";
    public string EventType { get; set; } = "";
    public int QuoteId { get; set; }
    public string Payload { get; set; } = "";
    public DateTime ProcessedAtUtc { get; set; }
}
