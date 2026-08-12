using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Authentication;
using QuotesApi.Services;

namespace QuotesApi.Tests;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestJwtSecret = "this-is-a-test-secret-long-enough-for-hs256-1234";
    public const string TestEntraTenantId = "11111111-1111-1111-1111-111111111111";
    public const string TestEntraClientId = "22222222-2222-2222-2222-222222222222";
    public static string TestEntraAuthority => JwtSchemes.GetEntraAuthority(TestEntraTenantId);

    public CustomWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable("Jwt__Secret", TestJwtSecret);
    }

    public FakeClock Clock { get; } = new()
    {
        UtcNow = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero)
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = TestJwtSecret,
                ["Jwt:Issuer"] = "https://localhost",
                ["Jwt:Audience"] = "quotes-api",
                ["Entra:TenantId"] = TestEntraTenantId,
                ["Entra:ClientId"] = TestEntraClientId
            });
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IClock));

            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddSingleton<IClock>(Clock);

            services.PostConfigure<JwtBearerOptions>(JwtSchemes.Entra, options =>
            {
                options.BackchannelHttpHandler = new FakeEntraMetadataHandler(TestEntraAuthority);
            });
        });
    }

    private sealed class FakeEntraMetadataHandler(string authority) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (path.EndsWith("/.well-known/openid-configuration", StringComparison.OrdinalIgnoreCase))
            {
                var json = "{" +
                    $"\"issuer\":\"{authority}\"," +
                    $"\"jwks_uri\":\"{authority}/discovery/v2.0/keys\"," +
                    $"\"authorization_endpoint\":\"{authority}/oauth2/v2.0/authorize\"," +
                    $"\"token_endpoint\":\"{authority}/oauth2/v2.0/token\"," +
                    "\"response_types_supported\":[\"code\",\"id_token\",\"code id_token\",\"token id_token\"]," +
                    "\"subject_types_supported\":[\"pairwise\",\"public\"]," +
                    "\"id_token_signing_alg_values_supported\":[\"RS256\"]" +
                    "}";

                return Task.FromResult(CreateJsonResponse(json));
            }

            if (path.EndsWith("/discovery/v2.0/keys", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(CreateJsonResponse("{\"keys\":[]}"));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }

        private static HttpResponseMessage CreateJsonResponse(string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }
}
