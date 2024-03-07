using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Storage;

namespace Marten.Events;

public static class TestingExtensions
{
    /// <summary>
    /// Wait for any running async daemons to catch up to the latest event sequence at the time
    /// this method is invoked for all projections. This method is meant to aid in automated testing
    /// </summary>
    /// <param name="store"></param>
    /// <param name="timeout"></param>
    public static async Task WaitForNonStaleProjectionDataAsync(this IDocumentStore store, TimeSpan timeout)
    {
        var databases = await store.Storage.AllDatabases().ConfigureAwait(false);

        if (databases.Count == 1)
        {
            await WaitForNonStaleProjectionDataAsync(databases.Single(), timeout).ConfigureAwait(false);
        }
        else
        {
            var tasks = databases.Select(db => db.WaitForNonStaleProjectionDataAsync(timeout));
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }


    /// <summary>
    /// Wait for any running async daemon for a specific tenant id or database name to catch up to the latest event sequence at the time
    /// this method is invoked for all projections. This method is meant to aid in automated testing
    /// </summary>
    /// <param name="store"></param>
    /// <param name="tenantIdOrDatabaseName">Either a tenant id or the name of a database within the system</param>
    /// <param name="timeout"></param>
    public static async Task WaitForNonStaleProjectionDataAsync(this IDocumentStore store, string tenantIdOrDatabaseName, TimeSpan timeout)
    {
        // Assuming there's only one database in this usage
        var database = await store.Storage.FindOrCreateDatabase(tenantIdOrDatabaseName).ConfigureAwait(false);

        if (store.Storage is DefaultTenancy)

        await WaitForNonStaleProjectionDataAsync(database, timeout).ConfigureAwait(false);
    }

    public static async Task WaitForNonStaleProjectionDataAsync(this IMartenDatabase database, TimeSpan timeout)
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.CancelAfter(timeout);

        var initial = await database.FetchEventStoreStatistics(cancellationSource.Token).ConfigureAwait(false);
        while (initial.EventSequenceNumber == 0 && !cancellationSource.IsCancellationRequested)
        {
            await Task.Delay(100.Milliseconds(), cancellationSource.Token).ConfigureAwait(false);
        }

        if (initial.EventSequenceNumber == 0)
        {
            throw new TimeoutException("No projection or event activity was detected within the timeout span");
        }

        var projections = await database.AllProjectionProgress(cancellationSource.Token).ConfigureAwait(false);
        while (!projections.All(x => x.Sequence >= initial.EventSequenceNumber) &&
               !cancellationSource.IsCancellationRequested)
        {
            await Task.Delay(100.Milliseconds(), cancellationSource.Token).ConfigureAwait(false);
        }

        if (cancellationSource.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The projections timed out before reaching the initial sequence of {initial.EventSequenceNumber}");
        }
    }
}
