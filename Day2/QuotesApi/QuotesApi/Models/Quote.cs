using System.Text.Json.Serialization;

namespace QuotesApi.Models;

public sealed class Quote
{
    private Quote()
    {
    }

    [JsonConstructor]
    private Quote(int id, string author, string text, DateTime createdAtUtc, bool isDeleted)
    {
        Id = id;
        Author = author;
        Text = text;
        CreatedAtUtc = createdAtUtc;
        IsDeleted = isDeleted;
    }

    private Quote(string author, string text)
    {
        Author = author;
        Text = text;
    }

    public int Id { get; private set; }
    public string Author { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public bool IsDeleted { get; private set; }

    public static (Quote? Quote, string? Error) Create(string? author, string? text)
    {
        var trimmedAuthor = author?.Trim() ?? string.Empty;
        var trimmedText = text?.Trim() ?? string.Empty;

        if (trimmedAuthor.Length is < 1 or > 200)
        {
            return (null, "Author must be between 1 and 200 characters.");
        }

        if (trimmedText.Length is < 1 or > 1000)
        {
            return (null, "Text must be between 1 and 1000 characters.");
        }

        return (new Quote(trimmedAuthor, trimmedText), null);
    }

    internal void SetCreatedAtUtc(DateTime createdAtUtc)
    {
        CreatedAtUtc = createdAtUtc;
    }

    public void Delete()
    {
        IsDeleted = true;
    }
}
