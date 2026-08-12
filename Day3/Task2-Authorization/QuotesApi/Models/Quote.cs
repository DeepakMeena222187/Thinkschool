namespace QuotesApi.Models;
public sealed class Quote
{
    public int Id { get; set; }
    public string Author { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public int OwnerId { get; set; }
}
