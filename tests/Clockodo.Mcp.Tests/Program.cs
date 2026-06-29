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
    ("server info reports catalog and runtime state", ServerInfoReportsCatalogAndRuntimeState),
    ("convenience tools build expected requests", ConvenienceToolsBuildExpectedRequests),
    ("business tools build expected requests", BusinessToolsBuildExpectedRequests),
    ("operation lookup rejects deprecated operation ids", OperationLookupRejectsDeprecatedIds),
    ("blocked operations are hidden and rejected", BlockedOperationsAreHiddenAndRejected),
    ("base url validation rejects unsafe hosts", BaseUrlValidationRejectsUnsafeHosts),
    ("request builds Clockodo headers and deep query", RequestBuildsHeadersAndQuery),
    ("request applies path parameters", RequestAppliesPathParameters),
    ("request sends JSON body", RequestSendsJsonBody),
    ("read and write tools enforce method boundaries", ReadAndWriteToolsEnforceBoundaries),
    ("read-only mode blocks writes", ReadOnlyModeBlocksWrites),
    ("read-only mode blocks business write tools", ReadOnlyModeBlocksBusinessWriteTools),
    ("transport failures surface as MCP errors", TransportFailuresSurfaceAsMcpErrors),
    ("write rejects missing required bodies and empty path params", WriteValidationRejectsInvalidInput),
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
        ClockodoOperationCatalog.All.Count(operation => ClockodoOperationCatalog.IsCallable(operation)),
        ClockodoOperationCatalog.Active.Count,
        "Active operation count must equal all callable operations.");
    Assert(ClockodoOperationCatalog.Active.All(operation => ClockodoOperationCatalog.IsCallable(operation)), "Active catalog must exclude deprecated and blocked operations.");

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

static Task ServerInfoReportsCatalogAndRuntimeState()
{
    var options = new ClockodoOptions(
        ApiUser: null,
        ApiKey: null,
        ExternalApplication: "clockodo-mcp-test;user@example.com",
        AcceptLanguage: "de",
        BaseUrl: new Uri("https://my.clockodo.com/api/"),
        ReadOnly: true);

    var info = ClockodoTools.ServerInfo(options);
    using var document = JsonDocument.Parse(info);
    var root = document.RootElement;

    AssertEqual(ClockodoOperationCatalog.OpenApiVersion, root.GetProperty("openApiVersion").GetString(), "Unexpected OpenAPI version.");
    AssertEqual("https://mcp.clockodo.com/mcp", root.GetProperty("nativeClockodoMcp").GetProperty("endpoint").GetString(), "Unexpected native MCP endpoint.");
    Assert(root.GetProperty("operations").GetProperty("active").GetInt32() > 100, "Expected active operation count.");
    Assert(root.GetProperty("operations").GetProperty("deprecatedHidden").GetInt32() > 0, "Expected hidden deprecated operations.");
    Assert(root.GetProperty("operations").GetProperty("blockedHidden").GetInt32() > 0, "Expected hidden blocked operations.");
    Assert(root.GetProperty("runtime").GetProperty("readOnly").GetBoolean(), "Expected read-only runtime state.");
    Assert(!root.GetProperty("runtime").GetProperty("credentialsConfigured").GetBoolean(), "Server info must not claim missing credentials are configured.");
    Assert(!info.Contains("secret", StringComparison.OrdinalIgnoreCase), "Server info must not contain credential values.");
    Assert(info.Contains("clockodo_get_entries_by_timeframe", StringComparison.Ordinal), "Server info should list convenience tools.");
    Assert(info.Contains("clockodo_start_clock", StringComparison.Ordinal), "Server info should list business tools.");

    return Task.CompletedTask;
}

