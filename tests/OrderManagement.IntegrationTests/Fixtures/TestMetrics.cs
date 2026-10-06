using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using OrderManagement.Infrastructure.Observability;

namespace OrderManagement.IntegrationTests.Fixtures;

/// <summary>Real <see cref="OrderMetrics"/> for services built by hand in tests (backed by a throwaway meter factory).</summary>
internal static class TestMetrics
{
    private static readonly IMeterFactory Factory =
        new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>();

    public static OrderMetrics Create() => new(Factory);
}
