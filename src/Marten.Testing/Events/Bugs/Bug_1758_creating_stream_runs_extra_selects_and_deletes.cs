using System;
using System.Collections.Generic;
using System.Linq;

using Bug1758;

using Marten.Events.Projections;
using Marten.Services;
using Marten.Testing.Harness;

using Npgsql;

using Shouldly;

using Xunit;

namespace Marten.Testing.Events.Bugs
{
    public class Bug_1758_creating_stream_runs_extra_selects_and_deletes : BugIntegrationContext
    {
        [Fact]
        public void should_not_run_selects_and_deletes_to_non_affected_aggregates()
        {
            var logger = new CollectingLogger();

            using var documentStore = SeparateStore(x =>
            {
                x.Events.Projections.SelfAggregate<AggregateA>(ProjectionLifecycle.Inline);
                x.Events.Projections.SelfAggregate<AggregateB>(ProjectionLifecycle.Inline);
                x.Events.Projections.SelfAggregate<AggregateC>(ProjectionLifecycle.Inline);
                x.Logger(logger);
            });

            documentStore.Advanced.Clean.CompletelyRemoveAll();

            using var session = documentStore.OpenSession();
            var id = session.Events.StartStream<AggregateA>(new CreateAEvent {Name = "Test"}).Id;
            session.SaveChanges();

            var commit = logger.LastCommit;

            commit.Deleted.Any().ShouldBeFalse();
        }

        private class CollectingLogger: IMartenLogger, IMartenSessionLogger
        {
            public IList<string> CommandTexts { get; } = new List<string>();

            public IMartenSessionLogger StartSession(IQuerySession session) => this;

            public void SchemaChange(string sql)
            {
            }

            public void LogSuccess(NpgsqlCommand command) => CommandTexts.Add(command.CommandText);
            public void LogFailure(NpgsqlCommand command, Exception ex) => CommandTexts.Add(command.CommandText);

            public void RecordSavedChanges(IDocumentSession session, IChangeSet commit)
            {
                LastCommit = commit;
            }

            public IChangeSet LastCommit { get; set; }

            public void OnBeforeExecute(NpgsqlCommand command)
            {
            }
        }
    }
}

namespace Bug1758
{
    public class CreateAEvent { public string Name { get; set; } }
    public class UpdateAEvent { public string NewName { get; set; } }

    public class CreateBEvent { public string Name { get; set; } }
    public class CreateCEvent { public string Name { get; set; } }

    public class AggregateA
    {
        public Guid Id { get; set; }
        public string Name { get; set; }

        public void Apply(CreateAEvent create) => Name = create.Name;
        public void Apply(UpdateAEvent update) => Name = update.NewName;
    }

    public class AggregateB
    {
        public Guid Id { get; set; }
        public string Name { get; set; }

        public void Apply(CreateBEvent create) => Name = create.Name;
    }

    public class AggregateC
    {
        public Guid Id { get; set; }
        public string Name { get; set; }

        public void Apply(CreateCEvent create) => Name = create.Name;
    }
}
