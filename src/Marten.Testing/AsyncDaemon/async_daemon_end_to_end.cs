using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Events.Projections.Async;
using Marten.Testing.CodeTracker;
using Xunit;
using Xunit.Abstractions;
using System.Linq;
using Shouldly;

namespace Marten.Testing.AsyncDaemon
{
    public class async_daemon_end_to_end : IntegratedFixture, IClassFixture<AsyncDaemonFixture>
    {
        
        public async_daemon_end_to_end(AsyncDaemonFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _logger = new TracingLogger(output.WriteLine);
        }
        
//        public async_daemon_end_to_end()
//        {
//            _fixture = new AsyncDaemonFixture();
//            _logger = new ConsoleDaemonLogger();
//        }

        private readonly AsyncDaemonFixture _fixture;
        private readonly IDaemonLogger _logger;

        [Fact] 
        public async Task build_continuously_as_events_flow_in()
        {
            _fixture.LoadAllProjects();

            StoreOptions(_ => { _.Events.AsyncProjections.AggregateStreamsWith<ActiveProject>(); });

            using (var daemon = theStore.BuildProjectionDaemon(logger: _logger, settings: new DaemonSettings
            {
                LeadingEdgeBuffer = 1.Seconds()
            }))
            {
                daemon.StartAll();

                await _fixture.PublishAllProjectEventsAsync(theStore, true);
                //_fixture.PublishAllProjectEvents(theStore);

                // Runs all projections until there are no more events coming in
                await daemon.WaitForNonStaleResults().ConfigureAwait(false);

                await daemon.StopAll().ConfigureAwait(false);
            }


            _fixture.CompareActiveProjects(theStore);
        }

        [Fact]
        public async Task do_a_complete_rebuild_of_the_active_projects_from_scratch()
        {
            _fixture.LoadAllProjects();

            StoreOptions(_ => { _.Events.AsyncProjections.AggregateStreamsWith<ActiveProject>(); });

            _fixture.PublishAllProjectEvents(theStore, true);


            using (var daemon = theStore.BuildProjectionDaemon(logger: _logger, settings: new DaemonSettings
            {
                LeadingEdgeBuffer = 0.Seconds()
            }))
            {
                await daemon.Rebuild<ActiveProject>().ConfigureAwait(false);
            }

            _fixture.CompareActiveProjects(theStore);
        }

        [Fact]
        public async Task run_with_error_handling()
        {
            _fixture.LoadAllProjects();

            StoreOptions(_ =>
            {
                _.Events.AsyncProjections.AggregateStreamsWith<ActiveProject>();
                _.Events.AsyncProjections.Add(new OccasionalErroringProjection());
            });

            var settings = new DaemonSettings
            {
                LeadingEdgeBuffer = 0.Seconds()
            };

            settings.ExceptionHandling.OnException<DivideByZeroException>().Retry(3);


            using (var daemon = theStore.BuildProjectionDaemon(logger: _logger, settings: settings))
            {
                daemon.StartAll();

                await _fixture.PublishAllProjectEventsAsync(theStore, true);
                //_fixture.PublishAllProjectEvents(theStore);

                // Runs all projections until there are no more events coming in
                await daemon.WaitForNonStaleResults().ConfigureAwait(false);

                await daemon.StopAll().ConfigureAwait(false);
            }

            _fixture.CompareActiveProjects(theStore);
        }


        [Fact]
        public async Task build_continuously_as_events_flow_in_on_other_schema()
        {
            _fixture.LoadAllProjects();

            StoreOptions(_ =>
            {
                _.Events.AsyncProjections.AggregateStreamsWith<ActiveProject>();
                _.Events.DatabaseSchemaName = "events";
            });

            using (var daemon = theStore.BuildProjectionDaemon(logger: _logger, settings: new DaemonSettings
            {
                LeadingEdgeBuffer = 1.Seconds()
            }))
            {
                daemon.StartAll();

                await _fixture.PublishAllProjectEventsAsync(theStore, true);
                //_fixture.PublishAllProjectEvents(theStore);

                // Runs all projections until there are no more events coming in
                await daemon.WaitForNonStaleResults().ConfigureAwait(false);

                await daemon.StopAll().ConfigureAwait(false);
            }


            _fixture.CompareActiveProjects(theStore);
        }

