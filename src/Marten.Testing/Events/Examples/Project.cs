using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten.Events;
using Marten.Events.Projections;
using Microsoft.Extensions.DependencyInjection;

namespace Marten.Testing.Events.Examples
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMarten(opts =>
            {
                opts.Connection("some connection");

                // Direct Marten to update the Project aggregate
                // inline as new events are captured
                opts
                    .Projections
                    .SelfAggregate<Project>(ProjectionLifecycle.Inline);

            });
        }
    }

    public class NewProjectCommand
    {
        public string Name { get; set; }
        public string[] Tasks { get; set; }
        public string ProjectId { get; set; }
    }

    public class NewProjectHandler
    {
        private readonly IDocumentSession _session;

        public NewProjectHandler(IDocumentSession session)
        {
            _session = session;
        }

        public Task Handle(NewProjectCommand command)
        {
            var timestamp = DateTimeOffset.UtcNow;
            var started = new ProjectStarted {Name = command.Name, Timestamp = timestamp};

            var tasks = command.Tasks
                .Select(name => new TaskRecorded {Timestamp = timestamp, Title = name});


            _session.Events.StartStream(command.ProjectId, started);
            _session.Events.Append(command.ProjectId, tasks);

            return _session.SaveChangesAsync();
        }
    }

    public class Project
    {
        private readonly IList<ProjectTask> _tasks = new List<ProjectTask>();

        public Project(ProjectStarted started)
        {
            Version = 1;
            Name = started.Name;
            StartedTime = started.Timestamp;
        }

        // This gets set by Marten
        public Guid Id { get; set; }

        public long Version { get; set; }
        public DateTimeOffset StartedTime { get; private set; }
        public DateTimeOffset? CompletedTime { get; private set; }

        public string Name { get; private set; }

        public ProjectTask[] Tasks
        {
            get
            {
                return _tasks.ToArray();
            }
            set
            {
                _tasks.Clear();
                _tasks.AddRange(value);
            }
        }

        public void Apply(TaskRecorded recorded, IEvent e)
        {
            Version = e.Version;
            var task = new ProjectTask
            {
                Title = recorded.Title,
                Number = _tasks.Max(x => x.Number) + 1,
                Recorded = recorded.Timestamp
            };

            _tasks.Add(task);
        }

        public void Apply(TaskStarted started, IEvent e)
        {
            // Update the Project document based on the event version
            Version = e.Version;
            var task = _tasks.FirstOrDefault(x => x.Number == started.Number);

            // Remember this isn't production code:)
            if (task != null) task.Started = started.Timestamp;
        }

        public void Apply(TaskFinished finished, IEvent e)
        {
            Version = e.Version;
            var task = _tasks.FirstOrDefault(x => x.Number == finished.Number);

            // Remember this isn't production code:)
            if (task != null) task.Finished = finished.Timestamp;
        }

        public void Apply(ProjectCompleted completed, IEvent e)
        {
            Version = e.Version;
            CompletedTime = completed.Timestamp;
        }
    }

    public class ProjectTask
    {
        public string Title { get; set; }
        public DateTimeOffset? Started { get; set; }
        public DateTimeOffset? Finished { get; set; }
        public int Number { get; set; }
        public DateTimeOffset Recorded { get; set; }
    }

    public class ProjectStarted
    {
        public string Name { get; set; }
        public DateTimeOffset Timestamp { get; set; }
    }

    public class TaskRecorded
    {
        public string Title { get; set; }
        public DateTimeOffset Timestamp { get; set; }
    }

    public class TaskStarted
    {
        public int Number { get; set; }
        public DateTimeOffset Timestamp { get; set; }
    }

    public class TaskFinished
    {
        public int Number { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
    }

    public class ProjectCompleted
    {
        public DateTimeOffset Timestamp { get; set; }
    }

    public class CreateTaskCommand
    {
        public string ProjectId { get; set; }
        public string Title { get; set; }
    }


    public class CreateTaskHandler
    {
        private readonly IDocumentSession _session;

        public CreateTaskHandler(IDocumentSession session)
        {
            _session = session;
        }

        public Task Handle(CreateTaskCommand command)
        {
            var recorded = new TaskRecorded
            {
                Timestamp = DateTimeOffset.UtcNow,
                Title = command.Title
            };

            _session.Events.Append(command.ProjectId, recorded);
            return _session.SaveChangesAsync();
        }
    }

    public class CompleteTaskCommand
    {
        public string ProjectId { get; set; }
        public int TaskNumber { get; set; }

        // This is the version of the project data
        // that was being edited in the user interface
        public long ExpectedVersion { get; set; }
    }

    public class CompleteTaskHandler
    {
        private readonly IDocumentSession _session;

        public CompleteTaskHandler(IDocumentSession session)
        {
            _session = session;
        }

        public Task Handle(CompleteTaskCommand command)
        {
            var @event = new TaskFinished
            {
                Number = command.TaskNumber,
                Timestamp = DateTimeOffset.UtcNow
            };

            _session.Events.Append(
                command.ProjectId,

                // Using this overload will make Marten do
                // an optimistic concurrency check against
                // the existing version of the project event
                // stream as it commits
                command.ExpectedVersion,
                @event);
            return _session.SaveChangesAsync();
        }
    }

    public class CompleteTaskHandler2
    {
        private readonly IDocumentSession _session;

        public CompleteTaskHandler2(IDocumentSession session)
        {
            _session = session;
        }

        public Task Handle(CompleteTaskCommand command)
        {
            var @event = new TaskFinished
            {
                Number = command.TaskNumber,
                Timestamp = DateTimeOffset.UtcNow
            };

            // If some other process magically zips
            // in and updates this project event stream
            // between the call to AppendOptimistic()
            // and SaveChangesAsync(), Marten will detect
            // that and reject the transaction
            _session.Events.AppendOptimistic(
                command.ProjectId,
                @event);
            return _session.SaveChangesAsync();
        }
    }

    public class CompleteTaskHandler3
    {
        private readonly IDocumentSession _session;

        public CompleteTaskHandler3(IDocumentSession session)
        {
            _session = session;
        }

        public Task Handle(CompleteTaskCommand command)
        {
            var @event = new TaskFinished
            {
                Number = command.TaskNumber,
                Timestamp = DateTimeOffset.UtcNow
            };

            // This tries to acquire an exclusive
            // lock on the stream identified by
            // command.ProjectId in the database
            // so that only one process at a time
            // can update this event stream
            _session.Events.AppendExclusive(
                command.ProjectId,
                @event);
            return _session.SaveChangesAsync();
        }
    }
}
