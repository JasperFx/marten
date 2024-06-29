using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Pagination;
using Marten.Schema.Identity;
using Marten.Testing.Harness;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_3274: BugIntegrationContext
{
    [Fact]
    public async Task multi_tenant_query_with_include_should_work()
    {
        StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Schema
                .For<User3274>()
                .Metadata(a => a.TenantId.MapTo(x => x.TenantId));
            opts.Schema.For<UserState3274>();
        });

        var newUser = new User3274()
        {
            Id = CombGuidIdGeneration.NewGuid(),
            Name = "Alex"
        };
        var newDoc = new UserState3274(newUser.Id, "TestState");

        var session = theStore.LightweightSession("tenant1");
        session.Store(newUser);
        session.Store(newDoc);
        await session.SaveChangesAsync();

        var userDict = new Dictionary<Guid, UserState3274>();

        var document = await session
            .Query<User3274>()
            .Include(x => x.Id, userDict)
            .Where(a => a.Id == newUser.Id)
            .ToPagedListAsync(1, 10);


        Assert.Single(document);
        Assert.Single(userDict);

        Assert.Equal(document.Single().Id, userDict.Single().Key);
    }


}

public record UserState3274(Guid Id, string State);
public class User3274
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string TenantId { get; set; }
}
