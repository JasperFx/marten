#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImTools;
using JasperFx;
using JasperFx.Core.Reflection;
using Marten.Internal.ClosedShape;
using Marten.Internal.Storage;
using Marten.Storage;
using Npgsql;
using Weasel.Core;
using Weasel.Storage;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Events.Daemon.Internals;

/// <summary>
///     #4685 PR 3 — carrier for a document insert that is eligible for the BulkWriter
///     (binary <c>COPY</c>) rebuild flush path. Created by
///     <see cref="Marten.Internal.Sessions.DocumentSessionBase"/> instead of posting the raw
///     insert operation when the session's work tracker is a <see cref="ProjectionUpdateBatch"/>
///     that accepts bulk-copy inserts (<see cref="ProjectionOptions.RebuildWithBulkCopy"/> on a
///     <c>Rebuild</c>-mode batch). Captures the pieces the COPY flush needs that the operation
///     itself doesn't expose — the document instance, its registered document type, and the
///     originating session's tenant id — while keeping the original operation around so the
///     batch can fall back to the ordinary per-row command path at any point
///     (<see cref="RebuildBulkCopyBuffer.Demote"/>).
/// </summary>
internal sealed class BulkCopyableInsert: Weasel.Storage.IStorageOperation
{
    public BulkCopyableInsert(Weasel.Storage.IStorageOperation inner, object document, Type documentType,
        string tenantId)
    {
        Inner = inner;
        Document = document;
        DocumentType = documentType;
        TenantId = tenantId;
    }

    /// <summary>The original insert operation, untouched, for the per-row fallback path.</summary>
    public Weasel.Storage.IStorageOperation Inner { get; }

    public object Document { get; }

    public string TenantId { get; }

    /// <summary>
    ///     The document type as registered with the session (matters for hierarchical storage:
    ///     this is the subclass type, and the provider graph resolves the subclass bulk loader
    ///     that writes <c>mt_doc_type</c> per row).
    /// </summary>
    public Type DocumentType { get; }

    public OperationRole Role() => OperationRole.Insert;

    // The wrapper itself never lands on an OperationPage — ProjectionUpdateBatch either buffers
    // it for the COPY flush or unwraps Inner onto the page. These delegations exist only so the
    // wrapper is a well-behaved IStorageOperation while it travels the batch's queue.
    public void ConfigureCommand(Weasel.Core.ICommandBuilder builder, IStorageSession session)
        => Inner.ConfigureCommand(builder, session);

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        => Inner.PostprocessAsync(reader, exceptions, token);
}

/// <summary>
///     #4685 PR 3 — accumulates <see cref="BulkCopyableInsert"/> operations for a single
///     rebuild-mode <see cref="ProjectionUpdateBatch"/> and flushes them through PostgreSQL's
///     binary <c>COPY</c> protocol inside the batch's transaction. Participates in the batch
///     transaction via <see cref="ITransactionParticipant"/>: the connection lifetimes invoke
///     <see cref="BeforeCommitAsync"/> after the batch's command pages execute and before the
///     transaction commits, so the COPY is atomic with the progression update — a failed
///     rebuild cannot leak partially-copied rows.
/// </summary>
/// <remarks>
///     Not thread safe by design: all mutation happens on the batch's single queue-consumer
///     task (<c>ProjectionUpdateBatch.Queue</c> is a single-reader <c>Block</c>), and
///     <see cref="BeforeCommitAsync"/> runs after <c>WaitForCompletion</c> has drained that
///     queue.
/// </remarks>
[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "CloseAndBuildAs over document types that are preserved at the StoreOptions / projection-registration boundary per the AOT publishing guide.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Type.MakeGenericType over registered document types — AOT consumers pre-generate codegen artifacts per the AOT publishing guide.")]
internal sealed class RebuildBulkCopyBuffer: ITransactionParticipant
{
    private readonly IMartenDatabase _database;
    private readonly ISerializer _serializer;
    private readonly List<BulkCopyableInsert> _inserts = new();
    private ImHashMap<Type, IRebuildBulkCopier?> _copiers = ImHashMap<Type, IRebuildBulkCopier?>.Empty;

    public RebuildBulkCopyBuffer(IMartenDatabase database, ISerializer serializer)
    {
        _database = database;
        _serializer = serializer;
    }

