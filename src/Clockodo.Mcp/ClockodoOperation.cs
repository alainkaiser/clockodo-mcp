namespace Clockodo.Mcp;

public sealed record ClockodoOperation(
    string OperationId,
    string Method,
    string Path,
    string Tag,
    string? Summary,
    string[] Parameters,
    string ParametersJson,
    bool RequiresBody,
    string? RequestBodyJson,
    bool Deprecated)
{
    public string DocumentationUrl => $"https://docs.clockodo.com/#tag/{Uri.EscapeDataString(Tag)}/operation/{Uri.EscapeDataString(OperationId)}";
}
