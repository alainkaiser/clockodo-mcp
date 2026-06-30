using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Clockodo.Mcp;

public sealed partial class ClockodoTools
{
    [McpServerTool(Name = "clockodo_current_time", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Returns the current time in UTC and in a requested IANA time zone. Use this before calculating relative Clockodo date ranges.")]
    public static string CurrentTime(
        [Description("Optional IANA time zone id, for example Europe/Zurich. Defaults to the machine's local time zone.")]
        string? timeZone = null)
    {
        var zone = ResolveTimeZone(timeZone);
        var utcNow = DateTimeOffset.UtcNow;
        var localNow = TimeZoneInfo.ConvertTime(utcNow, zone);

        return JsonSerializer.Serialize(new
        {
            utc = utcNow.ToString("O", CultureInfo.InvariantCulture),
            timeZone = zone.Id,
            local = localNow.ToString("O", CultureInfo.InvariantCulture),
            date = DateOnly.FromDateTime(localNow.DateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        }, JsonOptions);
    }

    [McpServerTool(Name = "clockodo_me", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Gets the current authenticated Clockodo user via the current /v4/users/me API operation.")]
    public static Task<string> Me(
        ClockodoClient client,
        CancellationToken cancellationToken = default)
    {
        return Read(client, operationId: "getUsersMeV4", cancellationToken: cancellationToken);
    }

    [McpServerTool(Name = "clockodo_get_my_absences", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Gets absences for the current authenticated Clockodo user by resolving /v4/users/me and filtering /v4/absences by users_id.")]
    public static async Task<string> GetMyAbsences(
        ClockodoClient client,
        [Description("Optional absence year filter.")]
        int? year = null,
        [Description("Optional absence status filter from the Clockodo API, for example approved, requested, declined, or canceled.")]
        string? status = null,
        [Description("Optional absence type filter from the Clockodo API.")]
        string? type = null,
        [Description("Optional absence scope from the Clockodo API.")]
        string? scope = null,
        CancellationToken cancellationToken = default)
    {
        var meJson = await Me(client, cancellationToken);
        var userId = ExtractUserId(meJson);
        var filter = new JsonObject
        {
            ["users_id"] = userId
        };

        if (year is not null)
        {
            filter["year"] = year;
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            filter["status"] = status;
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            filter["type"] = type;
        }

        var query = new JsonObject
        {
            ["filter"] = filter
        };

        if (!string.IsNullOrWhiteSpace(scope))
        {
            query["scope"] = scope;
        }

        var responseJson = await Read(
            client,
            operationId: "getAbsencesV4",
            queryJson: query.ToJsonString(),
            cancellationToken: cancellationToken);

        return JsonSerializer.Serialize(new
        {
            usersId = userId,
            query = JsonNode.Parse(query.ToJsonString()),
            clockodo = JsonNode.Parse(responseJson)
        }, JsonOptions);
    }

    [McpServerTool(Name = "clockodo_get_entries_by_timeframe", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Gets Clockodo time entries for today, yesterday, this_week, last_week, this_month, last_month, or a custom inclusive date range. Weeks start on Monday in the requested time zone.")]
    public static async Task<string> GetEntriesByTimeframe(
        ClockodoClient client,
        [Description("One of today, yesterday, this_week, last_week, this_month, last_month, or custom.")]
        string timeframe,
        [Description("Optional IANA time zone id used to resolve relative date ranges. Defaults to the machine's local time zone.")]
        string? timeZone = null,
        [Description("Required for timeframe=custom. Inclusive start date in YYYY-MM-DD format.")]
        string? dateSince = null,
        [Description("Required for timeframe=custom. Inclusive end date in YYYY-MM-DD format.")]
        string? dateUntil = null,
        [Description("Optional Clockodo user id filter.")]
        int? usersId = null,
        [Description("Optional Clockodo customer id filter.")]
        int? customersId = null,
        [Description("Optional Clockodo project id filter.")]
        int? projectsId = null,
        [Description("Optional Clockodo service id filter.")]
        int? servicesId = null,
        [Description("Optional page number.")]
        int? page = null,
        [Description("Optional page size. Clockodo allows up to 1000 for entries.")]
        int? itemsPerPage = null,
        CancellationToken cancellationToken = default)
    {
        var range = ResolveDateRange(timeframe, timeZone, dateSince, dateUntil);
        var query = new JsonObject
        {
            ["time_since"] = range.TimeSinceUtc,
            ["time_until"] = range.TimeUntilUtc
        };

        if (page is not null)
        {
            query["page"] = page;
        }

        if (itemsPerPage is not null)
        {
            query["items_per_page"] = itemsPerPage;
        }

        var filter = new JsonObject();
        AddFilter(filter, "users_id", usersId);
        AddFilter(filter, "customers_id", customersId);
        AddFilter(filter, "projects_id", projectsId);
        AddFilter(filter, "services_id", servicesId);

        if (filter.Count > 0)
        {
            query["filter"] = filter;
        }

        var responseJson = await Read(
            client,
            operationId: "getEntriesV2",
            queryJson: query.ToJsonString(),
            cancellationToken: cancellationToken);

        return JsonSerializer.Serialize(new
        {
            timeframe = range.Timeframe,
            timeZone = range.TimeZone,
            dateSince = range.DateSince,
            dateUntil = range.DateUntil,
            timeSince = range.TimeSinceUtc,
            timeUntil = range.TimeUntilUtc,
            query = JsonNode.Parse(query.ToJsonString()),
            clockodo = JsonNode.Parse(responseJson)
        }, JsonOptions);
    }

    private static long ExtractUserId(string meJson)
    {
        using var document = JsonDocument.Parse(meJson);
        var root = document.RootElement;

        if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
        {
            var status = root.TryGetProperty("status", out var statusElement)
                ? statusElement.GetInt32().ToString(CultureInfo.InvariantCulture)
                : "unknown";
            throw new McpException($"Could not resolve current Clockodo user; /v4/users/me returned HTTP {status}.");
        }

        if (root.TryGetProperty("body", out var body) &&
            body.TryGetProperty("data", out var data) &&
            data.TryGetProperty("id", out var id) &&
            id.TryGetInt64(out var userId))
        {
            return userId;
        }

        throw new McpException("Could not resolve current Clockodo user id from /v4/users/me.");
    }

    private static void AddFilter(JsonObject filter, string name, int? value)
    {
        if (value is not null)
        {
            filter[name] = value;
        }
    }

    private static TimeZoneInfo ResolveTimeZone(string? timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZone))
        {
            return TimeZoneInfo.Local;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        }
        catch (TimeZoneNotFoundException exception)
        {
            throw new McpException($"Unknown time zone: {timeZone}", exception);
        }
        catch (InvalidTimeZoneException exception)
        {
            throw new McpException($"Invalid time zone: {timeZone}", exception);
        }
    }

    private static DateRange ResolveDateRange(string timeframe, string? timeZone, string? dateSince, string? dateUntil)
    {
        if (string.IsNullOrWhiteSpace(timeframe))
        {
            throw new McpException("timeframe is required.");
        }

        var normalized = timeframe.Trim().ToLowerInvariant().Replace("-", "_", StringComparison.Ordinal);
        var zone = ResolveTimeZone(timeZone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
        DateOnly since;
        DateOnly until;

        switch (normalized)
        {
            case "today":
                since = today;
                until = today;
                break;
            case "yesterday":
                since = today.AddDays(-1);
                until = since;
                break;
            case "this_week":
                since = StartOfWeek(today);
                until = since.AddDays(6);
                break;
            case "last_week":
                since = StartOfWeek(today).AddDays(-7);
                until = since.AddDays(6);
                break;
            case "this_month":
                since = new DateOnly(today.Year, today.Month, 1);
                until = since.AddMonths(1).AddDays(-1);
                break;
            case "last_month":
                since = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
                until = since.AddMonths(1).AddDays(-1);
                break;
            case "custom":
                since = ParseDate(dateSince, nameof(dateSince));
                until = ParseDate(dateUntil, nameof(dateUntil));
                break;
            default:
                throw new McpException("timeframe must be one of today, yesterday, this_week, last_week, this_month, last_month, or custom.");
        }

        if (until < since)
        {
            throw new McpException("dateUntil must be on or after dateSince.");
        }

        return new DateRange(
            normalized,
            zone.Id,
            since.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            until.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ToUtcIso(since, zone),
            ToUtcIso(until.AddDays(1), zone));
    }

    private static DateOnly StartOfWeek(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private static DateOnly ParseDate(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new McpException($"{parameterName} is required when timeframe=custom.");
        }

        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        throw new McpException($"{parameterName} must use YYYY-MM-DD format.");
    }

    private static string ToUtcIso(DateOnly date, TimeZoneInfo zone)
    {
        try
        {
            var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
            var utc = TimeZoneInfo.ConvertTimeToUtc(local, zone);
            return utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidTimeZoneException)
        {
            throw new McpException($"Could not convert {date:yyyy-MM-dd} in time zone {zone.Id} to UTC.", exception);
        }
    }

    private sealed record DateRange(
        string Timeframe,
        string TimeZone,
        string DateSince,
        string DateUntil,
        string TimeSinceUtc,
        string TimeUntilUtc);
}
