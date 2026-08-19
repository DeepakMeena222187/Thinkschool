namespace QuotesApi.Models;

public sealed class Collection
{
    private readonly List<CollectionItem> _items = [];

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int OwnerId { get; private set; }
    public IReadOnlyCollection<CollectionItem> Items => _items.AsReadOnly();

    private Collection()
    {
    }

    public Collection(string name, int ownerId)
    {
        SetName(name);
        OwnerId = ownerId;
    }

    public void Rename(string name)
    {
        SetName(name);
    }

    public void AddItem(int quoteId)
    {
        if (quoteId <= 0)
        {
            throw new InvalidOperationException("QuoteId must be greater than zero.");
        }

        if (_items.Count >= 50)
        {
            throw new InvalidOperationException("A collection can contain at most 50 items.");
        }

        if (_items.Any(item => item.QuoteId == quoteId))
        {
            throw new InvalidOperationException("Duplicate quoteId is not allowed.");
        }

        _items.Add(new CollectionItem(quoteId, DateTime.UtcNow));
    }

    public void RemoveItem(int quoteId)
    {
        var item = _items.FirstOrDefault(x => x.QuoteId == quoteId);
        if (item is null)
        {
            throw new InvalidOperationException("Quote is not in the collection.");
        }

        _items.Remove(item);
    }

    private void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Collection name is required.");
        }

        if (name.Length < 3 || name.Length > 80)
        {
            throw new InvalidOperationException("Collection name must be between 3 and 80 characters.");
        }

        Name = name.Trim();
    }
}

public sealed class CollectionItem
{
    private CollectionItem()
    {
    }

    public CollectionItem(int quoteId, DateTime addedAt)
    {
        QuoteId = quoteId;
        AddedAt = addedAt;
    }

    public int QuoteId { get; private set; }
    public DateTime AddedAt { get; private set; }
}
