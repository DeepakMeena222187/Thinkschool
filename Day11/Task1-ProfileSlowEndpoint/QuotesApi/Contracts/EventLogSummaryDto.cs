namespace QuotesApi.Contracts;

public sealed class EventLogSummaryDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
