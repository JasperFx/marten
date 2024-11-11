using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Lamar.Microsoft.DependencyInjection;
using Marten;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Coordination;
using Marten.Events.Daemon.Resiliency;
using Marten.Events.Projections;
using Marten.Storage;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests.TestingSupport;

public abstract class DaemonContext: OneOffConfigurationsContext, IAsyncLifetime
{
    private int lockId = 10000;

    protected DaemonContext(ITestOutputHelper output)
    {
        _schemaName = "daemon";
        theStore.Advanced.Clean.DeleteAllEventDataAsync().GetAwaiter().GetResult();
        Logger = new TestLogger<IProjection>(output);

        // Creating a little uniqueness
        lockId++;

        theStore.Options.Projections.DaemonLockId = lockId;

        _output = output;
    }

    internal async Task wipeAllStreamTypeMarkers()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await conn.CreateCommand("update daemon.mt_streams set type = null").ExecuteNonQueryAsync();
        await conn.CreateCommand("delete from daemon.mt_event_progression where name != 'HighWaterMark'")
            .ExecuteNonQueryAsync();
    }

    public ILogger<IProjection> Logger { get; }

    internal async Task<IProjectionDaemon> StartDaemon()
    {
        var daemon = theStore.Tenancy.Default.Database.As<MartenDatabase>()
            .StartProjectionDaemon(theStore, new TestOutputMartenLogger(_output));

        await daemon.StartAllAsync();

        _daemon = daemon;

        return daemon;
    }

    internal async Task<ProjectionDaemon> StartDaemon(string tenantId)
    {
        var daemon = (ProjectionDaemon)await theStore.BuildProjectionDaemonAsync(tenantId, Logger);

        await daemon.StartAllAsync();

        _daemon = daemon;

        return daemon;
    }

    internal async Task<IHost> StartDaemonInHotColdMode()
    {
        var host = await Host.CreateDefaultBuilder()
            .UseLamar(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = _schemaName;
                    opts.Projections.LeadershipPollingTime = 100;
                    opts.Projections.DaemonLockId = lockId;

                    opts.Projections.Add(new TripProjectionWithCustomName(), ProjectionLifecycle.Async);
                }).AddAsyncDaemon(DaemonMode.HotCold);
            }).StartAsync();

        _disposables.Add(host);

        theStore = (DocumentStore)host.Services.GetRequiredService<IDocumentStore>();

        return host;
    }

    internal async Task<IHost> StartAdditionalDaemonInHotColdMode()
    {
        var host = await Host.CreateDefaultBuilder()
            .UseLamar(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = _schemaName;
                    opts.Projections.LeadershipPollingTime = 100;

                    opts.Projections.DaemonLockId = lockId;

                    opts.Projections.Add(new TripProjectionWithCustomName(), ProjectionLifecycle.Async);
                }).AddAsyncDaemon(DaemonMode.HotCold);
            }).StartAsync();

        _disposables.Add(host);

        return host;
    }

    protected Task WaitForAction(string shardName, ShardAction action, TimeSpan timeout = default)
    {
        if (timeout == default)
        {
            timeout = 30.Seconds();
        }

        return new ShardActionWatcher(_daemon.Tracker, shardName, action, timeout).Task;
    }

    public int NumberOfStreams
    {
        get
        {
            return _streams.Count;
        }
        set
        {
            _streams.Clear();
            _streams.AddRange(TripStream.RandomStreams(value));
        }
    }

    public void UseMixOfTenants(int numberOfStreams)
    {
        NumberOfStreams = numberOfStreams;

        foreach (var stream in _streams.ToArray())
        {
            stream.TenantId = "a";
            var other = new TripStream { TenantId = "b", StreamId = stream.StreamId };

            _streams.Add(other);
        }
    }


    public void UseTenant(string tenantId)
    {
        foreach (var stream in _streams.ToArray())
        {
            stream.TenantId = tenantId;
        }
    }

    public long NumberOfEvents => _streams.Sum(x => x.Events.Count);

    private readonly List<TripStream> _streams = new List<TripStream>();
    private IProjectionDaemon _daemon;
    protected ITestOutputHelper _output;

    public IReadOnlyList<TripStream> Streams => _streams;

    protected StreamAction[] ToStreamActions()
    {
        return _streams.Select(x => x.ToAction(theStore.Events)).ToArray();
    }

    // START HERE, NEEDS TO BE GENERALIZED
    protected async Task CheckAllExpectedAggregatesAgainstActuals()
    {
        var actuals = await LoadAllAggregatesFromDatabase();

        foreach (var stream in _streams)
        {
            var expected = await theSession.Events.AggregateStreamAsync<Trip>(stream.StreamId);

            if (expected == null)
            {
                actuals.ContainsKey(stream.StreamId).ShouldBeFalse();
            }
            else
            {
                if (actuals.TryGetValue(stream.StreamId, out var actual))
                {
                    expected.ShouldBe(actual);
                }
                else
                {
                    throw new Exception("Missing expected aggregate");
                }
            }
        }
    }

    protected async Task CheckAllExpectedGuidCentricAggregatesAgainstActuals<TDoc>(Func<TDoc, Guid> identifier) where TDoc : class
    {
        var actuals = await LoadAllAggregatesFromDatabase<Guid, TDoc>(identifier);

        foreach (var stream in _streams)
        {
            var expected = await theSession.Events.AggregateStreamAsync<TDoc>(stream.StreamId);

            if (expected == null)
            {
                actuals.ContainsKey(stream.StreamId).ShouldBeFalse();
            }
            else
            {
                if (actuals.TryGetValue(stream.StreamId, out var actual))
                {
                    expected.ShouldBe(actual);
                }
                else
                {
                    throw new Exception("Missing expected aggregate");
                }
            }
        }
    }

    protected async Task CheckAllExpectedStringCentricAggregatesAgainstActuals<TDoc>(Func<TDoc, string> identifier) where TDoc : class
    {
        var actuals = await LoadAllAggregatesFromDatabase<string, TDoc>(identifier);

        foreach (var stream in _streams)
        {
            var expected = await theSession.Events.AggregateStreamAsync<TDoc>(stream.StreamId.ToString());

            if (expected == null)
            {
                actuals.ContainsKey(stream.StreamId.ToString()).ShouldBeFalse();
            }
            else
            {
                if (actuals.TryGetValue(stream.StreamId.ToString(), out var actual))
                {
                    expected.ShouldBe(actual);
                }
                else
                {
                    throw new Exception("Missing expected aggregate");
                }
            }
        }
    }

    protected async Task CheckAllExpectedAggregatesAgainstActuals(string tenantId)
    {
        var actuals = await LoadAllAggregatesFromDatabase(tenantId);

        await using var session = theStore.LightweightSession(tenantId);

        foreach (var stream in _streams.Where(x => x.TenantId == tenantId))
        {
            var expected = await session.Events.AggregateStreamAsync<Trip>(stream.StreamId);

            if (expected == null)
            {
                actuals.ContainsKey(stream.StreamId).ShouldBeFalse();
            }
            else
            {
                if (!actuals.TryGetValue(stream.StreamId, out var actual))
                {
                    throw new Exception("Missing expected aggregate");
                }

                expected.ShouldBe(actual);
            }
        }
    }

    protected Task<Dictionary<Guid, Trip>> LoadAllAggregatesFromDatabase(string tenantId = null)
    {
        return LoadAllAggregatesFromDatabase<Guid, Trip>(x => x.Id, tenantId);
    }

    protected async Task<Dictionary<TId, TDoc>> LoadAllAggregatesFromDatabase<TId, TDoc>(Func<TDoc, TId> identifier,string tenantId = null)
    {
        if (string.IsNullOrEmpty(tenantId))
        {
            var data = await theSession.Query<TDoc>().ToListAsync();
            var dict = data.ToDictionary(x => identifier(x));
            return dict;
        }
        else
        {
            await using var session = theStore.LightweightSession(tenantId);
            var data = await session.Query<TDoc>().ToListAsync();
            var dict = data.ToDictionary(x => identifier(x));
            return dict;
        }
    }

    protected async Task PublishSingleThreaded()
    {
        await PublishSingleThreaded<Trip>();
    }

    protected async Task PublishSingleThreaded<T>() where T : class
    {
        var groups = _streams.GroupBy(x => x.TenantId).ToArray();
        if (groups.Length > 1 || groups.Single().Key != StorageConstants.DefaultTenantId)
        {
            foreach (var @group in groups)
            {
                foreach (var stream in @group)
                {
                    await using var session = theStore.LightweightSession(group.Key);

                    if (theStore.Options.EventGraph.StreamIdentity == StreamIdentity.AsGuid)
                    {
                        session.Events.StartStream<T>(stream.StreamId, stream.Events);
                    }
                    else
                    {
                        session.Events.StartStream<T>(stream.StreamId.ToString(), stream.Events);
                    }


                    await session.SaveChangesAsync();
                }
            }
        }
        else
        {
            foreach (var stream in _streams)
            {
                await using var session = theStore.LightweightSession();
                session.Events.StartStream<T>(stream.StreamId, stream.Events);
                await session.SaveChangesAsync();
            }
        }
    }

    protected Task PublishMultiThreaded(int threads)
    {
        foreach (var stream in _streams)
        {
            stream.Reset();
        }

        var publishers = createPublishers(threads);

        var tasks = publishers.Select(x => x.PublishAll()).ToArray();
        return Task.WhenAll(tasks);
    }

    private List<EventPublisher> createPublishers(int threads)
    {
        var streamsPerPublisher = (int)Math.Floor((double)_streams.Count / threads);
        var index = 0;

        var publishers = new List<EventPublisher>();
        for (var i = 0; i < threads; i++)
        {
            publishers.Add(new EventPublisher(theStore));
        }


        foreach (var publisher in publishers)
        {
            publisher.Streams.AddRange(_streams.GetRange(index, streamsPerPublisher));
            index += streamsPerPublisher;
        }

        if (index < _streams.Count - 1)
        {
            publishers.Last().Streams.Add(_streams.Last());
        }

        return publishers;
    }

    public class EventPublisher
    {
        private readonly DocumentStore _store;

        private readonly TaskCompletionSource<bool> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public EventPublisher(DocumentStore store)
        {
            _store = store;
        }

        public IList<TripStream> Streams { get; } = new List<TripStream>();

        public Task PublishAll()
        {
            Task.Factory.StartNew(publishAll, TaskCreationOptions.AttachedToParent);
            return _completion.Task;
        }

        private async Task publishAll()
        {
            try
            {
                while (Streams.Any() && !Streams.All(x => x.IsFinishedPublishing()))
                {
                    await using (var session = _store.LightweightSession())
                    {
                        foreach (var stream in Streams)
                        {
                            if (stream.TryCheckOutEvents(out var events))
                            {
                                if (events.Length == 0) throw new DivideByZeroException();
                                session.Events.Append(stream.StreamId, events);
                            }
                        }

                        await session.SaveChangesAsync();
                    }
                }
            }
            catch (Exception e)
            {
                _completion.SetException(e);
            }

            _completion.SetResult(true);
        }
    }

    public async Task InitializeAsync()
    {
        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }
}

