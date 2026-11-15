using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Marten;
using Marten.Linq;
using Marten.Testing.Documents;

namespace CompiledQueryTests;

// Three representative compiled-query shapes covering the matrix Marten's
// codegen splits into (Stateless / Cloned / Complex). Each type is registered
// with the runtime CompiledQueryHandlerRegistry at assembly load via the
// generator-emitted [ModuleInitializer] shim — see #4405 iter 3+4.

/// <summary>
/// Shape 1: simple <c>Where</c> + scalar return. Hits the Stateless handler path.
/// </summary>
public class UserByUserNameShape: ICompiledQuery<User, User>
{
    public string UserName { get; set; } = string.Empty;

    public Expression<Func<IMartenQueryable<User>, User>> QueryIs()
        => q => q.FirstOrDefault(x => x.UserName == UserName);
}

/// <summary>
/// Shape 2: <c>Where + OrderBy + Skip/Take</c> producing an enumerable. Still
/// Stateless — no statistics, no includes. Exercises multi-clause SQL emission
/// and the parameter binder across three parameter slots (FirstNamePrefix +
/// Skip + Take).
/// </summary>
public class UsersByFirstNamePageShape: ICompiledListQuery<User>
{
    public string FirstNamePrefix { get; set; } = string.Empty;
    public int Skip { get; set; }
    public int Take { get; set; }

    Expression<Func<IMartenQueryable<User>, IEnumerable<User>>>
        ICompiledQuery<User, IEnumerable<User>>.QueryIs()
        => q => q
            .Where(x => x.FirstName!.StartsWith(FirstNamePrefix))
            .OrderBy(x => x.LastName)
            .ThenBy(x => x.FirstName)
            .Skip(Skip)
            .Take(Take);
}

/// <summary>
/// Shape 3: <c>Where + Include</c>. Hits the Complex handler path — the
/// IncludeQueryHandler wrapping that iteration 4 added to the source-gen
/// runtime. Pulls back issues plus the related users into a side list.
/// </summary>
public class IssueWithAssigneeShape: ICompiledQuery<Issue, Issue>
{
    public string Title { get; set; } = string.Empty;
    public IList<User> Assignees { get; private set; } = new List<User>();

    public Expression<Func<IMartenQueryable<Issue>, Issue>> QueryIs()
        => q => q
            .Include<User>(x => x.AssigneeId!, Assignees)
            .FirstOrDefault(x => x.Title == Title);
}

/// <summary>
/// A minimal document for the #5920 paged-count shapes.
/// </summary>
public class PagedTarget
{
    public Guid Id { get; set; }
    public int Number { get; set; }
}

/// <summary>
/// #5920: <c>Where + OrderBy + Skip/Take + Count()</c>. The count has to be the number of rows on the page,
/// so the paging must stay inside the counted subquery instead of landing on the <c>count(*)</c> itself.
/// </summary>
public class TargetCountPageShape: ICompiledQuery<PagedTarget, int>
{
    public int MinimumNumber { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }

    public Expression<Func<IMartenQueryable<PagedTarget>, int>> QueryIs()
        => q => q
            .Where(x => x.Number > MinimumNumber)
            .OrderBy(x => x.Number)
            .Skip(Skip)
            .Take(Take)
            .Count();
}

/// <summary>
/// #5920: <c>Take + LongCount()</c>, the shape that used to return the full table count.
/// </summary>
public class TargetLongCountTakeShape: ICompiledQuery<PagedTarget, long>
{
    public int Take { get; set; }

    public Expression<Func<IMartenQueryable<PagedTarget>, long>> QueryIs()
        => q => q.Take(Take).LongCount();
}

/// <summary>
/// A minimal parent document for the #5928 paged-SelectMany shape.
/// </summary>
public class PagedHolder
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public List<PagedHolderItem> Items { get; set; } = new();
}

public record PagedHolderItem
{
    public int Holder { get; set; }
    public int Index { get; set; }
}

/// <summary>
/// #5928: <c>OrderBy + Skip/Take + SelectMany</c>. The paging selects the parent documents, so the result is
/// every item of the documents on the page rather than a page of the flattened items.
/// </summary>
public class PagedHolderItemsShape: ICompiledListQuery<PagedHolder, PagedHolderItem>
{
    public int Skip { get; set; }
    public int Take { get; set; }

    public Expression<Func<IMartenQueryable<PagedHolder>, IEnumerable<PagedHolderItem>>> QueryIs()
        => q => q.OrderBy(x => x.Number).Skip(Skip).Take(Take).SelectMany(x => x.Items);
}

/// <summary>
/// #5933: a <c>Select()</c> after <c>SelectMany()</c> computed over each flattened row, reading a value off the
/// compiled query, followed by paging of the projected rows.
/// </summary>
public class PagedHolderItemScoresShape: ICompiledListQuery<PagedHolder, int>
{
    public int Multiplier { get; set; }
    public int Take { get; set; }

    public Expression<Func<IMartenQueryable<PagedHolder>, IEnumerable<int>>> QueryIs()
        => q => q.OrderBy(x => x.Number).SelectMany(x => x.Items).Select(i => i.Index * Multiplier + i.Holder)
            .Take(Take);
}

/// <summary>
/// #5942: a computed top-level <c>Select()</c> reading a value off the compiled query, followed by paging of the
/// projected rows.
/// </summary>
public class PagedHolderNumberScoresShape: ICompiledListQuery<PagedHolder, int>
{
    public int Multiplier { get; set; }
    public int Take { get; set; }

    public Expression<Func<IMartenQueryable<PagedHolder>, IEnumerable<int>>> QueryIs()
        => q => q.OrderBy(x => x.Number).Select(x => x.Number * Multiplier % 7).Take(Take);
}
