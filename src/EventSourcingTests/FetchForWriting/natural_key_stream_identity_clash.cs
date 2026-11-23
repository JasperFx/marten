using System;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

/// <summary>
/// A natural key of the store's own stream identity type can never be fetched by: a fetch by <c>string</c> on a
/// string-identity store is a fetch by stream key. So it is refused when the store is built. A bare string key
/// on a Guid-identity store keeps working (#5344), as does a wrapper around a string.
/// </summary>
public class natural_key_stream_identity_clash: OneOffConfigurationsContext
{
    [Fact]
    public void a_string_key_on_a_string_identity_store_is_refused()
    {
        var ex = Should.Throw<InvalidProjectionException>(() => DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Snapshot<ClashLabel>(SnapshotLifecycle.Inline);
        }));

        ex.Message.ShouldContain("ClashLabel.Label has type string, the same type as this store's stream identity");
        ex.Message.ShouldContain("Wrap it in a strong-typed identifier");
    }

    [Fact]
    public void a_string_key_on_a_guid_identity_store_is_fine()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.Projections.Snapshot<ClashLabelByGuid>(SnapshotLifecycle.Inline);
        });
    }

    [Fact]
    public async Task a_wrapped_string_key_on_a_string_identity_store_is_fetched_by_key()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Snapshot<ClashWrappedLabel>(SnapshotLifecycle.Inline);
        });

        var streamKey = "stream-" + Guid.NewGuid().ToString("N");
        var label = new ClashLabelValue("L-" + Guid.NewGuid().ToString("N"));
        theSession.Events.StartStream<ClashWrappedLabel>(streamKey, new ClashWrappedLabelCreated(label));
        await theSession.SaveChangesAsync();

        (await theSession.Events.FetchLatest<ClashWrappedLabel, ClashLabelValue>(label))!.Id.ShouldBe(streamKey);
        (await theSession.Events.FetchForWriting<ClashWrappedLabel, ClashLabelValue>(label)).Key.ShouldBe(streamKey);
    }
}

public record ClashLabelCreated(string Label);

public class ClashLabel
{
    public string Id { get; set; } = string.Empty;

    [NaturalKey]
    public string Label { get; set; } = string.Empty;

    [NaturalKeySource]
    public void Apply(ClashLabelCreated e) => Label = e.Label;
}

public class ClashLabelByGuid
{
    public Guid Id { get; set; }

    [NaturalKey]
    public string Label { get; set; } = string.Empty;

    [NaturalKeySource]
    public void Apply(ClashLabelCreated e) => Label = e.Label;
}

public readonly record struct ClashLabelValue(string Value);

public record ClashWrappedLabelCreated(ClashLabelValue Label);

public class ClashWrappedLabel
{
    public string Id { get; set; } = string.Empty;

    [NaturalKey]
    public ClashLabelValue Label { get; set; }

    [NaturalKeySource]
    public void Apply(ClashWrappedLabelCreated e) => Label = e.Label;
}