        [Fact]
        public async Task do_a_complete_rebuild_of_the_active_projects_from_scratch_on_other_schema_single_event()
        {
            _fixture.LoadSingleProjects();

            StoreOptions(_ =>
            {
                _.Events.AsyncProjections.AggregateStreamsWith<ActiveProject>();
                _.Events.DatabaseSchemaName = "events";
            });

            _fixture.PublishAllProjectEvents(theStore, true);


            

            using (var daemon = theStore.BuildProjectionDaemon(logger: _logger, settings: new DaemonSettings
            {
                LeadingEdgeBuffer = 0.Seconds()
            }))
            {
                await daemon.Rebuild<ActiveProject>().ConfigureAwait(false);
            }

            _fixture.CompareActiveProjects(theStore);
            
        }

        [Fact]
        public async Task do_a_complete_rebuild_of_the_active_projects_from_scratch_on_other_schema()
        {
            _fixture.LoadAllProjects();

            StoreOptions(_ =>
            {
                _.Events.AsyncProjections.AggregateStreamsWith<ActiveProject>();
                _.Events.DatabaseSchemaName = "events";
            });

            _fixture.PublishAllProjectEvents(theStore, true);


            using (var daemon = theStore.BuildProjectionDaemon(logger: _logger, settings: new DaemonSettings
            {
                LeadingEdgeBuffer = 0.Seconds()
            }))
            {
                await daemon.Rebuild<ActiveProject>().ConfigureAwait(false);
            }

            _fixture.CompareActiveProjects(theStore);
        }

        //[Fact] Not super duper reliable when running back to back
        public async Task do_a_complete_rebuild_of_the_active_projects_from_scratch_twice_on_other_schema()
        {
            _fixture.LoadAllProjects();

            StoreOptions(_ =>
            {
                _.Events.AsyncProjections.AggregateStreamsWith<ActiveProject>();
                _.Events.DatabaseSchemaName = "events";
            });

            _fixture.PublishAllProjectEvents(theStore, true);

            using (var daemon = theStore.BuildProjectionDaemon(logger: _logger, settings: new DaemonSettings
            {
                LeadingEdgeBuffer = 0.Seconds()
            }))
            {
                await daemon.RebuildAll().ConfigureAwait(false);
                await daemon.RebuildAll().ConfigureAwait(false);
            }

            _fixture.CompareActiveProjects(theStore);
        }

        [Fact]
        public async Task run_with_error_handling_on_other_schema()
        {
            _fixture.LoadAllProjects();

            StoreOptions(_ =>
            {
                _.Events.AsyncProjections.AggregateStreamsWith<ActiveProject>();
                _.Events.AsyncProjections.Add(new OccasionalErroringProjection());
                _.Events.DatabaseSchemaName = "events";
            });

            var settings = new DaemonSettings
            {
                LeadingEdgeBuffer = 0.Seconds()
            };

            settings.ExceptionHandling.OnException<DivideByZeroException>().Retry(3);


            using (var daemon = theStore.BuildProjectionDaemon(logger: _logger, settings: settings))
            {
                daemon.StartAll();

                await _fixture.PublishAllProjectEventsAsync(theStore, true);
                //_fixture.PublishAllProjectEvents(theStore);

                // Runs all projections until there are no more events coming in
                await daemon.WaitForNonStaleResults().ConfigureAwait(false);

                await daemon.StopAll().ConfigureAwait(false);
            }

            _fixture.CompareActiveProjects(theStore);
        }

        [Fact]
        public async Task do_a_complete_rebuild_and_check_if_load_and_query_return_document_and_increment_project_count()
        {
            _fixture.LoadSingleProjects();

            StoreOptions(_ => { _.Events.AsyncProjections.Add(new TestLoadAndQueryProjection()); });

            _fixture.PublishAllProjectEvents(theStore, true);

            using (var daemon = theStore.BuildProjectionDaemon(
                logger: _logger,
                viewTypes: new Type[] { typeof(TestLoadAndQueryProjection) },
                settings: new DaemonSettings
                {
                    LeadingEdgeBuffer = 0.Seconds()
                }))
            {
                await daemon.Rebuild(typeof(TestLoadAndQueryProjection)).ConfigureAwait(false);
            }

            using (var session = theStore.LightweightSession())
            {
                session.Query<TestLoadAndQueryProjection>().First().ProjectCount.ShouldBe(3);
            }
        }

