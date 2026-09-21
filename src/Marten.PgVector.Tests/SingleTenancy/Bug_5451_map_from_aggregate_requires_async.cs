using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace Marten.PgVector.Tests.SingleTenancy;

/// <summary>
///     #5451. <see cref="Projection.VectorProjection{TId}" />'s class remarks have always said that
///     <c>MapFromAggregate</c> requires <see cref="ProjectionLifecycle.Async" />, and nothing enforced
///     it — <c>ValidateConfiguration</c> only checked the conjoined <c>tenant_id</c> column.
///
///     <para>
///     What an Inline registration actually did, measured on an unfixed build with the projection
///     below: the first save wrote <b>no row at all</b>, because live aggregation reads committed
///     events and the stream had none yet, so the aggregate was null. The second save wrote
///     <c>"a body"</c> — the state as of the FIRST event, missing the <c>NoteTitled</c> that triggered
///     it. Correct-if-async is <c>"a body"</c> then <c>"a title a body"</c>. So the embedding and
///     <c>content_text</c> lag one change behind for the life of the stream, the content hash records
///     that stale text as current, and nothing fails.
///     </para>
///
///     <para>
///     Fisher and Polecat already refuse any non-Async registration of their vector projections. Marten
///     deliberately allows Inline for event-mapped projections — the class remarks call it "correct,
///     not merely tolerated" — so the refusal here is narrowed to a map that declares an aggregate.
///     </para>
/// </summary>
[Collection("Marten.PgVector")]
public class Bug_5451_map_from_aggregate_requires_async
{
    private const string SchemaName = "pgvector_5451";

    /// <remarks>
    ///     Inline only. <see cref="ProjectionLifecycle.Live" /> is not covered here because it is not
    ///     this class's to refuse: JasperFx's <c>ProjectionGraph.Add</c> already rejects Live for any
    ///     plain <c>IProjection</c> with "Live cannot be used for IProjection". It is worth knowing
    ///     that registering this projection Live through Marten's overload does NOT currently hit that
    ///     guard and is accepted silently — but that is a gap in the registration path for every
    ///     IProjection, not something specific to MapFromAggregate, so it wants its own issue rather
    ///     than being papered over here.
    /// </remarks>
    [Fact]
    public void a_map_from_aggregate_projection_registered_inline_is_refused()
    {
        var ex = Should.Throw<InvalidProjectionException>(() => BuildStore(store =>
        {
            var projection = new NoteAggregateVectorProjection(new CountingEmbeddingProvider());
            store.Projections.Add(projection, ProjectionLifecycle.Inline);
            store.Storage.ExtendedSchemaObjects.Add(projection.BuildTable(SchemaName));
        }));

        // Name the mapping and the lifecycle that fixes it, not just "invalid".
        ex.Message.ShouldContain("MapFromAggregate");
        ex.Message.ShouldContain("ProjectionLifecycle.Async");
    }

    [Fact]
    public void the_same_projection_registered_async_still_builds()
    {
        using var store = BuildStore(opts =>
        {
            var projection = new NoteAggregateVectorProjection(new CountingEmbeddingProvider());
            opts.Projections.Add(projection, ProjectionLifecycle.Async);
            opts.Storage.ExtendedSchemaObjects.Add(projection.BuildTable(SchemaName));
        });

        store.Options.Projections.All.ShouldNotBeEmpty();
    }

    /// <summary>
    ///     The refusal must not widen to the event-mapped shape. That projection takes its text from
    ///     the event it was handed, which Inline has in full, and Marten supports it on purpose.
    /// </summary>
    [Fact]
    public void an_event_mapped_projection_registered_inline_still_builds()
    {
        using var store = BuildStore(opts =>
        {
            var projection = new NoteVectorProjection(new CountingEmbeddingProvider());
            opts.Projections.Add(projection, ProjectionLifecycle.Inline);
            opts.Storage.ExtendedSchemaObjects.Add(projection.BuildTable(SchemaName));
        });

        store.Options.Projections.All.ShouldNotBeEmpty();
    }

    private static DocumentStore BuildStore(System.Action<StoreOptions> configure)
    {
        return DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = SchemaName;
            opts.Events.DatabaseSchemaName = SchemaName;
            opts.AutoCreateSchemaObjects = AutoCreate.None;
            opts.UsePgVector();
            opts.Events.StreamIdentity = StreamIdentity.AsString;

            configure(opts);
        });
    }
}
