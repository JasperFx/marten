using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Util;

namespace Marten.Events.Projections
{
    /// <summary>
    /// Simple aggregation finder that looks for an aggregate document based on the stream id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class AggregateFinder<T> : IAggregationFinder<T> where T : class, new()
    {
        private readonly Action<T, Guid> _setId;

        public AggregateFinder()
        {
            var idMember = DocumentMapping.FindIdMember(typeof (T));

            var docParam = Expression.Parameter(typeof (T), "doc");
            var idParam = Expression.Parameter(typeof (Guid), "id");

            var member = Expression.PropertyOrField(docParam, idMember.Name);
            var assign = Expression.Assign(member, idParam);

            var lambda = Expression.Lambda<Action<T, Guid>>(assign, docParam, idParam);

            _setId = ExpressionCompiler.Compile<Action<T, Guid>>(lambda);
        }

        public T Find(EventStream stream, IDocumentSession session)
        {
            var returnValue =  stream.IsNew ? new T() : session.Load<T>(stream.Id) ?? new T();
            _setId(returnValue, stream.Id);

            return returnValue;
        }

        public async Task<T> FindAsync(EventStream stream, IDocumentSession session, CancellationToken token)
        {
            var returnValue = stream.IsNew ? new T() : await session.LoadAsync<T>(stream.Id, token).ConfigureAwait(false) ?? new T();

            _setId(returnValue, stream.Id);

            return returnValue;
        }

        public async Task FetchAllAggregates(IDocumentSession session, EventStream[] streams, CancellationToken token)
        {
            await session.LoadManyAsync<T>(token, streams.Select(x => x.Id).ToArray()).ConfigureAwait(false);
        }
    }
}