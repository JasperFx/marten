#nullable enable
using System;
using JasperFx.Events;
using Marten.EventStorage.Dialects;
using Marten.EventStorage.Quick;
using Marten.EventStorage.QuickWithServerTimestamps;
using Marten.EventStorage.Rich;
using Marten.Events;
using Marten.Services;

namespace Marten.EventStorage;

/// <summary>
/// Factory for the per-mode <see cref="EventStorage{TId}"/> subclass that
/// matches an <see cref="EventGraph"/>'s <see cref="EventAppendMode"/>.
/// One concrete subclass + descriptor built once at <c>DocumentStore</c>
/// construction; no per-call append-mode branching after startup.
/// </summary>
internal static class EventStorageBuilder
{
    public static EventStorage<TId> Build<TId>(EventGraph graph, ISerializer serializer)
    {
        // Postgres is the only dialect Marten ships. Polecat will inject its
        // SQL-Server dialect after W2 cuts the JasperFx.Storage repo.
        var dialect = new PostgresEventStoreDialect();

        // EventAppendMode (JasperFx.Events) has three values; Rich covers
        // both the Full and QuickWithVersion sub-variants the codegen path
        // emits separately (those are Marten-internal Schema.AppendMode
        // values, not user-facing). RichEventStorage exposes both via
        // AppendEvent + QuickAppendEventWithVersion.
        return graph.AppendMode switch
        {
            EventAppendMode.Rich =>
                new RichEventStorage<TId>(BuildRichDescriptor(graph, dialect, serializer)),

            EventAppendMode.Quick =>
                new QuickEventStorage<TId>(BuildQuickDescriptor(graph, dialect, serializer)),

            EventAppendMode.QuickWithServerTimestamps =>
                new QuickWithServerTimestampsEventStorage<TId>(BuildQuickWithServerTimestampsDescriptor(graph, dialect, serializer)),

            _ => throw new ArgumentOutOfRangeException(nameof(graph),
                $"Unsupported EventAppendMode for the closed-shape event-storage hierarchy: {graph.AppendMode}.")
        };
    }

    internal static RichEventStorageDescriptor BuildRichDescriptor(
        EventGraph graph, IEventStoreSqlDialect dialect, ISerializer serializer)
        => new(
            appendEventSqlPrefix: dialect.AppendEventFullPrefix(graph),
            appendEventSqlSuffix: dialect.AppendEventFullSuffix(graph),
            insertStreamSql: dialect.InsertStream(graph),
            updateStreamVersionSql: dialect.UpdateStreamVersion(graph),
            streamStateSelectSql: dialect.StreamStateSelect(graph),
            serializeEventData: e => serializer.ToJson(e.Data),
            metadataBinders: BuildRichMetadataBinders(graph));

    internal static QuickEventStorageDescriptor BuildQuickDescriptor(
        EventGraph graph, IEventStoreSqlDialect dialect, ISerializer serializer)
        => new(
            quickAppendEventsSql: dialect.QuickAppendEvents(graph),
            insertStreamSql: dialect.InsertStream(graph),
            updateStreamVersionSql: dialect.UpdateStreamVersion(graph),
            streamStateSelectSql: dialect.StreamStateSelect(graph),
            serializeEventData: e => serializer.ToJson(e.Data));

    internal static QuickWithServerTimestampsEventStorageDescriptor BuildQuickWithServerTimestampsDescriptor(
        EventGraph graph, IEventStoreSqlDialect dialect, ISerializer serializer)
        => new(
            quickAppendEventsWithServerTimestampsSql: dialect.QuickAppendEventsWithServerTimestamps(graph),
            insertStreamSql: dialect.InsertStream(graph),
            updateStreamVersionSql: dialect.UpdateStreamVersion(graph),
            streamStateSelectSql: dialect.StreamStateSelect(graph),
            serializeEventData: e => serializer.ToJson(e.Data));

    private static IEventMetadataBinder[] BuildRichMetadataBinders(EventGraph graph)
    {
        // Spike-era assembly; full configuration-axis matrix lands as
        // operations get filled in. Order of the binders here MUST match
        // the metadata-column ordering in the Rich SQL prefix produced by
        // <see cref="IEventStoreSqlDialect.AppendEventFullPrefix"/>.
        var list = new System.Collections.Generic.List<IEventMetadataBinder>(8)
        {
            new Marten.EventStorage.Metadata.SequenceColumnBinder(),
        };

        // TODO (#4410): conditionally include
        //   new HeadersColumnBinder() — when graph.Metadata.Headers.Enabled
        //   new CausationIdColumnBinder() — when graph.Metadata.CausationId.Enabled
        //   new CorrelationIdColumnBinder()
        //   new UserNameColumnBinder()
        //   new TagsColumnBinder() — HSTORE; Postgres-specific
        // Once those binders land, this method becomes a configuration-
        // axis branch instead of a fixed list.
        list.Add(new Marten.EventStorage.Metadata.HeadersColumnBinder());

        return list.ToArray();
    }
}