static async Task ConvenienceToolsBuildExpectedRequests()
{
    var currentTime = ClockodoTools.CurrentTime("Europe/Zurich");
    using (var document = JsonDocument.Parse(currentTime))
    {
        AssertEqual("Europe/Zurich", document.RootElement.GetProperty("timeZone").GetString(), "Unexpected current time zone.");
        Assert(document.RootElement.TryGetProperty("utc", out _), "Current time should include UTC time.");
    }

    var meHandler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent("""{"data":{"id":7}}""", Encoding.UTF8, "application/json")
    });

    await ClockodoTools.Me(CreateClient(meHandler));
    AssertEqual("/api/v4/users/me", meHandler.Request?.RequestUri?.AbsolutePath, "clockodo_me should call /v4/users/me.");

    var absencesHandler = new QueueHandler(
        JsonResponse("""{"data":{"id":7}}"""),
        JsonResponse("""{"data":[]}"""));

    var absencesResult = await ClockodoTools.GetMyAbsences(
        CreateClient(absencesHandler),
        year: 2026,
        status: "approved",
        scope: "extended");

    AssertEqual("/api/v4/users/me", absencesHandler.Requests[0].RequestUri?.AbsolutePath, "clockodo_get_my_absences should resolve the current user first.");
    AssertEqual("/api/v4/absences", absencesHandler.Requests[1].RequestUri?.AbsolutePath, "clockodo_get_my_absences should call /v4/absences.");

    var absencesQuery = Uri.UnescapeDataString(absencesHandler.Requests[1].RequestUri?.Query ?? "");
    Assert(absencesQuery.Contains("filter[users_id]=7", StringComparison.Ordinal), "Expected users_id absence filter.");
    Assert(absencesQuery.Contains("filter[year]=2026", StringComparison.Ordinal), "Expected year absence filter.");
    Assert(absencesQuery.Contains("filter[status]=approved", StringComparison.Ordinal), "Expected status absence filter.");
    Assert(absencesQuery.Contains("scope=extended", StringComparison.Ordinal), "Expected absence scope query.");

    using (var document = JsonDocument.Parse(absencesResult))
    {
        AssertEqual(7L, document.RootElement.GetProperty("usersId").GetInt64(), "Unexpected resolved absence user id.");
        Assert(document.RootElement.GetProperty("clockodo").GetProperty("ok").GetBoolean(), "Expected wrapped absence response.");
    }

    var entriesHandler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent("""{"data":{"entries":[]}}""", Encoding.UTF8, "application/json")
    });

    var entriesResult = await ClockodoTools.GetEntriesByTimeframe(
        CreateClient(entriesHandler),
        timeframe: "custom",
        timeZone: "Europe/Zurich",
        dateSince: "2026-06-01",
        dateUntil: "2026-06-03",
        usersId: 7,
        projectsId: 42,
        itemsPerPage: 100);

    var request = entriesHandler.Request ?? throw new InvalidOperationException("Expected an entries request.");
    var query = Uri.UnescapeDataString(request.RequestUri?.Query ?? "");

    AssertEqual("/api/v2/entries", request.RequestUri?.AbsolutePath, "Unexpected entries path.");
    Assert(query.Contains("time_since=2026-05-31T22:00:00Z", StringComparison.Ordinal), "Expected Europe/Zurich custom start converted to UTC.");
    Assert(query.Contains("time_until=2026-06-03T22:00:00Z", StringComparison.Ordinal), "Expected inclusive custom end converted to next-day UTC.");
    Assert(query.Contains("filter[users_id]=7", StringComparison.Ordinal), "Expected users_id filter.");
    Assert(query.Contains("filter[projects_id]=42", StringComparison.Ordinal), "Expected projects_id filter.");
    Assert(query.Contains("items_per_page=100", StringComparison.Ordinal), "Expected items_per_page query.");

    using (var document = JsonDocument.Parse(entriesResult))
    {
        var root = document.RootElement;
        AssertEqual("custom", root.GetProperty("timeframe").GetString(), "Unexpected timeframe.");
        AssertEqual("2026-06-01", root.GetProperty("dateSince").GetString(), "Unexpected resolved dateSince.");
        AssertEqual("2026-06-03", root.GetProperty("dateUntil").GetString(), "Unexpected resolved dateUntil.");
        Assert(root.GetProperty("clockodo").GetProperty("ok").GetBoolean(), "Expected wrapped Clockodo response.");
    }

    await AssertThrowsAsync<McpException>(() =>
        ClockodoTools.GetEntriesByTimeframe(CreateClient(new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK))), timeframe: "custom"));
}

