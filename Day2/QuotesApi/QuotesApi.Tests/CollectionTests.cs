using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using QuotesApi.Contracts;
using QuotesApi.Models;
using Xunit;

namespace QuotesApi.Tests;

public sealed class CollectionTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public CollectionTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void ValidCollectionCreation()
    {
        var collection = new Collection("Summer Reads", 42);
        Assert.Equal("Summer Reads", collection.Name);
        Assert.Equal(42, collection.OwnerId);
        Assert.Empty(collection.Items);
    }

    [Fact]
    public void NameShorterThanThreeCharacters()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new Collection("ab", 1));
        Assert.Contains("between 3 and 80", ex.Message);
    }

    [Fact]
    public void NameLongerThanEightyCharacters()
    {
        var longName = new string('x', 81);
        var ex = Assert.Throws<InvalidOperationException>(() => new Collection(longName, 1));
        Assert.Contains("between 3 and 80", ex.Message);
    }

    [Fact]
    public void EmptyOrWhitespaceName()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new Collection("   ", 1));
        Assert.Contains("required", ex.Message);
    }

    [Fact]
    public void AddingFirstQuote()
    {
        var collection = new Collection("Reading List", 1);
        collection.AddItem(7);

        Assert.Single(collection.Items);
        Assert.Equal(7, collection.Items.Single().QuoteId);
    }

    [Fact]
    public void DuplicateQuoteId()
    {
        var collection = new Collection("Reading List", 1);
        collection.AddItem(7);

        var ex = Assert.Throws<InvalidOperationException>(() => collection.AddItem(7));
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact]
    public void MoreThanFiftyItems()
    {
        var collection = new Collection("Reading List", 1);
        for (var i = 1; i <= 50; i++)
        {
            collection.AddItem(i);
        }

        var ex = Assert.Throws<InvalidOperationException>(() => collection.AddItem(51));
        Assert.Contains("at most 50", ex.Message);
    }

    [Fact]
    public void RemovingExistingQuote()
    {
        var collection = new Collection("Reading List", 1);
        collection.AddItem(7);
        collection.RemoveItem(7);

        Assert.Empty(collection.Items);
    }

    [Fact]
    public void RemovingQuoteThatDoesNotExist()
    {
        var collection = new Collection("Reading List", 1);
        var ex = Assert.Throws<InvalidOperationException>(() => collection.RemoveItem(7));
        Assert.Contains("not in the collection", ex.Message);
    }

    [Fact]
    public async Task ApiReturns400ProblemDetailsWhenInvariantIsViolated()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/collections", new CreateCollectionRequest("ab", 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(await response.Content.ReadFromJsonAsync<ProblemDetails>());
    }

    [Fact]
    public async Task CreateQuote_UsesFakeClock()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/quotes",
            new CreateQuoteRequest("Test Author", "Test quote"));

        response.EnsureSuccessStatusCode();

        var quote = await response.Content.ReadFromJsonAsync<Quote>();

        Assert.NotNull(quote);
        Assert.Equal(
            new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc),
            quote.CreatedAtUtc);
    }
}
