using System;
using System.Data.Common;
using Baseline;
using Marten.Schema;
using Marten.Services;
using Marten.Util;
using Npgsql;

namespace Marten.Linq.QueryHandlers
{
    public class LoadByIdHandler<T> : IQueryHandler<T>
    {
        private readonly IResolver<T> _resolver;
        private readonly IDocumentMapping _mapping;
        private readonly object _id;

        public LoadByIdHandler(IResolver<T> resolver, IDocumentMapping mapping, object id)
        {
            _resolver = resolver;
            _mapping = mapping;
            _id = id;
        }

        public Type SourceType => typeof (T);

        public void ConfigureCommand(NpgsqlCommand command)
        {
            var parameter = command.AddParameter(_id);
            var sql =
                $"select {_mapping.SelectFields().Join(", ")} from {_mapping.Table.QualifiedName} as d where id = :{parameter.ParameterName}";

            command.AppendQuery(sql);
        }

        public T Handle(DbDataReader reader, IIdentityMap map)
        {
            return reader.Read() ? _resolver.Resolve(0, reader, map) : default(T);
        }
    }
}