static async Task BusinessToolsBuildExpectedRequests()
{
    var customersHandler = new CapturingHandler(JsonResponse("""{"data":[]}"""));
    await ClockodoTools.ListCustomers(CreateClient(customersHandler), search: "Acme", itemsPerPage: 50);

    var customersQuery = Uri.UnescapeDataString(customersHandler.Request?.RequestUri?.Query ?? "");
    AssertEqual("/api/v3/customers", customersHandler.Request?.RequestUri?.AbsolutePath, "Unexpected customers path.");
    Assert(customersQuery.Contains("filter[active]=true", StringComparison.Ordinal), "Expected active customer filter.");
    Assert(customersQuery.Contains("filter[fulltext]=Acme", StringComparison.Ordinal), "Expected customer fulltext filter.");
    Assert(customersQuery.Contains("items_per_page=50", StringComparison.Ordinal), "Expected customer page size.");

    var projectsHandler = new CapturingHandler(JsonResponse("""{"data":[]}"""));
    await ClockodoTools.ListProjects(CreateClient(projectsHandler), search: "Relaunch", customersId: 17);

    var projectsQuery = Uri.UnescapeDataString(projectsHandler.Request?.RequestUri?.Query ?? "");
    AssertEqual("/api/v4/projects", projectsHandler.Request?.RequestUri?.AbsolutePath, "Unexpected projects path.");
    Assert(projectsQuery.Contains("filter[active]=true", StringComparison.Ordinal), "Expected active project filter.");
    Assert(projectsQuery.Contains("filter[completed]=false", StringComparison.Ordinal), "Expected incomplete project filter.");
    Assert(projectsQuery.Contains("filter[customers_id]=17", StringComparison.Ordinal), "Expected project customer filter.");

    var servicesHandler = new CapturingHandler(JsonResponse("""{"data":[]}"""));
    await ClockodoTools.ListServices(CreateClient(servicesHandler), search: "Consulting");

    var servicesQuery = Uri.UnescapeDataString(servicesHandler.Request?.RequestUri?.Query ?? "");
    AssertEqual("/api/v4/services", servicesHandler.Request?.RequestUri?.AbsolutePath, "Unexpected services path.");
    Assert(servicesQuery.Contains("filter[active]=true", StringComparison.Ordinal), "Expected active service filter.");
    Assert(servicesQuery.Contains("filter[fulltext]=Consulting", StringComparison.Ordinal), "Expected service fulltext filter.");

    var clockHandler = new CapturingHandler(JsonResponse("""{"running":{"id":99}}"""));
    await ClockodoTools.GetCurrentClock(CreateClient(clockHandler), usersId: 7);

    var clockQuery = Uri.UnescapeDataString(clockHandler.Request?.RequestUri?.Query ?? "");
    AssertEqual("/api/v2/clock", clockHandler.Request?.RequestUri?.AbsolutePath, "Unexpected current clock path.");
    Assert(clockQuery.Contains("users_id=7", StringComparison.Ordinal), "Expected current clock user query.");

    var startClockHandler = new CapturingHandler(JsonResponse("""{"running":{"id":99}}"""));
    await ClockodoTools.StartClock(
        CreateClient(startClockHandler),
        customersId: 10,
        servicesId: 20,
        projectsId: 30,
        text: "Planning",
        billable: 1,
        timeSince: "2026-06-25T08:00:00Z");

    AssertEqual(HttpMethod.Post, startClockHandler.Request?.Method, "Unexpected start clock method.");
    AssertEqual("/api/v2/clock", startClockHandler.Request?.RequestUri?.AbsolutePath, "Unexpected start clock path.");
    Assert(startClockHandler.Body?.Contains("\"customers_id\":10", StringComparison.Ordinal) == true, "Expected start clock customer id.");
    Assert(startClockHandler.Body?.Contains("\"services_id\":20", StringComparison.Ordinal) == true, "Expected start clock service id.");
    Assert(startClockHandler.Body?.Contains("\"projects_id\":30", StringComparison.Ordinal) == true, "Expected start clock project id.");
    Assert(startClockHandler.Body?.Contains("\"time_since\":\"2026-06-25T08:00:00Z\"", StringComparison.Ordinal) == true, "Expected start clock time_since.");

    var stopClockHandler = new QueueHandler(
        JsonResponse("""{"running":{"id":99}}"""),
        JsonResponse("""{"stopped":{"id":99}}"""));

    await ClockodoTools.StopClock(
        CreateClient(stopClockHandler),
        timeUntil: "2026-06-25T09:00:00Z",
        usersId: 7);

    AssertEqual("/api/v2/clock", stopClockHandler.Requests[0].RequestUri?.AbsolutePath, "Stop clock should resolve current clock first.");
    AssertEqual(HttpMethod.Delete, stopClockHandler.Requests[1].Method, "Unexpected stop clock method.");
    AssertEqual("/api/v2/clock/99", stopClockHandler.Requests[1].RequestUri?.AbsolutePath, "Unexpected stop clock path.");

    var stopClockQuery = Uri.UnescapeDataString(stopClockHandler.Requests[1].RequestUri?.Query ?? "");
    Assert(stopClockQuery.Contains("time_until=2026-06-25T09:00:00Z", StringComparison.Ordinal), "Expected stop clock time_until query.");
    Assert(stopClockQuery.Contains("users_id=7", StringComparison.Ordinal), "Expected stop clock users_id query.");

    var updateClockHandler = new CapturingHandler(JsonResponse("""{"updated":{"id":99}}"""));
    await ClockodoTools.UpdateClock(CreateClient(updateClockHandler), clockId: 99, duration: 75);

    AssertEqual(HttpMethod.Put, updateClockHandler.Request?.Method, "Unexpected update clock method.");
    AssertEqual("/api/v2/clock/99", updateClockHandler.Request?.RequestUri?.AbsolutePath, "Unexpected update clock path.");
    Assert(updateClockHandler.Body?.Contains("\"duration\":75", StringComparison.Ordinal) == true, "Expected update clock duration.");

    await AssertThrowsAsync<McpException>(() =>
        ClockodoTools.UpdateClock(CreateClient(new CapturingHandler(JsonResponse("""{"updated":{"id":99}}"""))), clockId: 99));

    var entryHandler = new CapturingHandler(JsonResponse("""{"entry":{"id":123}}"""));
    await ClockodoTools.CreateTimeEntry(
        CreateClient(entryHandler),
        customersId: 10,
        billable: 1,
        duration: 60,
        servicesId: 20,
        text: "Follow-up");

    AssertEqual(HttpMethod.Post, entryHandler.Request?.Method, "Unexpected create entry method.");
    AssertEqual("/api/v2/entries", entryHandler.Request?.RequestUri?.AbsolutePath, "Unexpected create entry path.");
    Assert(entryHandler.Body?.Contains("\"customers_id\":10", StringComparison.Ordinal) == true, "Expected create entry customer id.");
    Assert(entryHandler.Body?.Contains("\"billable\":1", StringComparison.Ordinal) == true, "Expected create entry billable flag.");
    Assert(entryHandler.Body?.Contains("\"duration\":60", StringComparison.Ordinal) == true, "Expected create entry duration.");

    var updateEntryHandler = new CapturingHandler(JsonResponse("""{"entry":{"id":123}}"""));
    await ClockodoTools.UpdateTimeEntry(CreateClient(updateEntryHandler), entryId: 123, text: "Updated", duration: 90);

    AssertEqual(HttpMethod.Put, updateEntryHandler.Request?.Method, "Unexpected update entry method.");
    AssertEqual("/api/v2/entries/123", updateEntryHandler.Request?.RequestUri?.AbsolutePath, "Unexpected update entry path.");
    Assert(updateEntryHandler.Body?.Contains("\"text\":\"Updated\"", StringComparison.Ordinal) == true, "Expected update entry text.");

    await AssertThrowsAsync<McpException>(() =>
        ClockodoTools.UpdateTimeEntry(CreateClient(new CapturingHandler(JsonResponse("""{"entry":{"id":123}}"""))), entryId: 123));

    var deleteEntryHandler = new CapturingHandler(JsonResponse("""{"success":true}"""));
    await ClockodoTools.DeleteTimeEntry(CreateClient(deleteEntryHandler), entryId: 123);

    AssertEqual(HttpMethod.Delete, deleteEntryHandler.Request?.Method, "Unexpected delete entry method.");
    AssertEqual("/api/v2/entries/123", deleteEntryHandler.Request?.RequestUri?.AbsolutePath, "Unexpected delete entry path.");
}

