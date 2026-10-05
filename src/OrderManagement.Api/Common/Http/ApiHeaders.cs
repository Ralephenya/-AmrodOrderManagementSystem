namespace OrderManagement.Api.Common.Http;

public static class ApiHeaders
{
    public const string CorrelationId = "X-Correlation-ID";
    public const string IdempotencyKey = "Idempotency-Key";
    public const string ApiSupportedVersions = "api-supported-versions";
}
