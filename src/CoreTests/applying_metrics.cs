using System.Linq;
using Marten;
using Marten.Services;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests;

public class applying_metrics
{
    [Fact]
    public void applies_the_event_counters_by_default()
    {
        // marten.event.append is exported by default since 10.0 (#5812)
        using var store = DocumentStore.For(ConnectionSource.ConnectionString);
        store.Options.Listeners.Single().ShouldBeOfType<MartenCommitMetrics>();
    }

    [Fact]
    public void should_not_apply_commit_metrics_when_the_event_counters_are_off()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.OpenTelemetry.EnableEventCounters = false;
        });

        store.Options.Listeners.ShouldBeEmpty();
    }

    [Fact]
    public void should_apply_commit_metrics_if_any()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.OpenTelemetry.ExportCounterOnChangeSets<long>("fake", "events", (counter, commit) =>
            {
                counter.Add(commit.GetEvents().Count());
            });
        });

        store.Options.Listeners.ShouldContain(x => x is MartenCommitMetrics);
    }
}
