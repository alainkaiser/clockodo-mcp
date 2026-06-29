using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Clockodo.Mcp;

public sealed class ClockodoClient(HttpClient httpClient, ClockodoOptions options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public ClockodoOptions Options => options;

    public async Task<string> SendAsync(
        ClockodoOperation operation,
        string path,
        string? queryJson,
        string? bodyJson,
        CancellationToken cancellationToken)
    {
        options.EnsureCredentials();
        EnsureWritesAllowed(operation);

        var requestUri = BuildUri(path, ParseJsonObject(queryJson, "queryJson"));
        using var request = new HttpRequestMessage(new HttpMethod(operation.Method), requestUri);

        request.Headers.TryAddWithoutValidation("X-ClockodoApiUser", options.ApiUser);
        request.Headers.TryAddWithoutValidation("X-ClockodoApiKey", options.ApiKey);
        request.Headers.TryAddWithoutValidation("X-Clockodo-External-Application", options.ExternalApplication);
        request.Headers.AcceptLanguage.ParseAdd(options.AcceptLanguage);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(bodyJson))
        {
            _ = JsonNode.Parse(bodyJson);
            request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
        }

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        var result = new JsonObject
        {
            ["ok"] = response.IsSuccessStatusCode,
            ["status"] = (int)response.StatusCode,
            ["reason"] = response.ReasonPhrase,
            ["method"] = operation.Method,
            ["path"] = path,
            ["operationId"] = operation.OperationId
        };

        if (response.Headers.RetryAfter is not null)
        {
            result["retryAfter"] = response.Headers.RetryAfter.ToString();
        }

        result["body"] = ParseResponseBody(body);

        return result.ToJsonString(JsonOptions);
    }

    private void EnsureWritesAllowed(ClockodoOperation operation)
    {
        if (!options.ReadOnly)
        {
            return;
        }

        if (!string.Equals(operation.Method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"CLOCKODO_READ_ONLY is enabled, so {operation.Method} {operation.Path} cannot be called.");
        }
    }

    private Uri BuildUri(string path, JsonObject? query)
    {
        var normalizedPath = path.TrimStart('/');
        var builder = new UriBuilder(new Uri(options.BaseUrl, normalizedPath));
        var queryParts = new List<string>();

        if (query is not null)
        {
            foreach (var property in query)
            {
                AddQueryPart(queryParts, property.Key, property.Value);
            }
        }

        builder.Query = string.Join("&", queryParts);
        return builder.Uri;
    }

    private static JsonObject? ParseJsonObject(string? json, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        var node = JsonNode.Parse(json);
        return node as JsonObject
            ?? throw new ArgumentException($"{parameterName} must be a JSON object.", parameterName);
    }

    private static JsonNode? ParseResponseBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return body;
        }
    }

    private static void AddQueryPart(List<string> queryParts, string key, JsonNode? value)
    {
        if (value is null)
        {
            return;
        }

        switch (value)
        {
            case JsonObject obj:
                foreach (var property in obj)
                {
                    AddQueryPart(queryParts, $"{key}[{property.Key}]", property.Value);
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    AddQueryPart(queryParts, key, item);
                }

                break;
            case JsonValue jsonValue:
                queryParts.Add(
                    $"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(ToQueryValue(jsonValue))}");
                break;
        }
    }

    private static string ToQueryValue(JsonValue value)
    {
        if (value.TryGetValue<string>(out var stringValue))
        {
            return stringValue;
        }

        if (value.TryGetValue<bool>(out var boolValue))
        {
            return boolValue ? "true" : "false";
        }

        if (value.TryGetValue<decimal>(out var decimalValue))
        {
            return decimalValue.ToString(CultureInfo.InvariantCulture);
        }

        // Numbers outside decimal range (very large magnitudes) fall back to double
        // so query values stay culture-invariant instead of relying on JSON formatting.
        if (value.TryGetValue<double>(out var doubleValue))
        {
            return doubleValue.ToString(CultureInfo.InvariantCulture);
        }

        return value.ToJsonString();
    }
}
