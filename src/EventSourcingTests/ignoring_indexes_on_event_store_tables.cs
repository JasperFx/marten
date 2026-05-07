using System;
using System.Linq;
using Marten;
using Marten.Events;
using Marten.Events.Schema;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

public class ignoring_indexes_on_event_store_tables
{
    [Fact]
    public void ignored_index_is_propagated_to_events_table()
    {
        var graph = new StoreOptions().EventGraph;
        graph.IgnoreIndex("custom_idx");

        var table = new EventsTable(graph);
        table.IgnoredIndexes.ShouldContain("custom_idx");
    }

    [Fact]
    public void ignored_index_is_propagated_to_streams_table()
    {
        var graph = new StoreOptions().EventGraph;
        graph.IgnoreIndex("custom_idx");

        var table = new StreamsTable(graph);
        table.IgnoredIndexes.ShouldContain("custom_idx");
    }

    [Fact]
    public void ignored_index_is_propagated_to_event_progression_table()
    {
        var graph = new StoreOptions().EventGraph;
        graph.IgnoreIndex("custom_idx");

        var table = new EventProgressionTable(graph);
        table.IgnoredIndexes.ShouldContain("custom_idx");
    }

    [Fact]
    public void ignore_index_through_configuration()
    {
        #region sample_event_store_ignore_index
        var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.Events.IgnoreIndex("mt_events_headers_gin_idx");
        });
        #endregion

        store.Options.Events.IgnoredIndexes.ShouldContain("mt_events_headers_gin_idx");
    }

    [Fact]
    public void ignore_index_is_idempotent()
    {
        var graph = new StoreOptions().EventGraph;
        graph.IgnoreIndex("custom_idx");
        graph.IgnoreIndex("custom_idx");

        graph.IgnoredIndexes.Count(x => x == "custom_idx").ShouldBe(1);
    }

    [Fact]
    public void ignore_index_returns_options_for_chaining()
    {
        var graph = new StoreOptions().EventGraph;
        var result = graph.IgnoreIndex("custom_idx");

        result.ShouldBeSameAs(graph);
    }

    [Fact]
    public void ignore_index_rejects_blank_names()
    {
        var graph = new StoreOptions().EventGraph;
        Should.Throw<ArgumentException>(() => graph.IgnoreIndex(""));
        Should.Throw<ArgumentException>(() => graph.IgnoreIndex("   "));
    }
}
