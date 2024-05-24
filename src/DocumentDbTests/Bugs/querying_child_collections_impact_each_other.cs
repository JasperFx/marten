using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs;

public class querying_child_collections_impact_each_other: IntegrationContext
{
    public querying_child_collections_impact_each_other(DefaultStoreFixture fixture): base(fixture) { }

    [Fact]
    public async Task query_collection_property_before_querying_against_collection_of_values()
    {
        StoreOptions(_ =>
        {
            _.Schema.For<One>();
            _.Schema.For<Two>();
        });

        var guid1 = Guid.NewGuid();

        theSession.Store(new One { Id = Guid.NewGuid(), Guids = new List<Guid>() { guid1, new Guid() } });
        theSession.Store(new One { Id = Guid.NewGuid(), Guids = new List<Guid>() { guid1, new Guid() } });
        theSession.Store(new Two { Id = guid1 });
        theSession.Store(new Two { Id = Guid.NewGuid() });

        await theSession.SaveChangesAsync();

        await using (var query = theStore.LightweightSession())
        {
            var result = await query.Query<One>()
                .Where(x => x.Guids.Contains(guid1))
                .ToListAsync();

            result.Count.ShouldBe(2);
        }

        await using (var query = theStore.LightweightSession())
        {
            var guids = new List<Guid> { guid1, new Guid() };
            var result = await query.Query<Two>()
                .Where(x => guids.Contains(x.Id))
                .ToListAsync();

            result.Count.ShouldBe(1);
        }
    }

    [Fact]
    public async Task query_collection_of_values_before_querying_against_collection_property()
    {
        StoreOptions(_ =>
        {
            _.Schema.For<One>();
            _.Schema.For<Two>();
        });

        var guid1 = Guid.NewGuid();

        theSession.Store(new One { Id = Guid.NewGuid(), Guids = new List<Guid>() { guid1, new Guid() } });
        theSession.Store(new One { Id = Guid.NewGuid(), Guids = new List<Guid>() { guid1, new Guid() } });
        theSession.Store(new Two { Id = guid1 });
        theSession.Store(new Two { Id = Guid.NewGuid() });

        await theSession.SaveChangesAsync();

        await using (var query = theStore.LightweightSession())
        {
            var guids = new List<Guid> { guid1, new Guid() };
            var result = await query.Query<Two>()
                .Where(x => guids.Contains(x.Id))
                .ToListAsync();

            result.Count.ShouldBe(1);
        }

        await using (var query = theStore.LightweightSession())
        {
            var result = await query.Query<One>()
                .Where(x => x.Guids.Contains(guid1))
                .ToListAsync();

            result.Count.ShouldBe(2);
        }
    }

    [Fact]
    public async Task query_collection_of_values_works_on_its_own()
    {
        StoreOptions(_ =>
        {
            _.Schema.For<One>();
            _.Schema.For<Two>();
        });

        var guid1 = Guid.NewGuid();

        theSession.Store(new One { Id = Guid.NewGuid(), Guids = new List<Guid>() { guid1, new Guid() } });
        theSession.Store(new One { Id = Guid.NewGuid(), Guids = new List<Guid>() { guid1, new Guid() } });
        theSession.Store(new Two { Id = guid1 });
        theSession.Store(new Two { Id = Guid.NewGuid() });

        await theSession.SaveChangesAsync();

        await using (var query = theStore.LightweightSession())
        {
            var guids = new List<Guid> { guid1, new Guid() };
            var result = await query.Query<Two>()
                .Where(x => guids.Contains(x.Id))
                .ToListAsync();

            result.Count.ShouldBe(1);
        }
    }

    [Fact]
    public async Task query_collection_property_works_on_its_own()
    {
        StoreOptions(_ =>
        {
            _.Schema.For<One>();
            _.Schema.For<Two>();
        });

        var guid1 = Guid.NewGuid();

        theSession.Store(new One { Id = Guid.NewGuid(), Guids = new List<Guid>() { guid1, new Guid() } });
        theSession.Store(new One { Id = Guid.NewGuid(), Guids = new List<Guid>() { guid1, new Guid() } });
        theSession.Store(new Two { Id = guid1 });
        theSession.Store(new Two { Id = Guid.NewGuid() });

        await theSession.SaveChangesAsync();

        await using (var query = theStore.LightweightSession())
        {
            var result = await query.Query<One>()
                .Where(x => x.Guids.Contains(guid1))
                .ToListAsync();

            result.Count.ShouldBe(2);
        }
    }

    public class One
    {
        public Guid Id { get; set; }

        public List<Guid> Guids { get; set; } = new();
    }

    public class Two
    {
        public Guid Id { get; set; }
    }
}
