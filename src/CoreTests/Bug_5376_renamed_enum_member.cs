using System;
using System.Collections.Generic;
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
//
// The first two tests are @erdtsieck's from #5378, adopted unchanged. The rest cover the collection
// shapes, which go through the Weasel fragments rather than EnumAsStringMember and so kept the bug
// until weasel#591 gave those fragments a renderer.
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

    private void storeAsStrings() =>
        StoreOptions(opts =>
        {
            opts.UseSystemTextJsonForSerialization(EnumStorage.AsString);
            opts.Schema.For<Aanlevering>();
        });

    private async Task<Guid> aRenamedRow()
    {
        var id = Guid.NewGuid();
        theSession.Store(new Aanlevering { Id = id, Zorgvorm = Zorgvorm.Apotheek });
        await theSession.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task can_be_found_by_the_member_it_was_stored_as()
    {
        storeAsStrings();

        var id = await aRenamedRow();

        (await theSession.Json.FindByIdAsync<Aanlevering>(id)).ShouldContain("apotheek-houdend");

        var found = await theSession.Query<Aanlevering>().Where(x => x.Zorgvorm == Zorgvorm.Apotheek).ToListAsync();

        found.ShouldHaveSingleItem().Id.ShouldBe(id);
    }

    // A duplicated column is written by the document storage rather than by the serializer, so it holds
    // the declared name - and a query against it has to keep comparing against that, or the column and
    // the query stop agreeing. The body and the column disagreeing for a renamed member is a wider
    // change than this one, and it belongs with whatever writes the column. Tracked as #5388.
    [Fact]
    public async Task a_duplicated_column_keeps_comparing_against_the_declared_name()
    {
        StoreOptions(opts =>
        {
            opts.UseSystemTextJsonForSerialization(EnumStorage.AsString);
            opts.Schema.For<Aanlevering>().Duplicate(x => x.Zorgvorm);
        });

        var id = await aRenamedRow();

        var found = await theSession.Query<Aanlevering>().Where(x => x.Zorgvorm == Zorgvorm.Apotheek).ToListAsync();

        found.ShouldHaveSingleItem().Id.ShouldBe(id);
    }

    [Fact]
    public async Task is_one_of_finds_a_renamed_member()
    {
        storeAsStrings();
        var id = await aRenamedRow();

        var found = await theSession.Query<Aanlevering>()
            .Where(x => x.Zorgvorm.IsOneOf(Zorgvorm.Apotheek)).ToListAsync();

        found.ShouldHaveSingleItem().Id.ShouldBe(id);
    }

    [Fact]
    public async Task in_finds_a_renamed_member()
    {
        storeAsStrings();
        var id = await aRenamedRow();

        var found = await theSession.Query<Aanlevering>()
            .Where(x => x.Zorgvorm.In(Zorgvorm.Apotheek, Zorgvorm.Huisarts)).ToListAsync();

        found.ShouldHaveSingleItem().Id.ShouldBe(id);
    }

    [Fact]
    public async Task an_array_contains_finds_a_renamed_member()
    {
        storeAsStrings();
        var id = await aRenamedRow();

        var wanted = new[] { Zorgvorm.Apotheek };
        var found = await theSession.Query<Aanlevering>()
            .Where(x => wanted.Contains(x.Zorgvorm)).ToListAsync();

        found.ShouldHaveSingleItem().Id.ShouldBe(id);
    }

    [Fact]
    public async Task a_hashset_contains_finds_a_renamed_member()
    {
        storeAsStrings();
        var id = await aRenamedRow();

        var wanted = new HashSet<Zorgvorm> { Zorgvorm.Apotheek };
        var found = await theSession.Query<Aanlevering>()
            .Where(x => wanted.Contains(x.Zorgvorm)).ToListAsync();

        found.ShouldHaveSingleItem().Id.ShouldBe(id);
    }

    [Fact]
    public async Task a_list_contains_finds_a_renamed_member()
    {
        storeAsStrings();
        var id = await aRenamedRow();

        var wanted = new List<Zorgvorm> { Zorgvorm.Apotheek };
        var found = await theSession.Query<Aanlevering>()
            .Where(x => wanted.Contains(x.Zorgvorm)).ToListAsync();

        found.ShouldHaveSingleItem().Id.ShouldBe(id);
    }

    [Fact]
    public async Task the_declared_name_no_longer_matches_a_renamed_member()
    {
        // The other half of the contract: "Apotheek" is not in the data, so nothing should come back
        // for it. Without this, a fix that rendered both names would look green.
        storeAsStrings();
        await aRenamedRow();

        var found = await theSession.Query<Aanlevering>()
            .Where(x => x.Zorgvorm == Zorgvorm.Huisarts).ToListAsync();

        found.ShouldBeEmpty();
    }

    public enum Bijzonder
    {
        Gewoon,

        // System.Text.Json's default encoder escapes '&' as &, so ToCleanJson hands back
        // "a&b". Trimming the quotes off that leaves the escape in place and matches nothing;
        // the name has to be decoded, not trimmed.
        [JsonStringEnumMemberName("a&b")]
        Ampersand
    }

    public class Bijzonderheid
    {
        public Guid Id { get; set; }
        public Bijzonder Soort { get; set; }
    }

    [Fact]
    public async Task a_renamed_member_containing_an_escaped_character_is_decoded()
    {
        StoreOptions(opts =>
        {
            opts.UseSystemTextJsonForSerialization(EnumStorage.AsString);
            opts.Schema.For<Bijzonderheid>();
        });

        var id = Guid.NewGuid();
        theSession.Store(new Bijzonderheid { Id = id, Soort = Bijzonder.Ampersand });
        await theSession.SaveChangesAsync();

        var found = await theSession.Query<Bijzonderheid>()
            .Where(x => x.Soort == Bijzonder.Ampersand).ToListAsync();

        found.ShouldHaveSingleItem().Id.ShouldBe(id);
    }
}
