using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Services.Json;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit.Abstractions;

namespace LinqTests.Acceptance;

public class casing_support: OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    [Theory]
    [InlineData(Casing.CamelCase)]
    [InlineData(Casing.SnakeCase)]
    // I needed to comment that out, because JSON.NET is trying to be freaking smart and cache settings statically.
    // Luckily, not a big issue, as the default setting is verified in other tests
    //[InlineData(SerializerType.Newtonsoft, Casing.Default)]
    public async Task serializer_casing_should_work_with_selects_newtonsoft( Casing casing)
    {
        var store = StoreOptions(opts =>
        {
            opts.UseNewtonsoftForSerialization(
                casing: casing,
                nonPublicMembersStorage: NonPublicMembersStorage.All,
                enumStorage: EnumStorage.AsString
            );

            opts.Logger(new TestOutputMartenLogger(_output));
        });

        await using var session = store.LightweightSession();

        var targets = new[]
        {
            new Target { Id = Guid.NewGuid(), Number = 1, Color = Colors.Green },
            new Target { Id = Guid.NewGuid(), Number = 2, Color = Colors.Red }
        };
        session.Store(targets);
        await session.SaveChangesAsync();

        var result = await session.Query<Target>()
            .Where(t => t.Id == targets[0].Id)
            .Select(t => new { t.Id, t.Number, t.Color })
            .ToListAsync();

        result.Count.ShouldBe(1);
        result[0].Id.ShouldBe(targets[0].Id);
        result[0].Number.ShouldBe(targets[0].Number);
        result[0].Color.ShouldBe(targets[0].Color);
    }

    [Theory]
    [InlineData(Casing.CamelCase)]
    [InlineData(Casing.SnakeCase)]
    [InlineData(Casing.Default)]
    // I needed to comment that out, because JSON.NET is trying to be freaking smart and cache settings statically.
    // Luckily, not a big issue, as the default setting is verified in other tests
    //[InlineData(SerializerType.Newtonsoft, Casing.Default)]
    public async Task serializer_casing_should_work_with_selects_STJ( Casing casing)
    {
        var store = StoreOptions(opts =>
        {
            opts.UseSystemTextJsonForSerialization(casing:casing, enumStorage:EnumStorage.AsString);

            opts.Logger(new TestOutputMartenLogger(_output));
        });

        await using var session = store.LightweightSession();

        var targets = new[]
        {
            new Target { Id = Guid.NewGuid(), Number = 1, Color = Colors.Green },
            new Target { Id = Guid.NewGuid(), Number = 2, Color = Colors.Red }
        };
        session.Store(targets);
        await session.SaveChangesAsync();

        var result = await session.Query<Target>()
            .Where(t => t.Id == targets[0].Id)
            .Select(t => new { t.Id, t.Number, t.Color })
            .ToListAsync();

        result.Count.ShouldBe(1);
        result[0].Id.ShouldBe(targets[0].Id);
        result[0].Number.ShouldBe(targets[0].Number);
        result[0].Color.ShouldBe(targets[0].Color);
    }

    public casing_support(ITestOutputHelper output)
    {
        _output = output;
    }
}
