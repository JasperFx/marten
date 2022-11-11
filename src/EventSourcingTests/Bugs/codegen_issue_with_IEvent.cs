using System;
using System.Collections.Generic;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Xunit;

namespace EventSourcingTests.Bugs;

public class CodeGenIEventIssue: BugIntegrationContext
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

public class FooProjection: MultiStreamAggregation<FooAuditLog, Guid>
{
    public FooProjection()
    {
        ProjectionName = nameof(FooAuditLog);
        Lifecycle = ProjectionLifecycle.Inline;

        Identity<FooCreated>(x => x.Id);

        ProjectEvent<IEvent<FooCreated>>((state, ev) => state.Changes.Add($"Foo was updated at {ev.Timestamp}"));
    }
}

public interface IRecordLogEvent
{
    Guid Id { get; init; }
}

public record RecordAuditLog(Guid Id, List<string> Changes);

public record RecordLogCreated(Guid Id): IRecordLogEvent;

public record RecordLogUpdated(Guid Id): IRecordLogEvent;

public class RecordProjection: MultiStreamAggregation<RecordAuditLog, Guid>
{
    public RecordProjection()
    {
        ProjectionName = nameof(RecordAuditLog);
        Lifecycle = ProjectionLifecycle.Inline;

        Identity<IRecordLogEvent>(x => x.Id);

        CreateEvent<RecordLogCreated>(x => new RecordAuditLog(x.Id, new List<string>()));
        ProjectEvent<IEvent<RecordLogUpdated>>((state, ev) => state.Changes.Add($"Log was updated at {ev.Timestamp}"));
    }
}
