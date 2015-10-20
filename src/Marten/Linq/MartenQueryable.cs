using System.Linq;
using System.Linq.Expressions;
using Npgsql;
using Remotion.Linq;
using Remotion.Linq.Parsing.Structure;

namespace Marten.Linq
{
    public class MartenQueryable<T> : QueryableBase<T>
    {
        private readonly IDocumentExecutor _executor;
        private readonly IQueryParser _queryParser;

        public MartenQueryable(IQueryParser queryParser, IDocumentExecutor executor) : base(queryParser, executor)
        {
            _queryParser = queryParser;
            _executor = executor;
        }

        public MartenQueryable(IQueryProvider provider) : base(provider)
        {
        }

        public MartenQueryable(IQueryProvider provider, Expression expression) : base(provider, expression)
        {
        }

        public NpgsqlCommand ToCommand()
        {
            return _executor.BuildCommand<T>(_queryParser.GetParsedQuery(Expression));
        }
    }
}