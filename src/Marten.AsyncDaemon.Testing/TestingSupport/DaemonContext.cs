using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Storage;
using Marten.Testing;
using Marten.Testing.Harness;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit.Abstractions;

namespace Marten.AsyncDaemon.Testing.TestingSupport;

#region sample_daemon_test_context
public abstract class DaemonContext: OneOffConfigurationsContext
{
    private readonly ProjectionDaemonRunner _projectionDaemonRunner;
    protected ITestOutputHelper Output;
    public ILogger<IProjection> Logger => _projectionDaemonRunner.Logger;

    protected DaemonContext(ITestOutputHelper output, ProjectionDaemonRunner projectionDaemonRunner) : base(projectionDaemonRunner)
    {
        _projectionDaemonRunner = projectionDaemonRunner;
        TheStore.Advanced.Clean.DeleteAllEventData();

        TheStore.Options.Projections.DaemonLockId++;
        Output = output;
    }

    protected DaemonContext(ITestOutputHelper output)
        : this(output, new ProjectionDaemonRunner(ConnectionSource.ConnectionString, new TestLogger<IProjection>(output))) { }

    public Task<IProjectionDaemon> StartDaemon() => _projectionDaemonRunner.StartDaemon();
    public Task<IProjectionDaemon> StartDaemon(string tenantId) => _projectionDaemonRunner.StartDaemon(tenantId);
    #endregion
    public Task<IProjectionDaemon> StartDaemonInHotColdMode() => _projectionDaemonRunner.StartDaemonInHotColdMode();
    public Task<IProjectionDaemon> StartAdditionalDaemonInHotColdMode() => _projectionDaemonRunner.StartAdditionalDaemonInHotColdMode();
    public Task WaitForAction(string shardName, ShardAction action, TimeSpan timeout = default) => _projectionDaemonRunner.WaitForAction(shardName, action, timeout);

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

    public IReadOnlyList<TripStream> Streams => _streams;

    protected StreamAction[] ToStreamActions()
    {
        return _streams.Select(x => x.ToAction(TheStore.Events)).ToArray();
    }

    protected async Task CheckAllExpectedAggregatesAgainstActuals()
    {
        var actuals = await LoadAllAggregatesFromDatabase();

        foreach (var stream in _streams)
        {
            var expected = await TheSession.Events.AggregateStreamAsync<Trip>(stream.StreamId);

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

    protected async Task CheckAllExpectedAggregatesAgainstActuals(string tenantId)
    {
        var actuals = await LoadAllAggregatesFromDatabase(tenantId);

        await using var session = TheStore.LightweightSession(tenantId);

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

    protected async Task<Dictionary<Guid, Trip>> LoadAllAggregatesFromDatabase(string tenantId = null)
    {
        if (string.IsNullOrEmpty(tenantId))
        {
            var data = await TheSession.Query<Trip>().ToListAsync();
            var dict = data.ToDictionary(x => x.Id);
            return dict;
        }
        else
        {
            await using var session = TheStore.LightweightSession(tenantId);
            var data = await session.Query<Trip>().ToListAsync();
            var dict = data.ToDictionary(x => x.Id);
            return dict;
        }
    }

    protected async Task PublishSingleThreaded()
    {
        var groups = _streams.GroupBy(x => x.TenantId).ToArray();
        if (groups.Length > 1 || groups.Single().Key != Tenancy.DefaultTenantId)
        {
            foreach (var @group in groups)
            {
                foreach (var stream in @group)
                {
                    await using var session = TheStore.LightweightSession(group.Key);
                    session.Events.StartStream(stream.StreamId, stream.Events);
                    await session.SaveChangesAsync();
                }
            }
        }
        else
        {
            foreach (var stream in _streams)
            {
                await using var session = TheStore.LightweightSession();
                session.Events.StartStream(stream.StreamId, stream.Events);
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
            publishers.Add(new EventPublisher(TheStore));
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
                                if (events.Length == 0)
                                    throw new DivideByZeroException();
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
}
