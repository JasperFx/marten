#nullable enable
using System.Collections.Generic;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration;

/// <summary>
/// #5233: implemented by a select clause that can render command parameters of its own.
///
/// <para>
/// Compiled-query parameter discovery used to walk <c>Statement.AllFilters()</c> only, which
/// enumerates WHERE clauses and never visits the select clause. Most select-list parameters
/// survived that anyway, because <c>QueryMember.tryToFind</c> falls back to matching a query
/// member's value directly against the parameter's value. What did NOT survive is a fragment whose
/// value is buried inside a COMPOSITE parameter — a jsonpath <c>vars</c> payload, say — because
/// there is no scalar to compare and re-binding has to go through the fragment's own
/// <see cref="Marten.Internal.CompiledQueries.ICompiledQueryAwareFilter" />. Exposing the
/// fragments here lets those reach <c>MatchParameters</c>.
/// </para>
/// </summary>
internal interface IParameterBearingSelectClause
{
    IEnumerable<ISqlFragment> SelectFragments();
}