internal class ShardActionWatcher: IObserver<ShardState>
{
    private readonly IDisposable _unsubscribe;
    private readonly string _shardName;
    private readonly ShardAction _expected;
    private readonly TaskCompletionSource<ShardState> _completion;
    private readonly CancellationTokenSource _timeout;

    public ShardActionWatcher(ShardStateTracker tracker, string shardName, ShardAction expected, TimeSpan timeout)
    {
        _shardName = shardName;
        _expected = expected;
        _completion = new TaskCompletionSource<ShardState>();


        _timeout = new CancellationTokenSource(timeout);
        _timeout.Token.Register(() =>
        {
            _completion.TrySetException(new TimeoutException(
                $"Shard {_shardName} did receive the action {_expected} in the time allowed"));
        });

        _unsubscribe = tracker.Subscribe(this);
    }

    public Task<ShardState> Task => _completion.Task;

    public void OnCompleted()
    {
    }

    public void OnError(Exception error)
    {
        _completion.SetException(error);
    }

    public void OnNext(ShardState value)
    {
        if (value.ShardName.EqualsIgnoreCase(_shardName) && value.Action == _expected)
        {
            _completion.SetResult(value);
            _unsubscribe.Dispose();
        }
    }
}

public static class HostExtensions
{
    public static ProjectionDaemon Daemon(this IHost host)
    {
        return (ProjectionDaemon)host.Services.GetRequiredService<IProjectionCoordinator>().DaemonForMainDatabase();
    }
}
