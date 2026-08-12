using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuotesApi.Contracts;
using QuotesApi.Models;
using QuotesApi.Repositories;
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
    public async Task PostCollectionItem_CancellationStopsCompletion()
    {
        var repository = new BlockingCollectionRepository();
        using var factory = new CancellationAwareCollectionFactory(repository);
        using var client = factory.CreateClient();

        var createCollectionResponse = await client.PostAsJsonAsync(
            "/api/collections",
            new CreateCollectionRequest("Reading List", 1));

        createCollectionResponse.EnsureSuccessStatusCode();

        var createdCollection = await createCollectionResponse.Content.ReadFromJsonAsync<Collection>();
        Assert.NotNull(createdCollection);

        using var cts = new CancellationTokenSource();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/collections/{createdCollection.Id}/items")
        {
            Content = JsonContent.Create(new AddCollectionItemRequest(7))
        };

        var requestTask = client.SendAsync(request, cts.Token);
        await repository.Started.Task;

        Assert.True(repository.ReceivedToken.HasValue);
        Assert.False(repository.ReceivedToken.Value.IsCancellationRequested);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => requestTask);
        Assert.True(repository.ReceivedToken.Value.IsCancellationRequested);
        Assert.True(repository.Canceled.Task.IsCompleted);
    }

    [Fact]
    public async Task CreateQuote_UsesFakeClock()
    {
        using var client = _factory.CreateClient();

        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest { Email = "admin@quotes.local", Password = "meena@123" });

        loginResponse.EnsureSuccessStatusCode();
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(loginBody);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginBody.AccessToken);

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

    private sealed class CancellationAwareCollectionFactory(ICollectionRepository repository) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICollectionRepository>();
                services.AddSingleton<ICollectionRepository>(repository);
            });
        }
    }

    private sealed class BlockingCollectionRepository : ICollectionRepository
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken? ReceivedToken { get; private set; }

        public async Task<Collection?> GetByIdAsync(int id, CancellationToken ct)
        {
            ReceivedToken = ct;
            Started.TrySetResult(true);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                Canceled.TrySetResult(true);
                throw;
            }

            return null;
        }

        public Task AddAsync(Collection collection, CancellationToken ct) => Task.CompletedTask;

        public Task UpdateAsync(Collection collection, CancellationToken ct) => Task.CompletedTask;

        public Task DeleteAsync(int id, CancellationToken ct) => Task.CompletedTask;
    }
}
