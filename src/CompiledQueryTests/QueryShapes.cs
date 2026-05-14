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
