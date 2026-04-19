using System;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests.Composites;

/// <summary>
/// Regression tests for https://github.com/JasperFx/marten/issues/4093.
///
/// When multiple single-stream projections run inside the same composite group,
/// a stream-archival call (from an <see cref="Archived"/> event) must only be
/// issued by the child projection that actually owns the stream — i.e., the one
/// with a snapshot for the id. Sibling projections that don't own the stream
/// must not issue redundant stream-archival operations.
///
/// Note: whether Archived *creates* or *mutates* a projected document is a
/// separate concern — it is driven entirely by the user-defined Create/Apply
/// handlers on the projection. Archiving a stream and deleting the projected
/// document are independent operations; see the "manually delete any projected
/// aggregates" note in docs/events/archiving.md.
/// </summary>
public class Bug_4093_archived_in_composite_projections : DaemonContext
{
    public Bug_4093_archived_in_composite_projections(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task owning_child_archives_stream_non_owning_child_is_a_noop()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsGuid;

            opts.Projections.CompositeProjectionFor("B4093Ownership", x =>
            {
                x.Add<Bug4093FooProjection>();
                x.Add<Bug4093BazProjection>();
            });
        }, true);

        var fooStreamId = Guid.NewGuid();
        var bazStreamId = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            // Foo stream with Archived at end — Foo should archive
            session.Events.StartStream<Bug4093FooDoc>(fooStreamId, new Bug4093FooStarted(), new Archived("done"));

            // Baz stream with only its own events — Baz should have a doc, stream should NOT be archived
            session.Events.StartStream<Bug4093BazDoc>(bazStreamId, new Bug4093BazStarted());

            await session.SaveChangesAsync();
        }

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(10.Seconds());

        await using var query = theStore.QuerySession();

        // Foo's doc exists and its stream is archived.
        var foo = await query.LoadAsync<Bug4093FooDoc>(fooStreamId);
        foo.ShouldNotBeNull();

        // Baz's doc exists for its own stream; it should NOT have a doc for the Foo stream.
        var baz = await query.LoadAsync<Bug4093BazDoc>(bazStreamId);
        baz.ShouldNotBeNull();

        var phantomBaz = await query.LoadAsync<Bug4093BazDoc>(fooStreamId);
        phantomBaz.ShouldBeNull(
            "Baz does not own the Foo stream; it must not have a doc under that id");
    }
}

// ─────────────────────────── fixtures ───────────────────────────

public record Bug4093FooStarted;
public record Bug4093BazStarted;

public class Bug4093FooDoc
{
    public Guid Id { get; set; }
}

public class Bug4093BazDoc
{
    public Guid Id { get; set; }
}

public class Bug4093FooProjection : SingleStreamProjection<Bug4093FooDoc, Guid>
{
    public Bug4093FooDoc Create(Bug4093FooStarted _) => new();
}

public class Bug4093BazProjection : SingleStreamProjection<Bug4093BazDoc, Guid>
{
    public Bug4093BazDoc Create(Bug4093BazStarted _) => new();
}
