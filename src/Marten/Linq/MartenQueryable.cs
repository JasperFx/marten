using System.Linq;
using System.Linq.Expressions;
using Marten.Schema;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Parsing.Structure;

namespace Marten.Linq
{
    public class MartenQueryable<T> : QueryableBase<T>
    {
        public MartenQueryable(IQueryParser queryParser, IQueryExecutor executor) : base(queryParser, executor)
        {
        }

        public MartenQueryable(IQueryProvider provider) : base(provider)
        {
        }

        public MartenQueryable(IQueryProvider provider, Expression expression) : base(provider, expression)
        {
        }


    }
}