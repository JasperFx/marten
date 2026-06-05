#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Storage;
using Npgsql;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// Shared projection-safe load implementation for closed-shape document
/// storages (#4667 Phase 2). Opens a fresh connection from the
/// <see cref="IMartenDatabase"/>, executes the load SQL, and deserializes
/// the data column directly via <see cref="ISerializer"/> — without any
/// <see cref="IMartenSession"/> reference, without writing to
/// <c>VersionTracker</c> / <c>ItemMap</c> / <c>ChangeTrackers</c>, and
/// without calling <c>MarkAsDocumentLoaded</c>.
/// </summary>
/// <remarks>
/// <para>
/// The projection-safe selector path intentionally skips metadata binders
/// (CreatedAt / LastModified / Headers / etc.). Projections care about the
/// aggregate state encoded in the data column for their Apply/Evolve hot
/// path; per-row metadata is not part of that contract. If a future
/// projection scenario needs metadata it can be added here as a focused
/// follow-up.
/// </para>
/// <para>
/// Hierarchical storages dispatch deserialization through the
/// <see cref="DocumentMapping.TypeFor"/> alias-to-.NET-type lookup,
/// mirroring <see cref="HierarchicalClosedShapeQueryOnlySelector{T,TId}"/>.
/// </para>
/// </remarks>
internal static class ClosedShapeProjectionLoader<TDoc, TId>
    where TDoc : notnull
    where TId : notnull
{
    // Column layout matches the writeable closed-shape selectors
    // (Lightweight / IdentityMap / DirtyChecked) since LoadProjectedAsync is
    // only reached from those storages — see
    // <see cref="ClosedShapeLightweightSelector{T,TId}"/>. QueryOnly storage
    // has a different layout (id excluded, data at col 0) but doesn't
    // implement LoadProjectedAsync; it throws NotSupportedException instead.
    private const int IdColumn = 0;
    private const int DataColumn = 1;
    private const int FirstMetadataColumn = 2;

    public static async Task<TDoc?> LoadAsync(
        NpgsqlCommand command,
        DocumentStorageDescriptor<TDoc, TId> descriptor,
        ISerializer serializer,
        IMartenDatabase database,
        CancellationToken token)
    {
        await using var conn = database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);
        try
        {
            command.Connection = conn;
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
            {
                return default;
            }

            return await readOneAsync(reader, descriptor, serializer, token).ConfigureAwait(false);
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    public static async Task<IReadOnlyList<TDoc>> LoadManyAsync(
        NpgsqlCommand command,
        DocumentStorageDescriptor<TDoc, TId> descriptor,
        ISerializer serializer,
        IMartenDatabase database,
        CancellationToken token)
    {
        await using var conn = database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);
        try
        {
            command.Connection = conn;
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            var list = new List<TDoc>();
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var doc = await readOneAsync(reader, descriptor, serializer, token).ConfigureAwait(false);
                if (doc is not null)
                {
                    list.Add(doc);
                }
            }
            return list;
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    private static async ValueTask<TDoc> readOneAsync(
        DbDataReader reader,
        DocumentStorageDescriptor<TDoc, TId> descriptor,
        ISerializer serializer,
        CancellationToken token)
    {
        // Hierarchical: dispatch via mt_doc_type alias just like
        // HierarchicalClosedShapeQueryOnlySelector. Flat: straight deserialize.
        if (descriptor.HierarchyMapping is { } hierarchy)
        {
            var docTypeOrdinal = FirstMetadataColumn + descriptor.DocTypeReadIndex;
            var alias = await reader.GetFieldValueAsync<string>(docTypeOrdinal, token).ConfigureAwait(false);
            return (TDoc)await serializer.FromJsonAsync(hierarchy.TypeFor(alias), reader, DataColumn, token).ConfigureAwait(false);
        }

        return await serializer.FromJsonAsync<TDoc>(reader, DataColumn, token).ConfigureAwait(false);
    }
}
