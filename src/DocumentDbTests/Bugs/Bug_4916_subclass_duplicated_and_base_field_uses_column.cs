using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Linq;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs;

public record Bug4916Animal(Guid Id, Guid FarmId);

public record Bug4916Cow(Guid Id, Guid FarmId): Bug4916Animal(Id, FarmId);

/// <summary>
/// #4916 — a subclass query that filters on a Duplicate()'d field or the base id emitted a JSONB filter
/// (<c>CAST(d.data -&gt;&gt; 'FarmId' as uuid)</c>) instead of the real column (<c>d.farm_id</c> / <c>d.id</c>).
/// A subclass shares the parent's table, but <c>SubClassMapping</c> built its own empty queryable-member
/// collection that never inherited the parent's duplicated fields or id member, so every member fell through
/// to the JSONB factory. Querying the parent type resolved the columns correctly the whole time.
/// </summary>
public class Bug_4916_subclass_duplicated_and_base_field_uses_column: BugIntegrationContext
{
    private void configure() =>
        StoreOptions(o => o.Schema.For<Bug4916Animal>().AddSubClass<Bug4916Cow>().Duplicate(x => x.FarmId));

    [Fact]
    public void subclass_query_on_a_duplicated_field_uses_the_column_not_jsonb()
    {
        configure();

        var id = Guid.NewGuid();
        var sql = theSession.Query<Bug4916Cow>().Where(x => x.FarmId == id)
            .ToCommand(FetchType.FetchMany).CommandText;

        sql.ShouldContain("farm_id = :p0", Case.Insensitive);
        sql.ShouldNotContain("data ->> 'FarmId'", Case.Insensitive);
    }

    [Fact]
    public void subclass_query_on_the_base_id_uses_the_id_column_not_jsonb()
    {
        configure();

        var id = Guid.NewGuid();
        var sql = theSession.Query<Bug4916Cow>().Where(x => x.Id == id)
            .ToCommand(FetchType.FetchMany).CommandText;

        sql.ShouldContain("d.id = :p0", Case.Insensitive);
        sql.ShouldNotContain("data ->> 'Id'", Case.Insensitive);
    }

    [Fact]
    public void parent_query_is_unchanged()
    {
        configure();

        var id = Guid.NewGuid();
        var sql = theSession.Query<Bug4916Animal>().Where(x => x.FarmId == id)
            .ToCommand(FetchType.FetchMany).CommandText;

        sql.ShouldContain("farm_id = :p0", Case.Insensitive);
        sql.ShouldNotContain("data ->> 'FarmId'", Case.Insensitive);
    }

    [Fact]
    public async Task subclass_query_on_a_duplicated_field_round_trips()
    {
        configure();

        var farmId = Guid.NewGuid();
        var cow = new Bug4916Cow(Guid.NewGuid(), farmId);
        theSession.Store(cow);
        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<Bug4916Cow>().Where(x => x.FarmId == farmId).ToListAsync();
        loaded.ShouldHaveSingleItem().Id.ShouldBe(cow.Id);

        var byId = await theSession.Query<Bug4916Cow>().Where(x => x.Id == cow.Id).ToListAsync();
        byId.ShouldHaveSingleItem().FarmId.ShouldBe(farmId);
    }
}
