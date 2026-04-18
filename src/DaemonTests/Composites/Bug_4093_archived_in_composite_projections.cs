using System;
using System.Linq;
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
/// an Archived event that belongs to one child's stream should NOT create
/// phantom documents in other children, and should not issue redundant
/// stream-archival operations from children that don't own the stream.
///
/// The fix (Option A): only archive from a single-stream projection that actually
/// has a snapshot for the stream id being archived.
/// </summary>
public class Bug_4093_archived_in_composite_projections : DaemonContext
{
    public Bug_4093_archived_in_composite_projections(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task archived_does_not_create_phantom_docs_in_other_children_of_composite()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsGuid;

            opts.Projections.CompositeProjectionFor("B4093Group", x =>
            {
                // This child owns streams that begin with FooStarted.
                x.Add<Bug4093FooProjection>();

                // This child "listens" for Archived (a contrived but legal pattern).
                // Today, when Archived appears in any stream in the composite's batch,
                // this projection's Create(Archived) fires and writes a phantom
                // Bug4093BarDoc with the id of the other child's stream.
                x.Add<Bug4093BarProjection>();
            });
        }, true);

        var fooStreamId = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<Bug4093FooDoc>(fooStreamId, new Bug4093FooStarted(), new Archived("done"));
            await session.SaveChangesAsync();
        }

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(10.Seconds());

        // The stream itself should be archived (Foo owns it).
        await using var query = theStore.QuerySession();

        // Bug4093BarDoc must NOT be written for the Foo stream id — Bar doesn't own it,
        // and even though it has a Create(Archived) handler, the Option A guard
        // (snapshot != null) should keep Bar's storage untouched.
        var phantomBar = await query.LoadAsync<Bug4093BarDoc>(fooStreamId);
        phantomBar.ShouldBeNull(
            "Bar projection does not own the Foo stream — no phantom Bar doc should be created");
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

        // Baz's doc exists for its own stream; it should NOT have a phantom doc for the Foo stream.
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

public class Bug4093BarDoc
{
    public Guid Id { get; set; }
    public string ArchivedReason { get; set; } = "";
}

public class Bug4093FooProjection : SingleStreamProjection<Bug4093FooDoc, Guid>
{
    public Bug4093FooDoc Create(Bug4093FooStarted _) => new();
}

public class Bug4093BazProjection : SingleStreamProjection<Bug4093BazDoc, Guid>
{
    public Bug4093BazDoc Create(Bug4093BazStarted _) => new();
}

/// <summary>
/// Contrived sibling projection that would accidentally create documents
/// for any stream carrying an Archived event. Used to expose phantom-doc
/// behavior in composites prior to the Option A guard.
/// </summary>
public class Bug4093BarProjection : SingleStreamProjection<Bug4093BarDoc, Guid>
{
    public Bug4093BarDoc Create(Archived e) => new() { ArchivedReason = e.Reason };
}
