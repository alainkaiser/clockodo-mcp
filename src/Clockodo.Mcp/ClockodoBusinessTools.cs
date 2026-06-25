using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Clockodo.Mcp;

public sealed partial class ClockodoTools
{
    [McpServerTool(Name = "clockodo_list_customers", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Lists active Clockodo customers for business users. Use search to find the customer before starting a clock or creating a time entry.")]
    public static Task<string> ListCustomers(
        ClockodoClient client,
        [Description("Optional full-text search over customer data.")]
        string? search = null,
        [Description("Whether to filter for active customers. Defaults to true for day-to-day time tracking.")]
        bool active = true,
        [Description("Optional page number.")]
        int? page = null,
        [Description("Optional page size. Clockodo allows up to 5000 for customers.")]
        int? itemsPerPage = null,
        [Description("Optional Clockodo customer/project scope.")]
        string? scope = null,
        CancellationToken cancellationToken = default)
    {
        var filter = new JsonObject
        {
            ["active"] = active
        };
        AddTextFilter(filter, "fulltext", search);

        return Read(
            client,
            operationId: "getCustomersV3",
            queryJson: BuildListQuery(filter, page, itemsPerPage, scope).ToJsonString(),
            cancellationToken: cancellationToken);
    }

    [McpServerTool(Name = "clockodo_list_projects", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Lists active, incomplete Clockodo projects for business users. Use this to find the project id for clock or time-entry tools.")]
    public static Task<string> ListProjects(
        ClockodoClient client,
        [Description("Optional full-text search over project data.")]
        string? search = null,
        [Description("Optional customer id filter.")]
        int? customersId = null,
        [Description("Whether to filter for active projects. Defaults to true for day-to-day time tracking.")]
        bool active = true,
        [Description("Whether to include only completed projects. Defaults to false for day-to-day time tracking.")]
        bool completed = false,
        [Description("Optional page number.")]
        int? page = null,
        [Description("Optional page size. Clockodo allows up to 5000 for projects.")]
        int? itemsPerPage = null,
        [Description("Optional Clockodo customer/project scope.")]
        string? scope = null,
        CancellationToken cancellationToken = default)
    {
        var filter = new JsonObject
        {
            ["active"] = active,
            ["completed"] = completed
        };
        AddTextFilter(filter, "fulltext", search);
        AddFilter(filter, "customers_id", customersId);

        return Read(
            client,
            operationId: "getProjectsV4",
            queryJson: BuildListQuery(filter, page, itemsPerPage, scope).ToJsonString(),
            cancellationToken: cancellationToken);
    }

    [McpServerTool(Name = "clockodo_list_services", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Lists active Clockodo services/activities for business users. Use this to find the service id for clock or time-entry tools.")]
    public static Task<string> ListServices(
        ClockodoClient client,
        [Description("Optional full-text search over service data.")]
        string? search = null,
        [Description("Whether to filter for active services. Defaults to true for day-to-day time tracking.")]
        bool active = true,
        [Description("Optional page number.")]
        int? page = null,
        [Description("Optional page size. Clockodo allows up to 5000 for services.")]
        int? itemsPerPage = null,
        [Description("Optional Clockodo service scope.")]
        string? scope = null,
        CancellationToken cancellationToken = default)
    {
        var filter = new JsonObject
        {
            ["active"] = active
        };
        AddTextFilter(filter, "fulltext", search);

        return Read(
            client,
            operationId: "getServicesV4",
            queryJson: BuildListQuery(filter, page, itemsPerPage, scope).ToJsonString(),
            cancellationToken: cancellationToken);
    }

    [McpServerTool(Name = "clockodo_get_current_clock", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Gets the currently running Clockodo clock entry, if one exists.")]
    public static Task<string> GetCurrentClock(
        ClockodoClient client,
        [Description("Optional Clockodo user id. Omit for the authenticated user.")]
        int? usersId = null,
        CancellationToken cancellationToken = default)
    {
        var query = new JsonObject();
        AddFilter(query, "users_id", usersId);

        return Read(
            client,
            operationId: "getClockV2",
            queryJson: query.Count == 0 ? null : query.ToJsonString(),
            cancellationToken: cancellationToken);
    }

    [McpServerTool(Name = "clockodo_start_clock", ReadOnly = false, Destructive = true, OpenWorld = false)]
    [Description("Starts the Clockodo running timer for a customer and service. Add project, text, user, and custom start time when needed.")]
    public static Task<string> StartClock(
        ClockodoClient client,
        [Description("Clockodo customer id.")]
        int customersId,
        [Description("Clockodo service/activity id.")]
        int servicesId,
        [Description("Optional Clockodo project id.")]
        int? projectsId = null,
        [Description("Optional Clockodo subproject id.")]
        int? subprojectsId = null,
        [Description("Optional description text for the running entry.")]
        string? text = null,
        [Description("Billable state: 0 = not billable, 1 = billable. Omit to let Clockodo decide.")]
        int? billable = null,
        [Description("Optional ISO 8601 start time, for example 2026-06-25T08:00:00Z.")]
        string? timeSince = null,
        [Description("Optional Clockodo user id. Omit for the authenticated user.")]
        int? usersId = null,
        CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["customers_id"] = customersId,
            ["services_id"] = servicesId
        };
        AddProperty(body, "projects_id", projectsId);
        AddProperty(body, "subprojects_id", subprojectsId);
        AddProperty(body, "text", text);
        AddProperty(body, "billable", billable);
        AddProperty(body, "time_since", timeSince);
        AddProperty(body, "users_id", usersId);

        return Write(
            client,
            operationId: "createClockV2",
            bodyJson: body.ToJsonString(),
            cancellationToken: cancellationToken);
    }

    [McpServerTool(Name = "clockodo_stop_clock", ReadOnly = false, Destructive = true, OpenWorld = false)]
    [Description("Stops the current Clockodo running timer. If clockId is omitted, the tool resolves the current running entry first.")]
    public static async Task<string> StopClock(
        ClockodoClient client,
        [Description("Optional running clock entry id. Omit to stop the authenticated user's current running entry.")]
        long? clockId = null,
        [Description("Optional ISO 8601 stop time, for example 2026-06-25T17:00:00Z.")]
        string? timeUntil = null,
        [Description("Optional away duration in minutes.")]
        int? away = null,
        [Description("Whether Clockodo should start a new clock after stopping this one.")]
        bool? startNew = null,
        [Description("Optional Clockodo user id. Omit for the authenticated user.")]
        int? usersId = null,
        CancellationToken cancellationToken = default)
    {
        clockId ??= ExtractRunningEntryId(await GetCurrentClock(client, usersId, cancellationToken));

        var query = new JsonObject();
        AddProperty(query, "time_until", timeUntil);
        AddProperty(query, "away", away);
        AddProperty(query, "start_new", startNew);
        AddProperty(query, "users_id", usersId);

        return await Write(
            client,
            operationId: "deleteClockByIdV2",
            pathParametersJson: JsonSerializer.Serialize(new { id = clockId.Value }),
            queryJson: query.Count == 0 ? null : query.ToJsonString(),
            cancellationToken: cancellationToken);
    }

    [McpServerTool(Name = "clockodo_update_clock", ReadOnly = false, Destructive = true, OpenWorld = false)]
    [Description("Updates the currently running Clockodo entry's start time or duration.")]
    public static Task<string> UpdateClock(
        ClockodoClient client,
        [Description("Running clock entry id.")]
        long clockId,
        [Description("Optional ISO 8601 start time.")]
        string? timeSince = null,
        [Description("Optional duration in minutes.")]
        int? duration = null,
        [Description("Optional previous start time for conflict handling.")]
        string? timeSinceBefore = null,
        [Description("Optional previous end time for conflict handling.")]
        string? timeUntilBefore = null,
        [Description("Optional previous duration for conflict handling.")]
        int? durationBefore = null,
        CancellationToken cancellationToken = default)
    {
        var body = new JsonObject();
        AddProperty(body, "time_since", timeSince);
        AddProperty(body, "duration", duration);
        AddProperty(body, "time_since_before", timeSinceBefore);
        AddProperty(body, "time_until_before", timeUntilBefore);
        AddProperty(body, "duration_before", durationBefore);
        EnsureNonEmptyBody(body, "Provide at least one clock field to update.");

        return Write(
            client,
            operationId: "updateClockByIdV2",
            pathParametersJson: JsonSerializer.Serialize(new { id = clockId }),
            bodyJson: body.ToJsonString(),
            cancellationToken: cancellationToken);
    }

    [McpServerTool(Name = "clockodo_get_time_entry", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Gets one Clockodo time entry by id.")]
    public static Task<string> GetTimeEntry(
        ClockodoClient client,
        [Description("Clockodo time entry id.")]
        long entryId,
        CancellationToken cancellationToken = default)
    {
        return Read(
            client,
            operationId: "getEntryByIdV2",
            pathParametersJson: JsonSerializer.Serialize(new { id = entryId }),
            cancellationToken: cancellationToken);
    }

    [McpServerTool(Name = "clockodo_create_time_entry", ReadOnly = false, Destructive = true, OpenWorld = false)]
    [Description("Creates a completed Clockodo time entry. Use this when the work has already happened instead of starting the live clock.")]
    public static Task<string> CreateTimeEntry(
        ClockodoClient client,
        [Description("Clockodo customer id.")]
        int customersId,
        [Description("Billable state: 0 = not billable, 1 = billable.")]
        int billable,
        [Description("Optional ISO 8601 start time.")]
        string? timeSince = null,
        [Description("Optional ISO 8601 end time.")]
        string? timeUntil = null,
        [Description("Optional duration in minutes.")]
        int? duration = null,
        [Description("Optional Clockodo project id.")]
        int? projectsId = null,
        [Description("Optional Clockodo subproject id.")]
        int? subprojectsId = null,
        [Description("Optional Clockodo service/activity id.")]
        int? servicesId = null,
        [Description("Optional entry text.")]
        string? text = null,
        [Description("Optional Clockodo user id. Omit for the authenticated user.")]
        int? usersId = null,
        CancellationToken cancellationToken = default)
    {
        var body = BuildEntryBody(customersId, billable, timeSince, timeUntil, duration, projectsId, subprojectsId, servicesId, text, usersId);

        return Write(
            client,
            operationId: "createEntryV2",
            bodyJson: body.ToJsonString(),
            cancellationToken: cancellationToken);
    }

    [McpServerTool(Name = "clockodo_update_time_entry", ReadOnly = false, Destructive = true, OpenWorld = false)]
    [Description("Updates an existing Clockodo time entry. Pass only the fields that should change.")]
    public static Task<string> UpdateTimeEntry(
        ClockodoClient client,
        [Description("Clockodo time entry id.")]
        long entryId,
        [Description("Optional Clockodo customer id.")]
        int? customersId = null,
        [Description("Optional billable state: 0 = not billable, 1 = billable, 2 = billed, 12 = billable or billed.")]
        int? billable = null,
        [Description("Optional ISO 8601 start time.")]
        string? timeSince = null,
        [Description("Optional ISO 8601 end time.")]
        string? timeUntil = null,
        [Description("Optional duration in minutes.")]
        int? duration = null,
        [Description("Optional Clockodo project id.")]
        int? projectsId = null,
        [Description("Optional Clockodo subproject id.")]
        int? subprojectsId = null,
        [Description("Optional Clockodo service/activity id.")]
        int? servicesId = null,
        [Description("Optional entry text.")]
        string? text = null,
        [Description("Optional Clockodo user id.")]
        int? usersId = null,
        CancellationToken cancellationToken = default)
    {
        var body = new JsonObject();
        AddProperty(body, "customers_id", customersId);
        AddProperty(body, "billable", billable);
        AddProperty(body, "time_since", timeSince);
        AddProperty(body, "time_until", timeUntil);
        AddProperty(body, "duration", duration);
        AddProperty(body, "projects_id", projectsId);
        AddProperty(body, "subprojects_id", subprojectsId);
        AddProperty(body, "services_id", servicesId);
        AddProperty(body, "text", text);
        AddProperty(body, "users_id", usersId);
        EnsureNonEmptyBody(body, "Provide at least one time-entry field to update.");

        return Write(
            client,
            operationId: "updateEntryByIdV2",
            pathParametersJson: JsonSerializer.Serialize(new { id = entryId }),
            bodyJson: body.ToJsonString(),
            cancellationToken: cancellationToken);
    }

    [McpServerTool(Name = "clockodo_delete_time_entry", ReadOnly = false, Destructive = true, OpenWorld = false)]
    [Description("Deletes a Clockodo time entry. Use only after the user explicitly confirms deletion.")]
    public static Task<string> DeleteTimeEntry(
        ClockodoClient client,
        [Description("Clockodo time entry id.")]
        long entryId,
        CancellationToken cancellationToken = default)
    {
        return Write(
            client,
            operationId: "deleteEntryByIdV2",
            pathParametersJson: JsonSerializer.Serialize(new { id = entryId }),
            cancellationToken: cancellationToken);
    }

    private static JsonObject BuildListQuery(JsonObject filter, int? page, int? itemsPerPage, string? scope)
    {
        var query = new JsonObject
        {
            ["filter"] = filter
        };
        AddProperty(query, "page", page);
        AddProperty(query, "items_per_page", itemsPerPage);
        AddProperty(query, "scope", scope);
        return query;
    }

    private static JsonObject BuildEntryBody(
        int customersId,
        int billable,
        string? timeSince,
        string? timeUntil,
        int? duration,
        int? projectsId,
        int? subprojectsId,
        int? servicesId,
        string? text,
        int? usersId)
    {
        var body = new JsonObject
        {
            ["customers_id"] = customersId,
            ["billable"] = billable
        };
        AddProperty(body, "time_since", timeSince);
        AddProperty(body, "time_until", timeUntil);
        AddProperty(body, "duration", duration);
        AddProperty(body, "projects_id", projectsId);
        AddProperty(body, "subprojects_id", subprojectsId);
        AddProperty(body, "services_id", servicesId);
        AddProperty(body, "text", text);
        AddProperty(body, "users_id", usersId);
        return body;
    }

    private static long ExtractRunningEntryId(string clockJson)
    {
        using var document = JsonDocument.Parse(clockJson);
        var root = document.RootElement;

        if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
        {
            var status = root.TryGetProperty("status", out var statusElement)
                ? statusElement.GetInt32().ToString(CultureInfo.InvariantCulture)
                : "unknown";
            throw new McpException($"Could not resolve current Clockodo clock; /v2/clock returned HTTP {status}.");
        }

        if (root.TryGetProperty("body", out var body) &&
            body.TryGetProperty("running", out var running) &&
            running.ValueKind == JsonValueKind.Object &&
            running.TryGetProperty("id", out var id) &&
            id.TryGetInt64(out var clockId))
        {
            return clockId;
        }

        throw new McpException("No running Clockodo clock entry was found.");
    }

    private static void EnsureNonEmptyBody(JsonObject body, string message)
    {
        if (body.Count == 0)
        {
            throw new McpException(message);
        }
    }

    private static void AddProperty(JsonObject obj, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            obj[name] = value;
        }
    }

    private static void AddProperty(JsonObject obj, string name, int? value)
    {
        if (value is not null)
        {
            obj[name] = value;
        }
    }

    private static void AddProperty(JsonObject obj, string name, long? value)
    {
        if (value is not null)
        {
            obj[name] = value;
        }
    }

    private static void AddProperty(JsonObject obj, string name, bool? value)
    {
        if (value is not null)
        {
            obj[name] = value;
        }
    }

    private static void AddTextFilter(JsonObject filter, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            filter[name] = value;
        }
    }
}
