using System;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class Bug_276_Query_by_abstract_type_in_hierarchy: BugIntegrationContext
{
    public Bug_276_Query_by_abstract_type_in_hierarchy()
    {
        StoreOptions(_ =>
        {
            _.Schema.For<Activity>()
                .AddSubClass<StatusActivity>();
        });
    }

    public abstract class Activity
    {
        public Activity()
        {
            Id = Guid.NewGuid();
        }

        public Guid Id { get; set; }

        public abstract string Type { get; }
    }

    public class StatusActivity: Activity
    {
        public override string Type
        {
            get { return "StatusUpdate"; }
        }

        public string StatusText { get; set; }
    }

    [Fact]
    public async Task persist_and_load_subclass_with_abstract_parent()
    {
        var activity = new StatusActivity()
        {
            Id = Guid.NewGuid(),
            StatusText = "testing status"
        };

        using (var session = theStore.IdentitySession())
        {
            session.Store(activity);
            await session.SaveChangesAsync();

            (await session.LoadAsync<Activity>(activity.Id)).ShouldBeSameAs(activity);
            (await session.LoadAsync<StatusActivity>(activity.Id)).ShouldBeSameAs(activity);
        }

        using (var session = theStore.QuerySession())
        {
            (await session.LoadAsync<Activity>(activity.Id)).ShouldNotBeNull().ShouldNotBeSameAs(activity);
            (await session.LoadAsync<StatusActivity>(activity.Id)).ShouldNotBeNull().ShouldNotBeSameAs(activity);
        }
    }
}
