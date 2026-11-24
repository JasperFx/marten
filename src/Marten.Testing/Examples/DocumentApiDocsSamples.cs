using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using Marten.Events;
using Marten.Internal;
using Marten.Linq.CreatedAt;
using Marten.Schema;
using Marten.Services;
using Microsoft.Extensions.Hosting;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Storage;

namespace Marten.Testing.Examples;

// Compiled samples for the document pages of the 10.0 docs (concurrency, metadata, identity,
// sessions, storing, testing/integration). These methods are never run as tests; they only
// have to compile so the code shown in the docs cannot drift from the API.

public class ApiDocsCoffeeShop
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class ApiDocsOrder
{
    public Guid Id { get; set; }
    public string Customer { get; set; } = string.Empty;
}

public class ApiDocsTally
{
    public string Id { get; set; } = string.Empty;
    public int Count { get; set; }
}

public record ApiDocsCounted;

#region sample_docs_metadata_attributes

[SoftDeleted]
public class ApiDocsInvoice
{
    public Guid Id { get; set; }

    // Filled in from mt_created_at. Mapping the member turns the column on
    [CreatedAtMetadata]
    public DateTimeOffset CreatedAt { get; set; }

    // Filled in from mt_last_modified
    [LastModifiedMetadata]
    public DateTimeOffset LastModified { get; set; }

    // Filled in from mt_deleted. [SoftDeletedMetadata] is the correctly named form of the
    // older [IsSoftDeletedMetadataAttribute], which still works
    [SoftDeletedMetadata]
    public bool IsDeleted { get; set; }

    // Filled in from mt_deleted_at
    [SoftDeletedAtMetadata]
    public DateTimeOffset? DeletedAt { get; set; }
}

// The tenant id column only exists on a multi-tenanted document type
[MultiTenanted]
public class ApiDocsTenantedInvoice
{
    public Guid Id { get; set; }

    // Filled in from tenant_id when the document is loaded
    [TenantIdMetadata]
    public string TenantId { get; set; } = string.Empty;
}

#endregion

#region sample_docs_index_created_at_via_attribute

// [IndexedCreatedAt] turns the opt-in mt_created_at column on as well
[IndexedCreatedAt]
public class ApiDocsAuditEntry
{
    public Guid Id { get; set; }

    [CreatedAtMetadata]
    public DateTimeOffset CreatedAt { get; set; }
}

#endregion

#region sample_docs_custom_storage_operation

// Deletes every ApiDocsOrder that has not been modified since the cutoff, as one more
// statement in the SaveChangesAsync() batch
public class PurgeStaleOrders(DateTimeOffset cutoff): Marten.Internal.Operations.IStorageOperation, NoDataReturnedCall
{
    public void ConfigureCommand(Weasel.Postgresql.ICommandBuilder builder, IStorageSession session)
    {
        var table = session.StorageFor(DocumentType).TableName.QualifiedName;
        builder.Append($"delete from {table} where mt_last_modified < ");
        builder.AppendParameter(cutoff);
    }

    // Marten makes sure the table for this document type exists before the batch runs
    public Type DocumentType => typeof(ApiDocsOrder);

    public OperationRole Role() => OperationRole.Other;

    // A delete returns no result set, which NoDataReturnedCall tells the batch reader
    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        => Task.CompletedTask;
}

#endregion

public static class DocumentApiDocsSamples
{
    public static async Task disable_concurrency_checks(IDocumentStore store, ApiDocsCoffeeShop shop)
    {
        #region sample_docs_disable_concurrency_checks_for_a_session

        await using var session = store.LightweightSession(new SessionOptions
        {
            ConcurrencyChecks = ConcurrencyChecks.Disabled
        });

        // IDocumentSession.Concurrency reports what the session was opened with
        Console.WriteLine(session.Concurrency); // Disabled

        // Overwrites the stored document, whatever version or revision it is at now
        session.Store(shop);
        await session.SaveChangesAsync();

        #endregion
    }

    public static async Task update_expected_version(IDocumentStore store, ApiDocsCoffeeShop shop,
        Guid versionTheClientLoaded)
    {
        #region sample_docs_update_expected_version

        await using var session = store.LightweightSession();

        shop.Name = "Mozart's";

        // Typically the version came back to you from a client, an ETag for example,
        // or from session.MetadataForAsync(shop) when the document was loaded
        session.UpdateExpectedVersion(shop, versionTheClientLoaded);

        // Throws ConcurrencyException if mt_version is no longer versionTheClientLoaded
        await session.SaveChangesAsync();

        #endregion
    }

    public static async Task created_at_queries()
    {
        #region sample_docs_created_at_queries

        using var store = DocumentStore.For(opts =>
        {
            opts.Connection("some connection string");

            // mt_created_at is opt in
            opts.Schema.For<ApiDocsOrder>().Metadata(m => m.CreatedAt.Enabled = true);
        });

        await using var session = store.QuerySession();

        var oneHourAgo = DateTimeOffset.UtcNow.AddHours(-1);
        var oneDayAgo = DateTimeOffset.UtcNow.AddDays(-1);

        // Orders created between one day and one hour ago
        var orders = await session.Query<ApiDocsOrder>()
            .Where(x => x.CreatedSince(oneDayAgo))
            .Where(x => x.CreatedBefore(oneHourAgo))
            .ToListAsync();

        #endregion
    }

