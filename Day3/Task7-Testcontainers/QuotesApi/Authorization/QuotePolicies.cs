namespace QuotesApi.Authorization;

public static class QuotePolicies
{
    public const string CanEditQuotes = "can-edit-quotes";
    public const string ScopeClaimType = "scope";
    public const string WriteScope = "quotes.write";
}
