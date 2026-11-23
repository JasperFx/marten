using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten;
using Marten.Metadata;
using Marten.Schema;
using Weasel.Postgresql;

namespace CoreTests.MigrationGuideSamples;

// Compile-only samples for docs/migration-guide.md. Nothing here runs as a test.

public class User
{
    public Guid Id { get; set; }
    public string UserName { get; set; }
    public bool Active { get; set; }
}

public class Reservation: IRevisioned
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public int Version { get; set; }
}

public class Order
{
    public Guid Id { get; set; }
    public string CustomerEmail { get; set; }
}

public static class migration_guide_documentation_samples
{
    public static async Task synchronous_linq_operators_become_asynchronous(IQuerySession session)
    {
        #region sample_migration_10_asynchronous_linq_operators

        var users = await session.Query<User>().Where(x => x.Active).ToListAsync();
        var first = await session.Query<User>().FirstAsync(x => x.UserName == "jdm");
        var count = await session.Query<User>().CountAsync();

        await foreach (var user in session.Query<User>().ToAsyncEnumerable())
        {
            Console.WriteLine(user.UserName);
        }

        #endregion
    }

    public static async Task get_connection_async(IDocumentSession session)
    {
        #region sample_migration_10_get_connection_async

        var connection = await session.GetConnectionAsync();
        var count = await connection
            .CreateCommand("select count(*) from tasks where status = 'open'")
            .ExecuteScalarAsync();

        await session.BeginTransactionAsync(CancellationToken.None);

        #endregion
    }

    public static async Task restore_a_working_write_of_a_revisioned_document(IDocumentSession session, Guid id)
    {
        #region sample_migration_10_store_a_revisioned_document

        var doc = await session.LoadAsync<Reservation>(id);
        doc.Name = "changed";

        // Name the revision the document becomes. The optimistic check still applies
        session.UpdateRevision(doc, doc.Version + 1);

        // ...or opt out of the check and let Marten assign the next revision
        doc.Version = 0;
        session.Store(doc);

        #endregion
    }

    public static void second_index_over_the_same_member(StoreOptions opts)
    {
        #region sample_migration_10_second_index_over_the_same_member

        opts.Schema.For<Order>().Index(x => x.CustomerEmail);
        opts.Schema.For<Order>().Index(x => x.CustomerEmail, x =>
        {
            x.Casing = ComputedIndex.Casings.Lower;
            x.Name = "idx_order_customer_email_lower";
        });

        #endregion
    }

    public static void turn_on_document_metadata(StoreOptions opts)
    {
        #region sample_migration_10_turn_on_document_metadata

        opts.Policies.ForAllDocuments(x =>
        {
            x.Metadata.CausationId.Enabled = true;
            x.Metadata.CorrelationId.Enabled = true;
            x.Metadata.LastModifiedBy.Enabled = true;
            x.Metadata.Headers.Enabled = true;
        });

        #endregion
    }

    public static void turn_on_event_metadata(StoreOptions opts)
    {
        #region sample_migration_10_turn_on_event_metadata

        opts.Events.MetadataConfig.CorrelationIdEnabled = true;
        opts.Events.MetadataConfig.CausationIdEnabled = true;

        // Only if you want the session's LastModifiedBy on every event
        opts.Events.MetadataConfig.UserNameEnabled = true;

        #endregion
    }
}
