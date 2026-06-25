using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Clockodo.Mcp;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

var tests = new (string Name, Func<Task> Run)[]
{
    ("catalog filters deprecated operations", CatalogFiltersDeprecatedOperations),
    ("operation lookup rejects deprecated operation ids", OperationLookupRejectsDeprecatedIds),
    ("request builds Clockodo headers and deep query", RequestBuildsHeadersAndQuery),
    ("request applies path parameters", RequestAppliesPathParameters),
    ("request sends JSON body", RequestSendsJsonBody),
    ("read and write tools enforce method boundaries", ReadAndWriteToolsEnforceBoundaries),
    ("read-only mode blocks writes", ReadOnlyModeBlocksWrites),
    ("stdio MCP server exposes and invokes expected tools", StdioServerExposesAndInvokesTools)
};

foreach (var test in tests)
{
    await test.Run();
    Console.WriteLine($"PASS {test.Name}");
}

if (Environment.GetEnvironmentVariable("CLOCKODO_LIVE_TEST") is "1" or "true")
{
    await LiveClockodoUsersMe();
    Console.WriteLine("PASS live Clockodo getUsersMeV4");
}

static Task CatalogFiltersDeprecatedOperations()
{
    Assert(ClockodoOperationCatalog.All.Count > 100, "Expected a rich Clockodo operation catalog.");
    Assert(ClockodoOperationCatalog.All.Count > ClockodoOperationCatalog.Active.Count, "Expected deprecated operations in the source catalog.");
    AssertEqual(
        ClockodoOperationCatalog.All.Count(operation => !operation.Deprecated),
        ClockodoOperationCatalog.Active.Count,
        "Active operation count must equal all non-deprecated operations.");
    Assert(ClockodoOperationCatalog.Active.All(operation => !operation.Deprecated), "Active catalog must exclude deprecated operations.");

    var services = ClockodoTools.ListOperations(search: "services", tag: "Service");
    Assert(services.Contains("getServicesV4", StringComparison.Ordinal), "Service list should include getServicesV4.");
    Assert(!services.Contains("parameterDetails", StringComparison.Ordinal), "Operation list should stay compact.");

    var getServices = ClockodoTools.GetOperation("getServicesV4");
    Assert(getServices.Contains("\"parameterDetails\"", StringComparison.Ordinal), "Operation details should include parameter metadata.");
    Assert(getServices.Contains("\"filter\"", StringComparison.Ordinal), "Operation details should include filter parameter metadata.");

    var createService = ClockodoTools.GetOperation("createServiceV4");
    Assert(createService.Contains("\"requestBody\"", StringComparison.Ordinal), "Write operation details should include request body metadata.");
    Assert(createService.Contains("\"requiredProperties\"", StringComparison.Ordinal), "Request body metadata should include required properties.");
    Assert(createService.Contains("\"name\"", StringComparison.Ordinal), "createServiceV4 should document the required name body field.");

    return Task.CompletedTask;
}

static Task OperationLookupRejectsDeprecatedIds()
{
    AssertThrows<McpException>(() => ClockodoTools.GetOperation("getAggregatesUsersMeV2"));
    return Task.CompletedTask;
}

static async Task RequestBuildsHeadersAndQuery()
{
    var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent("""{"data":[]}""", Encoding.UTF8, "application/json")
    });

    var client = CreateClient(handler);

    var result = await ClockodoTools.Read(
        client,
        operationId: "getServicesV4",
        queryJson: """{"filter":{"active":true},"sort":["name"],"page":2}""");

    var request = handler.Request ?? throw new InvalidOperationException("Expected an HTTP request.");
    AssertEqual(HttpMethod.Get, request.Method, "Unexpected HTTP method.");
    AssertEqual("/api/v4/services", request.RequestUri?.AbsolutePath, "Unexpected request path.");
    Assert(request.RequestUri?.Query.Contains("filter%5Bactive%5D=true", StringComparison.Ordinal) == true, "Expected deepObject filter query.");
    Assert(request.RequestUri?.Query.Contains("sort=name", StringComparison.Ordinal) == true, "Expected repeated array query item.");
    Assert(request.RequestUri?.Query.Contains("page=2", StringComparison.Ordinal) == true, "Expected page query.");
    AssertEqual("user@example.com", Header(request, "X-ClockodoApiUser"), "Missing API user header.");
    AssertEqual("secret", Header(request, "X-ClockodoApiKey"), "Missing API key header.");
    AssertEqual("clockodo-mcp;user@example.com", Header(request, "X-Clockodo-External-Application"), "Missing external app header.");
    Assert(result.Contains("\"ok\": true", StringComparison.Ordinal), "Expected successful result JSON.");
}

