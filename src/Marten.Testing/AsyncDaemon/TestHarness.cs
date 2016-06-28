using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using CodeTracker;
using Marten.Events;
using Marten.Util;
using Xunit.Abstractions;

namespace Marten.Testing.AsyncDaemon
{
    public class TestHarness : IntegratedFixture
    {
        public static bool HasBuiltExpectations = false;

        public static void RebuildExpectations()
        {
            if (HasBuiltExpectations) return;

            var store = TestingDocumentStore.For(_ =>
            {
                _.DatabaseSchemaName = "expected";
                _.Events.DatabaseSchemaName = "expected";

                _.Events.AggregateStreamsInlineWith<ActiveProject>();
                _.Events.TransformEventsInlineWith(new CommitViewTransform());
            });

            store.Schema.EnsureStorageExists(typeof(EventStream));

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

                tasks.Add(project.PublishEvents(store, 0));
            }

            Task.WaitAll(tasks.ToArray());

            HasBuiltExpectations = true;
        }

        public static void CompareAll()
        {
            // check that there are the same number of commit views
        }



    }
}