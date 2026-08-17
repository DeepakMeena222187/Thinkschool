namespace QuotesApi.Models;
public sealed class Quote
{
    public int Id { get; set; }
    public string Author { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public int OwnerId { get; set; }

    public static Quote Create(string author, string text, int ownerId, DateTime createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(author))
        {
            throw new ArgumentException("Author is required.", nameof(author));
        }

        if (author.Trim().Length > 100)
        {
            throw new ArgumentException("Author must be at most 100 characters.", nameof(author));
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text is required.", nameof(text));
        }

        if (text.Trim().Length > 1000)
        {
            throw new ArgumentException("Text must be at most 1000 characters.", nameof(text));
        }

        if (ownerId <= 0)
        {
            throw new ArgumentException("OwnerId must be greater than zero.", nameof(ownerId));
        }

        return new Quote
        {
            Author = author.Trim(),
            Text = text.Trim(),
            OwnerId = ownerId,
            CreatedAtUtc = createdAtUtc
        };
    }
}
