using System;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Marten.Schema;

namespace Marten.Events.Projections
{
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

            _setId = lambda.Compile();
        }

        public T Find(EventStream stream, IDocumentSession session)
        {
            var returnValue =  stream.IsNew ? new T() : session.Load<T>(stream.Id) ?? new T();
            _setId(returnValue, stream.Id);

            return returnValue;
        }

        public async Task<T> FindAsync(EventStream stream, IDocumentSession session, CancellationToken token)
        {
            var returnValue = stream.IsNew ? new T() : await session.LoadAsync<T>(stream.Id, token) ?? new T();

            _setId(returnValue, stream.Id);

            return returnValue;
        }
    }
}