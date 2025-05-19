using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class Bug_490_hierarchy_and_include: BugIntegrationContext
{
    public Bug_490_hierarchy_and_include()
    {
        StoreOptions(_ =>
        {
            _.Schema.For<Activity>()
                .AddSubClass<StatusActivity>();
        });
    }

    public class Account
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public abstract class Activity
    {
        public Activity()
        {
            Id = Guid.NewGuid();
        }

        public Guid Id { get; set; }

        public int AccountId { get; set; }

        public abstract string Type { get; }
    }

    public class StatusActivity: Activity
    {
        public override string Type => "StatusUpdate";
        public string StatusText { get; set; }
    }

    [Fact]
    public async Task load_abstract_type_with_include()
    {
        var account = new Account()
        {
            Id = 1,
            Name = "Paul"
        };

        theSession.Store(account);

        var activity = new StatusActivity()
        {
            Id = Guid.NewGuid(),
            StatusText = "testing status",
            AccountId = 1
        };

        theSession.Store(activity);
        await theSession.SaveChangesAsync();

        using (var session = theStore.QuerySession())
        {
            var accounts = new List<Account>();
            session.Query<Activity>()
                .Include(a => a.AccountId, accounts)
                .ToList()
                .ShouldNotBeNull()
                .ShouldNotBeSameAs(activity);

            accounts.First().Id.ShouldBe(1);
        }
    }

    [Fact]
    public async Task load_abstract_type_with_include_async()
    {
        var account = new Account()
        {
            Id = 1,
            Name = "Paul"
        };

        theSession.Store(account);

        var activity = new StatusActivity()
        {
            Id = Guid.NewGuid(),
            StatusText = "testing status",
            AccountId = 1
        };

        theSession.Store(activity);
        await theSession.SaveChangesAsync();

        await using (var session = theStore.QuerySession())
        {
            List<Account> accounts = new List<Account>();
            (await session.Query<Activity>()
                    .Include(a => a.AccountId, accounts)
                    .ToListAsync())
                .ShouldNotBeNull()
                .ShouldNotBeSameAs(activity);

            accounts.First().Id.ShouldBe(1);
        }
    }
}
