#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Storage;
using Marten.Testing.Harness;
using Weasel.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Polly;
using Shouldly;
using Xunit;

namespace CoreTests;

/// <summary>
/// #5587. The daemon's event loading used to run through two nested retry pipelines — the
/// <c>ResilientEventLoader</c> decorator and, inside it, the <see cref="Marten.Internal.Sessions.QuerySession"/>
/// that <c>EventLoader</c> opens per call — and both were handed the same
/// <c>Options.ResiliencePipeline</c>. Three retries at each level is 4 × 4 executions of one query.
/// </summary>
/// <remarks>
/// The inner pipeline is the one worth keeping: it is where the command runs and where
/// <c>AutoClosingLifetime</c> opens a fresh connection per attempt. So the decorator keeps its metrics and
/// its <c>EventLoaderException</c> wrap and stops retrying.
/// </remarks>
public class Bug_5587_daemon_loader_retry_budget: OneOffConfigurationsContext
{
    private int _executions;

    /// <summary>
    /// Counts executions by replacing the read policy with one that increments on every attempt. The retry
    /// budget is left at Marten's own 3, so the total is the real end-to-end budget, not an invented one.
    /// </summary>
    private void countingReadPolicy(StoreOptions opts)
    {
        opts.ConfigurePolly(builder => builder.AddRetry(new()
        {
            ShouldHandle = args =>
            {
                if (args.Outcome.Exception is not null) Interlocked.Increment(ref _executions);
                return new ValueTask<bool>(args.Outcome.Exception is NpgsqlException or Marten.Exceptions.MartenCommandException);
            },
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromMilliseconds(1),
            BackoffType = DelayBackoffType.Constant
        }));
    }

    [Fact]
    public async Task the_loader_spends_one_retry_budget_not_two()
    {
        StoreOptions(opts =>
        {
            opts.AutoCreateSchemaObjects = AutoCreate.None;
            countingReadPolicy(opts);
        });

        var database = (MartenDatabase)theStore.Storage.Database;
        var loader = ((IEventStore<IDocumentOperations, IQuerySession>)theStore).BuildEventLoader(
            database, NullLogger.Instance, new EventFilterable(), new AsyncOptions());

        // AutoCreate.None against a schema with no event tables, so the load fails 42P01 every attempt --
        // a PostgresException, which the policy above handles, so the full budget is spent.
        var shard = new ShardName("fake");
        var request = new EventRequest
        {
            Floor = 0,
            BatchSize = 10,
            HighWater = 100,
            Name = shard,
            ErrorOptions = new ErrorHandlingOptions(),
            Metrics = new SubscriptionMetrics((IEventStore)theStore, shard, database),
            Runtime = null!
        };

        await Should.ThrowAsync<Exception>(() => loader.LoadAsync(request, CancellationToken.None));

        // One pipeline: 1 attempt + 3 retries. Nested, this was 16.
        _executions.ShouldBe(4);
    }
}
