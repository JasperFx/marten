using System;
using EventSourcingTests.Projections;
using JasperFx.Events;
using Marten;
using Marten.EventStorage.Dialects;
using Marten.Events;
using Marten.Exceptions;
using Marten.Services;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests.Bugs;

/// <summary>
///     #5473. On the rich append path a lost optimistic-concurrency race is detected by the database
///     — the event row insert violates the <c>(stream_id, version)</c> unique key on
///     <c>mt_events</c>. The only translation used to be the store-GLOBAL transform, which sees the
///     <see cref="PostgresException" /> and nothing else, so it reconstructed the stream id by regex
///     over <c>Detail</c> — a field Npgsql redacts unless the connection string carries
///     <c>Include Error Detail=true</c>, which a production connection string generally should not —
///     and it could never populate <c>AggregateType</c> at all, because it never had the
///     <see cref="StreamAction" />.
///     <para>
///         weasel#596 added <c>TransformAppendEventException</c> to <c>RichEventStorageDescriptor</c>,
///         so the dialect can now translate with the stream in hand. These facts drive the closure
///         the dialect actually installs, rather than a copy of it.
///     </para>
/// </summary>
public class Bug_5473_rich_append_concurrency_exception: OneOffConfigurationsContext
{
    private const string RedactionSentinel = "Detail redacted as it may contain sensitive data. " +
        "Specify 'Include Error Detail' in the connection string to include this information.";

    private const string PostgresText = "duplicate key value violates unique constraint";

    private static PostgresException VersionCollision(string? detail,
        string constraintName = "pk_mt_events_stream_and_version") =>
        new(PostgresText, "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation, detail: detail,
            constraintName: constraintName, tableName: "mt_events");

    private Func<Exception, StreamAction, Exception?> richTransform()
    {
        StoreOptions(opts => opts.Events.AppendMode = EventAppendMode.Rich);

        var descriptor = new PostgresEventStoreDialect()
            .BuildRichDescriptor(theStore.Options.EventGraph, theStore.Options.Serializer());

        return descriptor.TransformAppendEventException
            .ShouldNotBeNull("the rich descriptor must install the #5473 append transform");
    }

    private static StreamAction StreamFor(Guid id, long expectedVersion)
    {
        var stream = StreamAction.Append(id, Array.Empty<IEvent>());
        stream.AggregateType = typeof(QuestParty);
        stream.ExpectedVersionOnServer = expectedVersion;
        return stream;
    }

    /// <summary>
    ///     The whole point of the issue: with <c>Detail</c> redacted — the production default — the
    ///     exception is still fully populated, because none of it came from the detail.
    /// </summary>
    [Fact]
    public void redacted_detail_still_yields_the_id_and_the_aggregate_type()
    {
        var transform = richTransform();
        var streamId = Guid.NewGuid();

        var ex = transform(VersionCollision(RedactionSentinel), StreamFor(streamId, 4))
            .ShouldBeOfType<EventStreamUnexpectedMaxEventIdException>();

        ex.Id.ShouldBe(streamId);
        ex.AggregateType.ShouldBe(typeof(QuestParty));
        ex.Message.ShouldContain(streamId.ToString());
        ex.Message.ShouldContain("expected 4");
        ex.Message.ShouldNotContain(PostgresText);
    }

    /// <summary>
    ///     AggregateType was null even when the detail WAS available, because the global transform
    ///     never had the StreamAction to read it from.
    /// </summary>
    [Fact]
    public void aggregate_type_is_populated_even_when_the_detail_is_available()
    {
        var transform = richTransform();
        var streamId = Guid.NewGuid();

        var ex = transform(VersionCollision($"Key (stream_id, version)=({streamId}, 5) already exists."),
                StreamFor(streamId, 4))
            .ShouldBeOfType<EventStreamUnexpectedMaxEventIdException>();

        ex.AggregateType.ShouldBe(typeof(QuestParty));
        ex.Id.ShouldBe(streamId);
        // `actual` is the one value that still has to come from the detail.
        ex.Message.ShouldContain("but was 5");
    }

    [Fact]
    public void a_MartenCommandException_wrapper_is_unwrapped()
    {
        var transform = richTransform();
        var streamId = Guid.NewGuid();

        var wrapped = new MartenCommandException(new NpgsqlCommand("insert into mt_events"),
            VersionCollision(RedactionSentinel));

        transform(wrapped, StreamFor(streamId, 1))
            .ShouldBeOfType<EventStreamUnexpectedMaxEventIdException>()
            .Id.ShouldBe(streamId);
    }

    /// <summary>
    ///     #5270's lesson, re-pinned on the new path: the OTHER unique index on mt_events is over
    ///     <c>id</c>, and a duplicate event id is not a concurrency conflict. Returning null leaves
    ///     the original exception for the rest of the chain.
    /// </summary>
    [Fact]
    public void unrelated_violations_are_left_alone()
    {
        var transform = richTransform();
        var stream = StreamFor(Guid.NewGuid(), 1);

        transform(VersionCollision(null, "mt_events_default_id_idx"), stream).ShouldBeNull();
        transform(VersionCollision(null, "pk_mt_doc_user"), stream).ShouldBeNull();
        transform(new Exception("not a postgres exception"), stream).ShouldBeNull();
    }

    /// <summary>
    ///     The partitioned child index names, which is where #3520 and #5270 both went wrong.
    /// </summary>
    [Theory]
    [InlineData("pk_mt_events_stream_and_version")]
    [InlineData("mt_events_default_stream_id_version_is_archived_idx")]
    [InlineData("mt_events_default_tenant_id_stream_id_version_is_archived_idx")]
    [InlineData("mt_events_archived_tenant_id_stream_id_version_is_archived_idx")]
    public void every_shape_of_the_version_guard_index_is_transformed(string constraintName)
    {
        var transform = richTransform();

        transform(VersionCollision(RedactionSentinel, constraintName), StreamFor(Guid.NewGuid(), 1))
            .ShouldBeOfType<EventStreamUnexpectedMaxEventIdException>();
    }
}
