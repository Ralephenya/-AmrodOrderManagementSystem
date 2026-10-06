namespace OrderManagement.IntegrationTests.Fixtures;

/// <summary>Polls until an asynchronous outcome (a consumer commit, an outbox delivery) becomes observable.</summary>
internal static class Eventually
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    public static async Task UntilAsync(Func<Task<bool>> condition, string what, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting for {what}.");
            }

            await Task.Delay(50);
        }
    }

    public static Task UntilAsync(Func<bool> condition, string what, TimeSpan? timeout = null) =>
        UntilAsync(() => Task.FromResult(condition()), what, timeout);
}
