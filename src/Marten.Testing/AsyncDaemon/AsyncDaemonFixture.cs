using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten.Testing.CodeTracker;

namespace Marten.Testing.AsyncDaemon
{
    public class renamestuff
    {
        public void RenameClasses()
        {
            var folder = ".".ToFullPath().ParentDirectory().ParentDirectory()
                .AppendPath("CodeTracker");

            var fileSystem = new FileSystem();
            var files = fileSystem.FindFiles(folder, FileSet.Shallow("*.json"));

            

            foreach (var file in files)
            {
                var json = fileSystem.ReadStringFromFile(file);

                json = replace(json, "GithubProject");
                json = replace(json, "Timestamped[]");
                json = replace(json, "ProjectStarted");
                json = replace(json, "IssueCreated");
                json = replace(json, "IssueClosed");
                json = replace(json, "IssueReopened");
                json = replace(json, "Commit");


                fileSystem.WriteStringToFile(file, json);
            }

        }

        public string replace(string json, string className)
        {
            var pattern = $"CodeTracker.{className}, CodeTracker";
            var replacement = $"Marten.Testing.CodeTracker.{className}, Marten.Testing";

            return json.Replace(pattern, replacement);
        }
    }

    public class AsyncDaemonFixture : IDisposable
    {
        private readonly IDocumentStore _store;

        public AsyncDaemonFixture()
        {
            _store = TestingDocumentStore.For(_ =>
            {
                _.DatabaseSchemaName = "expected";
                _.Events.DatabaseSchemaName = "expected";

                _.Events.InlineProjections.AggregateStreamsWith<ActiveProject>();
                _.Events.InlineProjections.TransformEvents(new CommitViewTransform());
            });

            var folder = ".".ToFullPath().ParentDirectory().ParentDirectory()
                .AppendPath("CodeTracker");

            var files = new FileSystem().FindFiles(folder, FileSet.Shallow("*.json"));

            AllProjects = new Dictionary<Guid, GithubProject>();
            foreach (var file in files)
            {
                var project = GithubProject.LoadFrom(file);
                AllProjects.Add(project.Id, project);

                Console.WriteLine($"Loaded {project.OrganizationName}{project.ProjectName} from JSON");
            }

            PublishAllProjectEvents(_store);
        }



        public Dictionary<Guid, GithubProject> AllProjects { get; }

        public Task PublishAllProjectEventsAsync(IDocumentStore store)
        {
//            foreach (var track in AllProjects.Values)
//            {
//                await track.PublishEvents(store, 50).ConfigureAwait(false);
//            }

            var tasks = AllProjects.Values.Select(project => project.PublishEvents(store, 10)).ToArray();
            return Task.WhenAll(tasks);
        }

        public void PublishAllProjectEvents(IDocumentStore store)
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

        public void CompareActiveProjects(IDocumentStore store)
        {
            var expected = fetchProjects(_store);
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

        public void Dispose()
        {
            _store.Dispose();
        }
    }
}