using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Remotion.Linq;

namespace Marten.Linq
{
    public class MartenQueryable<T> : QueryableBase<T>, IMartenQueryable<T>
    {
        public MartenQueryable(IQueryProvider provider) : base(provider)
        {
        }

        public MartenQueryable(IQueryProvider provider, Expression expression) : base(provider, expression)
        {
        }

        public async Task<IEnumerable<T>> ExecuteCollectionAsync(CancellationToken token)
        {
            var queryProvider = (IMartenQueryProvider)Provider;
            return await queryProvider.ExecuteCollectionAsync<T>(Expression, token);
        }
    }
}