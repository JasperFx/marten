using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten;
using Marten.Events.Rewriting;
using Marten.Storage;

namespace EventSourcingTests.Rewriting;

/// <summary>
///     The code samples in docs/events/rewriting.md. Compiled only, never run: the behavior they show is covered by
///     the other tests in this folder.
/// </summary>
public static class rewriting_documentation_samples
{
    public record PersonRegistered(string FullName);

    public record PersonMoved(string City);

    public record ClaimImported(string ClaimNumber);

    public static async Task rewrite_one_event_type(IDocumentStore store)
    {
        #region sample_rewriting_one_event_type

        var result = await store.Advanced.RewriteStoredEventsAsync(x =>
        {
            // Every event read as PersonRegistered, including those an upcaster reads from an older name
            x.IncludeType<PersonRegistered>();
        });

        #endregion
    }

    public static async Task correct_events_with_a_transform(IDocumentStore store)
    {
        #region sample_rewriting_with_a_transform

        await store.Advanced.RewriteStoredEventsAsync(x =>
            x.Transform<PersonMoved>(e => e with { City = e.City.ToUpperInvariant() }));

        #endregion
    }

    public static async Task only_events_stored_as_json(IDocumentStore store)
    {
        #region sample_rewriting_only_events_stored_as_json

        await store.Advanced.RewriteStoredEventsAsync(x =>
        {
            x.IncludeType<PersonRegistered>();
            x.OnlyEventsStoredAsJson();
        });

        #endregion
    }

    public static async Task throttle_the_rewrite(IDocumentStore store)
    {
        #region sample_rewriting_throttling

        await store.Advanced.RewriteStoredEventsAsync(x =>
        {
            x.IncludeType<ClaimImported>();

            // Events read per page and written back in one statement
            x.BatchSize = 500;

            // Databases rewritten at the same time, for a multi-database store
            x.MaxParallelDatabases = 1;

            // Room for the application between pages
            x.PauseBetweenBatches = 100.Milliseconds();
        });

        #endregion
    }

    public static async Task dry_run(IDocumentStore store)
    {
        #region sample_rewriting_dry_run

        var result = await store.Advanced.RewriteStoredEventsAsync(x =>
        {
            x.IncludeType<ClaimImported>();
            x.DryRun = true;
        });

        foreach (var database in result.Databases)
        {
            Console.WriteLine($"{database.Cursor.Database}: {database.Rewritten} of {database.Visited} events, " +
                              $"{database.BytesBefore} -> {database.BytesAfter} bytes");
        }

        #endregion
    }

    public static async Task one_tenant_or_one_database(IDocumentStore store, IMartenDatabase database)
    {
        #region sample_rewriting_one_tenant_or_one_database

        await store.Advanced.RewriteStoredEventsAsync(x =>
        {
            x.IncludeType<ClaimImported>();
            x.ForTenant("tenant-b");
        });

        await store.Advanced.RewriteStoredEventsAsync(x =>
        {
            x.IncludeType<ClaimImported>();
            x.ForDatabase(database.Identifier);
        });

        #endregion
    }

    private static Task saveCursorAsync(StoredEventRewriteCursor cursor, CancellationToken token) => Task.CompletedTask;

    public static async Task resume_a_rewrite(IDocumentStore store, StoredEventRewriteCursor lastCursor,
        CancellationToken cancellationToken)
    {
        #region sample_rewriting_resuming

        await store.Advanced.RewriteStoredEventsAsync(x =>
        {
            x.IncludeType<ClaimImported>();
            x.ResumeFrom(lastCursor);
            x.OnProgress = (progress, ct) => saveCursorAsync(progress.Cursor, ct);
        }, cancellationToken);

        #endregion
    }

    public static async Task stop_after_a_ceiling(IDocumentStore store)
    {
        #region sample_rewriting_stop_after

        var ceiling = await store.Storage.Database.FetchHighestEventSequenceNumber();

        await store.Advanced.RewriteStoredEventsAsync(x =>
        {
            x.Transform<PersonMoved>(e => e with { City = e.City.ToUpperInvariant() });
            x.StopAfter(ceiling);
        });

        #endregion
    }

    public static void register_a_named_transform(StoreOptions opts)
    {
        #region sample_rewriting_named_transform

        opts.Events.AddRewriteTransform<PersonMoved>("upper-case-city",
            e => e with { City = e.City.ToUpperInvariant() });

        #endregion
    }
}
