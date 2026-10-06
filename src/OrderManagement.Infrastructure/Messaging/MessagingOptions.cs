namespace OrderManagement.Infrastructure.Messaging;

/// <summary>Bound from the <c>Messaging</c> configuration section.</summary>
public sealed class MessagingOptions
{
    public const string SectionName = "Messaging";

    /// <summary>
    /// <see cref="MessagingTransport.RabbitMq"/> in every real environment. <see cref="MessagingTransport.InMemory"/>
    /// keeps a single process working without a broker (local dev without Docker, and tests).
    /// </summary>
    public MessagingTransport Transport { get; init; } = MessagingTransport.RabbitMq;

    public RetryOptions Retry { get; init; } = new();

    /// <summary>How often the outbox delivery service checks for committed-but-unsent messages.</summary>
    public TimeSpan OutboxQueryDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>How long consumed message IDs are remembered for duplicate detection (the inbox).</summary>
    public TimeSpan DuplicateDetectionWindow { get; init; } = TimeSpan.FromHours(24);
}

public enum MessagingTransport
{
    RabbitMq,
    InMemory,
}

/// <summary>Exponential back-off: the delays grow from <see cref="MinInterval"/> towards <see cref="MaxInterval"/>.</summary>
public sealed class RetryOptions
{
    public int Limit { get; init; } = 5;

    public TimeSpan MinInterval { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxInterval { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan IntervalDelta { get; init; } = TimeSpan.FromSeconds(2);
}
