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
    public void should_not_apply_commit_metrics_by_default()
    {
        using var store = DocumentStore.For(ConnectionSource.ConnectionString);
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
