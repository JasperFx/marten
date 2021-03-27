using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Baseline.Dates;
using Castle.Core.Internal;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Events.Daemon.HighWater;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.AsyncDaemon.Testing.TestingSupport
{
    [Collection("daemon")]
    public abstract class DaemonContext : OneOffConfigurationsContext, IDisposable
    {
        protected DaemonContext(ITestOutputHelper output) : base("daemon")
        {
            theStore.Advanced.Clean.DeleteAllEventData();
            Logger = new TestLogger<IProjection>(output);

            _output = output;
        }

        public ILogger<IProjection> Logger { get; }

        internal async Task<ProjectionDaemon> StartDaemon()
        {
            var daemon = new ProjectionDaemon(theStore, Logger);

            await daemon.StartAllShards();

            _daemon = daemon;

            return daemon;
        }

        internal async Task<ProjectionDaemon> StartDaemonInHotColdMode()
        {
            theStore.Events.Daemon.LeadershipPollingTime = 100;
            var coordinator = new HotColdCoordinator(theStore, theStore.Events.Daemon, Logger);
            var daemon = new ProjectionDaemon(theStore, new HighWaterDetector(coordinator, theStore.Events), Logger);

            await daemon.UseCoordinator(coordinator);

            _daemon = daemon;

            _disposables.Add(daemon);
            return daemon;
        }

        internal async Task<ProjectionDaemon> StartAdditionalDaemonInHotColdMode()
        {
            theStore.Events.Daemon.LeadershipPollingTime = 100;
            var coordinator = new HotColdCoordinator(theStore, theStore.Events.Daemon, Logger);
            var daemon = new ProjectionDaemon(theStore, new HighWaterDetector(coordinator, theStore.Events), Logger);

            await daemon.UseCoordinator(coordinator);

            _disposables.Add(daemon);
            return daemon;
        }

        protected Task WaitForAction(string shardName, ShardAction action, TimeSpan timeout = default)
        {
            if (timeout == default)
            {
                timeout = 30.Seconds();
            }

            return new ShardActionWatcher(_daemon.Tracker,shardName, action, timeout).Task;
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
                var other = new TripStream
                {
                    TenantId = "b",
                    StreamId = stream.StreamId
                };

                _streams.Add(other);
            }
        }

        public long NumberOfEvents => _streams.Sum(x => x.Events.Count);

        private readonly List<TripStream> _streams = new List<TripStream>();
        private ProjectionDaemon _daemon;
        protected ITestOutputHelper _output;

        protected StreamAction[] ToStreamActions()
        {
            return _streams.Select(x => x.ToAction(theStore.Events)).ToArray();
        }

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

        protected async Task CheckAllExpectedAggregatesAgainstActuals(string tenantId)
        {
            var actuals = await LoadAllAggregatesFromDatabase(tenantId);

            using var session = theStore.LightweightSession(tenantId);

            foreach (var stream in _streams.Where(x => x.TenantId == tenantId))
            {
                var expected = await session.Events.AggregateStreamAsync<Trip>(stream.StreamId);

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

        protected async Task<Dictionary<Guid, Trip>> LoadAllAggregatesFromDatabase(string tenantId = null)
        {

            if (tenantId.IsNullOrEmpty())
            {
                var data = await theSession.Query<Trip>().ToListAsync();
                var dict = data.ToDictionary(x => x.Id);
                return dict;
            }
            else
            {
                using var session = theStore.LightweightSession(tenantId);
                var data = await session.Query<Trip>().ToListAsync();
                var dict = data.ToDictionary(x => x.Id);
                return dict;
            }
        }

        protected async Task PublishSingleThreaded()
        {

            var groups = _streams.GroupBy(x => x.TenantId).ToArray();
            if (groups.Length > 1)
            {
                foreach (var @group in groups)
                {
                    foreach (var stream in @group)
                    {
                        using (var session = theStore.LightweightSession(@group.Key))
                        {
                            session.Events.StartStream(stream.StreamId, stream.Events);
                            await session.SaveChangesAsync();
                        }
                    }
                }
            }
            else
            {
                foreach (var stream in _streams)
                {
                    using (var session = theStore.LightweightSession())
                    {
                        session.Events.StartStream(stream.StreamId, stream.Events);
                        await session.SaveChangesAsync();
                    }
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
            var streamsPerPublisher = (int) Math.Floor((double) _streams.Count / threads);
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
            private readonly TaskCompletionSource<bool> _completion = new TaskCompletionSource<bool>();

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
                        using (var session = _store.LightweightSession())
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
}
