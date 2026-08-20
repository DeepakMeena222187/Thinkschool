namespace QuotesApi.Models;

public sealed class EventLog
{
    public int Id { get; set; }
    public string EventType { get; set; } = "";
    public int UserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string Payload { get; set; } = "";
}
