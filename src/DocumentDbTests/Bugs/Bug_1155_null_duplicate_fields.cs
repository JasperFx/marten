using System.Linq;
using System.Threading.Tasks;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_1155_null_duplicate_fields: BugIntegrationContext
{
    [Fact]
    public async Task when_enum_is_null_due_to_nullable_type()
    {
        StoreOptions(opts =>
        {
            opts.Serializer(new JsonNetSerializer { EnumStorage = EnumStorage.AsInteger });
            opts.Schema.For<Target>().Duplicate(t => t.NullableColor);
        });

        using (var session = theStore.LightweightSession())
        {
            session.Store(new Target
            {
                Number = 1,
                NullableColor = null
            });

            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            session.Query<Target>().Where(x => x.Number == 1)
                .ToArray()
                .Select(x => x.Number)
                .ShouldHaveTheSameElementsAs(1);
        }
    }

    [Fact]
    public async Task when_enum_is_null_due_to_nesting()
    {
        StoreOptions(opts =>
        {
            opts.Serializer(new JsonNetSerializer { EnumStorage = EnumStorage.AsInteger });
            opts.Schema.For<Target>().Duplicate(t => t.Inner.Color);
        });

        using (var session = theStore.LightweightSession())
        {
            session.Store(new Target
            {
                Number = 1,
                Inner = null
            });

            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            session.Query<Target>().Where(x => x.Number == 1)
                .ToArray()
                .Select(x => x.Number)
                .ShouldHaveTheSameElementsAs(1);
        }
    }

    [Fact]
    public async Task when_string_enum_is_null_due_to_nullable_type()
    {
        StoreOptions(_ =>
        {
            _.Serializer(new JsonNetSerializer { EnumStorage = EnumStorage.AsString });
            _.Schema.For<Target>().Duplicate(t => t.NullableColor);
        });

        using (var session = theStore.LightweightSession())
        {
            session.Store(new Target
            {
                Number = 1,
                NullableColor = null
            });

            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            session.Query<Target>().Where(x => x.Number == 1)
                .ToArray()
                .Select(x => x.Number)
                .ShouldHaveTheSameElementsAs(1);
        }
    }

    [Fact]
    public async Task when_string_enum_is_null_due_to_nesting()
    {
        StoreOptions(_ =>
        {
            _.Serializer(new JsonNetSerializer { EnumStorage = EnumStorage.AsString });
            _.Schema.For<Target>().Duplicate(t => t.Inner.Color);
        });

        using (var session = theStore.LightweightSession())
        {
            session.Store(new Target
            {
                Number = 1,
                Inner = null
            });

            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            session.Query<Target>().Where(x => x.Number == 1)
                .ToArray()
                .Select(x => x.Number)
                .ShouldHaveTheSameElementsAs(1);
        }
    }

    [Fact]
    public async Task when_field_is_not_null_due_to_nesting()
    {
        StoreOptions(_ => _.Schema.For<Target>().Duplicate(t => t.Inner.Number));

        using (var session = theStore.LightweightSession())
        {
            session.Store(new Target
            {
                Number = 1,
                Inner = new Target { Number = 2 }
            });

            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            session.Query<Target>().Where(x => x.Number == 1)
                .ToArray()
                .Select(x => x.Number)
                .ShouldHaveTheSameElementsAs(1);
        }
    }

    [Fact]
    public async Task when_field_is_null_due_to_nesting()
    {
        StoreOptions(_ => _.Schema.For<Target>().Duplicate(t => t.Inner.Number));

        using (var session = theStore.LightweightSession())
        {
            session.Store(new Target
            {
                Number = 1,
                Inner = null
            });

            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            session.Query<Target>().Where(x => x.Number == 1)
                .ToArray()
                .Select(x => x.Number)
                .ShouldHaveTheSameElementsAs(1);
        }
    }

    [Fact]
    public async Task when_bulk_inserting_and_field_is_null_due_to_nesting()
    {
        StoreOptions(_ => _.Schema.For<Target>().Duplicate(t => t.Inner.Number));

        await theStore.BulkInsertDocumentsAsync(new[]
        {
            new Target
            {
                Number = 1,
                Inner = null
            }
        });

        using var session = theStore.QuerySession();
        session.Query<Target>().Where(x => x.Number == 1)
            .ToArray()
            .Select(x => x.Number)
            .ShouldHaveTheSameElementsAs(1);
    }
}
