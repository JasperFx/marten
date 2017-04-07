using System;
using System.Data.Common;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Services;
using Marten.Util;
using Npgsql;

namespace Marten.Linq.QueryHandlers
{
    public class LoadByIdHandler<T> : IQueryHandler<T>
    {
        private readonly IDocumentStorage<T> storage;
        private readonly IQueryableDocument _mapping;
        private readonly object _id;

        public LoadByIdHandler(IDocumentStorage<T> documentStorage, IQueryableDocument mapping, object id)
        {
            storage = documentStorage;
            _mapping = mapping;
            _id = id;
        }

        public Type SourceType => typeof (T);

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
            sql.Append(" as d where id = :");
            
            var parameter = sql.AddParameter(_id);
            sql.Append(parameter.ParameterName);
        }

        public T Handle(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            return reader.Read() ? storage.Resolve(0, reader, map) : default(T);
        }

        public async Task<T> HandleAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            return await reader.ReadAsync(token).ConfigureAwait(false) 
                ? await storage.ResolveAsync(0, reader, map, token).ConfigureAwait(false) : default(T);
        }
    }
}