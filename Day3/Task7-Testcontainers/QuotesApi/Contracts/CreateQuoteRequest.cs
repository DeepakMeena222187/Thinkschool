using System.ComponentModel.DataAnnotations;

namespace QuotesApi.Contracts;

public sealed record CreateQuoteRequest(
    [property: Required, StringLength(100, MinimumLength = 1)] string Author,
    [property: Required, StringLength(1000, MinimumLength = 1)] string Text);
