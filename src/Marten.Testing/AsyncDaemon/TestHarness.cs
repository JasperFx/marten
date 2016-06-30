using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using CodeTracker;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Events.Projections.Async;
using Xunit;

namespace Marten.Testing.AsyncDaemon
{
    public class TestHarness : IntegratedFixture
    {
        static TestHarness()
        {
            var folder = AppDomain.CurrentDomain.BaseDirectory.ParentDirectory().ParentDirectory().ParentDirectory()
                .AppendPath("CodeTracker");

            var files = new FileSystem().FindFiles(folder, FileSet.Shallow("*.json"));

            AllProjects = new Dictionary<Guid, GithubProject>();
            foreach (var file in files)
            {
                var project = GithubProject.LoadFrom(file);
                AllProjects.Add(project.Id, project);

                Console.WriteLine($"Loaded {project.OrganizationName}{project.ProjectName} from JSON");
            }

            PublishAllProjectEvents(ExpectedStore);
        }

        public static Dictionary<Guid, GithubProject> AllProjects { get; }


        public static IDocumentStore ExpectedStore = TestingDocumentStore.For(_ =>
        {
            _.DatabaseSchemaName = "expected";
            _.Events.DatabaseSchemaName = "expected";

            _.Events.InlineProjections.AggregateStreamsWith<ActiveProject>();
            _.Events.InlineProjections.TransformEvents(new CommitViewTransform());
        });


        public static void PublishAllProjectEvents(IDocumentStore store)
        {
            var tasks = AllProjects.Values.Select(project => project.PublishEvents(store, 0)).ToArray();

            Task.WaitAll(tasks.ToArray());
        }


        private static IDictionary<string, ActiveProject> fetchProjects(IDocumentStore store)
        {
            var dict = new Dictionary<string, ActiveProject>();

            using (var session = store.QuerySession())
            {
                session.Query<ActiveProject>().ToList().Each(proj => dict.Add(proj.ProjectName, proj));
            }

            return dict;
        }

        public static void CompareActiveProjects(IDocumentStore store)
        {
            var expected = fetchProjects(ExpectedStore);
            var actual = fetchProjects(store);

            var list = new List<string>();

            expected.Each(pair =>
            {
                if (actual.ContainsKey(pair.Key))
                {
                    var actualProject = actual[pair.Key];
                    if (!pair.Value.Equals(actualProject))
                    {
                        list.Add($"Expected {pair.Value}, but got {actualProject}");
                    }
                }
                else
                {
                    list.Add($"Missing project '{pair.Key}'");
                }
            });


            if (list.Any())
            {
                throw new Exception($"Differences in ActiveProjects:\n{list.Join("\n")}");
            }
            else
            {
                Console.WriteLine("");
                Console.WriteLine("");
                Console.WriteLine("==============================");
                Console.WriteLine("All ActiveProject views match!");
                Console.WriteLine("==============================");
            }

        }

 


        public async Task do_a_complete_rebuild_of_the_active_projects_from_scratch()
        {
            PublishAllProjectEvents(theStore);

            var projection = new AggregationProjection<ActiveProject>(new AggregateFinder<ActiveProject>(), new Aggregator<ActiveProject>());
            var build = new CompleteRebuild(new DaemonOptions(theStore.Schema.Events), theStore, projection);

            var last = await build.PerformRebuild(new CancellationToken()).ConfigureAwait(false);

            Console.WriteLine(last);

            build.Dispose();

            CompareActiveProjects(theStore);

            build.Dispose();
        }

    }
}