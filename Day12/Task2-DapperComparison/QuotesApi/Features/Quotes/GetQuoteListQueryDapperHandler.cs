using Dapper;
using MediatR;
using Microsoft.Data.SqlClient;

namespace QuotesApi.Features.Quotes;

// MediatR resolves exactly one handler per request type, so this can't just be a
// second IRequestHandler<GetQuoteListQuery, ...> registration alongside the EF Core
// one in GetQuoteListQuery.cs - whichever handler DI resolves last would silently
// shadow the other. GetQuoteListQueryDapper is a distinct request type with an
// identical shape (same CurrentUserId in, same List<QuoteListItemDto> out) so both
// paths are reachable side by side and the comparison stays apples-to-apples.
public sealed record GetQuoteListQueryDapper(int CurrentUserId) : IRequest<List<QuoteListItemDto>>;

public sealed class GetQuoteListQueryDapperHandler(IConfiguration configuration) : IRequestHandler<GetQuoteListQueryDapper, List<QuoteListItemDto>>
{
    // The CASE must be cast to BIT, not left as the bare-literal INT that
    // `THEN 1 ELSE 0` would otherwise produce: QuoteListItemDto.IsOwnedByCurrentUser
    // is bool, and Dapper's constructor-based materialization for records requires
    // an exact type match per parameter - an INT column here throws
    // InvalidOperationException at query time ("no constructor found") instead of
    // silently coercing to bool.
    private const string Sql = """
        SELECT Id, Author, Text, CreatedAtUtc,
               CAST(CASE WHEN OwnerId = @CurrentUserId THEN 1 ELSE 0 END AS BIT) AS IsOwnedByCurrentUser
        FROM Quotes
        ORDER BY CreatedAtUtc DESC
        """;

    public async Task<List<QuoteListItemDto>> Handle(GetQuoteListQueryDapper request, CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("Quotes")
            ?? throw new InvalidOperationException("Missing required configuration value: ConnectionStrings:Quotes.");

        using var connection = new SqlConnection(connectionString);

        var quotes = await connection.QueryAsync<QuoteListItemDto>(Sql, new { CurrentUserId = request.CurrentUserId });

        return quotes.AsList();
    }
}
