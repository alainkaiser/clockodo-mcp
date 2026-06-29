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
            ValidateBaseUrl(new Uri(string.IsNullOrWhiteSpace(baseUrl) ? "https://my.clockodo.com/api/" : EnsureTrailingSlash(baseUrl))),
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

    private static Uri ValidateBaseUrl(Uri baseUrl)
    {
        if (AllowAnyBaseUrl())
        {
            return baseUrl;
        }

        if (!string.IsNullOrEmpty(baseUrl.UserInfo))
        {
            throw new InvalidOperationException("CLOCKODO_BASE_URL must not include userinfo.");
        }

        if (!IsLocalTestHost(baseUrl) &&
            !string.Equals(baseUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("CLOCKODO_BASE_URL must use HTTPS for non-local hosts.");
        }

        if (!IsLocalTestHost(baseUrl) && !IsClockodoHost(baseUrl.Host))
        {
            throw new InvalidOperationException(
                "CLOCKODO_BASE_URL must point to a Clockodo API host (*.clockodo.com). " +
                "Set CLOCKODO_BASE_URL_ALLOW_ANY=true only for local testing.");
        }

        return baseUrl;
    }

    private static bool AllowAnyBaseUrl() =>
        string.Equals(Environment.GetEnvironmentVariable("CLOCKODO_BASE_URL_ALLOW_ANY"), "true", StringComparison.OrdinalIgnoreCase);

    private static bool IsLocalTestHost(Uri baseUrl) =>
        string.Equals(baseUrl.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(baseUrl.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
        baseUrl.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);

    private static bool IsClockodoHost(string host) =>
        string.Equals(host, "clockodo.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".clockodo.com", StringComparison.OrdinalIgnoreCase);

    private static string? BlankToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string EnsureTrailingSlash(string value) => value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/";
}
