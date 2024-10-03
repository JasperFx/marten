using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten;
using Marten.Storage;
using Marten.Testing.Harness;
using Xunit;
using Xunit.Abstractions;

namespace DocumentDbTests.Bugs;

public class Bug_3378_jsonb_ops_index_syntax : BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public Bug_3378_jsonb_ops_index_syntax(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task use_same_syntax_as_query()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<SomeModel>().Index(x => x.ChildCollection, index => index.ToGinWithJsonbPathOps());
            opts.Logger(new TestOutputMartenLogger(_output));
        });

        await theStore.Storage.Database.EnsureStorageExistsAsync(typeof(SomeModel));

        var results = await theSession.Query<SomeModel>().Where(x => x.ChildCollection.Any(c => c.Number == 1))
            .ToListAsync();

    }
}

public class SomeModel
{
    public Guid Id { get; set; }
    public List<ChildModel> ChildCollection { get; set; } = new();
}

public class ChildModel
{
    public string Name { get; set; } = Guid.NewGuid().ToString();
    public int Number { get; set; }
}
