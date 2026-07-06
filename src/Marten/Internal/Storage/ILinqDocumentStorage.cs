#nullable enable
using Marten.Linq.Members;

namespace Marten.Internal.Storage;

/// <summary>
///     Marten-side extension of the neutral <see cref="IDocumentStorage"/> contract that carries the
///     LINQ query-translation member model (<see cref="IQueryableMemberCollection"/>). Kept off the
///     movable <see cref="IDocumentStorage"/> surface so that contract can relocate to the db-agnostic
///     Weasel.Storage package (#4821) without dragging Marten's LINQ member model — the LINQ query
///     pipeline reaches <see cref="QueryMembers"/> through this facet instead.
/// </summary>
public interface ILinqDocumentStorage: IDocumentStorage
{
    IQueryableMemberCollection QueryMembers { get; }
}
