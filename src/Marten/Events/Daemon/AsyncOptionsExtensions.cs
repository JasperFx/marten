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
    ///     Add an explicit teardown rule to wipe data in the named table
    ///     when this projection shard is rebuilt
    /// </summary>
    /// <param name="name"></param>
    public static void DeleteDataInTableOnTeardown(this AsyncOptions options, DbObjectName name)
    {
        options.DeleteDataInTableOnTeardown(name.QualifiedName);
    }

}