        [Fact]
        public async Task do_a_complete_rebuild_of_the_project_count_with_seq_id_gap_at_100()
        {
            _fixture.LoadTwoProjectsWithOneEventEach();

            StoreOptions(_ => { _.Events.AsyncProjections.Add(new ProjectCountProjection()); });

            _fixture.PublishAllProjectEvents(theStore, true);

            // Increment seq_id so events have a respective 1 and 101 seq_id
            using (var conn = theStore.Advanced.OpenConnection())
            {
                var command = conn.Connection.CreateCommand();
                command.CommandText = "UPDATE mt_events SET seq_id = 20000 WHERE seq_id = 2";
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

        [Fact]
        public async Task do_a_complete_rebuild_of_the_project_count_with_seq_id_gap_at_101()
        {
            _fixture.LoadTwoProjectsWithOneEventEach();

            StoreOptions(_ =>
            {
                _.Events.AddEventType(typeof(ProjectStarted));
                _.Events.AsyncProjections.Add(new ProjectCountProjection());
            });

            theStore.Schema.ApplyAllConfiguredChangesToDatabase();

            _fixture.PublishAllProjectEvents(theStore, false);

            // Increment seq_id so events have a respective 1 and 102 seq_id
            using (var conn = theStore.Advanced.OpenConnection())
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

        public class TestLoadAndQueryProjection : IProjection
        {
            public Guid Id { get; set; }

            IDocumentSession _session;

            public Type[] Consumes { get; } = new Type[] { typeof(ProjectStarted) };
            public Type Produces { get; } = typeof(TestLoadAndQueryProjection);
            public AsyncOptions AsyncOptions { get; } = new AsyncOptions();
            public void Apply(IDocumentSession session, EventStream[] streams)
            {
            }

            public Task ApplyAsync(IDocumentSession session, EventStream[] streams, CancellationToken token)
            {
                _session = session;

                var projectEvents = streams.SelectMany(s => s.Events).OrderBy(s => s.Sequence).Select(s => s.Data);

                foreach (var e in projectEvents)
                {
                    Apply((ProjectStarted)e);
                }

                return Task.CompletedTask;
            }

            public int ProjectCount { get; set; }

            public void Apply(ProjectStarted @event)
            {
                var newId = Guid.NewGuid();
                var model = new TestLoadAndQueryProjection();
                model.Id = newId;
                model.ProjectCount++;
                _session.Store(model);

                var loadModel = _session.Load<TestLoadAndQueryProjection>(newId);
                loadModel.ProjectCount++;
                _session.Store(loadModel);

                var queryModel = _session.Query<TestLoadAndQueryProjection>().Where(_ => _.Id == newId).FirstOrDefault();
                if (queryModel != null)
                {
                    queryModel.ProjectCount++;
                    _session.Store(queryModel);
                }
            }
        }

        public class ProjectCountProjection : IProjection
        {
            public Guid Id { get; set; }

            IDocumentSession _session;

            public Type[] Consumes { get; } = new Type[] { typeof(ProjectStarted) };
            public Type Produces { get; } = typeof(ProjectCountProjection);
            public AsyncOptions AsyncOptions { get; } = new AsyncOptions();
            public void Apply(IDocumentSession session, EventStream[] streams)
            {
            }

            public Task ApplyAsync(IDocumentSession session, EventStream[] streams, CancellationToken token)
            {
                _session = session;

                var projectEvents = streams.SelectMany(s => s.Events).OrderBy(s => s.Sequence).Select(s => s.Data);

                foreach (var e in projectEvents)
                {
                    Apply((ProjectStarted)e);
                }

                return Task.CompletedTask;
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
            public void Apply(IDocumentSession session, EventStream[] streams)
            {
                
            }

            public Task ApplyAsync(IDocumentSession session, EventStream[] streams, CancellationToken token)
            {
                if (!_failed && _random.Next(0, 10) == 9)
                {
                    _failed = true;
                    throw new DivideByZeroException();
                }

                _failed = false;

                return Task.CompletedTask;
            }
        }

        public class FakeThing
        {
            public Guid Id;
        }
    }
}