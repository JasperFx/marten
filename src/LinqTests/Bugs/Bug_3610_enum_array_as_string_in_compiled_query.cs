using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten;
using Marten.Linq;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;

namespace LinqTests.Bugs;

public class Bug_3610_enum_array_as_string_in_compiled_query : BugIntegrationContext
{
    [Fact]
    public async Task use_the_query()
    {
        StoreOptions(opts =>
        {
            opts.UseNewtonsoftForSerialization(enumStorage: EnumStorage.AsString,
                nonPublicMembersStorage: NonPublicMembersStorage.NonPublicSetters);
        });

        var elayne = new Foo { Name = "Elayne", Status = StatusEnum.Active };
        theSession.Store(elayne);
        await theSession.SaveChangesAsync();

        var results = await theSession.QueryAsync(new ActiveFooQuery("Elayne"));

        results.Any().ShouldBeTrue();
    }
}

public class ActiveFooQuery : ICompiledListQuery<Foo>, IQueryPlanning
{
    public string Name { get; set; }
    public ActiveFooQuery()
    {

    }

    public ActiveFooQuery(string name)
    {
        Name = name;
    }

    public Expression<Func<IMartenQueryable<Foo>, IEnumerable<Foo>>> QueryIs() =>
        q => q.Where(x => x.Status.In(StatusEnum.Active) && x.Name == Name);

    public void SetUniqueValuesForQueryPlanning()
    {
        Name = "ActiveFooQuery";
    }
}

public class Foo
{
    public Guid Id { get; set; }
    public StatusEnum Status { get; set; }
    public string Name { get; set; }
}

public enum StatusEnum
{
    Active,
    Inactive
}
