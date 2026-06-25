namespace Clockodo.Mcp;

public sealed record ClockodoOptions(
    string? ApiUser,
    string? ApiKey,
    string ExternalApplication,
    string AcceptLanguage,
    Uri BaseUrl,
    bool ReadOnly)
{
    public static ClockodoOptions FromEnvironment()
    {
        var apiUser = Environment.GetEnvironmentVariable("CLOCKODO_API_USER");
        var apiKey = Environment.GetEnvironmentVariable("CLOCKODO_API_KEY");
        var externalApplication = Environment.GetEnvironmentVariable("CLOCKODO_EXTERNAL_APPLICATION");
        var acceptLanguage = Environment.GetEnvironmentVariable("CLOCKODO_ACCEPT_LANGUAGE");
        var baseUrl = Environment.GetEnvironmentVariable("CLOCKODO_BASE_URL");
        var readOnly = Environment.GetEnvironmentVariable("CLOCKODO_READ_ONLY");

        if (string.IsNullOrWhiteSpace(externalApplication) && !string.IsNullOrWhiteSpace(apiUser))
        {
            externalApplication = $"clockodo-mcp;{apiUser}";
        }

        return new ClockodoOptions(
            BlankToNull(apiUser),
            BlankToNull(apiKey),
            string.IsNullOrWhiteSpace(externalApplication) ? "clockodo-mcp;unknown@example.invalid" : externalApplication,
            string.IsNullOrWhiteSpace(acceptLanguage) ? "en" : acceptLanguage,
            new Uri(string.IsNullOrWhiteSpace(baseUrl) ? "https://my.clockodo.com/api/" : EnsureTrailingSlash(baseUrl)),
            string.Equals(readOnly, "true", StringComparison.OrdinalIgnoreCase) || readOnly == "1");
    }

    public void EnsureCredentials()
    {
        if (string.IsNullOrWhiteSpace(ApiUser) || string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                "Clockodo credentials are missing. Set CLOCKODO_API_USER and CLOCKODO_API_KEY in the MCP server environment.");
        }
    }

    private static string? BlankToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string EnsureTrailingSlash(string value) => value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/";
}
