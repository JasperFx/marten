using System.Collections.Generic;
using System.Linq;
using Marten.Linq;
using Remotion.Linq.Clauses;

namespace Marten.Util
{
    internal static class OrderingExtensions
    {
        internal static (Ordering Clause, bool CaseSensitive)[] GetStringOrderingClauses(
            this IEnumerable<IBodyClause> bodyClauses)
        {
            return bodyClauses
                .SelectMany<IBodyClause, (Ordering Clause, bool CaseSensitive)>(x =>
                {
                    switch (x)
                    {
                        case OrderByClause orderByClause:
                            return orderByClause.Orderings.Select(o => (o, true));
                        case OrderByComparerClause orderByComparerClause:
                            return orderByComparerClause.Orderings.Select(o => (o, orderByComparerClause.CaseSensitive));
                        default:
                            return Enumerable.Empty<(Ordering, bool)>();
                    }
                })
                .ToArray();
        }
    }
}
