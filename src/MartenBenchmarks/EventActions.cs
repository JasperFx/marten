using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using BenchmarkDotNet.Attributes;
using Marten.Testing.CodeTracker;

namespace MartenBenchmarks
{
    [SimpleJob(warmupCount: 2)]
    public class EventActions
    {
        public Dictionary<Guid, GithubProject> AllProjects { get; private set; } = new Dictionary<Guid, GithubProject>();

        [GlobalSetup]
        public void Setup()
        {
            var fileSystem = new FileSystem();

            var folder = AppContext.BaseDirectory;
            while (!folder.EndsWith("src"))
            {
                folder = folder.ParentDirectory();
            }

            folder = folder.AppendPath("Marten.Testing","CodeTracker");

            var files = fileSystem.FindFiles(folder, FileSet.Shallow("*.json"));

            AllProjects = new Dictionary<Guid, GithubProject>();
            foreach (var file in files)
            {
                var project = GithubProject.LoadFrom(file);
                AllProjects.Add(project.Id, project);

                Console.WriteLine($"Loaded {project.OrganizationName}{project.ProjectName} from JSON");
            }


        }

        
        [Benchmark]
        [MemoryDiagnoser]
        public void AppendEvents()
        {
            var events = AllProjects.SelectMany(x => x.Value.Events).Take(1000).ToArray();
            using (var session = BenchmarkStore.Store.OpenSession())
            {
                session.Events.Append(Guid.NewGuid(), events);
                session.SaveChanges();
            }
        }


    }
}