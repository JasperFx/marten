using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Xunit;

namespace EventSourcingTests.Bugs;

public class CodeGenIEventIssue: BugIntegrationContext
{
    [Fact]
    public async Task TestAggregation()
    {
        var store = StoreOptions(_ =>
        {
            _.Projections.Add(new FooProjection(), ProjectionLifecycle.Inline);
        });

        using var session = store.LightweightSession();
        session.Events.Append(Guid.NewGuid(), new FooCreated { Id = Guid.NewGuid() });
        await session.SaveChangesAsync();
    }

    [Fact]
    public async Task TestRecordAggregation()
    {
        var store = StoreOptions(_ =>
        {
            _.Projections.Add(new RecordProjection(), ProjectionLifecycle.Inline);
        });

        using var session = store.LightweightSession();
        var id = Guid.NewGuid();
        session.Events.Append(id, new RecordLogCreated(id));
        session.Events.Append(id, new RecordLogUpdated(id));

        await session.SaveChangesAsync();
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

public class FooProjection: MultiStreamProjection<FooAuditLog, Guid>
{
    public FooProjection()
    {
        ProjectionName = nameof(FooAuditLog);

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

public class RecordProjection: MultiStreamProjection<RecordAuditLog, Guid>
{
    public RecordProjection()
    {
        ProjectionName = nameof(RecordAuditLog);

        Identity<IRecordLogEvent>(x => x.Id);

        CreateEvent<RecordLogCreated>(x => new RecordAuditLog(x.Id, new List<string>()));
        ProjectEvent<IEvent<RecordLogUpdated>>((state, ev) => state.Changes.Add($"Log was updated at {ev.Timestamp}"));
    }
}
