using System;
using System.Collections.Generic;
using System.Data.Common;
using Baseline;
using Marten.Schema;
using Marten.Services;
using Marten.Util;
using Npgsql;

namespace Marten.Linq.Results
{
    public class UserSuppliedQueryHandler<T> : IQueryHandler<IList<T>>
    {
        private readonly ISerializer _serializer;
        private readonly string _sql;
        private readonly object[] _parameters;

        public UserSuppliedQueryHandler(ISerializer serializer, string sql, object[] parameters)
        {
            _serializer = serializer;
            _sql = sql;
            _parameters = parameters;
        }

        public Type SourceType => typeof(T);

        public void ConfigureCommand(IDocumentSchema schema, NpgsqlCommand command)
        {
            var sql = _sql;
            if (!sql.Contains("select", StringComparison.OrdinalIgnoreCase))
            {
                var mapping = schema.MappingFor(typeof(T));
                var tableName = mapping.QualifiedTableName;
                sql = "select data from {0} {1}".ToFormat(tableName, sql);
            }

            _parameters.Each(x =>
            {
                var param = command.AddParameter(x);
                sql = sql.UseParameter(param);
            });

            command.AppendQuery(sql);
        }

        public IList<T> Handle(DbDataReader reader, IIdentityMap map)
        {
            var selector = new DeserializeSelector<T>(_serializer);
            return selector.Read(reader, map);
        }
    }
}