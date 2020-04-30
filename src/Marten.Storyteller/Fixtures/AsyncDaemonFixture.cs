using System;
using System.Threading;
using System.Threading.Tasks;
using Baseline.Dates;
using Marten.Events.Projections;
using Marten.Events.Projections.Async;
using Marten.Storage;
using Marten.Testing;
using Marten.Testing.AsyncDaemon;
using Marten.Testing.CodeTracker;
using Marten.Testing.Harness;
using Marten.Util;
using StoryTeller;

namespace Marten.Storyteller.Fixtures
{
    public class AsyncDaemonFixture: Fixture
    {
        private StoreOptions _options;
        private DaemonSettings _settings;
        private Lazy<DocumentStore> _store;
        private Lazy<IDaemon> _daemon;

        public override void SetUp()
        {
            _options = new StoreOptions();
            _options.Connection(ConnectionSource.ConnectionString);

            _options.Events.AsyncProjections.AggregateStreamsWith<ActiveProject>();
            _options.Events.AsyncProjections.TransformEvents(new CommitViewTransform());

            _settings = new DaemonSettings();

            helper = Context.State.Retrieve<AsyncDaemonTestHelper>();

            _store = new Lazy<DocumentStore>(() =>
            {
                var store = new DocumentStore(_options);
                store.Advanced.Clean.CompletelyRemoveAll();

                return store;
            });

            _daemon = new Lazy<IDaemon>(() => _store.Value.BuildProjectionDaemon(settings: _settings));
        }

        public override void TearDown()
        {
            if (_daemon.IsValueCreated)
            {
                _daemon.Value.Dispose();
            }

            if (_store.IsValueCreated)
            {
                _store.Value.Dispose();
            }

            _store = null;
        }

        protected AsyncDaemonTestHelper helper;

        public void EventSchemaIs(string schema)
        {
            _options.Events.DatabaseSchemaName = schema;
        }

        public void LeadingEdgeBuffer(int seconds)
        {
            _settings.LeadingEdgeBuffer = seconds.Seconds();
        }

        public void PublishAllEvents()
        {
            helper.PublishAllProjectEvents(_store.Value, false);
        }

        public async Task PublishAllEventsAsync()
        {
            await helper.PublishAllProjectEventsAsync(_store.Value, false);
            await _daemon.Value.WaitForNonStaleResults();
        }

        public void StartTheDaemon()
        {
            _daemon.Value.StartAll();
        }

        public async Task StopWhenFinished()
        {
            await _daemon.Value.WaitForNonStaleResults();
            await _daemon.Value.StopAll();
        }

        public void UseTheErroringProjection()
        {
            _options.Events.AsyncProjections.Add(new OccasionalErroringProjection());
        }

        [FormatAs("Configure the Async Daemon to retry 3 times on DivideByZeroException")]
        public void RetryThreeTimesOnDivideByZeroException()
        {
            _settings.ExceptionHandling.OnException<DivideByZeroException>().Retry(3);
        }

        [FormatAs("All GitHub projects match the expected aggregate")]
        public bool CompareProjects()
        {
            helper.CompareActiveProjects(_store.Value);

            return true;
        }

        public Task RebuildProjection()
        {
            return _daemon.Value.Rebuild<ActiveProject>();
        }

        public void CreateSequentialGap(int original, int seq)
        {
            // Increment seq_id so events have a respective 1 and 101 seq_id
            using (var conn = _store.Value.Tenancy.Default.OpenConnection())
            {
                var command = conn.Connection.CreateCommand();
                command.CommandText = "UPDATE mt_events SET seq_id = :seq WHERE seq_id = :original";
                command.AddParameter(seq).ParameterName = "seq";
                command.AddParameter(original).ParameterName = "original";

                command.CommandType = System.Data.CommandType.Text;
                conn.Execute(command);
            }
        }
    }

    public class OccasionalErroringProjection: IProjection
    {
        private readonly Random _random = new Random(5);
        private bool _failed;

        public Type[] Consumes { get; } = new Type[] { typeof(ProjectStarted), typeof(IssueCreated), typeof(IssueClosed), typeof(Commit) };
        public Type Produces { get; } = typeof(FakeThing);
        public AsyncOptions AsyncOptions { get; } = new AsyncOptions();

        public void Apply(IDocumentSession session, EventPage page)
        {
        }

        public Task ApplyAsync(IDocumentSession session, EventPage page, CancellationToken token)
        {
            if (!_failed && _random.Next(0, 10) == 9)
            {
                _failed = true;
                throw new DivideByZeroException();
            }

            _failed = false;

            return Task.CompletedTask;
        }

        public void EnsureStorageExists(ITenant tenant)
        {
        }
    }

    public class FakeThing
    {
        public Guid Id;
    }
}
