namespace QuotesApi.Authentication;

public static class JwtSchemes
{
    public const string Policy = "JwtPolicy";
    public const string Internal = "InternalJwt";
    public const string Entra = "EntraJwt";

    public static string GetEntraAuthority(string tenantId)
        => $"https://login.microsoftonline.com/{tenantId}/v2.0";
}