using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.CommandLine.TextualDisplays;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Coordination;
using Marten.Services;
using Marten.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    ///     Use with caution! This will try to wait for all projections to "catch up" to the currently
    ///     known farthest known sequence of the event store for the supplied "ancillary" store
    /// </summary>
    /// <param name="timeout"></param>
    /// <returns></returns>
    public static Task WaitForNonStaleProjectionDataAsync<T>(this IHost host, TimeSpan timeout) where T : IDocumentStore
    {
        return host.DocumentStore<T>().WaitForNonStaleProjectionDataAsync(timeout);
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
    ///     this is invoked. *Note*, this method was intended for test automation and will wait
    ///     until there is any event data. If this is not what you intended, use WaitForNonStaleQueryableDataAsync()
    ///     instead
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

        // Just get out of there if there are no projections
        if (projectionsCount == 1)
        {
            return;
        }

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

        if (initial.EventSequenceNumber == 0 || initial.EventCount == 0)
        {
            throw new TimeoutException("No event activity was detected within the timeout span");
        }

        await database.WaitForNonStaleDataAsync(cancellationSource, projectionsCount, initial).ConfigureAwait(false);
    }

    /// <summary>
    /// Wait for all projections and subscriptions to catch up to the high water mark at the point this is called.
    /// This method will cleanly exit if there is no event data upfront
    /// </summary>
    /// <param name="database"></param>
    /// <param name="timeout"></param>
    /// <param name="cancellation"></param>
    public static async Task WaitForNonStaleQueryableDataAsync(this IMartenDatabase database, TimeSpan timeout, CancellationToken cancellation)
    {
        // Number of active projection shards, plus the high water mark
        var projectionsCount = database.As<MartenDatabase>().Options.Projections.AllShards().Count + 1;

        // Just get out of there if there are no projections
        if (projectionsCount == 1)
        {
            return;
        }

        var initial = await database.FetchEventStoreStatistics(cancellation).ConfigureAwait(false);

        // No data, get out of here
        if (initial.EventCount == 0 || initial.EventSequenceNumber == 1) return;

        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.CancelAfter(timeout);

        await database.WaitForNonStaleDataAsync(cancellationSource, projectionsCount, initial).ConfigureAwait(false);
    }

    public static async Task WaitForNonStaleDataAsync(this IMartenDatabase database, CancellationTokenSource cancellationSource,
        int projectionsCount, EventStoreStatistics initial)
    {
        IReadOnlyList<ShardState> projections = [];
        try
        {
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
        }
        catch (TaskCanceledException)
        {
            // We just didn't finish
        }

        if (projections.Count < projectionsCount)
        {
            var writer = new StringWriter();
            await writer.WriteLineAsync($"The projection shards (in total of {projectionsCount}) haven't been completely started within the timeout span").ConfigureAwait(false);
            await writer.WriteLineAsync().ConfigureAwait(false);
            await writer.WriteLineAsync(writeStatusMessage(projections)).ConfigureAwait(false);
            await writer.WriteLineAsync().ConfigureAwait(false);

            throw new TimeoutException(writer.ToString());
        }

        if (cancellationSource.IsCancellationRequested)
        {
            var writer = new StringWriter();
            await writer.WriteLineAsync($"The projections timed out before reaching the initial sequence of {initial.EventSequenceNumber}").ConfigureAwait(false);
            await writer.WriteLineAsync().ConfigureAwait(false);
            await writer.WriteLineAsync(writeStatusMessage(projections)).ConfigureAwait(false);
            await writer.WriteLineAsync().ConfigureAwait(false);

            throw new TimeoutException(writer.ToString());
        }
    }

    private static string writeStatusMessage(IReadOnlyList<ShardState> projections)
    {

        if (!projections.Any())
            return
                "There is no recorded projection, subscription, or even high water mark activity detected. Is the daemon started correctly?";

        var grid = new Grid<ShardState>();
        grid.AddColumn("Shard Name", x => x.ShardName);
        grid.AddColumn("Sequence", x => x.Sequence.ToString(), true);

        return grid.Write(projections);


    }

    private static bool isComplete(this Dictionary<string, long> tracking, long highWaterMark)
    {
        return tracking.Values.All(x => x >= highWaterMark);
    }

    public static async Task WaitForNonStaleProjectionDataAsync(this IMartenDatabase database, Type aggregationType, TimeSpan timeout, CancellationToken token)
    {
        // Number of active projection shards, plus the high water mark
        var shards = database.As<MartenDatabase>().Options.Projections.AsyncShardsPublishingType(aggregationType);
        if (shards.Length == 0) throw new InvalidOperationException($"Cannot find any registered async projection shards for aggregate type {aggregationType.FullNameInCode()}");

        var tracking = new Dictionary<string, long>();
        foreach (var shard in shards)
        {
            tracking[shard.Identity] = 0;
        }

        var highWaterMark = await database.FetchHighestEventSequenceNumber(token).ConfigureAwait(false);
        if (highWaterMark <= 1) return;

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

    /// <summary>
    /// Force any Marten async daemons to immediately advance to the latest changes. This is strictly
    /// meant for test automation scenarios with small to medium sized databases
    /// </summary>
    /// <param name="host"></param>
    /// <param name="cancellation"></param>
    /// <param name="mode">Optionally control whether the projections and subscriptions should be restarted after they have caught up</param>
    /// <returns></returns>
    public static Task<IReadOnlyList<Exception>> ForceAllMartenDaemonActivityToCatchUpAsync(this IHost host, CancellationToken cancellation,
        CatchUpMode mode = CatchUpMode.AndResumeNormally)
    {
        return host.Services.ForceAllMartenDaemonActivityToCatchUpAsync(cancellation, mode);
    }

    /// <summary>
    /// Force any Marten async daemons to immediately advance to the latest changes. This is strictly
    /// meant for test automation scenarios with small to medium sized databases
    /// </summary>
    /// <param name="services"></param>
    /// <param name="cancellation"></param>
    /// <param name="mode">Optionally control whether the projections and subscriptions should be restarted after they have caught up</param>
    /// <returns></returns>
    public static async Task<IReadOnlyList<Exception>> ForceAllMartenDaemonActivityToCatchUpAsync(this IServiceProvider services, CancellationToken cancellation,
        CatchUpMode mode = CatchUpMode.AndResumeNormally)
    {
        var logger = services.GetService<ILogger<ProjectionDaemon>>() ?? new NullLogger<ProjectionDaemon>();
        var coordinator = services.GetRequiredService<IProjectionCoordinator>();
        var daemons = await coordinator.AllDaemonsAsync().ConfigureAwait(false);

        var list = new List<Exception>();

        foreach (var daemon in daemons)
        {
            try
            {
                await daemon.StopAllAsync().ConfigureAwait(false);
                await daemon.CatchUpAsync(cancellation).ConfigureAwait(false);

                if (mode == CatchUpMode.AndResumeNormally)
                {
                    await daemon.StartAllAsync().ConfigureAwait(false);
                }

                logger.LogDebug("Executed a ProjectionDaemon.CatchUp() against {Daemon} in the main Marten store", daemon);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error trying to execute a CatchUp on {Daemon} in the main Marten store", daemon);
                list.Add(e);
            }
        }

        return list;
    }

        /// <summary>
    /// Force any Marten async daemons for an ancillary Marten store to immediately advance to the latest changes. This is strictly
    /// meant for test automation scenarios with small to medium sized databases
    /// </summary>
    /// <param name="host"></param>
    /// <param name="cancellation"></param>
    /// <param name="mode">Optionally control whether the projections and subscriptions should be restarted after they have caught up</param>
    /// <returns></returns>
    public static Task<IReadOnlyList<Exception>> ForceAllMartenDaemonActivityToCatchUpAsync<T>(this IHost host, CancellationToken cancellation,
        CatchUpMode mode = CatchUpMode.AndResumeNormally) where T : IDocumentStore
    {
        return host.Services.ForceAllMartenDaemonActivityToCatchUpAsync<T>(cancellation, mode);
    }

    /// <summary>
    /// Force any Marten async daemons for an ancillary Marten store to immediately advance to the latest changes. This is strictly
    /// meant for test automation scenarios with small to medium sized databases
    /// </summary>
    /// <param name="services"></param>
    /// <param name="cancellation"></param>
    /// <param name="mode">Optionally control whether the projections and subscriptions should be restarted after they have caught up</param>
    /// <returns></returns>
    public static async Task<IReadOnlyList<Exception>> ForceAllMartenDaemonActivityToCatchUpAsync<T>(this IServiceProvider services, CancellationToken cancellation,
        CatchUpMode mode = CatchUpMode.AndResumeNormally) where T : IDocumentStore
    {
        var logger = services.GetService<ILogger<ProjectionDaemon>>() ?? new NullLogger<ProjectionDaemon>();
        var coordinator = services.GetRequiredService<IProjectionCoordinator<T>>();
        var daemons = await coordinator.AllDaemonsAsync().ConfigureAwait(false);

        var list = new List<Exception>();

        foreach (var daemon in daemons)
        {
            try
            {
                await daemon.StopAllAsync().ConfigureAwait(false);
                await daemon.CatchUpAsync(cancellation).ConfigureAwait(false);

                if (mode == CatchUpMode.AndResumeNormally)
                {
                    await daemon.StartAllAsync().ConfigureAwait(false);
                }

                logger.LogDebug("Executed a ProjectionDaemon.CatchUp() against {Daemon} in Marten store {StoreType}", daemon, typeof(T).FullNameInCode());
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error trying to execute a CatchUp on {Daemon} in Marten store {StoreType}", daemon, typeof(T).FullNameInCode());
                list.Add(e);
            }
        }

        return list;
    }
}


public enum CatchUpMode
{
    /// <summary>
    /// Default setting, in this case the projections and subscriptions will be restarted in normal operation
    /// after the CatchUp operation is complete
    /// </summary>
    AndResumeNormally,

    /// <summary>
    /// Do not resume the asynchronous projection or synchronous behavior after the CatchUp operation is complete
    /// This may be useful for test automation
    /// </summary>
    AndDoNothing
}

