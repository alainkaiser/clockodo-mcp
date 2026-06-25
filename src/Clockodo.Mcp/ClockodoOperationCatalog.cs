namespace Clockodo.Mcp;

public static partial class ClockodoOperationCatalog
{
    private static readonly Lazy<IReadOnlyDictionary<string, ClockodoOperation>> ByOperationId =
        new(() => All.ToDictionary(operation => operation.OperationId, StringComparer.OrdinalIgnoreCase));

    public static IReadOnlyList<ClockodoOperation> Active => All.Where(operation => !operation.Deprecated).ToArray();

    public static ClockodoOperation? FindByOperationId(string operationId) =>
        ByOperationId.Value.TryGetValue(operationId, out var operation) ? operation : null;

    public static ClockodoOperation? Match(string method, string concretePath) =>
        Active.FirstOrDefault(operation =>
            string.Equals(operation.Method, method, StringComparison.OrdinalIgnoreCase) &&
            MatchesTemplate(operation.Path, concretePath));

    private static bool MatchesTemplate(string template, string path)
    {
        var templateParts = template.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var pathParts = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (templateParts.Length != pathParts.Length)
        {
            return false;
        }

        for (var i = 0; i < templateParts.Length; i++)
        {
            if (IsPlaceholder(templateParts[i]))
            {
                continue;
            }

            if (!string.Equals(templateParts[i], pathParts[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPlaceholder(string part) => part.Length > 2 && part[0] == '{' && part[^1] == '}';
}