static Task OperationLookupRejectsDeprecatedIds()
{
    AssertThrows<McpException>(() => ClockodoTools.GetOperation("getAggregatesUsersMeV2"));
    return Task.CompletedTask;
}

static Task BaseUrlValidationRejectsUnsafeHosts()
{
    AssertThrows<InvalidOperationException>(() => LoadOptions(new Dictionary<string, string?>
    {
        ["CLOCKODO_BASE_URL"] = "https://evil.example/api/"
    }));

    AssertThrows<InvalidOperationException>(() => LoadOptions(new Dictionary<string, string?>
    {
        ["CLOCKODO_BASE_URL"] = "http://my.clockodo.com/api/"
    }));

    var local = LoadOptions(new Dictionary<string, string?>
    {
        ["CLOCKODO_BASE_URL"] = "http://127.0.0.1:8080/api/"
    });
    AssertEqual("127.0.0.1", local.BaseUrl.Host, "Local test hosts should remain allowed.");

    var production = LoadOptions(new Dictionary<string, string?>
    {
        ["CLOCKODO_BASE_URL"] = "https://my.clockodo.com/api/"
    });
    AssertEqual("my.clockodo.com", production.BaseUrl.Host, "Clockodo production host should remain allowed.");

    var overrideAllowed = LoadOptions(new Dictionary<string, string?>
    {
        ["CLOCKODO_BASE_URL"] = "https://evil.example/api/",
        ["CLOCKODO_BASE_URL_ALLOW_ANY"] = "true"
    });
    AssertEqual("evil.example", overrideAllowed.BaseUrl.Host, "Explicit override should allow custom hosts.");

    return Task.CompletedTask;
}

