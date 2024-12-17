using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Events.Daemon;
using Marten.Storage;
using Microsoft.Extensions.Hosting;

namespace Marten.Events;

public static class TestingExtensions
{
    /// <summary>
    ///     Use with caution! This will try to wait for all projections to "catch up" to the currently
    ///     known farthest known sequence of the event store
    /// </summary>
    /// <param name="timeout"></param>
    /// <returns></returns>
    public static Task WaitForNonStaleProjectionDataAsync(this IHost host, TimeSpan timeout)
    {
        return host.DocumentStore().WaitForNonStaleProjectionDataAsync(timeout);
    }

    /// <summary>
    ///     Wait for any running async daemon for a specific tenant id or database name to catch up to the latest event
    ///     sequence at the time
    ///     this method is invoked for all projections. This method is meant to aid in automated testing
    /// </summary>
    /// <param name="tenantIdOrDatabaseName">Either a tenant id or the name of a database within the system</param>
    public static async Task WaitForNonStaleProjectionDataAsync(this IHost host,
        string tenantIdOrDatabaseName, TimeSpan timeout)
    {
        // Assuming there's only one database in this usage
        var database = await host.DocumentStore().Storage.FindOrCreateDatabase(tenantIdOrDatabaseName).ConfigureAwait(false);

        await database.WaitForNonStaleProjectionDataAsync(timeout).ConfigureAwait(false);
    }

    /// <summary>
    ///     Wait for any running async daemons to catch up to the latest event sequence at the time
    ///     this method is invoked for all projections. This method is meant to aid in automated testing
    /// </summary>
    /// <param name="store"></param>
    /// <param name="timeout"></param>
    public static Task WaitForNonStaleProjectionDataAsync(this IDocumentStore store, TimeSpan timeout)
    {
        if (store.As<DocumentStore>().Tenancy is DefaultTenancy)
        {
            return store.Storage.Database.WaitForNonStaleProjectionDataAsync(timeout);
        }

        throw new InvalidOperationException(
            "If using multi-tenancy through any kind of separate databases per tenant, please specify a tenant id or database name");
    }

    /// <summary>
    ///     Wait for any running async daemon for a specific tenant id or database name to catch up to the latest event
    ///     sequence at the time
    ///     this method is invoked for all projections. This method is meant to aid in automated testing
    /// </summary>
    /// <param name="tenantIdOrDatabaseName">Either a tenant id or the name of a database within the system</param>
    public static async Task WaitForNonStaleProjectionDataAsync(this IDocumentStore store,
        string tenantIdOrDatabaseName, TimeSpan timeout)
    {
        // Assuming there's only one database in this usage
        var database = await store.Storage.FindOrCreateDatabase(tenantIdOrDatabaseName).ConfigureAwait(false);

        await WaitForNonStaleProjectionDataAsync(database, timeout).ConfigureAwait(false);
    }

    /// <summary>
    ///     Wait for any running async daemon to catch up to the latest event sequence at the time
    /// </summary>
    /// <param name="projectionsCount">
    ///     Will be awaited till all shards have been started before checking if they've caught up
    ///     with the sequence number
    /// </param>
    /// <exception cref="TimeoutException"></exception>
    public static async Task WaitForNonStaleProjectionDataAsync(this IMartenDatabase database, TimeSpan timeout)
    {
        // Number of active projection shards, plus the high water mark
        var projectionsCount = database.As<MartenDatabase>().Options.Projections.AllShards().Count + 1;

        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.CancelAfter(timeout);

        EventStoreStatistics initial;
        do
        {
            initial = await database.FetchEventStoreStatistics(cancellationSource.Token).ConfigureAwait(false);
            if (initial.EventSequenceNumber > 0 || cancellationSource.IsCancellationRequested)
            {
                break;
            }

            await Task.Delay(100.Milliseconds(), cancellationSource.Token).ConfigureAwait(false);
        } while (true);

        if (initial.EventSequenceNumber == 0)
        {
            throw new TimeoutException("No event activity was detected within the timeout span");
        }

        IReadOnlyList<ShardState> projections;
        do
        {
            projections = await database.AllProjectionProgress(cancellationSource.Token).ConfigureAwait(false);
            if ((projections.Count >= projectionsCount &&
                 projections.All(x => x.Sequence >= initial.EventSequenceNumber))
                || cancellationSource.IsCancellationRequested)
            {
                break;
            }

            await Task.Delay(250.Milliseconds(), cancellationSource.Token).ConfigureAwait(false);
        } while (true);

        if (projections.Count < projectionsCount)
        {
            throw new TimeoutException(
                $"The projection shards (in total of {projectionsCount}) haven't been completely started within the timeout span");
        }

        if (cancellationSource.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The projections timed out before reaching the initial sequence of {initial.EventSequenceNumber}");
        }
    }

    private static bool isComplete(this Dictionary<string, long> tracking, long highWaterMark)
    {
        return tracking.Values.All(x => x >= highWaterMark);
    }

    public static async Task WaitForNonStaleProjectionDataAsync(this IMartenDatabase database, Type aggregationType, TimeSpan timeout, CancellationToken token)
    {
        // Number of active projection shards, plus the high water mark
        var shards = database.As<MartenDatabase>().Options.Projections.AsyncShardsPublishingType(aggregationType);
        if (!shards.Any()) throw new InvalidOperationException($"Cannot find any registered async projection shards for aggregate type {aggregationType.FullNameInCode()}");

        var all = shards.Concat([ShardName.HighWaterMark]).ToArray();
        var tracking = new Dictionary<string, long>();
        foreach (var shard in shards)
        {
            tracking[shard.Identity] = 0;
        }

        long highWaterMark = long.MaxValue;
        var initial = await database.FetchProjectionProgressFor(all, token).ConfigureAwait(false);
        foreach (var state in initial)
        {
            if (state.ShardName == ShardState.HighWaterMark)
            {
                highWaterMark = state.Sequence;
            }
            else
            {
                tracking[state.ShardName] = state.Sequence;
            }
        }

        if (tracking.isComplete(highWaterMark)) return;

        using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token);
        cancellationSource.CancelAfter(timeout);

        while (!cancellationSource.Token.IsCancellationRequested)
        {
            var current = await database.FetchProjectionProgressFor(shards, cancellationSource.Token).ConfigureAwait(false);
            foreach (var state in current)
            {
                tracking[state.ShardName] = state.Sequence;
            }

            if (tracking.isComplete(highWaterMark)) return;

            await Task.Delay(100.Milliseconds(), cancellationSource.Token).ConfigureAwait(false);
        }

        if (cancellationSource.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The projections timed out before reaching the initial sequence of {highWaterMark}");
        }
    }
}
