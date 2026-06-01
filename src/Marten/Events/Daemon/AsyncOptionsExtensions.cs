#nullable enable
using JasperFx.Events.Projections;
using Marten.Internal.Operations;
using Weasel.Core;

namespace Marten.Events.Daemon;

internal static class AsyncOptionsExtensions
{
    public static void Teardown(this AsyncOptions options, IDocumentOperations session)
    {
        foreach (var cleanUp in options.CleanUps)
        {
            if (cleanUp is DeleteDocuments documents)
            {
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
    public static void TeardownForTenant(this AsyncOptions options, IDocumentOperations session, string tenantId)
    {
        foreach (var cleanUp in options.CleanUps)
        {
            if (cleanUp is DeleteDocuments documents)
            {
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