static ClockodoOptions LoadOptions(Dictionary<string, string?> values)
{
    var previous = new Dictionary<string, string?>();
    foreach (var key in values.Keys.Concat(["CLOCKODO_BASE_URL_ALLOW_ANY"]))
    {
        previous[key] = Environment.GetEnvironmentVariable(key);
    }

    try
    {
        foreach (var (key, value) in values)
        {
            Environment.SetEnvironmentVariable(key, value);
        }

        if (!values.ContainsKey("CLOCKODO_BASE_URL_ALLOW_ANY"))
        {
            Environment.SetEnvironmentVariable("CLOCKODO_BASE_URL_ALLOW_ANY", null);
        }

        return ClockodoOptions.FromEnvironment();
    }
    finally
    {
        foreach (var (key, value) in previous)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}

static async Task BlockedOperationsAreHiddenAndRejected()
{
    Assert(ClockodoOperationCatalog.IsBlocked(ClockodoOperationCatalog.FindByOperationId("createRegister")!), "createRegister should be blocklisted.");
    Assert(!ClockodoOperationCatalog.Active.Any(operation => operation.OperationId == "createRegister"), "createRegister must not appear in active catalog.");

    var operations = ClockodoTools.ListOperations(search: "createRegister");
    Assert(!operations.Contains("createRegister", StringComparison.Ordinal), "Blocked operation must not appear in list results.");

    AssertThrows<McpException>(() => ClockodoTools.GetOperation("createRegister"));

    await AssertThrowsAsync<McpException>(() =>
        ClockodoTools.Write(
            CreateClient(new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK))),
            operationId: "createRegister",
            bodyJson: """{"companies_name":"Acme","name":"Test","email":"test@example.com"}"""));
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

static async Task ReadOnlyModeBlocksBusinessWriteTools()
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
        ClockodoTools.StartClock(client, customersId: 1, servicesId: 2));
}

static async Task TransportFailuresSurfaceAsMcpErrors()
{
    var client = CreateClient(new ThrowingHandler(new HttpRequestException("Network unreachable.")));

    await AssertThrowsAsync<McpException>(() =>
        ClockodoTools.Read(client, operationId: "getUsersMeV4"));
}

