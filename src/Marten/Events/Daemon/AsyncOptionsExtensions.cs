#nullable enable
using JasperFx.Events.Projections;
using Marten.Internal.Operations;
using Weasel.Core;

namespace Marten.Events.Daemon;

internal static class AsyncOptionsExtensions
{
    /// <summary>
    /// #5329: a <see cref="DeleteDocuments"/> cleanup names a document TYPE, and resolving that type
    /// to a table only works if the type really is in Marten's document storage. Every aggregate
    /// projection inherits <c>Options.DeleteViewTypeOnTeardown&lt;TDoc&gt;()</c> from
    /// <c>JasperFxAggregationProjectionBase</c>, including ones whose data Marten does not own at all
    /// — an EF Core projection writes to a DbContext-mapped table, and Marten never creates
    /// <c>mt_doc_&lt;tdoc&gt;</c> for it (see
    /// <c>DocumentSessionBase.FetchProjectionStorageAsync</c>, which consults this same registry to
    /// skip <c>EnsureStorageExistsAsync(typeof(TDoc))</c> on the write path).
    ///
    /// <para>
    /// So teardown asks the registry the same question the write path already asks: does this
    /// document type have custom projection storage? If it does, its rows are somewhere else and the
    /// document-table truncate is wrong — it either blows up with <c>42P01</c> under
    /// <see cref="JasperFx.AutoCreate.None"/> (the reported symptom) or silently truncates an empty,
    /// unused table while leaving the real data in place. Such a projection contributes its own
    /// <see cref="DeleteTableData"/> cleanup naming the table it actually writes.
    /// </para>
    ///
    /// <para>
    /// A conventional Marten projection has no entry here, so nothing about its teardown changes.
    /// </para>
    /// </summary>
    private static bool isStoredOutsideMarten(StoreOptions storeOptions, DeleteDocuments cleanup)
    {
        return storeOptions.CustomProjectionStorageProviders.ContainsKey(cleanup.DocumentType);
    }

    public static void Teardown(this AsyncOptions options, IDocumentOperations session, StoreOptions storeOptions)
    {
        foreach (var cleanUp in options.CleanUps)
        {
            if (cleanUp is DeleteDocuments documents)
            {
                if (isStoredOutsideMarten(storeOptions, documents)) continue;

                session.QueueOperation(new TruncateTable(documents.DocumentType));
            }

            if (cleanUp is DeleteTableData tableData)
            {
                session.QueueSqlCommand($"delete from {tableData.TableIdentifier};");
            }
        }
    }

    /// <summary>
    /// #4596 Phase 2c — tenant-scoped counterpart to <see cref="Teardown"/> used
    /// by Marten's per-tenant pre-rebuild reset path. For every cleanup target,
    /// scope the wipe to one tenant's rows instead of TRUNCATEing the whole
    /// table. Required for jasperfx#407 Phase 2b's per-tenant
    /// RebuildProjectionAsync — wiping every tenant's docs would be the exact
    /// cross-tenant corruption the per-tenant rebuild path is designed to avoid.
    /// Assumes the underlying tables carry a <c>tenant_id</c> column (true under
    /// <see cref="Marten.Storage.TenancyStyle.Conjoined"/> or
    /// <c>AllDocumentsAreMultiTenanted*</c> policies, which are the only
    /// configurations compatible with <c>UseTenantPartitionedEvents</c>).
    /// </summary>
    public static void TeardownForTenant(this AsyncOptions options, IDocumentOperations session, string tenantId,
        StoreOptions storeOptions)
    {
        foreach (var cleanUp in options.CleanUps)
        {
            if (cleanUp is DeleteDocuments documents)
            {
                // #5329 — same reasoning as Teardown above: not Marten's table, not Marten's to wipe.
                if (isStoredOutsideMarten(storeOptions, documents)) continue;

                session.QueueOperation(new DeleteAllForTenant(documents.DocumentType, tenantId));
            }

            if (cleanUp is DeleteTableData tableData)
            {
                session.QueueOperation(new DeleteAllForTenant(tableData.TableIdentifier, tenantId));
            }
        }
    }


    /// <summary>
    ///     Add an explicit teardown rule to wipe data in the named table
    ///     when this projection shard is rebuilt
    /// </summary>
    /// <param name="name"></param>
    public static void DeleteDataInTableOnTeardown(this AsyncOptions options, DbObjectName name)
    {
        options.DeleteDataInTableOnTeardown(name.QualifiedName);
    }

}
