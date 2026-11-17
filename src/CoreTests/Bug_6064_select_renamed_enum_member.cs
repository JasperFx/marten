using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace CoreTests;

// #6064: with EnumStorage.AsString a scalar projection of an enum read the stored text back with Enum.Parse,
// which only knows the declared member names. A member renamed with [JsonStringEnumMemberName] is stored under
// its new name (#5376), so Select(x => x.Zorgvorm) threw "Requested value 'apotheek-houdend' was not found",
// while Where() over the same member already matched it.
public class Bug_6064_select_renamed_enum_member: OneOffConfigurationsContext
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
        public Zorgvorm? Vorige { get; set; }
        public List<Zorgvorm> Historie { get; set; } = new();
    }

    private async Task<Guid> aRenamedRow()
    {
        StoreOptions(opts =>
        {
            opts.UseSystemTextJsonForSerialization(EnumStorage.AsString);
            opts.Schema.For<Aanlevering>();
        });

        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Aanlevering));

        var id = Guid.NewGuid();
        theSession.Store(new Aanlevering
        {
            Id = id,
            Zorgvorm = Zorgvorm.Apotheek,
            Vorige = Zorgvorm.Apotheek,
            Historie = [Zorgvorm.Huisarts, Zorgvorm.Apotheek]
        });
        await theSession.SaveChangesAsync();

        (await theSession.Json.FindByIdAsync<Aanlevering>(id)).ShouldContain("apotheek-houdend");

        return id;
    }

    [Fact]
    public async Task select_a_renamed_member()
    {
        await aRenamedRow();

        var zorgvormen = await theSession.Query<Aanlevering>().Select(x => x.Zorgvorm).ToListAsync();

        zorgvormen.ShouldHaveSingleItem().ShouldBe(Zorgvorm.Apotheek);
    }

    [Fact]
    public async Task select_a_renamed_member_in_a_batch()
    {
        await aRenamedRow();

        var batch = theSession.CreateBatchQuery();
        var zorgvormen = batch.Query<Aanlevering>().Select(x => x.Zorgvorm).ToList();
        await batch.Execute();

        (await zorgvormen).ShouldHaveSingleItem().ShouldBe(Zorgvorm.Apotheek);
    }

    [Fact]
    public async Task select_a_renamed_member_through_first()
    {
        await aRenamedRow();

        (await theSession.Query<Aanlevering>().Select(x => x.Zorgvorm).FirstAsync()).ShouldBe(Zorgvorm.Apotheek);
    }

    [Fact]
    public async Task select_a_nullable_renamed_member()
    {
        await aRenamedRow();

        var vorige = await theSession.Query<Aanlevering>().Select(x => x.Vorige).ToListAsync();

        vorige.ShouldHaveSingleItem().ShouldBe(Zorgvorm.Apotheek);
    }

    [Fact]
    public async Task select_many_over_renamed_members()
    {
        await aRenamedRow();

        var historie = await theSession.Query<Aanlevering>().SelectMany(x => x.Historie).ToListAsync();

        historie.ShouldBe([Zorgvorm.Huisarts, Zorgvorm.Apotheek]);
    }

    public enum Snake
    {
        FirstThing,
        SecondThing
    }

    public class WithSnake
    {
        public Guid Id { get; set; }
        public Snake Snake { get; set; }
    }

    [Fact]
    public async Task select_a_member_named_by_a_naming_policy()
    {
        // No attribute at all: the converter's naming policy is what renames the member, and Enum.Parse cannot
        // read "second_thing" back even ignoring case.
        StoreOptions(opts =>
        {
            opts.UseSystemTextJsonForSerialization(EnumStorage.AsString, configure: json =>
            {
                json.Converters.Insert(0, new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
            });
            opts.Schema.For<WithSnake>();
        });

        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(WithSnake));

        var id = Guid.NewGuid();
        theSession.Store(new WithSnake { Id = id, Snake = Snake.SecondThing });
        await theSession.SaveChangesAsync();

        (await theSession.Json.FindByIdAsync<WithSnake>(id)).ShouldContain("second_thing");

        (await theSession.Query<WithSnake>().Select(x => x.Snake).ToListAsync())
            .ShouldHaveSingleItem().ShouldBe(Snake.SecondThing);
    }
}
