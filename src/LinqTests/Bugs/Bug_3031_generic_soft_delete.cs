using System;
using System.Linq;
using Marten.Linq.SoftDeletes;
using Marten.Metadata;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

/// <summary>
///     These calls previously failed with "System.InvalidOperationException":
///     "Document type object is not configured as soft deleted"
///     due to the generic context
/// </summary>
public class Bug_3031_generic_soft_delete : BugIntegrationContext
{
    [Fact]
    public void test_any_maybe_deleted_in_generic_method()
    {
        var entity = new Bug_3031_Entity { Id = Guid.NewGuid() };

        using (var sessionX = theStore.LightweightSession())
        {
            sessionX.Store(entity, entity);
            sessionX.SaveChanges();
        }

        using var session = theStore.QuerySession();
        var q = session.Query<Bug_3031_Entity>();
        var w = AddWhere(q);
        var result = w.Any();

        result.ShouldBeTrue();
    }

    // Fails with "System.InvalidOperationException":
    // "Document type object is not configured as soft deleted"
    [Fact]
    public void test_any_maybe_deleted_in_generic_extension_method()
    {
        var entity = new Bug_3031_Entity { Id = Guid.NewGuid() };

        using (var sessionX = theStore.LightweightSession())
        {
            sessionX.Store(entity, entity);
            sessionX.SaveChanges();
        }

        using var session = theStore.QuerySession();
        bool result = session
            .Query<Bug_3031_Entity>()
            .Bug_3031_WithArchivedState(Bug_3031_ArchivedState.IncludeArchived)
            .Any();

        result.ShouldBeTrue();
    }

    // Fails with "System.InvalidOperationException":
    // "Document type object is not configured as soft deleted"
    [Fact]
    public void test_list_maybe_deleted_in_generic_extension_method()
    {
        var entity = new Bug_3031_Entity { Id = Guid.NewGuid() };

        using (var sessionX = theStore.LightweightSession())
        {
            sessionX.Store(entity, entity);
            sessionX.SaveChanges();
        }

        using var session = theStore.QuerySession();
        var result = session
            .Query<Bug_3031_Entity>()
            .Bug_3031_WithArchivedState(Bug_3031_ArchivedState.IncludeArchived)
            .ToList();

        result.Count.ShouldBe(1);
    }

    private static IQueryable<T> AddWhere<T>(IQueryable<T> q)
    {
        return q.Where(x => x.MaybeDeleted());
    }
}

public enum Bug_3031_ArchivedState
{
    NotArchived,
    IncludeArchived,
    OnlyArchived
}

public class Bug_3031_Entity: ISoftDeleted
{
    public Guid Id { get; set; }
    public bool Deleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public static class Bug_3031_ArchiveStateExtensions
{
    public static IQueryable<T> Bug_3031_WithArchivedState<T>(this IQueryable<T> q, Bug_3031_ArchivedState bug3031ArchivedState)
    {
        return bug3031ArchivedState switch
        {
            Bug_3031_ArchivedState.NotArchived => q, // no-op
            Bug_3031_ArchivedState.IncludeArchived => q.Where(x => x.MaybeDeleted()),
            Bug_3031_ArchivedState.OnlyArchived => q.Where(x => x.IsDeleted()),
            _ => throw new ArgumentOutOfRangeException(nameof(bug3031ArchivedState), bug3031ArchivedState, null)
        };
    }
}