static async Task WriteValidationRejectsInvalidInput()
{
    var client = CreateClient(new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)));

    await AssertThrowsAsync<McpException>(() =>
        ClockodoTools.Write(client, operationId: "createServiceV4"));

    await AssertThrowsAsync<McpException>(() =>
        ClockodoTools.Read(
            client,
            operationId: "getServiceByIdV4",
            pathParametersJson: """{"id":""}"""));
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
    var devServer = Path.Combine(repoRoot, "scripts", "run-dev-mcp-server");
    var transport = new StdioClientTransport(new StdioClientTransportOptions
    {
        Name = "clockodo-mcp-test",
        Command = Environment.GetEnvironmentVariable("CLOCKODO_MCP_COMMAND")
            ?? (File.Exists(devServer) ? devServer : Path.Combine(repoRoot, "src", "Clockodo.Mcp", "bin", "Debug", "net10.0", "Clockodo.Mcp")),
        Arguments = [],
        WorkingDirectory = repoRoot,
        InheritEnvironmentVariables = false,
        EnvironmentVariables = env
    });

    await using var client = await McpClient.CreateAsync(transport, cancellationToken: cts.Token);
    var tools = await client.ListToolsAsync(cancellationToken: cts.Token);
    var toolNames = tools.Select(tool => tool.Name).ToArray();

    Assert(toolNames.Contains("clockodo_server_info"), "Missing clockodo_server_info tool.");
    Assert(toolNames.Contains("clockodo_current_time"), "Missing clockodo_current_time tool.");
    Assert(toolNames.Contains("clockodo_me"), "Missing clockodo_me tool.");
    Assert(toolNames.Contains("clockodo_get_my_absences"), "Missing clockodo_get_my_absences tool.");
    Assert(toolNames.Contains("clockodo_get_entries_by_timeframe"), "Missing clockodo_get_entries_by_timeframe tool.");
    Assert(toolNames.Contains("clockodo_list_customers"), "Missing clockodo_list_customers tool.");
    Assert(toolNames.Contains("clockodo_list_projects"), "Missing clockodo_list_projects tool.");
    Assert(toolNames.Contains("clockodo_list_services"), "Missing clockodo_list_services tool.");
    Assert(toolNames.Contains("clockodo_get_current_clock"), "Missing clockodo_get_current_clock tool.");
    Assert(toolNames.Contains("clockodo_start_clock"), "Missing clockodo_start_clock tool.");
    Assert(toolNames.Contains("clockodo_stop_clock"), "Missing clockodo_stop_clock tool.");
    Assert(toolNames.Contains("clockodo_update_clock"), "Missing clockodo_update_clock tool.");
    Assert(toolNames.Contains("clockodo_get_time_entry"), "Missing clockodo_get_time_entry tool.");
    Assert(toolNames.Contains("clockodo_create_time_entry"), "Missing clockodo_create_time_entry tool.");
    Assert(toolNames.Contains("clockodo_update_time_entry"), "Missing clockodo_update_time_entry tool.");
    Assert(toolNames.Contains("clockodo_delete_time_entry"), "Missing clockodo_delete_time_entry tool.");
    Assert(toolNames.Contains("clockodo_list_operations"), "Missing clockodo_list_operations tool.");
    Assert(toolNames.Contains("clockodo_get_operation"), "Missing clockodo_get_operation tool.");
    Assert(toolNames.Contains("clockodo_read"), "Missing clockodo_read tool.");
    Assert(toolNames.Contains("clockodo_write"), "Missing clockodo_write tool.");

    var infoResult = await client.CallToolAsync(
        "clockodo_server_info",
        cancellationToken: cts.Token);

    Assert(ToolText(infoResult).Contains(ClockodoOperationCatalog.OpenApiVersion, StringComparison.Ordinal), "server info call did not return the OpenAPI version.");

    var timeResult = await client.CallToolAsync(
        "clockodo_current_time",
        new Dictionary<string, object?> { ["timeZone"] = "Europe/Zurich" },
        cancellationToken: cts.Token);

    Assert(ToolText(timeResult).Contains("Europe/Zurich", StringComparison.Ordinal), "current time call did not return the requested time zone.");

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

static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
{
    Content = new StringContent(json, Encoding.UTF8, "application/json")
};

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

internal sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> responses = new(responses);

    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        if (responses.Count == 0)
        {
            throw new InvalidOperationException("No queued HTTP response available.");
        }

        return Task.FromResult(responses.Dequeue());
    }
}

internal sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromException<HttpResponseMessage>(exception);
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
