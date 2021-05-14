using System;
using System.Collections.Generic;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class CodeGenIEventIssue : BugIntegrationContext
    {
        [Fact]
        public void TestAggregation()
        {
            var store = StoreOptions(_ =>
            {
                _.Projections.Add(new FooProjection());
            });

            using var session = store.OpenSession();
            session.Events.Append(Guid.NewGuid(), new FooCreated { Id = Guid.NewGuid() });
            session.SaveChanges();
        }

#if NET
        [Fact]
        public void TestRecordAggregation()
        {
            var store = StoreOptions(_ =>
            {
                _.Projections.Add(new RecordProjection());
            });

            using var session = store.OpenSession();
            var id = Guid.NewGuid();
            session.Events.Append(id, new RecordLogCreated(id));
            session.Events.Append(id, new RecordLogUpdated(id));

            session.SaveChanges();
        }

#endif
    }
    public class FooCreated
    {
        public Guid Id { get; set; }
    }


    public class Foo
    {
        public Guid Id { get; set; }
    }

    public class FooAuditLog
    {
        public Guid Id { get; set; }
        public List<string> Changes { get; set; } = new List<string>();

    }

    public class FooProjection: ViewProjection<FooAuditLog, Guid>
    {
        public FooProjection()
        {
            ProjectionName = nameof(FooAuditLog);
            Lifecycle = ProjectionLifecycle.Inline;

            Identity<FooCreated>(x => x.Id);

            ProjectEvent<IEvent<FooCreated>>((state, ev) => state.Changes.Add($"Foo was updated at {ev.Timestamp}"));
        }
    }

#if NET


    public interface IRecordLogEvent
    {
        Guid Id { get; init; }
    }

    public record RecordAuditLog(Guid Id, List<string> Changes);

    public record RecordLogCreated(Guid Id): IRecordLogEvent;

    public record RecordLogUpdated(Guid Id): IRecordLogEvent;

    public class RecordProjection: ViewProjection<RecordAuditLog, Guid>
    {
        public RecordProjection()
        {
            ProjectionName = nameof(RecordAuditLog);
            Lifecycle = ProjectionLifecycle.Inline;

            Identity<IRecordLogEvent>(x=> x.Id);

            CreateEvent<RecordLogCreated>(x => new RecordAuditLog(x.Id, new List<string>()));
            ProjectEvent<IEvent<RecordLogUpdated>>((state, ev) => state.Changes.Add($"Log was updated at {ev.Timestamp}"));

        }
    }

#endif

}
