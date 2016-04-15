using System;
using System.Collections.Generic;
using System.Data.Common;
using Baseline;
using Marten.Schema;
using Marten.Services;
using Marten.Util;
using Npgsql;

namespace Marten.Linq.QueryHandlers
{
    public class LoadByIdArrayHandler<T, TKey> : IQueryHandler<IList<T>>
    {
        private readonly IResolver<T> _resolver;
        private readonly IDocumentMapping _mapping;
        private readonly TKey[] _ids;

        public LoadByIdArrayHandler(IResolver<T> resolver, IDocumentMapping mapping, TKey[] ids)
        {
            _resolver = resolver;
            _mapping = mapping;
            _ids = ids;
        }

        public Type SourceType => typeof(T);


        public void ConfigureCommand(NpgsqlCommand command)
        {
            var parameter = command.AddParameter(_ids);
            var sql =
                $"select {_mapping.SelectFields().Join(", ")} from {_mapping.Table.QualifiedName} as d where id = ANY(:{parameter.ParameterName})";

            command.AppendQuery(sql);
        }

        public IList<T> Handle(DbDataReader reader, IIdentityMap map)
        {
            var list = new List<T>();

            while (reader.Read())
            {
                list.Add(_resolver.Resolve(0, reader, map));
            }

            return list;
        }
    }
}