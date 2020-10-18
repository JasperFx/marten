using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Linq.Filters;
using Marten.Linq.Selectors;
using Marten.Storage;
using Marten.Util;

namespace Marten.Linq.QueryHandlers
{
    public class LoadByIdHandler<T, TId>: IQueryHandler<T>
    {
        private readonly IDocumentStorage<T> storage;
        private readonly TId _id;

        public LoadByIdHandler(IDocumentStorage<T, TId> documentStorage, TId id)
        {
            storage = documentStorage;
            _id = id;
        }

        public void ConfigureCommand(CommandBuilder sql, IMartenSession session)
        {
            sql.Append("select ");

            var fields = storage.SelectFields();
            sql.Append(fields[0]);
            for (int i = 1; i < fields.Length; i++)
            {
                sql.Append(", ");
                sql.Append(fields[i]);
            }

            sql.Append(" from ");
            sql.Append(storage.FromObject);
            sql.Append(" as d where id = :");

            var parameter = sql.AddParameter(_id);
            sql.Append(parameter.ParameterName);

            // TODO -- there's some duplication here that should be handled consistently
            if (storage.TenancyStyle == TenancyStyle.Conjoined)
            {
                sql.Append($" and {CurrentTenantFilter.Filter}");
            }
        }


        public T Handle(DbDataReader reader, IMartenSession session)
        {
            var selector = (ISelector<T>)storage.BuildSelector(session);
            return reader.Read() ? selector.Resolve(reader) : default;
        }

        public async Task<T> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
        {
            var selector = (ISelector<T>)storage.BuildSelector(session);
            if (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                return await selector.ResolveAsync(reader, token).ConfigureAwait(false);
            }

            return default;
        }
    }
}
