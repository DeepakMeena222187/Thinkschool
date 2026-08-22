# CQRS-Lite: Separate Command and Query for Quotes

Same `QuotesApi` app as [Day11/Task2-FixSlowEndpoint/QuotesApi](../../../Day11/Task2-FixSlowEndpoint/QuotesApi), copied unchanged (minus `bin`/`obj`) and extended with a MediatR-based command/query split for quotes. `EventLog` and its endpoints (`SlowEndpointExtensions.cs`, `FastEndpointExtensions.cs`) are untouched. The existing non-CQRS `/api/quotes` endpoints in [Extensions/QuoteEndpointExtensions.cs](Extensions/QuoteEndpointExtensions.cs) are also untouched — the new `/api/cqrs/quotes` endpoints are additive, side-by-side for comparison.

## Files

| File | What it does |
|---|---|
| [Features/Quotes/CreateQuoteCommand.cs](Features/Quotes/CreateQuoteCommand.cs) | New — `CreateQuoteCommand` + `CreateQuoteCommandHandler` (write side) |
| [Features/Quotes/GetQuoteListQuery.cs](Features/Quotes/GetQuoteListQuery.cs) | New — `GetQuoteListQuery` + `GetQuoteListQueryHandler` + `QuoteListItemDto` (read side) |
| [Extensions/CqrsQuoteEndpointExtensions.cs](Extensions/CqrsQuoteEndpointExtensions.cs) | New — `POST /api/cqrs/quotes` and `GET /api/cqrs/quotes` |
| [Program.cs](Program.cs) | Two lines added: `AddMediatR(...)` registration and `app.MapCqrsQuoteEndpoints();` |
| [QuotesApi.csproj](QuotesApi.csproj) | `MediatR` 14.2.0 added via `dotnet add package` |

## The command handler (write side)

```csharp
public sealed record CreateQuoteCommand(string Author, string Text, int OwnerId) : IRequest<int>;

public sealed class CreateQuoteCommandHandler(QuotesDbContext db, IClock clock) : IRequestHandler<CreateQuoteCommand, int>
{
    public async Task<int> Handle(CreateQuoteCommand request, CancellationToken cancellationToken)
    {
        var quote = Quote.Create(request.Author, request.Text, request.OwnerId, clock.UtcNow.UtcDateTime);

        db.Quotes.Add(quote);
        await db.SaveChangesAsync(cancellationToken);

        return quote.Id;
    }
}
```

The handler does exactly one thing: build a valid `Quote` through the model's own `Quote.Create(...)` factory (the same validated constructor path `QuoteEndpointExtensions` already used — no validation logic duplicated here), add it to the tracked `QuotesDbContext`, save, and hand back the generated `Id`. It never has to know how a quote will later be displayed.

## The query / read model (read side)

```csharp
public sealed record QuoteListItemDto(int Id, string Author, string Text, DateTime CreatedAtUtc, bool IsOwnedByCurrentUser);

public sealed record GetQuoteListQuery(int CurrentUserId) : IRequest<List<QuoteListItemDto>>;

public sealed class GetQuoteListQueryHandler(QuotesDbContext db) : IRequestHandler<GetQuoteListQuery, List<QuoteListItemDto>>
{
    public Task<List<QuoteListItemDto>> Handle(GetQuoteListQuery request, CancellationToken cancellationToken)
    {
        return db.Quotes
            .AsNoTracking()
            .OrderByDescending(q => q.CreatedAtUtc)
            .Select(q => new QuoteListItemDto(
                q.Id,
                q.Author,
                q.Text,
                q.CreatedAtUtc,
                q.OwnerId == request.CurrentUserId))
            .ToListAsync(cancellationToken);
    }
}
```

Two lessons from Day10 ([EF Core query translation and projections](../../../Day10/Task2-QueryTranslation/QuotesApi/README.md), [change tracker benchmark](../../../Day10/Task1-ChangeTrackerBenchmark/QuotesApi/README.md)) are applied directly: `AsNoTracking()` because this is a read-only round trip with nothing to update, and `.Select(...)` projecting straight into `QuoteListItemDto` so EF Core translates the whole shape — including the `OwnerId == CurrentUserId` comparison — into the `SELECT` list in SQL, rather than pulling full `Quote` entities into memory and reshaping them in C#. `IsOwnedByCurrentUser` exists only in the read model; `Quote` itself has no such concept.

## What got simpler

Splitting the two sides means the write path only has to validate and persist `Quote`'s own fields through its one factory method, and the read path never touches `Quote`'s validation logic at all — it can freely reshape, denormalize, or add derived fields like `IsOwnedByCurrentUser` without the write model ever needing to know or care.

## Endpoints

| Method | Route | Sends |
|---|---|---|
| `POST` | `/api/cqrs/quotes` | `CreateQuoteCommand` (bound directly from the JSON body: `author`, `text`, `ownerId`) |
| `GET` | `/api/cqrs/quotes` | `GetQuoteListQuery` (`CurrentUserId` from the bearer token's claims if present, else the `currentUserId` query parameter) |

## API evidence (TODO — run locally against Azure SQL and paste real output)

Not yet run. Fill in after starting the app (`dotnet run`, same `QuotesApiDay7` Azure SQL database as Day11) — do not fabricate these.

**Create a quote:**

```
TODO: curl -X POST http://localhost:<port>/api/cqrs/quotes \
  -H "Content-Type: application/json" \
  -d '{"author": "TODO", "text": "TODO", "ownerId": TODO}'

TODO: paste the actual 201 response body (new Id) here
```

**List quotes as one user, showing the denormalized shape:**

```
TODO: curl "http://localhost:<port>/api/cqrs/quotes?currentUserId=<userIdA>"

TODO: paste the actual response here — an array of QuoteListItemDto
```

**List the same quotes as a different user, to show `IsOwnedByCurrentUser` flipping correctly:**

```
TODO: curl "http://localhost:<port>/api/cqrs/quotes?currentUserId=<userIdB>"

TODO: paste the actual response here and confirm IsOwnedByCurrentUser differs
      from the previous call for quotes owned by userIdA vs userIdB
```

## What I learned

TODO — fill in after running the evidence above (e.g. how much of the "CQRS" split here was really just "give the read side its own DTO and skip tracking," versus anything MediatR itself added).

## What would break this

TODO — e.g. `OwnerId` is taken directly from the request body with no check against the authenticated caller, so nothing stops one user from creating a quote stamped with someone else's `OwnerId`; and `GetQuoteListQuery`'s `currentUserId` query-parameter fallback means anyone who omits a bearer token can ask "is this owned by user N?" for any N.
