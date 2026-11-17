using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Linq;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs;

public abstract class Bug5777Root
{
    public Guid Id { get; set; }
    public int Top { get; set; }
    public Bug5777Inner Inner { get; set; } = new();
}

public class Bug5777Sub: Bug5777Root;

public class Bug5777Inner
{
    public int Value { get; set; }
    public int NotDuplicated { get; set; }
    public Bug5777Deep Deep { get; set; } = new();
}

public class Bug5777Deep
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// #5777 — the follow-up to #4916. A subclass query resolved a duplicated field on a <em>nested</em> member
/// (<c>x.Inner.Value</c>) from JSONB, for filtering and ordering, while the parent query used the column.
/// The subclass inherited only members that are themselves column-backed, and <c>Inner</c> is a child
/// document, so the subclass built its own <c>Inner</c> that knew nothing of the parent's duplicated field.
/// </summary>
public class Bug_5777_subclass_nested_duplicated_field_uses_column: BugIntegrationContext
{
    private void configure() =>
        StoreOptions(o => o.Schema.For<Bug5777Root>()
            .AddSubClass<Bug5777Sub>()
            .Duplicate(x => x.Top)
            .Duplicate(x => x.Inner.Value)
            .Duplicate(x => x.Inner.Deep.Name));

    [Fact]
    public void subclass_filter_on_a_nested_duplicated_field_uses_the_column()
    {
        configure();

        var sql = theSession.Query<Bug5777Sub>().Where(x => x.Top == 1 && x.Inner.Value == 2)
            .ToCommand(FetchType.FetchMany).CommandText;

        sql.ShouldContain("d.top = :p0", Case.Insensitive);
        sql.ShouldContain("d.inner_value = :p1", Case.Insensitive);
        sql.ShouldNotContain("'Inner'", Case.Insensitive);
    }

    [Fact]
    public void subclass_ordering_on_a_nested_duplicated_field_uses_the_column()
    {
        configure();

        var sql = theSession.Query<Bug5777Sub>().OrderBy(x => x.Inner.Value)
            .ToCommand(FetchType.FetchMany).CommandText;

        sql.ShouldContain("order by d.inner_value", Case.Insensitive);
        sql.ShouldNotContain("'Inner'", Case.Insensitive);
    }

    [Fact]
    public void subclass_filter_on_a_deeper_nested_duplicated_field_uses_the_column()
    {
        configure();

        var sql = theSession.Query<Bug5777Sub>().Where(x => x.Inner.Deep.Name == "a")
            .ToCommand(FetchType.FetchMany).CommandText;

        sql.ShouldContain("d.inner_deep_name = :p0", Case.Insensitive);
        sql.ShouldNotContain("'Inner'", Case.Insensitive);
    }

    [Fact]
    public void subclass_filter_on_a_nested_member_that_is_not_duplicated_still_uses_jsonb()
    {
        configure();

        var sql = theSession.Query<Bug5777Sub>().Where(x => x.Inner.NotDuplicated == 3)
            .ToCommand(FetchType.FetchMany).CommandText;

        sql.ShouldContain("d.data -> 'Inner' ->> 'NotDuplicated'", Case.Insensitive);
    }

    [Fact]
    public void parent_query_is_unchanged()
    {
        configure();

        var sql = theSession.Query<Bug5777Root>().Where(x => x.Top == 1 && x.Inner.Value == 2)
            .OrderBy(x => x.Inner.Value)
            .ToCommand(FetchType.FetchMany).CommandText;

        sql.ShouldContain("d.inner_value = :p1", Case.Insensitive);
        sql.ShouldContain("order by d.inner_value", Case.Insensitive);
        sql.ShouldNotContain("'Inner'", Case.Insensitive);
    }

    [Fact]
    public async Task subclass_query_on_nested_duplicated_fields_round_trips()
    {
        configure();
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Bug5777Root));

        var match = new Bug5777Sub { Id = Guid.NewGuid(), Top = 1, Inner = new Bug5777Inner { Value = 2, NotDuplicated = 3, Deep = new Bug5777Deep { Name = "a" } } };
        var other = new Bug5777Sub { Id = Guid.NewGuid(), Top = 1, Inner = new Bug5777Inner { Value = 1, NotDuplicated = 4, Deep = new Bug5777Deep { Name = "b" } } };
        theSession.Store(match, other);
        await theSession.SaveChangesAsync();

        var filtered = await theSession.Query<Bug5777Sub>()
            .Where(x => x.Inner.Value == 2 && x.Inner.Deep.Name == "a" && x.Inner.NotDuplicated == 3)
            .ToListAsync();
        filtered.ShouldHaveSingleItem().Id.ShouldBe(match.Id);

        var ordered = await theSession.Query<Bug5777Sub>().OrderBy(x => x.Inner.Value).ToListAsync();
        ordered.Select(x => x.Id).ShouldBe([other.Id, match.Id]);
    }
}
