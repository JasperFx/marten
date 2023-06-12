#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten;
using Marten.Testing.Harness;
using Newtonsoft.Json;
using Xunit;

namespace CoreTests.Bugs;

public class Bug_9002_include_with_conjoined_tenancy: BugIntegrationContext
{
    [Fact]
    public async Task multi_tenant_query_with_include_should_work()
    {
        StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.RegisterDocumentType<User>();
            opts.RegisterDocumentType<Document>();
        });

        var newUser = new User(CombGuidIdGeneration.NewGuid(), "Alex");
        var newDoc = new Document(CombGuidIdGeneration.NewGuid(), "My Story", newUser.Id);

        var session = theStore.LightweightSession("tenant1");
        session.Store(newUser);
        session.Store(newDoc);
        await session.SaveChangesAsync();

        var userDict = new Dictionary<Guid, User>();

        var document = await session.Query<Document>().Include(x => x.Author, userDict).ToListAsync();

        Assert.Single(document);
        Assert.Single(userDict);

        Assert.Equal(document.Single().Author, userDict.Single().Key);
    }

    public record Document(Guid Id, string Title, Guid Author);
    public record User(Guid Id, string Name);
}
