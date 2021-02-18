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
                _.Events.Projections.Add(new FooProjection());
            });

            using var session = store.OpenSession();
            session.Events.Append(Guid.NewGuid(), new FooCreated { Id = Guid.NewGuid() });
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

}