static async Task RequestAppliesPathParameters()
{
    var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent("""{"data":{"id":42}}""", Encoding.UTF8, "application/json")
    });

    var client = CreateClient(handler);

    await ClockodoTools.Read(
        client,
        operationId: "getServiceByIdV4",
        pathParametersJson: """{"id":42}""");

    AssertEqual("/api/v4/services/42", handler.Request?.RequestUri?.AbsolutePath, "Path parameter was not applied.");
}

static async Task RequestSendsJsonBody()
{
    var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent("""{"data":{"id":1}}""", Encoding.UTF8, "application/json")
    });

    var client = CreateClient(handler);

    await ClockodoTools.Write(
        client,
        operationId: "createServiceV4",
        bodyJson: """{"name":"Consulting"}""");

    AssertEqual(HttpMethod.Post, handler.Request?.Method, "Unexpected HTTP method.");
    AssertEqual("""{"name":"Consulting"}""", handler.Body, "Unexpected request body.");
    AssertEqual("application/json", handler.Request?.Content?.Headers.ContentType?.MediaType, "Unexpected content type.");
}

static async Task ReadAndWriteToolsEnforceBoundaries()
{
    var client = CreateClient(new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)));

    await AssertThrowsAsync<McpException>(() =>
        ClockodoTools.Read(client, operationId: "createServiceV4"));

    await AssertThrowsAsync<McpException>(() =>
        ClockodoTools.Write(client, operationId: "getServicesV4"));
}

static async Task ReadOnlyModeBlocksWrites()
{
    var options = new ClockodoOptions(
        "user@example.com",
        "secret",
        "clockodo-mcp;user@example.com",
        "en",
        new Uri("https://my.clockodo.com/api/"),
        ReadOnly: true);

    var client = new ClockodoClient(new HttpClient(new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK))), options);

    await AssertThrowsAsync<McpException>(() =>
        ClockodoTools.Write(client, operationId: "createServiceV4", bodyJson: """{"name":"Blocked"}"""));
}

static async Task StdioServerExposesAndInvokesTools()
{
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    await using var fakeClockodo = new OneShotHttpServer("""{"data":{"id":7,"name":"Test User"}}""");

    var env = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
    env["CLOCKODO_API_USER"] = "user@example.com";
    env["CLOCKODO_API_KEY"] = "secret";
    env["CLOCKODO_EXTERNAL_APPLICATION"] = "clockodo-mcp-test;user@example.com";
    env["CLOCKODO_BASE_URL"] = fakeClockodo.BaseUri.ToString();

    var repoRoot = FindRepoRoot();
    var transport = new StdioClientTransport(new StdioClientTransportOptions
    {
        Name = "clockodo-mcp-test",
        Command = Environment.GetEnvironmentVariable("CLOCKODO_MCP_COMMAND")
            ?? Path.Combine(repoRoot, "src", "Clockodo.Mcp", "bin", "Debug", "net10.0", "Clockodo.Mcp"),
        Arguments = [],
        WorkingDirectory = repoRoot,
        InheritEnvironmentVariables = false,
        EnvironmentVariables = env
    });

    await using var client = await McpClient.CreateAsync(transport, cancellationToken: cts.Token);
    var tools = await client.ListToolsAsync(cancellationToken: cts.Token);
    var toolNames = tools.Select(tool => tool.Name).ToArray();

    Assert(toolNames.Contains("clockodo_list_operations"), "Missing clockodo_list_operations tool.");
    Assert(toolNames.Contains("clockodo_get_operation"), "Missing clockodo_get_operation tool.");
    Assert(toolNames.Contains("clockodo_read"), "Missing clockodo_read tool.");
    Assert(toolNames.Contains("clockodo_write"), "Missing clockodo_write tool.");

    var listResult = await client.CallToolAsync(
        "clockodo_list_operations",
        new Dictionary<string, object?> { ["search"] = "getUsersMeV4" },
        cancellationToken: cts.Token);

    Assert(ToolText(listResult).Contains("getUsersMeV4", StringComparison.Ordinal), "list operation call did not return getUsersMeV4.");

    var requestResult = await client.CallToolAsync(
        "clockodo_read",
        new Dictionary<string, object?> { ["operationId"] = "getUsersMeV4" },
        cancellationToken: cts.Token);

    var captured = await fakeClockodo.Request.WaitAsync(cts.Token);
    AssertEqual("GET", captured.Method, "Unexpected fake Clockodo method.");
    AssertEqual("/api/v4/users/me", captured.Path, "Unexpected fake Clockodo path.");
    AssertEqual("user@example.com", captured.Headers["x-clockodoapiuser"], "Unexpected fake Clockodo user header.");
    AssertEqual("secret", captured.Headers["x-clockodoapikey"], "Unexpected fake Clockodo key header.");
    AssertEqual("clockodo-mcp-test;user@example.com", captured.Headers["x-clockodo-external-application"], "Unexpected fake Clockodo external app header.");
    Assert(ToolText(requestResult).Contains("\"ok\": true", StringComparison.Ordinal), "request tool did not report success.");
}

