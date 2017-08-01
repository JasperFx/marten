using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Services;
using Marten.Storage;
using Marten.Util;

namespace Marten.Linq.QueryHandlers
{
    public class LoadByIdArrayHandler<T, TKey> : IQueryHandler<IList<T>>
    {
        private readonly IDocumentStorage<T> storage;
        private readonly IQueryableDocument _mapping;
        private readonly TKey[] _ids;

        public LoadByIdArrayHandler(IDocumentStorage<T> documentStorage, IQueryableDocument mapping, TKey[] ids)
        {
            storage = documentStorage;
            _mapping = mapping;
            _ids = ids;
        }

        public Type SourceType => typeof(T);


        public void ConfigureCommand(CommandBuilder sql)
        {
            sql.Append("select ");

            var fields = _mapping.SelectFields();

            sql.Append(fields[0]);
            for (int i = 1; i < fields.Length; i++)
            {
                sql.Append(", ");
                sql.Append(fields[i]);
            }

            sql.Append(" from ");
            sql.Append(_mapping.Table.QualifiedName);
            sql.Append(" as d where id = ANY(:");

            var parameter = sql.AddParameter(_ids);
            sql.Append(parameter.ParameterName);
            sql.Append(")");

            if (storage.TenancyStyle == TenancyStyle.Conjoined)
            {
                sql.Append($" and {TenantWhereFragment.Filter}");
            }

        }

        public IList<T> Handle(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            var list = new List<T>();

            while (reader.Read())
            {
                list.Add(storage.Resolve(0, reader, map));
            }

            return list;
        }

        public async Task<IList<T>> HandleAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            var list = new List<T>();

            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                list.Add(await storage.ResolveAsync(0, reader, map, token).ConfigureAwait(false));
            }

            return list;
        }
    }
}