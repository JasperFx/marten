#nullable enable
using Marten.Linq.Members;

namespace Marten.Internal.Storage;

/// <summary>
///     Marten-side extension of the neutral <see cref="IDocumentStorage"/> contract (now in
///     Weasel.Storage, #4821) that carries the LINQ query-translation concerns: the member model
///     (<see cref="IQueryableMemberCollection"/>), the Marten (Postgres/LINQ-typed)
///     <see cref="ISelectClause"/> facet, and the duplicated-fields select clause used by
///     Includes()/CTE queries. Kept off the movable surface so the neutral contract has no
///     dependency on Marten's LINQ pipeline — the LINQ code reaches these through this facet.
/// </summary>
public interface ILinqDocumentStorage: IDocumentStorage, ISelectClause
{
    IQueryableMemberCollection QueryMembers { get; }

    /// <summary>
    /// Necessary (maybe) for usage within the temporary tables when using Includes()
    /// </summary>
    ISelectClause SelectClauseWithDuplicatedFields { get; }
}
