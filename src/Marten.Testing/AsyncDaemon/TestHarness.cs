using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Baseline;
using CodeTracker;
using Marten.Util;
using Xunit.Abstractions;

namespace Marten.Testing.AsyncDaemon
{
    public class TestHarness : IntegratedFixture
    {

        public void generate_full_aggregates()
        {
            var folder = AppDomain.CurrentDomain.BaseDirectory.ParentDirectory().ParentDirectory().ParentDirectory()
                .AppendPath("CodeTracker");

            var files = new FileSystem().FindFiles(folder, FileSet.Shallow("*.json"));

            var projects = new Dictionary<Guid, GithubProject>();
            var tasks = new List<Task>();

            foreach (var file in files)
            {
                var project = GithubProject.LoadFrom(file);
                projects.Add(project.Id, project);

                Debug.WriteLine($"Loaded {project.OrganizationName}{project.ProjectName} from JSON");

                tasks.Add(project.PublishEvents(theStore, 0));
            }

            Task.WaitAll(tasks.ToArray());

            // TODO -- download all the CommitView documents and persist
            // TODO -- download all the ActiveProject documents and persist


        }
    }
}