using System;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace CoreTests;

// #5376: with EnumStorage.AsString a query rendered the enum with Enum.GetName, which is the declared
// name. System.Text.Json stores the name from [JsonStringEnumMemberName], so a filter comparing a
// renamed member matched nothing at all — and said so as "no rows" rather than as an error.
public class Bug_5376_renamed_enum_member: OneOffConfigurationsContext
{
    public enum Zorgvorm
    {
        Huisarts,

        [JsonStringEnumMemberName("apotheek-houdend")]
        Apotheek
    }

    public class Aanlevering
    {
        public Guid Id { get; set; }
        public Zorgvorm Zorgvorm { get; set; }
    }

    [Fact]
    public async Task can_be_found_by_the_member_it_was_stored_as()
    {
        StoreOptions(opts =>
        {
            opts.UseSystemTextJsonForSerialization(EnumStorage.AsString);
            opts.Schema.For<Aanlevering>();
        });

        var id = Guid.NewGuid();
        theSession.Store(new Aanlevering { Id = id, Zorgvorm = Zorgvorm.Apotheek });
        await theSession.SaveChangesAsync();

        (await theSession.Json.FindByIdAsync<Aanlevering>(id)).ShouldContain("apotheek-houdend");

        var found = await theSession.Query<Aanlevering>().Where(x => x.Zorgvorm == Zorgvorm.Apotheek).ToListAsync();

        found.ShouldHaveSingleItem().Id.ShouldBe(id);
    }

    // A duplicated column is written by the document storage rather than by the serializer, so it holds
    // the declared name - and a query against it has to keep comparing against that, or the column and
    // the query stop agreeing. The body and the column disagreeing for a renamed member is a wider
    // change than this one, and it belongs with whatever writes the column.
    [Fact]
    public async Task a_duplicated_column_keeps_comparing_against_the_declared_name()
    {
        StoreOptions(opts =>
        {
            opts.UseSystemTextJsonForSerialization(EnumStorage.AsString);
            opts.Schema.For<Aanlevering>().Duplicate(x => x.Zorgvorm);
        });

        var id = Guid.NewGuid();
        theSession.Store(new Aanlevering { Id = id, Zorgvorm = Zorgvorm.Apotheek });
        await theSession.SaveChangesAsync();

        var found = await theSession.Query<Aanlevering>().Where(x => x.Zorgvorm == Zorgvorm.Apotheek).ToListAsync();

        found.ShouldHaveSingleItem().Id.ShouldBe(id);
    }
}
