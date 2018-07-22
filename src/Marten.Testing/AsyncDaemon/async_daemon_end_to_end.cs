using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Events.Projections.Async;
using Marten.Testing.CodeTracker;
using Xunit;
using Xunit.Abstractions;
using System.Linq;
using Baseline.Dates;
using Marten.Storage;
using Marten.Util;
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

            // SAMPLE: rebuild-single-projection
            using (var daemon = theStore.BuildProjectionDaemon(logger: _logger, settings: new DaemonSettings
            {
                LeadingEdgeBuffer = 0.Seconds()
            }))
            {
                await daemon.Rebuild<ActiveProject>().ConfigureAwait(false);
            }
            // ENDSAMPLE

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

        [Fact]
        public async Task use_projection_with_custom_projectionKey_name()
        {
            _testHelper.LoadTwoProjectsWithOneEventEach();

            var projection = new ProjectionWithCustomProjectionKeyName();
            StoreOptions(_ =>
            {
                _.Events.AddEventType(typeof(ProjectStarted));
                _.Events.AsyncProjections.Add(projection);
                _.Events.DatabaseSchemaName = "events";
            });

            theStore.Schema.ApplyAllConfiguredChangesToDatabase();

            _testHelper.PublishAllProjectEvents(theStore, false);

            using (var daemon = theStore.BuildProjectionDaemon(
                logger: _logger,
                viewTypes: new[] { typeof(ProjectionWithCustomProjectionKeyName) },
                settings: new DaemonSettings
                {
                    LeadingEdgeBuffer = 0.Seconds()
                }))
            {
                await daemon.Rebuild<ProjectionWithCustomProjectionKeyName>();
            }

            projection.Observed.Count.ShouldBe(2);
            using (var conn = theStore.Tenancy.Default.OpenConnection())
            {
                var command = conn.Connection.CreateCommand();
                
                command.Sql($"select last_seq_id from {theStore.Events.DatabaseSchemaName}.mt_event_progression where name = :name")
                    .With("name", projection.GetEventProgressionName());
                
                using (var reader = await command.ExecuteReaderAsync().ConfigureAwait(false))
                {
                    var any = await reader.ReadAsync().ConfigureAwait(false);
                    if (!any)
                    {
                        throw new Exception("No projection found");
                    }
                    
                    var lastEncountered = await reader.GetFieldValueAsync<long>(0);
                    lastEncountered.ShouldBe(2);
                }
            }
        }
        
        [Fact]
        public async Task custom_projection_with_customKeyName_can_fetch_current_state()
        {
            _testHelper.LoadTwoProjectsWithOneEventEach();

            var projection = new ProjectionWithCustomProjectionKeyName();
            StoreOptions(_ =>
            {
                _.Events.AddEventType(typeof(ProjectStarted));
                _.Events.AsyncProjections.Add(projection);
                _.Events.DatabaseSchemaName = "events";
            });

            theStore.Schema.ApplyAllConfiguredChangesToDatabase();

            _testHelper.PublishAllProjectEvents(theStore, false);

            using (var conn = theStore.Tenancy.Default.OpenConnection())
            {
                var command = conn.Connection.CreateCommand();
                
                command.Sql($"insert into {theStore.Events.DatabaseSchemaName}.mt_event_progression (last_seq_id, name) values (:seq, :name)")
                    .With("seq", 1)
                    .With("name", projection.GetEventProgressionName())
                    ;

                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            using (var daemon = theStore.BuildProjectionDaemon(
                logger: _logger,
                viewTypes: new[] { typeof(ProjectionWithCustomProjectionKeyName) },
                settings: new DaemonSettings
                {
                    LeadingEdgeBuffer = 0.Seconds()
                }))
            {
                daemon.Start<ProjectionWithCustomProjectionKeyName>(DaemonLifecycle.StopAtEndOfEventData);
                await daemon.WaitForNonStaleResultsOf<ProjectionWithCustomProjectionKeyName>();
            }

            projection.Observed.ShouldHaveSingleItem().ShouldBe(2);
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
        
        public class ProjectionWithCustomProjectionKeyName : IProjection, IHasCustomEventProgressionName
        {
            public Guid Id { get; set; }

            public Type[] Consumes { get; } = { typeof(ProjectStarted) };
            public AsyncOptions AsyncOptions { get; } = new AsyncOptions();
            public void Apply(IDocumentSession session, EventPage page)
            {
            }

            public Task ApplyAsync(IDocumentSession session, EventPage page, CancellationToken token)
            {
                foreach (var pageEvent in page.Events)
                {
                    Observed.Add(pageEvent.Sequence);
                }

                return Task.CompletedTask;
            }

            public void EnsureStorageExists(ITenant tenant)
            {
            }

            public string Name => "Custom_projection_key_name";
            
            public List<long> Observed { get; } = new List<long>();
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