    /// <summary>
    ///     Once true, the batch has seen a non-insert document operation and every previously
    ///     buffered insert has drained back to the per-row path. No further buffering happens
    ///     for the lifetime of the batch — mirroring the monotonic
    ///     <see cref="BatchFlushModeClassifier"/> transition, but at document-operation
    ///     granularity so the ever-present progression write (role <c>Events</c>) doesn't
    ///     disqualify the batch.
    /// </summary>
    public bool Demoted { get; private set; }

    public int Count => _inserts.Count;

    /// <summary>
    ///     Buffer the insert for the COPY flush. Returns false when the document type has no
    ///     usable bulk loader (e.g. a document shape outside the COPY machinery), in which case
    ///     the caller sends the inner operation down the ordinary per-row path.
    /// </summary>
    public bool TryBuffer(BulkCopyableInsert insert)
    {
        if (copierFor(insert.DocumentType) is null)
        {
            return false;
        }

        _inserts.Add(insert);
        return true;
    }

    /// <summary>
    ///     Graceful degradation (#4685): a non-insert document operation arrived, so the
    ///     insert-only premise no longer holds for this batch. Returns the buffered inserts in
    ///     their original arrival order so the batch can append them to its command pages
    ///     <i>before</i> the demoting operation, preserving the original operation order.
    /// </summary>
    public IReadOnlyList<BulkCopyableInsert> Demote()
    {
        Demoted = true;

        if (_inserts.Count == 0)
        {
            return Array.Empty<BulkCopyableInsert>();
        }

        var drained = _inserts.ToArray();
        _inserts.Clear();
        return drained;
    }

    public async Task BeforeCommitAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        CancellationToken token)
    {
        if (Demoted || _inserts.Count == 0)
        {
            return;
        }

        // One COPY stream per (document type, tenant) group. GroupBy preserves the arrival
        // order of documents within each group; order across groups is irrelevant because
        // each group targets its own table/tenant rows and everything commits atomically.
        foreach (var group in _inserts.GroupBy(x => (x.DocumentType, x.TenantId)))
        {
            var copier = copierFor(group.Key.DocumentType)!;
            var tenant = new Tenant(group.Key.TenantId, _database);

            await copier.CopyAsync(group.Select(x => x.Document), tenant, _serializer, connection, token)
                .ConfigureAwait(false);
        }
    }

    private IRebuildBulkCopier? copierFor(Type documentType)
    {
        if (_copiers.TryFind(documentType, out var copier))
        {
            return copier;
        }

        var built = typeof(RebuildBulkCopier<>).CloseAndBuildAs<IRebuildBulkCopier>(documentType);
        copier = built.HasBulkLoader(_database) ? built : null;

        _copiers = _copiers.AddOrUpdate(documentType, copier);
        return copier;
    }
}

internal interface IRebuildBulkCopier
{
    bool HasBulkLoader(IMartenDatabase database);

    Task CopyAsync(IEnumerable<object> documents, Tenant tenant, ISerializer serializer,
        NpgsqlConnection connection, CancellationToken token);
}

/// <summary>
///     Closed over a single document type: resolves the same <c>IBulkLoader</c> the
///     <c>IDocumentStore.BulkInsertAsync</c> pipeline uses (id assignment, tenant id, data,
///     and every metadata write binder — version, last modified, dotnet type, soft-delete
///     columns, duplicated fields) and streams the buffered documents through
///     <c>BeginBinaryImport</c> on the batch's open connection, joining its transaction.
/// </summary>
[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Document types are preserved at the StoreOptions / projection-registration boundary per the AOT publishing guide.")]
internal sealed class RebuildBulkCopier<T>: IRebuildBulkCopier where T : notnull
{
    public bool HasBulkLoader(IMartenDatabase database)
        => database.Providers.StorageFor<T>() is MartenDocumentProvider<T>
        {
            BulkLoader: not SpikeNotImplementedBulkLoader<T>
        };

    public async Task CopyAsync(IEnumerable<object> documents, Tenant tenant, ISerializer serializer,
        NpgsqlConnection connection, CancellationToken token)
    {
        var provider = (MartenDocumentProvider<T>)tenant.Database.Providers.StorageFor<T>();

        try
        {
            await provider.BulkLoader
                .LoadAsync(tenant, serializer, connection, documents.Cast<T>().ToArray(), token)
                .ConfigureAwait(false);
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Mirror the per-row insert path's exception transform so callers see the same
            // exception type for a duplicate id. The COPY protocol reports the violation for
            // the stream as a whole, so the offending id is only available through the
            // PostgreSQL error detail.
            throw new DocumentAlreadyExistsException(e, typeof(T), e.Detail ?? "(bulk copy)");
        }
    }
}