static async Task LiveClockodoUsersMe()
{
    var options = ClockodoOptions.FromEnvironment();
    var client = new ClockodoClient(new HttpClient(), options);
    var result = await ClockodoTools.Read(client, operationId: "getUsersMeV4");
    using var document = JsonDocument.Parse(result);

    var root = document.RootElement;
    var status = root.GetProperty("status").GetInt32();
    var reason = root.GetProperty("reason").GetString();

    Assert(root.GetProperty("ok").GetBoolean(), $"Live Clockodo getUsersMeV4 failed with HTTP {status} {reason}.");
    AssertEqual(200, status, "Live Clockodo getUsersMeV4 returned an unexpected status.");
}

static ClockodoClient CreateClient(HttpMessageHandler handler)
{
    var options = new ClockodoOptions(
        "user@example.com",
        "secret",
        "clockodo-mcp;user@example.com",
        "en",
        new Uri("https://my.clockodo.com/api/"),
        ReadOnly: false);

    return new ClockodoClient(new HttpClient(handler), options);
}

static string Header(HttpRequestMessage request, string name) =>
    request.Headers.TryGetValues(name, out var values) ? values.Single() : "";

static string ToolText(CallToolResult result) =>
    string.Join("\n", result.Content.OfType<TextContentBlock>().Select(content => content.Text));

static string FindRepoRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "ClockodoMcp.slnx")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    throw new InvalidOperationException("Could not find repository root.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertEqual<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
    }
}

static void AssertThrows<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static async Task AssertThrowsAsync<TException>(Func<Task> action)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

internal sealed class CapturingHandler(HttpResponseMessage response) : HttpMessageHandler
{
    public HttpRequestMessage? Request { get; private set; }

    public string? Body { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Request = request;

        if (request.Content is not null)
        {
            Body = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        return response;
    }
}

internal sealed class CapturedHttpRequest
{
    public required string Method { get; init; }

    public required string Path { get; init; }

    public required Dictionary<string, string> Headers { get; init; }
}

internal sealed class OneShotHttpServer : IAsyncDisposable
{
    private readonly TcpListener listener;
    private readonly string responseBody;
    private readonly Task serverTask;
    private readonly TaskCompletionSource<CapturedHttpRequest> request = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public OneShotHttpServer(string responseBody)
    {
        this.responseBody = responseBody;
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        BaseUri = new Uri($"http://127.0.0.1:{port}/api/");
        serverTask = RunAsync();
    }

    public Uri BaseUri { get; }

    public Task<CapturedHttpRequest> Request => request.Task;

    public async ValueTask DisposeAsync()
    {
        listener.Stop();

        try
        {
            await serverTask;
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException)
        {
        }
    }

    private async Task RunAsync()
    {
        using var tcpClient = await listener.AcceptTcpClientAsync();
        await using var stream = tcpClient.GetStream();

        var buffer = new byte[16_384];
        var length = await stream.ReadAsync(buffer);
        var rawRequest = Encoding.ASCII.GetString(buffer, 0, length);
        request.SetResult(ParseRequest(rawRequest));

        var bodyBytes = Encoding.UTF8.GetBytes(responseBody);
        var responseHeader = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: application/json\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Connection: close\r\n\r\n");

        await stream.WriteAsync(responseHeader);
        await stream.WriteAsync(bodyBytes);
    }

    private static CapturedHttpRequest ParseRequest(string rawRequest)
    {
        var lines = rawRequest.Split("\r\n", StringSplitOptions.None);
        var requestLine = lines[0].Split(' ');
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                break;
            }

            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator > 0)
            {
                headers[line[..separator].Trim().ToLowerInvariant()] = line[(separator + 1)..].Trim();
            }
        }

        return new CapturedHttpRequest
        {
            Method = requestLine[0],
            Path = requestLine[1],
            Headers = headers
        };
    }
}
