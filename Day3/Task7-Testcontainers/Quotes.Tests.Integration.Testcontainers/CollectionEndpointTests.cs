using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using QuotesApi.Authorization;
using QuotesApi.Contracts;
using QuotesApi.Models;
using Xunit;

namespace Quotes.Tests.Integration.Testcontainers;

[Collection(SqlServerCollection.Name)]
public sealed class CollectionEndpointTests(SqlServerContainerFixture _fixture)
{
    private static string CreateWriterToken(int userId = 1) =>
        TestAuth.CreateInternalToken(userId, new Claim(QuotePolicies.ScopeClaimType, QuotePolicies.WriteScope));

    private static int GetCreatedId(HttpResponseMessage response)
    {
        var location = response.Headers.Location?.OriginalString
            ?? throw new InvalidOperationException("Response did not include a Location header.");
        return int.Parse(location[(location.LastIndexOf('/') + 1)..]);
    }

    [Fact]
    public async Task PostCollections_WithoutToken_ReturnsUnauthorized()
    {
        using var factory = new IntegrationTestFactory(_fixture.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/collections", new CreateCollectionRequest("Reading List", 1));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostCollections_WithoutWriteScope_ReturnsForbidden()
    {
        using var factory = new IntegrationTestFactory(_fixture.ConnectionString);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.CreateInternalToken(1));

        var response = await client.PostAsJsonAsync("/api/collections", new CreateCollectionRequest("Reading List", 1));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostCollections_WithNameBelowMinimumLength_ReturnsBadRequestProblemDetails()
    {
        using var factory = new IntegrationTestFactory(_fixture.ConnectionString);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateWriterToken());

        var response = await client.PostAsJsonAsync("/api/collections", new CreateCollectionRequest("ab", 1));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Title.Should().Be("Invalid collection");
    }

    [Fact]
    public async Task PostCollections_WithValidRequest_ReturnsCreated()
    {
        using var factory = new IntegrationTestFactory(_fixture.ConnectionString);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateWriterToken());

        var response = await client.PostAsJsonAsync("/api/collections", new CreateCollectionRequest("Reading List", 1));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var collection = await response.Content.ReadFromJsonAsync<Collection>();
        collection!.Name.Should().Be("Reading List");
    }

    [Fact]
    public async Task PostCollectionItem_WhenCollectionDoesNotExist_ReturnsNotFound()
    {
        using var factory = new IntegrationTestFactory(_fixture.ConnectionString);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateWriterToken());

        var response = await client.PostAsJsonAsync("/api/collections/999/items", new AddCollectionItemRequest(7));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostCollectionItem_WithValidRequest_ReturnsOkWithItemAdded()
    {
        using var factory = new IntegrationTestFactory(_fixture.ConnectionString);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateWriterToken());
        var createResponse = await client.PostAsJsonAsync("/api/collections", new CreateCollectionRequest("Reading List", 1));
        var createdId = GetCreatedId(createResponse);

        var response = await client.PostAsJsonAsync($"/api/collections/{createdId}/items", new AddCollectionItemRequest(7));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // Collection.Items has no setter (it's a computed wrapper over a private field),
        // so it can't round-trip through typed JSON deserialization - read the raw
        // payload instead of binding it back into the domain type.
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items");
        items.GetArrayLength().Should().Be(1);
        items[0].GetProperty("quoteId").GetInt32().Should().Be(7);
    }

    [Fact]
    public async Task PostCollectionItem_WithDuplicateQuoteId_ReturnsBadRequestProblemDetails()
    {
        using var factory = new IntegrationTestFactory(_fixture.ConnectionString);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateWriterToken());
        var createResponse = await client.PostAsJsonAsync("/api/collections", new CreateCollectionRequest("Reading List", 1));
        var createdId = GetCreatedId(createResponse);
        await client.PostAsJsonAsync($"/api/collections/{createdId}/items", new AddCollectionItemRequest(7));

        var response = await client.PostAsJsonAsync($"/api/collections/{createdId}/items", new AddCollectionItemRequest(7));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Detail.Should().Contain("Duplicate");
    }

    [Fact]
    public async Task DeleteCollectionItem_WhenItemNotInCollection_ReturnsBadRequestProblemDetails()
    {
        using var factory = new IntegrationTestFactory(_fixture.ConnectionString);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateWriterToken());
        var createResponse = await client.PostAsJsonAsync("/api/collections", new CreateCollectionRequest("Reading List", 1));
        var createdId = GetCreatedId(createResponse);

        var response = await client.DeleteAsync($"/api/collections/{createdId}/items/7");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Detail.Should().Contain("not in the collection");
    }

    [Fact]
    public async Task DeleteCollectionItem_WithValidRequest_ReturnsOkWithoutItem()
    {
        using var factory = new IntegrationTestFactory(_fixture.ConnectionString);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateWriterToken());
        var createResponse = await client.PostAsJsonAsync("/api/collections", new CreateCollectionRequest("Reading List", 1));
        var createdId = GetCreatedId(createResponse);
        await client.PostAsJsonAsync($"/api/collections/{createdId}/items", new AddCollectionItemRequest(7));

        var response = await client.DeleteAsync($"/api/collections/{createdId}/items/7");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task DeleteCollectionItem_WithoutWriteScope_ReturnsForbidden()
    {
        using var factory = new IntegrationTestFactory(_fixture.ConnectionString);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateWriterToken());
        var createResponse = await client.PostAsJsonAsync("/api/collections", new CreateCollectionRequest("Reading List", 1));
        var createdId = GetCreatedId(createResponse);
        await client.PostAsJsonAsync($"/api/collections/{createdId}/items", new AddCollectionItemRequest(7));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.CreateInternalToken(1));

        var response = await client.DeleteAsync($"/api/collections/{createdId}/items/7");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
