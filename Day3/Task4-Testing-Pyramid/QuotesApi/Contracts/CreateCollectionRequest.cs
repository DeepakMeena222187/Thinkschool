namespace QuotesApi.Contracts;

public sealed record CreateCollectionRequest(string Name, int OwnerId);

public sealed record AddCollectionItemRequest(int QuoteId);