    public static void index_created_at()
    {
        #region sample_docs_index_created_at_via_fluent_interface

        using var store = DocumentStore.For(opts =>
        {
            opts.Connection("some connection string");

            opts.Schema.For<ApiDocsOrder>()
                // IndexCreatedAt() turns the opt-in mt_created_at column on as well
                .IndexCreatedAt();
        });

        #endregion
    }

    public static void hilo_max_advance_attempts()
    {
        #region sample_docs_hilo_max_advance_to_next_hi_attempts

        using var store = DocumentStore.For(opts =>
        {
            opts.Connection("some connection string");

            // The default is 30
            opts.Advanced.HiloSequenceDefaults.MaxAdvanceToNextHiAttempts = 50;
        });

        #endregion
    }

    public static async Task session_options_helpers(IDocumentStore store, IDocumentSessionListener auditListener)
    {
        #region sample_docs_session_options_helpers

        var options = new SessionOptions
            {
                // Open a session for the default tenant even when
                // StoreOptions.Advanced.DefaultTenantUsageEnabled is false
                AllowAnyTenant = true
            }
            // Same as setting SessionOptions.Tracking
            .WithTracking(DocumentTracking.IdentityOnly)
            // A listener for this session only, on top of the store-wide StoreOptions.Listeners
            .ListenAt(auditListener);

        await using var session = store.OpenSession(options);

        #endregion
    }

    public static async Task eject_all_of_type(IDocumentStore store, ApiDocsOrder order, ApiDocsCoffeeShop shop)
    {
        #region sample_docs_eject_all_of_type

        await using var session = store.IdentitySession();

        session.Store(order);
        session.Store(shop);

        // Forget every ApiDocsOrder: out of the identity map, and the pending
        // store/insert/update operations for that type are dropped
        session.EjectAllOfType(typeof(ApiDocsOrder));

        // Only the coffee shop is written
        await session.SaveChangesAsync();

        #endregion
    }

    public static async Task insert_objects(IDocumentStore store)
    {
        #region sample_docs_insert_objects

        var documents = new object[]
        {
            new ApiDocsOrder { Customer = "Wanda" },
            new ApiDocsCoffeeShop { Name = "Dominican Joe's" }
        };

        await using var session = store.LightweightSession();

        // Insert() for a mixed bag of document types: SaveChangesAsync() throws
        // DocumentAlreadyExistsException if any of them already exists
        session.InsertObjects(documents);
        await session.SaveChangesAsync();

        #endregion
    }

    public static async Task queue_operation(IDocumentStore store, ApiDocsOrder order)
    {
        #region sample_docs_queue_operation

        await using var session = store.LightweightSession();

        session.Store(order);
        session.QueueOperation(new PurgeStaleOrders(DateTimeOffset.UtcNow.AddYears(-1)));

        // Both run in the same batch and the same transaction
        await session.SaveChangesAsync();

        #endregion
    }

    public static async Task bulk_insert_enlist_transaction(IDocumentStore store, IReadOnlyCollection<ApiDocsOrder> orders)
    {
        #region sample_docs_bulk_insert_enlist_transaction

        using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            // Other work enlisted in the same ambient transaction goes here

            await store.BulkInsertEnlistTransactionAsync(orders, Transaction.Current!);

            // Nothing is committed unless the scope completes
            scope.Complete();
        }

        #endregion
    }

    public static async Task force_daemon_catch_up(IHost host)
    {
        #region sample_docs_force_all_daemon_activity_to_catch_up

        // Run every asynchronous projection and subscription up to the current
        // high-water mark, then let the daemon carry on as normal
        var errors = await host.ForceAllMartenDaemonActivityToCatchUpAsync(CancellationToken.None);
        if (errors.Any())
        {
            throw new AggregateException(errors);
        }

        // Or catch up, and leave the daemons stopped so nothing moves while the test asserts
        await host.ForceAllMartenDaemonActivityToCatchUpAsync(CancellationToken.None, CatchUpMode.AndDoNothing);

        // ... assertions ...

        // Start them again
        await host.ResumeAllDaemonsAsync();

        #endregion
    }

    public static async Task wait_for_own_events(IHost host)
    {
        #region sample_docs_wait_for_projections_in_a_test

        var store = host.DocumentStore();

        await using var session = store.LightweightSession();
        session.Events.StartStream(Guid.NewGuid(), new ApiDocsCounted());
        await session.SaveChangesAsync();

        // Wait for this commit's events only, not for everything in the event store
        var result = await store.WaitForProjectionsAsync(session.LastAppendedPosition!, [typeof(ApiDocsTally)],
            TimeSpan.FromSeconds(10));

        if (!result.Reached)
        {
            throw new TimeoutException(result.ToString());
        }

        #endregion
    }

    public static async Task pause_and_resume_daemons(IHost host)
    {
        #region sample_docs_pause_and_resume_daemons

        // Stop every async projection and subscription, and stop the coordinator
        // from restarting them
        await host.PauseAllDaemonsAsync();

        // ... append events and assert on the state before any projection sees them ...

        await host.ResumeAllDaemonsAsync();

        #endregion
    }
}
