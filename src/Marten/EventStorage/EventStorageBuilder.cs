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
/// Picks ONE concrete subclass per <c>DocumentStore</c>; no per-call
/// append-mode branching after startup.
/// </summary>
internal static class EventStorageBuilder
{
    public static EventStorage<TId> Build<TId>(EventGraph graph, ISerializer serializer)
    {
        // Postgres is the only dialect Marten ships. Polecat will inject its
        // SQL-Server dialect after W2 cuts the JasperFx.Storage repo.
        IEventStoreSqlDialect dialect = new PostgresEventStoreDialect();

        // The dialect owns descriptor construction end-to-end — SQL strings
        // and metadata-binder ordering are joint concerns it builds in
        // lockstep.
        return graph.AppendMode switch
        {
            EventAppendMode.Rich =>
                new RichEventStorage<TId>(dialect.BuildRichDescriptor(graph, serializer)),

            EventAppendMode.Quick =>
                new QuickEventStorage<TId>(dialect.BuildQuickDescriptor(graph, serializer)),

            EventAppendMode.QuickWithServerTimestamps =>
                new QuickWithServerTimestampsEventStorage<TId>(
                    dialect.BuildQuickWithServerTimestampsDescriptor(graph, serializer)),

            _ => throw new ArgumentOutOfRangeException(nameof(graph),
                $"Unsupported EventAppendMode for the closed-shape event-storage hierarchy: {graph.AppendMode}.")
        };
    }
}
