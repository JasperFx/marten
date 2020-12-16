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
    [Obsolete("This will be completely replaced by the new AggregateProjection in V4")]
    public class AggregateFinder<T>: IAggregationFinder<T> where T : class
    {
        private readonly Action<T, Guid> _setId;

        public AggregateFinder()
        {
            var idMember = DocumentMapping.FindIdMember(typeof(T));

            var docParam = Expression.Parameter(typeof(T), "doc");
            var idParam = Expression.Parameter(typeof(Guid), "id");

            var member = Expression.PropertyOrField(docParam, idMember.Name);
            var assign = Expression.Assign(member, idParam);

            var lambda = Expression.Lambda<Action<T, Guid>>(assign, docParam, idParam);

            _setId = ExpressionCompiler.Compile<Action<T, Guid>>(lambda);
        }

        public T Find(StreamAction stream, IDocumentSession session)
        {
            var returnValue = stream.ActionType == StreamActionType.Start ? New<T>.Instance() : session.Load<T>(stream.Id) ?? New<T>.Instance();
            _setId(returnValue, stream.Id);

            return returnValue;
        }

        public async Task<T> FindAsync(StreamAction stream, IDocumentSession session, CancellationToken token)
        {
            var returnValue = stream.ActionType == StreamActionType.Start ? New<T>.Instance() : await session.LoadAsync<T>(stream.Id, token).ConfigureAwait(false) ?? New<T>.Instance();

            _setId(returnValue, stream.Id);

            return returnValue;
        }

        public Task FetchAllAggregates(IDocumentSession session, StreamAction[] streams, CancellationToken token)
        {
            if (streams.Length > 0)
            {
                return session.LoadManyAsync<T>(token, streams.Select(x => x.Id).ToArray());
            }
            return Task.CompletedTask;
        }
    }
}
