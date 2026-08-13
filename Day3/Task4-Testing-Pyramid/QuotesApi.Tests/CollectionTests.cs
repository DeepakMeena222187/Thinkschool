using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuotesApi.Authorization;
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

    // Collection invariant tests (naming rules, item limits, duplicates) moved to
    // Tests.Domain/CollectionTests.cs: they exercised only the Collection domain
    // model and gained nothing from the WebApplicationFactory/HTTP pipeline.
    // What remains here is the behavior that genuinely needs the running app:
    // request validation -> ProblemDetails mapping, auth/authz enforcement,
    // cancellation propagation through the middleware pipeline, and DI-provided
    // clock wiring.

    [Fact]
    public async Task ApiReturns400ProblemDetailsWhenInvariantIsViolated()
    {
        using var client = _factory.CreateClient();
        var token = TestTokens.CreateInternalToken(1, new Claim(QuotePolicies.ScopeClaimType, QuotePolicies.WriteScope));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/collections", new CreateCollectionRequest("ab", 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(await response.Content.ReadFromJsonAsync<ProblemDetails>());
    }

    [Fact]
    public async Task PostCollections_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/collections", new CreateCollectionRequest("Reading List", 1));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostCollections_WithoutWriteScopeClaim_Returns403()
    {
        using var client = _factory.CreateClient();
        var token = TestTokens.CreateInternalToken(1);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/collections", new CreateCollectionRequest("Reading List", 1));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostCollections_WithWriteScopeClaim_Returns201()
    {
        using var client = _factory.CreateClient();
        var token = TestTokens.CreateInternalToken(1, new Claim(QuotePolicies.ScopeClaimType, QuotePolicies.WriteScope));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/collections", new CreateCollectionRequest("Reading List", 1));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task PostCollectionItem_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();
        var ownerToken = TestTokens.CreateInternalToken(1, new Claim(QuotePolicies.ScopeClaimType, QuotePolicies.WriteScope));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var createResponse = await client.PostAsJsonAsync("/api/collections", new CreateCollectionRequest("Reading List", 1));
        createResponse.EnsureSuccessStatusCode();
        var createdId = GetCreatedId(createResponse);

        client.DefaultRequestHeaders.Authorization = null;
        var response = await client.PostAsJsonAsync($"/api/collections/{createdId}/items", new AddCollectionItemRequest(7));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteCollectionItem_WithoutWriteScopeClaim_Returns403()
    {
        using var client = _factory.CreateClient();
        var ownerToken = TestTokens.CreateInternalToken(1, new Claim(QuotePolicies.ScopeClaimType, QuotePolicies.WriteScope));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var createResponse = await client.PostAsJsonAsync("/api/collections", new CreateCollectionRequest("Reading List", 1));
        createResponse.EnsureSuccessStatusCode();
        var createdId = GetCreatedId(createResponse);

        var addResponse = await client.PostAsJsonAsync($"/api/collections/{createdId}/items", new AddCollectionItemRequest(7));
        addResponse.EnsureSuccessStatusCode();

        var readOnlyToken = TestTokens.CreateInternalToken(1);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", readOnlyToken);

        var response = await client.DeleteAsync($"/api/collections/{createdId}/items/7");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static int GetCreatedId(HttpResponseMessage response)
    {
        var location = response.Headers.Location?.OriginalString
            ?? throw new InvalidOperationException("Response did not include a Location header.");
        return int.Parse(location[(location.LastIndexOf('/') + 1)..]);
    }

    [Fact]
    public async Task PostCollectionItem_CancellationStopsCompletion()
    {
        var repository = new BlockingCollectionRepository();
        using var factory = new CancellationAwareCollectionFactory(repository);
        using var client = factory.CreateClient();
        var token = TestTokens.CreateInternalToken(1, new Claim(QuotePolicies.ScopeClaimType, QuotePolicies.WriteScope));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

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
