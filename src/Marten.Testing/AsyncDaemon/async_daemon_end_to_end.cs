using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Events.Projections.Async;
using Marten.Testing.CodeTracker;
using Xunit;
using Xunit.Abstractions;
using System.Linq;
using Baseline.Dates;
using Marten.Storage;
using Shouldly;

namespace Marten.Testing.AsyncDaemon
{
    public class async_daemon_end_to_end : IntegratedFixture, IClassFixture<AsyncDaemonTestHelper>
    {
        
        public async_daemon_end_to_end(AsyncDaemonTestHelper testHelper, ITestOutputHelper output)
        {
            _testHelper = testHelper;
            _logger = new TracingLogger(output.WriteLine);
        }
        
//        public async_daemon_end_to_end()
//        {
//            _fixture = new AsyncDaemonFixture();
//            _logger = new ConsoleDaemonLogger();
//        }

        private readonly AsyncDaemonTestHelper _testHelper;
        private readonly IDaemonLogger _logger;




        [Fact]
        public async Task do_a_complete_rebuild_of_the_active_projects_from_scratch_on_other_schema_single_event()
        {
            _testHelper.LoadSingleProject();

            StoreOptions(_ =>
            {
                _.Events.AsyncProjections.AggregateStreamsWith<ActiveProject>();
                _.Events.DatabaseSchemaName = "events";
            });

            _testHelper.PublishAllProjectEvents(theStore, true);


            

            using (var daemon = theStore.BuildProjectionDaemon(logger: _logger, settings: new DaemonSettings
            {
                LeadingEdgeBuffer = 0.Seconds()
            }))
            {
                await daemon.Rebuild<ActiveProject>().ConfigureAwait(false);
            }

            _testHelper.CompareActiveProjects(theStore);
            
        }

        [Fact]
        public async Task start_and_stop_a_projection()
        {
            _testHelper.LoadSingleProject();

            StoreOptions(_ =>
            {
                _.Events.AsyncProjections.AggregateStreamsWith<ActiveProject>();
                _.Events.DatabaseSchemaName = "events";
            });

            _testHelper.PublishAllProjectEvents(theStore, true);



            // Really just kind of a smoke test here
            using (var daemon = theStore.BuildProjectionDaemon(logger: _logger, settings: new DaemonSettings
            {
                LeadingEdgeBuffer = 0.Seconds()
            }))
            {
                daemon.Start<ActiveProject>(DaemonLifecycle.Continuous);
                await Task.Delay(200);
                await daemon.Stop<ActiveProject>().ConfigureAwait(false);

                daemon.Start<ActiveProject>(DaemonLifecycle.StopAtEndOfEventData);

            }



        }


        //[Fact] Not super duper reliable when running back to back
        public async Task do_a_complete_rebuild_of_the_active_projects_from_scratch_twice_on_other_schema()
        {
            _testHelper.LoadAllProjects();

            StoreOptions(_ =>
            {
                _.Events.AsyncProjections.AggregateStreamsWith<ActiveProject>();
                _.Events.DatabaseSchemaName = "events";
            });

            _testHelper.PublishAllProjectEvents(theStore, true);

            using (var daemon = theStore.BuildProjectionDaemon(logger: _logger, settings: new DaemonSettings
            {
                LeadingEdgeBuffer = 0.Seconds()
            }))
            {
                await daemon.RebuildAll().ConfigureAwait(false);
                await daemon.RebuildAll().ConfigureAwait(false);
            }

            _testHelper.CompareActiveProjects(theStore);
        }



        [Fact]
        public async Task do_a_complete_rebuild_of_the_project_count_with_seq_id_gap_at_101()
        {
            _testHelper.LoadTwoProjectsWithOneEventEach();

            StoreOptions(_ =>
            {
                _.Events.AddEventType(typeof(ProjectStarted));
                _.Events.AsyncProjections.Add(new ProjectCountProjection());
            });

            theStore.Schema.ApplyAllConfiguredChangesToDatabase();

            _testHelper.PublishAllProjectEvents(theStore, false);

            // Increment seq_id so events have a respective 1 and 102 seq_id
            using (var conn = theStore.Tenancy.Default.OpenConnection())
            {
                var command = conn.Connection.CreateCommand();
                command.CommandText = "UPDATE mt_events SET seq_id = 102 WHERE seq_id = 2";
                command.CommandType = System.Data.CommandType.Text;
                conn.Execute(command);
            }

            using (var daemon = theStore.BuildProjectionDaemon(
                logger: _logger,
                viewTypes: new Type[] { typeof(ProjectCountProjection) },
                settings: new DaemonSettings
                {
                    LeadingEdgeBuffer = 0.Seconds()
                }))
            {
                await daemon.Rebuild(typeof(ProjectCountProjection)).ConfigureAwait(false);
            }

            using (var session = theStore.LightweightSession())
            {
                session.Query<ProjectCountProjection>().Count().ShouldBe(2);
            }
        }

        public class ProjectCountProjection : IProjection
        {
            public Guid Id { get; set; }

            IDocumentSession _session;

            public Type[] Consumes { get; } = new Type[] { typeof(ProjectStarted) };
            public Type Produces { get; } = typeof(ProjectCountProjection);
            public AsyncOptions AsyncOptions { get; } = new AsyncOptions();
            public void Apply(IDocumentSession session, EventPage page)
            {
            }

            public Task ApplyAsync(IDocumentSession session, EventPage page, CancellationToken token)
            {
                _session = session;

                var projectEvents = page.Events.OrderBy(s => s.Sequence).Select(s => s.Data).OfType<ProjectStarted>();

                foreach (var e in projectEvents)
                {
                    Apply(e);
                }

                return Task.CompletedTask;
            }

            public void EnsureStorageExists(ITenant tenant)
            {
                
            }

            public int ProjectCount { get; set; }

            public void Apply(ProjectStarted @event)
            {
                var model = new ProjectCountProjection();
                model.ProjectCount++;
                _session.Store(model);
            }
        }

        public class OccasionalErroringProjection : IProjection
        {
            private readonly Random _random = new Random(5);
            private bool _failed;

            public Type[] Consumes { get; } = new Type[] {typeof(ProjectStarted), typeof(IssueCreated), typeof(IssueClosed), typeof(Commit)};
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
}