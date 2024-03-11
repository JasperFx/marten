using System;
using System.Linq;
using Marten.Linq.SoftDeletes;
using Marten.Metadata;
using Marten.Testing.Harness;
using Shouldly;
using Xunit.Abstractions;

namespace LinqTests.Bugs;

public class Bug_3031_querying_using_soft_deletes : BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public Bug_3031_querying_using_soft_deletes(ITestOutputHelper output)
    {
        _output = output;
    }

    public static IQueryable<T> AddWhere<T>(IQueryable<T> q)
    {
        return q.Where(x => x.MaybeDeleted());
    }

    public static IQueryable<Entity> AddWhereNonGeneric(IQueryable<Entity> q)
    {
        return q.Where(x => x.MaybeDeleted());
    }

    // Fails with "System.InvalidOperationException":
    // "Document type object is not configured as soft deleted"
    [Fact]
    public void test_any_maybe_deleted_in_generic_method()
    {
        var entity = new Entity { Id = Guid.NewGuid() };

        using (var sessionX = theStore.LightweightSession())
        {
            sessionX.Store(entity, entity);
            sessionX.SaveChanges();
        }

        using var session = theStore.QuerySession();
        var q = session.Query<Entity>();
        var w = AddWhere(q);
        var result = w.Any();

        result.ShouldBeTrue();
    }

    // Works fine
    [Fact]
    public void test_any_maybe_deleted_in_non_generic_method()
    {
        var entity = new Entity { Id = Guid.NewGuid() };

        using (var sessionX = theStore.LightweightSession())
        {
            sessionX.Store(entity, entity);
            sessionX.SaveChanges();
        }

        using var session = theStore.QuerySession();
        var q = session.Query<Entity>();
        var w = AddWhereNonGeneric(q);
        var result = w.Any();

        result.ShouldBeTrue();
    }

    // Fails with "System.InvalidOperationException":
    // "Document type object is not configured as soft deleted"
    [Fact]
    public void test_any_maybe_deleted_in_generic_extension_method()
    {
        var entity = new Entity { Id = Guid.NewGuid() };

        using (var sessionX = theStore.LightweightSession())
        {
            sessionX.Store(entity, entity);
            sessionX.SaveChanges();
        }

        using var session = theStore.QuerySession();

        session.Logger = new TestOutputMartenLogger(_output);

        bool result = session
            .Query<Entity>()
            .WithArchivedState(ArchivedState.IncludeArchived)
            .Any();

        result.ShouldBeTrue();
    }

    // Fails with "System.InvalidOperationException":
    // "Document type object is not configured as soft deleted"
    [Fact]
    public void test_list_maybe_deleted_in_generic_extension_method()
    {
        var entity = new Entity { Id = Guid.NewGuid() };

        using (var sessionX = theStore.LightweightSession())
        {
            sessionX.Store(entity, entity);
            sessionX.SaveChanges();
        }

        using var session = theStore.QuerySession();
        var result = session
            .Query<Entity>()
            .WithArchivedState(ArchivedState.IncludeArchived)
            .ToList();

        result.Count.ShouldBe(1);
    }

    // Working fine
    [Fact]
    public void test_any_maybe_deleted_in_extension_method()
    {
        var entity = new Entity { Id = Guid.NewGuid() };

        using (var sessionX = theStore.LightweightSession())
        {
            sessionX.Store(entity, entity);
            sessionX.SaveChanges();
        }

        using var session = theStore.QuerySession();
        bool result = session
            .Query<Entity>()
            .WithArchivedStateNonGeneric(ArchivedState.IncludeArchived)
            .Any();

        result.ShouldBeTrue();
    }

    // Working fine
    [Fact]
    public void test_list_maybe_deleted_in_extension_method()
    {
        var entity = new Entity { Id = Guid.NewGuid() };

        using (var sessionX = theStore.LightweightSession())
        {
            sessionX.Store(entity, entity);
            sessionX.SaveChanges();
        }

        using var session = theStore.QuerySession();
        var result = session
            .Query<Entity>()
            .WithArchivedStateNonGeneric(ArchivedState.IncludeArchived)
            .ToList();

        result.Count.ShouldBe(1);
    }

    // Working fine
    [Fact]
    public void test_any()
    {
        var entity = new Entity { Id = Guid.NewGuid() };

        using (var sessionX = theStore.LightweightSession())
        {
            sessionX.Store(entity, entity);
            sessionX.SaveChanges();
        }

        using var session = theStore.QuerySession();
        bool result = session
            .Query<Entity>()
            .Where(x => x.MaybeDeleted())
            .Any();

        result.ShouldBeTrue();
    }

    // Working fine
    [Fact]
    public void test_list()
    {
        var entity = new Entity { Id = Guid.NewGuid() };
        var entityDeleted = new Entity { Id = Guid.NewGuid() };

        using (var session = theStore.LightweightSession())
        {
            session.Store(entity, entityDeleted);
            session.SaveChanges();
        }

        using (var session = theStore.LightweightSession())
        {
            session.Delete<Entity>(entityDeleted.Id);
            session.SaveChanges();
        }

        using var query = theStore.QuerySession();

        var list = query.Query<Entity>()
            .Where(x => x.MaybeDeleted())
            .ToList();

        list.Count.ShouldBe(2);
        list[0].Id.ShouldBe(entity.Id);
        list[0].Deleted.ShouldBe(false);
        list[0].DeletedAt.ShouldBe(null);
        list[1].Id.ShouldBe(entityDeleted.Id);
        list[1].Deleted.ShouldBe(true);
        list[1].DeletedAt.HasValue.ShouldBeTrue();
    }
}

public enum ArchivedState
{
    NotArchived,
    IncludeArchived,
    OnlyArchived
}


public static class ArchiveStateExtensions
{
    public static IQueryable<T> WithArchivedState<T>(this IQueryable<T> q, ArchivedState archivedState)
    {
        return archivedState switch
        {
            ArchivedState.NotArchived => q, // no-op
            ArchivedState.IncludeArchived => q.Where(x => x.MaybeDeleted()),
            ArchivedState.OnlyArchived => q.Where(x => x.IsDeleted()),
            _ => throw new ArgumentOutOfRangeException(nameof(archivedState), archivedState, null)
        };
    }

    public static IQueryable<Entity> WithArchivedStateNonGeneric(this IQueryable<Entity> q, ArchivedState archivedState)
    {
        return archivedState switch
        {
            ArchivedState.NotArchived => q, // no-op
            ArchivedState.IncludeArchived => q.Where(x => x.MaybeDeleted()),
            ArchivedState.OnlyArchived => q.Where(x => x.IsDeleted()),
            _ => throw new ArgumentOutOfRangeException(nameof(archivedState), archivedState, null)
        };
    }
}

public class Entity: ISoftDeleted
{
    public Guid Id { get; set; }
    public bool Deleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
