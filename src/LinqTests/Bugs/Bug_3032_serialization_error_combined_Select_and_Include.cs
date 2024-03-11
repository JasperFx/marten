using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten;
using Marten.Services;
using Marten.Testing.Harness;
using Shouldly;
using Xunit.Abstractions;

namespace LinqTests.Bugs;

public class Bug_3032_serialization_error_combined_Select_and_Include : BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public Bug_3032_serialization_error_combined_Select_and_Include(ITestOutputHelper output)
    {
        _output = output;
    }

    // Fails with error:
    // Cannot deserialize the current JSON object (e.g. {"name":"value"}) into type 'System.Collections.Generic.IReadOnlyList`1
    [Fact]
    public async Task test_select_and_include()
    {
        const string scope = "scope1";
        const string scopeDescription = "scope1Desc";
        var meta = new EntityScope
        {
            Id = scope,
            Description = scopeDescription
        };

        var entity = new EntityWithChild
        {
            Id = Guid.NewGuid(),
            Metadata = scope,
            Children = new[]
            {
                new ChildOfEntity { Id = Guid.NewGuid() },
                new ChildOfEntity { Id = Guid.NewGuid() }
            }
        };

        await using (var sessionX = theStore.LightweightSession())
        {
            sessionX.Store(meta);
            sessionX.Store(entity);
            await sessionX.SaveChangesAsync();
        }

        await using var session = theStore.QuerySession();
        session.Logger = new TestOutputMartenLogger(_output);

        var metadata = new List<EntityScope>();
        var result = await session
            .Query<EntityWithChild>()
            .Where(x => x.Id.IsOneOf(entity.Id))
            .Include(x => x.Metadata, metadata)
            .Select(x => x.Children)
            .SingleOrDefaultAsync(CancellationToken.None);

        metadata.Count.ShouldBe(1);
        metadata[0].Description.ShouldBe(scopeDescription);

        result.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task test_just_include()
    {
        const string scope = "scope1";
        const string scopeDescription = "scope1Desc";
        var meta = new EntityScope
        {
            Id = scope,
            Description = scopeDescription
        };

        var entity = new EntityWithChild
        {
            Id = Guid.NewGuid(),
            Metadata = scope,
            Children = new[]
            {
                new ChildOfEntity { Id = Guid.NewGuid() },
                new ChildOfEntity { Id = Guid.NewGuid() }
            }
        };

        await using (var sessionX = theStore.LightweightSession())
        {
            sessionX.Store(meta);
            sessionX.Store(entity);
            await sessionX.SaveChangesAsync();
        }

        await using var session = theStore.QuerySession();
        var metadata = new List<EntityScope>();
        var result = await session
            .Query<EntityWithChild>()
            .Where(x => x.Id.IsOneOf(entity.Id))
            .Include(x => x.Metadata, metadata)
            .SingleOrDefaultAsync(CancellationToken.None);

        metadata.Count.ShouldBe(1);
        metadata[0].Description.ShouldBe(scopeDescription);

        result.ShouldNotBeNull();
        result.Id.ShouldBe(entity.Id);
    }

    [Fact]
    public async Task test_just_select()
    {
        const string scope = "scope1";
        const string scopeDescription = "scope1Desc";
        var meta = new EntityScope
        {
            Id = scope,
            Description = scopeDescription
        };

        var entity = new EntityWithChild
        {
            Id = Guid.NewGuid(),
            Metadata = scope,
            Children = new[]
            {
                new ChildOfEntity { Id = Guid.NewGuid() },
                new ChildOfEntity { Id = Guid.NewGuid() }
            }
        };

        await using (var sessionX = theStore.LightweightSession())
        {
            sessionX.Store(meta);
            sessionX.Store(entity);
            await sessionX.SaveChangesAsync();
        }

        await using var session = theStore.QuerySession();
        session.Logger = new TestOutputMartenLogger(_output);
        var result = await session
            .Query<EntityWithChild>()
            .Where(x => x.Id.IsOneOf(entity.Id))
            .Select(x => x.Children)
            .SingleOrDefaultAsync(CancellationToken.None);

        result.ShouldNotBeNull();
    }
}

public class EntityWithChild
{
    public Guid Id { get; set; }
    public string Metadata { get; set; }

    public IReadOnlyList<ChildOfEntity> Children { get; set; } = Array.Empty<ChildOfEntity>();
}

public class EntityScope
{
    public string Id { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class ChildOfEntity
{
    public Guid Id { get; set; }
}
