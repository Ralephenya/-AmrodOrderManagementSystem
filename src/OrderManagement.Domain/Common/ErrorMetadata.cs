namespace OrderManagement.Domain.Common;

/// <summary>
/// Metadata attached to domain errors so the API can point a message at the request field that caused it
/// (ProblemDetails <c>errors</c> dictionary, and form fields in the UI).
/// </summary>
public static class ErrorMetadata
{
    public const string FieldKey = "field";

    public static Dictionary<string, object> ForField(string field) => new() { [FieldKey] = field };
}
