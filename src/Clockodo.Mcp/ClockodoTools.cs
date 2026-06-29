using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Clockodo.Mcp;

public sealed partial class ClockodoTools
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    [McpServerTool(Name = "clockodo_server_info", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Shows Clockodo MCP catalog coverage, source version, runtime safety settings, and how this local server differs from Clockodo's native MCP endpoint.")]
    public static string ServerInfo(ClockodoOptions options)
    {
        var all = ClockodoOperationCatalog.All;
        var active = ClockodoOperationCatalog.Active;

        return JsonSerializer.Serialize(new
        {
            source = ClockodoOperationCatalog.SourceUrl,
            openApiVersion = ClockodoOperationCatalog.OpenApiVersion,
            documentation = "https://docs.clockodo.com/",
            operations = new
            {
                total = all.Count,
                active = active.Count,
                deprecatedHidden = all.Count(operation => operation.Deprecated),
                blockedHidden = ClockodoOperationCatalog.BlockedOperations.Count,
                byMethod = active
                    .GroupBy(operation => operation.Method)
                    .OrderBy(group => group.Key)
                    .ToDictionary(group => group.Key, group => group.Count()),
                byTag = active
                    .GroupBy(operation => operation.Tag)
                    .OrderBy(group => group.Key)
                    .ToDictionary(group => group.Key, group => group.Count())
            },
            runtime = new
            {
                transport = "stdio",
                baseUrl = options.BaseUrl.ToString(),
                acceptLanguage = options.AcceptLanguage,
                readOnly = options.ReadOnly,
                credentialsConfigured = !string.IsNullOrWhiteSpace(options.ApiUser) && !string.IsNullOrWhiteSpace(options.ApiKey),
                restAuthenticationHeaders = new[] { "X-ClockodoApiUser", "X-ClockodoApiKey" }
            },
            nativeClockodoMcp = new
            {
                endpoint = "https://mcp.clockodo.com/mcp",
                transport = "http",
                authenticationHeaders = new[] { "X-Clockodo-User", "X-Clockodo-Key" },
                documentedCoverage = "selected Clockodo API capabilities"
            },
            safety = new[]
            {
                "Deprecated OpenAPI operations are hidden and rejected.",
                "High-risk operations such as public registration are blocklisted and rejected.",
                "GET operations use clockodo_read; POST, PUT, and DELETE operations use clockodo_write.",
                "Set CLOCKODO_READ_ONLY=true to block all write operations at runtime."
            },
            convenienceTools = new[]
            {
                "clockodo_current_time",
                "clockodo_me",
                "clockodo_get_my_absences",
                "clockodo_get_entries_by_timeframe"
            },
            businessTools = new[]
            {
                "clockodo_list_customers",
                "clockodo_list_projects",
                "clockodo_list_services",
                "clockodo_get_current_clock",
                "clockodo_start_clock",
                "clockodo_stop_clock",
                "clockodo_update_clock",
                "clockodo_get_time_entry",
                "clockodo_create_time_entry",
                "clockodo_update_time_entry",
                "clockodo_delete_time_entry"
            }
        }, JsonOptions);
    }

    [McpServerTool(Name = "clockodo_list_operations", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Lists current, non-deprecated Clockodo REST API operations from the bundled OpenAPI catalog. Use this before clockodo_read or clockodo_write when you need the latest path, method, and parameters.")]
    public static string ListOperations(
        [Description("Optional case-insensitive search over operation id, tag, path, summary, and parameter names.")]
        string? search = null,
        [Description("Optional exact tag filter, for example Entry, Service, Project, Clock, User, Absence, Customer.")]
        string? tag = null)
    {
        var operations = ClockodoOperationCatalog.Active.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(tag))
        {
            operations = operations.Where(operation => string.Equals(operation.Tag, tag, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            operations = operations.Where(operation => MatchesSearch(operation, search));
        }

        var list = operations.ToList();

        return JsonSerializer.Serialize(new
        {
            source = ClockodoOperationCatalog.SourceUrl,
            openApiVersion = ClockodoOperationCatalog.OpenApiVersion,
            count = list.Count,
            operations = list.Select(ToSummaryDto)
        }, JsonOptions);
    }

    [McpServerTool(Name = "clockodo_get_operation", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Gets details for one current Clockodo REST API operation by operationId.")]
    public static string GetOperation(
        [Description("The operationId returned by clockodo_list_operations, for example getUsersMeV4 or getServicesV4.")]
        string operationId)
    {
        var operation = ClockodoOperationCatalog.FindByOperationId(operationId);

        if (operation is null || !ClockodoOperationCatalog.IsCallable(operation))
        {
            throw new McpException(DescribeUnavailableOperation(operationId, operation));
        }

        return JsonSerializer.Serialize(ToDetailedDto(operation), JsonOptions);
    }

    [McpServerTool(Name = "clockodo_read", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Calls a current, non-deprecated Clockodo GET operation. Prefer operationId plus pathParametersJson; or pass method GET and a concrete path without the /api prefix. Responses include ok, status, reason, and body; when Clockodo rate-limits, retryAfter may be present.")]
    public static Task<string> Read(
        ClockodoClient client,
        [Description("Optional operationId from clockodo_list_operations. When provided, method and path are inferred from the catalog.")]
        string? operationId = null,
        [Description("HTTP method. Must be GET when provided.")]
        string? method = null,
        [Description("Concrete API path without the /api prefix, for example /v4/services or /v4/services/123. Required when operationId is not provided.")]
        string? path = null,
        [Description("Optional JSON object for path placeholders when operationId or a templated path is used, for example {\"id\":123}.")]
        string? pathParametersJson = null,
        [Description("Optional JSON object of query parameters. Nested objects are encoded as deepObject keys such as filter[active]. Arrays are repeated query keys; pass a CSV string when the Clockodo docs require CSV.")]
        string? queryJson = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteRequest(client, operationId, method, path, pathParametersJson, queryJson, bodyJson: null, requireRead: true, cancellationToken);
    }

    [McpServerTool(Name = "clockodo_write", ReadOnly = false, Destructive = true, OpenWorld = false)]
    [Description("Calls a current, non-deprecated Clockodo POST, PUT, or DELETE operation. Use only when you intend to create, update, delete, start, stop, approve, or otherwise change Clockodo data. Responses include ok, status, reason, and body; when Clockodo rate-limits, retryAfter may be present.")]
    public static Task<string> Write(
        ClockodoClient client,
        [Description("Optional operationId from clockodo_list_operations. When provided, method and path are inferred from the catalog.")]
        string? operationId = null,
        [Description("HTTP method such as POST, PUT, or DELETE. Required when operationId is not provided.")]
        string? method = null,
        [Description("Concrete API path without the /api prefix, for example /v4/services/123. Required when operationId is not provided.")]
        string? path = null,
        [Description("Optional JSON object for path placeholders when operationId or a templated path is used, for example {\"id\":123}.")]
        string? pathParametersJson = null,
        [Description("Optional JSON object of query parameters. Nested objects are encoded as deepObject keys such as filter[active]. Arrays are repeated query keys; pass a CSV string when the Clockodo docs require CSV.")]
        string? queryJson = null,
        [Description("Optional JSON request body for POST and PUT operations.")]
        string? bodyJson = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteRequest(client, operationId, method, path, pathParametersJson, queryJson, bodyJson, requireRead: false, cancellationToken);
    }

    private static async Task<string> ExecuteRequest(
        ClockodoClient client,
        string? operationId,
        string? method,
        string? path,
        string? pathParametersJson,
        string? queryJson,
        string? bodyJson,
        bool requireRead,
        CancellationToken cancellationToken)
    {
        try
        {
            var (operation, concretePath) = ResolveOperation(operationId, method, path, pathParametersJson);

            if (requireRead && !string.Equals(operation.Method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                throw new McpException($"clockodo_read only supports GET operations. Use clockodo_write for {operation.Method} {operation.Path}.");
            }

            if (!requireRead && string.Equals(operation.Method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                throw new McpException("clockodo_write is for POST, PUT, and DELETE operations. Use clockodo_read for GET operations.");
            }

            if (!requireRead && operation.RequiresBody && string.IsNullOrWhiteSpace(bodyJson))
            {
                throw new McpException($"Operation {operation.OperationId} requires a JSON request body.");
            }

            return await client.SendAsync(operation, concretePath, queryJson, bodyJson, cancellationToken);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException or JsonException or HttpRequestException or TaskCanceledException)
        {
            throw new McpException(DescribeRequestFailure(exception), exception);
        }
    }

    private static object ToSummaryDto(ClockodoOperation operation) => new
    {
        operationId = operation.OperationId,
        method = operation.Method,
        path = operation.Path,
        tag = operation.Tag,
        summary = operation.Summary,
        parameters = operation.Parameters,
        requiresBody = operation.RequiresBody,
        documentationUrl = operation.DocumentationUrl
    };

    private static object ToDetailedDto(ClockodoOperation operation) => new
    {
        operationId = operation.OperationId,
        method = operation.Method,
        path = operation.Path,
        tag = operation.Tag,
        summary = operation.Summary,
        parameters = operation.Parameters,
        parameterDetails = JsonNode.Parse(operation.ParametersJson),
        requiresBody = operation.RequiresBody,
        requestBody = string.IsNullOrWhiteSpace(operation.RequestBodyJson) ? null : JsonNode.Parse(operation.RequestBodyJson),
        documentationUrl = operation.DocumentationUrl
    };

    private static bool MatchesSearch(ClockodoOperation operation, string search)
    {
        return Contains(operation.OperationId, search)
            || Contains(operation.Tag, search)
            || Contains(operation.Path, search)
            || Contains(operation.Summary, search)
            || operation.Parameters.Any(parameter => Contains(parameter, search));
    }

    private static bool Contains(string? value, string search) =>
        value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;

    private static string DescribeUnavailableOperation(string operationId, ClockodoOperation? operation)
    {
        if (operation is not null && ClockodoOperationCatalog.IsBlocked(operation))
        {
            return $"Blocked Clockodo operationId: {operationId}. This operation is not exposed through the MCP server.";
        }

        if (operation is not null && operation.Deprecated)
        {
            return $"Unknown or deprecated Clockodo operationId: {operationId}";
        }

        return $"Unknown or deprecated Clockodo operationId: {operationId}";
    }

    private static string DescribeRequestFailure(Exception exception) =>
        exception switch
        {
            TaskCanceledException => "Clockodo request timed out or was canceled.",
            HttpRequestException httpRequestException when httpRequestException.Message.Length > 0 => httpRequestException.Message,
            _ => exception.Message
        };

    private static (ClockodoOperation Operation, string ConcretePath) ResolveOperation(
        string? operationId,
        string? method,
        string? path,
        string? pathParametersJson)
    {
        ClockodoOperation? operation;
        var effectivePath = path;

        if (!string.IsNullOrWhiteSpace(operationId))
        {
            operation = ClockodoOperationCatalog.FindByOperationId(operationId);

            if (operation is null || !ClockodoOperationCatalog.IsCallable(operation))
            {
                throw new McpException(DescribeUnavailableOperation(operationId, operation));
            }

            method = operation.Method;
            effectivePath = operation.Path;
        }
        else
        {
            method = NormalizeMethod(method);
            effectivePath = NormalizePath(effectivePath);
            operation = ClockodoOperationCatalog.Match(method, effectivePath);

            if (operation is null)
            {
                throw new McpException($"No current Clockodo operation matches {method} {effectivePath}.");
            }
        }

        var concretePath = ApplyPathParameters(NormalizePath(effectivePath), pathParametersJson);

        var matchedOperation = ClockodoOperationCatalog.Match(method, concretePath);
        if (matchedOperation is null || !string.Equals(matchedOperation.OperationId, operation.OperationId, StringComparison.OrdinalIgnoreCase))
        {
            throw new McpException($"Path parameters do not match Clockodo operation {operation.OperationId} ({operation.Method} {operation.Path}).");
        }

        return (operation, concretePath);
    }

    private static string NormalizeMethod(string? method)
    {
        if (string.IsNullOrWhiteSpace(method))
        {
            throw new McpException("method is required when operationId is not provided.");
        }

        var normalized = method.Trim().ToUpperInvariant();
        return normalized is "GET" or "POST" or "PUT" or "DELETE"
            ? normalized
            : throw new McpException($"Unsupported Clockodo HTTP method: {method}");
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new McpException("path is required when operationId is not provided.");
        }

        var normalized = path.Trim();
        if (normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpException("Use a path without scheme/host, for example /v4/services.");
        }

        if (normalized.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[4..];
        }

        return normalized.StartsWith('/') ? normalized : "/" + normalized;
    }

    private static string ApplyPathParameters(string path, string? pathParametersJson)
    {
        if (!path.Contains('{', StringComparison.Ordinal))
        {
            return path;
        }

        if (string.IsNullOrWhiteSpace(pathParametersJson))
        {
            throw new McpException($"Path {path} contains placeholders. Provide pathParametersJson.");
        }

        var node = JsonNode.Parse(pathParametersJson) as JsonObject
            ?? throw new McpException("pathParametersJson must be a JSON object.");

        foreach (var property in node)
        {
            var value = property.Value?.GetValue<object>()?.ToString();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new McpException($"Path parameter '{property.Key}' must not be empty.");
            }

            path = path.Replace("{" + property.Key + "}", Uri.EscapeDataString(value), StringComparison.Ordinal);
        }

        if (path.Contains('{', StringComparison.Ordinal))
        {
            throw new McpException($"Not all path placeholders were provided: {path}");
        }

        return path;
    }

}
