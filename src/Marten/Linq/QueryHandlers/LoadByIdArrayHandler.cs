using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Linq.Filters;
using Marten.Linq.Selectors;
using Weasel.Postgresql;
using Marten.Storage;
using Marten.Util;
#nullable enable
namespace Marten.Linq.QueryHandlers
{
    public class LoadByIdArrayHandler<T, TKey>: IQueryHandler<IReadOnlyList<T>> where T: notnull
    {
        private readonly IDocumentStorage<T> storage;
        private readonly TKey[] _ids;

        public LoadByIdArrayHandler(IDocumentStorage<T> documentStorage, TKey[] ids)
        {
            storage = documentStorage;
            _ids = ids;
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
            sql.Append(" as d where id = ANY(:");

            var parameter = sql.AddParameter(_ids);
            sql.Append(parameter.ParameterName);
            sql.Append(")");

            // TODO -- there's some duplication here that should be handled consistently
            if (storage.TenancyStyle == TenancyStyle.Conjoined)
            {
                sql.Append($" and {CurrentTenantFilter.Filter}");
            }
        }

        public IReadOnlyList<T> Handle(DbDataReader reader, IMartenSession session)
        {
            var list = new List<T>();

            var selector = (ISelector<T>)storage.BuildSelector(session);

            while (reader.Read())
            {
                list.Add(selector.Resolve(reader));
            }

            return list;
        }

        public async Task<IReadOnlyList<T>> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
        {
            var list = new List<T>();

            var selector = (ISelector<T>)storage.BuildSelector(session);

            while (await reader.ReadAsync(token))
            {
                list.Add(await selector.ResolveAsync(reader, token));
            }

            return list;
        }
    }
}
